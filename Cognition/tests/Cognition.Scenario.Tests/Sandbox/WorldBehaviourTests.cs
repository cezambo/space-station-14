using Cognition.Core.Perception;
using Cognition.Sandbox;
using static Cognition.Scenario.Tests.Sandbox.SandboxTestKit;

namespace Cognition.Scenario.Tests.Sandbox;

[TestFixture]
public sealed class WorldBehaviourTests
{
    private const string Room = """
        ##########
        #........#
        #........#
        #........#
        ##########
        """;

    private static WorldEvent Last(SandboxWorld w) => w.Log[^1];

    private static void Do(SandboxWorld w, Agent a, ActionIntent intent)
    {
        w.Submit(a.G(), intent);
        w.Step();
    }

    [Test]
    public void PickupEatReducesHunger()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        ana.Hunger = 70;
        var food = w.AddItem(ItemKind.Food, "sandwich", new Cell(2, 1));

        Assert.That(w.GetAffordances(ana.G()).Actions, Does.Contain(new Affordance(ActionVerb.Pickup, ItemRef: food.Ref)));
        Do(w, ana, new ActionIntent(ActionVerb.Pickup, ItemRef: food.Ref));
        Do(w, ana, new ActionIntent(ActionVerb.Eat, ItemRef: food.Ref));

        Assert.That(Last(w).Kind, Is.EqualTo(WorldEventKind.ActionCompleted));
        Assert.That(ana.Hunger, Is.LessThan(31));
        Assert.That(w.Entity(food.Ref), Is.Null, "eaten food is gone");
    }

    [Test]
    public void ImpossibleActionsAreNotOfferedAndFailWhenSubmitted()
    {
        // RJ-05
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var far = w.AddItem(ItemKind.Food, "sandwich", new Cell(8, 3));

        Assert.That(w.GetAffordances(ana.G()).Actions.Select(a => a.ItemRef), Does.Not.Contain(far.Ref));
        Do(w, ana, new ActionIntent(ActionVerb.Pickup, ItemRef: far.Ref));

        Assert.That((Last(w).Kind, Last(w).Detail), Is.EqualTo((WorldEventKind.ActionFailed, "not_possible")));
    }

    [Test]
    public void HandsHoldTwoItems()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        w.AddItem(ItemKind.Tool, "wrench", ana);
        w.AddItem(ItemKind.Tool, "crowbar", ana);
        w.AddItem(ItemKind.Food, "apple", new Cell(2, 1));

        Assert.That(w.GetAffordances(ana.G()).Actions.Where(a => a.Verb == ActionVerb.Pickup), Is.Empty);
    }

    [Test]
    public void LockedDoorNeedsItsKey()
    {
        var w = World("""
            #######
            #..L..#
            #######
            """);
        var ana = w.Person("ana", 2, 1);
        var door = w.Doors.Single();

        Assert.That(w.GetAffordances(ana.G()).Actions.Where(a => a.TargetRef == door.Ref), Is.Empty);

        var key = w.AddItem(ItemKind.Key, "red key", ana, door.LockId);
        Do(w, ana, new ActionIntent(ActionVerb.Unlock, door.Ref, key.Ref));
        Do(w, ana, new ActionIntent(ActionVerb.Open, door.Ref));
        Assert.That(door.State, Is.EqualTo(DoorState.Open));

        Do(w, ana, new ActionIntent(ActionVerb.Move, Direction: Compass.East, Extent: MoveExtent.UntilObstacle));
        w.RunFor(2);
        Assert.That(ana.Cell, Is.EqualTo(new Cell(5, 1)));
    }

    [Test]
    public void ClosedDoorBlocksAMoveInProgress()
    {
        var w = World("""
            ########
            #...O..#
            ########
            """);
        var ana = w.Person("ana", 1, 1);
        Do(w, ana, new ActionIntent(ActionVerb.Move, Direction: Compass.East, Extent: MoveExtent.UntilObstacle));
        w.SetDoor(w.Doors.Single(), DoorState.Closed);
        w.RunFor(3);

        Assert.That(ana.Cell, Is.EqualTo(new Cell(3, 1)));
        Assert.That(w.Log.Last(e => e.Kind == WorldEventKind.ActionFailed).Detail, Is.EqualTo("blocked"));
    }

    [Test]
    public void ContainersPutAndTake()
    {
        var w = World("""
            #####
            #.K.#
            #####
            """);
        var ana = w.Person("ana", 1, 1);
        var locker = w.Containers.Single();
        var kit = w.AddItem(ItemKind.Medkit, "medkit", ana);

        Assert.That(w.GetAffordances(ana.G()).Actions.Any(a => a.Verb == ActionVerb.Put), Is.False, "closed");
        Do(w, ana, new ActionIntent(ActionVerb.Open, locker.Ref));
        Do(w, ana, new ActionIntent(ActionVerb.Put, locker.Ref, kit.Ref));
        Assert.That(locker.Items, Is.EqualTo(new[] { kit }));

        Do(w, ana, new ActionIntent(ActionVerb.Take, locker.Ref, kit.Ref));
        Assert.That(ana.HeldItems, Is.EqualTo(new[] { kit }));
    }

    [Test]
    public void GiveMovesTheItemToAnotherPerson()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var bob = w.Person("bob", 2, 1);
        var drink = w.AddItem(ItemKind.Drink, "water", ana);

        Do(w, ana, new ActionIntent(ActionVerb.Give, bob.Ref, drink.Ref));

        Assert.That(bob.HeldItems, Is.EqualTo(new[] { drink }));
        Assert.That(ana.HeldItems, Is.Empty);
    }

    [Test]
    public void MedkitHealsAndStopsBleeding()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var bob = w.Person("bob", 2, 1);
        var kit = w.AddItem(ItemKind.Medkit, "medkit", ana);
        w.Damage(bob, "Slash", 30, bleeding: 2);

        Do(w, ana, new ActionIntent(ActionVerb.Use, bob.Ref, kit.Ref));

        Assert.That(bob.TotalDamage, Is.EqualTo(5).Within(0.01));
        Assert.That(bob.Bleeding, Is.Zero);
    }

    [Test]
    public void DestinationMoveWalksToABedAroundWalls()
    {
        var w = World("""
            #######
            #..#.B#
            #..#..#
            #.....#
            #######
            """);
        var ana = w.Person("ana", 1, 1);
        var bed = w.Beds.Single();
        ana.SeenRefs.Add(bed.Ref);

        Assert.That(w.GetAffordances(ana.G()).Destinations.Select(d => d.Ref), Does.Contain(bed.Ref));
        Do(w, ana, new ActionIntent(ActionVerb.Move, DestinationRef: bed.Ref));
        w.RunFor(5);

        Assert.That(ana.Cell, Is.EqualTo(bed.Cell));
        Assert.That(w.Log.Last().Kind, Is.EqualTo(WorldEventKind.ActionCompleted));
    }

    // RP-02: normal 10 tiles, whisper 2, shout 20; −60 % per wall.
    [TestCase(SpeechVolume.Normal, 9, true)]
    [TestCase(SpeechVolume.Normal, 11, false)]
    [TestCase(SpeechVolume.Whisper, 2, true)]
    [TestCase(SpeechVolume.Whisper, 3, false)]
    [TestCase(SpeechVolume.Shout, 19, true)]
    public void HearingRangeByVolume(SpeechVolume volume, int distance, bool heard)
    {
        var w = World("#" + new string('.', 24) + "#\n");
        var ana = w.Person("ana", 1, 0);
        var bob = w.Person("bob", 1 + distance, 0);

        Do(w, ana, new ActionIntent(ActionVerb.Speak, Text: "Hello", Volume: volume));

        Assert.That(w.GetPerception(bob.G()).Heard.Any(), Is.EqualTo(heard));
    }

    [Test]
    public void WallsAttenuateSpeech()
    {
        var w = World("""
            ##############
            #.....#......#
            ##############
            """);
        var ana = w.Person("ana", 5, 1);
        var near = w.Person("bob", 8, 1);
        var far = w.Person("cat", 12, 1);

        Do(w, ana, new ActionIntent(ActionVerb.Speak, near.Ref, Text: "Can you hear me?"));

        var heard = w.GetPerception(near.G()).Heard.Single();
        Assert.That((heard.WallsBetween, heard.Loudness), Is.EqualTo((1, 4f)).Using<(int, float)>((x, y) =>
            x.Item1 == y.Item1 && Math.Abs(x.Item2 - y.Item2) < 1e-4 ? 0 : 1));
        Assert.That(heard.AddresseeGuid, Is.EqualTo(near.G()));
        Assert.That(heard.SpeakerDescription, Is.EqualTo("person called ana"));
        Assert.That(w.GetPerception(far.G()).Heard, Is.Empty, "7 tiles through a wall > 4");
    }

    [Test]
    public void HeardSoundsAreDeliveredOnce()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var bob = w.Person("bob", 3, 1);
        Do(w, ana, new ActionIntent(ActionVerb.Speak, Text: "Hi"));

        Assert.That(w.GetPerception(bob.G()).Heard, Has.Count.EqualTo(1));
        Assert.That(w.GetPerception(bob.G()).Heard, Is.Empty);
    }

    [Test]
    public void ShoutWakesASleeperWhoHearsNothing()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var bob = w.Person("bob", 5, 1);
        bob.Fatigue = 50;
        Do(w, bob, new ActionIntent(ActionVerb.Sleep));
        Do(w, ana, new ActionIntent(ActionVerb.Speak, Text: "psst", Volume: SpeechVolume.Normal));
        Assert.That(bob.Asleep, Is.True);
        Assert.That(w.GetPerception(bob.G()).Heard, Is.Empty);
        Assert.That(w.GetPerception(bob.G()).Seen, Is.Empty, "sleepers see nothing");

        Do(w, ana, new ActionIntent(ActionVerb.Speak, Text: "FIRE!", Volume: SpeechVolume.Shout));

        Assert.That(bob.Asleep, Is.False);
        Assert.That(w.Log.Last(e => e.Kind == WorldEventKind.Woke).Detail, Is.EqualTo("noise"));
    }

    [Test]
    public void FatigueRisesToAboutEightyOverTWake()
    {
        // RS-01, RS-04: fatigue_at_t_wake (80) after T_wake minutes idle.
        var w = World(Room, new SandboxOptions { TickSeconds = 1 });
        var ana = w.Person("ana", 1, 1);

        w.RunFor(Config.Sleep.TWakeMinutes * 60);

        Assert.That(ana.Fatigue, Is.EqualTo(80).Within(0.5));
        Assert.That(ana.Asleep, Is.False);
    }

    [Test]
    public void BedSleepRecoversFasterAndReportsRawSleepFacts()
    {
        // RS-03, RS-05: the world reports cause, start fatigue and duration; Core decides if the day ends.
        var w = World("""
            #####
            #B..#
            #####
            """, new SandboxOptions { TickSeconds = 1 });
        var inBed = w.Person("ana", 1, 1);
        var onFloor = w.Person("bob", 3, 1);
        inBed.Fatigue = onFloor.Fatigue = 60;
        w.Submit(inBed.G(), new ActionIntent(ActionVerb.Sleep, w.Beds.Single().Ref));
        w.Submit(onFloor.G(), new ActionIntent(ActionVerb.Sleep));
        w.RunFor(60);

        Assert.That(inBed.Fatigue, Is.LessThan(onFloor.Fatigue));
        var asleep = w.Log.First(e => e.Kind == WorldEventKind.FellAsleep && e.AgentGuid == inBed.G());
        Assert.That((asleep.Detail, asleep.Amount, asleep.TargetRef), Is.EqualTo(("voluntary", (float?)60f, "bed1")));

        w.RunFor(Config.Sleep.FullSleepMinutes * 60);
        var woke = w.Log.First(e => e.Kind == WorldEventKind.Woke && e.AgentGuid == inBed.G());
        Assert.That(woke.Detail, Is.EqualTo("rested"));
        Assert.That(woke.Amount, Is.GreaterThanOrEqualTo(Config.Sleep.MinConsolidatedSeconds));
    }

    [Test]
    public void CollapseAtOneHundred()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        ana.Fatigue = 99.999f;
        w.RunFor(1);

        Assert.That(w.Log.Select(e => e.Kind), Does.Contain(WorldEventKind.Collapsed));
        Assert.That(w.Log.First(e => e.Kind == WorldEventKind.FellAsleep).Detail, Is.EqualTo("collapse"));
    }

    [Test]
    public void CriticalFatigueDozesAndWakesAfterADoze()
    {
        // RS-02: chance of involuntary doze at 95+; RS-06: naps do not rest fully.
        var w = World(Room, new SandboxOptions { CriticalDozeChancePerSecond = 1000, TickSeconds = 1 });
        var ana = w.Person("ana", 1, 1);
        ana.Fatigue = 96;
        w.RunFor(1);
        Assert.That(w.Log.Single(e => e.Kind == WorldEventKind.FellAsleep).Detail, Is.EqualTo("involuntary"));

        w.RunFor(w.Options.DozeSeconds + 1);
        var woke = w.Log.Single(e => e.Kind == WorldEventKind.Woke);
        Assert.That(woke.Detail, Is.EqualTo("doze_over"));
        Assert.That(ana.Fatigue, Is.GreaterThan(50));
    }

    [Test]
    public void StrongFatigueSlowsAndNarrowsPerception()
    {
        // RS-02: −15 % speed and −20 % perception at 80+; −30 % / −40 % at 95+.
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        ana.Fatigue = 85;
        Assert.That((w.SpeedTilesPerSecond(ana), w.PerceptionRange(ana)), Is.EqualTo((3.4f, 12.8f)));
        ana.Fatigue = 96;
        Assert.That(w.PerceptionRange(ana), Is.EqualTo(9.6f).Within(1e-4));
    }

    [Test]
    public void GasLeakThinsTheAirAndSuffocates()
    {
        var w = World("""
            ###########
            #....#....#
            #....D....#
            #....#....#
            ###########
            """, new SandboxOptions { TickSeconds = 1 });
        var ana = w.Person("ana", 2, 2);
        var bob = w.Person("bob", 8, 2);
        w.AddLeak(new Cell(2, 1), radius: 10);

        var env = w.GetPerception(ana.G()).Env;
        Assert.That(env.PressureKPa, Is.EqualTo(35f));
        Assert.That(env.GasFractions["plasma"], Is.GreaterThan(0));
        var sensations = Categorizers.Environment(env, Config.Perception.Environment, new HashSet<string> { "plasma" });
        Assert.That(sensations.Select(s => s.Kind), Is.EqualTo(new[] { SensationKind.ThinAir, SensationKind.Gas }));
        Assert.That(w.GetPerception(bob.G()).Env.PressureKPa, Is.EqualTo(101.3f), "the closed door holds the gas");

        w.RunFor(30);
        Assert.That(ana.OxygenSaturation, Is.LessThan(0.85f));
        Assert.That(ana.DamageByType.ContainsKey("Asphyxiation"), Is.True);
        Assert.That(w.Log.Any(e => e.Kind == WorldEventKind.Damaged && e.Detail == "Asphyxiation"), Is.True);
        Assert.That(bob.OxygenSaturation, Is.EqualTo(1f));
    }

    [Test]
    public void VisiblePuddlesAreHazards()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        w.AddPuddle(new Cell(4, 2), "blood");

        Assert.That(w.GetPerception(ana.G()).Env.Hazards, Is.EqualTo(new[] { HazardKeys.Puddle("blood") }));
    }

    [Test]
    public void SameSeedSameHistory()
    {
        // P2: a single seed for all randomness; identical inputs give identical event logs.
        string Run()
        {
            var w = LayoutGenerator.World(11, Config, agents: 6, items: 20,
                options: new SandboxOptions { CriticalDozeChancePerSecond = 0.5f, TickSeconds = 0.5 });
            foreach (var a in w.Agents)
            {
                a.Fatigue = 96;
            }

            var random = new Random(3);
            for (var i = 0; i < 400; i++)
            {
                var a = w.Agents[random.Next(w.Agents.Count)];
                w.Submit(a.G(), new ActionIntent(ActionVerb.Move, Direction: (Compass)random.Next(8), Extent: MoveExtent.Short));
                w.Step();
            }

            return string.Join("\n", w.Log.Select(e => e.ToString()));
        }

        var first = Run();
        Assert.That(first, Does.Contain("FellAsleep"));
        Assert.That(Run(), Is.EqualTo(first));
    }

    [Test]
    public void EventsArePublishedToSubscribers()
    {
        var w = World(Room);
        var ana = w.Person("ana", 1, 1);
        var received = new List<WorldEvent>();
        using var sub = w.Events.Subscribe(new Collector(received));

        Do(w, ana, new ActionIntent(ActionVerb.Speak, Text: "hi"));

        Assert.That(received.Select(e => e.Kind), Is.EqualTo(new[] { WorldEventKind.Spoke, WorldEventKind.ActionCompleted }));
    }

    private sealed class Collector(List<WorldEvent> into) : IObserver<WorldEvent>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(WorldEvent value) => into.Add(value);
    }
}
