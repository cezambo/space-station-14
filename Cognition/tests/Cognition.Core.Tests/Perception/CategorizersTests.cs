using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;

namespace Cognition.Core.Tests.Perception;

[TestFixture]
public sealed class CategorizersTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static readonly Lazy<Vocabulary> Words =
        new(() => Vocabulary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts")));

    private static CognitionConfig C => Config.Value;

    // RP-05: 1.5 / 5 / 12 tiles, each boundary inclusive on the closer band.
    [TestCase(0.0, DistanceBand.WithinReach)]
    [TestCase(1.5, DistanceBand.WithinReach)]
    [TestCase(1.5001, DistanceBand.Near)]
    [TestCase(5.0, DistanceBand.Near)]
    [TestCase(5.0001, DistanceBand.Medium)]
    [TestCase(12.0, DistanceBand.Medium)]
    [TestCase(12.0001, DistanceBand.Far)]
    [TestCase(400.0, DistanceBand.Far)]
    public void Distance(double tiles, DistanceBand expected)
    {
        Assert.That(Categorizers.Distance(tiles, C.Perception), Is.EqualTo(expected));
    }

    // RP-05: north is −Y. Sector edges at 22.5° + k·45° go clockwise.
    [TestCase(0, -1, Compass.North)]
    [TestCase(1, -1, Compass.NorthEast)]
    [TestCase(1, 0, Compass.East)]
    [TestCase(1, 1, Compass.SouthEast)]
    [TestCase(0, 1, Compass.South)]
    [TestCase(-1, 1, Compass.SouthWest)]
    [TestCase(-1, 0, Compass.West)]
    [TestCase(-1, -1, Compass.NorthWest)]
    [TestCase(0, -5, Compass.North)]
    public void DirectionOnAxes(double dx, double dy, Compass expected)
    {
        Assert.That(Categorizers.Direction(dx, dy), Is.EqualTo(expected));
    }

    [TestCase(22.4, Compass.North)]
    [TestCase(22.5, Compass.NorthEast)]
    [TestCase(67.4, Compass.NorthEast)]
    [TestCase(67.5, Compass.East)]
    [TestCase(337.4, Compass.NorthWest)]
    [TestCase(337.5, Compass.North)]
    [TestCase(359.9, Compass.North)]
    [TestCase(180.0, Compass.South)]
    [TestCase(202.5, Compass.SouthWest)]
    public void DirectionAtSectorEdges(double bearingDeg, Compass expected)
    {
        var rad = bearingDeg * Math.PI / 180.0;
        var dx = Math.Round(Math.Sin(rad), 12);
        var dy = Math.Round(-Math.Cos(rad), 12);

        Assert.That(Categorizers.Direction(dx, dy), Is.EqualTo(expected));
    }

    [Test]
    public void SameTileHasNoDirection()
    {
        Assert.That(Categorizers.Direction(0, 0), Is.Null);
    }

    // RS-02 fatigue bands: 0-59 ok, 60-79 mild, 80-94 strong, 95+ critical.
    [TestCase(0.0, NeedBand.Ok)]
    [TestCase(59.99, NeedBand.Ok)]
    [TestCase(60.0, NeedBand.Mild)]
    [TestCase(79.99, NeedBand.Mild)]
    [TestCase(80.0, NeedBand.Strong)]
    [TestCase(94.99, NeedBand.Strong)]
    [TestCase(95.0, NeedBand.Critical)]
    [TestCase(100.0, NeedBand.Critical)]
    public void FatigueBands(double severity, NeedBand expected)
    {
        Assert.That(Categorizers.Need("fatigue", severity, C.Needs), Is.EqualTo(expected));
    }

    [Test]
    public void UnknownNeedThrows()
    {
        Assert.Throws<KeyNotFoundException>(() => Categorizers.Need("boredom", 50, C.Needs));
    }

    // RS-07: bounds at 0.33 / 0.75 / 1.0 of T_wake (T_wake = 100 min keeps the arithmetic exact).
    [TestCase(0.0, DayPhase.Early)]
    [TestCase(32.99, DayPhase.Early)]
    [TestCase(33.0, DayPhase.Middle)]
    [TestCase(74.99, DayPhase.Middle)]
    [TestCase(75.0, DayPhase.Late)]
    [TestCase(99.99, DayPhase.Late)]
    [TestCase(100.0, DayPhase.Overdue)]
    [TestCase(250.0, DayPhase.Overdue)]
    public void DayPhases(double awakeMinutes, DayPhase expected)
    {
        var sleep = C.Sleep with { TWakeMinutes = 100 };

        Assert.That(Categorizers.Phase(TimeSpan.FromMinutes(awakeMinutes), sleep), Is.EqualTo(expected));
    }

    // RJ-09 with daily_units = 20: some from 6, plenty from 12.
    [TestCase(0, BudgetBand.Empty)]
    [TestCase(-3, BudgetBand.Empty)]
    [TestCase(1, BudgetBand.Low)]
    [TestCase(5, BudgetBand.Low)]
    [TestCase(6, BudgetBand.Some)]
    [TestCase(11, BudgetBand.Some)]
    [TestCase(12, BudgetBand.Plenty)]
    [TestCase(20, BudgetBand.Plenty)]
    public void BudgetBands(int remaining, BudgetBand expected)
    {
        var budget = C.Budget with { DailyUnits = 20 };

        Assert.That(Categorizers.Budget(remaining, budget), Is.EqualTo(expected));
    }

    // RJ-09: light costs 1, deep costs 6.
    [TestCase(0, ThinkModes.None)]
    [TestCase(1, ThinkModes.Light)]
    [TestCase(5, ThinkModes.Light)]
    [TestCase(6, ThinkModes.Light | ThinkModes.Deep)]
    public void AffordableThinkModes(int remaining, ThinkModes expected)
    {
        var budget = C.Budget with { LightCost = 1, DeepCost = 6 };

        Assert.That(Categorizers.Affordable(remaining, budget), Is.EqualTo(expected));
    }

    // RE-04: nearest intensity_map label, cut at midpoints (0.225 / 0.4 / 0.6 / 0.8).
    [TestCase(0.0, null)]
    [TestCase(-0.1, null)]
    [TestCase(0.01, "faint")]
    [TestCase(0.2249, "faint")]
    [TestCase(0.225, "mild")]
    [TestCase(0.3999, "mild")]
    [TestCase(0.4, "moderate")]
    [TestCase(0.5999, "moderate")]
    [TestCase(0.6, "strong")]
    [TestCase(0.7999, "strong")]
    [TestCase(0.8, "overwhelming")]
    [TestCase(1.0, "overwhelming")]
    public void IntensityLabels(double intensity, string? expected)
    {
        Assert.That(Categorizers.Intensity(intensity, C.Emotion.IntensityMap), Is.EqualTo(expected));
    }

    private static EnvironmentReading Air(double kpa = 101.3, double kelvin = 293.15, bool fire = false,
        Dictionary<string, double>? gases = null, string[]? puddles = null) =>
        new(kpa, kelvin, gases ?? new Dictionary<string, double>(), fire, puddles ?? []);

    private static SensationKind[] KindsOf(EnvironmentReading r) =>
        Categorizers.Environment(r, C.Perception.Environment, Words.Value.NoticeableGases).Select(s => s.Kind).ToArray();

    // RP-06, SS14 pressure constants: hazard ≤ 20, warning ≤ 50, warning ≥ 385, hazard ≥ 550.
    [TestCase(0.0, SensationKind.AlmostNoAir)]
    [TestCase(20.0, SensationKind.AlmostNoAir)]
    [TestCase(20.01, SensationKind.ThinAir)]
    [TestCase(50.0, SensationKind.ThinAir)]
    [TestCase(385.0, SensationKind.HeavyAir)]
    [TestCase(549.99, SensationKind.HeavyAir)]
    [TestCase(550.0, SensationKind.CrushingPressure)]
    public void Pressure(double kpa, SensationKind expected)
    {
        Assert.That(KindsOf(Air(kpa: kpa)), Is.EqualTo(new[] { expected }));
    }

    [TestCase(50.01)]
    [TestCase(101.3)]
    [TestCase(384.99)]
    public void NormalAirIsNotSensed(double kpa)
    {
        Assert.That(KindsOf(Air(kpa: kpa)), Is.Empty);
    }

    // RP-06 temperature: ≤ 260 K freezing (SS14 cold damage), ≤ 273.15 cold, ≥ 323.15 hot, ≥ 360 scorching.
    [TestCase(100.0, SensationKind.FreezingCold)]
    [TestCase(260.0, SensationKind.FreezingCold)]
    [TestCase(260.01, SensationKind.Cold)]
    [TestCase(273.15, SensationKind.Cold)]
    [TestCase(323.15, SensationKind.Hot)]
    [TestCase(359.99, SensationKind.Hot)]
    [TestCase(360.0, SensationKind.ScorchingHeat)]
    public void Temperature(double kelvin, SensationKind expected)
    {
        Assert.That(KindsOf(Air(kelvin: kelvin)), Is.EqualTo(new[] { expected }));
    }

    [Test]
    public void GasesAreSensedAboveNoticeLevelStrongestFirst()
    {
        var r = Air(gases: new Dictionary<string, double>
        {
            ["oxygen"] = 21.0,
            ["plasma"] = 0.6,
            ["tritium"] = 3.0,
            ["frezon"] = 0.49,
        });

        var sensed = Categorizers.Environment(r, C.Perception.Environment, Words.Value.NoticeableGases);

        Assert.That(sensed.Select(s => s.Subject), Is.EqualTo(new[] { "tritium", "plasma" }));
    }

    [Test]
    public void HazardsComeFirstThenFireGasesAndPuddles()
    {
        var r = Air(kpa: 10, kelvin: 400, fire: true, gases: new Dictionary<string, double> { ["plasma"] = 5 },
            puddles: ["blood", "blood", "water"]);

        var sensed = Categorizers.Environment(r, C.Perception.Environment, Words.Value.NoticeableGases);

        Assert.That(sensed, Is.EqualTo(new[]
        {
            new Sensation(SensationKind.AlmostNoAir),
            new Sensation(SensationKind.ScorchingHeat),
            new Sensation(SensationKind.Fire),
            new Sensation(SensationKind.Gas, "plasma"),
            new Sensation(SensationKind.Puddle, "blood"),
            new Sensation(SensationKind.Puddle, "water"),
        }));
    }
}
