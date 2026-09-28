namespace Cognition.Core.Providers;

/// <summary>A Jev question. The map key naming it is never seen by the model (RJ-16).</summary>
public abstract record JevQuestion(string Instructions);

/// <summary>Option key to description; at most 255 options. An empty description is sent as null.</summary>
public sealed record ChoiceQuestion(string Instructions, IReadOnlyDictionary<string, string> Criteria)
    : JevQuestion(Instructions);

/// <summary>Ordered level descriptions, low to high, 2 to 10 levels.</summary>
public sealed record ScoreQuestion(string Instructions, IReadOnlyList<string> Levels)
    : JevQuestion(Instructions);

/// <summary>Yes/no question. <see cref="WhenTrue"/> and <see cref="WhenFalse"/> go together or not at all.</summary>
public sealed record NoulQuestion(string Instructions, string? WhenTrue = null, string? WhenFalse = null)
    : JevQuestion(Instructions);

public sealed record JevRequest(
    string Model,
    string State,
    IReadOnlyDictionary<string, JevQuestion> Questions,
    string PurposeTag);

public abstract record JevAnswer;

public sealed record ChoiceAnswer(string Choice, double Confidence, IReadOnlyDictionary<string, double> Probabilities)
    : JevAnswer;

/// <summary>
/// <see cref="Score"/> is probability-weighted and may fall between levels: use it only as an
/// ordinal band or threshold (RJ-19). <see cref="Legend"/> is the description of the most probable level.
/// <see cref="Probabilities"/> is indexed by level, 0 = lowest.
/// </summary>
public sealed record ScoreAnswer(double Score, string Legend, double Confidence, IReadOnlyList<double> Probabilities)
    : JevAnswer;

/// <summary>P(yes). Values near 0.5 mean uncertainty, not medium intensity.</summary>
public sealed record NoulAnswer(double PYes) : JevAnswer;

public sealed record UsageInfo(int InputTokens, int OutputTokens, decimal CostUsd);

/// <summary>
/// <see cref="Latency"/> covers the successful attempt only. <see cref="Model"/> is the version that
/// answered, as reported by the API.
/// </summary>
public sealed record JevResponse(
    string RequestId,
    TimeSpan Latency,
    IReadOnlyDictionary<string, JevAnswer> Answers,
    UsageInfo Usage)
{
    public string Model { get; init; } = string.Empty;
    public int Attempts { get; init; } = 1;
}

public interface IJevClient
{
    Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct);
}
