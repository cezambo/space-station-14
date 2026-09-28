using Cognition.Core.Providers;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Cognition.Core.Prompts;

public enum JevQuestionType
{
    Choice,
    Score,
    Noul,
}

/// <summary>
/// One question of a <c>jev/*.yaml</c> set. Exactly one of <see cref="Options"/>, <see cref="Levels"/> or
/// <see cref="CriteriaPlaceholder"/> is set for Choice/Score; none for Noul.
/// </summary>
public sealed record JevQuestionTemplate(
    string Id,
    JevQuestionType Type,
    string Instructions,
    IReadOnlyList<KeyValuePair<string, string>>? Options,
    IReadOnlyList<string>? Levels,
    string? CriteriaPlaceholder,
    string? WhenTrue,
    string? WhenFalse,
    string? IncludeWhen,
    string? ThresholdKey);

/// <summary>
/// Render inputs. <see cref="ChoiceOptions"/> / <see cref="ScoreLevels"/> fill criteria given as a single
/// placeholder. With <c>repeat_per</c>, each <see cref="RepeatItems"/> entry adds its values and yields
/// questions <c>{id}_{index}</c> (0-based). <see cref="Include"/> decides <c>include_when</c> (default: all).
/// </summary>
public sealed record JevRenderInput(IReadOnlyDictionary<string, string> Values)
{
    public IReadOnlyDictionary<string, IReadOnlyList<KeyValuePair<string, string>>> ChoiceOptions { get; init; } =
        new Dictionary<string, IReadOnlyList<KeyValuePair<string, string>>>();

    public IReadOnlyDictionary<string, IReadOnlyList<string>> ScoreLevels { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<IReadOnlyDictionary<string, string>>? RepeatItems { get; init; }

    public Func<JevQuestionTemplate, bool>? Include { get; init; }
}

public sealed record RenderedJev(JevRequest Request, IReadOnlyList<string> Warnings);

/// <summary>A <c>jev/*.yaml</c> question set (prompts/README.md).</summary>
public sealed class JevTemplate
{
    private static readonly HashSet<string> TopKeys = ["purpose", "state_template", "questions", "repeat_per"];

    private static readonly HashSet<string> QuestionKeys =
        ["type", "instructions", "criteria", "when_true", "when_false", "include_when", "threshold_key"];

    public string Name { get; }
    public string Purpose { get; }
    public string StateTemplate { get; }
    public string? RepeatPer { get; }
    public IReadOnlyList<JevQuestionTemplate> Questions { get; }
    public string Hash { get; }

    private JevTemplate(string name, string purpose, string state, string? repeatPer,
        IReadOnlyList<JevQuestionTemplate> questions, string hash)
    {
        Name = name;
        Purpose = purpose;
        StateTemplate = state;
        RepeatPer = repeatPer;
        Questions = questions;
        Hash = hash;
    }

    public RenderedJev Render(string model, JevRenderInput input)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();
        var tags = Placeholders.TagsIn(StateTemplate);

        var state = Placeholders.Render(StateTemplate, input.Values, tags, used, missing);
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal);
        var selected = Questions.Where(q => input.Include?.Invoke(q) ?? true).ToList();

        if (RepeatPer is null)
        {
            foreach (var q in selected)
            {
                questions[q.Id] = Build(q, input.Values, input, used, missing);
            }
        }
        else
        {
            if (input.RepeatItems is null)
                throw new PromptRenderException(Name, [], $"template '{Name}': repeat_per '{RepeatPer}' needs RepeatItems");
            for (var i = 0; i < input.RepeatItems.Count; i++)
            {
                var item = input.RepeatItems[i];
                var merged = new Dictionary<string, string>(input.Values, StringComparer.Ordinal);
                foreach (var (k, v) in item)
                {
                    merged[k] = v;
                }

                var itemUsed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var q in selected)
                {
                    questions[$"{q.Id}_{i}"] = Build(q, merged, input, itemUsed, missing);
                }

                used.UnionWith(itemUsed);
                warnings.AddRange(item.Keys.Where(k => !itemUsed.Contains(k)).Order(StringComparer.Ordinal)
                    .Select(k => $"template '{Name}': repeat item {i} value '{k}' is not used by any placeholder"));
            }
        }

        if (missing.Count > 0)
            throw new PromptRenderException(Name, missing.ToList());
        if (questions.Count == 0)
            throw new PromptRenderException(Name, [], $"template '{Name}': no question selected; do not call Jev");

        warnings.InsertRange(0, input.Values.Keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => $"template '{Name}': value '{k}' is not used by any placeholder"));
        var request = new JevRequest(model, state, questions, Purpose) { TemplateHash = Hash };
        return new RenderedJev(request, warnings);
    }

    private JevQuestion Build(JevQuestionTemplate q, IReadOnlyDictionary<string, string> values, JevRenderInput input,
        ISet<string> used, ISet<string> missing)
    {
        string R(string s) => Placeholders.Render(s, values, [], used, missing);
        var instructions = R(q.Instructions);
        switch (q.Type)
        {
            case JevQuestionType.Choice:
                IReadOnlyList<KeyValuePair<string, string>> options;
                if (q.CriteriaPlaceholder is { } p)
                {
                    if (!input.ChoiceOptions.TryGetValue(p, out var dynamic))
                    {
                        missing.Add(p);
                        dynamic = [];
                    }

                    options = dynamic;
                }
                else
                {
                    options = q.Options!.Select(o => new KeyValuePair<string, string>(o.Key, R(o.Value))).ToList();
                }

                return new ChoiceQuestion(instructions, new OrderedCriteria(options));
            case JevQuestionType.Score:
                IReadOnlyList<string> levels;
                if (q.CriteriaPlaceholder is { } sp)
                {
                    if (!input.ScoreLevels.TryGetValue(sp, out var dynamicLevels))
                    {
                        missing.Add(sp);
                        dynamicLevels = [];
                    }

                    levels = dynamicLevels;
                }
                else
                {
                    levels = q.Levels!.Select(R).ToList();
                }

                return new ScoreQuestion(instructions, levels);
            default:
                return new NoulQuestion(instructions, q.WhenTrue is null ? null : R(q.WhenTrue),
                    q.WhenFalse is null ? null : R(q.WhenFalse));
        }
    }

    internal static JevTemplate? Parse(string name, string text, List<string> errors)
    {
        var at = $"jev/{name}.yaml";
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode m)
            {
                errors.Add($"{at}: must be one YAML mapping");
                return null;
            }

            root = m;
        }
        catch (YamlException e)
        {
            errors.Add($"{at}: YAML error at line {e.Start.Line}: {e.Message}");
            return null;
        }

        var before = errors.Count;
        foreach (var key in root.Children.Keys.Select(Scalar).Where(k => !TopKeys.Contains(k)))
        {
            errors.Add($"{at}: unknown key '{key}'");
        }

        var purpose = ScalarAt(root, "purpose", at, errors, required: true);
        var state = ScalarAt(root, "state_template", at, errors, required: true)?.TrimEnd();
        var repeatPer = ScalarAt(root, "repeat_per", at, errors, required: false);

        var questions = new List<JevQuestionTemplate>();
        if (!root.Children.TryGetValue(new YamlScalarNode("questions"), out var qNode) || qNode is not YamlMappingNode qMap
            || qMap.Children.Count == 0)
        {
            errors.Add($"{at}: 'questions' must be a non-empty mapping");
        }
        else
        {
            foreach (var (idNode, body) in qMap.Children)
            {
                var q = ParseQuestion(Scalar(idNode), body, $"{at}: questions.{Scalar(idNode)}", errors);
                if (q is not null)
                    questions.Add(q);
            }
        }

        if (errors.Count > before || purpose is null || state is null)
            return null;
        return new JevTemplate(name, purpose, state, repeatPer, questions, Placeholders.Hash(text));
    }

    private static JevQuestionTemplate? ParseQuestion(string id, YamlNode body, string at, List<string> errors)
    {
        if (body is not YamlMappingNode m)
        {
            errors.Add($"{at}: must be a mapping");
            return null;
        }

        var before = errors.Count;
        foreach (var key in m.Children.Keys.Select(Scalar).Where(k => !QuestionKeys.Contains(k)))
        {
            errors.Add($"{at}: unknown key '{key}'");
        }

        var typeText = ScalarAt(m, "type", at, errors, required: true);
        var instructions = ScalarAt(m, "instructions", at, errors, required: true)?.Trim();
        var whenTrue = ScalarAt(m, "when_true", at, errors, required: false);
        var whenFalse = ScalarAt(m, "when_false", at, errors, required: false);
        var includeWhen = ScalarAt(m, "include_when", at, errors, required: false);
        var thresholdKey = ScalarAt(m, "threshold_key", at, errors, required: false);
        m.Children.TryGetValue(new YamlScalarNode("criteria"), out var criteria);

        JevQuestionType? type = typeText switch
        {
            "choice" => JevQuestionType.Choice,
            "score" => JevQuestionType.Score,
            "noul" => JevQuestionType.Noul,
            null => null,
            _ => null,
        };
        if (typeText is not null && type is null)
            errors.Add($"{at}: type '{typeText}' is not choice, score or noul");

        List<KeyValuePair<string, string>>? options = null;
        List<string>? levels = null;
        string? placeholder = null;
        if (criteria is YamlScalarNode s && Placeholders.NamesIn(s.Value ?? string.Empty) is [var only]
            && s.Value!.Trim() == "{{" + only + "}}")
        {
            placeholder = only;
        }
        else if (type == JevQuestionType.Choice)
        {
            if (criteria is YamlMappingNode map)
            {
                options = map.Children.Select(c => new KeyValuePair<string, string>(Scalar(c.Key), Scalar(c.Value))).ToList();
                if (options.Count is < 2 or > 255)
                    errors.Add($"{at}: choice needs 2-255 options, has {options.Count}");
            }
            else
            {
                errors.Add($"{at}: choice criteria must be a mapping or a single placeholder");
            }
        }
        else if (type == JevQuestionType.Score)
        {
            if (criteria is YamlSequenceNode seq)
            {
                levels = seq.Children.Select(Scalar).ToList();
                if (levels.Count is < 2 or > 10)
                    errors.Add($"{at}: score needs 2-10 levels, has {levels.Count}");
            }
            else
            {
                errors.Add($"{at}: score criteria must be a list or a single placeholder");
            }
        }

        if (type == JevQuestionType.Noul && criteria is not null)
            errors.Add($"{at}: noul takes when_true/when_false, not criteria");
        if (type != JevQuestionType.Noul && (whenTrue ?? whenFalse) is not null)
            errors.Add($"{at}: when_true/when_false are for noul questions only");
        if ((whenTrue is null) != (whenFalse is null))
            errors.Add($"{at}: when_true and when_false go together");

        if (errors.Count > before || type is null || instructions is null)
            return null;
        return new JevQuestionTemplate(id, type.Value, instructions, options, levels, placeholder, whenTrue, whenFalse,
            includeWhen, thresholdKey);
    }

    private static string Scalar(YamlNode node) => node is YamlScalarNode s ? s.Value ?? string.Empty : node.ToString();

    private static string? ScalarAt(YamlMappingNode m, string key, string at, List<string> errors, bool required)
    {
        if (!m.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            if (required)
                errors.Add($"{at}: missing '{key}'");
            return null;
        }

        if (node is YamlScalarNode { Value: { Length: > 0 } value })
            return value;
        errors.Add($"{at}: '{key}' must be a non-empty string");
        return null;
    }
}

/// <summary>Choice criteria that keep YAML order, so the option order sent to Jev is the authored one.</summary>
internal sealed class OrderedCriteria : IReadOnlyDictionary<string, string>
{
    private readonly IReadOnlyList<KeyValuePair<string, string>> _items;
    private readonly Dictionary<string, string> _lookup;

    public OrderedCriteria(IReadOnlyList<KeyValuePair<string, string>> items)
    {
        _items = items;
        _lookup = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in items)
        {
            _lookup.TryAdd(k, v);
        }
    }

    public string this[string key] => _lookup[key];
    public IEnumerable<string> Keys => _items.Select(i => i.Key);
    public IEnumerable<string> Values => _items.Select(i => i.Value);
    public int Count => _items.Count;
    public bool ContainsKey(string key) => _lookup.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => _lookup.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
