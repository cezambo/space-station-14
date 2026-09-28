using System.Text;
using System.Text.RegularExpressions;
using Cognition.Core.Config;
using Cognition.Core.Prompts;

namespace Cognition.Core.Perception;

/// <summary>
/// What the formatter needs to know about the perceiving character. <see cref="KnownNames"/> maps the stable
/// guid of each acquaintance to the name they know (RD-03); everyone else is shown by description.
/// </summary>
public sealed record PerceptionContext(IReadOnlyDictionary<string, string> KnownNames, IReadOnlyList<string> GoalTexts);

public enum PerceptionCategory
{
    Entity,
    Item,
}

/// <summary><see cref="Name"/> is how this character refers to the entity (known name or description).</summary>
public sealed record PerceivedLine(
    string Ref,
    string Name,
    PerceptionCategory Category,
    double Salience,
    DistanceBand Distance,
    Compass? Direction,
    string Text);

/// <summary>How the perceiving character refers to an entity, and where it is from them (null when held).</summary>
public sealed record EntityLabel(string Name, string? Position);

public sealed record HeardLine(string? SpeakerGuid, DistanceBand Distance, Compass? Direction, bool ToYou, string Text);

public sealed record NeedLine(string Need, NeedBand Band);

/// <summary>The perception block (T1.12) and the structured data behind each line, for tests and telemetry.</summary>
public sealed record PerceptionBlock(
    string Text,
    IReadOnlyList<PerceivedLine> Seen,
    IReadOnlyList<HeardLine> Heard,
    IReadOnlyList<Sensation> Environment,
    IReadOnlyList<NeedLine> Needs,
    int DroppedSeen,
    string EnvironmentText,
    string BodyText);

/// <summary>
/// Turns <see cref="RawPerception"/> into the categorical text block (RP-03…08, RD-03). Only what the adapter
/// supplied is used, so nothing outside FOV/hearing can appear. All words come from <see cref="Vocabulary"/>.
/// </summary>
public sealed partial class PerceptionFormatter
{
    private readonly CognitionConfig _config;
    private readonly Vocabulary _v;
    private readonly HashSet<string> _dangerTraits;

    public PerceptionFormatter(CognitionConfig config, Vocabulary vocabulary)
    {
        _config = config;
        _v = vocabulary;
        _dangerTraits = new HashSet<string>(config.Perception.Salience.DangerTraits, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Lower-case words of at least 4 letters from the goals, minus stopwords.</summary>
    public IReadOnlySet<string> GoalKeywords(IEnumerable<string> goalTexts) =>
        goalTexts.SelectMany(Words).Where(w => w.Length >= 4 && !_v.GoalStopwords.Contains(w)).ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value);

    /// <summary>T1.12: proximity/(1+d) + novelty + goal keyword match + danger, each weighted by config.</summary>
    public double Salience(RawPerceivedEntity e, float distance, IReadOnlySet<string> goalWords)
    {
        var w = _config.Perception.Salience;
        var s = w.Proximity / (1 + distance);
        if (e.IsNovel)
            s += w.Novelty;
        if (goalWords.Count > 0 && Words(e.DisplayName).Concat(e.HeldItems.SelectMany(Words)).Any(goalWords.Contains))
            s += w.Goal;
        if (e.VisibleTraits.Any(_dangerTraits.Contains))
            s += w.Danger;
        return s;
    }

    public PerceptionBlock Format(RawPerception p, PerceptionContext ctx)
    {
        var cfg = _config.Perception;
        var goalWords = GoalKeywords(ctx.GoalTexts);
        var ranked = p.Seen
            .Select(e => (E: e, D: p.Self.DistanceTo(e.Position)))
            .Select(x => (x.E, x.D, S: Salience(x.E, x.D, goalWords)))
            .OrderByDescending(x => x.S).ThenBy(x => x.D).ThenBy(x => x.E.EntityRef, StringComparer.Ordinal)
            .ToList();
        var entities = ranked.Where(x => !x.E.IsItem).Take(cfg.MaxEntities);
        var items = ranked.Where(x => x.E.IsItem).Take(cfg.MaxItems);
        var seen = entities.Concat(items).Select(x => SeenLine(p, x.E, x.D, x.S, ctx)).ToList();

        var heard = p.Heard.TakeLast(cfg.MaxSounds).Select(s => HeardLineOf(p, s, ctx)).ToList();
        var env = Categorizers.Environment(p.Env, cfg.Environment, _v.NoticeableGases).Take(cfg.MaxEnv).ToList();
        var needs = NeedsOf(p.Body);

        var envText = env.Count == 0 ? _v.Phrase("air_normal") : string.Join(_v.Phrase("list_separator"), env.Select(_v.Word));
        var bodyText = BodyText(p.Body, needs);
        return new PerceptionBlock(Render(seen, heard, envText, bodyText), seen, heard, env, needs,
            p.Seen.Count - seen.Count, envText, bodyText);
    }

    /// <summary>
    /// Labels for everything seen (not only what fits in the block) and the character's own held items, so that
    /// decision menus can describe every affordance (T1.15).
    /// </summary>
    public Dictionary<string, EntityLabel> Labels(RawPerception p, PerceptionContext ctx)
    {
        var labels = new Dictionary<string, EntityLabel>(StringComparer.Ordinal);
        foreach (var e in p.Seen)
        {
            var name = e.IsPerson ? NameOf(e.StableGuid, e.DisplayName, ctx, markKnown: false) : e.DisplayName;
            labels[e.EntityRef] = new EntityLabel(name, Position(p, e.Position, out _, out _));
        }

        foreach (var item in p.Held ?? [])
        {
            labels[item.Ref] = new EntityLabel(item.Name, null);
        }

        return labels;
    }

    /// <summary>
    /// Drops the <paramref name="count"/> least salient seen lines (RJ-08 trim step); ties drop the later line.
    /// Heard, environment and body are never trimmed.
    /// </summary>
    public PerceptionBlock DropLeastSalient(PerceptionBlock block, int count)
    {
        if (count <= 0)
            return block;
        var drop = block.Seen.Select((l, i) => (l, i)).OrderBy(x => x.l.Salience).ThenByDescending(x => x.i)
            .Take(count).Select(x => x.i).ToHashSet();
        var seen = block.Seen.Where((_, i) => !drop.Contains(i)).ToList();
        return block with
        {
            Text = Render(seen, block.Heard, block.EnvironmentText, block.BodyText),
            Seen = seen,
            DroppedSeen = block.DroppedSeen + drop.Count,
        };
    }

    private string Render(IEnumerable<PerceivedLine> seen, IEnumerable<HeardLine> heard, string envText, string bodyText)
    {
        var sb = new StringBuilder();
        sb.Append(_v.Phrase("seen_header")).Append('\n');
        AppendLines(sb, seen.Select(l => l.Text));
        sb.Append(_v.Phrase("heard_header")).Append('\n');
        AppendLines(sb, heard.Select(h => h.Text));
        sb.Append(_v.Phrase("environment_header")).Append(' ').Append(envText).Append('\n');
        sb.Append(_v.Phrase("body_header")).Append(' ').Append(bodyText);
        return sb.ToString();
    }

    private void AppendLines(StringBuilder sb, IEnumerable<string> lines)
    {
        var any = false;
        foreach (var line in lines)
        {
            sb.Append(_v.Phrase("line", ("text", line))).Append('\n');
            any = true;
        }

        if (!any)
            sb.Append(_v.Phrase("line", ("text", _v.Phrase("nothing")))).Append('\n');
    }

    private string Position(RawPerception p, Vec2 at, out DistanceBand band, out Compass? direction)
    {
        band = Categorizers.Distance(p.Self.DistanceTo(at), _config.Perception);
        direction = Categorizers.Direction(at.X - p.Self.X, at.Y - p.Self.Y);
        return direction is null
            ? _v.Phrase("here")
            : _v.Phrase("position", ("distance", _v.Word(band)), ("direction", _v.Word(direction.Value)));
    }

    private string NameOf(string? guid, string description, PerceptionContext ctx, bool markKnown) =>
        guid is not null && ctx.KnownNames.TryGetValue(guid, out var name)
            ? markKnown ? _v.Phrase("known", ("name", name)) : name
            : description;

    private PerceivedLine SeenLine(RawPerception p, RawPerceivedEntity e, float distance, double salience, PerceptionContext ctx)
    {
        var sep = _v.Phrase("field_separator");
        var name = e.IsPerson ? NameOf(e.StableGuid, e.DisplayName, ctx, markKnown: false) : e.DisplayName;
        var fields = new List<string>
        {
            e.IsPerson ? NameOf(e.StableGuid, e.DisplayName, ctx, markKnown: true) : e.DisplayName,
            Position(p, e.Position, out var band, out var dir),
        };
        if (e.HeldItems.Count > 0)
            fields.Add(_v.Phrase("holding", ("items", string.Join(_v.Phrase("list_and"), e.HeldItems))));
        if (e.VisibleTraits.Count > 0)
        {
            var traits = string.Join(_v.Phrase("list_and"), e.VisibleTraits);
            fields.Add(e.IsPerson ? _v.Phrase("looks", ("trait", traits)) : traits);
        }

        return new PerceivedLine(e.EntityRef, name, e.IsItem ? PerceptionCategory.Item : PerceptionCategory.Entity, salience, band, dir,
            string.Join(sep, fields));
    }

    private HeardLine HeardLineOf(RawPerception p, RawSound s, PerceptionContext ctx)
    {
        var position = Position(p, s.Origin, out var band, out var dir);
        var toYou = s.AddresseeGuid is not null && s.AddresseeGuid == p.AgentGuid;
        var text = _v.Phrase("heard_line",
            ("speaker", NameOf(s.SpeakerGuid, s.SpeakerDescription ?? s.Kind, ctx, markKnown: false)),
            ("position", position),
            ("verb", _v.Word(s.Volume ?? SpeechVolume.Normal)),
            ("to_you", toYou ? _v.Phrase("to_you") : string.Empty),
            ("text", OneLine(s.Text ?? string.Empty)));
        return new HeardLine(s.SpeakerGuid, band, dir, toYou, text);
    }

    private static string OneLine(string text) => WhitespaceRun().Replace(text, " ").Trim();

    private List<NeedLine> NeedsOf(RawBiophysics b)
    {
        var needs = _config.Needs;
        return
        [
            new NeedLine("hunger", Categorizers.Need("hunger", b.Hunger, needs)),
            new NeedLine("thirst", Categorizers.Need("thirst", b.Thirst, needs)),
            new NeedLine("fatigue", Categorizers.Need("fatigue", b.Fatigue, needs)),
            new NeedLine("body_temperature",
                Categorizers.Need("body_temperature", Categorizers.BodyTemperatureSeverity(b.BodyTempK, needs), needs)),
            new NeedLine("oxygen", Categorizers.Need("oxygen", Categorizers.OxygenSeverity(b.OxygenSaturation, needs), needs)),
        ];
    }

    /// <summary>Hunger, thirst and fatigue always; body temperature and breathing only when not ok; then injuries.</summary>
    private string BodyText(RawBiophysics b, List<NeedLine> needs)
    {
        var parts = needs
            .Where(n => n.Need is "hunger" or "thirst" or "fatigue" || n.Band != NeedBand.Ok)
            .Select(n => _v.Phrase("need", ("need", _v.NeedName(n.Need)), ("band", _v.Word(n.Band))))
            .ToList();
        var bounds = _config.Perception.DamageBands;
        foreach (var (type, amount) in b.DamageByType.OrderByDescending(d => d.Value).ThenBy(d => d.Key, StringComparer.Ordinal))
        {
            if (Categorizers.Damage(amount, bounds) is { } band)
                parts.Add(_v.Phrase("damage", ("band", _v.Word(band)), ("type", type.ToLowerInvariant())));
        }

        if (Categorizers.Damage(b.Pain, bounds) is { } pain)
            parts.Add(_v.Phrase("pain", ("band", _v.Word(pain))));
        if (b.Bleeding > 0)
            parts.Add(_v.Phrase("bleeding"));
        return string.Join(_v.Phrase("list_separator"), parts);
    }

    [GeneratedRegex("[a-z]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
