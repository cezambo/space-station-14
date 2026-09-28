using Cognition.Core.Config;
using Cognition.Core.Minds;
using Cognition.Core.Scheduling;

namespace Cognition.Core.Tests.Scheduling;

[TestFixture]
public sealed class SchedulerTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static ScheduleRequest Person(string id, double? last = null, bool idle = false, bool visible = false,
        bool asleep = false, ControlMode control = ControlMode.Ai, IReadOnlyList<DecisionTrigger>? triggers = null) =>
        new(id, control, asleep, idle, visible, last, triggers ?? []);

    [Test]
    public void ATriggerWaitsOutTheMinimumInterval()
    {
        var scheduler = new Scheduler(Config.Value);
        var min = Config.Value.Decision.MinIntervalS;

        var early = scheduler.Plan(min - 0.01, [Person("ana", last: 0, triggers: [DecisionTrigger.Damage])]);
        var ready = scheduler.Plan(min, [Person("ana", last: 0, triggers: [DecisionTrigger.Damage])]);

        Assert.That(early.Single().Outcome, Is.EqualTo(ScheduleOutcome.NotDue));
        Assert.That(ready.Single().Outcome, Is.EqualTo(ScheduleOutcome.Grant));
    }

    [Test]
    public void SilenceWaitsForTheMaximumAndIdleWaitsLonger()
    {
        var scheduler = new Scheduler(Config.Value);
        var max = Config.Value.Decision.MaxIntervalS;
        var idle = Config.Value.Decision.IdleMaxIntervalS;

        Assert.That(scheduler.Plan(max - 0.01, [Person("ana", last: 0)]).Single().Outcome, Is.EqualTo(ScheduleOutcome.NotDue));
        Assert.That(scheduler.Plan(max, [Person("ana", last: 0)]).Single().Outcome, Is.EqualTo(ScheduleOutcome.Grant));

        var quiet = new Scheduler(Config.Value);
        Assert.That(quiet.Plan(max, [Person("ana", last: 0, idle: true)]).Single().Outcome, Is.EqualTo(ScheduleOutcome.NotDue));
        Assert.That(quiet.Plan(idle, [Person("ana", last: 0, idle: true)]).Single().Outcome, Is.EqualTo(ScheduleOutcome.Grant));
    }

    [Test]
    public void SleepingAndPlayerControlledCharactersAreExcluded()
    {
        var decisions = new Scheduler(Config.Value).Plan(100, [
            Person("asleep", asleep: true, triggers: [DecisionTrigger.Damage]),
            Person("played", control: ControlMode.Player, triggers: [DecisionTrigger.Damage]),
            Person("awake", triggers: [DecisionTrigger.Damage]),
        ]);

        Assert.That(decisions.Select(d => d.Outcome), Is.EqualTo(new[]
        {
            ScheduleOutcome.Excluded, ScheduleOutcome.Excluded, ScheduleOutcome.Grant,
        }));
    }

    [Test]
    public void PriorityUsesTheFormulaAndTheStrongestTrigger()
    {
        var weights = Config.Value.Scheduler;
        var scheduler = new Scheduler(Config.Value);
        var waited = 4.0 / Config.Value.Decision.MaxIntervalS;
        var expected = (weights.WUrgency * 0.7) + (weights.WWait * waited) + weights.WVisible;

        var decision = scheduler.Plan(4, [Person("ana", last: 0, visible: true,
            triggers: [DecisionTrigger.ActionCompleted, DecisionTrigger.ActionFailed])]);

        Assert.That(decision.Single().Priority, Is.EqualTo(expected).Within(1e-9));
        Assert.That(Scheduler.Urgency([DecisionTrigger.ActionCompleted, DecisionTrigger.Damage]), Is.EqualTo(1));
    }

    [Test]
    public void TwentyAgentsAtTwiceTheBucketGrantOnlyTheRateAndPreferDamage()
    {
        // SC-LOAD-20: 20 characters, demand at twice the Jev bucket. No call is made for the rest.
        var config = Config.Value;
        var rate = (int)Math.Floor(config.Providers.Jev.MaxRps + 1e-9);
        var scheduler = new Scheduler(config);
        var agents = Enumerable.Range(0, 20).Select(i =>
        {
            var triggers = i < 4 ? new[] { DecisionTrigger.Damage } : Array.Empty<DecisionTrigger>();
            return Person($"npc_{i:D2}", triggers: triggers);
        }).ToList();
        agents[19] = Person("npc_19", asleep: true, triggers: [DecisionTrigger.Damage]);

        var grants = new List<double>();
        for (var step = 0; step < 50; step++)
        {
            var now = step / 10.0;
            foreach (var decision in scheduler.Plan(now, agents))
            {
                if (decision.CharacterId == "npc_19")
                    Assert.That(decision.Outcome, Is.EqualTo(ScheduleOutcome.Excluded));
                else if (decision.Outcome == ScheduleOutcome.Grant)
                    grants.Add(now);
                else
                    Assert.That(decision.Outcome, Is.EqualTo(ScheduleOutcome.KeepCurrent));
            }
        }

        Assert.That(grants, Has.Count.EqualTo(rate * 5));
        for (var second = 0; second < 5; second++)
        {
            Assert.That(grants.Count(t => t >= second && t < second + 1), Is.EqualTo(rate));
        }

        var first = new Scheduler(config).Plan(0, agents);
        Assert.That(first.Where(d => d.Outcome == ScheduleOutcome.Grant).Select(d => d.CharacterId),
            Is.SupersetOf(new[] { "npc_00", "npc_01", "npc_02", "npc_03" }));
        Assert.That(first.Count(d => d.Outcome == ScheduleOutcome.Grant), Is.EqualTo(rate));
        Assert.That(first.Where(d => d.Outcome == ScheduleOutcome.KeepCurrent), Is.Not.Empty);
    }

    [Test]
    public void AnUrgentCharacterIsNeverSkippedForACalmOne()
    {
        var config = Config.Value with
        {
            Providers = Config.Value.Providers with { Jev = Config.Value.Providers.Jev with { MaxRps = 2 } },
        };
        var agents = new[]
        {
            Person("calm_a"),
            Person("hurt", triggers: [DecisionTrigger.Damage]),
            Person("calm_b"),
            Person("hurt_2", triggers: [DecisionTrigger.Addressed]),
        };

        var granted = new Scheduler(config).Plan(0, agents).Where(d => d.Outcome == ScheduleOutcome.Grant).Select(d => d.CharacterId);

        Assert.That(granted, Is.EquivalentTo(new[] { "hurt", "hurt_2" }));
    }

    [Test]
    public void LlmWindowsAreSeparateFromJevAndFromEachOther()
    {
        var scheduler = new Scheduler(Config.Value);
        var light = (int)Math.Floor(Config.Value.Providers.Light.MaxRps + 1e-9);
        var heavy = (int)Math.Floor(Config.Value.Providers.Heavy.MaxRps + 1e-9);

        Assert.That(Enumerable.Range(0, light).Count(_ => scheduler.TryAcquire(ScheduleRole.LightLlm, 0)), Is.EqualTo(light));
        Assert.That(scheduler.TryAcquire(ScheduleRole.LightLlm, 0), Is.False);
        Assert.That(Enumerable.Range(0, heavy).All(_ => scheduler.TryAcquire(ScheduleRole.HeavyLlm, 0)), Is.True);
        Assert.That(scheduler.TryAcquire(ScheduleRole.HeavyLlm, 0), Is.False);
        Assert.That(new Scheduler(Config.Value).Plan(0, [Person("ana", triggers: [DecisionTrigger.Damage])]).Single().Outcome,
            Is.EqualTo(ScheduleOutcome.Grant));
        Assert.That(scheduler.TryAcquire(ScheduleRole.LightLlm, 1), Is.True);
    }

    [Test]
    public void AnIdleCharacterWithATriggerDoesNotWaitForTheIdleInterval()
    {
        var min = Config.Value.Decision.MinIntervalS;
        var decision = new Scheduler(Config.Value).Plan(min,
            [Person("ana", last: 0, idle: true, triggers: [DecisionTrigger.SalientEntity])]);

        Assert.That(decision.Single().Outcome, Is.EqualTo(ScheduleOutcome.Grant));
    }

    [Test]
    public void ASecondPlanAtTheSameInstantDoesNotGrantAgain()
    {
        var rate = (int)Math.Floor(Config.Value.Providers.Jev.MaxRps + 1e-9);
        var scheduler = new Scheduler(Config.Value);
        var agents = Enumerable.Range(0, rate * 2)
            .Select(i => Person($"npc_{i:D2}", triggers: [DecisionTrigger.Damage]))
            .ToList();

        Assert.That(scheduler.Plan(0, agents).Count(d => d.Outcome == ScheduleOutcome.Grant), Is.EqualTo(rate));
        Assert.That(scheduler.Plan(0, agents).Count(d => d.Outcome == ScheduleOutcome.Grant), Is.Zero);
    }

    [Test]
    public void TimeDoesNotMoveBackwards()
    {
        var scheduler = new Scheduler(Config.Value);
        scheduler.AdvanceTo(5);

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.AdvanceTo(4));
    }
}
