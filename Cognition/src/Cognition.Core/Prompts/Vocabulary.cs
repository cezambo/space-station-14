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
    ];

    private readonly Dictionary<string, Dictionary<string, string>> _words;
    private readonly Dictionary<string, string> _gases;

    private Vocabulary(Dictionary<string, Dictionary<string, string>> words, Dictionary<string, string> gases)
    {
        _words = words;
        _gases = gases;
        NoticeableGases = new HashSet<string>(gases.Keys, StringComparer.Ordinal);
    }

    /// <summary>Gas ids that can be sensed; pass to <see cref="Categorizers.Environment"/>.</summary>
    public IReadOnlySet<string> NoticeableGases { get; }

    public string Word(DistanceBand v) => Lookup("distance", v);
    public string Word(Compass v) => Lookup("direction", v);
    public string Word(NeedBand v) => Lookup("need_band", v);
    public string Word(DayPhase v) => Lookup("day_phase", v);
    public string Word(BudgetBand v) => Lookup("budget_band", v);

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
        var known = Sections.Select(s => s.Section).Append("gas").ToHashSet(StringComparer.Ordinal);
        foreach (var key in root.Children.Keys.Select(k => k.ToString()).Where(k => !known.Contains(k)))
        {
            errors.Add($"{FileName}: unknown section '{key}'");
        }

        return new Vocabulary(words, gases);
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
