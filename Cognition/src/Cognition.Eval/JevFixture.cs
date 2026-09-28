using System.Text.Json;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>
/// JSON request fixtures under <c>fixtures/jev/</c>. Stand-in until <c>PromptLibrary</c> (T1.07)
/// loads <c>prompts/jev/*.yaml</c>; keeps request text out of C# source (P7).
/// </summary>
internal sealed record JevFixture(
    string Purpose,
    string State,
    string? TinyState,
    IReadOnlyDictionary<string, JevQuestion> Questions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Sets)
{
    public static JevFixture Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var state = root.TryGetProperty("state_file", out var sf)
            ? File.ReadAllText(Path.Combine(dir, sf.GetString()!)).TrimEnd()
            : root.GetProperty("state").GetString()!;

        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal);
        foreach (var q in root.GetProperty("questions").EnumerateObject())
        {
            questions[q.Name] = ParseQuestion(q.Value);
        }

        var sets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (root.TryGetProperty("sets", out var setsEl))
        {
            foreach (var s in setsEl.EnumerateObject())
            {
                sets[s.Name] = s.Value.EnumerateArray().Select(e => e.GetString()!).ToList();
            }
        }

        return new JevFixture(
            root.GetProperty("purpose").GetString()!,
            state,
            root.TryGetProperty("tiny_state", out var ts) ? ts.GetString() : null,
            questions,
            sets);
    }

    public JevRequest Request(string model, IEnumerable<string>? questionIds = null, string? state = null)
    {
        var ids = questionIds?.ToList() ?? Questions.Keys.ToList();
        var selected = new Dictionary<string, JevQuestion>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            selected[id] = Questions[id];
        }

        return new JevRequest(model, state ?? State, selected, Purpose);
    }

    private static JevQuestion ParseQuestion(JsonElement q)
    {
        var instructions = q.GetProperty("instructions").GetString()!;
        return q.GetProperty("type").GetString() switch
        {
            "choice" => new ChoiceQuestion(instructions,
                q.GetProperty("criteria").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty)),
            "score" => new ScoreQuestion(instructions,
                q.GetProperty("criteria").EnumerateArray().Select(e => e.GetString()!).ToList()),
            "noul" => new NoulQuestion(instructions,
                q.TryGetProperty("when_true", out var t) ? t.GetString() : null,
                q.TryGetProperty("when_false", out var f) ? f.GetString() : null),
            var other => throw new InvalidDataException($"unknown question type '{other}'"),
        };
    }
}
