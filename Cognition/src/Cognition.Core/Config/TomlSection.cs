using System.Text.RegularExpressions;
using Tomlyn.Model;

namespace Cognition.Core.Config;

/// <summary>
/// Reads one TOML table with typed getters, collecting errors instead of throwing so the
/// loader can report every problem at once. Tracks read keys to flag unknown ones.
/// </summary>
internal sealed partial class TomlSection
{
    private readonly TomlTable _table;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);
    private readonly List<TomlSection> _children = new();

    public string Path { get; }
    public List<string> Errors { get; }

    public TomlSection(TomlTable table, string path, List<string> errors)
    {
        _table = table;
        Path = path;
        Errors = errors;
    }

    private string Full(string key) => Path.Length == 0 ? key : $"{Path}.{key}";

    private bool TryGet(string key, out object value)
    {
        _read.Add(key);
        if (_table.TryGetValue(key, out var raw) && raw is not null)
        {
            value = raw;
            return true;
        }

        Errors.Add($"{Full(key)}: missing");
        value = null!;
        return false;
    }

    private void TypeError(string key, string expected, object actual)
    {
        Errors.Add($"{Full(key)}: expected {expected}, got {actual.GetType().Name}");
    }

    public TomlSection Table(string key)
    {
        if (TryGet(key, out var raw) && raw is TomlTable t)
        {
            var child = new TomlSection(t, Full(key), Errors);
            _children.Add(child);
            return child;
        }

        if (raw is not null)
            TypeError(key, "table", raw);

        var empty = new TomlSection(new TomlTable(), Full(key), Errors);
        return empty;
    }

    public string String(string key)
    {
        if (!TryGet(key, out var raw))
            return string.Empty;
        if (raw is string s)
            return s;
        TypeError(key, "string", raw);
        return string.Empty;
    }

    public long Long(string key)
    {
        if (!TryGet(key, out var raw))
            return 0;
        if (raw is long l)
            return l;
        TypeError(key, "integer", raw);
        return 0;
    }

    public int Int(string key)
    {
        var value = Long(key);
        if (value is < int.MinValue or > int.MaxValue)
        {
            Errors.Add($"{Full(key)}: out of range");
            return 0;
        }

        return (int)value;
    }

    public double Double(string key)
    {
        if (!TryGet(key, out var raw))
            return 0;
        return raw switch
        {
            double d => d,
            long l => l,
            _ => TypeErrorDefault(key, "number", raw),
        };
    }

    public decimal Decimal(string key)
    {
        return (decimal)Double(key);
    }

    private double TypeErrorDefault(string key, string expected, object raw)
    {
        TypeError(key, expected, raw);
        return 0;
    }

    public TEnum Enum<TEnum>(string key, IReadOnlyDictionary<string, TEnum> allowed) where TEnum : struct
    {
        var value = String(key);
        if (allowed.TryGetValue(value, out var parsed))
            return parsed;
        if (value.Length > 0)
            Errors.Add($"{Full(key)}: '{value}' is not one of: {string.Join(", ", allowed.Keys)}");
        return default;
    }

    public IReadOnlyList<string> StringList(string key)
    {
        if (!TryGet(key, out var raw))
            return [];
        if (raw is not TomlArray arr)
        {
            TypeError(key, "array of strings", raw);
            return [];
        }

        var list = new List<string>();
        foreach (var item in arr)
        {
            if (item is string s)
                list.Add(s);
            else
                Errors.Add($"{Full(key)}: every element must be a string");
        }

        return list;
    }

    public IReadOnlyList<int> IntList(string key)
    {
        if (!TryGet(key, out var raw))
            return [];
        if (raw is not TomlArray arr)
        {
            TypeError(key, "array of integers", raw);
            return [];
        }

        var list = new List<int>();
        foreach (var item in arr)
        {
            if (item is long l and >= int.MinValue and <= int.MaxValue)
                list.Add((int)l);
            else
                Errors.Add($"{Full(key)}: every element must be an integer");
        }

        return list;
    }

    /// <summary>Every key of the table must hold a number (integers are accepted).</summary>
    public IReadOnlyDictionary<string, double> DoubleMap(string key)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (k, v) in Entries(key))
        {
            switch (v)
            {
                case double d:
                    map[k] = d;
                    break;
                case long l:
                    map[k] = l;
                    break;
                default:
                    Errors.Add($"{Full(key)}.{k}: expected number");
                    break;
            }
        }

        return map;
    }

    public IReadOnlyDictionary<string, int> IntMap(string key)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (k, v) in Entries(key))
        {
            if (v is long l and >= int.MinValue and <= int.MaxValue)
                map[k] = (int)l;
            else
                Errors.Add($"{Full(key)}.{k}: expected integer");
        }

        return map;
    }

    private IEnumerable<KeyValuePair<string, object>> Entries(string key)
    {
        if (!TryGet(key, out var raw))
            return [];
        if (raw is TomlTable t)
            return t;
        TypeError(key, "table", raw);
        return [];
    }

    /// <summary>All entries of this table, marking every key as read.</summary>
    public IEnumerable<KeyValuePair<string, object>> AllEntries()
    {
        foreach (var entry in _table)
        {
            _read.Add(entry.Key);
            yield return entry;
        }
    }

    /// <summary>Reports keys that no getter consumed, here and in child tables.</summary>
    public void ReportUnknownKeys()
    {
        foreach (var key in _table.Keys)
        {
            if (_read.Contains(key))
                continue;
            Errors.Add(SecretLikeKey().IsMatch(key)
                ? $"{Full(key)}: secrets must not be stored in the config file; name an env var in api_key_env instead (RM-05)"
                : $"{Full(key)}: unknown key");
        }

        foreach (var child in _children)
        {
            child.ReportUnknownKeys();
        }
    }

    [GeneratedRegex("(^|_)(key|token|secret|password)$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretLikeKey();
}
