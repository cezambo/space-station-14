namespace Cognition.Core.Perception;

/// <summary>Tile coordinates; +X east, +Y south (north is −Y, screen up).</summary>
public readonly record struct Vec2(float X, float Y)
{
    public float DistanceTo(Vec2 o) => MathF.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

public enum SpeechVolume
{
    Whisper,
    Normal,
    Shout,
}

/// <summary>
/// Something the agent sees (RP-01). Persons carry <see cref="StableGuid"/>; Core decides whether to show a
/// name or <see cref="DisplayName"/> (a visible description) using the acquaintance registry (RD-03).
/// <see cref="IsItem"/> marks things that can be carried; they have their own cap (RP-04).
/// </summary>
public sealed record RawPerceivedEntity(
    string EntityRef,
    string DisplayName,
    bool IsPerson,
    string? StableGuid,
    Vec2 Position,
    IReadOnlyList<string> VisibleTraits,
    IReadOnlyList<string> HeldItems,
    bool IsNovel,
    bool IsItem = false);

/// <summary>
/// RP-02/RP-03. <see cref="Loudness"/> is the hearing range in tiles left after wall attenuation.
/// <see cref="AddresseeGuid"/> is set when the speaker addressed someone (the RJ-01 "addressed" trigger).
/// </summary>
public sealed record RawSound(
    string Kind,
    Vec2 Origin,
    float Loudness,
    int WallsBetween,
    string? SpeakerGuid,
    string? SpeakerDescription,
    string? Text,
    SpeechVolume? Volume,
    string? AddresseeGuid = null);

/// <summary>
/// RP-06 raw atmosphere at the agent's tile. <see cref="Hazards"/> uses <see cref="HazardKeys"/>.
/// </summary>
public sealed record RawEnvironment(
    float PressureKPa,
    float TemperatureK,
    IReadOnlyDictionary<string, float> GasFractions,
    IReadOnlyList<string> Hazards);

public static class HazardKeys
{
    public const string Fire = "fire";
    public const string PuddlePrefix = "puddle:";

    public static string Puddle(string reagent) => PuddlePrefix + reagent;
}

/// <summary>RP-07/RP-08. Hunger, thirst and fatigue are 0-100 severities; oxygen saturation is 0-1.</summary>
public sealed record RawBiophysics(
    IReadOnlyDictionary<string, float> DamageByType,
    float Pain,
    float Bleeding,
    float Hunger,
    float Thirst,
    float Fatigue,
    float BodyTempK,
    float OxygenSaturation,
    bool Conscious);

/// <summary>An item the agent itself holds; <see cref="Name"/> is what the agent calls it.</summary>
public sealed record RawHeldItem(string Ref, string Name);

/// <summary><see cref="Held"/> lists the agent's own hands (T1.15 menus need their names).</summary>
public sealed record RawPerception(
    string AgentGuid,
    Vec2 Self,
    float FacingRad,
    IReadOnlyList<RawPerceivedEntity> Seen,
    IReadOnlyList<RawSound> Heard,
    RawEnvironment Env,
    RawBiophysics Body,
    IReadOnlyList<RawHeldItem>? Held = null);

public enum ActionVerb
{
    Move,
    Pickup,
    Drop,
    Use,
    Open,
    Close,
    Lock,
    Unlock,
    Put,
    Take,
    Eat,
    Drink,
    Sleep,
    Wake,
    Speak,
    Give,
}

/// <summary>
/// One possible action (RJ-05). <see cref="TargetRef"/> is the entity acted on (door, container, person);
/// <see cref="ItemRef"/> the item used, taken or given.
/// </summary>
public sealed record Affordance(ActionVerb Verb, string? TargetRef = null, string? ItemRef = null);

/// <summary>A place the agent knows and can walk to (RJ-03 <c>move_target</c>); beds also feed <c>sleep_where</c>.</summary>
public sealed record KnownDestination(string Ref, string Name, Vec2 Position, bool IsBed = false);

public sealed record ActionAffordances(
    IReadOnlyList<Affordance> Actions,
    IReadOnlyList<KnownDestination> Destinations,
    IReadOnlyList<Compass> OpenDirections);

public enum MoveExtent
{
    OneStep,
    Short,
    Medium,
    UntilObstacle,
}

/// <summary>
/// What an agent wants to do. Moves use either <see cref="DestinationRef"/> or <see cref="Direction"/> +
/// <see cref="Extent"/>; speech uses <see cref="Text"/> + <see cref="Volume"/> (+ optional target).
/// </summary>
public sealed record ActionIntent(
    ActionVerb Verb,
    string? TargetRef = null,
    string? ItemRef = null,
    string? DestinationRef = null,
    Compass? Direction = null,
    MoveExtent? Extent = null,
    string? Text = null,
    SpeechVolume? Volume = null);

public enum WorldEventKind
{
    ActionCompleted,
    ActionFailed,
    Spoke,
    Damaged,
    FellAsleep,
    Woke,
    Collapsed,
    LostConsciousness,
}

/// <summary>
/// A raw event. <see cref="Detail"/> is a machine-readable reason or payload (e.g. <c>blocked</c>,
/// <c>involuntary</c>); <see cref="Amount"/> is damage, fatigue at sleep start or seconds slept.
/// </summary>
public sealed record WorldEvent(
    double AtSeconds,
    WorldEventKind Kind,
    string AgentGuid,
    ActionVerb? Verb = null,
    string? TargetRef = null,
    string? ItemRef = null,
    string? Detail = null,
    float? Amount = null);

/// <summary>Game time in seconds since the session started, and the station clock shown in context (RS-07).</summary>
public sealed class GameClock
{
    public double Seconds { get; private set; }
    public TimeSpan StationStart { get; }

    public GameClock(TimeSpan stationStart)
    {
        StationStart = stationStart;
    }

    public TimeSpan StationTime => StationStart + TimeSpan.FromSeconds(Seconds);

    public void Advance(double seconds)
    {
        if (seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        Seconds += seconds;
    }
}

/// <summary>The only way Core talks to a world (RA-01). Adapters apply FOV and occlusion (RA-06).</summary>
public interface IWorldAdapter
{
    /// <summary>What the agent perceives now; sounds are those heard since the previous call (RP-01/02).</summary>
    RawPerception GetPerception(string agentGuid);

    ActionAffordances GetAffordances(string agentGuid);

    /// <summary>Queues an action; it runs deterministically on the next ticks (P2).</summary>
    void Submit(string agentGuid, ActionIntent intent);

    IObservable<WorldEvent> Events { get; }

    GameClock Clock { get; }
}
