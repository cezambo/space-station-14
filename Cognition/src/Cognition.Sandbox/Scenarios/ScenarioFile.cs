namespace Cognition.Sandbox.Scenarios;

// YAML shape of scenarios/*.yaml (see scenarios/README.md). Deserialized strictly: unknown keys are errors.
#pragma warning disable CA1002, CA2227 // DTOs for YamlDotNet

public sealed class ScenarioFile
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Requirements { get; set; } = [];
    public string Driver { get; set; } = "scripted";
    public int Seed { get; set; } = 1;
    public double DurationS { get; set; }
    public Dictionary<string, string> Options { get; set; } = [];
    public string Map { get; set; } = "";
    public List<CharacterSpec> Characters { get; set; } = [];
    public List<ItemSpec> Items { get; set; } = [];
    public List<PuddleSpec> Puddles { get; set; } = [];
    public List<StepSpec> Script { get; set; } = [];
    public List<ExpectSpec> Expect { get; set; } = [];
}

public sealed class CharacterSpec
{
    /// <summary>Id of a seed in <c>fixtures/characters</c>.</summary>
    public string Seed { get; set; } = "";

    /// <summary>What strangers see (RD-03).</summary>
    public string Description { get; set; } = "";

    public List<int> At { get; set; } = [];
    public string? Facing { get; set; }
    public Dictionary<string, float> Needs { get; set; } = [];
    public List<ItemSpec> Holding { get; set; } = [];

    /// <summary>Seed ids this character already knows by name.</summary>
    public List<string> Knows { get; set; } = [];

    public bool Asleep { get; set; }
}

public sealed class ItemSpec
{
    /// <summary>Alias used by steps and expectations; defaults to the generated ref (e.g. <c>food1</c>).</summary>
    public string? Id { get; set; }

    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public List<int>? At { get; set; }

    /// <summary>Container ref from the map, e.g. <c>fridge1</c>.</summary>
    public string? In { get; set; }

    /// <summary>For keys: the door ref they open.</summary>
    public string? Opens { get; set; }
}

public sealed class PuddleSpec
{
    public List<int> At { get; set; } = [];
    public string Reagent { get; set; } = "";
}

public sealed class StepSpec
{
    public double AtS { get; set; }
    public DoSpec? Do { get; set; }
    public SaySpec? Say { get; set; }
    public DamageSpec? Damage { get; set; }
    public DoorSpec? Door { get; set; }
    public LeakSpec? Leak { get; set; }
    public string? StopLeak { get; set; }
}

public sealed class DoSpec
{
    public string Who { get; set; } = "";
    public string Verb { get; set; } = "";
    public string? Target { get; set; }
    public string? Item { get; set; }
    public string? Destination { get; set; }
    public string? Direction { get; set; }
    public string? Extent { get; set; }
}

public sealed class SaySpec
{
    public string Who { get; set; } = "";
    public string Text { get; set; } = "";
    public string Volume { get; set; } = "normal";
    public string? To { get; set; }
}

public sealed class DamageSpec
{
    public string Who { get; set; } = "";
    public string Type { get; set; } = "Blunt";
    public float Amount { get; set; }
    public float Bleeding { get; set; }
}

public sealed class DoorSpec
{
    public string Ref { get; set; } = "";
    public string State { get; set; } = "";
}

public sealed class LeakSpec
{
    public string? Id { get; set; }
    public List<int> At { get; set; } = [];
    public float Radius { get; set; } = 5;
    public string Gas { get; set; } = "plasma";
}

/// <summary>
/// One expectation: exactly one timing (<c>within_s</c>, <c>at_s</c>, <c>at_end</c>, <c>never</c>) and exactly one check.
/// </summary>
public sealed class ExpectSpec
{
    public double? WithinS { get; set; }
    public double? AtS { get; set; }
    public bool AtEnd { get; set; }
    public bool Never { get; set; }

    public EventCheck? Event { get; set; }
    public NeedCheck? Need { get; set; }
    public HoldingCheck? Holding { get; set; }
    public NearCheck? Near { get; set; }
    public WhoValueCheck? Asleep { get; set; }
    public SeesCheck? Sees { get; set; }
    public HearsCheck? Hears { get; set; }
    public DoorSpec? Door { get; set; }
    public ExistsCheck? Exists { get; set; }
    public DamageCheck? Damage { get; set; }
}

public sealed class EventCheck
{
    public string? Who { get; set; }
    public string Kind { get; set; } = "";
    public string? Verb { get; set; }
    public string? Target { get; set; }
    public string? Item { get; set; }
    public string? Detail { get; set; }
}

public sealed class NeedCheck
{
    public string Who { get; set; } = "";
    public string Need { get; set; } = "";
    public float? Below { get; set; }
    public float? Above { get; set; }
}

public sealed class HoldingCheck
{
    public string Who { get; set; } = "";
    public string Item { get; set; } = "";
    public bool Value { get; set; } = true;
}

public sealed class NearCheck
{
    public string Who { get; set; } = "";
    public string? Of { get; set; }
    public List<int>? At { get; set; }
    public float Within { get; set; } = 1.5f;
}

public sealed class WhoValueCheck
{
    public string Who { get; set; } = "";
    public bool Value { get; set; } = true;
}

public sealed class SeesCheck
{
    public string Who { get; set; } = "";
    public string What { get; set; } = "";
    public bool Value { get; set; } = true;
}

public sealed class HearsCheck
{
    public string Who { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Value { get; set; } = true;
}

public sealed class ExistsCheck
{
    public string Item { get; set; } = "";
    public bool Value { get; set; } = true;
}

public sealed class DamageCheck
{
    public string Who { get; set; } = "";
    public float? Below { get; set; }
    public float? Above { get; set; }
}
