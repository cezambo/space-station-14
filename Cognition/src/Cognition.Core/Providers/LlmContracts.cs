namespace Cognition.Core.Providers;

public enum LlmRole
{
    Light,
    Heavy,
}

/// <summary>
/// One chat completion. <see cref="JsonSchema"/> non-null means the reply must be a JSON document matching it
/// (the <c>## SCHEMA</c> section of an LLM template); null means plain text (RL-02).
/// <see cref="MaxOutputTokens"/> excludes reasoning; the provider's <c>reasoning_allowance_tokens</c> is added.
/// </summary>
public sealed record LlmRequest(
    LlmRole Role,
    string SystemPrompt,
    string UserPrompt,
    string? JsonSchema,
    int MaxOutputTokens,
    string PurposeTag);

/// <summary>
/// <see cref="Text"/> is the reply content; for schema requests it is the validated JSON without code fences.
/// <see cref="UsageInfo.OutputTokens"/> includes reasoning tokens, which are billed as output.
/// </summary>
public sealed record LlmResponse(string Text, UsageInfo Usage, string ModelId)
{
    public string RequestId { get; init; } = string.Empty;
    public TimeSpan Latency { get; init; }

    /// <summary>HTTP attempts across all rounds, including the schema repair round.</summary>
    public int Attempts { get; init; } = 1;

    /// <summary>1 when the first reply failed schema validation and the repair round succeeded.</summary>
    public int SchemaRepairs { get; init; }

    public int ReasoningTokens { get; init; }

    /// <summary>False when the endpoint did not report <c>usage.cost</c> and USD was computed from configured prices.</summary>
    public bool CostReported { get; init; }
}

public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}
