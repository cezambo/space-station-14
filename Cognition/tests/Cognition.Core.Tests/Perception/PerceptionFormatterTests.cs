using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;

namespace Cognition.Core.Tests.Perception;

[TestFixture]
public sealed class PerceptionFormatterTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static readonly Lazy<PerceptionFormatter> Formatter = new(() =>
        new PerceptionFormatter(Config.Value, Vocabulary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"))));

    private const string Self = "guid-self";
    private const string Bob = "guid-bob";

    private static readonly Vec2 Origin = new(10.5f, 10.5f);

    private static RawBiophysics Body(float hunger = 65, float thirst = 10, float fatigue = 85,
        Dictionary<string, float>? damage = null, float bleeding = 0, float sat = 1, float temp = 310.15f) =>
        new(damage ?? new Dictionary<string, float>(), damage?.Values.Sum() ?? 0, bleeding, hunger, thirst, fatigue, temp, sat, true);

    private static RawEnvironment Air(float kpa = 101.3f) =>
        new(kpa, 293.15f, new Dictionary<string, float> { ["oxygen"] = 0.21f, ["nitrogen"] = 0.79f }, []);

    private static RawPerceivedEntity Thing(string @ref, string name, float x, float y, bool item = true, bool novel = false,
        params string[] traits) =>
        new(@ref, name, false, null, new Vec2(x, y), traits, [], novel, item);

    private static RawPerceivedEntity Person(string @ref, string guid, string description, float x, float y,
        string[]? held = null, params string[] traits) =>
        new(@ref, description, true, guid, new Vec2(x, y), traits, held ?? [], false);

    private static RawPerception Perception(IReadOnlyList<RawPerceivedEntity> seen, IReadOnlyList<RawSound>? heard = null,
        RawEnvironment? env = null, RawBiophysics? body = null) =>
        new(Self, Origin, 0, seen, heard ?? [], env ?? Air(), body ?? Body());

    private static PerceptionContext KnowsBob(params string[] goals) =>
        new(new Dictionary<string, string> { [Bob] = "Bob" }, goals);

    [Test]
    public void BlockMatchesThePlanLayout()
    {
        var p = Perception(
            [
                Person("npc_03", Bob, "man in a security uniform", 13.5f, 7.5f, ["fire extinguisher"], "injured"),
                Thing("food1", "sandwich", 10.5f, 11.5f, traits: "on a table"),
            ],
            [new RawSound("speech", new Vec2(13.5f, 7.5f), 20, 0, Bob, "man in a security uniform", "There's a fire\nin the kitchen!",
                SpeechVolume.Shout)],
            body: Body(damage: new Dictionary<string, float> { ["Blunt"] = 4 }));

        var block = Formatter.Value.Format(p, KnowsBob());

        Assert.That(block.Text, Is.EqualTo("""
            SEEN:
            - Bob (known) — near, northeast — holding fire extinguisher — looks injured
            - sandwich — within reach, south — on a table
            HEARD:
            - Bob, near, northeast, shouting: "There's a fire in the kitchen!"
            ENVIRONMENT: air normal
            BODY: hunger mild; thirst ok; fatigue strong; minor blunt damage; minor pain
            """.ReplaceLineEndings("\n")));
    }

    [Test]
    public void StrangersAreShownByDescriptionOnly()
    {
        // RD-03
        var p = Perception([Person("npc_03", Bob, "man in a security uniform", 12.5f, 10.5f)],
            [new RawSound("speech", new Vec2(12.5f, 10.5f), 10, 0, Bob, "man in a security uniform", "Hi", SpeechVolume.Normal)]);

        var block = Formatter.Value.Format(p, new PerceptionContext(new Dictionary<string, string>(), []));

        Assert.That(block.Text, Does.Not.Contain("Bob"));
        Assert.That(block.Seen.Single().Text, Does.StartWith("man in a security uniform — near, east"));
        Assert.That(block.Heard.Single().Text, Does.StartWith("man in a security uniform, near, east, saying"));
    }

    [Test]
    public void SpeechAddressedToMeIsMarked()
    {
        var p = Perception([], [new RawSound("speech", new Vec2(10.5f, 12.5f), 10, 0, Bob, "man", "Ana, help!", SpeechVolume.Normal, Self)]);

        var heard = Formatter.Value.Format(p, KnowsBob()).Heard.Single();

        Assert.That(heard.ToYou, Is.True);
        Assert.That(heard.Text, Is.EqualTo("Bob, near, south, saying to you: \"Ana, help!\""));
    }

    [Test]
    public void EmptySectionsSayNothingNotable()
    {
        var block = Formatter.Value.Format(Perception([]), KnowsBob());

        Assert.That(block.Text, Does.StartWith("SEEN:\n- nothing notable\nHEARD:\n- nothing notable\n"));
    }

    [Test]
    public void CapsPerCategory()
    {
        // RP-04: 8 entities, 5 items, 5 sounds.
        var seen = Enumerable.Range(0, 12).Select(i => Thing($"door{i}", "door", 10.5f + i, 12.5f, item: false))
            .Concat(Enumerable.Range(0, 9).Select(i => Thing($"tool{i}", "wrench", 9.5f - i, 12.5f)))
            .ToList();
        var heard = Enumerable.Range(0, 7).Select(i =>
            new RawSound("speech", Origin, 10, 0, Bob, "man", $"line {i}", SpeechVolume.Normal)).ToList();

        var block = Formatter.Value.Format(Perception(seen, heard), KnowsBob());

        Assert.That(block.Seen.Count(l => l.Category == PerceptionCategory.Entity), Is.EqualTo(8));
        Assert.That(block.Seen.Count(l => l.Category == PerceptionCategory.Item), Is.EqualTo(5));
        Assert.That(block.DroppedSeen, Is.EqualTo(8));
        Assert.That(block.Heard.Select(h => h.Text), Has.All.Contains("line").And.Some.Contains("line 6").And.None.Contains("line 1\""),
            "the most recent 5 lines are kept");
    }

    [Test]
    public void SalienceRanksDangerGoalsAndNoveltyAboveMereProximity()
    {
        var p = Perception(
        [
            Thing("near", "cup", 11.5f, 10.5f),
            Thing("goal", "medkit", 17.5f, 10.5f),
            Thing("novel", "strange device", 17.5f, 11.5f, novel: true),
            Person("hurt", "guid-x", "woman", 18.5f, 10.5f, null, "bleeding"),
        ]);

        var block = Formatter.Value.Format(p, KnowsBob("Keep the medbay supplied with medkits", "Find a medkit"));

        // goal: 0.8 + 1/8 ≈ 0.93; novel: 0.5 + 1/8.1 ≈ 0.62; near: 1/2 = 0.5.
        Assert.That(block.Seen.Where(l => l.Category == PerceptionCategory.Item).Select(l => l.Ref),
            Is.EqualTo(new[] { "goal", "novel", "near" }));
        Assert.That(block.Seen.First().Ref, Is.EqualTo("hurt"), "danger outranks proximity");
    }

    [Test]
    public void GoalKeywordsSkipStopwordsAndShortWords()
    {
        var words = Formatter.Value.GoalKeywords(["Find something to eat in the kitchen"]);

        Assert.That(words, Is.EquivalentTo(new[] { "kitchen" }));
    }

    [Test]
    public void EnvironmentAndCriticalBody()
    {
        var env = new RawEnvironment(35f, 293.15f, new Dictionary<string, float> { ["oxygen"] = 0.1f, ["plasma"] = 0.3f }, [HazardKeys.Puddle("blood")]);
        var body = Body(hunger: 0, thirst: 0, fatigue: 0, damage: new Dictionary<string, float> { ["Asphyxiation"] = 35 }, bleeding: 1,
            sat: 0.55f);

        var block = Formatter.Value.Format(Perception([], env: env, body: body), KnowsBob());

        Assert.That(block.Text, Does.Contain("ENVIRONMENT: thin air; smell of plasma; a puddle of blood\n"));
        Assert.That(block.Text, Does.EndWith("BODY: hunger ok; thirst ok; fatigue ok; breathing strong; serious asphyxiation damage; serious pain; bleeding"));
        Assert.That(block.Needs.Single(n => n.Need == "oxygen").Band, Is.EqualTo(NeedBand.Strong));
    }

    [Test]
    public void SameTileIsRightHere()
    {
        var block = Formatter.Value.Format(Perception([Thing("key1", "key", 10.5f, 10.5f)]), KnowsBob());

        Assert.That(block.Seen.Single().Text, Is.EqualTo("key — right here"));
        Assert.That(block.Seen.Single().Direction, Is.Null);
    }

    [Test]
    public void OnlySuppliedThingsAppear()
    {
        // RP-01 leakage: the formatter has no other source than the raw perception.
        var raw = Perception([Thing("a", "apple", 11.5f, 10.5f)]);

        var block = Formatter.Value.Format(raw, KnowsBob());

        Assert.That(block.Seen.Select(l => l.Ref), Is.SubsetOf(raw.Seen.Select(e => e.EntityRef)));
    }
}
