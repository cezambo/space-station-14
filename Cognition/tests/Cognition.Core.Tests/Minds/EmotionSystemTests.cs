using Cognition.Core.Config;
using Cognition.Core.Minds;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class EmotionSystemTests
{
    private static readonly Lazy<EmotionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null).Emotion);

    private static EmotionModifier Joy(double intensity, double start, double duration) =>
        new("joy", intensity, "a good meal", start, duration);

    private static Dictionary<string, double> Uniform() =>
        Config.Value.Labels.ToDictionary(l => l, _ => 1.0 / Config.Value.Labels.Count);

    private static Dictionary<string, double> Spike(string emotion)
    {
        var p = Config.Value.Labels.ToDictionary(l => l, _ => 0.0);
        p[emotion] = 1;
        return p;
    }

    [Test]
    public void LinearIntensityDecaysToZeroAtTheEndOfItsDuration()
    {
        var modifier = Joy(0.8, start: 10, duration: 4);

        Assert.That(EmotionSystem.IntensityAt(modifier, 10, "linear"), Is.EqualTo(0.8).Within(1e-9));
        Assert.That(EmotionSystem.IntensityAt(modifier, 12, "linear"), Is.EqualTo(0.4).Within(1e-9));
        Assert.That(EmotionSystem.IntensityAt(modifier, 14, "linear"), Is.EqualTo(0));
        Assert.That(EmotionSystem.IntensityAt(modifier, 9, "linear"), Is.EqualTo(0));
    }

    [Test]
    public void ExponentialIntensityAlsoExpiresAtTheEnd()
    {
        var modifier = Joy(0.8, start: 10, duration: 4);

        Assert.That(EmotionSystem.IntensityAt(modifier, 10, "exponential"), Is.EqualTo(0.8).Within(1e-9));
        Assert.That(EmotionSystem.IntensityAt(modifier, 12, "exponential"), Is.EqualTo(0.8 * Math.Exp(-0.5)).Within(1e-9));
        Assert.That(EmotionSystem.IntensityAt(modifier, 14, "exponential"), Is.EqualTo(0));
    }

    [Test]
    public void IntensityStaysInsideZeroAndOne()
    {
        var modifier = Joy(1.4, 0, 10);

        Assert.That(EmotionSystem.IntensityAt(modifier, 0, "linear"), Is.EqualTo(1));
        Assert.That(EmotionSystem.IntensityAt(modifier, 3, "linear"), Is.InRange(0, 1));
    }

    [Test]
    public void DistributionSumsToOneAndAModifierPullsItsEmotionUp()
    {
        var config = Config.Value;
        var previous = Uniform();
        var observed = Uniform();
        var plain = EmotionSystem.Distribution(observed, [], previous, 10, config with { Alpha = 1 });
        var pulled = EmotionSystem.Distribution(observed, [Joy(1, 10, 8)], previous, 10, config with { Alpha = 1 });

        Assert.That(pulled.Values.Sum(), Is.EqualTo(1).Within(1e-9));
        Assert.That(pulled.Values, Is.All.InRange(0, 1));
        Assert.That(pulled["joy"], Is.GreaterThan(plain["joy"]));
        Assert.That(pulled.Keys, Is.EquivalentTo(config.Labels));
    }

    [Test]
    public void InertiaMixesTheNewDistributionWithThePreviousOne()
    {
        var config = Config.Value;
        var previous = Spike("fear");
        var observed = Spike("joy");

        var stayed = EmotionSystem.Distribution(observed, [], previous, 1, config with { Alpha = 0 });
        var replaced = EmotionSystem.Distribution(observed, [], previous, 1, config with { Alpha = 1 });

        Assert.That(stayed["fear"], Is.EqualTo(1).Within(1e-9));
        Assert.That(replaced["joy"], Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void IncorporateDropsExpiredModifiersAndResetsTheCheckCounter()
    {
        var state = new EmotionState(Uniform(), [Joy(0.5, 0, 5), Joy(0.5, 0, 20)], 4);

        var next = EmotionSystem.Incorporate(state, Spike("sadness"), day: 5, Config.Value);

        Assert.That(next.Modifiers.Select(m => m.DurationDays), Is.EqualTo(new[] { 20.0 }));
        Assert.That(next.DecisionsSinceLastCheck, Is.EqualTo(0));
        Assert.That(next.LastDistribution.Values.Sum(), Is.EqualTo(1).Within(1e-9));
    }

    [Test]
    public void AddingPastTheCapEvictsTheWeakestAndClampsTheLimits()
    {
        var config = Config.Value with { MaxModifiers = 2, MaxDurationDays = 60 };
        var current = new[] { Joy(0.2, 0, 10), new EmotionModifier("fear", 0.9, "a fire", 0, 10) };

        var next = EmotionSystem.Add(current, new EmotionModifier("anger", 1.5, "an insult", 0, 90), day: 0, config);

        Assert.That(next.Select(m => m.Emotion), Is.EquivalentTo(new[] { "fear", "anger" }));
        Assert.That(next.Single(m => m.Emotion == "anger").Intensity, Is.EqualTo(1));
        Assert.That(next.Single(m => m.Emotion == "anger").DurationDays, Is.EqualTo(60));
    }

    [Test]
    public void CategoriesConvertAndTheBandIsTheHighestThatFits()
    {
        var config = Config.Value;

        Assert.That(EmotionSystem.IntensityOf("mild", config), Is.EqualTo(0.30).Within(1e-9));
        Assert.That(EmotionSystem.DurationOf("about a week", config), Is.EqualTo(7));
        Assert.That(EmotionSystem.Band(0.5, config), Is.EqualTo("moderate"));
        Assert.That(EmotionSystem.Band(0.05, config), Is.EqualTo("faint"));
        Assert.Throws<KeyNotFoundException>(() => EmotionSystem.IntensityOf("huge", config));
    }

    [Test]
    public void SecondaryEmotionIsReportedOnlyAboveItsMinimum()
    {
        var even = new Dictionary<string, double> { ["joy"] = 0.6, ["fear"] = 0.4 };
        var dominated = new Dictionary<string, double> { ["joy"] = 0.9, ["fear"] = 0.1 };

        Assert.That(EmotionSystem.Dominant(even, 0.25), Is.EqualTo(("joy", "fear")));
        Assert.That(EmotionSystem.Dominant(dominated, 0.25).Secondary, Is.Null);
    }
}
