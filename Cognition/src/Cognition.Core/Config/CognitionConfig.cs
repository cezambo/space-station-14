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

/// <summary>How the JSON Schema of a request is passed to the endpoint. The reply is validated locally in every mode.</summary>
public enum StructuredOutputMode
{
    JsonSchema,
    JsonObject,
    None,
}

/// <summary>
/// An OpenAI-compatible chat completions endpoint (RM-03, RM-04). An empty <see cref="ReasoningEffort"/> is
/// not sent; an empty <see cref="ApiKeyEnv"/> means no auth (local servers).
/// </summary>
public sealed record LlmProviderConfig(
    string BaseUrl,
    string Model,
    string ReasoningEffort,
    string ApiKeyEnv,
    int TimeoutMs,
    double MaxRps,
    int MaxRetries,
    int BackoffInitialMs,
    int BackoffMaxMs,
    double BackoffJitter,
    StructuredOutputMode StructuredOutput,
    bool RequireParameters,
    int ReasoningAllowanceTokens,
    decimal InputPriceUsdPerMtok,
    decimal OutputPriceUsdPerMtok);

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

public sealed record ContextConfig(
    int TargetTokens,
    int HardCapTokens,
    double CharsPerTokenInitial,
    double CalibrationRate,
    IReadOnlyDictionary<string, int> BlockTargets);

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
    double WallAttenuation,
    IReadOnlyList<double> DamageBands,
    SalienceConfig Salience,
    EnvironmentConfig Environment);

/// <summary>T1.12 salience = proximity/(1+d) + novelty + goal keyword match + danger, each weighted.</summary>
public sealed record SalienceConfig(double Proximity, double Novelty, double Goal, double Danger,
    IReadOnlyList<string> DangerTraits);

/// <summary>RP-06 sensation thresholds. Hazard values are SS14's (Atmospherics.cs, TemperatureDamageComponent).</summary>
public sealed record EnvironmentConfig(
    double PressureHazardLowKpa,
    double PressureWarningLowKpa,
    double PressureWarningHighKpa,
    double PressureHazardHighKpa,
    double TempFreezingK,
    double TempColdK,
    double TempHotK,
    double TempScorchingK,
    double GasNoticeKpa);

/// <summary>RP-08: per need, the values (0-100 severity) where <c>mild</c>, <c>strong</c> and <c>critical</c> start.</summary>
public sealed record NeedsConfig(
    double OxygenFullSeverityDrop,
    double BodyTempNormalK,
    double BodyTempFullSeverityK,
    IReadOnlyDictionary<string, IReadOnlyList<double>> Bands);

public sealed record SpeechConfig(double MinIntervalS, double StaleAfterS, int MaxSentences);

/// <summary><see cref="BandBounds"/>: remaining/daily fractions where <c>some</c> and <c>plenty</c> start (RJ-09).</summary>
public sealed record BudgetConfig(int DailyUnits, int LightCost, int DeepCost, IReadOnlyList<double> BandBounds);

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

/// <summary><see cref="DayPhaseBounds"/>: t_awake/T_wake fractions where <c>middle</c>, <c>late</c>, <c>overdue</c> start (RS-07).</summary>
public sealed record SleepConfig(
    double TWakeMinutes,
    double FullSleepMinutes,
    double MinConsolidatedSeconds,
    double MinFatigueForDayEnd,
    double BedRecoveryMultiplier,
    IReadOnlyList<double> DayPhaseBounds);

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
    NeedsConfig Needs,
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
