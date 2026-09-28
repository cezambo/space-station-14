using Cognition.Core.Perception;

namespace Cognition.Sandbox;

public enum ItemKind
{
    Food,
    Drink,
    Extinguisher,
    Medkit,
    Tool,
    Key,
}

public enum DoorState
{
    Open,
    Closed,
    Locked,
}

public enum ContainerKind
{
    Locker,
    Fridge,
}

public abstract class Entity
{
    public string Ref { get; }
    public string Name { get; internal set; }

    protected Entity(string @ref, string name)
    {
        Ref = @ref;
        Name = name;
    }
}

/// <summary>An item is on a floor/table cell, in a container, or held by an agent — exactly one of the three.</summary>
public sealed class Item : Entity
{
    public ItemKind Kind { get; }

    /// <summary>For keys: the lock id they open.</summary>
    public string? KeyId { get; }

    public Cell? Cell { get; internal set; }
    public Container? In { get; internal set; }
    public Agent? HeldBy { get; internal set; }

    internal Item(string @ref, string name, ItemKind kind, string? keyId)
        : base(@ref, name)
    {
        Kind = kind;
        KeyId = keyId;
    }
}

public sealed class Door : Entity
{
    public Cell Cell { get; }
    public DoorState State { get; internal set; }

    /// <summary>Keys with this id lock/unlock the door; null means it cannot be locked.</summary>
    public string? LockId { get; }

    internal Door(string @ref, Cell cell, DoorState state, string? lockId)
        : base(@ref, "door")
    {
        Cell = cell;
        State = state;
        LockId = lockId;
    }
}

public sealed class Bed : Entity
{
    public Cell Cell { get; }

    internal Bed(string @ref, Cell cell)
        : base(@ref, "bed")
    {
        Cell = cell;
    }
}

public sealed class Container : Entity
{
    public Cell Cell { get; }
    public ContainerKind Kind { get; }
    public bool IsOpen { get; internal set; }
    internal List<Item> Contents { get; } = [];

    public IReadOnlyList<Item> Items => Contents;

    internal Container(string @ref, string name, Cell cell, ContainerKind kind)
        : base(@ref, name)
    {
        Cell = cell;
        Kind = kind;
    }
}

/// <summary>A gas leak (T1.11): the area reachable within <see cref="Radius"/> tiles without crossing walls or closed doors.</summary>
public sealed class GasLeak : Entity
{
    public Cell Origin { get; }
    public float Radius { get; }
    public string Gas { get; }
    public bool Active { get; internal set; } = true;

    internal GasLeak(string @ref, Cell origin, float radius, string gas)
        : base(@ref, "gas leak")
    {
        Origin = origin;
        Radius = radius;
        Gas = gas;
    }
}

public sealed class Agent : Entity
{
    public const int Hands = 2;

    public Guid Guid { get; }

    /// <summary>What strangers see instead of the name (RD-03), e.g. "woman in a chef's uniform".</summary>
    public string Description { get; }

    public Cell Cell { get; internal set; }
    public Compass Facing { get; internal set; } = Compass.South;
    internal List<Item> Held { get; } = [];

    public IReadOnlyList<Item> HeldItems => Held;

    public float Hunger { get; internal set; }
    public float Thirst { get; internal set; }
    public float Fatigue { get; internal set; }
    public float Bleeding { get; internal set; }
    public float OxygenSaturation { get; internal set; } = 1f;
    internal Dictionary<string, float> Damage { get; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, float> DamageByType => Damage;
    public float TotalDamage => Damage.Values.Sum();

    public bool Asleep { get; internal set; }
    public bool Conscious => TotalDamage < UnconsciousAt;
    public double SleepStartedAt { get; internal set; }
    public float SleepStartFatigue { get; internal set; }
    public string? SleepCause { get; internal set; }
    public Bed? InBed { get; internal set; }

    internal float UnconsciousAt { get; init; } = 100f;
    internal Movement? Moving { get; set; }
    internal Queue<ActionIntent> Pending { get; } = new();
    internal List<RawSound> HeardBuffer { get; } = [];
    internal HashSet<string> SeenRefs { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, float> DamageAccrued { get; } = new(StringComparer.Ordinal);

    internal Agent(string @ref, string name, Guid guid, string description, Cell cell)
        : base(@ref, name)
    {
        Guid = guid;
        Description = description;
        Cell = cell;
    }
}

internal sealed class Movement
{
    public required Queue<Cell> Path { get; init; }
    public required ActionIntent Intent { get; init; }
    public double Progress { get; set; }
}
