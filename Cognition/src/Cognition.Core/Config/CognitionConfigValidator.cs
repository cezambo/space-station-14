using System.Text.RegularExpressions;

namespace Cognition.Core.Config;

/// <summary>Semantic checks that the TOML types alone cannot express.</summary>
public static partial class CognitionConfigValidator
{
    /// <summary>RC-01: Jev stays at or below 50% of the 1,200 requests/min account limit.</summary>
    public const double JevMaxRpsCeiling = 10.0;

    /// <summary>RM-07: at most three retries per call.</summary>
    public const int JevMaxRetriesCeiling = 3;

    public const int LlmMaxRetriesCeiling = 3;

    /// <summary>OpenRouter chat completions <c>reasoning_effort</c> enum (docs/openai-compat-wire-format.md).</summary>
    private static readonly string[] ReasoningEfforts = ["none", "minimal", "low", "medium", "high", "xhigh", "max"];

    private static readonly string[] RequiredNeeds = ["hunger", "thirst", "fatigue", "body_temperature", "oxygen"];

    private static readonly string[] Decays = ["linear", "exponential"];
    private static readonly string[] StubbornnessCaps = ["none", "2x", "3x"];

    public static IReadOnlyList<string> Validate(CognitionConfig c)
    {
        var e = new List<string>();

        ValidateJev(c.Providers.Jev, e);
        ValidateLlm("providers.llm.light", c.Providers.Light, e);
        ValidateLlm("providers.llm.heavy", c.Providers.Heavy, e);

        NotEmpty("replay.dir", c.Replay.Dir, e);
        Positive("live_guard.max_cost_usd_per_run", (double)c.LiveGuard.MaxCostUsdPerRun, e);

        var d = c.Decision;
        Positive("decision.min_interval_s", d.MinIntervalS, e);
        Ordered(e, ("decision.min_interval_s", d.MinIntervalS), ("decision.max_interval_s", d.MaxIntervalS),
            ("decision.idle_max_interval_s", d.IdleMaxIntervalS));
        Positive("decision.emotion_every_n", d.EmotionEveryN, e);
        if (d.ShortlistTop is < 2 or > 255)
            e.Add($"decision.shortlist_top: {d.ShortlistTop} must be 2-255 (a Choice needs 2-255 options, RJ-04)");
        Positive("decision.shortlist_batch", d.ShortlistBatch, e);

        NonNegative("scheduler.w_urgency", c.Scheduler.WUrgency, e);
        NonNegative("scheduler.w_wait", c.Scheduler.WWait, e);
        NonNegative("scheduler.w_visible", c.Scheduler.WVisible, e);

        foreach (var (key, p) in c.Thresholds.Probabilities)
        {
            Probability($"thresholds.{key}", p, e);
        }

        Positive("context.target_tokens", c.Context.TargetTokens, e);
        Ordered(e, ("context.target_tokens", c.Context.TargetTokens), ("context.hard_cap_tokens", c.Context.HardCapTokens));
        Positive("context.chars_per_token_initial", c.Context.CharsPerTokenInitial, e);
        if (c.Context.CalibrationRate is <= 0 or > 1)
            e.Add($"context.calibration_rate: {c.Context.CalibrationRate} must be in (0, 1]");
        var blocks = Enum.GetNames<Decision.ContextBlock>().Select(Decision.ContextBlocks.Key).ToList();
        foreach (var missing in blocks.Where(b => !c.Context.BlockTargets.ContainsKey(b)))
        {
            e.Add($"context.block_targets.{missing}: required");
        }

        foreach (var (key, value) in c.Context.BlockTargets.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!blocks.Contains(key))
                e.Add($"context.block_targets.{key}: unknown block (known: {string.Join(", ", blocks)})");
            Positive($"context.block_targets.{key}", value, e);
        }

        var p2 = c.Perception;
        Positive("perception.max_entities", p2.MaxEntities, e);
        Positive("perception.max_items", p2.MaxItems, e);
        Positive("perception.max_env", p2.MaxEnv, e);
        Positive("perception.max_sounds", p2.MaxSounds, e);
        Positive("perception.band_within_reach", p2.BandWithinReach, e);
        StrictlyOrdered(e, ("perception.band_within_reach", p2.BandWithinReach), ("perception.band_near", p2.BandNear),
            ("perception.band_medium", p2.BandMedium));
        Positive("perception.hearing_whisper", p2.HearingWhisper, e);
        StrictlyOrdered(e, ("perception.hearing_whisper", p2.HearingWhisper), ("perception.hearing_normal", p2.HearingNormal),
            ("perception.hearing_shout", p2.HearingShout));
        Probability("perception.wall_attenuation", p2.WallAttenuation, e);
        var env = p2.Environment;
        const string pe = "perception.environment";
        Positive($"{pe}.pressure_hazard_low_kpa", env.PressureHazardLowKpa, e);
        StrictlyOrdered(e, ($"{pe}.pressure_hazard_low_kpa", env.PressureHazardLowKpa),
            ($"{pe}.pressure_warning_low_kpa", env.PressureWarningLowKpa),
            ($"{pe}.pressure_warning_high_kpa", env.PressureWarningHighKpa),
            ($"{pe}.pressure_hazard_high_kpa", env.PressureHazardHighKpa));
        Positive($"{pe}.temp_freezing_k", env.TempFreezingK, e);
        StrictlyOrdered(e, ($"{pe}.temp_freezing_k", env.TempFreezingK), ($"{pe}.temp_cold_k", env.TempColdK),
            ($"{pe}.temp_hot_k", env.TempHotK), ($"{pe}.temp_scorching_k", env.TempScorchingK));
        Positive($"{pe}.gas_notice_kpa", env.GasNoticeKpa, e);

        Bounds("perception.damage_bands", p2.DamageBands, 3, 0, double.MaxValue, e);
        var sal = p2.Salience;
        foreach (var (k, v) in new[] { ("proximity", sal.Proximity), ("novelty", sal.Novelty), ("goal", sal.Goal), ("danger", sal.Danger) })
        {
            if (v < 0 || double.IsNaN(v))
                e.Add($"perception.salience.{k}: must be ≥ 0");
        }

        Probability("needs.oxygen_full_severity_drop", c.Needs.OxygenFullSeverityDrop, e);
        Positive("needs.oxygen_full_severity_drop", c.Needs.OxygenFullSeverityDrop, e);
        Positive("needs.body_temp_normal_k", c.Needs.BodyTempNormalK, e);
        Positive("needs.body_temp_full_severity_k", c.Needs.BodyTempFullSeverityK, e);

        foreach (var need in RequiredNeeds.Where(n => !c.Needs.Bands.ContainsKey(n)))
        {
            e.Add($"needs.bands.{need}: missing (RP-08)");
        }

        foreach (var (need, bounds) in c.Needs.Bands)
        {
            Bounds($"needs.bands.{need}", bounds, 3, 0, 100, e);
        }

        Positive("speech.min_interval_s", c.Speech.MinIntervalS, e);
        Positive("speech.stale_after_s", c.Speech.StaleAfterS, e);
        Positive("speech.max_sentences", c.Speech.MaxSentences, e);

        Positive("budget.daily_units", c.Budget.DailyUnits, e);
        Positive("budget.light_cost", c.Budget.LightCost, e);
        StrictlyOrdered(e, ("budget.light_cost", c.Budget.LightCost), ("budget.deep_cost", c.Budget.DeepCost));
        Bounds("budget.band_bounds", c.Budget.BandBounds, 2, 0, 1, e);

        ValidateEmotion(c.Emotion, e);
        ValidateOpinion(c.Opinion, e);

        var s = c.Sleep;
        Positive("sleep.t_wake_minutes", s.TWakeMinutes, e);
        Positive("sleep.full_sleep_minutes", s.FullSleepMinutes, e);
        Positive("sleep.min_consolidated_seconds", s.MinConsolidatedSeconds, e);
        NonNegative("sleep.min_fatigue_for_day_end", s.MinFatigueForDayEnd, e);
        Positive("sleep.bed_recovery_multiplier", s.BedRecoveryMultiplier, e);
        Bounds("sleep.day_phase_bounds", s.DayPhaseBounds, 3, 0, double.MaxValue, e);

        var m = c.Memory;
        Positive("memory.recent_hard_cap", m.RecentHardCap, e);
        Positive("memory.aggregate_window_s", m.AggregateWindowS, e);
        Positive("memory.daily_buffer_min", m.DailyBufferMin, e);
        Ordered(e, ("memory.daily_buffer_min", m.DailyBufferMin), ("memory.daily_compact_at", m.DailyCompactAt));
        Positive("memory.daily_compact_count", m.DailyCompactCount, e);
        Ordered(e, ("memory.daily_compact_count", m.DailyCompactCount), ("memory.daily_compact_at", m.DailyCompactAt));
        Positive("memory.daily_words_min", m.DailyWordsMin, e);
        Ordered(e, ("memory.daily_words_min", m.DailyWordsMin), ("memory.daily_words_max", m.DailyWordsMax));

        NonNegative("consolidation.step_max_retries", c.Consolidation.StepMaxRetries, e);
        Positive("consolidation.commit_attempts", c.Consolidation.CommitAttempts, e);
        NotEmpty("persistence.sqlite_path", c.Persistence.SqlitePath, e);
        NotEmpty("telemetry.dir", c.Telemetry.Dir, e);

        return e;
    }

    private static void ValidateJev(JevProviderConfig j, List<string> e)
    {
        const string p = "providers.jev";
        AbsoluteHttpUrl($"{p}.base_url", j.BaseUrl, e);
        if (!PinnedJevModel().IsMatch(j.Model))
            e.Add($"{p}.model: '{j.Model}' must be a pinned version like 'jev-1.13.0', not an alias (RM-02)");
        EnvVarName($"{p}.api_key_env", j.ApiKeyEnv, e);
        Positive($"{p}.timeout_ms", j.TimeoutMs, e);
        if (j.MaxRetries is < 0 or > JevMaxRetriesCeiling)
            e.Add($"{p}.max_retries: must be between 0 and {JevMaxRetriesCeiling} (RM-07)");
        Positive($"{p}.max_rps", j.MaxRps, e);
        if (j.MaxRps > JevMaxRpsCeiling)
            e.Add($"{p}.max_rps: must be at most {JevMaxRpsCeiling} (RC-01)");
        Positive($"{p}.backoff_initial_ms", j.BackoffInitialMs, e);
        Ordered(e, ($"{p}.backoff_initial_ms", j.BackoffInitialMs), ($"{p}.backoff_max_ms", j.BackoffMaxMs));
        Probability($"{p}.backoff_jitter", j.BackoffJitter, e);
        Positive($"{p}.input_price_usd_per_mtok", (double)j.InputPriceUsdPerMtok, e);
        NonNegative($"{p}.output_price_usd_per_mtok", (double)j.OutputPriceUsdPerMtok, e);
    }

    private static void ValidateLlm(string p, LlmProviderConfig l, List<string> e)
    {
        AbsoluteHttpUrl($"{p}.base_url", l.BaseUrl, e);
        NotEmpty($"{p}.model", l.Model, e);
        if (l.ReasoningEffort.Length > 0 && !ReasoningEfforts.Contains(l.ReasoningEffort))
            e.Add($"{p}.reasoning_effort: '{l.ReasoningEffort}' is not empty or one of: {string.Join(", ", ReasoningEfforts)}");
        if (l.ApiKeyEnv.Length > 0)
            EnvVarName($"{p}.api_key_env", l.ApiKeyEnv, e);
        Positive($"{p}.timeout_ms", l.TimeoutMs, e);
        Positive($"{p}.max_rps", l.MaxRps, e);
        if (l.MaxRetries is < 0 or > LlmMaxRetriesCeiling)
            e.Add($"{p}.max_retries: must be between 0 and {LlmMaxRetriesCeiling}");
        Positive($"{p}.backoff_initial_ms", l.BackoffInitialMs, e);
        Ordered(e, ($"{p}.backoff_initial_ms", l.BackoffInitialMs), ($"{p}.backoff_max_ms", l.BackoffMaxMs));
        Probability($"{p}.backoff_jitter", l.BackoffJitter, e);
        NonNegative($"{p}.reasoning_allowance_tokens", l.ReasoningAllowanceTokens, e);
        NonNegative($"{p}.input_price_usd_per_mtok", (double)l.InputPriceUsdPerMtok, e);
        NonNegative($"{p}.output_price_usd_per_mtok", (double)l.OutputPriceUsdPerMtok, e);
    }

    private static void ValidateEmotion(EmotionConfig m, List<string> e)
    {
        if (m.Labels.Count == 0)
            e.Add("emotion.labels: must not be empty");
        if (m.Labels.Distinct(StringComparer.Ordinal).Count() != m.Labels.Count)
            e.Add("emotion.labels: must be unique");
        Positive("emotion.beta", m.Beta, e);
        Probability("emotion.alpha", m.Alpha, e);
        if (!Decays.Contains(m.Decay))
            e.Add($"emotion.decay: '{m.Decay}' is not one of: {string.Join(", ", Decays)}");
        Probability("emotion.secondary_min_p", m.SecondaryMinP, e);
        Positive("emotion.max_modifiers", m.MaxModifiers, e);
        Positive("emotion.max_duration_days", m.MaxDurationDays, e);
        if (m.IntensityMap.Count == 0)
            e.Add("emotion.intensity_map: must not be empty");
        foreach (var (k, v) in m.IntensityMap)
        {
            if (v is <= 0 or > 1)
                e.Add($"emotion.intensity_map.{k}: must be in (0, 1]");
        }

        if (m.DurationMapDays.Count == 0)
            e.Add("emotion.duration_map_days: must not be empty");
        foreach (var (k, v) in m.DurationMapDays)
        {
            if (v <= 0 || v > m.MaxDurationDays)
                e.Add($"emotion.duration_map_days.{k}: must be between 1 and emotion.max_duration_days");
        }
    }

    private static void ValidateOpinion(OpinionConfig o, List<string> e)
    {
        Positive("opinion.stubbornness_default", o.StubbornnessDefault, e);
        foreach (var (k, v) in o.StubbornnessByTag)
        {
            Positive($"opinion.stubbornness_by_tag.{k}", v, e);
        }

        if (!StubbornnessCaps.Contains(o.StubbornnessCap))
            e.Add($"opinion.stubbornness_cap: '{o.StubbornnessCap}' is not one of: {string.Join(", ", StubbornnessCaps)}");
        NonNegative("opinion.buffer_decay_days", o.BufferDecayDays, e);
        if (o.SynergyIncrement.Count != 2 || o.SynergyIncrement.Any(v => v <= 0))
            e.Add("opinion.synergy_increment: must be two positive integers [agrees, strongly_agrees]");
        NonNegative("opinion.stubbornness_decay_days", o.StubbornnessDecayDays, e);
        Positive("opinion.max_in_context", o.MaxInContext, e);
        NonNegative("opinion.max_regenerations", o.MaxRegenerations, e);
        try
        {
            _ = new Regex(o.TemporalRegex, RegexOptions.IgnoreCase);
        }
        catch (ArgumentException ex)
        {
            e.Add($"opinion.temporal_regex: invalid regex ({ex.Message})");
        }
    }

    /// <summary><paramref name="count"/> strictly increasing values in the open interval (min, max].</summary>
    private static void Bounds(string key, IReadOnlyList<double> values, int count, double min, double max, List<string> e)
    {
        if (values.Count != count)
        {
            e.Add($"{key}: must have exactly {count} values");
            return;
        }

        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] <= min || values[i] > max)
                e.Add($"{key}: values must be in ({min}, {max}]");
            if (i > 0 && values[i] <= values[i - 1])
                e.Add($"{key}: values must be strictly increasing");
        }
    }

    private static void NotEmpty(string key, string value, List<string> e)
    {
        if (string.IsNullOrWhiteSpace(value))
            e.Add($"{key}: must not be empty");
    }

    private static void Positive(string key, double value, List<string> e)
    {
        if (!(value > 0))
            e.Add($"{key}: must be > 0");
    }

    private static void NonNegative(string key, double value, List<string> e)
    {
        if (!(value >= 0))
            e.Add($"{key}: must be >= 0");
    }

    private static void Probability(string key, double value, List<string> e)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
            e.Add($"{key}: must be in [0, 1]");
    }

    private static void Ordered(List<string> e, params (string Key, double Value)[] items)
    {
        for (var i = 1; i < items.Length; i++)
        {
            if (items[i].Value < items[i - 1].Value)
                e.Add($"{items[i].Key}: must be >= {items[i - 1].Key}");
        }
    }

    private static void StrictlyOrdered(List<string> e, params (string Key, double Value)[] items)
    {
        for (var i = 1; i < items.Length; i++)
        {
            if (items[i].Value <= items[i - 1].Value)
                e.Add($"{items[i].Key}: must be > {items[i - 1].Key}");
        }
    }

    private static void AbsoluteHttpUrl(string key, string value, List<string> e)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            e.Add($"{key}: must be an absolute http(s) URL");
    }

    private static void EnvVarName(string key, string value, List<string> e)
    {
        if (!EnvVarPattern().IsMatch(value))
            e.Add($"{key}: must be an environment variable name like TYPESAFE_API_KEY (RM-05)");
    }

    [GeneratedRegex(@"^jev-\d+\.\d+\.\d+$")]
    private static partial Regex PinnedJevModel();

    [GeneratedRegex("^[A-Z_][A-Z0-9_]*$")]
    private static partial Regex EnvVarPattern();
}
