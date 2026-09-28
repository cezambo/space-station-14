using Cognition.Core.Config;

namespace Cognition.Core.Perception;

public enum DistanceBand
{
    WithinReach,
    Near,
    Medium,
    Far,
}

/// <summary>Clockwise from north. North is −Y (screen up) in the sandbox grid.</summary>
public enum Compass
{
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

public enum NeedBand
{
    Ok,
    Mild,
    Strong,
    Critical,
}

public enum DayPhase
{
    Early,
    Middle,
    Late,
    Overdue,
}

public enum BudgetBand
{
    Empty,
    Low,
    Some,
    Plenty,
}

[Flags]
public enum ThinkModes
{
    None = 0,
    Light = 1,
    Deep = 2,
}

public enum SensationKind
{
    AlmostNoAir,
    ThinAir,
    HeavyAir,
    CrushingPressure,
    FreezingCold,
    Cold,
    Hot,
    ScorchingHeat,
    Fire,
    Gas,
    Puddle,
}

/// <summary><see cref="Subject"/> is the gas id for <see cref="SensationKind.Gas"/> or the reagent for <see cref="SensationKind.Puddle"/>.</summary>
public sealed record Sensation(SensationKind Kind, string? Subject = null);

/// <summary>Raw atmosphere facts at the character's tile, as provided by an adapter (RA-06).</summary>
public sealed record EnvironmentReading(
    double PressureKpa,
    double TemperatureK,
    IReadOnlyDictionary<string, double> GasPartialKpa,
    bool FireVisible,
    IReadOnlyList<string> VisiblePuddles);

/// <summary>
/// Pure number → category functions (RP-05, RP-06, RP-08, RS-07, RJ-09, RE-04). Boundaries are inclusive on
/// the lower band: exactly 1.5 tiles is <c>within reach</c>; exactly 60 fatigue is <c>mild</c>.
/// Words for each category live in <c>prompts/vocabulary.yaml</c> (P7).
/// </summary>
public static class Categorizers
{
    public static DistanceBand Distance(double tiles, PerceptionConfig p)
    {
        if (tiles <= p.BandWithinReach)
            return DistanceBand.WithinReach;
        if (tiles <= p.BandNear)
            return DistanceBand.Near;
        return tiles <= p.BandMedium ? DistanceBand.Medium : DistanceBand.Far;
    }

    /// <summary>
    /// 8 sectors of 45°, centred on each compass point; a bearing exactly on a sector edge (22.5° + k·45°)
    /// belongs to the clockwise sector. Null when the target is on the same tile.
    /// </summary>
    public static Compass? Direction(double dx, double dy)
    {
        if (dx == 0 && dy == 0)
            return null;
        var bearing = Math.Round(Math.Atan2(dx, -dy) * 180.0 / Math.PI, 9);
        if (bearing < 0)
            bearing += 360.0;
        var sector = (int)Math.Floor((bearing + 22.5) / 45.0) % 8;
        return (Compass)sector;
    }

    /// <summary><paramref name="bounds"/>: where mild, strong and critical start (severity 0-100).</summary>
    public static NeedBand Need(double severity, IReadOnlyList<double> bounds)
    {
        if (severity >= bounds[2])
            return NeedBand.Critical;
        if (severity >= bounds[1])
            return NeedBand.Strong;
        return severity >= bounds[0] ? NeedBand.Mild : NeedBand.Ok;
    }

    public static NeedBand Need(string need, double severity, NeedsConfig needs) =>
        Need(severity, needs.Bands.TryGetValue(need, out var b) ? b : throw new KeyNotFoundException($"needs.bands.{need}"));

    /// <summary>RS-07/RS-08: phase from the awake fraction t_awake / T_wake.</summary>
    public static DayPhase Phase(TimeSpan awake, SleepConfig sleep)
    {
        var fraction = awake.TotalMinutes / sleep.TWakeMinutes;
        var b = sleep.DayPhaseBounds;
        if (fraction >= b[2])
            return DayPhase.Overdue;
        if (fraction >= b[1])
            return DayPhase.Late;
        return fraction >= b[0] ? DayPhase.Middle : DayPhase.Early;
    }

    /// <summary>RJ-09: the budget is shown as a band, never as a number.</summary>
    public static BudgetBand Budget(int remainingUnits, BudgetConfig budget)
    {
        if (remainingUnits <= 0)
            return BudgetBand.Empty;
        var fraction = (double)remainingUnits / budget.DailyUnits;
        if (fraction >= budget.BandBounds[1])
            return BudgetBand.Plenty;
        return fraction >= budget.BandBounds[0] ? BudgetBand.Some : BudgetBand.Low;
    }

    /// <summary>RJ-09: think options the character can pay for; unaffordable ones are removed from the menu.</summary>
    public static ThinkModes Affordable(int remainingUnits, BudgetConfig budget)
    {
        var modes = ThinkModes.None;
        if (remainingUnits >= budget.LightCost)
            modes |= ThinkModes.Light;
        if (remainingUnits >= budget.DeepCost)
            modes |= ThinkModes.Deep;
        return modes;
    }

    /// <summary>
    /// RE-04: the <c>emotion.intensity_map</c> label nearest to <paramref name="intensity"/> (cut at midpoints
    /// between labels; a midpoint goes to the stronger label). Null when the modifier has expired (≤ 0).
    /// </summary>
    public static string? Intensity(double intensity, IReadOnlyDictionary<string, double> intensityMap)
    {
        if (intensity <= 0)
            return null;
        var ordered = intensityMap.OrderBy(kv => kv.Value).ToList();
        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var midpoint = (ordered[i].Value + ordered[i + 1].Value) / 2;
            if (intensity < midpoint)
                return ordered[i].Key;
        }

        return ordered[^1].Key;
    }

    /// <summary>
    /// RP-06: hazards first (pressure, temperature, fire), then gases in <paramref name="noticeableGases"/> by
    /// partial pressure (highest first), then puddles. At most one pressure and one temperature sensation.
    /// </summary>
    public static IReadOnlyList<Sensation> Environment(EnvironmentReading r, EnvironmentConfig c,
        IReadOnlySet<string> noticeableGases)
    {
        var list = new List<Sensation>();
        if (r.PressureKpa <= c.PressureHazardLowKpa)
            list.Add(new Sensation(SensationKind.AlmostNoAir));
        else if (r.PressureKpa <= c.PressureWarningLowKpa)
            list.Add(new Sensation(SensationKind.ThinAir));
        else if (r.PressureKpa >= c.PressureHazardHighKpa)
            list.Add(new Sensation(SensationKind.CrushingPressure));
        else if (r.PressureKpa >= c.PressureWarningHighKpa)
            list.Add(new Sensation(SensationKind.HeavyAir));

        if (r.TemperatureK <= c.TempFreezingK)
            list.Add(new Sensation(SensationKind.FreezingCold));
        else if (r.TemperatureK <= c.TempColdK)
            list.Add(new Sensation(SensationKind.Cold));
        else if (r.TemperatureK >= c.TempScorchingK)
            list.Add(new Sensation(SensationKind.ScorchingHeat));
        else if (r.TemperatureK >= c.TempHotK)
            list.Add(new Sensation(SensationKind.Hot));

        if (r.FireVisible)
            list.Add(new Sensation(SensationKind.Fire));

        list.AddRange(r.GasPartialKpa
            .Where(g => noticeableGases.Contains(g.Key) && g.Value >= c.GasNoticeKpa)
            .OrderByDescending(g => g.Value).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new Sensation(SensationKind.Gas, g.Key)));

        list.AddRange(r.VisiblePuddles.Distinct(StringComparer.Ordinal).Select(p => new Sensation(SensationKind.Puddle, p)));
        return list;
    }
}
