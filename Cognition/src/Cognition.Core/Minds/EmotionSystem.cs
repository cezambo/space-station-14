using Cognition.Core.Config;

namespace Cognition.Core.Minds;

/// <summary>
/// Emotional state (T1.20, RE-03). Linear decay is the formula in the requirements. Exponential decay is
/// <c>I0 · exp(-(d − d0) / τ)</c> until the same expiry <c>d0 + τ</c>, where it becomes 0 (Q-11).
/// </summary>
public static class EmotionSystem
{
    public static double IntensityAt(EmotionModifier modifier, double day, string decay)
    {
        if (modifier.DurationDays <= 0 || day < modifier.StartDay || day >= modifier.StartDay + modifier.DurationDays)
            return 0;
        var elapsed = (day - modifier.StartDay) / modifier.DurationDays;
        var curve = decay == "exponential" ? Math.Exp(-elapsed) : 1 - elapsed;
        return Math.Clamp(modifier.Intensity * curve, 0, 1);
    }

    /// <summary>
    /// The distribution after modifiers and inertia. <paramref name="observed"/> is the Choice distribution from
    /// the emotion question; <paramref name="previous"/> is the last stored distribution.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Distribution(
        IReadOnlyDictionary<string, double> observed,
        IReadOnlyList<EmotionModifier> modifiers,
        IReadOnlyDictionary<string, double> previous,
        double day,
        EmotionConfig config)
    {
        var labels = config.Labels;
        var observedNorm = Normalize(labels.Select(l => (l, observed.GetValueOrDefault(l))));
        var boosted = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var label in labels)
        {
            var pull = modifiers.Where(m => m.Emotion == label).Sum(m => IntensityAt(m, day, config.Decay));
            boosted[label] = observedNorm[label] * Math.Exp(config.Beta * pull);
        }

        var prime = Normalize(boosted.Select(kv => (kv.Key, kv.Value)));
        var blended = labels.ToDictionary(
            l => l,
            l => (config.Alpha * prime[l]) + ((1 - config.Alpha) * previous.GetValueOrDefault(l)),
            StringComparer.Ordinal);
        return Normalize(blended.Select(kv => (kv.Key, kv.Value)));
    }

    /// <summary>Drops expired modifiers, stores the new distribution, and resets the every-5th counter (RE-02).</summary>
    public static EmotionState Incorporate(EmotionState state, IReadOnlyDictionary<string, double> observed, double day,
        EmotionConfig config)
    {
        var active = state.Modifiers.Where(m => day < m.StartDay + m.DurationDays).ToList();
        return state with
        {
            LastDistribution = Distribution(observed, active, state.LastDistribution, day, config),
            Modifiers = active,
            DecisionsSinceLastCheck = 0,
        };
    }

    /// <summary>RE-08 limits: intensity ≤ 1, duration ≤ the configured maximum, at most <c>max_modifiers</c> active.</summary>
    public static IReadOnlyList<EmotionModifier> Add(IReadOnlyList<EmotionModifier> current, EmotionModifier added, double day,
        EmotionConfig config)
    {
        var next = current.Where(m => day < m.StartDay + m.DurationDays).ToList();
        next.Add(added with
        {
            Intensity = Math.Clamp(added.Intensity, 0, 1),
            DurationDays = Math.Min(Math.Max(0, added.DurationDays), config.MaxDurationDays),
        });
        return next
            .OrderByDescending(m => IntensityAt(m, day, config.Decay))
            .ThenBy(m => m.Reason, StringComparer.Ordinal)
            .Take(config.MaxModifiers)
            .ToList();
    }

    /// <summary>The configured intensity for a category such as <c>mild</c>. Unknown categories throw.</summary>
    public static double IntensityOf(string category, EmotionConfig config) =>
        config.IntensityMap.TryGetValue(category, out var value)
            ? value
            : throw new KeyNotFoundException($"emotion.intensity_map has no '{category}'");

    /// <summary>The configured duration in personal days for a category such as <c>about a week</c>.</summary>
    public static int DurationOf(string category, EmotionConfig config) =>
        config.DurationMapDays.TryGetValue(category, out var days)
            ? days
            : throw new KeyNotFoundException($"emotion.duration_map_days has no '{category}'");

    /// <summary>Highest configured band whose value is at or below <paramref name="intensity"/> (RE-04).</summary>
    public static string Band(double intensity, EmotionConfig config)
    {
        string? band = null;
        var best = double.NegativeInfinity;
        foreach (var (name, value) in config.IntensityMap.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (value <= intensity + 1e-9 && value >= best)
            {
                best = value;
                band = name;
            }
        }

        return band ?? config.IntensityMap.OrderBy(kv => kv.Value).First().Key;
    }

    /// <summary>Dominant emotion, and the second when its probability is above <c>secondary_min_p</c> (RE-04).</summary>
    public static (string Primary, string? Secondary) Dominant(IReadOnlyDictionary<string, double> distribution, double secondaryMin)
    {
        var ordered = distribution.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).ToList();
        var second = ordered.Count > 1 && ordered[1].Value > secondaryMin ? ordered[1].Key : null;
        return (ordered[0].Key, second);
    }

    private static Dictionary<string, double> Normalize(IEnumerable<(string Label, double Value)> rows)
    {
        var list = rows.ToList();
        var sum = list.Sum(r => Math.Max(0, r.Value));
        if (sum <= 0)
            return list.ToDictionary(r => r.Label, _ => 1.0 / list.Count, StringComparer.Ordinal);
        return list.ToDictionary(r => r.Label, r => Math.Max(0, r.Value) / sum, StringComparer.Ordinal);
    }
}
