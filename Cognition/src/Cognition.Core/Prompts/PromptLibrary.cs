using Cognition.Core.Config;

namespace Cognition.Core.Prompts;

/// <summary>
/// Loads <c>prompts/jev/*.yaml</c> and <c>prompts/llm/*.md</c> (P7, RL-04). Every file is checked at load and
/// all problems are reported together. Files starting with <c>_</c> in <c>llm/</c> are fragments.
/// </summary>
public sealed class PromptLibrary
{
    public const string JsonReplyFragment = "_json_reply";
    public const string JsonRepairFragment = "_json_repair";

    private readonly Dictionary<string, JevTemplate> _jev;
    private readonly Dictionary<string, LlmTemplate> _llm;
    private readonly string _repair;

    public string RepairHash { get; }

    private PromptLibrary(Dictionary<string, JevTemplate> jev, Dictionary<string, LlmTemplate> llm, string repair,
        string repairHash)
    {
        _jev = jev;
        _llm = llm;
        _repair = repair;
        RepairHash = repairHash;
    }

    public IReadOnlyCollection<string> JevNames => _jev.Keys;
    public IReadOnlyCollection<string> LlmNames => _llm.Keys;

    public JevTemplate Jev(string name) =>
        _jev.TryGetValue(name, out var t) ? t : throw new KeyNotFoundException($"no Jev template 'jev/{name}.yaml'");

    public LlmTemplate Llm(string name) =>
        _llm.TryGetValue(name, out var t) ? t : throw new KeyNotFoundException($"no LLM template 'llm/{name}.md'");

    /// <summary>The schema repair message for <see cref="Providers.OpenAiCompatClientOptions.RepairMessage"/>.</summary>
    public string RepairMessage(string errors) => _repair.Replace("{{errors}}", errors, StringComparison.Ordinal);

    /// <param name="thresholds">When given, every <c>threshold_key</c> must exist under <c>[thresholds]</c>.</param>
    public static PromptLibrary Load(string promptsDir, ThresholdsConfig? thresholds = null)
    {
        var errors = new List<string>();
        var jevDir = Path.Combine(promptsDir, "jev");
        var llmDir = Path.Combine(promptsDir, "llm");
        if (!Directory.Exists(jevDir))
            errors.Add($"missing directory {jevDir}");
        if (!Directory.Exists(llmDir))
            errors.Add($"missing directory {llmDir}");
        if (errors.Count > 0)
            throw new PromptLoadException(errors);

        var jev = new Dictionary<string, JevTemplate>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(jevDir, "*.yaml").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var template = JevTemplate.Parse(name, File.ReadAllText(file), errors);
            if (template is null)
                continue;
            jev[name] = template;
            if (thresholds is null)
                continue;
            foreach (var q in template.Questions.Where(q => q.ThresholdKey is not null))
            {
                if (!thresholds.Probabilities.ContainsKey(q.ThresholdKey!) && !thresholds.ScoreLevels.ContainsKey(q.ThresholdKey!))
                    errors.Add($"jev/{name}.yaml: questions.{q.Id}.threshold_key '{q.ThresholdKey}' is not in [thresholds]");
            }
        }

        var jsonReply = Fragment(llmDir, JsonReplyFragment, "schema", errors);
        var repair = Fragment(llmDir, JsonRepairFragment, "errors", errors);

        var llm = new Dictionary<string, LlmTemplate>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(llmDir, "*.md").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (name.StartsWith('_'))
                continue;
            var template = LlmTemplate.Parse(name, File.ReadAllText(file), jsonReply, errors);
            if (template is not null)
                llm[name] = template;
        }

        if (errors.Count > 0)
            throw new PromptLoadException(errors);
        return new PromptLibrary(jev, llm, repair!, Placeholders.Hash(repair!));
    }

    /// <summary>A fragment file without its comment lines; it must use exactly one placeholder, <paramref name="placeholder"/>.</summary>
    private static string? Fragment(string dir, string name, string placeholder, List<string> errors)
    {
        var path = Path.Combine(dir, name + ".md");
        if (!File.Exists(path))
        {
            errors.Add($"missing fragment llm/{name}.md");
            return null;
        }

        var lines = File.ReadAllText(path).ReplaceLineEndings("\n").Split('\n')
            .Where(l => !(l.TrimStart().StartsWith("<!--", StringComparison.Ordinal) && l.TrimEnd().EndsWith("-->", StringComparison.Ordinal)));
        var text = string.Join("\n", lines).Trim('\n', ' ');
        var names = Placeholders.NamesIn(text);
        if (names.Count != 1 || names[0] != placeholder)
            errors.Add($"llm/{name}.md: must use exactly one placeholder, {{{{{placeholder}}}}}");
        return text;
    }
}
