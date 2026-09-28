using Cognition.Core.Config;
using Cognition.Core.Minds;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class CharacterSeedTests
{
    private static readonly Lazy<CognitionConfig> Config =
        new(() => CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null));

    private static string SeedDir => Path.Combine(RepoPaths.CognitionRoot, "fixtures", "characters");

    private string _tmp = null!;

    [SetUp]
    public void SetUp()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "cognition-seeds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_tmp, recursive: true);

    [Test]
    public void TenRepositorySeedsLoad()
    {
        var minds = CharacterSeeds.LoadDirectory(SeedDir, Config.Value);

        Assert.That(minds, Has.Count.EqualTo(10));
        Assert.That(minds.Select(m => m.Ss14Profile.Job).Distinct().Count(), Is.EqualTo(10), "one job each");
    }

    [Test]
    public void SeedsCoverEveryStubbornnessLevel()
    {
        // T1.30 runs E-01 over stubborn / default / fickle personalities.
        var bases = CharacterSeeds.LoadDirectory(SeedDir, Config.Value).Select(m => m.Personality.BaseStubbornness).ToList();

        Assert.That(bases, Does.Contain(8).And.Contain(5).And.Contain(3));
    }

    [Test]
    public void SeedMindStartsAtDayOneWithAFullBudget()
    {
        var mind = CharacterSeeds.LoadDirectory(SeedDir, Config.Value).First(m => m.Id == "npc_01");

        Assert.That(mind.Sleep, Is.EqualTo(new SleepState(1, 0, 0)));
        Assert.That(mind.ThinkingBudget, Is.EqualTo(new ThinkingBudget(Config.Value.Budget.DailyUnits, Config.Value.Budget.DailyUnits)));
        Assert.That(mind.Emotion.LastDistribution, Is.EqualTo(new Dictionary<string, double> { ["neutral"] = 1.0 }));
        Assert.That(mind.Goals.Immediate, Is.Empty);
        Assert.That(mind.Goals.Medium.Select(g => (g.Horizon, g.Status, g.CreatedDay)),
            Has.All.EqualTo((Horizon.Medium, GoalStatus.Active, 1)));
        Assert.That(mind.Acquaintances, Is.Empty);
    }

    [Test]
    public void SeedsStoreAndReloadUnchanged()
    {
        var store = new SqliteMindStore(Path.Combine(_tmp, "minds.sqlite"));
        var minds = CharacterSeeds.LoadDirectory(SeedDir, Config.Value);
        foreach (var m in minds)
        {
            store.Create(m);
        }

        Assert.That(store.List().Select(g => MindJson.Serialize(store.Load(g)!)),
            Is.EqualTo(minds.OrderBy(m => m.Id, StringComparer.Ordinal).Select(m => MindJson.Serialize(m))));
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    // RD-01: stubborn = 8, fickle = 3, otherwise the default 5.
    [TestCase(new string[0], 5)]
    [TestCase(new[] { "curious" }, 5)]
    [TestCase(new[] { "stubborn", "curious" }, 8)]
    [TestCase(new[] { "fickle" }, 3)]
    [TestCase(new[] { "stubborn", "stubborn" }, 8)]
    public void BaseStubbornnessFromTags(string[] tags, int expected)
    {
        Assert.That(Stubbornness.Base(tags, Config.Value.Opinion), Is.EqualTo(expected));
    }

    [Test]
    public void ContradictoryStubbornnessTagsAreRejected()
    {
        Assert.Throws<MindFormatException>(() => Stubbornness.Base(["stubborn", "fickle"], Config.Value.Opinion));
    }

    private void Copy(string id, Func<string, string>? change = null, string? asName = null)
    {
        var text = File.ReadAllText(Path.Combine(SeedDir, id + ".json"));
        File.WriteAllText(Path.Combine(_tmp, (asName ?? id) + ".json"), change?.Invoke(text) ?? text);
    }

    [Test]
    public void SeedCannotSetBaseStubbornness()
    {
        Copy("npc_01", t => t.Replace("\"tags\":", "\"baseStubbornness\": 9, \"tags\":", StringComparison.Ordinal));

        var ex = Assert.Throws<MindFormatException>(() => CharacterSeeds.LoadDirectory(_tmp, Config.Value));

        Assert.That(ex!.Message, Does.Contain("npc_01.json").And.Contain("baseStubbornness"));
    }

    [Test]
    public void AllProblemsAreReportedTogether()
    {
        Copy("npc_01", asName: "wrong_name");
        Copy("npc_02", t => t.Replace("\"openness\": 0.3", "\"openness\": 3", StringComparison.Ordinal));
        Copy("npc_03", t => t.Replace("5eed0000-0000-4000-8000-000000000003", "5eed0000-0000-4000-8000-000000000001",
            StringComparison.Ordinal));

        var ex = Assert.Throws<MindFormatException>(() => CharacterSeeds.LoadDirectory(_tmp, Config.Value));

        Assert.That(ex!.Message, Does.Contain("wrong_name.json: id 'npc_01' must match the file name")
            .And.Contain("npc_02.json: personality.traits.openness")
            .And.Contain("stableGuid 5eed0000-0000-4000-8000-000000000001 is used by npc_01, npc_03"));
    }
}
