using System.Text;
using Cognition.Core.Perception;
using YamlDotNet.RepresentationModel;

namespace Cognition.Core.Prompts;

/// <summary>
/// The words for each category from <see cref="Categorizers"/>, loaded from <c>prompts/vocabulary.yaml</c> (P7).
/// Load fails unless every enum value has a word, so a missing word is found at startup, not mid-session.
/// </summary>
public sealed class Vocabulary
{
    public const string FileName = "vocabulary.yaml";

    private static readonly (string Section, Type Enum)[] Sections =
    [
        ("distance", typeof(DistanceBand)),
        ("direction", typeof(Compass)),
        ("need_band", typeof(NeedBand)),
        ("day_phase", typeof(DayPhase)),
        ("budget_band", typeof(BudgetBand)),
        ("sensation", typeof(SensationKind)),
        ("damage_band", typeof(DamageBand)),
        ("speech_verb", typeof(SpeechVolume)),
    ];

    /// <summary>Need ids (RP-08) and the words shown for them.</summary>
    public static readonly string[] Needs = ["hunger", "thirst", "fatigue", "body_temperature", "oxygen"];

    /// <summary>Perception block phrases (T1.12) and the placeholders each must use.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> PhrasePlaceholders = new Dictionary<string, string[]>
    {
        ["seen_header"] = [],
        ["heard_header"] = [],
        ["environment_header"] = [],
        ["body_header"] = [],
        ["line"] = ["text"],
        ["field_separator"] = [],
        ["list_separator"] = [],
        ["list_and"] = [],
        ["nothing"] = [],
        ["none"] = [],
        ["known"] = ["name"],
        ["holding"] = ["items"],
        ["looks"] = ["trait"],
        ["here"] = [],
        ["position"] = ["distance", "direction"],
        ["heard_line"] = ["speaker", "position", "verb", "to_you", "text"],
        ["to_you"] = [],
        ["air_normal"] = [],
        ["need"] = ["need", "band"],
        ["damage"] = ["band", "type"],
        ["pain"] = ["band"],
        ["bleeding"] = [],
    };

    private readonly Dictionary<string, Dictionary<string, string>> _words;
    private readonly Dictionary<string, string> _gases;
    private readonly Dictionary<string, string> _needs;
    private readonly Dictionary<string, string> _phrases;

    private Vocabulary(Dictionary<string, Dictionary<string, string>> words, Dictionary<string, string> gases,
        Dictionary<string, string> needs, Dictionary<string, string> phrases, IReadOnlySet<string> stopwords)
    {
        _words = words;
        _gases = gases;
        _needs = needs;
        _phrases = phrases;
        GoalStopwords = stopwords;
        NoticeableGases = new HashSet<string>(gases.Keys, StringComparer.Ordinal);
    }

    /// <summary>Words ignored when matching goal text against what is perceived.</summary>
    public IReadOnlySet<string> GoalStopwords { get; }

    public string NeedName(string need) => _needs[need];

    /// <summary>A perception phrase with its placeholders filled (values are inserted literally).</summary>
    public string Phrase(string key, params (string Name, string Value)[] values)
    {
        var text = _phrases[key];
        foreach (var (name, value) in values)
        {
            text = text.Replace("{{" + name + "}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <summary>Gas ids that can be sensed; pass to <see cref="Categorizers.Environment"/>.</summary>
    public IReadOnlySet<string> NoticeableGases { get; }

    public string Word(DistanceBand v) => Lookup("distance", v);
    public string Word(Compass v) => Lookup("direction", v);
    public string Word(NeedBand v) => Lookup("need_band", v);
    public string Word(DayPhase v) => Lookup("day_phase", v);
    public string Word(BudgetBand v) => Lookup("budget_band", v);
    public string Word(DamageBand v) => Lookup("damage_band", v);
    public string Word(SpeechVolume v) => Lookup("speech_verb", v);

    public string Word(Sensation s) => s.Kind switch
    {
        SensationKind.Gas => _gases.TryGetValue(s.Subject ?? string.Empty, out var g)
            ? g
            : throw new KeyNotFoundException($"{FileName}: no gas '{s.Subject}'"),
        SensationKind.Puddle => Lookup("sensation", s.Kind).Replace("{{reagent}}", s.Subject ?? "something", StringComparison.Ordinal),
        _ => Lookup("sensation", s.Kind),
    };

    private string Lookup(string section, Enum value) => _words[section][SnakeCase(value.ToString())];

    public static Vocabulary Load(string promptsDir)
    {
        var path = Path.Combine(promptsDir, FileName);
        var errors = new List<string>();
        if (!File.Exists(path))
            throw new PromptLoadException([$"missing {path}"]);

        var vocabulary = Parse(File.ReadAllText(path), errors);
        if (errors.Count > 0)
            throw new PromptLoadException(errors);
        return vocabulary!;
    }

    internal static Vocabulary? Parse(string text, List<string> errors)
    {
        YamlMappingNode root;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
            if (stream.Documents.Count != 1 || stream.Documents[0].RootNode is not YamlMappingNode m)
            {
                errors.Add($"{FileName}: must be one YAML mapping");
                return null;
            }

            root = m;
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            errors.Add($"{FileName}: {ex.Message}");
            return null;
        }

        var words = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (section, enumType) in Sections)
        {
            var map = Section(root, section, errors);
            var expected = Enum.GetNames(enumType).Where(n => !(enumType == typeof(SensationKind) && n == nameof(SensationKind.Gas)))
                .Select(SnakeCase).ToList();
            foreach (var key in expected.Where(k => !map.ContainsKey(k)))
            {
                errors.Add($"{FileName}: {section}.{key} is missing");
            }

            foreach (var key in map.Keys.Where(k => !expected.Contains(k)))
            {
                errors.Add($"{FileName}: {section}.{key} is not a {enumType.Name} value");
            }

            words[section] = map;
        }

        var gases = Section(root, "gas", errors);
        var needs = Section(root, "need", errors);
        foreach (var n in Needs.Where(n => !needs.ContainsKey(n)))
        {
            errors.Add($"{FileName}: need.{n} is missing");
        }

        var phrases = Section(root, "perception", errors);
        foreach (var (key, names) in PhrasePlaceholders)
        {
            if (!phrases.TryGetValue(key, out var phrase))
            {
                errors.Add($"{FileName}: perception.{key} is missing");
                continue;
            }

            var used = Placeholders.NamesIn(phrase).Order(StringComparer.Ordinal);
            if (!used.SequenceEqual(names.Order(StringComparer.Ordinal)))
                errors.Add($"{FileName}: perception.{key} must use exactly {(names.Length == 0 ? "no placeholders" : string.Join(", ", names.Select(n => "{{" + n + "}}")))}");
        }

        foreach (var key in phrases.Keys.Where(k => !PhrasePlaceholders.ContainsKey(k)))
        {
            errors.Add($"{FileName}: perception.{key} is not a known phrase");
        }

        var stopwords = new HashSet<string>(StringComparer.Ordinal);
        if (root.Children.TryGetValue(new YamlScalarNode("goal_stopwords"), out var sw) && sw is YamlSequenceNode seq)
            stopwords.UnionWith(seq.Children.OfType<YamlScalarNode>().Select(n => (n.Value ?? string.Empty).ToLowerInvariant()));
        else
            errors.Add($"{FileName}: 'goal_stopwords' must be a list");

        var known = Sections.Select(s => s.Section).Concat(["gas", "need", "perception", "goal_stopwords"])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in root.Children.Keys.Select(k => k.ToString()).Where(k => !known.Contains(k)))
        {
            errors.Add($"{FileName}: unknown section '{key}'");
        }

        return new Vocabulary(words, gases, needs, phrases, stopwords);
    }

    private static Dictionary<string, string> Section(YamlMappingNode root, string name, List<string> errors)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!root.Children.TryGetValue(new YamlScalarNode(name), out var node) || node is not YamlMappingNode m)
        {
            errors.Add($"{FileName}: '{name}' must be a mapping");
            return map;
        }

        foreach (var (k, v) in m.Children)
        {
            var value = v is YamlScalarNode s ? s.Value : null;
            if (string.IsNullOrWhiteSpace(value))
                errors.Add($"{FileName}: {name}.{k} must be a non-empty string");
            map[k.ToString()] = value ?? string.Empty;
        }

        return map;
    }

    internal static string SnakeCase(string pascal)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < pascal.Length; i++)
        {
            if (char.IsUpper(pascal[i]) && i > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(pascal[i]));
        }

        return sb.ToString();
    }
}
