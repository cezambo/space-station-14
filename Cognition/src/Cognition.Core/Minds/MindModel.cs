using System.Text.Json.Serialization;

namespace Cognition.Core.Minds;

/// <summary>
/// A character's mind (requirements §6), without its memories: those live in their own table so a commit can
/// persist new memory before deleting old (RMe-04). <see cref="MindSnapshot"/> joins both for export.
/// <see cref="Version"/> is the optimistic-concurrency counter (RS-09); the store owns it.
/// </summary>
public sealed record Mind(
    string Id,
    Guid StableGuid,
    Ss14Profile Ss14Profile,
    Personality Personality,
    Goals Goals,
    Opinions Opinions,
    EmotionState Emotion,
    ThinkingBudget ThinkingBudget,
    SleepState Sleep,
    ControlState Control,
    IReadOnlyDictionary<string, Acquaintance> Acquaintances,
    long Version);

/// <summary>RD-02: imported from the SS14 character profile at creation.</summary>
public sealed record Ss14Profile(string Name, string Species, int Age, string Job, string FlavorText);

/// <summary>RD-01. Big Five traits are 0-1.</summary>
public sealed record Personality(
    BigFive Traits,
    IReadOnlyList<string> Tags,
    int BaseStubbornness,
    IReadOnlyList<Preference> Likes,
    IReadOnlyList<Preference> Dislikes);

public sealed record BigFive(double Openness, double Conscientiousness, double Extraversion, double Agreeableness,
    double Neuroticism);

public enum Strength
{
    Mild,
    Moderate,
    Strong,
}

public sealed record Preference(string Text, Strength Strength);

public enum Horizon
{
    Immediate,
    Medium,
    Long,
}

public enum GoalStatus
{
    Active,
    Done,
    Dropped,
}

public enum Priority
{
    High,
    Medium,
    Low,
}

/// <summary>RD-05. <see cref="SuccessCheck"/> is an observable condition (RG-07).</summary>
public sealed record Goal(
    string Id,
    string Text,
    Horizon Horizon,
    GoalStatus Status,
    Priority Priority,
    string? SuccessCheck,
    int CreatedDay);

public sealed record Goals(IReadOnlyList<Goal> Immediate, IReadOnlyList<Goal> Medium, IReadOnlyList<Goal> Long)
{
    public static readonly Goals None = new([], [], []);

    [JsonIgnore]
    public IEnumerable<Goal> All => Immediate.Concat(Medium).Concat(Long);
}

public enum OpinionKind
{
    Social,
    General,
}

public enum Valence
{
    Positive,
    Negative,
    Mixed,
}

public sealed record Impression(string Text, int Day);

/// <summary>
/// §14.1. <see cref="Target"/> is a character id or <c>concept:&lt;name&gt;</c>. <see cref="Tags"/> relate general
/// opinions to impressions (§14.2 step 2). Stubbornness is fractional because E-01 tests +0.5 increments.
/// </summary>
public sealed record Opinion(
    string Target,
    OpinionKind Kind,
    string NuanceDescription,
    Valence Valence,
    IReadOnlyList<string> Tags,
    IReadOnlyList<Impression> DissonanceBuffer,
    double StubbornnessBase,
    double Stubbornness,
    int CreatedDay,
    int? LastRuptureDay);

public sealed record Opinions(IReadOnlyList<Opinion> General, IReadOnlyList<Opinion> Social)
{
    public static readonly Opinions None = new([], []);
}

/// <summary>RE-03: I(d) = <see cref="Intensity"/> · max(0, 1 − (d − <see cref="StartDay"/>) / <see cref="DurationDays"/>).</summary>
public sealed record EmotionModifier(string Emotion, double Intensity, string Reason, double StartDay, double DurationDays);

public sealed record EmotionState(
    IReadOnlyDictionary<string, double> LastDistribution,
    IReadOnlyList<EmotionModifier> Modifiers,
    int DecisionsSinceLastCheck);

/// <summary>RG-03.</summary>
public sealed record ThinkingBudget(int DailyUnits, int Remaining);

/// <summary>§8. Fatigue is 0-100.</summary>
public sealed record SleepState(int PersonalDay, double AwakeSeconds, double Fatigue);

public enum ControlMode
{
    Ai,
    Player,
}

public sealed record ControlState(ControlMode Mode);

/// <summary>RD-03: keyed by the other character's <see cref="Mind.Id"/>.</summary>
public sealed record Acquaintance(string KnownName, int FirstMetDay);

public enum MemoryLevel
{
    Recent,
    Daily,
    Fortnightly,
}

public enum MemorySource
{
    Event,
    OwnSpeech,
    Thought,
    Summary,
}

/// <summary>
/// One memory at any level (§13). <see cref="Id"/> is assigned by the store (0 before insert).
/// Recent: <see cref="Importance"/> 1-5 and <see cref="AtSeconds"/> (awake seconds). Daily: <see cref="ShortText"/>.
/// Fortnightly: <see cref="Day"/>..<see cref="ToDay"/>.
/// </summary>
public sealed record MemoryEntry(
    long Id,
    MemoryLevel Level,
    MemorySource Source,
    int Day,
    int? ToDay,
    double? AtSeconds,
    string Text,
    string? ShortText,
    int? Importance);

/// <summary>§6 layout: the mind plus its memories grouped by level.</summary>
public sealed record MindSnapshot(Mind Mind, IReadOnlyList<MemoryEntry> Memories);
