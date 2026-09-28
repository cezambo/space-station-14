using System.Globalization;
using System.Text.Json;

namespace Cognition.Core.Providers;

/// <summary>
/// Validator for the JSON Schema subset used by <c>prompts/llm/*.md</c>: <c>type</c>, <c>properties</c>,
/// <c>required</c>, <c>additionalProperties</c> (boolean), <c>items</c>, <c>minItems</c>, <c>maxItems</c>,
/// <c>enum</c>, <c>minimum</c>, <c>maximum</c>, <c>minLength</c>, <c>maxLength</c>; <c>description</c>,
/// <c>title</c> and <c>$schema</c> are annotations. Any other keyword is a definition error, so a schema is
/// never silently half-enforced.
/// </summary>
public sealed class JsonSchemaLite
{
    private static readonly HashSet<string> Types = ["object", "array", "string", "integer", "number", "boolean", "null"];

    private readonly Node _root;

    /// <summary>The schema re-serialized compactly; stable for a given input and used on the wire.</summary>
    public string CanonicalJson { get; }

    private JsonSchemaLite(Node root, string canonicalJson)
    {
        _root = root;
        CanonicalJson = canonicalJson;
    }

    public static JsonSchemaLite Parse(string schemaJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(schemaJson);
        }
        catch (JsonException e)
        {
            throw new JsonSchemaDefinitionException($"not valid JSON ({e.Message})");
        }

        using (doc)
        {
            var root = Compile(doc.RootElement, "$");
            return new JsonSchemaLite(root, JsonSerializer.Serialize(doc.RootElement));
        }
    }

    public IReadOnlyList<string> Validate(JsonElement instance)
    {
        var errors = new List<string>();
        Check(_root, instance, "$", errors);
        return errors;
    }

    private sealed class Node
    {
        public HashSet<string>? Types;
        public Dictionary<string, Node>? Properties;
        public List<string>? Required;
        public bool AdditionalProperties = true;
        public Node? Items;
        public int? MinItems;
        public int? MaxItems;
        public List<JsonElement>? Enum;
        public decimal? Minimum;
        public decimal? Maximum;
        public int? MinLength;
        public int? MaxLength;
    }

    private static Node Compile(JsonElement s, string path)
    {
        if (s.ValueKind != JsonValueKind.Object)
            throw new JsonSchemaDefinitionException($"{path}: a schema must be an object");

        var node = new Node();
        foreach (var p in s.EnumerateObject())
        {
            var at = $"{path}.{p.Name}";
            var v = p.Value;
            switch (p.Name)
            {
                case "description" or "title" or "$schema":
                    break;
                case "type":
                    node.Types = v.ValueKind == JsonValueKind.Array
                        ? v.EnumerateArray().Select(t => TypeName(t, at)).ToHashSet()
                        : [TypeName(v, at)];
                    break;
                case "properties":
                    if (v.ValueKind != JsonValueKind.Object)
                        throw new JsonSchemaDefinitionException($"{at}: must be an object");
                    node.Properties = new Dictionary<string, Node>(StringComparer.Ordinal);
                    foreach (var prop in v.EnumerateObject())
                    {
                        node.Properties[prop.Name] = Compile(prop.Value, $"{at}.{prop.Name}");
                    }

                    break;
                case "required":
                    if (v.ValueKind != JsonValueKind.Array || v.EnumerateArray().Any(r => r.ValueKind != JsonValueKind.String))
                        throw new JsonSchemaDefinitionException($"{at}: must be an array of strings");
                    node.Required = v.EnumerateArray().Select(r => r.GetString()!).ToList();
                    break;
                case "additionalProperties":
                    if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new JsonSchemaDefinitionException($"{at}: only a boolean is supported");
                    node.AdditionalProperties = v.GetBoolean();
                    break;
                case "items":
                    node.Items = Compile(v, at);
                    break;
                case "minItems":
                    node.MinItems = Count(v, at);
                    break;
                case "maxItems":
                    node.MaxItems = Count(v, at);
                    break;
                case "minLength":
                    node.MinLength = Count(v, at);
                    break;
                case "maxLength":
                    node.MaxLength = Count(v, at);
                    break;
                case "minimum":
                    node.Minimum = Number(v, at);
                    break;
                case "maximum":
                    node.Maximum = Number(v, at);
                    break;
                case "enum":
                    if (v.ValueKind != JsonValueKind.Array || v.GetArrayLength() == 0)
                        throw new JsonSchemaDefinitionException($"{at}: must be a non-empty array");
                    node.Enum = v.EnumerateArray().Select(e => e.Clone()).ToList();
                    break;
                default:
                    throw new JsonSchemaDefinitionException($"{at}: keyword not supported");
            }
        }

        if (node.Required is not null && node.Properties is not null)
        {
            foreach (var r in node.Required.Where(r => !node.Properties.ContainsKey(r)))
            {
                throw new JsonSchemaDefinitionException($"{path}.required: '{r}' is not in properties");
            }
        }

        return node;
    }

    private static string TypeName(JsonElement t, string at)
    {
        if (t.ValueKind == JsonValueKind.String && t.GetString() is { } name && Types.Contains(name))
            return name;
        throw new JsonSchemaDefinitionException($"{at}: type must be one of {string.Join(", ", Types)}");
    }

    private static int Count(JsonElement v, string at)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 0)
            return n;
        throw new JsonSchemaDefinitionException($"{at}: must be a non-negative integer");
    }

    private static decimal Number(JsonElement v, string at)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d))
            return d;
        throw new JsonSchemaDefinitionException($"{at}: must be a number");
    }

    private static void Check(Node node, JsonElement value, string path, List<string> errors)
    {
        if (node.Types is not null && !node.Types.Any(t => IsType(value, t)))
        {
            errors.Add($"{path}: expected {string.Join(" or ", node.Types)}, got {Describe(value)}");
            return;
        }

        if (node.Enum is not null && !node.Enum.Any(e => JsonElement.DeepEquals(e, value)))
            errors.Add($"{path}: must be one of {string.Join(", ", node.Enum.Select(e => e.GetRawText()))}");

        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                CheckObject(node, value, path, errors);
                break;
            case JsonValueKind.Array:
                var length = value.GetArrayLength();
                if (length < node.MinItems)
                    errors.Add($"{path}: at least {node.MinItems} item(s) required, got {length}");
                if (length > node.MaxItems)
                    errors.Add($"{path}: at most {node.MaxItems} item(s) allowed, got {length}");
                if (node.Items is not null)
                {
                    var i = 0;
                    foreach (var item in value.EnumerateArray())
                    {
                        Check(node.Items, item, $"{path}[{i++}]", errors);
                    }
                }

                break;
            case JsonValueKind.String:
                var chars = value.GetString()!.Length;
                if (chars < node.MinLength)
                    errors.Add($"{path}: at least {node.MinLength} character(s) required");
                if (chars > node.MaxLength)
                    errors.Add($"{path}: at most {node.MaxLength} character(s) allowed");
                break;
            case JsonValueKind.Number:
                if (value.TryGetDecimal(out var number))
                {
                    if (number < node.Minimum)
                        errors.Add($"{path}: must be >= {node.Minimum.Value.ToString(CultureInfo.InvariantCulture)}");
                    if (number > node.Maximum)
                        errors.Add($"{path}: must be <= {node.Maximum.Value.ToString(CultureInfo.InvariantCulture)}");
                }

                break;
        }
    }

    private static void CheckObject(Node node, JsonElement value, string path, List<string> errors)
    {
        foreach (var name in node.Required ?? [])
        {
            if (!value.TryGetProperty(name, out _))
                errors.Add($"{path}: missing required property '{name}'");
        }

        foreach (var prop in value.EnumerateObject())
        {
            if (node.Properties is not null && node.Properties.TryGetValue(prop.Name, out var child))
                Check(child, prop.Value, $"{path}.{prop.Name}", errors);
            else if (!node.AdditionalProperties)
                errors.Add($"{path}: property '{prop.Name}' is not allowed");
        }
    }

    private static bool IsType(JsonElement v, string type) => type switch
    {
        "object" => v.ValueKind == JsonValueKind.Object,
        "array" => v.ValueKind == JsonValueKind.Array,
        "string" => v.ValueKind == JsonValueKind.String,
        "number" => v.ValueKind == JsonValueKind.Number,
        "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) && d == decimal.Truncate(d),
        "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => v.ValueKind == JsonValueKind.Null,
        _ => false,
    };

    private static string Describe(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.True or JsonValueKind.False => "boolean",
        _ => v.ValueKind.ToString().ToLowerInvariant(),
    };
}
