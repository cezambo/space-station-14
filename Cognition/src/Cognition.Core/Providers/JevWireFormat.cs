using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Cognition.Core.Providers;

/// <summary>
/// Maps the internal contracts to the HTTP body of <c>POST /v1/systemone</c> and back.
/// Field names and shapes follow docs.typesafe.ai/api; see docs/jev-wire-format.md.
/// </summary>
public static class JevWireFormat
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>Deterministic JSON: fields and questions are written in a fixed order.</summary>
    public static string SerializeRequest(JevRequest request)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("model", request.Model);
            w.WriteString("state", request.State);
            w.WriteStartObject("questions");
            foreach (var (id, question) in request.Questions)
            {
                w.WriteStartObject(id);
                WriteQuestion(w, question);
                w.WriteEndObject();
            }

            w.WriteEndObject();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteQuestion(Utf8JsonWriter w, JevQuestion question)
    {
        switch (question)
        {
            case ChoiceQuestion c:
                w.WriteString("type", "choice");
                w.WriteString("instructions", c.Instructions);
                w.WriteStartObject("criteria");
                foreach (var (option, description) in c.Criteria)
                {
                    if (string.IsNullOrEmpty(description))
                        w.WriteNull(option);
                    else
                        w.WriteString(option, description);
                }

                w.WriteEndObject();
                break;
            case ScoreQuestion s:
                w.WriteString("type", "score");
                w.WriteString("instructions", s.Instructions);
                w.WriteStartArray("criteria");
                foreach (var level in s.Levels)
                {
                    w.WriteStringValue(level);
                }

                w.WriteEndArray();
                break;
            case NoulQuestion n:
                w.WriteString("type", "noul");
                w.WriteString("instructions", n.Instructions);
                if (!string.IsNullOrWhiteSpace(n.WhenTrue) && !string.IsNullOrWhiteSpace(n.WhenFalse))
                {
                    w.WriteStartObject("criteria");
                    w.WriteString("true", n.WhenTrue);
                    w.WriteString("false", n.WhenFalse);
                    w.WriteEndObject();
                }

                break;
            default:
                throw new ArgumentException($"unsupported question type {question.GetType().Name}");
        }
    }

    public sealed record ParsedResponse(string Model, IReadOnlyDictionary<string, JevAnswer> Answers, int InputTokens, int OutputTokens);

    /// <summary>Parses a 2xx body and checks it answers exactly the questions that were asked.</summary>
    public static ParsedResponse ParseResponse(string body, JevRequest request)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new JevProtocolException("body is not valid JSON", e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new JevProtocolException("body is not a JSON object");

            var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()!
                : string.Empty;

            if (!root.TryGetProperty("answers", out var answersEl) || answersEl.ValueKind != JsonValueKind.Object)
                throw new JevProtocolException("missing 'answers' object");

            var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
            foreach (var (id, question) in request.Questions)
            {
                if (!answersEl.TryGetProperty(id, out var a) || a.ValueKind != JsonValueKind.Object)
                    throw new JevProtocolException($"no answer for question '{id}'");
                answers[id] = ParseAnswer(id, question, a);
            }

            var inputTokens = 0;
            var outputTokens = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                inputTokens = usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt32(out var i) ? i : 0;
                outputTokens = usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt32(out var o) ? o : 0;
            }
            else
            {
                throw new JevProtocolException("missing 'usage' object");
            }

            return new ParsedResponse(model, answers, inputTokens, outputTokens);
        }
    }

    private static JevAnswer ParseAnswer(string id, JevQuestion question, JsonElement a)
    {
        var type = a.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (question)
        {
            case ChoiceQuestion c:
            {
                ExpectType(id, type, "choice");
                var choice = RequiredString(id, a, "choice");
                if (!c.Criteria.ContainsKey(choice))
                    throw new JevProtocolException($"question '{id}': choice '{choice}' is not one of the options");
                var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var p in RequiredObject(id, a, "probabilities").EnumerateObject())
                {
                    if (!c.Criteria.ContainsKey(p.Name))
                        throw new JevProtocolException($"question '{id}': probability for unknown option '{p.Name}'");
                    probabilities[p.Name] = p.Value.GetDouble();
                }

                return new ChoiceAnswer(choice, RequiredDouble(id, a, "confidence"), probabilities);
            }
            case ScoreQuestion s:
            {
                ExpectType(id, type, "score");
                var probabilities = new double[s.Levels.Count];
                foreach (var p in RequiredObject(id, a, "probabilities").EnumerateObject())
                {
                    if (!int.TryParse(p.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var level)
                        || level < 0 || level >= s.Levels.Count)
                        throw new JevProtocolException($"question '{id}': probability for unknown level '{p.Name}'");
                    probabilities[level] = p.Value.GetDouble();
                }

                var mostLikely = 0;
                for (var i = 1; i < probabilities.Length; i++)
                {
                    if (probabilities[i] > probabilities[mostLikely])
                        mostLikely = i;
                }

                return new ScoreAnswer(
                    RequiredDouble(id, a, "score"),
                    s.Levels[mostLikely],
                    RequiredDouble(id, a, "confidence"),
                    probabilities);
            }
            case NoulQuestion:
                ExpectType(id, type, "noul");
                return new NoulAnswer(RequiredDouble(id, a, "noul"));
            default:
                throw new ArgumentException($"unsupported question type {question.GetType().Name}");
        }
    }

    private static void ExpectType(string id, string? actual, string expected)
    {
        if (actual != expected)
            throw new JevProtocolException($"question '{id}': expected a {expected} answer, got '{actual}'");
    }

    private static string RequiredString(string id, JsonElement a, string name)
    {
        if (a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
            return v.GetString()!;
        throw new JevProtocolException($"question '{id}': missing string '{name}'");
    }

    private static double RequiredDouble(string id, JsonElement a, string name)
    {
        if (a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
            return v.GetDouble();
        throw new JevProtocolException($"question '{id}': missing number '{name}'");
    }

    private static JsonElement RequiredObject(string id, JsonElement a, string name)
    {
        if (a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object)
            return v;
        throw new JevProtocolException($"question '{id}': missing object '{name}'");
    }
}
