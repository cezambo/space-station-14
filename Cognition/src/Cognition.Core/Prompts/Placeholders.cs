using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Cognition.Core.Prompts;

public sealed class PromptLoadException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public PromptLoadException(IReadOnlyList<string> errors)
        : base("Prompt library failed to load:\n  " + string.Join("\n  ", errors))
    {
        Errors = errors;
    }
}

/// <summary>A render left placeholders without a value (or had nothing to ask); nothing was sent.</summary>
public sealed class PromptRenderException : Exception
{
    public string Template { get; }
    public IReadOnlyList<string> Missing { get; }

    public PromptRenderException(string template, IReadOnlyList<string> missing, string? message = null)
        : base(message ?? $"template '{template}': no value for placeholder(s) {string.Join(", ", missing.Select(m => "{{" + m + "}}"))}")
    {
        Template = template;
        Missing = missing;
    }
}

/// <summary>
/// <c>{{name}}</c> substitution. Single pass, so braces inside a value are never expanded. Values cannot open
/// or close any <c>&lt;tag&gt;</c> the template itself uses, so quoted speech stays inside its data region.
/// </summary>
public static partial class Placeholders
{
    public static IReadOnlyList<string> NamesIn(string template) =>
        Pattern().Matches(template).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    public static IReadOnlyList<string> TagsIn(string template) =>
        Tag().Matches(template).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Records every placeholder used in <paramref name="used"/> and every one lacking a value in <paramref name="missing"/>.</summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values, IReadOnlyList<string> guardedTags,
        ISet<string> used, ISet<string> missing)
    {
        return Pattern().Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            used.Add(name);
            if (values.TryGetValue(name, out var value))
                return Neutralize(value, guardedTags);
            missing.Add(name);
            return m.Value;
        });
    }

    private static string Neutralize(string value, IReadOnlyList<string> tags)
    {
        foreach (var tag in tags)
        {
            value = Regex.Replace(value, $@"<\s*/?\s*{Regex.Escape(tag)}\s*>", $"({tag})", RegexOptions.IgnoreCase);
        }

        return value;
    }

    /// <summary>SHA-256 of the text with line endings normalized, so a checkout on Windows hashes the same.</summary>
    public static string Hash(params string[] parts)
    {
        var text = string.Join("\n\u0000\n", parts.Select(p => p.ReplaceLineEndings("\n")));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    [GeneratedRegex(@"\{\{([A-Za-z_][A-Za-z0-9_]*)\}\}")]
    private static partial Regex Pattern();

    [GeneratedRegex(@"</([A-Za-z_][A-Za-z0-9_]*)>")]
    private static partial Regex Tag();
}
