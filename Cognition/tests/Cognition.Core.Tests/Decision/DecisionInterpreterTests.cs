using Cognition.Core.Config;
using Cognition.Core.Decision;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Decision;

[TestFixture]
public sealed class DecisionInterpreterTests
{
    private static readonly Lazy<DecisionInterpreter> Loaded = new(() =>
    {
        var config = CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null);
        var library = PromptLibrary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"), config.Thresholds);
        return new DecisionInterpreter(library.Jev("decision"), config.Thresholds);
    });

    private static DecisionInterpreter Interpreter => Loaded.Value;

    private static ChoiceAnswer Choice(string key, double confidence = 0.8) =>
        new(key, confidence, new Dictionary<string, double> { [key] = confidence, ["other"] = 1 - confidence });

    private static MenuOption Category(ActionCategory category) =>
        new(Vocabulary.SnakeCase(category.ToString()), category.ToString()) { Category = category };

    private static DecisionMenus Menus(
        IReadOnlyDictionary<string, IReadOnlyList<MenuOption>> asked,
        IReadOnlySet<string>? included = null,
        IReadOnlyDictionary<string, MenuOption>? fixedMenus = null) =>
        new(asked, fixedMenus ?? new Dictionary<string, MenuOption>(), included ?? asked.Keys.ToHashSet(StringComparer.Ordinal), []);

    [Test]
    public void AConfidentUseBecomesThatActionAndIgnoresOtherSubmenus()
    {
        var eat = new MenuOption("eat_food1", "Eat sandwich") { Action = new Affordance(ActionVerb.Eat, ItemRef: "food1") };
        var door = new MenuOption("open_door1", "Open door") { Action = new Affordance(ActionVerb.Open, "door1") };
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.UseItem), Category(ActionCategory.Interact)],
            [DecisionQuestions.UseItem] = [eat, new MenuOption("drink_drink1", "Drink water") { Action = new Affordance(ActionVerb.Drink, ItemRef: "drink1") }],
            [DecisionQuestions.InteractTarget] = [door, new MenuOption("close_door1", "Close door") { Action = new Affordance(ActionVerb.Close, "door1") }],
        });

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item"),
            [DecisionQuestions.UseItem] = Choice("eat_food1"),
            [DecisionQuestions.InteractTarget] = Choice("open_door1"),
        });

        Assert.That(decision.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Eat, ItemRef: "food1")));
        Assert.That(decision.Detail, Is.Null);
    }

    [Test]
    public void LowCategoryConfidenceKeepsTheCurrentAction()
    {
        var eat = new MenuOption("eat_food1", "Eat sandwich") { Action = new Affordance(ActionVerb.Eat, ItemRef: "food1") };
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.UseItem)],
            [DecisionQuestions.UseItem] = [eat, new MenuOption("drop_food1", "Drop sandwich") { Action = new Affordance(ActionVerb.Drop, ItemRef: "food1") }],
        });

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item", 0.34),
            [DecisionQuestions.UseItem] = Choice("eat_food1"),
        });

        Assert.That(decision.KeepsCurrentAction, Is.True);
        Assert.That(decision.Detail, Is.EqualTo(InterpretedDecision.LowConfidence));
        Assert.That(decision.Question, Is.EqualTo(DecisionQuestions.ActionCategory));
    }

    [Test]
    public void LowSubmenuConfidenceAlsoKeepsTheCurrentAction()
    {
        var eat = new MenuOption("eat_food1", "Eat sandwich") { Action = new Affordance(ActionVerb.Eat, ItemRef: "food1") };
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.UseItem)],
            [DecisionQuestions.UseItem] = [eat, new MenuOption("drop_food1", "Drop sandwich") { Action = new Affordance(ActionVerb.Drop, ItemRef: "food1") }],
        });

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item"),
            [DecisionQuestions.UseItem] = Choice("eat_food1", 0.2),
        });

        Assert.That(decision.Detail, Is.EqualTo(InterpretedDecision.LowConfidence));
        Assert.That(decision.Question, Is.EqualTo(DecisionQuestions.UseItem));
        Assert.That(decision.Intent, Is.Null);
    }

    [Test]
    public void AnUnlistedKeyNeverBecomesAnAction()
    {
        var eat = new MenuOption("eat_food1", "Eat sandwich") { Action = new Affordance(ActionVerb.Eat, ItemRef: "food1") };
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.UseItem)],
            [DecisionQuestions.UseItem] = [eat, new MenuOption("drop_food1", "Drop sandwich") { Action = new Affordance(ActionVerb.Drop, ItemRef: "food1") }],
        });

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item"),
            [DecisionQuestions.UseItem] = Choice("eat_sword"),
        });

        Assert.That(decision.Detail, Is.EqualTo(InterpretedDecision.UnknownOption));
        Assert.That(decision.Intent, Is.Null);
    }

    [Test]
    public void ASingleFixedOptionIsTakenWithoutAnAnswer()
    {
        var door = new MenuOption("open_door1", "Open door") { Action = new Affordance(ActionVerb.Open, "door1") };
        var menus = Menus(
            new Dictionary<string, IReadOnlyList<MenuOption>>
            {
                [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.Interact)],
            },
            fixedMenus: new Dictionary<string, MenuOption> { [DecisionQuestions.InteractTarget] = door });

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("interact"),
        });

        Assert.That(decision.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Open, "door1")));
    }

    [Test]
    public void NoneOfTheseFallsThroughToDirectionAndALowExtentIsIgnoredForADestination()
    {
        var fridge = new MenuOption("go_fridge1", "Walk to fridge") { DestinationRef = "fridge1" };
        var none = new MenuOption(DecisionQuestions.NoneOfThese, "None of these");
        var north = new MenuOption("north", "Walk north") { Direction = Compass.North };
        var south = new MenuOption("south", "Walk south") { Direction = Compass.South };
        var asked = new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.Move)],
            [DecisionQuestions.MoveTarget] = [fridge, none],
            [DecisionQuestions.MoveDirection] = [north, south],
        };
        var included = asked.Keys.Append(DecisionQuestions.MoveExtent).ToHashSet(StringComparer.Ordinal);

        var destination = Interpreter.Interpret(Menus(asked, included), new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("move"),
            [DecisionQuestions.MoveTarget] = Choice("go_fridge1"),
            [DecisionQuestions.MoveExtent] = Choice("short", 0.1),
        });
        Assert.That(destination.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Move, DestinationRef: "fridge1")));

        var walking = Interpreter.Interpret(Menus(asked, included), new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("move"),
            [DecisionQuestions.MoveTarget] = Choice(DecisionQuestions.NoneOfThese),
            [DecisionQuestions.MoveDirection] = Choice("north"),
            [DecisionQuestions.MoveExtent] = Choice("short"),
        });
        Assert.That(walking.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Move, Direction: Compass.North, Extent: MoveExtent.Short)));
    }

    [Test]
    public void SpeechAndThoughtCarryTheirOwnAnswers()
    {
        var bob = new MenuOption("to_npc02", "Speak to Bob") { ListenerRef = "npc_02" };
        var everyone = new MenuOption(DecisionQuestions.Everyone, "Speak to everyone") { Everyone = true };
        var speakMenus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.Speak)],
            [DecisionQuestions.SpeakTarget] = [bob, everyone],
        }, new HashSet<string>(StringComparer.Ordinal) { "action_category", "speak_target", "speak_intent" });

        var spoken = Interpreter.Interpret(speakMenus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("speak"),
            [DecisionQuestions.SpeakTarget] = Choice(DecisionQuestions.Everyone),
            [DecisionQuestions.SpeakIntent] = Choice("warn"),
        });
        Assert.That(spoken.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Speak, Volume: SpeechVolume.Normal)));
        Assert.That(spoken.SpeakToEveryone, Is.True);
        Assert.That(spoken.SpeakIntent, Is.EqualTo("warn"));

        var thinkMenus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.Think)],
            [DecisionQuestions.ThinkMode] = [new MenuOption("light", "Think briefly") { Think = ThinkModes.Light }, new MenuOption("deep", "Reconsider plans") { Think = ThinkModes.Deep }],
        });
        var thought = Interpreter.Interpret(thinkMenus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("think"),
            [DecisionQuestions.ThinkMode] = Choice("deep"),
        });
        Assert.That(thought.Think, Is.EqualTo(ThinkModes.Deep));
        Assert.That(thought.Intent, Is.Null);
        Assert.That(thought.KeepsCurrentAction, Is.False);
    }

    [Test]
    public void SleepHereActsAndADistantBedIsAWalk()
    {
        var here = new MenuOption(DecisionQuestions.Here, "Sleep right here") { Action = new Affordance(ActionVerb.Sleep) };
        var bed = new MenuOption("bed_bed1", "Walk to bed") { BedRef = "bed1", DestinationRef = "bed1" };
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.Sleep)],
            [DecisionQuestions.SleepWhere] = [here, bed],
        });

        var staying = Interpreter.Interpret(menus, Answers("sleep", DecisionQuestions.SleepWhere, DecisionQuestions.Here));
        Assert.That(staying.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Sleep)));

        var walking = Interpreter.Interpret(menus, Answers("sleep", DecisionQuestions.SleepWhere, "bed_bed1"));
        Assert.That(walking.Intent, Is.EqualTo(new ActionIntent(ActionVerb.Move, DestinationRef: "bed1")));
    }

    [Test]
    public void GoalBlockedDoesNotCancelTheActionAndATimidEmotionIsDropped()
    {
        var eat = new MenuOption("eat_food1", "Eat sandwich") { Action = new Affordance(ActionVerb.Eat, ItemRef: "food1") };
        var asked = new Dictionary<string, IReadOnlyList<MenuOption>>
        {
            [DecisionQuestions.ActionCategory] = [Category(ActionCategory.Nothing), Category(ActionCategory.UseItem)],
            [DecisionQuestions.UseItem] = [eat, new MenuOption("drop_food1", "Drop sandwich") { Action = new Affordance(ActionVerb.Drop, ItemRef: "food1") }],
        };
        var included = asked.Keys.Append(DecisionQuestions.GoalBlocked).Append(DecisionQuestions.Emotion).ToHashSet(StringComparer.Ordinal);
        var answers = new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item"),
            [DecisionQuestions.UseItem] = Choice("eat_food1"),
            [DecisionQuestions.GoalBlocked] = new NoulAnswer(0.60),
            [DecisionQuestions.Emotion] = Choice("fear", 0.1),
        };

        var decision = Interpreter.Interpret(Menus(asked, included), answers);

        Assert.That(decision.Intent!.Verb, Is.EqualTo(ActionVerb.Eat));
        Assert.That(decision.GoalBlocked, Is.True);
        Assert.That(decision.Emotion, Is.Null);

        answers[DecisionQuestions.GoalBlocked] = new NoulAnswer(0.59);
        answers[DecisionQuestions.Emotion] = Choice("fear");
        var calmer = Interpreter.Interpret(Menus(asked, included), answers);
        Assert.That(calmer.GoalBlocked, Is.False);
        Assert.That(calmer.Emotion, Is.EqualTo("fear"));
    }

    [Test]
    public void NothingToDecideDoesNotReadAnswers()
    {
        var menus = Menus(new Dictionary<string, IReadOnlyList<MenuOption>>());

        var decision = Interpreter.Interpret(menus, new Dictionary<string, JevAnswer>
        {
            [DecisionQuestions.ActionCategory] = Choice("use_item"),
        });

        Assert.That(decision.KeepsCurrentAction, Is.True);
        Assert.That(decision.Detail, Is.Null);
    }

    private static Dictionary<string, JevAnswer> Answers(string category, string menu, string key) => new()
    {
        [DecisionQuestions.ActionCategory] = Choice(category),
        [menu] = Choice(key),
    };
}
