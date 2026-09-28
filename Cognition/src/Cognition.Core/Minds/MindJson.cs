using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Cognition.Core.Minds;

public sealed class MindFormatException : Exception
{
    public MindFormatException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Strict JSON for minds (§6 layout, camelCase, enums as snake_case strings). Unknown or missing fields are
/// errors, so a typo in a fixture fails at load instead of becoming a default value.
/// </summary>
public static class MindJson
{
    public static readonly JsonSerializerOptions Options = Create(indented: false);
    public static readonly JsonSerializerOptions Indented = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        WriteIndented = indented,
        NewLine = "\n",
    };

    public static string Serialize<T>(T value, bool indented = false) =>
        JsonSerializer.Serialize(value, indented ? Indented : Options);

    public static T Deserialize<T>(string json, string what)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new MindFormatException($"{what}: null");
        }
        catch (JsonException ex)
        {
            throw new MindFormatException($"{what}: {ex.Message}", ex);
        }
    }

    /// <summary>The §6 document: mind fields with <c>memory: {recent, daily, fortnightly}</c> after <c>goals</c>.</summary>
    public static string SerializeSnapshot(MindSnapshot snapshot, bool indented = true)
    {
        var mind = JsonSerializer.SerializeToNode(snapshot.Mind, Options)!.AsObject();
        var memory = new JsonObject();
        foreach (var level in Enum.GetValues<MemoryLevel>())
        {
            memory[JsonNamingPolicy.CamelCase.ConvertName(level.ToString())] = JsonSerializer.SerializeToNode(
                snapshot.Memories.Where(m => m.Level == level).ToList(), Options);
        }

        var doc = new JsonObject();
        foreach (var (key, value) in mind.ToList())
        {
            mind.Remove(key);
            doc[key] = value;
            if (key == "goals")
                doc["memory"] = memory;
        }

        return doc.ToJsonString(indented ? Indented : Options);
    }

    public static MindSnapshot DeserializeSnapshot(string json, string what)
    {
        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(json)?.AsObject() ?? throw new MindFormatException($"{what}: null");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new MindFormatException($"{what}: {ex.Message}", ex);
        }

        if (doc["memory"] is not JsonObject memory)
            throw new MindFormatException($"{what}: 'memory' must be an object");
        doc.Remove("memory");
        var mind = Deserialize<Mind>(doc.ToJsonString(), what);

        var memories = new List<MemoryEntry>();
        foreach (var (key, value) in memory)
        {
            var level = Enum.GetValues<MemoryLevel>().FirstOrDefault(l => JsonNamingPolicy.CamelCase.ConvertName(l.ToString()) == key,
                (MemoryLevel)(-1));
            if (level < 0)
                throw new MindFormatException($"{what}: memory.{key} is not a memory level");
            var entries = Deserialize<List<MemoryEntry>>(value?.ToJsonString() ?? "null", $"{what} memory.{key}");
            if (entries.Any(e => e.Level != level))
                throw new MindFormatException($"{what}: memory.{key} holds an entry of another level");
            memories.AddRange(entries);
        }

        return new MindSnapshot(mind, memories);
    }
}
