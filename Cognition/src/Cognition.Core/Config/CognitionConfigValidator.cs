using System.Text.RegularExpressions;

namespace Cognition.Core.Config;

/// <summary>Semantic checks that the TOML types alone cannot express.</summary>
public static partial class CognitionConfigValidator
{
    /// <summary>RC-01: Jev stays at or below 50% of the 1,200 requests/min account limit.</summary>
    public const double JevMaxRpsCeiling = 10.0;

    /// <summary>RM-07: at most three retries per call.</summary>
    public const int JevMaxRetriesCeiling = 3;

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

        Positive("speech.min_interval_s", c.Speech.MinIntervalS, e);
        Positive("speech.stale_after_s", c.Speech.StaleAfterS, e);
        Positive("speech.max_sentences", c.Speech.MaxSentences, e);

        Positive("budget.daily_units", c.Budget.DailyUnits, e);
        Positive("budget.light_cost", c.Budget.LightCost, e);
        StrictlyOrdered(e, ("budget.light_cost", c.Budget.LightCost), ("budget.deep_cost", c.Budget.DeepCost));

        ValidateEmotion(c.Emotion, e);
        ValidateOpinion(c.Opinion, e);

        var s = c.Sleep;
        Positive("sleep.t_wake_minutes", s.TWakeMinutes, e);
        Positive("sleep.full_sleep_minutes", s.FullSleepMinutes, e);
        Positive("sleep.min_consolidated_seconds", s.MinConsolidatedSeconds, e);
        NonNegative("sleep.min_fatigue_for_day_end", s.MinFatigueForDayEnd, e);
        Positive("sleep.bed_recovery_multiplier", s.BedRecoveryMultiplier, e);

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
        NotEmpty($"{p}.reasoning_effort", l.ReasoningEffort, e);
        EnvVarName($"{p}.api_key_env", l.ApiKeyEnv, e);
        Positive($"{p}.timeout_ms", l.TimeoutMs, e);
        Positive($"{p}.max_rps", l.MaxRps, e);
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
