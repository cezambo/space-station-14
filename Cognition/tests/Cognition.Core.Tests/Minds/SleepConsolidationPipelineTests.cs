using Cognition.Core.Config;
using Cognition.Core.Minds;
using Microsoft.Data.Sqlite;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class SleepConsolidationPipelineTests
{
    private string _dir = null!;
    private SqliteMindStore _store = null!;
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cognition-sleep-" + Guid.NewGuid().ToString("N"));
        _store = new SqliteMindStore(Path.Combine(_dir, "minds.sqlite"));
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private Mind Spent() => TestMinds.Ana() with { ThinkingBudget = new ThinkingBudget(20, 3) };

    private SleepConsolidationPipeline Pipeline(IReadOnlyList<IConsolidationStep>? steps = null, CognitionConfig? config = null) =>
        new(_store, config ?? Config.Value, steps);

    [Test]
    public async Task AFullSleepAdvancesTheDayRefillsTheBudgetAndSummarizesTheDay()
    {
        var mind = _store.Create(Spent());
        _store.AppendMemory(mind.StableGuid, TestMinds.Recent("cooked lunch"));
        _store.AppendMemory(mind.StableGuid, TestMinds.Recent("argued with Bob"));

        var result = await Pipeline().RunAsync(mind.StableGuid);

        Assert.That(result.Resumed, Is.False);
        Assert.That(result.DeferredSteps, Is.Empty);
        Assert.That(result.Mind.Sleep.PersonalDay, Is.EqualTo(13));
        Assert.That(result.Mind.Sleep.AwakeSeconds, Is.EqualTo(0));
        Assert.That(result.Mind.Sleep.Fatigue, Is.EqualTo(41));
        Assert.That(result.Mind.ThinkingBudget.Remaining, Is.EqualTo(20));
        Assert.That(result.Mind.Version, Is.EqualTo(1));
        var daily = _store.Memories(mind.StableGuid, MemoryLevel.Daily);
        Assert.That(daily.Select(m => m.Text), Is.EqualTo(new[] { "cooked lunch; argued with Bob" }));
        Assert.That(daily[0].Day, Is.EqualTo(12));
        Assert.That(_store.LoadCheckpoint(mind.StableGuid), Is.Null);
    }

    [TestCase(ConsolidationSteps.DailySummary)]
    [TestCase(ConsolidationSteps.Opinions)]
    [TestCase(ConsolidationSteps.Budget)]
    public async Task KillingAfterAStepLeavesThePriorStateAndResumingFinishesOnce(string haltAfter)
    {
        var mind = _store.Create(Spent());
        _store.AppendMemory(mind.StableGuid, TestMinds.Recent("cooked lunch"));
        var prior = MindJson.Serialize(_store.Load(mind.StableGuid)!);
        var first = Pipeline();
        first.AfterCheckpoint = id =>
        {
            if (id == haltAfter)
                throw new ConsolidationHaltedException(id);
        };

        Assert.ThrowsAsync<ConsolidationHaltedException>(() => first.RunAsync(mind.StableGuid));

        Assert.That(MindJson.Serialize(_store.Load(mind.StableGuid)!), Is.EqualTo(prior));
        Assert.That(_store.Memories(mind.StableGuid, MemoryLevel.Daily), Is.Empty);
        Assert.That(_store.LoadCheckpoint(mind.StableGuid), Is.Not.Null);

        var result = await Pipeline().RunAsync(mind.StableGuid);

        Assert.That(result.Resumed, Is.True);
        Assert.That(result.Mind.Sleep.PersonalDay, Is.EqualTo(13));
        Assert.That(result.Mind.ThinkingBudget.Remaining, Is.EqualTo(20));
        Assert.That(_store.Memories(mind.StableGuid, MemoryLevel.Daily), Has.Count.EqualTo(1));
        Assert.That(_store.LoadCheckpoint(mind.StableGuid), Is.Null);
    }

    [Test]
    public async Task AStepIsRetriedThenDeferredAndTheRestStillCommit()
    {
        var calls = 0;
        var steps = ConsolidationSteps.Defaults.Select(s => s.Id == ConsolidationSteps.Opinions
            ? new DelegateConsolidationStep(s.Id, _ =>
            {
                calls++;
                throw new InvalidOperationException("classifier down");
            })
            : s).ToList();
        var mind = _store.Create(Spent());

        var result = await Pipeline(steps).RunAsync(mind.StableGuid);

        Assert.That(calls, Is.EqualTo(Config.Value.Consolidation.StepMaxRetries + 1));
        Assert.That(result.DeferredSteps, Is.EqualTo(new[] { ConsolidationSteps.Opinions }));
        Assert.That(result.Mind.Sleep.DeferredSteps, Is.EqualTo(new[] { ConsolidationSteps.Opinions }));
        Assert.That(result.Mind.Sleep.PersonalDay, Is.EqualTo(13));
        Assert.That(result.Mind.ThinkingBudget.Remaining, Is.EqualTo(20));
        Assert.That(result.Log, Has.Some.Contains("deferred to the next sleep").And.Some.Contains("classifier down"));
    }

    [Test]
    public async Task ATransientFailureIsRetriedWithoutDeferring()
    {
        var calls = 0;
        var steps = ConsolidationSteps.Defaults.Select(s => s.Id == ConsolidationSteps.Rupture
            ? new DelegateConsolidationStep(s.Id, view =>
            {
                if (calls++ == 0)
                    throw new InvalidOperationException("once");
                return ConsolidationPatch.None(view.Mind);
            })
            : s).ToList();

        var result = await Pipeline(steps).RunAsync(_store.Create(Spent()).StableGuid);

        Assert.That(calls, Is.EqualTo(2));
        Assert.That(result.DeferredSteps, Is.Empty);
        Assert.That(result.Log, Has.Some.Contains("attempt 1"));
    }

    [Test]
    public async Task MemoriesAppendedDuringTheSleepMoveToTheNextDay()
    {
        var mind = _store.Create(Spent());
        var pipeline = Pipeline();
        pipeline.AfterCheckpoint = id =>
        {
            if (id == ConsolidationSteps.Opinions)
                _store.AppendMemory(mind.StableGuid, TestMinds.Recent("a noise during the sleep"));
        };

        await pipeline.RunAsync(mind.StableGuid);

        var noise = _store.Memories(mind.StableGuid).Single(m => m.Text == "a noise during the sleep");
        Assert.That(noise.Day, Is.EqualTo(13));
        Assert.That(noise.Level, Is.EqualTo(MemoryLevel.Recent));
    }

    [Test]
    public async Task AMindCommittedDuringTheSleepDoesNotBlockTheCommit()
    {
        var mind = _store.Create(Spent());
        var pipeline = Pipeline();
        pipeline.AfterCheckpoint = id =>
        {
            if (id != ConsolidationSteps.Budget)
                return;
            var current = _store.Load(mind.StableGuid)!;
            _store.Commit(MindCommit.Of(current with { Ss14Profile = current.Ss14Profile with { FlavorText = "changed while asleep" } }));
        };

        var result = await pipeline.RunAsync(mind.StableGuid);

        Assert.That(result.Mind.Version, Is.EqualTo(2));
        Assert.That(result.Mind.Sleep.PersonalDay, Is.EqualTo(13));
        Assert.That(result.Mind.ThinkingBudget.Remaining, Is.EqualTo(20));
        Assert.That(result.Mind.Ss14Profile.FlavorText, Is.EqualTo(mind.Ss14Profile.FlavorText));
        Assert.That(result.Log, Has.Some.Contains("newer mind"));
    }

    [Test]
    public void TheCommitIsLeftCheckpointedWhenEveryAttemptConflicts()
    {
        var config = Config.Value with { Consolidation = Config.Value.Consolidation with { CommitAttempts = 1 } };
        var mind = _store.Create(Spent());
        var pipeline = Pipeline(config: config);
        pipeline.AfterCheckpoint = id =>
        {
            if (id != ConsolidationSteps.Budget)
                return;
            var current = _store.Load(mind.StableGuid)!;
            _store.Commit(MindCommit.Of(current with { Sleep = current.Sleep with { Fatigue = 50 } }));
        };

        Assert.ThrowsAsync<MindVersionConflictException>(() => pipeline.RunAsync(mind.StableGuid));

        Assert.That(_store.Load(mind.StableGuid)!.Sleep.PersonalDay, Is.EqualTo(12));
        Assert.That(_store.LoadCheckpoint(mind.StableGuid), Is.Not.Null);
    }
}
