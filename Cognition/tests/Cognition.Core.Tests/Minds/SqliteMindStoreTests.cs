using Cognition.Core.Minds;
using Microsoft.Data.Sqlite;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class SqliteMindStoreTests
{
    private string _dir = null!;
    private string _path = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cognition-minds-" + Guid.NewGuid().ToString("N"));
        _path = Path.Combine(_dir, "minds.sqlite");
    }

    [TearDown]
    public void TearDown()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private SqliteMindStore NewStore() => new(_path);

    [Test]
    public void CreateThenLoadAcrossStoreInstances()
    {
        NewStore().Create(TestMinds.Ana() with { Version = 42 });

        var loaded = NewStore().Load(TestMinds.Ana().StableGuid);

        Assert.That(loaded, Is.Not.Null);
        Assert.That(loaded!.Version, Is.EqualTo(0));
        Assert.That(MindJson.Serialize(loaded), Is.EqualTo(MindJson.Serialize(TestMinds.Ana())));
    }

    [Test]
    public void UnknownGuidLoadsNull()
    {
        Assert.That(NewStore().Load(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public void DuplicateCreateIsRejected()
    {
        var store = NewStore();
        store.Create(TestMinds.Ana());

        Assert.Throws<MindStoreException>(() => store.Create(TestMinds.Ana()));
        Assert.Throws<MindStoreException>(() => store.Create(TestMinds.Ana(Guid.NewGuid())));
    }

    [Test]
    public void InvalidMindIsNotWritten()
    {
        var store = NewStore();

        Assert.Throws<MindFormatException>(() => store.Create(TestMinds.Ana() with { Id = "" }));
        Assert.That(store.List(), Is.Empty);
    }

    [Test]
    public void CommitBumpsVersion()
    {
        var store = NewStore();
        var mind = store.Create(TestMinds.Ana());

        var result = store.Commit(MindCommit.Of(mind with { Sleep = mind.Sleep with { Fatigue = 90 } }));

        Assert.That(result.Mind.Version, Is.EqualTo(1));
        Assert.That(store.Load(mind.StableGuid)!.Sleep.Fatigue, Is.EqualTo(90));
        Assert.That(store.Load(mind.StableGuid)!.Version, Is.EqualTo(1));
    }

    [Test]
    public void StaleCommitConflicts()
    {
        // RS-09: consolidation works on a copy; a commit from an outdated copy is rejected.
        var store = NewStore();
        var copyA = store.Create(TestMinds.Ana());
        var copyB = store.Load(copyA.StableGuid)!;
        store.Commit(MindCommit.Of(copyA with { Sleep = copyA.Sleep with { Fatigue = 10 } }));

        var ex = Assert.Throws<MindVersionConflictException>(() => store.Commit(MindCommit.Of(copyB)));

        Assert.That((ex!.Expected, ex.Actual), Is.EqualTo((0L, (long?)1)));
        Assert.That(store.Load(copyA.StableGuid)!.Sleep.Fatigue, Is.EqualTo(10));
    }

    [Test]
    public void CommitToMissingMindConflicts()
    {
        var ex = Assert.Throws<MindVersionConflictException>(() => NewStore().Commit(MindCommit.Of(TestMinds.Ana())));

        Assert.That(ex!.Actual, Is.Null);
    }

    [Test]
    public void NewMemoryIsInsertedBeforeOldIsDeleted()
    {
        // RMe-04
        var store = NewStore();
        var mind = store.Create(TestMinds.Ana());
        var old = store.AppendMemory(mind.StableGuid, TestMinds.Recent("I cooked lunch."));
        var steps = new List<string>();
        var counts = new List<long>();
        store.OnCommitStep = (step, count) =>
        {
            steps.Add(step);
            counts.Add(count("memories"));
        };

        var result = store.Commit(new MindCommit(mind, [TestMinds.Daily("I cooked lunch for the crew.")], [old.Id]));

        Assert.That(steps, Is.EqualTo(new[] { "inserted", "deleted" }));
        Assert.That(counts, Is.EqualTo(new[] { 2L, 1L }), "old and new coexist before the delete");
        Assert.That(store.Memories(mind.StableGuid).Select(m => m.Level), Is.EqualTo(new[] { MemoryLevel.Daily }));
        Assert.That(result.Added.Single().Id, Is.GreaterThan(old.Id));
    }

    [Test]
    public void FailureInsideCommitRollsBackEverything()
    {
        var store = NewStore();
        var mind = store.Create(TestMinds.Ana());
        var old = store.AppendMemory(mind.StableGuid, TestMinds.Recent("I cooked lunch."));
        store.OnCommitStep = (step, _) =>
        {
            if (step == "inserted")
                throw new IOException("simulated crash");
        };

        Assert.Throws<IOException>(() =>
            store.Commit(new MindCommit(mind with { Sleep = mind.Sleep with { PersonalDay = 13 } },
                [TestMinds.Daily("summary")], [old.Id])));

        Assert.That(store.Memories(mind.StableGuid).Select(m => m.Id), Is.EqualTo(new[] { old.Id }));
        Assert.That(store.Load(mind.StableGuid)!.Version, Is.EqualTo(0));
        Assert.That(store.Load(mind.StableGuid)!.Sleep.PersonalDay, Is.EqualTo(12));
    }

    [Test]
    public void DeletingAnUnknownOrForeignMemoryRollsBack()
    {
        var store = NewStore();
        var ana = store.Create(TestMinds.Ana());
        var bob = store.Create(TestMinds.Ana(Guid.NewGuid()) with { Id = "npc_03", Acquaintances = new Dictionary<string, Acquaintance>() });
        var bobs = store.AppendMemory(bob.StableGuid, TestMinds.Recent("Bob's memory"));

        Assert.Throws<MindStoreException>(() => store.Commit(new MindCommit(ana, [TestMinds.Daily("x")], [bobs.Id])));
        Assert.Throws<MindStoreException>(() => store.Commit(new MindCommit(ana, [], [999])));

        Assert.That(store.Memories(ana.StableGuid), Is.Empty);
        Assert.That(store.Memories(bob.StableGuid), Has.Count.EqualTo(1));
        Assert.That(store.Load(ana.StableGuid)!.Version, Is.EqualTo(0));
    }

    [Test]
    public void MemoriesAppendedDuringSleepSurviveTheConsolidationCommit()
    {
        // RS-09: events that happen during sleep are merged into the next day.
        var store = NewStore();
        var mind = store.Create(TestMinds.Ana());
        var consumed = store.AppendMemory(mind.StableGuid, TestMinds.Recent("Before sleep."));
        var copy = store.Load(mind.StableGuid)!;

        var duringSleep = store.AppendMemory(mind.StableGuid, TestMinds.Recent("Heard an alarm while asleep."));
        var result = store.Commit(new MindCommit(copy with { Sleep = copy.Sleep with { PersonalDay = 13 } },
            [TestMinds.Daily("Day 12 summary.")], [consumed.Id]));

        Assert.That(result.Mind.Version, Is.EqualTo(1));
        Assert.That(store.Memories(mind.StableGuid, MemoryLevel.Recent).Select(m => m.Text),
            Is.EqualTo(new[] { "Heard an alarm while asleep." }));
        Assert.That(store.Memories(mind.StableGuid, MemoryLevel.Daily), Has.Count.EqualTo(1));
        Assert.That(duringSleep.Id, Is.GreaterThan(consumed.Id));
    }

    [Test]
    public void AppendToMissingMindIsRejected()
    {
        Assert.Throws<MindStoreException>(() => NewStore().AppendMemory(Guid.NewGuid(), TestMinds.Recent("x")));
    }

    [Test]
    public void MemoriesComeBackInInsertOrderWithIds()
    {
        var store = NewStore();
        var mind = store.Create(TestMinds.Ana());
        var a = store.AppendMemory(mind.StableGuid, TestMinds.Recent("a"));
        var b = store.AppendMemory(mind.StableGuid, TestMinds.Recent("b"));

        var memories = NewStore().Memories(mind.StableGuid);

        Assert.That(memories.Select(m => (m.Id, m.Text)), Is.EqualTo(new[] { (a.Id, "a"), (b.Id, "b") }));
    }

    [Test]
    public void NewerSchemaIsRefused()
    {
        NewStore();
        using (var c = new SqliteConnection($"Data Source={_path};Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"PRAGMA user_version = {SqliteMindStore.SchemaVersion + 1};";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
        Assert.Throws<MindStoreException>(() => NewStore());
    }
}
