namespace Cognition.Sandbox;

/// <summary>
/// Sandbox-only physics. Values Core cares about (sleep durations, hearing ranges, bands) come from
/// <c>cognition.toml</c>; these only shape the toy world and can be overridden per scenario.
/// </summary>
public sealed record SandboxOptions
{
    /// <summary>Fixed tick in game seconds.</summary>
    public double TickSeconds { get; init; } = 0.1;

    /// <summary>Game seconds per real second when a runner paces the world; the world itself never sleeps.</summary>
    public double TimeScale { get; init; } = 1.0;

    /// <summary>The single seed for every random draw in the world.</summary>
    public int Seed { get; init; } = 1;

    public TimeSpan StationStart { get; init; } = TimeSpan.FromHours(12);

    /// <summary>360 matches SS14 (top-down, occlusion only); smaller values give a facing cone.</summary>
    public float FovAngleDeg { get; init; } = 360f;

    public float FovRangeTiles { get; init; } = 16f;

    public float WalkTilesPerSecond { get; init; } = 4f;

    /// <summary>Severity gained per game minute awake.</summary>
    public float HungerPerMinute { get; init; } = 100f / 90f;

    public float ThirstPerMinute { get; init; } = 100f / 60f;

    public float EatRelief { get; init; } = 40f;
    public float DrinkRelief { get; init; } = 40f;

    /// <summary>Fatigue reached after T_wake awake without exertion or injury (RS-01).</summary>
    public float FatigueAtTWake { get; init; } = 80f;

    public float ExertionFatigueMultiplier { get; init; } = 1.25f;

    /// <summary>Extra fatigue rate per point of total damage.</summary>
    public float InjuryFatiguePerDamage { get; init; } = 0.01f;

    /// <summary>RS-02 critical band: chance per second of dozing off in place.</summary>
    public float CriticalDozeChancePerSecond { get; init; } = 0.02f;

    public float DozeSeconds { get; init; } = 20f;

    /// <summary>A single hit at least this large wakes a sleeper.</summary>
    public float WakeOnDamage { get; init; } = 5f;

    public float BleedDamagePerSecond { get; init; } = 0.2f;
    public float BleedDecayPerSecond { get; init; } = 0.02f;
    public float InjuredTraitDamage { get; init; } = 20f;
    public float BadlyInjuredTraitDamage { get; init; } = 60f;
    public float UnconsciousDamage { get; init; } = 100f;
    public float MedkitHeal { get; init; } = 25f;

    /// <summary>Continuous damage (bleeding, suffocation) is reported as a <c>Damaged</c> event each time this much accrues.</summary>
    public float DamageEventStep { get; init; } = 5f;

    public float BasePressureKpa { get; init; } = 101.3f;
    public float BaseTemperatureK { get; init; } = 293.15f;
    public float BodyTemperatureK { get; init; } = 310.15f;

    /// <summary>Pressure inside an active gas leak area ("thin air").</summary>
    public float LeakPressureKpa { get; init; } = 35f;

    public float LeakGasFraction { get; init; } = 0.3f;
    public float LeakOxygenFraction { get; init; } = 0.1f;
    public float SaturationDropPerSecond { get; init; } = 0.02f;
    public float SaturationRecoverPerSecond { get; init; } = 0.05f;
    public float SuffocationBelowSaturation { get; init; } = 0.85f;
    public float SuffocationDamagePerSecond { get; init; } = 1f;
}
