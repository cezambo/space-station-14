using System.Text.RegularExpressions;
using Cognition.Core.Config;
using Cognition.Core.Decision;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Decision;

[TestFixture]
public sealed partial class DecisionCallBuilderTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static readonly Lazy<PromptLibrary> Library =
        new(() => PromptLibrary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"), Config.Value.Thresholds));

    private static DecisionCallBuilder Builder() => new(Library.Value, Config.Value);

    private static readonly Vec2 Self = new(5.5f, 5.5f);

    private static readonly Dictionary<string, EntityLabel> Labels = new(StringComparer.Ordinal)
    {
        ["door1"] = new("door", "within reach, north"),
        ["food1"] = new("sandwich", null),
        ["drink1"] = new("water bottle", "within reach, east"),
        ["fridge1"] = new("fridge", "within reach, west"),
        ["npc_02"] = new("Bob", "near, northeast"),
        ["medkit1"] = new("medkit", null),
    };

    private static ActionAffordances Kitchen(params Affordance[] extra) => new(
        [
            new Affordance(ActionVerb.Open, "door1"),
            new Affordance(ActionVerb.Eat, ItemRef: "food1"),
            new Affordance(ActionVerb.Pickup, ItemRef: "drink1"),
            new Affordance(ActionVerb.Put, "fridge1", "food1"),
            new Affordance(ActionVerb.Use, "npc_02", "medkit1"),
            new Affordance(ActionVerb.Speak, "npc_02"),
            new Affordance(ActionVerb.Sleep),
            .. extra,
        ],
        [new KnownDestination("fridge1", "fridge", new Vec2(4.5f, 5.5f)), new KnownDestination("bed1", "bed", new Vec2(15.5f, 5.5f), IsBed: true)],
        [Compass.North, Compass.South]);

    private static MenuInput Input(ActionAffordances? affordances = null, NeedBand fatigue = NeedBand.Ok,
        ThinkModes think = ThinkModes.Light | ThinkModes.Deep, bool goal = true, int sinceEmotion = 0,
        IReadOnlyDictionary<string, EntityLabel>? labels = null) =>
        new(affordances ?? Kitchen(), labels ?? Labels, Self, fatigue, think, goal, sinceEmotion);

    private static Dictionary<string, string> Keys(DecisionMenus m, string id) =>
        m.Asked[id].ToDictionary(o => o.Key, o => o.Criteria);

    [Test]
    public void MenusAreBuiltFromAffordancesWithShortKeysAndDescriptiveCriteria()
    {
        var m = Builder().Menus(Input());

        Assert.That(m.Fixed[DecisionQuestions.InteractTarget].Criteria, Is.EqualTo("Open door (within reach, north)"));
        Assert.That(m.Fixed[DecisionQuestions.InteractTarget].Action, Is.EqualTo(new Affordance(ActionVerb.Open, "door1")));
        Assert.That(Keys(m, DecisionQuestions.UseItem), Is.EqualTo(new Dictionary<string, string>
        {
            ["eat_food1"] = "Eat sandwich",
            ["use_medkit1_npc02"] = "Use medkit on Bob (near, northeast)",
        }));
        Assert.That(Keys(m, DecisionQuestions.InventoryAction), Is.EqualTo(new Dictionary<string, string>
        {
            ["pickup_drink1"] = "Pick up water bottle (within reach, east)",
            ["put_food1_fridge1"] = "Put sandwich into fridge (within reach, west)",
        }));
        Assert.That(Keys(m, DecisionQuestions.MoveTarget), Is.EqualTo(new Dictionary<string, string>
        {
            ["go_fridge1"] = "Walk to fridge (within reach, west)",
            ["go_bed1"] = "Walk to bed (medium distance, east)",
            ["none_of_these"] = "None of these places fits what they want",
        }));
        Assert.That(Keys(m, DecisionQuestions.SpeakTarget), Is.EqualTo(new Dictionary<string, string>
        {
            ["to_npc02"] = "Speak to Bob (near, northeast)",
            ["everyone"] = "Speak to everyone who can hear",
        }));
        Assert.That(Keys(m, DecisionQuestions.MoveDirection).Keys, Is.EqualTo(new[] { "north", "south" }));
    }

    [Test]
    public void OnlyPossibleCategoriesAreOffered()
    {
        var m = Builder().Menus(Input());

        Assert.That(Keys(m, DecisionQuestions.ActionCategory).Keys,
            Is.EqualTo(new[] { "nothing", "move", "interact", "use_item", "inventory", "speak", "think" }));

        var none = Builder().Menus(Input(new ActionAffordances([], [], []), think: ThinkModes.None));
        Assert.That(none.NothingToDecide, Is.True);
        Assert.That(none.Included, Is.Empty);
    }

    [Test]
    public void SingleOptionMenusAreFixedNotAsked()
    {
        var m = Builder().Menus(Input(think: ThinkModes.Light));

        Assert.That(m.Included, Does.Not.Contain(DecisionQuestions.ThinkMode).And.Not.Contain(DecisionQuestions.InteractTarget));
        Assert.That(m.Fixed[DecisionQuestions.ThinkMode].Think, Is.EqualTo(ThinkModes.Light));
        Assert.That(Keys(m, DecisionQuestions.ActionCategory), Does.ContainKey("think").And.ContainKey("interact"));
    }

    [Test]
    public void SleepIsOfferedOnlyWhenTiredWithKnownBeds()
    {
        Assert.That(Builder().Menus(Input()).Included, Does.Not.Contain(DecisionQuestions.SleepWhere));

        var tired = Builder().Menus(Input(fatigue: NeedBand.Mild));

        Assert.That(Keys(tired, DecisionQuestions.SleepWhere), Is.EqualTo(new Dictionary<string, string>
        {
            ["here"] = "Lie down and sleep right here",
            ["bed_bed1"] = "Walk to bed and sleep there (medium distance, east)",
        }));
        Assert.That(tired.Asked[DecisionQuestions.SleepWhere][1].BedRef, Is.EqualTo("bed1"));
        Assert.That(Keys(tired, DecisionQuestions.ActionCategory), Does.ContainKey("sleep"));
    }

    [Test]
    public void TheNearestOfSeveralBedsIsMarked()
    {
        var affordances = Kitchen() with
        {
            Destinations = [new("bed2", "bed", new Vec2(25.5f, 5.5f), true), new("bed1", "bed", new Vec2(8.5f, 5.5f), true)],
            Actions = [new Affordance(ActionVerb.Sleep, "bed9")],
        };

        var m = Builder().Menus(Input(affordances, fatigue: NeedBand.Strong));

        Assert.That(Keys(m, DecisionQuestions.SleepWhere).Values, Is.EqualTo(new[]
        {
            "Sleep in the bed right here",
            "Walk to bed and sleep there, the nearest one (near, east)",
            "Walk to bed and sleep there (far, east)",
        }));
    }

    [Test]
    public void PeriodicAndConditionalQuestions()
    {
        Assert.That(Builder().Menus(Input(sinceEmotion: 3)).Included, Does.Not.Contain(DecisionQuestions.Emotion));
        Assert.That(Builder().Menus(Input(sinceEmotion: Config.Value.Decision.EmotionEveryN - 1)).Included,
            Does.Contain(DecisionQuestions.Emotion));
        Assert.That(Builder().Menus(Input(goal: false)).Included, Does.Not.Contain(DecisionQuestions.GoalBlocked));
        Assert.That(Builder().Menus(Input()).Included,
            Does.Contain(DecisionQuestions.GoalBlocked).And.Contain(DecisionQuestions.MoveExtent).And.Contain(DecisionQuestions.SpeakIntent));
    }

    [Test]
    public void OptionsWithoutALabelAreLeftOutWithAWarning()
    {
        var labels = new Dictionary<string, EntityLabel>(Labels);
        labels.Remove("drink1");

        var m = Builder().Menus(Input(labels: labels));

        Assert.That(m.Fixed[DecisionQuestions.InventoryAction].Key, Is.EqualTo("put_food1_fridge1"));
        Assert.That(m.Warnings, Has.Some.Contains("drink1"));
    }

    [Test]
    public void DuplicateKeysGetASuffix()
    {
        var affordances = Kitchen(new Affordance(ActionVerb.Open, "door-1"));
        var labels = new Dictionary<string, EntityLabel>(Labels) { ["door-1"] = new("door", "near, south") };

        var m = Builder().Menus(Input(affordances, labels: labels));

        Assert.That(Keys(m, DecisionQuestions.InteractTarget).Keys, Is.EqualTo(new[] { "open_door1", "open_door1_2" }));
    }

    [Test]
    public void RenderedRequestIsValidAndAsksExactlyTheIncludedQuestions()
    {
        var b = Builder();
        var m = b.Menus(Input(fatigue: NeedBand.Mild));
        var ctx = ContextFor(b, m);

        var rendered = b.Render(ctx, m, "model-x");

        Assert.That(rendered.Request.Questions.Keys, Is.EquivalentTo(m.Included));
        Assert.That(JevRequestValidator.Validate(rendered.Request, 4.0), Is.Empty);
        var category = (ChoiceQuestion)rendered.Request.Questions[DecisionQuestions.ActionCategory];
        Assert.That(category.Criteria["sleep"], Is.EqualTo("Go to sleep"));
        Assert.That(rendered.Request.State, Is.EqualTo(b.State(ctx)));
        Assert.That(rendered.Warnings, Is.Empty);
    }

    [Test]
    public void EveryInstructionIsSelfExplanatoryAndSubMenusAreConditional()
    {
        // RJ-16, RJ-17: ids are not seen, so no text may lean on a multi-word id or a raw ref; sub-menus start "Suppose".
        var b = Builder();
        var m = b.Menus(Input(fatigue: NeedBand.Mild, sinceEmotion: 4));
        var request = b.Render(ContextFor(b, m), m, "model-x").Request;
        var ids = request.Questions.Keys.Where(k => k.Contains('_', StringComparison.Ordinal)).ToList();

        Assert.Multiple(() =>
        {
            foreach (var (id, q) in request.Questions)
            {
                var texts = new List<string> { q.Instructions };
                if (q is ChoiceQuestion c)
                    texts.AddRange(c.Criteria.Values);
                foreach (var text in texts)
                {
                    Assert.That(ids.Where(other => text.Contains(other, StringComparison.Ordinal)), Is.Empty, $"{id}: {text}");
                    Assert.That(text, Does.Not.Contain("{{"), id);
                    Assert.That(RefPattern().IsMatch(text), Is.False, $"{id}: raw ref in '{text}'");
                }

                if (id is not (DecisionQuestions.ActionCategory or DecisionQuestions.GoalBlocked or DecisionQuestions.Emotion))
                    Assert.That(q.Instructions, Does.StartWith("Suppose"), id);
            }
        });
    }

    [GeneratedRegex(@"\b[a-z]+[0-9]+\b|npc_")]
    private static partial Regex RefPattern();

    private static AssembledContext ContextFor(DecisionCallBuilder b, DecisionMenus m)
    {
        var formatter = new PerceptionFormatter(Config.Value, Library.Value.Vocabulary);
        var body = new RawBiophysics(new Dictionary<string, float>(), 0, 0, 40, 10, 70, 310.15f, 1, true);
        var env = new RawEnvironment(101.3f, 293.15f, new Dictionary<string, float>(), []);
        var perception = formatter.Format(new RawPerception("self", Self, 0, [], [], env, body), new PerceptionContext(new Dictionary<string, string>(), []));
        var c = Config.Value.Context;
        var assembler = new ContextAssembler(Library.Value.Jev("decision"), c, Library.Value.Vocabulary, formatter, new TokenEstimator(c));
        return assembler.Assemble(new ContextInput("Ana", 34, "human", "chef", "14:05", 3, "midday", "standing still", "sandwich",
            "warm", "cooking", "mess", "joy", "", "none", ["Get something to eat"], [], [], perception, [], null, "comfortable",
            b.MenuChars(m)));
    }

    // ------------------------------------------------------------------ RJ-04

    private static ActionAffordances Crowded(int items) => new(
        Enumerable.Range(0, items).Select(i => new Affordance(ActionVerb.Pickup, ItemRef: $"item{i}")).Append(new Affordance(ActionVerb.Speak, "npc_02"))
            .ToList(),
        [],
        [Compass.North]);

    private static Dictionary<string, EntityLabel> CrowdedLabels(int items)
    {
        var labels = Enumerable.Range(0, items).ToDictionary(i => $"item{i}", i => new EntityLabel($"thing number {i}", "near, south"));
        labels["npc_02"] = new EntityLabel("Bob", "near, northeast");
        return labels;
    }

    private sealed class ScoringJev : IJevClient
    {
        public List<JevRequest> Requests { get; } = [];

        public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var answers = request.Questions.ToDictionary(q => q.Key, q =>
            {
                var n = int.Parse(Regex.Match(((ScoreQuestion)q.Value).Instructions, @"thing number (\d+)").Groups[1].Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                return (JevAnswer)new ScoreAnswer(n % 7 == 0 ? 4.9 : 1.1, "x", 0.9, [0.2, 0.2, 0.2, 0.2, 0.2]);
            });
            return Task.FromResult(new JevResponse($"r{Requests.Count}", TimeSpan.Zero, answers, new UsageInfo(1, 1, 0)));
        }
    }

    [Test]
    public async Task MenusOverTheLimitAreShortlistedInTwoStages()
    {
        var b = Builder();
        var m = b.Menus(Input(Crowded(300), labels: CrowdedLabels(300)));
        Assert.That(m.Oversized(DecisionCallBuilder.MaxChoiceOptions), Is.EqualTo(new[] { DecisionQuestions.InventoryAction }));
        var ctx = ContextFor(b, m);
        Assert.Throws<InvalidOperationException>(() => b.Render(ctx, m, "model-x"));

        var jev = new ScoringJev();
        var shortlisted = await b.ShortlistAsync(m, b.State(ctx), jev, "model-x", CancellationToken.None);

        Assert.That(jev.Requests, Has.Count.EqualTo(6));
        Assert.That(jev.Requests.Select(r => r.Questions.Count), Is.All.EqualTo(50));
        Assert.That(jev.Requests.Select(r => r.State), Is.All.EqualTo(b.State(ctx)));
        Assert.That(jev.Requests[0].Questions.Values.First().Instructions, Does.StartWith("Suppose"));
        var kept = shortlisted.Asked[DecisionQuestions.InventoryAction];
        Assert.That(kept, Has.Count.EqualTo(30));
        Assert.That(kept.Take(3).Select(o => o.Key), Is.EqualTo(new[] { "pickup_item0", "pickup_item7", "pickup_item14" }));
        Assert.That(kept.Count(o => int.Parse(o.Key["pickup_item".Length..], System.Globalization.CultureInfo.InvariantCulture) % 7 == 0),
            Is.EqualTo(30));
        Assert.That(JevRequestValidator.Validate(b.Render(ctx, shortlisted, "model-x").Request, 4.0), Is.Empty);
    }

    [Test]
    public void MissingShortlistAnswersRankLowest()
    {
        var b = Builder();
        var options = Enumerable.Range(0, 40).Select(i => new MenuOption($"o{i}", $"option {i}")).ToList();
        var answers = new Dictionary<string, JevAnswer> { ["fit_39"] = new ScoreAnswer(2, "x", 1, [0.5, 0.5]) };
        var response = new JevResponse("r", TimeSpan.Zero, answers, new UsageInfo(1, 1, 0));

        var kept = b.Shortlisted(options, [response]);

        Assert.That(kept, Has.Count.EqualTo(30));
        Assert.That(kept.Select(o => o.Key), Does.Contain("o39").And.Contain("o0").And.Not.Contain("o29"));
    }
}

[TestFixture]
public sealed class IncludeConditionTests
{
    private static readonly Dictionary<string, object> Vars = new(StringComparer.Ordinal)
    {
        ["fanout"] = "full",
        ["full"] = "full",
        ["reduced"] = "reduced",
        ["has_listeners"] = false,
        ["fatigue_band"] = 1,
        ["mild"] = 1,
        ["strong"] = 2,
        ["n"] = 4,
        ["every"] = 5,
    };

    [TestCase("fanout == full", true)]
    [TestCase("fanout == reduced", false)]
    [TestCase("fanout != reduced", true)]
    [TestCase("fatigue_band >= mild", true)]
    [TestCase("fatigue_band >= strong", false)]
    [TestCase("n == every - 1", true)]
    [TestCase("n + 1 == every", true)]
    [TestCase("has_listeners && fanout == full", false)]
    [TestCase("has_listeners || (fanout == full && fatigue_band < strong)", true)]
    public void Evaluates(string text, bool expected)
    {
        Assert.That(IncludeCondition.Parse(text).Evaluate(Vars, _ => throw new AssertionException("likely() asked")), Is.EqualTo(expected));
    }

    [Test]
    public void LikelyIsAskedOnlyWhenItDecides()
    {
        var asked = new List<string>();
        bool Likely(string c)
        {
            asked.Add(c);
            return c == "move";
        }

        Assert.That(IncludeCondition.Parse("fanout == full || likely(move)").Evaluate(Vars, Likely), Is.True);
        Assert.That(asked, Is.Empty);
        Assert.That(IncludeCondition.Parse("fanout == reduced || likely(move)").Evaluate(Vars, Likely), Is.True);
        Assert.That(IncludeCondition.Parse("fanout == reduced || likely(speak)").Evaluate(Vars, Likely), Is.False);
        Assert.That(asked, Is.EqualTo(new[] { "move", "speak" }));
    }

    [TestCase("fanout == ful", "unknown name 'ful'")]
    [TestCase("fanout ==", "ends too early")]
    [TestCase("(fanout == full", "expected ')'")]
    [TestCase("fanout = full", "unexpected character '='")]
    [TestCase("fatigue_band >= full", "is not a number")]
    [TestCase("n", "does not evaluate to true/false")]
    [TestCase("likely(3)", "likely() takes a name")]
    public void ErrorsAreSpecific(string text, string message)
    {
        var ex = Assert.Throws<IncludeConditionException>(() => IncludeCondition.Parse(text).Evaluate(Vars, _ => true));

        Assert.That(ex!.Message, Does.Contain(message));
    }

    [Test]
    public void VariablesExcludeLikelyArguments() =>
        Assert.That(IncludeCondition.Parse("has_listeners && (fanout == full || likely(speak))").Variables,
            Is.EquivalentTo(new[] { "has_listeners", "fanout", "full" }));
}
