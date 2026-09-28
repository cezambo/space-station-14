using System.Diagnostics;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Sandbox;
using static Cognition.Scenario.Tests.Sandbox.SandboxTestKit;

namespace Cognition.Scenario.Tests.Sandbox;

/// <summary>§7 metrics (T1.12 acceptance) measured over generated layouts.</summary>
[TestFixture]
public sealed class PerceptionMetricsTests
{
    private static PerceptionFormatter NewFormatter()
    {
        var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Cognition.sln")))
        {
            root = root.Parent!;
        }

        return new PerceptionFormatter(Config, Vocabulary.Load(Path.Combine(root.FullName, "prompts")));
    }

    private static readonly PerceptionContext NoOne = new(new Dictionary<string, string>(), ["Find something to eat"]);

    private static Cell CellOf(SandboxWorld w, string @ref) => w.Entity(@ref) switch
    {
        Agent a => a.Cell,
        Door d => d.Cell,
        Bed b => b.Cell,
        Container c => c.Cell,
        Item { Cell: { } c } => c,
        Item { In: { } box } => box.Cell,
        _ => throw new InvalidOperationException(@ref),
    };

    /// <summary>Ground truth from integer cells with independent arithmetic (degrees, clockwise from north = −Y).</summary>
    private static (DistanceBand, Compass?) Truth(Cell from, Cell to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        var d = Math.Sqrt(dx * dx + dy * dy);
        var band = d <= 1.5 ? DistanceBand.WithinReach : d <= 5 ? DistanceBand.Near : d <= 12 ? DistanceBand.Medium : DistanceBand.Far;
        if (dx == 0 && dy == 0)
            return (band, null);
        var deg = (Math.Atan2(dx, -dy) * 180 / Math.PI + 360) % 360;
        return (band, (Compass)((int)((deg + 22.5) / 45) % 8));
    }

    [Test]
    public void DirectionAndDistanceMatchGroundTruth()
    {
        // §7: ≥ 98 % each; the formatter should be exact.
        var formatter = NewFormatter();
        int total = 0, distanceOk = 0, directionOk = 0;
        for (var seed = 1; seed <= 50; seed++)
        {
            var w = LayoutGenerator.World(seed, Config, agents: 8, items: 40);
            foreach (var a in w.Agents)
            {
                var block = formatter.Format(w.GetPerception(a.G()), NoOne);
                foreach (var line in block.Seen)
                {
                    var (band, dir) = Truth(a.Cell, CellOf(w, line.Ref));
                    total++;
                    distanceOk += line.Distance == band ? 1 : 0;
                    directionOk += line.Direction == dir ? 1 : 0;
                }
            }
        }

        Assert.That(total, Is.GreaterThan(500));
        Assert.That((distanceOk, directionOk), Is.EqualTo((total, total)));
    }

    [Test]
    public void MeanBlockIsUnder900Tokens()
    {
        // §7: mean perception block ≤ 900 tokens (chars / chars_per_token_initial).
        var formatter = NewFormatter();
        var sizes = new List<double>();
        for (var seed = 1; seed <= 20; seed++)
        {
            var w = LayoutGenerator.World(seed, Config, agents: 20, items: 80);
            foreach (var a in w.Agents)
            {
                sizes.Add(formatter.Format(w.GetPerception(a.G()), NoOne).Text.Length / Config.Context.CharsPerTokenInitial);
            }
        }

        TestContext.Out.WriteLine($"mean {sizes.Average():F0} tokens, max {sizes.Max():F0}");
        Assert.That(sizes.Average(), Is.LessThanOrEqualTo(900));
        Assert.That(sizes.Max(), Is.LessThanOrEqualTo(900), "caps keep even the busiest block small");
    }

    [Test]
    public void SnapshotCostUnderPointThreeMsPerCharacter()
    {
        // §7: snapshot (perception + formatting) ≤ 0.3 ms per character.
        var formatter = NewFormatter();
        var w = LayoutGenerator.World(3, Config, agents: 20, items: 80);
        foreach (var a in w.Agents)
        {
            formatter.Format(w.GetPerception(a.G()), NoOne);
        }

        var runs = new List<double>();
        for (var r = 0; r < 7; r++)
        {
            var sw = Stopwatch.StartNew();
            foreach (var a in w.Agents)
            {
                formatter.Format(w.GetPerception(a.G()), NoOne);
            }

            runs.Add(sw.Elapsed.TotalMilliseconds / w.Agents.Count);
        }

        runs.Sort();
        TestContext.Out.WriteLine($"median {runs[3]:F4} ms per character");
        Assert.That(runs[3], Is.LessThan(0.3));
    }

    [Test]
    public void AcquaintancesAreNamedAndStrangersAreNot()
    {
        var w = World("""
            #######
            #.....#
            #######
            """);
        var ana = w.Person("ana", 1, 1);
        var bob = w.Person("bob", 3, 1);
        var cat = w.Person("cat", 5, 1);
        var ctx = new PerceptionContext(new Dictionary<string, string> { [bob.G()] = "Bob" }, []);

        var text = NewFormatter().Format(w.GetPerception(ana.G()), ctx).Text;

        Assert.That(text, Does.Contain("Bob (known) — near, east"));
        Assert.That(text, Does.Contain("person called cat — near, east").And.Not.Contain("Cat"));
        _ = cat;
    }
}
