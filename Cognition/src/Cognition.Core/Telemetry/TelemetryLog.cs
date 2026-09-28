using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cognition.Core.Providers;

namespace Cognition.Core.Telemetry;

/// <summary>
/// The character a call is made for, flowing across awaits. Set by the scheduler around each character's
/// turn: <c>using (TelemetryContext.ForCharacter(id)) { … }</c>.
/// </summary>
public static class TelemetryContext
{
    private static readonly AsyncLocal<string?> Current = new();

    public static string? CharacterId => Current.Value;

    public static IDisposable ForCharacter(string characterId)
    {
        var previous = Current.Value;
        Current.Value = characterId;
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }
}

/// <summary>One provider call (RNF-06). <see cref="UsdSpent"/> is 0 for replayed calls; <see cref="UsdRecorded"/> is not.</summary>
public sealed record CallRecord(
    double T,
    string? Character,
    string Role,
    string Purpose,
    string Model,
    string RequestId,
    bool Replayed,
    int InputTokens,
    int OutputTokens,
    int ReasoningTokens,
    decimal UsdSpent,
    decimal UsdRecorded,
    double LatencyMs,
    int Attempts,
    int SchemaRepairs,
    string? AnswersJson,
    string? Error);

/// <summary>A decision taken from Jev answers: what was done and how confident the deciding answer was.</summary>
public sealed record DecisionRecord(double T, string? Character, string Purpose, string Decision, double Confidence,
    string? Detail);

/// <summary>Size of one assembled decision context (RJ-08): estimated tokens per block and what was trimmed.</summary>
public sealed record ContextRecord(double T, string? Character, string Purpose, int EstimatedTokens, bool OverTarget,
    IReadOnlyList<(string Block, int Tokens)> Blocks, IReadOnlyList<(string Block, int Removed)> Trims,
    IReadOnlyList<string> OverBlockTarget);

public interface ITelemetrySink
{
    void Write(CallRecord record);
    void Write(DecisionRecord record);

    void Write(ContextRecord record)
    {
    }
}

/// <summary>
/// Writes records with a fixed key order and rounding, and takes time from an injected clock (simulation
/// seconds), so two replay-strict runs of a scenario produce byte-identical logs.
/// </summary>
public static class TelemetryJson
{
    private static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Line(CallRecord r) => Write(w =>
    {
        w.WriteNumber("t", Math.Round(r.T, 3));
        w.WriteString("kind", "call");
        WriteNullable(w, "character", r.Character);
        w.WriteString("role", r.Role);
        w.WriteString("purpose", r.Purpose);
        w.WriteString("model", r.Model);
        w.WriteString("request_id", r.RequestId);
        w.WriteBoolean("replayed", r.Replayed);
        w.WriteNumber("input_tokens", r.InputTokens);
        w.WriteNumber("output_tokens", r.OutputTokens);
        w.WriteNumber("reasoning_tokens", r.ReasoningTokens);
        w.WriteNumber("usd", r.UsdSpent);
        w.WriteNumber("usd_recorded", r.UsdRecorded);
        w.WriteNumber("latency_ms", Math.Round(r.LatencyMs, 1));
        w.WriteNumber("attempts", r.Attempts);
        w.WriteNumber("schema_repairs", r.SchemaRepairs);
        w.WritePropertyName("answers");
        if (r.AnswersJson is null)
            w.WriteNullValue();
        else
            w.WriteRawValue(r.AnswersJson);
        WriteNullable(w, "error", r.Error);
    });

    public static string Line(DecisionRecord r) => Write(w =>
    {
        w.WriteNumber("t", Math.Round(r.T, 3));
        w.WriteString("kind", "decision");
        WriteNullable(w, "character", r.Character);
        w.WriteString("purpose", r.Purpose);
        w.WriteString("decision", r.Decision);
        w.WriteNumber("confidence", Math.Round(r.Confidence, 4));
        WriteNullable(w, "detail", r.Detail);
    });

    public static string Line(ContextRecord r) => Write(w =>
    {
        w.WriteNumber("t", Math.Round(r.T, 3));
        w.WriteString("kind", "context");
        WriteNullable(w, "character", r.Character);
        w.WriteString("purpose", r.Purpose);
        w.WriteNumber("estimated_tokens", r.EstimatedTokens);
        w.WriteBoolean("over_target", r.OverTarget);
        w.WriteStartObject("blocks");
        foreach (var (block, tokens) in r.Blocks)
        {
            w.WriteNumber(block, tokens);
        }

        w.WriteEndObject();
        w.WriteStartObject("trims");
        foreach (var (block, removed) in r.Trims)
        {
            w.WriteNumber(block, removed);
        }

        w.WriteEndObject();
        w.WriteStartArray("over_block_target");
        foreach (var block in r.OverBlockTarget)
        {
            w.WriteStringValue(block);
        }

        w.WriteEndArray();
    });

    /// <summary>Compact per-question summary: choice + confidence, score level + confidence, or P(yes).</summary>
    public static string Answers(IReadOnlyDictionary<string, JevAnswer> answers) => Write(w =>
    {
        foreach (var (id, answer) in answers)
        {
            w.WriteStartObject(id);
            switch (answer)
            {
                case ChoiceAnswer c:
                    w.WriteString("choice", c.Choice);
                    w.WriteNumber("confidence", Math.Round(c.Confidence, 4));
                    break;
                case ScoreAnswer s:
                    w.WriteNumber("score", Math.Round(s.Score, 4));
                    w.WriteString("legend", s.Legend);
                    w.WriteNumber("confidence", Math.Round(s.Confidence, 4));
                    break;
                case NoulAnswer n:
                    w.WriteNumber("p_yes", Math.Round(n.PYes, 4));
                    break;
            }

            w.WriteEndObject();
        }
    });

    private static void WriteNullable(Utf8JsonWriter w, string name, string? value)
    {
        if (value is null)
            w.WriteNull(name);
        else
            w.WriteString(name, value);
    }

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, Options))
        {
            w.WriteStartObject();
            body(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

/// <summary>Appends one JSON object per line to a file; safe to share between clients and threads.</summary>
public sealed class JsonlTelemetrySink : ITelemetrySink, IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter _writer;

    public string Path { get; }

    public JsonlTelemetrySink(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
        _writer = new StreamWriter(path, append: true, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
    }

    /// <summary><c>{dir}/{runId}.jsonl</c>; the run id is chosen by the caller (scenario name, or a timestamp for live play).</summary>
    public static JsonlTelemetrySink ForRun(string dir, string runId) =>
        new(System.IO.Path.Combine(dir, runId + ".jsonl"));

    public void Write(CallRecord record) => WriteLine(TelemetryJson.Line(record));

    public void Write(DecisionRecord record) => WriteLine(TelemetryJson.Line(record));

    public void Write(ContextRecord record) => WriteLine(TelemetryJson.Line(record));

    private void WriteLine(string line)
    {
        lock (_lock)
        {
            _writer.WriteLine(line);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer.Dispose();
        }
    }
}

/// <summary>Fans records out to several sinks, e.g. the JSONL file and the <see cref="CostLedger"/>.</summary>
public sealed class CompositeTelemetrySink(params ITelemetrySink[] sinks) : ITelemetrySink
{
    public void Write(CallRecord record)
    {
        foreach (var s in sinks)
        {
            s.Write(record);
        }
    }

    public void Write(DecisionRecord record)
    {
        foreach (var s in sinks)
        {
            s.Write(record);
        }
    }

    public void Write(ContextRecord record)
    {
        foreach (var s in sinks)
        {
            s.Write(record);
        }
    }
}

/// <summary>Running totals for the cost panel (RM-06): tokens and USD per role and per character, and USD/hour.</summary>
public sealed class CostLedger : ITelemetrySink
{
    public sealed record Totals(int Calls, long InputTokens, long OutputTokens, decimal Usd);

    private readonly object _lock = new();
    private readonly Dictionary<string, Totals> _byRole = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Totals> _byCharacter = new(StringComparer.Ordinal);
    private Totals _all = new(0, 0, 0, 0m);

    public void Write(CallRecord record)
    {
        lock (_lock)
        {
            _all = Add(_all, record);
            _byRole[record.Role] = Add(_byRole.GetValueOrDefault(record.Role, new Totals(0, 0, 0, 0m)), record);
            var character = record.Character ?? "(none)";
            _byCharacter[character] = Add(_byCharacter.GetValueOrDefault(character, new Totals(0, 0, 0, 0m)), record);
        }
    }

    public void Write(DecisionRecord record)
    {
    }

    public Totals All
    {
        get
        {
            lock (_lock)
            {
                return _all;
            }
        }
    }

    public IReadOnlyDictionary<string, Totals> ByRole
    {
        get
        {
            lock (_lock)
            {
                return new SortedDictionary<string, Totals>(_byRole, StringComparer.Ordinal);
            }
        }
    }

    public IReadOnlyDictionary<string, Totals> ByCharacter
    {
        get
        {
            lock (_lock)
            {
                return new SortedDictionary<string, Totals>(_byCharacter, StringComparer.Ordinal);
            }
        }
    }

    /// <summary>USD per hour over <paramref name="elapsed"/> (wall time for live play, RC-04).</summary>
    public decimal UsdPerHour(TimeSpan elapsed) =>
        elapsed <= TimeSpan.Zero ? 0m : All.Usd / (decimal)elapsed.TotalHours;

    public string Summary(TimeSpan elapsed)
    {
        var sb = new StringBuilder();
        var all = All;
        sb.Append(CultureInfo.InvariantCulture,
            $"total: {all.Calls} calls, {all.InputTokens} in / {all.OutputTokens} out, ${all.Usd:0.000000}, ${UsdPerHour(elapsed):0.00}/h\n");
        foreach (var (role, t) in ByRole)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  role {role}: {t.Calls} calls, ${t.Usd:0.000000}\n");
        }

        foreach (var (character, t) in ByCharacter)
        {
            sb.Append(CultureInfo.InvariantCulture, $"  character {character}: {t.Calls} calls, ${t.Usd:0.000000}\n");
        }

        return sb.ToString();
    }

    private static Totals Add(Totals t, CallRecord r) =>
        new(t.Calls + 1, t.InputTokens + r.InputTokens, t.OutputTokens + r.OutputTokens, t.Usd + r.UsdSpent);
}
