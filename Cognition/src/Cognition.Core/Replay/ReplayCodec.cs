using System.Text;
using System.Text.Json;
using Cognition.Core.Providers;

namespace Cognition.Core.Replay;

/// <summary>Round-trips client-level responses through the recording's <c>response</c> object.</summary>
internal static class ReplayCodec
{
    public static string WriteJev(JevResponse r) => Write(w =>
    {
        w.WriteString("request_id", r.RequestId);
        w.WriteString("model", r.Model);
        w.WriteNumber("latency_ms", Math.Round(r.Latency.TotalMilliseconds, 3));
        w.WriteNumber("attempts", r.Attempts);
        WriteUsage(w, r.Usage);
        w.WriteStartObject("answers");
        foreach (var (id, answer) in r.Answers)
        {
            w.WriteStartObject(id);
            switch (answer)
            {
                case ChoiceAnswer c:
                    w.WriteString("type", "choice");
                    w.WriteString("choice", c.Choice);
                    w.WriteNumber("confidence", c.Confidence);
                    w.WriteStartObject("probabilities");
                    foreach (var (option, p) in c.Probabilities)
                    {
                        w.WriteNumber(option, p);
                    }

                    w.WriteEndObject();
                    break;
                case ScoreAnswer s:
                    w.WriteString("type", "score");
                    w.WriteNumber("score", s.Score);
                    w.WriteString("legend", s.Legend);
                    w.WriteNumber("confidence", s.Confidence);
                    w.WriteStartArray("probabilities");
                    foreach (var p in s.Probabilities)
                    {
                        w.WriteNumberValue(p);
                    }

                    w.WriteEndArray();
                    break;
                case NoulAnswer n:
                    w.WriteString("type", "noul");
                    w.WriteNumber("p_yes", n.PYes);
                    break;
                default:
                    throw new InvalidOperationException($"unknown answer type {answer.GetType().Name}");
            }

            w.WriteEndObject();
        }

        w.WriteEndObject();
    });

    public static JevResponse ReadJev(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (var a in root.GetProperty("answers").EnumerateObject())
        {
            var v = a.Value;
            answers[a.Name] = v.GetProperty("type").GetString() switch
            {
                "choice" => new ChoiceAnswer(
                    v.GetProperty("choice").GetString()!,
                    v.GetProperty("confidence").GetDouble(),
                    v.GetProperty("probabilities").EnumerateObject()
                        .ToDictionary(p => p.Name, p => p.Value.GetDouble(), StringComparer.Ordinal)),
                "score" => new ScoreAnswer(
                    v.GetProperty("score").GetDouble(),
                    v.GetProperty("legend").GetString()!,
                    v.GetProperty("confidence").GetDouble(),
                    v.GetProperty("probabilities").EnumerateArray().Select(p => p.GetDouble()).ToList()),
                "noul" => new NoulAnswer(v.GetProperty("p_yes").GetDouble()),
                var t => throw new InvalidDataException($"recording has unknown answer type '{t}'"),
            };
        }

        return new JevResponse(
            root.GetProperty("request_id").GetString()!,
            TimeSpan.FromMilliseconds(root.GetProperty("latency_ms").GetDouble()),
            answers,
            ReadUsage(root))
        {
            Model = root.GetProperty("model").GetString()!,
            Attempts = root.GetProperty("attempts").GetInt32(),
            Replayed = true,
        };
    }

    public static string WriteLlm(LlmResponse r) => Write(w =>
    {
        w.WriteString("request_id", r.RequestId);
        w.WriteString("model", r.ModelId);
        w.WriteNumber("latency_ms", Math.Round(r.Latency.TotalMilliseconds, 3));
        w.WriteNumber("attempts", r.Attempts);
        w.WriteNumber("schema_repairs", r.SchemaRepairs);
        w.WriteNumber("reasoning_tokens", r.ReasoningTokens);
        w.WriteBoolean("cost_reported", r.CostReported);
        WriteUsage(w, r.Usage);
        w.WriteString("text", r.Text);
    });

    public static LlmResponse ReadLlm(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new LlmResponse(root.GetProperty("text").GetString()!, ReadUsage(root), root.GetProperty("model").GetString()!)
        {
            RequestId = root.GetProperty("request_id").GetString()!,
            Latency = TimeSpan.FromMilliseconds(root.GetProperty("latency_ms").GetDouble()),
            Attempts = root.GetProperty("attempts").GetInt32(),
            SchemaRepairs = root.GetProperty("schema_repairs").GetInt32(),
            ReasoningTokens = root.GetProperty("reasoning_tokens").GetInt32(),
            CostReported = root.GetProperty("cost_reported").GetBoolean(),
            Replayed = true,
        };
    }

    private static void WriteUsage(Utf8JsonWriter w, UsageInfo u)
    {
        w.WriteStartObject("usage");
        w.WriteNumber("input_tokens", u.InputTokens);
        w.WriteNumber("output_tokens", u.OutputTokens);
        w.WriteNumber("cost_usd", u.CostUsd);
        w.WriteEndObject();
    }

    private static UsageInfo ReadUsage(JsonElement root)
    {
        var u = root.GetProperty("usage");
        return new UsageInfo(u.GetProperty("input_tokens").GetInt32(), u.GetProperty("output_tokens").GetInt32(),
            u.GetProperty("cost_usd").GetDecimal());
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
