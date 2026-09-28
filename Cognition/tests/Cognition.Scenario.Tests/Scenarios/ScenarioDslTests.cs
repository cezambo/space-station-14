using Cognition.Core.Minds;
using Cognition.Core.Perception;
using Cognition.Sandbox;
using Cognition.Sandbox.Scenarios;
using Cognition.Scenario.Tests.Sandbox;

namespace Cognition.Scenario.Tests.Scenarios;

[TestFixture]
public sealed class ScenarioDslTests
{
    private static ScenarioRunner Runner() =>
        new(SandboxTestKit.Config, CharacterSeeds.LoadDirectory(Path.Combine(FindRoot(), "fixtures", "characters"), SandboxTestKit.Config));

    private static IEnumerable<TestCaseData> ScriptedScenarios() =>
        ScenarioLoader.LoadDirectory(Path.Combine(FindRoot(), "scenarios"))
            .Where(s => s.Driver == "scripted")
            .Select(s => new TestCaseData(s).SetName($"Scripted_{s.Id}"));

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cognition.sln")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private const string Minimal = """
        id: tiny
        duration_s: 1
        map: |
          #####
          #...#
          #####
        characters:
          - seed: npc_01
            description: woman in a chef's uniform
            at: [1, 1]
        """;

    [TestCaseSource(nameof(ScriptedScenarios))]
    public async Task ScriptedScenarioPasses(ScenarioFile scenario)
    {
        var result = await Runner().RunAsync(scenario, driver: null);

        var failed = result.Assertions.Where(a => !a.Passed).Select(a => $"#{a.Index} {a.Description} -> {a.Detail}");
        Assert.That(result.Passed, Is.True, string.Join("\n", failed));
        Assert.That(result.Assertions, Has.Count.EqualTo(scenario.Expect.Count));
    }

    [Test]
    public void EveryScenarioFileLoadsAndBuilds()
    {
        var all = ScenarioLoader.LoadDirectory(Path.Combine(FindRoot(), "scenarios"));
        var runner = Runner();

        Assert.That(all.Select(s => s.Driver), Does.Contain("agent").And.Contain("scripted"));
        Assert.Multiple(() =>
        {
            foreach (var s in all)
            {
                Assert.DoesNotThrow(() => runner.Build(s), s.Id);
                Assert.That(s.Requirements, Is.Not.Empty, s.Id);
            }
        });
        Assert.That(all.Select(s => s.Id), Is.Unique);
    }

    [Test]
    public async Task RunsAreDeterministic()
    {
        var file = ScenarioLoader.LoadDirectory(Path.Combine(FindRoot(), "scenarios")).Single(s => s.Id == "scripted_gas_leak");

        var a = await Runner().RunAsync(file, null);
        var b = await Runner().RunAsync(file, null);

        Assert.That(b.Events, Is.EqualTo(a.Events));
    }

    [Test]
    public void UnknownKeysAreRejected()
    {
        var ex = Assert.Throws<ScenarioLoadException>(() => ScenarioLoader.Parse(Minimal + "\nwhen: later\n", "bad.yaml"));

        Assert.That(ex!.Message, Does.Contain("bad.yaml").And.Contain("when"));
    }

    [Test]
    public void AllProblemsAreReportedTogether()
    {
        var yaml = Minimal + """

            items:
              - kind: sword
                name: sword
                at: [0, 0]
            script:
              - at_s: 0
                do: { who: nobody, verb: fly }
            expect:
              - at_end: true
                within_s: 3
                door: { ref: door9, state: ajar }
            """;

        var ex = Assert.Throws<ScenarioLoadException>(() => Runner().Build(ScenarioLoader.Parse(yaml, "bad.yaml")));

        Assert.That(ex!.Errors, Has.Some.Contains("items[0].kind").And.Some.Contains("items[0].at")
            .And.Some.Contains("script[0].do.who").And.Some.Contains("script[0].do.verb")
            .And.Some.Contains("expect[0]: needs exactly one of").And.Some.Contains("door9")
            .And.Some.Contains("ajar"));
        Assert.That(ex.Errors, Has.All.StartWith("tiny: "));
    }

    [Test]
    public async Task FailedExpectationsAreReportedWithDetail()
    {
        var yaml = Minimal + """

            expect:
              - within_s: 1
                event: { who: npc_01, kind: fell_asleep }
              - at_end: true
                need: { who: npc_01, need: hunger, above: 50 }
              - at_s: 0.5
                asleep: { who: npc_01, value: false }
            """;

        var result = await Runner().RunAsync(ScenarioLoader.Parse(yaml, "t.yaml"), null);

        Assert.That(result.Passed, Is.False);
        Assert.That(result.Assertions.Select(a => a.Passed), Is.EqualTo(new[] { false, false, true }));
        Assert.That(result.Assertions[0].Detail, Is.EqualTo("not seen"));
        Assert.That(result.Assertions[0].Description, Does.StartWith("within 1s: event"));
    }

    [Test]
    public async Task DriverIsCalledEveryTickBeforeTheWorldSteps()
    {
        var yaml = Minimal + """

            driver: agent
            expect:
              - within_s: 1
                event: { who: npc_01, kind: action_completed, verb: move }
            """;
        var driver = new MoveEastOnce();

        var result = await Runner().RunAsync(ScenarioLoader.Parse(yaml, "t.yaml"), driver);

        Assert.That(driver.Ticks, Is.EqualTo(10));
        Assert.That(result.Passed, Is.True);
        Assert.ThrowsAsync<InvalidOperationException>(() => Runner().RunAsync(ScenarioLoader.Parse(yaml, "t.yaml"), null));
    }

    [Test]
    public void KnowsAddsAcquaintancesByName()
    {
        var yaml = """
            id: knows
            duration_s: 1
            map: |
              #####
              #...#
              #####
            characters:
              - seed: npc_01
                description: woman in a chef's uniform
                at: [1, 1]
                knows: [npc_02]
              - seed: npc_02
                description: man in engineering overalls
                at: [3, 1]
            expect:
              - at_end: true
                sees: { who: npc_01, what: npc_02 }
            """;

        var run = Runner().Build(ScenarioLoader.Parse(yaml, "t.yaml"));

        Assert.That(run.KnownNames("npc_01").Values, Is.EquivalentTo(new[] { run.Minds["npc_02"].Ss14Profile.Name }));
        Assert.That(run.KnownNames("npc_02"), Is.Empty);
    }

    [Test]
    public async Task CharactersCanStartAsleepInBed()
    {
        var yaml = """
            id: asleep
            duration_s: 1
            map: |
              #####
              #B..#
              #####
            characters:
              - seed: npc_01
                description: woman in a chef's uniform
                at: [1, 1]
                needs: { fatigue: 85 }
                asleep: true
            expect:
              - at_s: 0.1
                asleep: { who: npc_01 }
              - within_s: 1
                event: { who: npc_01, kind: fell_asleep, target: bed1 }
            """;

        var result = await Runner().RunAsync(ScenarioLoader.Parse(yaml, "t.yaml"), null);

        Assert.That(result.Passed, Is.True, string.Join("\n", result.Assertions.Select(a => $"{a.Description} -> {a.Detail}")));
    }

    private sealed class MoveEastOnce : IScenarioDriver
    {
        public int Ticks { get; private set; }

        public Task TickAsync(ScenarioRun run, CancellationToken ct)
        {
            if (Ticks++ == 0)
            {
                var a = run.Characters["npc_01"];
                run.World.Submit(SandboxWorld.Key(a.Guid), new ActionIntent(ActionVerb.Move, Direction: Compass.East, Extent: MoveExtent.OneStep));
            }

            return Task.CompletedTask;
        }
    }
}
