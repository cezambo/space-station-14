using System.Globalization;
using Cognition.Core.Config;
using Cognition.Core.Minds;
using Cognition.Core.Perception;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cognition.Sandbox.Scenarios;

public sealed class ScenarioLoadException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ScenarioLoadException(IReadOnlyList<string> errors)
        : base("Scenario is invalid:\n  " + string.Join("\n  ", errors))
    {
        Errors = errors;
    }
}

public sealed record AssertionResult(int Index, string Description, bool Passed, string Detail);

public sealed record ScenarioResult(
    string Id,
    bool Passed,
    IReadOnlyList<AssertionResult> Assertions,
    IReadOnlyList<WorldEvent> Events,
    double GameSeconds);

/// <summary>Plugs a mind (AI or test double) into a run; called once per tick before the world steps.</summary>
public interface IScenarioDriver
{
    Task TickAsync(ScenarioRun run, CancellationToken ct);
}

/// <summary>A built scenario: the world, its characters by seed id, and their minds.</summary>
public sealed class ScenarioRun
{
    private readonly Dictionary<string, string> _aliases;

    internal ScenarioRun(ScenarioFile file, SandboxWorld world, Dictionary<string, Agent> characters,
        Dictionary<string, Mind> minds, Dictionary<string, string> aliases)
    {
        File = file;
        World = world;
        Characters = characters;
        Minds = minds;
        _aliases = aliases;
    }

    public ScenarioFile File { get; }
    public SandboxWorld World { get; }
    public IReadOnlyDictionary<string, Agent> Characters { get; }
    public IReadOnlyDictionary<string, Mind> Minds { get; }

    internal void AliasLeak(string alias, string @ref) => _aliases[alias] = @ref;

    /// <summary>An item/leak alias or any world ref, resolved to the world ref.</summary>
    public string Ref(string aliasOrRef) => _aliases.GetValueOrDefault(aliasOrRef, aliasOrRef);

    /// <summary>RD-03 names this character knows, keyed by stable guid, for <see cref="PerceptionFormatter"/>.</summary>
    public IReadOnlyDictionary<string, string> KnownNames(string characterId)
    {
        var mind = Minds[characterId];
        return mind.Acquaintances.Where(a => Minds.ContainsKey(a.Key))
            .ToDictionary(a => SandboxWorld.Key(Minds[a.Key].StableGuid), a => a.Value.KnownName, StringComparer.Ordinal);
    }
}

internal static class GuidKey
{
    public static string K(this Guid g) => SandboxWorld.Key(g);
}

public static class ScenarioLoader
{
    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public static ScenarioFile Parse(string yaml, string name)
    {
        try
        {
            return Yaml.Deserialize<ScenarioFile>(yaml) ?? throw new ScenarioLoadException([$"{name}: empty"]);
        }
        catch (YamlException ex)
        {
            throw new ScenarioLoadException([$"{name}: {ex.Message} {ex.InnerException?.Message}".Trim()]);
        }
    }

    public static IReadOnlyList<ScenarioFile> LoadDirectory(string dir) =>
        Directory.GetFiles(dir, "*.yaml").Order(StringComparer.Ordinal)
            .Select(f => Parse(File.ReadAllText(f), Path.GetFileName(f))).ToList();
}

/// <summary>
/// Builds a <see cref="SandboxWorld"/> from a scenario, runs it for <c>duration_s</c> with an optional driver,
/// and evaluates every expectation (T1.13, RDev-01). All problems in a scenario are reported together.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly CognitionConfig _config;
    private readonly IReadOnlyDictionary<string, Mind> _seeds;

    public ScenarioRunner(CognitionConfig config, IEnumerable<Mind> seeds)
    {
        _config = config;
        _seeds = seeds.ToDictionary(m => m.Id, StringComparer.Ordinal);
    }

    internal static T ParseEnum<T>(string snake, string at, List<string> errors)
        where T : struct, Enum
    {
        var pascal = string.Concat(snake.Split('_').Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]));
        if (Enum.TryParse<T>(pascal, ignoreCase: false, out var value) && Enum.IsDefined(value))
            return value;
        errors.Add($"{at}: '{snake}' is not one of {string.Join(", ", Enum.GetNames<T>().Select(n => Vocab(n)))}");
        return default;
    }

    private static string Vocab(string pascal) =>
        string.Concat(pascal.Select((ch, i) => char.IsUpper(ch) ? (i > 0 ? "_" : "") + char.ToLowerInvariant(ch) : ch.ToString()));

    private static Cell? CellOf(List<int>? xy, string at, List<string> errors)
    {
        if (xy is null)
            return null;
        if (xy.Count == 2)
            return new Cell(xy[0], xy[1]);
        errors.Add($"{at}: must be [x, y]");
        return null;
    }

    private SandboxOptions Options(ScenarioFile f, List<string> errors)
    {
        var options = new SandboxOptions { Seed = f.Seed };
        foreach (var (key, raw) in f.Options)
        {
            var prop = typeof(SandboxOptions).GetProperty(string.Concat(key.Split('_').Select(p => char.ToUpperInvariant(p[0]) + p[1..])));
            if (prop is null || prop.Name is nameof(SandboxOptions.Seed) or nameof(SandboxOptions.StationStart))
            {
                errors.Add($"options.{key}: unknown option");
                continue;
            }

            try
            {
                prop.SetValue(options, Convert.ChangeType(raw, prop.PropertyType, CultureInfo.InvariantCulture));
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                errors.Add($"options.{key}: '{raw}' is not a {prop.PropertyType.Name}");
            }
        }

        return options;
    }

    public ScenarioRun Build(ScenarioFile f)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(f.Id))
            errors.Add("id: required");
        if (f.DurationS <= 0)
            errors.Add("duration_s: must be > 0");
        if (f.Driver is not ("scripted" or "agent"))
            errors.Add($"driver: '{f.Driver}' must be scripted or agent");

        SandboxWorld world;
        try
        {
            world = SandboxWorld.FromMap(f.Map, _config, Options(f, errors));
        }
        catch (FormatException ex)
        {
            throw new ScenarioLoadException([.. errors, $"map: {ex.Message}"]);
        }

        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        var characters = new Dictionary<string, Agent>(StringComparer.Ordinal);
        var minds = new Dictionary<string, Mind>(StringComparer.Ordinal);
        for (var i = 0; i < f.Characters.Count; i++)
        {
            var c = f.Characters[i];
            var at = $"characters[{i}]";
            if (!_seeds.TryGetValue(c.Seed, out var mind))
            {
                errors.Add($"{at}.seed: no seed character '{c.Seed}'");
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.Description))
                errors.Add($"{at}.description: required (what strangers see, RD-03)");
            if (CellOf(c.At, $"{at}.at", errors) is not { } cell)
                continue;
            if (!world.Grid.IsWalkable(cell))
            {
                errors.Add($"{at}.at: {cell} is not walkable");
                continue;
            }

            var agent = world.AddAgent(mind.Id, mind.Ss14Profile.Name, mind.StableGuid, c.Description, cell);
            characters[mind.Id] = agent;
            if (c.Facing is not null)
                agent.Facing = ParseEnum<Compass>(c.Facing, $"{at}.facing", errors);
            foreach (var (need, value) in c.Needs)
            {
                switch (need)
                {
                    case "hunger":
                        agent.Hunger = value;
                        break;
                    case "thirst":
                        agent.Thirst = value;
                        break;
                    case "fatigue":
                        agent.Fatigue = value;
                        break;
                    default:
                        errors.Add($"{at}.needs.{need}: must be hunger, thirst or fatigue");
                        break;
                }
            }

            minds[mind.Id] = mind;
        }

        for (var i = 0; i < f.Characters.Count; i++)
        {
            var c = f.Characters[i];
            if (!minds.TryGetValue(c.Seed, out var mind))
                continue;
            var known = new Dictionary<string, Acquaintance>(mind.Acquaintances);
            foreach (var other in c.Knows)
            {
                if (_seeds.TryGetValue(other, out var o) && other != c.Seed)
                    known[other] = new Acquaintance(o.Ss14Profile.Name, CharacterSeeds.FirstDay);
                else
                    errors.Add($"characters[{i}].knows: '{other}' is not another seed character");
            }

            minds[c.Seed] = mind with { Acquaintances = known };
            foreach (var (item, j) in c.Holding.Select((h, j) => (h, j)))
            {
                AddItem(world, item, $"characters[{i}].holding[{j}]", characters[c.Seed], aliases, errors);
            }

            if (c.Asleep)
            {
                var sleeper = characters[c.Seed];
                world.Submit(sleeper.Guid.K(), new ActionIntent(ActionVerb.Sleep, world.BedAt(sleeper.Cell)?.Ref));
            }
        }

        for (var i = 0; i < f.Items.Count; i++)
        {
            AddItem(world, f.Items[i], $"items[{i}]", null, aliases, errors);
        }

        foreach (var (p, i) in f.Puddles.Select((p, i) => (p, i)))
        {
            if (CellOf(p.At, $"puddles[{i}].at", errors) is { } cell)
                world.AddPuddle(cell, p.Reagent);
        }

        var run = new ScenarioRun(f, world, characters, minds, aliases);
        ValidateSteps(run, errors);
        ValidateExpectations(run, errors);
        if (errors.Count > 0)
            throw new ScenarioLoadException(errors.Select(e => $"{f.Id}: {e}").ToList());
        return run;
    }

    private static void AddItem(SandboxWorld world, ItemSpec s, string at, Agent? holder, Dictionary<string, string> aliases,
        List<string> errors)
    {
        var kind = ParseEnum<ItemKind>(s.Kind, $"{at}.kind", errors);
        string? keyId = null;
        if (s.Opens is not null)
        {
            if (world.Entity(s.Opens) is Door d)
                keyId = d.LockId;
            else
                errors.Add($"{at}.opens: '{s.Opens}' is not a door");
        }

        Item? item = null;
        if (holder is not null)
        {
            if (holder.HeldItems.Count < Agent.Hands)
                item = world.AddItem(kind, s.Name, holder, keyId);
            else
                errors.Add($"{at}: no free hand");
        }
        else if (s.In is not null)
        {
            if (world.Entity(s.In) is Container box)
                item = world.AddItem(kind, s.Name, box, keyId);
            else
                errors.Add($"{at}.in: '{s.In}' is not a container");
        }
        else if (CellOf(s.At, $"{at}.at", errors) is { } cell)
        {
            if (world.Grid[cell] == Tile.Wall || !world.Grid.InBounds(cell))
                errors.Add($"{at}.at: {cell} is a wall");
            else
                item = world.AddItem(kind, s.Name, cell, keyId);
        }
        else
        {
            errors.Add($"{at}: needs at or in");
        }

        if (item is not null && s.Id is not null && !aliases.TryAdd(s.Id, item.Ref))
            errors.Add($"{at}.id: '{s.Id}' is used twice");
    }

    // ------------------------------------------------------------------ steps

    private static void ValidateSteps(ScenarioRun run, List<string> errors)
    {
        for (var i = 0; i < run.File.Script.Count; i++)
        {
            var s = run.File.Script[i];
            var at = $"script[{i}]";
            var kinds = new object?[] { s.Do, s.Say, s.Damage, s.Door, s.Leak, s.StopLeak }.Count(k => k is not null);
            if (kinds != 1)
                errors.Add($"{at}: needs exactly one of do, say, damage, door, leak, stop_leak");
            if (s.AtS < 0 || s.AtS > run.File.DurationS)
                errors.Add($"{at}.at_s: must be within the scenario duration");
            if (s.Do is { } d)
            {
                Who(run, d.Who, $"{at}.do.who", errors);
                ParseEnum<ActionVerb>(d.Verb, $"{at}.do.verb", errors);
                if (d.Direction is not null)
                    ParseEnum<Compass>(d.Direction, $"{at}.do.direction", errors);
                if (d.Extent is not null)
                    ParseEnum<MoveExtent>(d.Extent, $"{at}.do.extent", errors);
            }

            if (s.Say is { } say)
            {
                Who(run, say.Who, $"{at}.say.who", errors);
                ParseEnum<SpeechVolume>(say.Volume, $"{at}.say.volume", errors);
                if (say.To is not null)
                    Who(run, say.To, $"{at}.say.to", errors);
            }

            if (s.Damage is { } dmg)
                Who(run, dmg.Who, $"{at}.damage.who", errors);
            if (s.Door is { } door)
            {
                if (run.World.Entity(door.Ref) is not Door)
                    errors.Add($"{at}.door.ref: '{door.Ref}' is not a door");
                ParseEnum<DoorState>(door.State, $"{at}.door.state", errors);
            }

            if (s.Leak is { } leak)
                CellOf(leak.At, $"{at}.leak.at", errors);
        }
    }

    private static Agent? Who(ScenarioRun run, string who, string at, List<string> errors)
    {
        if (run.Characters.TryGetValue(who, out var a))
            return a;
        errors.Add($"{at}: '{who}' is not a character in this scenario");
        return null;
    }

    private static void Apply(ScenarioRun run, StepSpec s)
    {
        var w = run.World;
        var none = new List<string>();
        if (s.Do is { } d)
        {
            w.Submit(run.Characters[d.Who].Guid.K(), new ActionIntent(
                ParseEnum<ActionVerb>(d.Verb, "", none),
                d.Target is null ? null : run.Ref(d.Target),
                d.Item is null ? null : run.Ref(d.Item),
                d.Destination is null ? null : run.Ref(d.Destination),
                d.Direction is null ? null : ParseEnum<Compass>(d.Direction, "", none),
                d.Extent is null ? null : ParseEnum<MoveExtent>(d.Extent, "", none)));
        }
        else if (s.Say is { } say)
        {
            w.Submit(run.Characters[say.Who].Guid.K(), new ActionIntent(ActionVerb.Speak, say.To,
                Text: say.Text, Volume: ParseEnum<SpeechVolume>(say.Volume, "", none)));
        }
        else if (s.Damage is { } dmg)
        {
            w.Damage(run.Characters[dmg.Who], dmg.Type, dmg.Amount, dmg.Bleeding);
        }
        else if (s.Door is { } door)
        {
            w.SetDoor((Door)w.Entity(door.Ref)!, ParseEnum<DoorState>(door.State, "", none));
        }
        else if (s.Leak is { } leak)
        {
            var l = w.AddLeak(new Cell(leak.At[0], leak.At[1]), leak.Radius, leak.Gas);
            if (leak.Id is not null)
                run.AliasLeak(leak.Id, l.Ref);
        }
        else if (s.StopLeak is { } stop && w.Entity(run.Ref(stop)) is GasLeak gl)
        {
            w.StopLeak(gl);
        }
    }

    // ------------------------------------------------------------------ run

    public async Task<ScenarioResult> RunAsync(ScenarioFile f, IScenarioDriver? driver, CancellationToken ct = default)
    {
        if (f.Driver == "agent" && driver is null)
            throw new InvalidOperationException($"{f.Id}: an agent scenario needs a driver");
        var run = Build(f);
        var w = run.World;
        var steps = f.Script.Select((s, i) => (s, i)).OrderBy(x => x.s.AtS).ThenBy(x => x.i).Select(x => x.s).ToList();
        var timed = f.Expect.Select((e, i) => (e, i)).Where(x => x.e.AtS is not null).OrderBy(x => x.e.AtS).ToList();
        var results = new List<AssertionResult>();
        var next = 0;
        var nextTimed = 0;
        var ticks = (int)Math.Round(f.DurationS / w.Options.TickSeconds);
        for (var t = 0; t < ticks; t++)
        {
            ct.ThrowIfCancellationRequested();
            while (next < steps.Count && steps[next].AtS <= w.Clock.Seconds + 1e-9)
            {
                Apply(run, steps[next++]);
            }

            if (driver is not null)
                await driver.TickAsync(run, ct).ConfigureAwait(false);
            w.Step();
            while (nextTimed < timed.Count && timed[nextTimed].e.AtS <= w.Clock.Seconds + 1e-9)
            {
                var (e, i) = timed[nextTimed++];
                results.Add(Evaluate(run, e, i));
            }
        }

        results.AddRange(f.Expect.Select((e, i) => (e, i)).Where(x => x.e.AtS is null).Select(x => Evaluate(run, x.e, x.i)));
        results.AddRange(timed.Skip(nextTimed).Select(x => new AssertionResult(x.i, Describe(x.e), false, "never reached")));
        results.Sort((a, b) => a.Index.CompareTo(b.Index));
        return new ScenarioResult(f.Id, results.All(r => r.Passed), results, w.Log, w.Clock.Seconds);
    }

    // ------------------------------------------------------------------ expectations

    private static void ValidateExpectations(ScenarioRun run, List<string> errors)
    {
        if (run.File.Expect.Count == 0)
            errors.Add("expect: at least one expectation");
        for (var i = 0; i < run.File.Expect.Count; i++)
        {
            var e = run.File.Expect[i];
            var at = $"expect[{i}]";
            var timings = new[] { e.WithinS is not null, e.AtS is not null, e.AtEnd, e.Never }.Count(b => b);
            if (timings != 1)
                errors.Add($"{at}: needs exactly one of within_s, at_s, at_end, never");
            var checks = new object?[] { e.Event, e.Need, e.Holding, e.Near, e.Asleep, e.Sees, e.Hears, e.Door, e.Exists, e.Damage }
                .Count(c => c is not null);
            if (checks != 1)
                errors.Add($"{at}: needs exactly one check");
            if ((e.WithinS is not null || e.Never) && e.Event is null && e.Hears is null)
                errors.Add($"{at}: within_s and never apply to event or hears checks");
            if (e.Event is { } ev)
            {
                if (ev.Who is not null)
                    Who(run, ev.Who, $"{at}.event.who", errors);
                ParseEnum<WorldEventKind>(ev.Kind, $"{at}.event.kind", errors);
                if (ev.Verb is not null)
                    ParseEnum<ActionVerb>(ev.Verb, $"{at}.event.verb", errors);
            }

            foreach (var who in new[] { e.Need?.Who, e.Holding?.Who, e.Near?.Who, e.Asleep?.Who, e.Sees?.Who, e.Hears?.Who, e.Damage?.Who })
            {
                if (who is not null)
                    Who(run, who, $"{at}.who", errors);
            }

            if (e.Need is { } n && n.Need is not ("hunger" or "thirst" or "fatigue"))
                errors.Add($"{at}.need.need: must be hunger, thirst or fatigue");
            if (e.Door is { } d)
            {
                if (run.World.Entity(d.Ref) is not Door)
                    errors.Add($"{at}.door.ref: '{d.Ref}' is not a door");
                ParseEnum<DoorState>(d.State, $"{at}.door.state", errors);
            }

            if (e.Near is { Of: null, At: null })
                errors.Add($"{at}.near: needs of or at");
        }
    }

    private static string Describe(ExpectSpec e)
    {
        var timing = e.WithinS is { } w ? $"within {w}s" : e.AtS is { } a ? $"at {a}s" : e.Never ? "never" : "at end";
        var (name, check) = typeof(ExpectSpec).GetProperties()
            .Where(p => p.PropertyType.IsClass && p.PropertyType != typeof(string))
            .Select(p => (p.Name, V: p.GetValue(e)))
            .Where(x => x.V is not null)
            .Select(x => (x.Name, V: x.V!))
            .First();
        var fields = check.GetType().GetProperties()
            .Select(p => (p.Name, V: p.GetValue(check)))
            .Where(x => x.V is not null && x.V is not (List<int> { Count: 0 }))
            .Select(x => $"{Vocab(x.Name)}={(x.V is List<int> l ? $"[{string.Join(",", l)}]" : Convert.ToString(x.V, CultureInfo.InvariantCulture))}");
        return $"{timing}: {Vocab(name)} {string.Join(" ", fields)}";
    }

    private static AssertionResult Evaluate(ScenarioRun run, ExpectSpec e, int index)
    {
        var (ok, detail) = Check(run, e);
        return new AssertionResult(index, Describe(e), ok, detail);
    }

    private static (bool, string) Check(ScenarioRun run, ExpectSpec e)
    {
        var w = run.World;
        var none = new List<string>();
        if (e.Event is { } ev)
        {
            var kind = ParseEnum<WorldEventKind>(ev.Kind, "", none);
            var guid = ev.Who is null ? null : run.Characters[ev.Who].Guid.K();
            ActionVerb? verb = ev.Verb is null ? null : ParseEnum<ActionVerb>(ev.Verb, "", none);
            var matches = w.Log.Where(x => x.Kind == kind && (guid is null || x.AgentGuid == guid) && (verb is null || x.Verb == verb)
                && (ev.Target is null || x.TargetRef == run.Ref(ev.Target)) && (ev.Item is null || x.ItemRef == run.Ref(ev.Item))
                && (ev.Detail is null || x.Detail == ev.Detail)).ToList();
            if (e.Never)
                return (matches.Count == 0, matches.Count == 0 ? "none" : $"at {matches[0].AtSeconds:F1}s");
            var limit = e.WithinS ?? double.MaxValue;
            var first = matches.FirstOrDefault(x => x.AtSeconds <= limit + 1e-9);
            return (first is not null, first is null ? matches.Count == 0 ? "not seen" : $"only at {matches[0].AtSeconds:F1}s" : $"at {first.AtSeconds:F1}s");
        }

        if (e.Hears is { } h)
        {
            var guid = run.Characters[h.Who].Guid.K();
            var limit = e.WithinS ?? double.MaxValue;
            var heard = w.HeardLog.Any(x => x.ListenerGuid == guid && x.At <= limit + 1e-9
                && (x.Sound.Text ?? "").Contains(h.Text, StringComparison.OrdinalIgnoreCase));
            var expected = e.Never ? false : h.Value;
            return (heard == expected, heard ? "heard" : "not heard");
        }

        if (e.Need is { } n)
        {
            var a = run.Characters[n.Who];
            var v = n.Need switch { "hunger" => a.Hunger, "thirst" => a.Thirst, _ => a.Fatigue };
            var ok = (n.Below is null || v < n.Below) && (n.Above is null || v > n.Above);
            return (ok, v.ToString("F1", CultureInfo.InvariantCulture));
        }

        if (e.Holding is { } hold)
        {
            var a = run.Characters[hold.Who];
            var held = a.HeldItems.Any(i => i.Ref == run.Ref(hold.Item) || i.Name == hold.Item);
            return (held == hold.Value, held ? "holding" : $"holding [{string.Join(", ", a.HeldItems.Select(i => i.Name))}]");
        }

        if (e.Near is { } near)
        {
            var a = run.Characters[near.Who];
            var target = near.At is { Count: 2 } xy ? new Cell(xy[0], xy[1]) : PositionOf(run, near.Of!);
            if (target is null)
                return (false, $"'{near.Of}' has no position");
            var d = Math.Sqrt(Math.Pow(a.Cell.X - target.Value.X, 2) + Math.Pow(a.Cell.Y - target.Value.Y, 2));
            return (d <= near.Within + 1e-9, $"at {a.Cell}, {d:F1} tiles");
        }

        if (e.Asleep is { } s)
        {
            var a = run.Characters[s.Who];
            return (a.Asleep == s.Value, a.Asleep ? "asleep" : "awake");
        }

        if (e.Sees is { } sees)
        {
            var a = run.Characters[sees.Who];
            var target = run.World.Entity(run.Ref(sees.What)) is { } ent ? PositionOf(run, ent.Ref)
                : run.World.Items.FirstOrDefault(i => i.Name == sees.What) is { } byName ? PositionOf(run, byName.Ref) : null;
            var visible = target is not null && !a.Asleep && w.CanSee(a, target.Value);
            return (visible == sees.Value, visible ? "visible" : "not visible");
        }

        if (e.Door is { } door)
        {
            var d = (Door)w.Entity(door.Ref)!;
            return (d.State == ParseEnum<DoorState>(door.State, "", none), d.State.ToString());
        }

        if (e.Exists is { } ex)
        {
            var exists = w.Entity(run.Ref(ex.Item)) is not null;
            return (exists == ex.Value, exists ? "exists" : "gone");
        }

        var dmg = e.Damage!;
        var total = run.Characters[dmg.Who].TotalDamage;
        return ((dmg.Below is null || total < dmg.Below) && (dmg.Above is null || total > dmg.Above),
            total.ToString("F1", CultureInfo.InvariantCulture));
    }

    private static Cell? PositionOf(ScenarioRun run, string aliasOrRef)
    {
        if (run.Characters.TryGetValue(aliasOrRef, out var agent))
            return agent.Cell;
        return run.World.Entity(run.Ref(aliasOrRef)) switch
        {
            Agent a => a.Cell,
            Door d => d.Cell,
            Bed b => b.Cell,
            Container c => c.Cell,
            Item { Cell: { } c } => c,
            Item { In: { } box } => box.Cell,
            Item { HeldBy: { } holder } => holder.Cell,
            _ => null,
        };
    }
}
