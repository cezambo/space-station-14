using System.Globalization;
using Cognition.Core.Config;
using Cognition.Core.Perception;

namespace Cognition.Sandbox;

/// <summary>
/// Grid world for tests (T1.11, P4). Deterministic: a fixed tick, one seeded <see cref="Random"/>, and agents,
/// entities and queued intents processed in insertion order. Provides raw facts only (RA-06).
/// </summary>
public sealed partial class SandboxWorld : IWorldAdapter
{
    private static readonly (Compass Dir, int Dx, int Dy)[] Directions =
    [
        (Compass.North, 0, -1), (Compass.NorthEast, 1, -1), (Compass.East, 1, 0), (Compass.SouthEast, 1, 1),
        (Compass.South, 0, 1), (Compass.SouthWest, -1, 1), (Compass.West, -1, 0), (Compass.NorthWest, -1, -1),
    ];

    private readonly CognitionConfig _config;
    private readonly Random _random;
    private readonly EventStream _events = new();
    private readonly List<WorldEvent> _log = [];
    private readonly Dictionary<string, Entity> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _refCounters = new(StringComparer.Ordinal);
    private readonly List<Agent> _agents = [];
    private readonly Dictionary<string, Agent> _byGuid = new(StringComparer.Ordinal);
    private readonly List<Item> _items = [];
    private readonly Dictionary<Cell, Door> _doors = [];
    private readonly Dictionary<Cell, Bed> _beds = [];
    private readonly Dictionary<Cell, Container> _containers = [];
    private readonly List<GasLeak> _leaks = [];
    private readonly Dictionary<Cell, string> _puddles = [];
    private HashSet<Cell>? _leakArea;

    public SandboxOptions Options { get; }
    public Grid Grid { get; }
    public GameClock Clock { get; }
    public IObservable<WorldEvent> Events => _events;
    public IReadOnlyList<WorldEvent> Log => _log;
    public IReadOnlyList<Agent> Agents => _agents;
    public IReadOnlyList<Item> Items => _items;
    public IEnumerable<Door> Doors => _doors.Values;
    public IEnumerable<Bed> Beds => _beds.Values;
    public IEnumerable<Container> Containers => _containers.Values;

    public SandboxWorld(Grid grid, CognitionConfig config, SandboxOptions? options = null)
    {
        Grid = grid;
        _config = config;
        Options = options ?? new SandboxOptions();
        _random = new Random(Options.Seed);
        Clock = new GameClock(Options.StationStart);
    }

    /// <summary>
    /// Legend: <c>#</c> or space wall, <c>.</c> floor, <c>T</c> table, <c>D</c>/<c>O</c>/<c>L</c> closed/open/locked
    /// door, <c>B</c> bed, <c>F</c> fridge, <c>K</c> locker. Refs are numbered in reading order (<c>door1</c>, …).
    /// </summary>
    public static SandboxWorld FromMap(string map, CognitionConfig config, SandboxOptions? options = null)
    {
        var lines = map.ReplaceLineEndings("\n").Trim('\n').Split('\n');
        var grid = new Grid(lines.Max(l => l.Length), lines.Length);
        var pending = new List<(Cell Cell, char C)>();
        for (var y = 0; y < lines.Length; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                var c = x < lines[y].Length ? lines[y][x] : ' ';
                var cell = new Cell(x, y);
                grid[cell] = c switch
                {
                    '#' or ' ' => Tile.Wall,
                    'T' => Tile.Table,
                    '.' or 'D' or 'O' or 'L' or 'B' or 'F' or 'K' => Tile.Floor,
                    _ => throw new FormatException($"map ({x},{y}): unknown tile '{c}'"),
                };
                if (c is 'D' or 'O' or 'L' or 'B' or 'F' or 'K')
                    pending.Add((cell, c));
            }
        }

        var world = new SandboxWorld(grid, config, options);
        foreach (var (cell, c) in pending)
        {
            switch (c)
            {
                case 'D':
                    world.AddDoor(cell, DoorState.Closed);
                    break;
                case 'O':
                    world.AddDoor(cell, DoorState.Open);
                    break;
                case 'L':
                    world.AddDoor(cell, DoorState.Locked);
                    break;
                case 'B':
                    world.AddBed(cell);
                    break;
                case 'F':
                    world.AddContainer(cell, ContainerKind.Fridge, "fridge");
                    break;
                case 'K':
                    world.AddContainer(cell, ContainerKind.Locker, "locker");
                    break;
            }
        }

        return world;
    }

    // ---------------------------------------------------------------- building

    private string NextRef(string prefix)
    {
        _refCounters[prefix] = _refCounters.GetValueOrDefault(prefix) + 1;
        return prefix + _refCounters[prefix].ToString(CultureInfo.InvariantCulture);
    }

    private T Register<T>(T e)
        where T : Entity
    {
        if (!_entities.TryAdd(e.Ref, e))
            throw new ArgumentException($"ref '{e.Ref}' already exists");
        return e;
    }

    public Door AddDoor(Cell cell, DoorState state, string? lockId = null)
    {
        var r = NextRef("door");
        var door = Register(new Door(r, cell, state, lockId ?? r));
        _doors.Add(cell, door);
        RefreshCell(cell);
        return door;
    }

    public Bed AddBed(Cell cell)
    {
        var bed = Register(new Bed(NextRef("bed"), cell));
        _beds.Add(cell, bed);
        return bed;
    }

    public Container AddContainer(Cell cell, ContainerKind kind, string name)
    {
        var c = Register(new Container(NextRef(kind.ToString().ToLowerInvariant()), name, cell, kind));
        _containers.Add(cell, c);
        RefreshCell(cell);
        return c;
    }

    public Agent AddAgent(string id, string name, Guid guid, string description, Cell cell)
    {
        if (!Grid.IsWalkable(cell))
            throw new ArgumentException($"agent '{id}' placed on a blocked cell {cell}");
        var agent = Register(new Agent(id, name, guid, description, cell) { UnconsciousAt = Options.UnconsciousDamage });
        _agents.Add(agent);
        _byGuid.Add(Key(guid), agent);
        return agent;
    }

    private Item NewItem(ItemKind kind, string name, string? keyId)
    {
        var item = Register(new Item(NextRef(kind.ToString().ToLowerInvariant()), name, kind, keyId));
        _items.Add(item);
        return item;
    }

    public Item AddItem(ItemKind kind, string name, Cell cell, string? keyId = null)
    {
        if (Grid[cell] == Tile.Wall)
            throw new ArgumentException($"item '{name}' placed in a wall {cell}");
        var item = NewItem(kind, name, keyId);
        item.Cell = cell;
        return item;
    }

    public Item AddItem(ItemKind kind, string name, Container container, string? keyId = null)
    {
        var item = NewItem(kind, name, keyId);
        item.In = container;
        container.Contents.Add(item);
        return item;
    }

    public Item AddItem(ItemKind kind, string name, Agent holder, string? keyId = null)
    {
        if (holder.Held.Count >= Agent.Hands)
            throw new ArgumentException($"'{holder.Ref}' has no free hand");
        var item = NewItem(kind, name, keyId);
        item.HeldBy = holder;
        holder.Held.Add(item);
        return item;
    }

    public Item AddKey(string name, Door door, Cell cell) => AddItem(ItemKind.Key, name, cell, door.LockId);

    public GasLeak AddLeak(Cell origin, float radius, string gas = "plasma")
    {
        var leak = Register(new GasLeak(NextRef("leak"), origin, radius, gas));
        _leaks.Add(leak);
        _leakArea = null;
        return leak;
    }

    public void StopLeak(GasLeak leak)
    {
        leak.Active = false;
        _leakArea = null;
    }

    public void AddPuddle(Cell cell, string reagent) => _puddles[cell] = reagent;

    public void SetDoor(Door door, DoorState state)
    {
        door.State = state;
        RefreshCell(door.Cell);
    }

    /// <summary>A discrete hit (scenario scripted); wakes sleepers when large enough.</summary>
    public void Damage(Agent agent, string type, float amount, float bleeding = 0)
    {
        var wasConscious = agent.Conscious;
        agent.Damage[type] = agent.Damage.GetValueOrDefault(type) + amount;
        agent.Bleeding += bleeding;
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.Damaged, Key(agent.Guid), Detail: type, Amount: amount));
        if (agent.Asleep && amount >= Options.WakeOnDamage)
            WakeUp(agent, "damage");
        CheckConsciousness(agent, wasConscious);
    }

    public Entity? Entity(string @ref) => _entities.GetValueOrDefault(@ref);

    public Agent AgentByGuid(string guid) =>
        _byGuid.TryGetValue(guid, out var a) ? a : throw new KeyNotFoundException($"no agent {guid}");

    public static string Key(Guid g) => g.ToString("D", CultureInfo.InvariantCulture);

    private void RefreshCell(Cell cell)
    {
        var door = _doors.GetValueOrDefault(cell);
        var closedDoor = door is not null && door.State != DoorState.Open;
        Grid.SetOverlay(cell, opaque: closedDoor, blocked: closedDoor || _containers.ContainsKey(cell));
        _leakArea = null;
    }

    // ---------------------------------------------------------------- ticking

    public void Step()
    {
        var dt = Options.TickSeconds;
        Clock.Advance(dt);
        foreach (var a in _agents)
        {
            while (a.Pending.TryDequeue(out var intent))
            {
                Start(a, intent);
            }
        }

        foreach (var a in _agents)
        {
            Advance(a, dt);
        }

        foreach (var a in _agents)
        {
            Body(a, dt);
        }
    }

    public void RunFor(double gameSeconds)
    {
        var ticks = (int)Math.Round(gameSeconds / Options.TickSeconds);
        for (var i = 0; i < ticks; i++)
        {
            Step();
        }
    }

    public void Submit(string agentGuid, ActionIntent intent) => AgentByGuid(agentGuid).Pending.Enqueue(intent);

    private void Emit(WorldEvent e)
    {
        _log.Add(e);
        _events.Publish(e);
    }

    private void Completed(Agent a, ActionIntent i) =>
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.ActionCompleted, Key(a.Guid), i.Verb, i.TargetRef, i.ItemRef));

    private void Failed(Agent a, ActionIntent i, string reason) =>
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.ActionFailed, Key(a.Guid), i.Verb, i.TargetRef, i.ItemRef, reason));

    private float FatigueFactor(Agent a, float strong, float critical)
    {
        var bounds = _config.Needs.Bands["fatigue"];
        if (a.Fatigue >= bounds[2])
            return critical;
        return a.Fatigue >= bounds[1] ? strong : 1f;
    }

    /// <summary>RS-02: −15 % / −30 % speed in the strong / critical bands.</summary>
    public float SpeedTilesPerSecond(Agent a) => Options.WalkTilesPerSecond * FatigueFactor(a, 0.85f, 0.70f);

    /// <summary>RS-02: −20 % / −40 % perception range in the strong / critical bands.</summary>
    public float PerceptionRange(Agent a) => Options.FovRangeTiles * FatigueFactor(a, 0.80f, 0.60f);

    private void Advance(Agent a, double dt)
    {
        var m = a.Moving;
        if (m is null)
            return;
        m.Progress += SpeedTilesPerSecond(a) * dt;
        while (m.Path.Count > 0)
        {
            var next = m.Path.Peek();
            var cost = next.X != a.Cell.X && next.Y != a.Cell.Y ? Math.Sqrt(2) : 1.0;
            if (m.Progress < cost)
                return;
            if (!CanStep(a.Cell, next))
            {
                a.Moving = null;
                Failed(a, m.Intent, "blocked");
                return;
            }

            m.Progress -= cost;
            a.Facing = DirectionOf(next.X - a.Cell.X, next.Y - a.Cell.Y);
            a.Cell = next;
            m.Path.Dequeue();
        }

        a.Moving = null;
        Completed(a, m.Intent);
    }

    private void Body(Agent a, double dt)
    {
        var wasConscious = a.Conscious;
        var minutes = (float)(dt / 60.0);
        a.Hunger = Math.Min(100f, a.Hunger + Options.HungerPerMinute * minutes);
        a.Thirst = Math.Min(100f, a.Thirst + Options.ThirstPerMinute * minutes);

        var tWakeSeconds = _config.Sleep.TWakeMinutes * 60.0;
        if (a.Asleep)
        {
            var fullSleepSeconds = _config.Sleep.FullSleepMinutes * 60.0;
            var rate = 100.0 / fullSleepSeconds * (a.InBed is null ? 1.0 / _config.Sleep.BedRecoveryMultiplier : 1.0);
            a.Fatigue = (float)Math.Max(0, a.Fatigue - rate * dt);
            var slept = Clock.Seconds - a.SleepStartedAt;
            if (a.Fatigue <= 0)
                WakeUp(a, "rested");
            else if (a.SleepCause == "involuntary" && slept >= Options.DozeSeconds)
                WakeUp(a, "doze_over");
        }
        else if (a.Conscious)
        {
            var rate = Options.FatigueAtTWake / tWakeSeconds
                * (a.Moving is null ? 1.0 : Options.ExertionFatigueMultiplier)
                * (1.0 + Options.InjuryFatiguePerDamage * a.TotalDamage);
            a.Fatigue = (float)Math.Min(100, a.Fatigue + rate * dt);
            if (a.Fatigue >= 100)
            {
                Emit(new WorldEvent(Clock.Seconds, WorldEventKind.Collapsed, Key(a.Guid), Amount: a.Fatigue));
                FallAsleep(a, "collapse");
            }
            else if (a.Fatigue >= _config.Needs.Bands["fatigue"][2]
                     && _random.NextDouble() < Options.CriticalDozeChancePerSecond * dt)
            {
                FallAsleep(a, "involuntary");
            }
        }

        if (a.Bleeding > 0)
        {
            Accrue(a, "Bloodloss", (float)(a.Bleeding * Options.BleedDamagePerSecond * dt));
            a.Bleeding = (float)Math.Max(0, a.Bleeding - Options.BleedDecayPerSecond * dt);
        }

        var inLeak = LeakArea().Contains(a.Cell);
        a.OxygenSaturation = (float)Math.Clamp(
            a.OxygenSaturation + (inLeak ? -Options.SaturationDropPerSecond : Options.SaturationRecoverPerSecond) * dt, 0, 1);
        if (a.OxygenSaturation < Options.SuffocationBelowSaturation)
            Accrue(a, "Asphyxiation", (float)(Options.SuffocationDamagePerSecond * dt));

        CheckConsciousness(a, wasConscious);
    }

    private void Accrue(Agent a, string type, float amount)
    {
        a.Damage[type] = a.Damage.GetValueOrDefault(type) + amount;
        var total = a.DamageAccrued.GetValueOrDefault(type) + amount;
        if (total >= Options.DamageEventStep)
        {
            Emit(new WorldEvent(Clock.Seconds, WorldEventKind.Damaged, Key(a.Guid), Detail: type, Amount: total));
            total = 0;
        }

        a.DamageAccrued[type] = total;
    }

    private void CheckConsciousness(Agent a, bool wasConscious)
    {
        if (!wasConscious || a.Conscious)
            return;
        a.Moving = null;
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.LostConsciousness, Key(a.Guid), Amount: a.TotalDamage));
    }

    private void FallAsleep(Agent a, string cause)
    {
        a.Asleep = true;
        a.Moving = null;
        a.SleepStartedAt = Clock.Seconds;
        a.SleepStartFatigue = a.Fatigue;
        a.SleepCause = cause;
        a.InBed = _beds.GetValueOrDefault(a.Cell);
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.FellAsleep, Key(a.Guid), TargetRef: a.InBed?.Ref, Detail: cause,
            Amount: a.Fatigue));
    }

    private void WakeUp(Agent a, string reason)
    {
        var slept = (float)(Clock.Seconds - a.SleepStartedAt);
        a.Asleep = false;
        a.InBed = null;
        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.Woke, Key(a.Guid), Detail: reason, Amount: slept));
    }

    // ---------------------------------------------------------------- geometry helpers

    private static Compass DirectionOf(int dx, int dy) => Categorizers.Direction(dx, dy) ?? Compass.South;

    private static Vec2 Center(Cell c) => new(c.X + 0.5f, c.Y + 0.5f);

    private static float Distance(Cell a, Cell b) => Center(a).DistanceTo(Center(b));

    private bool CanStep(Cell from, Cell to)
    {
        if (!Grid.IsWalkable(to))
            return false;
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        return dx == 0 || dy == 0 || (Grid.IsWalkable(from.Offset(dx, 0)) && Grid.IsWalkable(from.Offset(0, dy)));
    }

    /// <summary>Breadth-first path (8 directions, no corner cutting) to the nearest cell satisfying <paramref name="goal"/>.</summary>
    private Queue<Cell>? PathTo(Cell start, Func<Cell, bool> goal)
    {
        if (goal(start))
            return new Queue<Cell>();
        var previous = new Dictionary<Cell, Cell> { [start] = start };
        var frontier = new Queue<Cell>();
        frontier.Enqueue(start);
        while (frontier.TryDequeue(out var c))
        {
            foreach (var (_, dx, dy) in Directions)
            {
                var n = c.Offset(dx, dy);
                if (previous.ContainsKey(n) || !CanStep(c, n))
                    continue;
                previous[n] = c;
                if (goal(n))
                {
                    var path = new Stack<Cell>();
                    for (var p = n; p != start; p = previous[p])
                    {
                        path.Push(p);
                    }

                    return new Queue<Cell>(path);
                }

                frontier.Enqueue(n);
            }
        }

        return null;
    }

    private HashSet<Cell> LeakArea()
    {
        if (_leakArea is not null)
            return _leakArea;
        var area = new HashSet<Cell>();
        foreach (var leak in _leaks.Where(l => l.Active))
        {
            var frontier = new Queue<Cell>();
            frontier.Enqueue(leak.Origin);
            area.Add(leak.Origin);
            while (frontier.TryDequeue(out var c))
            {
                foreach (var (_, dx, dy) in Directions.Where(d => d.Dx == 0 || d.Dy == 0))
                {
                    var n = c.Offset(dx, dy);
                    if (Grid.IsOpaque(n) || Distance(leak.Origin, n) > leak.Radius || !area.Add(n))
                        continue;
                    frontier.Enqueue(n);
                }
            }
        }

        _leakArea = area;
        return area;
    }

    private sealed class EventStream : IObservable<WorldEvent>
    {
        private readonly List<IObserver<WorldEvent>> _observers = [];

        public IDisposable Subscribe(IObserver<WorldEvent> observer)
        {
            _observers.Add(observer);
            return new Unsubscriber(() => _observers.Remove(observer));
        }

        public void Publish(WorldEvent e)
        {
            foreach (var o in _observers.ToList())
            {
                o.OnNext(e);
            }
        }

        private sealed class Unsubscriber(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }
}
