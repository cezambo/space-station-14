namespace Cognition.Core.Config;

public enum ReplayMode
{
    Live,
    Record,
    Replay,
    ReplayStrict,
}

public enum FanoutMode
{
    Full,
    Reduced,
}

public sealed record JevProviderConfig(
    string BaseUrl,
    string Model,
    string ApiKeyEnv,
    int TimeoutMs,
    int MaxRetries,
    double MaxRps,
    int BackoffInitialMs,
    int BackoffMaxMs,
    double BackoffJitter,
    decimal InputPriceUsdPerMtok,
    decimal OutputPriceUsdPerMtok);

public sealed record LlmProviderConfig(
    string BaseUrl,
    string Model,
    string ReasoningEffort,
    string ApiKeyEnv,
    int TimeoutMs,
    double MaxRps);

public sealed record ProvidersConfig(JevProviderConfig Jev, LlmProviderConfig Light, LlmProviderConfig Heavy);

public sealed record ReplayConfig(ReplayMode Mode, string Dir);

public sealed record LiveGuardConfig(decimal MaxCostUsdPerRun);

public sealed record DecisionConfig(
    double MinIntervalS,
    double MaxIntervalS,
    double IdleMaxIntervalS,
    int EmotionEveryN,
    FanoutMode Fanout);

public sealed record SchedulerConfig(double WUrgency, double WWait, double WVisible);

/// <summary>
/// Per-question thresholds (RJ-18). Probabilities apply to Noul P(yes) or Choice confidence;
/// score levels are ordinal Score cut-offs and must never be used in arithmetic (RJ-19).
/// </summary>
public sealed record ThresholdsConfig(
    IReadOnlyDictionary<string, double> Probabilities,
    IReadOnlyDictionary<string, int> ScoreLevels);

public sealed record ContextConfig(int TargetTokens, int HardCapTokens, double CharsPerTokenInitial);

public sealed record PerceptionConfig(
    int MaxEntities,
    int MaxItems,
    int MaxEnv,
    int MaxSounds,
    double BandWithinReach,
    double BandNear,
    double BandMedium,
    double HearingWhisper,
    double HearingNormal,
    double HearingShout,
    double WallAttenuation);

public sealed record SpeechConfig(double MinIntervalS, double StaleAfterS, int MaxSentences);

public sealed record BudgetConfig(int DailyUnits, int LightCost, int DeepCost);

public sealed record EmotionConfig(
    IReadOnlyList<string> Labels,
    double Beta,
    double Alpha,
    string Decay,
    double SecondaryMinP,
    int MaxModifiers,
    int MaxDurationDays,
    IReadOnlyDictionary<string, double> IntensityMap,
    IReadOnlyDictionary<string, int> DurationMapDays);

public sealed record OpinionConfig(
    int StubbornnessDefault,
    IReadOnlyDictionary<string, int> StubbornnessByTag,
    string StubbornnessCap,
    int BufferDecayDays,
    IReadOnlyList<int> SynergyIncrement,
    int StubbornnessDecayDays,
    int MaxInContext,
    int MaxRegenerations,
    string TemporalRegex);

public sealed record SleepConfig(
    double TWakeMinutes,
    double FullSleepMinutes,
    double MinConsolidatedSeconds,
    double MinFatigueForDayEnd,
    double BedRecoveryMultiplier);

public sealed record MemoryConfig(
    int RecentHardCap,
    double AggregateWindowS,
    int DailyBufferMin,
    int DailyCompactAt,
    int DailyCompactCount,
    int DailyWordsMin,
    int DailyWordsMax);

public sealed record ConsolidationConfig(int StepMaxRetries);

public sealed record PersistenceConfig(string SqlitePath);

public sealed record TelemetryConfig(string Dir);

/// <summary>Typed view of <c>Cognition/cognition.toml</c> (RM-04). Secrets are never stored here (RM-05).</summary>
public sealed record CognitionConfig(
    string BaseDirectory,
    ProvidersConfig Providers,
    ReplayConfig Replay,
    LiveGuardConfig LiveGuard,
    DecisionConfig Decision,
    SchedulerConfig Scheduler,
    ThresholdsConfig Thresholds,
    ContextConfig Context,
    PerceptionConfig Perception,
    SpeechConfig Speech,
    BudgetConfig Budget,
    EmotionConfig Emotion,
    OpinionConfig Opinion,
    SleepConfig Sleep,
    MemoryConfig Memory,
    ConsolidationConfig Consolidation,
    PersistenceConfig Persistence,
    TelemetryConfig Telemetry)
{
    /// <summary>Resolves a path from the config file relative to the directory that holds it.</summary>
    public string ResolvePath(string relativeOrAbsolute)
    {
        return Path.GetFullPath(Path.Combine(BaseDirectory, relativeOrAbsolute));
    }
}
