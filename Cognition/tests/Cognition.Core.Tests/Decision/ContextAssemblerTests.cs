using Cognition.Core.Config;
using Cognition.Core.Decision;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Telemetry;

namespace Cognition.Core.Tests.Decision;

[TestFixture]
public sealed class ContextAssemblerTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static readonly Lazy<PromptLibrary> Library =
        new(() => PromptLibrary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"), Config.Value.Thresholds));

    private static PerceptionFormatter Formatter => new(Config.Value, Library.Value.Vocabulary);

    private static ContextAssembler Assembler(ContextConfig? context = null)
    {
        var c = context ?? Config.Value.Context;
        return new ContextAssembler(Library.Value.Jev("decision"), c, Library.Value.Vocabulary, Formatter, new TokenEstimator(c));
    }

    private static ContextConfig Budget(int target, int hardCap = 32000) =>
        Config.Value.Context with { TargetTokens = target, HardCapTokens = hardCap };

    private static PerceptionBlock Seeing(params (string Name, float X, bool Novel)[] things)
    {
        var seen = things.Select((t, i) => new RawPerceivedEntity($"item{i}", t.Name, false, null, new Vec2(t.X, 0.5f), [], [], t.Novel, true))
            .ToList();
        var body = new RawBiophysics(new Dictionary<string, float>(), 0, 0, 90, 10, 20, 310.15f, 1, true);
        var env = new RawEnvironment(101.3f, 293.15f, new Dictionary<string, float> { ["oxygen"] = 0.21f }, []);
        return Formatter.Format(new RawPerception("self", new Vec2(0.5f, 0.5f), 0, seen, [], env, body),
            new PerceptionContext(new Dictionary<string, string>(), []));
    }

    private static ContextInput Input(PerceptionBlock? perception = null, IReadOnlyList<string>? memory = null,
        IReadOnlyList<string>? opinions = null, string? daily = "Worked in the kitchen and argued with Bob.") =>
        new("Ana Reyes", 34, "human", "chef", "14:05", 3, "midday", "standing still", "empty hands",
            "warm, impatient, proud of her cooking", "cooking; music", "mess; rudeness",
            "joy", ", a little anticipation", "none",
            ["Get something to eat"], ["Keep the kitchen stocked"],
            opinions ?? ["Bob is lazy but means well."],
            perception ?? Seeing(("sandwich", 1.5f, false)),
            memory ?? ["Saw Bob in the bar.", "Heard a shout from the kitchen."],
            daily, "comfortable", MenuChars: 1600);

    [Test]
    public void FillsExactlyTheDecisionTemplatePlaceholders()
    {
        var ctx = Assembler().Assemble(Input());

        var names = Placeholders.NamesIn(Library.Value.Jev("decision").StateTemplate).Order(StringComparer.Ordinal);
        Assert.That(ctx.Values.Keys.Order(StringComparer.Ordinal), Is.EqualTo(names));
        Assert.That(ctx.Values["recent_memory"], Is.EqualTo("- Saw Bob in the bar.\n- Heard a shout from the kitchen."));
        Assert.That(ctx.Values["immediate_goals"], Is.EqualTo("Get something to eat"));
        Assert.That(ctx.Values["perception_block"], Does.Contain("sandwich"));
    }

    [Test]
    public void SmallContextIsNotTrimmedAndEveryBlockIsMeasured()
    {
        var ctx = Assembler().Assemble(Input());

        Assert.That(ctx.Trims, Is.Empty);
        Assert.That(ctx.OverTarget, Is.False);
        Assert.That(ctx.BlockTokens.Keys, Is.EquivalentTo(Enum.GetValues<ContextBlock>()));
        Assert.That(ctx.BlockTokens[ContextBlock.Instructions], Is.GreaterThan(100));
        Assert.That(ctx.BlockTokens[ContextBlock.Menus], Is.EqualTo(400));
        Assert.That(ctx.EstimatedTokens, Is.EqualTo(ctx.BlockTokens.Values.Sum()));
    }

    [Test]
    public void EmptyListsSayNone()
    {
        var ctx = Assembler().Assemble(Input(memory: [], opinions: [], daily: null) with { MediumGoals = [] });

        Assert.That(ctx.Values["recent_memory"], Is.EqualTo("- none"));
        Assert.That(ctx.Values["present_opinions"], Is.EqualTo("none"));
        Assert.That(ctx.Values["last_daily_short"], Is.EqualTo("none"));
        Assert.That(ctx.Values["medium_goals"], Is.EqualTo("none"));
    }

    [Test]
    public void DailyMemoryIsTrimmedFirst()
    {
        var full = Assembler().Assemble(Input(daily: new string('d', 2000)));
        var target = full.EstimatedTokens - 10;

        var ctx = Assembler(Budget(target)).Assemble(Input(daily: new string('d', 2000)));

        Assert.That(ctx.Trims, Is.EqualTo(new[] { new TrimStep(ContextBlock.DailyMemory, 1) }));
        Assert.That(ctx.Values["last_daily_short"], Is.EqualTo("none"));
        Assert.That(ctx.Values["recent_memory"], Does.Contain("Saw Bob"));
    }

    [Test]
    public void ThenOldestRecentMemoriesThenLeastRelevantOpinions()
    {
        var memory = Enumerable.Range(1, 10).Select(i => $"memory number {i:D2} " + new string('m', 200)).ToList();
        var opinions = new[] { "Most relevant opinion " + new string('o', 200), "Least relevant opinion " + new string('o', 200) };
        var baseline = Assembler().Assemble(Input(memory: [], opinions: [opinions[0]], daily: null)).EstimatedTokens;

        var ctx = Assembler(Budget(baseline)).Assemble(Input(memory: memory, opinions: opinions));

        Assert.That(ctx.Trims.Select(t => t.Block), Is.EqualTo(new[]
        {
            ContextBlock.RecentMemory, ContextBlock.DailyMemory, ContextBlock.Opinions,
        }.Order()));
        Assert.That(ctx.Trims.Single(t => t.Block == ContextBlock.RecentMemory).Removed, Is.EqualTo(10));
        Assert.That(ctx.Values["present_opinions"], Does.StartWith("Most relevant"));
        Assert.That(ctx.Values["perception_block"], Does.Contain("sandwich"));
    }

    [Test]
    public void OldestMemoriesGoBeforeNewerOnes()
    {
        var memory = Enumerable.Range(1, 10).Select(i => $"memory number {i:D2} " + new string('m', 200)).ToList();
        var withThree = Assembler().Assemble(Input(memory: memory.TakeLast(3).ToList(), daily: null)).EstimatedTokens;

        var ctx = Assembler(Budget(withThree)).Assemble(Input(memory: memory));

        Assert.That(ctx.Values["recent_memory"], Does.Not.Contain("number 07").And.Contain("number 08").And.Contain("number 10"));
        Assert.That(ctx.Values["present_opinions"], Does.Contain("Bob is lazy"));
    }

    [Test]
    public void PerceptionLosesItsLeastSalientLinesLast()
    {
        var perception = Seeing(("far crate", 12.5f, false), ("sandwich", 1.5f, false), ("new gadget", 6.5f, true));
        var noThings = Assembler().Assemble(Input(perception: Seeing(("sandwich", 1.5f, false), ("new gadget", 6.5f, true)),
            memory: [], opinions: [], daily: null)).EstimatedTokens;

        var ctx = Assembler(Budget(noThings)).Assemble(Input(perception: perception));

        Assert.That(ctx.Trims.Select(t => t.Block), Does.Contain(ContextBlock.Perception));
        Assert.That(ctx.Perception.Seen.Select(s => s.Ref), Is.EquivalentTo(new[] { "item1", "item2" }));
        Assert.That(ctx.Values["perception_block"], Does.Not.Contain("far crate").And.Contain("new gadget"));
        Assert.That(ctx.Perception.DroppedSeen, Is.EqualTo(1));
    }

    [Test]
    public void NeverTrimmedBlocksSurviveAnImpossibleTarget()
    {
        var ctx = Assembler(Budget(1)).Assemble(Input(perception: Seeing(("sandwich", 1.5f, false), ("crate", 5.5f, false))));

        Assert.That(ctx.OverTarget, Is.True);
        Assert.That(ctx.Perception.Seen, Is.Empty);
        Assert.That(ctx.Values["immediate_goals"], Is.EqualTo("Get something to eat"));
        Assert.That(ctx.Values["perception_block"], Does.Contain("BODY: hunger"));
        Assert.That(ctx.BlockTokens[ContextBlock.Menus], Is.EqualTo(400));
        Assert.That(ctx.BlockTokens[ContextBlock.Instructions], Is.GreaterThan(100));
        Assert.That(ctx.Values["personality_summary"], Is.Not.Empty);
    }

    [Test]
    public void AboveTheHardCapAfterAllTrimsThrows()
    {
        var ex = Assert.Throws<ContextOverflowException>(() => Assembler(Budget(1, hardCap: 500)).Assemble(Input()));

        Assert.That(ex!.Message, Does.Contain("hard cap 500"));
    }

    [Test]
    public void BlocksAboveTheirOwnTargetAreFlaggedNotTrimmed()
    {
        var ctx = Assembler().Assemble(Input() with { PersonalitySummary = new string('p', 1500) });

        Assert.That(ctx.OverBlockTarget, Is.EqualTo(new[] { ContextBlock.Personality }));
        Assert.That(ctx.Values["personality_summary"], Has.Length.EqualTo(1500));
    }

    [Test]
    public void ContextRecordIsOneStableJsonLine()
    {
        var ctx = Assembler(Budget(900)).Assemble(Input(daily: new string('d', 2000)));

        var line = TelemetryJson.Line(ctx.ToRecord(12.3456, "npc_01", "decision"));

        Assert.That(line, Does.StartWith("""{"t":12.346,"kind":"context","character":"npc_01","purpose":"decision","estimated_tokens":"""));
        Assert.That(line, Does.Contain("\"daily_memory\":1").And.Contain("\"menus\":400").And.Not.Contain("\n"));
    }
}

[TestFixture]
public sealed class TokenEstimatorTests
{
    private static ContextConfig Context(double rate = 0.1) =>
        new(4000, 32000, 4.0, rate, Enum.GetValues<ContextBlock>().ToDictionary(b => b.Key(), _ => 100));

    [Test]
    public void StartsAtTheConfiguredRatio()
    {
        var e = new TokenEstimator(Context());

        Assert.That(e.Estimate(ContextBlock.Perception, 400), Is.EqualTo(100));
        Assert.That(e.Estimate(ContextBlock.Perception, 401), Is.EqualTo(101));
        Assert.That(e.CharsPerToken(ContextBlock.Menus), Is.EqualTo(4.0).Within(1e-9));
    }

    [Test]
    public void LearnsPerBlockRatiosFromWholeCallTotals()
    {
        var truth = new Dictionary<ContextBlock, double>
        {
            [ContextBlock.Instructions] = 4.5,
            [ContextBlock.Perception] = 3.0,
            [ContextBlock.RecentMemory] = 4.2,
            [ContextBlock.Menus] = 2.5,
        };
        var e = new TokenEstimator(Context());
        var random = new Random(7);

        for (var call = 0; call < 3000; call++)
        {
            var chars = truth.Keys.ToDictionary(b => b, _ => random.Next(100, 4000));
            var billed = (long)Math.Round(chars.Sum(kv => kv.Value / truth[kv.Key]));
            e.Observe(chars, billed);
        }

        Assert.Multiple(() =>
        {
            foreach (var (block, cpt) in truth)
            {
                Assert.That(e.CharsPerToken(block), Is.EqualTo(cpt).Within(cpt * 0.05), block.ToString());
            }
        });
        Assert.That(e.Observations, Is.EqualTo(3000));
    }

    [Test]
    public void AbsurdObservationsAreBounded()
    {
        var e = new TokenEstimator(Context(rate: 1.0));
        var chars = new Dictionary<ContextBlock, int> { [ContextBlock.Perception] = 1000 };

        e.Observe(chars, 1_000_000);
        Assert.That(e.CharsPerToken(ContextBlock.Perception), Is.EqualTo(1.0));
        e.Observe(chars, 1);
        Assert.That(e.CharsPerToken(ContextBlock.Perception), Is.EqualTo(8.0));
        e.Observe(chars, 0);
        e.Observe(new Dictionary<ContextBlock, int>(), 100);
        Assert.That(e.Observations, Is.EqualTo(2));
    }
}
