using Cognition.Core.Perception;

namespace Cognition.Sandbox;

public sealed partial class SandboxWorld
{
    /// <summary>RP-01: in range (fatigue-reduced), inside the cone, and with line of sight. The agent's own cell is always seen.</summary>
    public bool CanSee(Agent a, Cell target)
    {
        if (target == a.Cell)
            return true;
        if (Distance(a.Cell, target) > PerceptionRange(a))
            return false;
        if (Options.FovAngleDeg < 360f && !InCone(a, target))
            return false;
        return Grid.LineOfSight(a.Cell, target);
    }

    private bool InCone(Agent a, Cell target)
    {
        var (_, fx, fy) = Directions[(int)a.Facing];
        double dx = target.X - a.Cell.X, dy = target.Y - a.Cell.Y;
        var cos = (dx * fx + dy * fy) / (Math.Sqrt(dx * dx + dy * dy) * Math.Sqrt(fx * fx + fy * fy));
        return cos >= Math.Cos(Options.FovAngleDeg / 2.0 * Math.PI / 180.0) - 1e-9;
    }

    /// <summary>Where each visible thing is; items in closed containers or held by someone are not listed on their own.</summary>
    private IEnumerable<(Entity Entity, Cell Cell)> Placed()
    {
        foreach (var o in _agents)
        {
            yield return (o, o.Cell);
        }

        foreach (var d in _doors.Values)
        {
            yield return (d, d.Cell);
        }

        foreach (var b in _beds.Values)
        {
            yield return (b, b.Cell);
        }

        foreach (var c in _containers.Values)
        {
            yield return (c, c.Cell);
        }

        foreach (var i in _items)
        {
            if (i.Cell is { } cell)
                yield return (i, cell);
            else if (i.In is { IsOpen: true } box)
                yield return (i, box.Cell);
        }
    }

    public RawPerception GetPerception(string agentGuid)
    {
        var a = AgentByGuid(agentGuid);
        var seen = new List<(float Dist, RawPerceivedEntity E)>();
        if (!a.Asleep && a.Conscious)
        {
            foreach (var (e, cell) in Placed())
            {
                if (ReferenceEquals(e, a) || !CanSee(a, cell))
                    continue;
                seen.Add((Distance(a.Cell, cell), Describe(a, e, cell)));
            }
        }

        var heard = a.HeardBuffer.ToList();
        a.HeardBuffer.Clear();
        var (fx, fy) = (Directions[(int)a.Facing].Dx, Directions[(int)a.Facing].Dy);
        return new RawPerception(
            agentGuid,
            Center(a.Cell),
            (float)Math.Atan2(fx, -fy),
            seen.OrderBy(s => s.Dist).ThenBy(s => s.E.EntityRef, StringComparer.Ordinal).Select(s => s.E).ToList(),
            heard,
            EnvironmentAt(a),
            BodyOf(a));
    }

    private RawPerceivedEntity Describe(Agent viewer, Entity e, Cell cell)
    {
        var novel = viewer.SeenRefs.Add(e.Ref);
        var pos = Center(cell);
        return e switch
        {
            Agent o => new RawPerceivedEntity(o.Ref, o.Description, true, Key(o.Guid), pos, PersonTraits(o),
                o.Held.Select(i => i.Name).ToList(), novel),
            Door d => new RawPerceivedEntity(d.Ref, d.Name, false, null, pos, [d.State == DoorState.Open ? "open" : "closed"], [],
                novel),
            Container c => new RawPerceivedEntity(c.Ref, c.Name, false, null, pos, [c.IsOpen ? "open" : "closed"], [], novel),
            Item i => new RawPerceivedEntity(i.Ref, i.Name, false, null, pos, ItemTraits(i), [], novel, IsItem: true),
            _ => new RawPerceivedEntity(e.Ref, e.Name, false, null, pos, [], [], novel),
        };
    }

    private List<string> PersonTraits(Agent o)
    {
        var traits = new List<string>();
        if (!o.Conscious)
            traits.Add("unconscious");
        else if (o.Asleep)
            traits.Add("asleep");
        if (o.TotalDamage >= Options.BadlyInjuredTraitDamage)
            traits.Add("badly injured");
        else if (o.TotalDamage >= Options.InjuredTraitDamage)
            traits.Add("injured");
        if (o.Bleeding > 0)
            traits.Add("bleeding");
        return traits;
    }

    private List<string> ItemTraits(Item i)
    {
        if (i.In is { } box)
            return [$"in the {box.Name}"];
        return i.Cell is { } c && Grid[c] == Tile.Table ? ["on a table"] : [];
    }

    private RawEnvironment EnvironmentAt(Agent a)
    {
        var hazards = _puddles.Where(p => CanSee(a, p.Key)).OrderBy(p => Distance(a.Cell, p.Key))
            .Select(p => HazardKeys.Puddle(p.Value)).Distinct().ToList();
        var leak = _leaks.FirstOrDefault(l => l.Active && LeakArea().Contains(a.Cell) && Distance(l.Origin, a.Cell) <= l.Radius);
        if (leak is null)
        {
            return new RawEnvironment(Options.BasePressureKpa, Options.BaseTemperatureK,
                new Dictionary<string, float> { ["oxygen"] = 0.21f, ["nitrogen"] = 0.79f }, hazards);
        }

        var o2 = Options.LeakOxygenFraction;
        var gas = Options.LeakGasFraction;
        return new RawEnvironment(Options.LeakPressureKpa, Options.BaseTemperatureK,
            new Dictionary<string, float> { ["oxygen"] = o2, ["nitrogen"] = 1f - o2 - gas, [leak.Gas] = gas }, hazards);
    }

    private RawBiophysics BodyOf(Agent a) => new(
        new Dictionary<string, float>(a.Damage),
        Pain: Math.Min(100f, a.TotalDamage),
        Bleeding: a.Bleeding,
        Hunger: a.Hunger,
        Thirst: a.Thirst,
        Fatigue: a.Fatigue,
        BodyTempK: Options.BodyTemperatureK,
        OxygenSaturation: a.OxygenSaturation,
        Conscious: a.Conscious);

    /// <summary>RP-02: range by volume, −<c>wall_attenuation</c> per wall or closed door in between.</summary>
    private void Speak(Agent speaker, ActionIntent intent)
    {
        var volume = intent.Volume ?? SpeechVolume.Normal;
        var baseRange = volume switch
        {
            SpeechVolume.Whisper => _config.Perception.HearingWhisper,
            SpeechVolume.Shout => _config.Perception.HearingShout,
            _ => _config.Perception.HearingNormal,
        };
        var addressee = intent.TargetRef is { } t && Entity(t) is Agent to ? Key(to.Guid) : null;
        foreach (var listener in _agents)
        {
            if (ReferenceEquals(listener, speaker) || !listener.Conscious)
                continue;
            var walls = Grid.WallsBetween(speaker.Cell, listener.Cell);
            var loudness = (float)(baseRange * Math.Pow(1 - _config.Perception.WallAttenuation, walls));
            if (Distance(speaker.Cell, listener.Cell) > loudness)
                continue;
            if (listener.Asleep)
            {
                if (volume == SpeechVolume.Shout)
                    WakeUp(listener, "noise");
                continue;
            }

            listener.HeardBuffer.Add(new RawSound("speech", Center(speaker.Cell), loudness, walls, Key(speaker.Guid),
                speaker.Description, intent.Text, volume, addressee));
        }

        Emit(new WorldEvent(Clock.Seconds, WorldEventKind.Spoke, Key(speaker.Guid), ActionVerb.Speak, intent.TargetRef,
            Detail: volume.ToString().ToLowerInvariant()));
    }
}
