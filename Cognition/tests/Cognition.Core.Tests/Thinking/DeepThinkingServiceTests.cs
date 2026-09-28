using Cognition.Core.Config;
using Cognition.Core.Minds;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;
using Cognition.Core.Scheduling;
using Cognition.Core.Thinking;

namespace Cognition.Core.Tests.Thinking;

[TestFixture]
public sealed class DeepThinkingServiceTests
{
    private const string Valid = """
        {"thought":"The door is locked. I should ask someone for the key.","immediate_goals":[
        {"text":"Ask a crewmate for the storage key","priority":"high","success_check":"has the storage key"}]}
        """;

    private static readonly Lazy<(CognitionConfig Config, PromptLibrary Prompts)> Loaded = new(() =>
    {
        var config = CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null);
        var prompts = PromptLibrary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"), config.Thresholds);
        return (config, prompts);
    });

    private static ThinkRequest Ask(ThinkModes mode, int remaining, IReadOnlySet<string>? reserved = null) =>
        new("ana", 0, mode, remaining, 3, 12.5, "Ana, chef", "repair the oven", "be trusted",
            "fetch the toolbox", "none", "cooked lunch", "the door was locked", "none",
            "a locked door to the east", "calm", reserved);

    [Test]
    public async Task LightAndDeepShareTheTemplateAndPayTheirOwnCost()
    {
        var config = Loaded.Value.Config;
        var lightLlm = new ScriptLlm(Valid);
        var light = await Service(lightLlm).ThinkAsync(Ask(ThinkModes.Light, config.Budget.DailyUnits));
        var heavyLlm = new ScriptLlm(Valid);
        var deep = await Service(heavyLlm).ThinkAsync(Ask(ThinkModes.Deep, config.Budget.DailyUnits));

        Assert.That(light.Outcome, Is.EqualTo(ThinkOutcome.Thought));
        Assert.That(deep.Outcome, Is.EqualTo(ThinkOutcome.Thought));
        Assert.That(light.Cost, Is.EqualTo(config.Budget.LightCost));
        Assert.That(deep.Cost, Is.EqualTo(config.Budget.DeepCost));
        Assert.That(light.RemainingAfter, Is.EqualTo(config.Budget.DailyUnits - config.Budget.LightCost));
        Assert.That(deep.RemainingAfter, Is.EqualTo(config.Budget.DailyUnits - config.Budget.DeepCost));
        Assert.That(lightLlm.Last!.Role, Is.EqualTo(LlmRole.Light));
        Assert.That(heavyLlm.Last!.Role, Is.EqualTo(LlmRole.Heavy));
        Assert.That(lightLlm.Last.TemplateHash, Is.EqualTo(heavyLlm.Last.TemplateHash));
        Assert.That(lightLlm.Last.JsonSchema, Is.Not.Null.And.EqualTo(heavyLlm.Last.JsonSchema));
        Assert.That(lightLlm.Last.UserPrompt, Does.Contain("a locked door to the east"));
    }

    [Test]
    public async Task AThoughtBecomesAMemoryAndImmediateGoalsKeepTheirChecks()
    {
        var result = await Service(new ScriptLlm(Valid)).ThinkAsync(Ask(ThinkModes.Deep, 20, new HashSet<string> { "i1" }));

        Assert.That(result.Thought!.Source, Is.EqualTo(MemorySource.Thought));
        Assert.That(result.Thought.Level, Is.EqualTo(MemoryLevel.Recent));
        Assert.That(result.Thought.Importance, Is.EqualTo(DeepThinkingService.ThoughtImportance));
        Assert.That(result.Thought.Text, Is.EqualTo("The door is locked. I should ask someone for the key."));
        Assert.That(result.Thought.Day, Is.EqualTo(3));
        Assert.That(result.Thought.AtSeconds, Is.EqualTo(12.5));
        Assert.That(MindValidator.Validate(result.Thought), Is.Empty);
        Assert.That(result.ImmediateGoals, Has.Count.EqualTo(1));
        var goal = result.ImmediateGoals[0];
        Assert.That(goal.Id, Is.EqualTo("i2"));
        Assert.That(goal.Horizon, Is.EqualTo(Horizon.Immediate));
        Assert.That(goal.Priority, Is.EqualTo(Priority.High));
        Assert.That(goal.SuccessCheck, Is.EqualTo("has the storage key"));
        Assert.That(goal.CreatedDay, Is.EqualTo(3));
    }

    [Test]
    public async Task EveryAffordableSpendLeavesANonNegativeBudget()
    {
        var budget = Loaded.Value.Config.Budget;
        foreach (var mode in new[] { ThinkModes.Light, ThinkModes.Deep })
        {
            var cost = mode == ThinkModes.Deep ? budget.DeepCost : budget.LightCost;
            for (var remaining = 0; remaining <= budget.DailyUnits; remaining++)
            {
                var llm = new ScriptLlm(Valid);
                var result = await Service(llm).ThinkAsync(Ask(mode, remaining));

                Assert.That(result.RemainingAfter, Is.GreaterThanOrEqualTo(0), $"{mode} from {remaining}");
                if (remaining < cost)
                {
                    Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Unaffordable), $"{mode} from {remaining}");
                    Assert.That(result.Cost, Is.Zero);
                    Assert.That(result.RemainingAfter, Is.EqualTo(remaining));
                    Assert.That(llm.Calls, Is.Zero);
                }
                else
                {
                    Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Thought), $"{mode} from {remaining}");
                    Assert.That(result.Cost, Is.EqualTo(cost));
                    Assert.That(result.RemainingAfter, Is.EqualTo(remaining - cost));
                }
            }
        }
    }

    [Test]
    public async Task ASecondThoughtUsesWhatTheFirstOneLeft()
    {
        var deep = Loaded.Value.Config.Budget.DeepCost;
        var service = Service(new ScriptLlm(Valid));
        var first = await service.ThinkAsync(Ask(ThinkModes.Deep, deep));
        var second = await service.ThinkAsync(Ask(ThinkModes.Deep, first.RemainingAfter));

        Assert.That(first.RemainingAfter, Is.Zero);
        Assert.That(second.Outcome, Is.EqualTo(ThinkOutcome.Unaffordable));
        Assert.That(second.RemainingAfter, Is.Zero);
    }

    [Test]
    public async Task InvalidJsonAndABlankThoughtAreFree()
    {
        var start = Loaded.Value.Config.Budget.DailyUnits;
        var broken = await Service(new ScriptLlm("not json")).ThinkAsync(Ask(ThinkModes.Light, start));
        var empty = await Service(new ScriptLlm("""{"thought":"  ","immediate_goals":[]}""")).ThinkAsync(Ask(ThinkModes.Light, start));
        var blankGoal = await Service(new ScriptLlm("""
            {"thought":"I should do something.","immediate_goals":[{"text":"  ","priority":"low","success_check":"done"}]}
            """)).ThinkAsync(Ask(ThinkModes.Deep, start));
        var tooMany = await Service(new ScriptLlm(FourGoals())).ThinkAsync(Ask(ThinkModes.Deep, start));

        Assert.That(broken.Outcome, Is.EqualTo(ThinkOutcome.Invalid));
        Assert.That(broken.Detail, Is.EqualTo(ThinkResult.Schema));
        Assert.That(empty.Detail, Is.EqualTo(ThinkResult.EmptyThought));
        Assert.That(blankGoal.Detail, Is.EqualTo(ThinkResult.BlankGoal));
        Assert.That(tooMany.Detail, Is.EqualTo(ThinkResult.Schema));
        Assert.That(new[] { broken, empty, blankGoal, tooMany }.Select(r => r.Cost), Is.All.Zero);
        Assert.That(new[] { broken, empty, blankGoal, tooMany }.Select(r => r.RemainingAfter), Is.All.EqualTo(start));
    }

    [Test]
    public async Task ASchemaRejectionFromTheClientIsFree()
    {
        var start = 20;
        var result = await Service(new ScriptLlm(Valid, reject: true)).ThinkAsync(Ask(ThinkModes.Deep, start));

        Assert.That(result.Outcome, Is.EqualTo(ThinkOutcome.Invalid));
        Assert.That(result.Cost, Is.Zero);
        Assert.That(result.RemainingAfter, Is.EqualTo(start));
    }

    [Test]
    public async Task AFullHeavyWindowDefersDeepAndStillAllowsLight()
    {
        var config = Loaded.Value.Config;
        var scheduler = new Scheduler(config);
        var room = (int)Math.Floor(config.Providers.Heavy.MaxRps + 1e-9);
        for (var i = 0; i < room; i++)
            Assert.That(scheduler.TryAcquire(ScheduleRole.HeavyLlm, 0), Is.True);

        var heavy = new ScriptLlm(Valid);
        var deferred = await Service(heavy, scheduler).ThinkAsync(Ask(ThinkModes.Deep, config.Budget.DailyUnits));
        var light = await Service(new ScriptLlm(Valid), scheduler).ThinkAsync(Ask(ThinkModes.Light, config.Budget.DailyUnits));

        Assert.That(deferred.Outcome, Is.EqualTo(ThinkOutcome.Deferred));
        Assert.That(deferred.Cost, Is.Zero);
        Assert.That(heavy.Calls, Is.Zero);
        Assert.That(light.Outcome, Is.EqualTo(ThinkOutcome.Thought));
        Assert.That(light.Cost, Is.EqualTo(config.Budget.LightCost));
    }

    [Test]
    public void AThoughtMustBeOneMode()
    {
        var service = Service(new ScriptLlm(Valid));

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ThinkAsync(Ask(ThinkModes.None, 20)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ThinkAsync(Ask(ThinkModes.Light | ThinkModes.Deep, 20)));
    }

    private static DeepThinkingService Service(ScriptLlm llm, Scheduler? scheduler = null) =>
        new(Loaded.Value.Config, Loaded.Value.Prompts, llm, scheduler ?? new Scheduler(Loaded.Value.Config));

    private static string FourGoals()
    {
        var goals = Enumerable.Range(1, 4).Select(i =>
            $$$"""{"text":"Goal {{{i}}}","priority":"low","success_check":"goal {{{i}}} is done"}""");
        return "{\"thought\":\"Too many.\",\"immediate_goals\":[" + string.Join(',', goals) + "]}";
    }

    private sealed class ScriptLlm(string reply, bool reject = false) : ILlmClient
    {
        public int Calls { get; private set; }
        public LlmRequest? Last { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            Last = request;
            if (reject)
                throw new LlmSchemaException(["$: missing 'thought'"], reply);
            return Task.FromResult(new LlmResponse(reply, new UsageInfo(1, 1, 0), "fake"));
        }
    }
}
