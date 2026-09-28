using Tomlyn;
using Tomlyn.Model;

namespace Cognition.Core.Config;

/// <summary>Loads and validates <c>cognition.toml</c> (RM-04, RM-05).</summary>
public static class CognitionConfigLoader
{
    /// <summary>Overrides <c>[replay].mode</c>; test runs set it to <c>replay-strict</c> (RDev-04).</summary>
    public const string ReplayModeEnvVar = "COGNITION_REPLAY_MODE";

    public static readonly IReadOnlyDictionary<string, ReplayMode> ReplayModes = new Dictionary<string, ReplayMode>
    {
        ["live"] = ReplayMode.Live,
        ["record"] = ReplayMode.Record,
        ["replay"] = ReplayMode.Replay,
        ["replay-strict"] = ReplayMode.ReplayStrict,
    };

    private static readonly IReadOnlyDictionary<string, FanoutMode> FanoutModes = new Dictionary<string, FanoutMode>
    {
        ["full"] = FanoutMode.Full,
        ["reduced"] = FanoutMode.Reduced,
    };

    private static readonly IReadOnlyDictionary<string, StructuredOutputMode> StructuredOutputModes =
        new Dictionary<string, StructuredOutputMode>
        {
            ["json_schema"] = StructuredOutputMode.JsonSchema,
            ["json_object"] = StructuredOutputMode.JsonObject,
            ["none"] = StructuredOutputMode.None,
        };

    public static CognitionConfig LoadFile(string path, Func<string, string?>? getEnv = null)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new ConfigException($"config file not found: {fullPath}");

        return Parse(File.ReadAllText(fullPath), System.IO.Path.GetDirectoryName(fullPath)!, getEnv);
    }

    public static CognitionConfig Parse(string toml, string baseDirectory, Func<string, string?>? getEnv = null)
    {
        getEnv ??= Environment.GetEnvironmentVariable;

        TomlTable model;
        try
        {
            model = TomlSerializer.Deserialize<TomlTable>(toml)
                ?? throw new ConfigException("config file is empty");
        }
        catch (ConfigException)
        {
            throw;
        }
        catch (Exception e)
        {
            throw new ConfigException($"TOML syntax error: {e.Message}");
        }

        var errors = new List<string>();
        var root = new TomlSection(model, string.Empty, errors);
        var config = Read(root, baseDirectory);
        root.ReportUnknownKeys();

        var modeOverride = getEnv(ReplayModeEnvVar);
        if (!string.IsNullOrWhiteSpace(modeOverride))
        {
            if (ReplayModes.TryGetValue(modeOverride.Trim(), out var mode))
                config = config with { Replay = config.Replay with { Mode = mode } };
            else
                errors.Add($"{ReplayModeEnvVar}: '{modeOverride}' is not one of: {string.Join(", ", ReplayModes.Keys)}");
        }

        if (errors.Count == 0)
            errors.AddRange(CognitionConfigValidator.Validate(config));

        if (errors.Count > 0)
            throw new ConfigException(errors);

        return config;
    }

    private static CognitionConfig Read(TomlSection root, string baseDirectory)
    {
        var providers = root.Table("providers");
        var jev = providers.Table("jev");
        var llm = providers.Table("llm");

        var replay = root.Table("replay");
        var liveGuard = root.Table("live_guard");
        var decision = root.Table("decision");
        var scheduler = root.Table("scheduler");
        var context = root.Table("context");
        var perception = root.Table("perception");
        var speech = root.Table("speech");
        var budget = root.Table("budget");
        var emotion = root.Table("emotion");
        var opinion = root.Table("opinion");
        var sleep = root.Table("sleep");
        var memory = root.Table("memory");

        return new CognitionConfig(
            BaseDirectory: baseDirectory,
            Providers: new ProvidersConfig(
                Jev: new JevProviderConfig(
                    BaseUrl: jev.String("base_url"),
                    Model: jev.String("model"),
                    ApiKeyEnv: jev.String("api_key_env"),
                    TimeoutMs: jev.Int("timeout_ms"),
                    MaxRetries: jev.Int("max_retries"),
                    MaxRps: jev.Double("max_rps"),
                    BackoffInitialMs: jev.Int("backoff_initial_ms"),
                    BackoffMaxMs: jev.Int("backoff_max_ms"),
                    BackoffJitter: jev.Double("backoff_jitter"),
                    InputPriceUsdPerMtok: jev.Decimal("input_price_usd_per_mtok"),
                    OutputPriceUsdPerMtok: jev.Decimal("output_price_usd_per_mtok")),
                Light: ReadLlm(llm.Table("light")),
                Heavy: ReadLlm(llm.Table("heavy"))),
            Replay: new ReplayConfig(
                Mode: replay.Enum("mode", ReplayModes),
                Dir: replay.String("dir")),
            LiveGuard: new LiveGuardConfig(liveGuard.Decimal("max_cost_usd_per_run")),
            Decision: new DecisionConfig(
                MinIntervalS: decision.Double("min_interval_s"),
                MaxIntervalS: decision.Double("max_interval_s"),
                IdleMaxIntervalS: decision.Double("idle_max_interval_s"),
                EmotionEveryN: decision.Int("emotion_every_n"),
                Fanout: decision.Enum("fanout", FanoutModes),
                ShortlistTop: decision.Int("shortlist_top"),
                ShortlistBatch: decision.Int("shortlist_batch")),
            Scheduler: new SchedulerConfig(
                WUrgency: scheduler.Double("w_urgency"),
                WWait: scheduler.Double("w_wait"),
                WVisible: scheduler.Double("w_visible")),
            Thresholds: ReadThresholds(root.Table("thresholds")),
            Context: new ContextConfig(
                TargetTokens: context.Int("target_tokens"),
                HardCapTokens: context.Int("hard_cap_tokens"),
                CharsPerTokenInitial: context.Double("chars_per_token_initial"),
                CalibrationRate: context.Double("calibration_rate"),
                BlockTargets: context.IntMap("block_targets")),
            Perception: new PerceptionConfig(
                MaxEntities: perception.Int("max_entities"),
                MaxItems: perception.Int("max_items"),
                MaxEnv: perception.Int("max_env"),
                MaxSounds: perception.Int("max_sounds"),
                BandWithinReach: perception.Double("band_within_reach"),
                BandNear: perception.Double("band_near"),
                BandMedium: perception.Double("band_medium"),
                HearingWhisper: perception.Double("hearing_whisper"),
                HearingNormal: perception.Double("hearing_normal"),
                HearingShout: perception.Double("hearing_shout"),
                WallAttenuation: perception.Double("wall_attenuation"),
                DamageBands: perception.DoubleList("damage_bands"),
                Salience: ReadSalience(perception.Table("salience")),
                Environment: ReadEnvironment(perception.Table("environment"))),
            Needs: ReadNeeds(root.Table("needs")),
            Speech: new SpeechConfig(
                MinIntervalS: speech.Double("min_interval_s"),
                StaleAfterS: speech.Double("stale_after_s"),
                MaxSentences: speech.Int("max_sentences")),
            Budget: new BudgetConfig(
                DailyUnits: budget.Int("daily_units"),
                LightCost: budget.Int("light_cost"),
                DeepCost: budget.Int("deep_cost"),
                BandBounds: budget.DoubleList("band_bounds")),
            Emotion: new EmotionConfig(
                Labels: emotion.StringList("labels"),
                Beta: emotion.Double("beta"),
                Alpha: emotion.Double("alpha"),
                Decay: emotion.String("decay"),
                SecondaryMinP: emotion.Double("secondary_min_p"),
                MaxModifiers: emotion.Int("max_modifiers"),
                MaxDurationDays: emotion.Int("max_duration_days"),
                IntensityMap: emotion.DoubleMap("intensity_map"),
                DurationMapDays: emotion.IntMap("duration_map_days")),
            Opinion: new OpinionConfig(
                StubbornnessDefault: opinion.Int("stubbornness_default"),
                StubbornnessByTag: opinion.IntMap("stubbornness_by_tag"),
                StubbornnessCap: opinion.String("stubbornness_cap"),
                BufferDecayDays: opinion.Int("buffer_decay_days"),
                SynergyIncrement: opinion.IntList("synergy_increment"),
                StubbornnessDecayDays: opinion.Int("stubbornness_decay_days"),
                MaxInContext: opinion.Int("max_in_context"),
                MaxRegenerations: opinion.Int("max_regenerations"),
                TemporalRegex: opinion.String("temporal_regex")),
            Sleep: new SleepConfig(
                TWakeMinutes: sleep.Double("t_wake_minutes"),
                FullSleepMinutes: sleep.Double("full_sleep_minutes"),
                MinConsolidatedSeconds: sleep.Double("min_consolidated_seconds"),
                MinFatigueForDayEnd: sleep.Double("min_fatigue_for_day_end"),
                BedRecoveryMultiplier: sleep.Double("bed_recovery_multiplier"),
                DayPhaseBounds: sleep.DoubleList("day_phase_bounds")),
            Memory: new MemoryConfig(
                RecentHardCap: memory.Int("recent_hard_cap"),
                AggregateWindowS: memory.Double("aggregate_window_s"),
                DailyBufferMin: memory.Int("daily_buffer_min"),
                DailyCompactAt: memory.Int("daily_compact_at"),
                DailyCompactCount: memory.Int("daily_compact_count"),
                DailyWordsMin: memory.Int("daily_words_min"),
                DailyWordsMax: memory.Int("daily_words_max")),
            Consolidation: new ConsolidationConfig(root.Table("consolidation").Int("step_max_retries")),
            Persistence: new PersistenceConfig(root.Table("persistence").String("sqlite_path")),
            Telemetry: new TelemetryConfig(root.Table("telemetry").String("dir")));
    }

    private static SalienceConfig ReadSalience(TomlSection s) => new(
        Proximity: s.Double("proximity"),
        Novelty: s.Double("novelty"),
        Goal: s.Double("goal"),
        Danger: s.Double("danger"),
        DangerTraits: s.StringList("danger_traits"));

    private static NeedsConfig ReadNeeds(TomlSection s) => new(
        OxygenFullSeverityDrop: s.Double("oxygen_full_severity_drop"),
        BodyTempNormalK: s.Double("body_temp_normal_k"),
        BodyTempFullSeverityK: s.Double("body_temp_full_severity_k"),
        Bands: s.DoubleListMap("bands"));

    private static EnvironmentConfig ReadEnvironment(TomlSection s)
    {
        return new EnvironmentConfig(
            PressureHazardLowKpa: s.Double("pressure_hazard_low_kpa"),
            PressureWarningLowKpa: s.Double("pressure_warning_low_kpa"),
            PressureWarningHighKpa: s.Double("pressure_warning_high_kpa"),
            PressureHazardHighKpa: s.Double("pressure_hazard_high_kpa"),
            TempFreezingK: s.Double("temp_freezing_k"),
            TempColdK: s.Double("temp_cold_k"),
            TempHotK: s.Double("temp_hot_k"),
            TempScorchingK: s.Double("temp_scorching_k"),
            GasNoticeKpa: s.Double("gas_notice_kpa"));
    }

    private static LlmProviderConfig ReadLlm(TomlSection s)
    {
        return new LlmProviderConfig(
            BaseUrl: s.String("base_url"),
            Model: s.String("model"),
            ReasoningEffort: s.String("reasoning_effort"),
            ApiKeyEnv: s.String("api_key_env"),
            TimeoutMs: s.Int("timeout_ms"),
            MaxRps: s.Double("max_rps"),
            MaxRetries: s.Int("max_retries"),
            BackoffInitialMs: s.Int("backoff_initial_ms"),
            BackoffMaxMs: s.Int("backoff_max_ms"),
            BackoffJitter: s.Double("backoff_jitter"),
            StructuredOutput: s.Enum("structured_output", StructuredOutputModes),
            RequireParameters: s.Bool("require_parameters"),
            ReasoningAllowanceTokens: s.Int("reasoning_allowance_tokens"),
            InputPriceUsdPerMtok: s.Decimal("input_price_usd_per_mtok"),
            OutputPriceUsdPerMtok: s.Decimal("output_price_usd_per_mtok"));
    }

    /// <summary>Integer values are ordinal Score levels; floats are probabilities (RJ-18, RJ-19).</summary>
    private static ThresholdsConfig ReadThresholds(TomlSection s)
    {
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        var levels = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in s.AllEntries())
        {
            switch (value)
            {
                case double d:
                    probabilities[key] = d;
                    break;
                case long l and >= 0 and <= 9:
                    levels[key] = (int)l;
                    break;
                default:
                    s.Errors.Add($"{s.Path}.{key}: expected a probability (float) or a Score level index (integer 0-9)");
                    break;
            }
        }

        return new ThresholdsConfig(probabilities, levels);
    }
}
