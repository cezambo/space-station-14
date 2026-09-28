using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Cognition.Core.Replay;

/// <summary>A recorded call: the canonical request and the client-level response, both as JSON.</summary>
public sealed record ReplayRecord(
    string Key,
    string Kind,
    string Purpose,
    string ModelId,
    string TemplateHash,
    string RequestJson,
    string ResponseJson);

/// <summary>A recording was required (<c>replay-strict</c>) but none exists for this request.</summary>
public sealed class ReplayMissException : Exception
{
    public string Purpose { get; }
    public string Key { get; }

    public ReplayMissException(string purpose, string key, string path)
        : base($"replay-strict: no recording for purpose '{purpose}' key {key[..16]}… (expected {path}). "
            + "Record it with a live run in record or replay mode.")
    {
        Purpose = purpose;
        Key = key;
    }
}

/// <summary>
/// RA-03 replay key: SHA-256 of the canonical request JSON, the prompt template hash and the model id,
/// joined by newlines so no two different triples collide by concatenation.
/// </summary>
public static class ReplayKey
{
    public static string Compute(string canonicalRequestJson, string templateHash, string modelId)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalRequestJson + "\n" + templateHash + "\n" + modelId);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }
}

public interface IReplayStore
{
    ReplayRecord? TryLoad(string purpose, string key);
    void Save(ReplayRecord record);
    string PathFor(string purpose, string key);
}

/// <summary>
/// One file per call at <c>{dir}/{purpose}/{key[..32]}.json</c>, indented, keys in fixed order, LF endings, so
/// recordings are reviewable in diffs and re-recording an unchanged call rewrites identical bytes.
/// </summary>
public sealed class FileReplayStore : IReplayStore
{
    private const int FileKeyLength = 32;

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly string _dir;

    public FileReplayStore(string dir)
    {
        _dir = dir;
    }

    public string PathFor(string purpose, string key) =>
        Path.Combine(_dir, SafeSegment(purpose), key[..FileKeyLength] + ".json");

    public ReplayRecord? TryLoad(string purpose, string key)
    {
        var path = PathFor(purpose, key);
        if (!File.Exists(path))
            return null;

        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var record = new ReplayRecord(
            root.GetProperty("key").GetString()!,
            root.GetProperty("kind").GetString()!,
            root.GetProperty("purpose").GetString()!,
            root.GetProperty("model_id").GetString()!,
            root.GetProperty("template_hash").GetString()!,
            root.GetProperty("request").GetRawText(),
            root.GetProperty("response").GetRawText());
        return record.Key == key ? record : null;
    }

    public void Save(ReplayRecord record)
    {
        var path = PathFor(record.Purpose, record.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, WriterOptions))
        {
            w.WriteStartObject();
            w.WriteString("key", record.Key);
            w.WriteString("kind", record.Kind);
            w.WriteString("purpose", record.Purpose);
            w.WriteString("model_id", record.ModelId);
            w.WriteString("template_hash", record.TemplateHash);
            w.WritePropertyName("request");
            WriteParsed(w, record.RequestJson);
            w.WritePropertyName("response");
            WriteParsed(w, record.ResponseJson);
            w.WriteEndObject();
        }

        buffer.WriteByte((byte)'\n');
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllBytes(temp, buffer.ToArray());
        File.Move(temp, path, overwrite: true);
    }

    private static void WriteParsed(Utf8JsonWriter w, string json)
    {
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.WriteTo(w);
    }

    private static string SafeSegment(string purpose)
    {
        var chars = purpose.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray();
        return chars.Length == 0 ? "_" : new string(chars);
    }
}
