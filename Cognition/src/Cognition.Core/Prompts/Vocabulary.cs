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
        ("action", typeof(ActionVerb)),
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

    /// <summary>Decision menu option descriptions (T1.15, RJ-16/RJ-20), with the placeholders each must use.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> MenuPlaceholders = new Dictionary<string, string[]>
    {
        ["category_nothing"] = [],
        ["category_move"] = [],
        ["category_interact"] = [],
        ["category_use_item"] = [],
        ["category_inventory"] = [],
        ["category_speak"] = [],
        ["category_think"] = [],
        ["category_sleep"] = [],
        ["at"] = ["text", "position"],
        ["in_hand"] = [],
        ["walk_to"] = ["target"],
        ["none_of_these"] = [],
        ["walk_direction"] = ["direction"],
        ["open"] = ["target"],
        ["close"] = ["target"],
        ["lock"] = ["target", "item"],
        ["unlock"] = ["target", "item"],
        ["wake"] = ["target"],
        ["use"] = ["item"],
        ["use_on"] = ["item", "target"],
        ["eat"] = ["item"],
        ["drink"] = ["item"],
        ["pickup"] = ["item"],
        ["drop"] = ["item"],
        ["put"] = ["item", "target"],
        ["take"] = ["item", "target"],
        ["give"] = ["item", "target"],
        ["speak_to"] = ["target"],
        ["speak_everyone"] = [],
        ["think_light"] = [],
        ["think_deep"] = [],
        ["sleep_here"] = [],
        ["sleep_this_bed"] = [],
        ["sleep_bed"] = ["target"],
        ["nearest"] = ["text"],
    };

    /// <summary>Recent-memory sentences (T1.21, RMe-01). A count is a word, never a number sent to Jev.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> MemoryPlaceholders = new Dictionary<string, string[]>
    {
        ["did"] = ["verb", "what"],
        ["did_again"] = ["verb", "what", "times"],
        ["failed"] = ["verb", "what"],
        ["failed_again"] = ["verb", "what", "times"],
        ["failed_because"] = ["verb", "what", "reason"],
        ["failed_because_again"] = ["verb", "what", "reason", "times"],
        ["said"] = ["text"],
        ["thought"] = ["text"],
        ["hurt"] = [],
        ["hurt_again"] = ["times"],
        ["hurt_kind"] = ["kind"],
        ["hurt_kind_again"] = ["kind", "times"],
        ["fell_asleep"] = [],
        ["fell_asleep_where"] = ["where"],
        ["woke"] = [],
        ["collapsed"] = [],
        ["collapsed_again"] = ["times"],
        ["unconscious"] = [],
        ["unconscious_again"] = ["times"],
        ["give_what"] = ["item", "target"],
        ["put_what"] = ["item", "target"],
        ["take_what"] = ["item", "target"],
        ["lock_what"] = ["target", "item"],
        ["use_what"] = ["item", "target"],
        ["something"] = [],
        ["times_couple"] = [],
        ["times_several"] = [],
        ["times_many"] = [],
    };

    private readonly Dictionary<string, Dictionary<string, string>> _words;
    private readonly Dictionary<string, string> _gases;
    private readonly Dictionary<string, string> _needs;
    private readonly Dictionary<string, string> _phrases;
    private readonly Dictionary<string, string> _menu;
    private readonly Dictionary<string, string> _memory;

    private Vocabulary(Dictionary<string, Dictionary<string, string>> words, Dictionary<string, string> gases,
        Dictionary<string, string> needs, Dictionary<string, string> phrases, Dictionary<string, string> menu,
        Dictionary<string, string> memory, IReadOnlySet<string> stopwords)
    {
        _menu = menu;
        _memory = memory;
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
    public string Phrase(string key, params (string Name, string Value)[] values) => Fill(_phrases[key], values);

    /// <summary>A menu option description with its placeholders filled (values are inserted literally).</summary>
    public string Menu(string key, params (string Name, string Value)[] values) => Fill(_menu[key], values);

    /// <summary>A recent-memory sentence with its placeholders filled (values are inserted literally).</summary>
    public string Memory(string key, params (string Name, string Value)[] values) => Fill(_memory[key], values);

    private static string Fill(string text, (string Name, string Value)[] values)
    {
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
    public string Word(ActionVerb v) => Lookup("action", v);

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

        var phrases = Phrases(root, "perception", PhrasePlaceholders, errors);
        var menu = Phrases(root, "menu", MenuPlaceholders, errors);
        var memory = Phrases(root, "memory", MemoryPlaceholders, errors);

        var stopwords = new HashSet<string>(StringComparer.Ordinal);
        if (root.Children.TryGetValue(new YamlScalarNode("goal_stopwords"), out var sw) && sw is YamlSequenceNode seq)
            stopwords.UnionWith(seq.Children.OfType<YamlScalarNode>().Select(n => (n.Value ?? string.Empty).ToLowerInvariant()));
        else
            errors.Add($"{FileName}: 'goal_stopwords' must be a list");

        var known = Sections.Select(s => s.Section).Concat(["gas", "need", "perception", "menu", "memory", "goal_stopwords"])
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in root.Children.Keys.Select(k => k.ToString()).Where(k => !known.Contains(k)))
        {
            errors.Add($"{FileName}: unknown section '{key}'");
        }

        return new Vocabulary(words, gases, needs, phrases, menu, memory, stopwords);
    }

    private static Dictionary<string, string> Phrases(YamlMappingNode root, string section,
        IReadOnlyDictionary<string, string[]> spec, List<string> errors)
    {
        var phrases = Section(root, section, errors);
        foreach (var (key, names) in spec)
        {
            if (!phrases.TryGetValue(key, out var phrase))
            {
                errors.Add($"{FileName}: {section}.{key} is missing");
                continue;
            }

            var used = Placeholders.NamesIn(phrase).Order(StringComparer.Ordinal);
            if (!used.SequenceEqual(names.Order(StringComparer.Ordinal)))
                errors.Add($"{FileName}: {section}.{key} must use exactly {(names.Length == 0 ? "no placeholders" : string.Join(", ", names.Select(n => "{{" + n + "}}")))}");
        }

        foreach (var key in phrases.Keys.Where(k => !spec.ContainsKey(k)))
        {
            errors.Add($"{FileName}: {section}.{key} is not a known phrase");
        }

        return phrases;
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
