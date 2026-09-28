using System.Text;
using Cognition.Core.Providers;

namespace Cognition.Core.Prompts;

public sealed record RenderedLlm(LlmRequest Request, IReadOnlyList<string> Warnings);

/// <summary>An <c>llm/*.md</c> template: <c>## SYSTEM</c>, <c>## USER</c>, optional <c>## SCHEMA</c>.</summary>
public sealed class LlmTemplate
{
    public string Name { get; }
    public string System { get; }
    public string User { get; }

    /// <summary>Compact schema JSON, or null for plain-text replies.</summary>
    public string? Schema { get; }

    public string Hash { get; }

    private readonly IReadOnlyList<string> _tags;

    private LlmTemplate(string name, string system, string user, string? schema, string hash)
    {
        Name = name;
        System = system;
        User = user;
        Schema = schema;
        Hash = hash;
        _tags = Placeholders.TagsIn(system + "\n" + user);
    }

    public IReadOnlyList<string> PlaceholderNames => Placeholders.NamesIn(System + "\n" + User);

    public RenderedLlm Render(IReadOnlyDictionary<string, string> values, LlmRole role, int maxOutputTokens)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var system = Placeholders.Render(System, values, _tags, used, missing);
        var user = Placeholders.Render(User, values, _tags, used, missing);
        if (missing.Count > 0)
            throw new PromptRenderException(Name, missing.ToList());

        var warnings = values.Keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal)
            .Select(k => $"template '{Name}': value '{k}' is not used by any placeholder").ToList();
        var request = new LlmRequest(role, system, user, Schema, maxOutputTokens, Name) { TemplateHash = Hash };
        return new RenderedLlm(request, warnings);
    }

    /// <summary>
    /// Parses one file. <paramref name="jsonReply"/> (the <c>_json_reply.md</c> fragment, with <c>{{schema}}</c>)
    /// is appended to SYSTEM when there is a SCHEMA, and is part of the hash.
    /// </summary>
    internal static LlmTemplate? Parse(string name, string text, string? jsonReply, List<string> errors)
    {
        var sections = new Dictionary<string, StringBuilder>(StringComparer.Ordinal);
        StringBuilder? current = null;
        var lineNo = 0;
        foreach (var raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            lineNo++;
            var line = raw.TrimEnd();
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var header = line[3..].Trim();
                if (header is not ("SYSTEM" or "USER" or "SCHEMA"))
                {
                    errors.Add($"llm/{name}.md:{lineNo}: unknown section '## {header}'");
                    return null;
                }

                if (sections.ContainsKey(header))
                {
                    errors.Add($"llm/{name}.md:{lineNo}: duplicate section '## {header}'");
                    return null;
                }

                current = sections[header] = new StringBuilder();
                continue;
            }

            if (current is null)
            {
                if (line.Length > 0 && !(line.StartsWith("<!--", StringComparison.Ordinal) && line.EndsWith("-->", StringComparison.Ordinal)))
                    errors.Add($"llm/{name}.md:{lineNo}: text before the first section");
                continue;
            }

            current.Append(raw).Append('\n');
        }

        string? Section(string key) => sections.TryGetValue(key, out var sb) ? sb.ToString().Trim('\n', ' ') : null;
        var system = Section("SYSTEM");
        var user = Section("USER");
        var schemaText = Section("SCHEMA");
        if (string.IsNullOrWhiteSpace(system))
            errors.Add($"llm/{name}.md: missing or empty '## SYSTEM'");
        if (string.IsNullOrWhiteSpace(user))
            errors.Add($"llm/{name}.md: missing or empty '## USER'");
        if (system is null || user is null)
            return null;

        string? schema = null;
        if (schemaText is not null)
        {
            try
            {
                schema = JsonSchemaLite.Parse(schemaText).CanonicalJson;
            }
            catch (JsonSchemaDefinitionException e)
            {
                errors.Add($"llm/{name}.md: SCHEMA: {e.Message}");
                return null;
            }

            if (jsonReply is null)
            {
                errors.Add($"llm/{name}.md: has a SCHEMA but llm/_json_reply.md is missing");
                return null;
            }

            system = system + "\n\n" + jsonReply.Replace("{{schema}}", schema, StringComparison.Ordinal);
        }

        var hash = schema is null ? Placeholders.Hash(text) : Placeholders.Hash(text, jsonReply!);
        return new LlmTemplate(name, system, user, schema, hash);
    }
}
