using System.Diagnostics;
using Cognition.Core.Perception;
using Cognition.Sandbox;
using static Cognition.Scenario.Tests.Sandbox.SandboxTestKit;

namespace Cognition.Scenario.Tests.Sandbox;

[TestFixture]
public sealed class VisibilityTests
{
    /// <summary>
    /// Independent occlusion oracle: exact segment/square clipping (Liang–Barsky) against every opaque cell
    /// interior, plus lattice corners whose two side cells are both opaque. Shares no code with <see cref="Grid"/>.
    /// </summary>
    private static bool OracleVisible(Grid g, Cell a, Cell b)
    {
        double ax = a.X + 0.5, ay = a.Y + 0.5, bx = b.X + 0.5, by = b.Y + 0.5;
        int minX = Math.Min(a.X, b.X), maxX = Math.Max(a.X, b.X), minY = Math.Min(a.Y, b.Y), maxY = Math.Max(a.Y, b.Y);
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var c = new Cell(x, y);
                if (c == a || c == b || !g.IsOpaque(c))
                    continue;
                if (CrossesInterior(ax, ay, bx, by, x, y))
                    return false;
            }
        }

        // Corners: a lattice point (px, py) strictly inside the segment where both diagonal side cells are opaque.
        double dx = bx - ax, dy = by - ay;
        for (var px = minX; px <= maxX + 1; px++)
        {
            for (var py = minY; py <= maxY + 1; py++)
            {
                var cross = (px - ax) * dy - (py - ay) * dx;
                if (Math.Abs(cross) > 1e-9)
                    continue;
                var t = Math.Abs(dx) > Math.Abs(dy) ? (px - ax) / dx : (py - ay) / dy;
                if (t <= 0 || t >= 1)
                    continue;
                var sx = Math.Sign(dx);
                var sy = Math.Sign(dy);
                if (sx == 0 || sy == 0)
                    continue;
                // Cells around the corner: before = the one the segment leaves, sides = the two it skips.
                var before = new Cell(sx > 0 ? px - 1 : px, sy > 0 ? py - 1 : py);
                var side1 = new Cell(before.X + sx, before.Y);
                var side2 = new Cell(before.X, before.Y + sy);
                if (g.IsOpaque(side1) && g.IsOpaque(side2))
                    return false;
            }
        }

        return true;
    }

    private static bool CrossesInterior(double ax, double ay, double bx, double by, int x, int y)
    {
        double t0 = 0, t1 = 1, dx = bx - ax, dy = by - ay;
        foreach (var (p, q) in new[] { (-dx, ax - x), (dx, x + 1 - ax), (-dy, ay - y), (dy, y + 1 - ay) })
        {
            if (p == 0)
            {
                if (q <= 0)
                    return false;
                continue;
            }

            var r = q / p;
            if (p < 0)
                t0 = Math.Max(t0, r);
            else
                t1 = Math.Min(t1, r);
            if (t0 >= t1)
                return false;
        }

        return t1 - t0 > 1e-9;
    }

    [Test]
    public void NoFovLeaksAcrossFiftyGeneratedLayouts()
    {
        // T1.11 acceptance, RP-01: 0 leaks.
        long checkedPairs = 0, seen = 0;
        for (var seed = 1; seed <= 50; seed++)
        {
            var world = LayoutGenerator.World(seed, Config, agents: 8, items: 40);
            foreach (var agent in world.Agents)
            {
                var p = world.GetPerception(agent.G());
                foreach (var e in p.Seen)
                {
                    var cell = new Cell((int)MathF.Floor(e.Position.X), (int)MathF.Floor(e.Position.Y));
                    Assert.That(p.Self.DistanceTo(e.Position), Is.LessThanOrEqualTo(world.PerceptionRange(agent) + 1e-4),
                        $"seed {seed}: {agent.Ref} sees {e.EntityRef} out of range");
                    Assert.That(OracleVisible(world.Grid, agent.Cell, cell), Is.True,
                        $"seed {seed}: {agent.Ref} at {agent.Cell} sees {e.EntityRef} at {cell} through a wall");
                    seen++;
                }

                foreach (var cell in world.Grid.Cells())
                {
                    checkedPairs++;
                    if (world.Grid.LineOfSight(agent.Cell, cell))
                        Assert.That(OracleVisible(world.Grid, agent.Cell, cell), Is.True, $"seed {seed}: LOS {agent.Cell}→{cell}");
                }
            }
        }

        Assert.That(seen, Is.GreaterThan(1000), "the property is vacuous if nothing is seen");
        TestContext.Out.WriteLine($"{seen} sightings, {checkedPairs} LOS pairs checked");
    }

    [Test]
    public void LineOfSightIsNotOverlyConservative()
    {
        // Every oracle-visible pair in an open room is visible to the grid too.
        var world = World("""
            ##########
            #........#
            #........#
            #...#....#
            #........#
            ##########
            """);
        foreach (var a in world.Grid.Cells().Where(world.Grid.IsWalkable))
        {
            foreach (var b in world.Grid.Cells().Where(world.Grid.IsWalkable))
            {
                Assert.That(world.Grid.LineOfSight(a, b), Is.EqualTo(OracleVisible(world.Grid, a, b)), $"{a}→{b}");
            }
        }
    }

    [Test]
    public void WallsAndClosedDoorsOcclude()
    {
        var world = World("""
            #######
            #..#..#
            #..D..#
            #..#..#
            #######
            """);
        var ana = world.Person("ana", 1, 2);
        world.AddItem(ItemKind.Food, "sandwich", new Cell(5, 2));

        Assert.That(world.GetPerception(ana.G()).Seen.Select(e => e.DisplayName), Does.Contain("door").And.Not.Contain("sandwich"));

        world.SetDoor(world.Doors.Single(), DoorState.Open);
        Assert.That(world.GetPerception(ana.G()).Seen.Select(e => e.DisplayName), Does.Contain("sandwich"));
    }

    [Test]
    public void DiagonalGapBetweenTwoWallsIsClosed()
    {
        var world = World("""
            #####
            #.#.#
            ##..#
            #####
            """);

        Assert.That(world.Grid.LineOfSight(new Cell(1, 1), new Cell(2, 2)), Is.False);
        Assert.That(world.Grid.LineOfSight(new Cell(3, 1), new Cell(2, 2)), Is.True);
    }

    [Test]
    public void ConeLimitsSightToTheFacingDirection()
    {
        var world = World("""
            #######
            #.....#
            #.....#
            #.....#
            #######
            """, new SandboxOptions { FovAngleDeg = 90 });
        var ana = world.Person("ana", 3, 2);
        world.AddItem(ItemKind.Food, "north thing", new Cell(3, 1));
        world.AddItem(ItemKind.Food, "south thing", new Cell(3, 3));
        ana.Facing = Compass.North;

        var names = world.GetPerception(ana.G()).Seen.Select(e => e.DisplayName).ToList();

        Assert.That(names, Does.Contain("north thing").And.Not.Contain("south thing"));
    }

    [Test]
    public void ItemsInClosedContainersAreHiddenAndHeldItemsShowOnThePerson()
    {
        var world = World("""
            ######
            #.F..#
            ######
            """);
        var ana = world.Person("ana", 1, 1);
        var bob = world.Person("bob", 4, 1);
        var fridge = world.Containers.Single();
        world.AddItem(ItemKind.Drink, "milk", fridge);
        world.AddItem(ItemKind.Extinguisher, "fire extinguisher", bob);

        var p = world.GetPerception(ana.G());

        Assert.That(p.Seen.Select(e => e.DisplayName), Does.Not.Contain("milk").And.Not.Contain("fire extinguisher"));
        Assert.That(p.Seen.Single(e => e.IsPerson).HeldItems, Is.EqualTo(new[] { "fire extinguisher" }));

        fridge.IsOpen = true;
        var milk = world.GetPerception(ana.G()).Seen.Single(e => e.DisplayName == "milk");
        Assert.That(milk.VisibleTraits, Is.EqualTo(new[] { "in the fridge" }));
    }

    [Test]
    public void NoveltyIsTrueOnlyTheFirstTime()
    {
        var world = World("""
            #####
            #...#
            #####
            """);
        var ana = world.Person("ana", 1, 1);
        world.AddItem(ItemKind.Tool, "wrench", new Cell(3, 1));

        Assert.That(world.GetPerception(ana.G()).Seen.Single().IsNovel, Is.True);
        Assert.That(world.GetPerception(ana.G()).Seen.Single().IsNovel, Is.False);
    }

    [Test]
    public void TwentyAgentsTickUnderOneMillisecond()
    {
        // T1.11 acceptance: 20 agents at 100 ticks/s without AI → < 1 ms per tick.
        var world = LayoutGenerator.World(7, Config, agents: 20, items: 40, options: new SandboxOptions { TickSeconds = 0.01 });
        var random = new Random(7);
        void Wander()
        {
            foreach (var a in world.Agents)
            {
                world.Submit(a.G(), new ActionIntent(ActionVerb.Move, Direction: (Compass)random.Next(8), Extent: MoveExtent.Medium));
            }
        }

        Wander();
        for (var i = 0; i < 200; i++)
        {
            world.Step();
        }

        var ticks = new List<double>();
        for (var batch = 0; batch < 10; batch++)
        {
            Wander();
            var sw = Stopwatch.StartNew();
            for (var i = 0; i < 100; i++)
            {
                world.Step();
            }

            ticks.Add(sw.Elapsed.TotalMilliseconds / 100);
        }

        ticks.Sort();
        TestContext.Out.WriteLine($"median {ticks[5]:F4} ms/tick, max {ticks[^1]:F4}");
        Assert.That(ticks[5], Is.LessThan(1.0));
    }
}
