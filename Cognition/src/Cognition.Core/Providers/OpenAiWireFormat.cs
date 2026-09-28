using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cognition.Core.Config;

namespace Cognition.Core.Providers;

public sealed record ChatMessage(string Role, string Content);

/// <summary>A parsed chat completion, or the error object some endpoints return with status 200.</summary>
internal sealed record ParsedChat(
    string Id,
    string Model,
    string? Content,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int ReasoningTokens,
    decimal? CostUsd,
    int? ErrorCode,
    string? ErrorMessage);

/// <summary>OpenAI-compatible <c>POST {base_url}/chat/completions</c> bodies (docs/openai-compat-wire-format.md).</summary>
internal static partial class OpenAiWireFormat
{
    /// <summary>Deterministic: the same inputs always give the same bytes (the replay key depends on it).</summary>
    public static string SerializeRequest(LlmProviderConfig config, IReadOnlyList<ChatMessage> messages,
        int maxCompletionTokens, JsonSchemaLite? schema, string purposeTag)
    {
        var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("model", config.Model);
            w.WriteStartArray("messages");
            foreach (var m in messages)
            {
                w.WriteStartObject();
                w.WriteString("role", m.Role);
                w.WriteString("content", m.Content);
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteNumber("max_completion_tokens", maxCompletionTokens);
            if (config.ReasoningEffort.Length > 0)
                w.WriteString("reasoning_effort", config.ReasoningEffort);

            if (schema is not null)
            {
                switch (config.StructuredOutput)
                {
                    case StructuredOutputMode.JsonSchema:
                        w.WriteStartObject("response_format");
                        w.WriteString("type", "json_schema");
                        w.WriteStartObject("json_schema");
                        w.WriteString("name", SchemaName(purposeTag));
                        w.WriteBoolean("strict", false);
                        w.WritePropertyName("schema");
                        w.WriteRawValue(schema.CanonicalJson);
                        w.WriteEndObject();
                        w.WriteEndObject();
                        break;
                    case StructuredOutputMode.JsonObject:
                        w.WriteStartObject("response_format");
                        w.WriteString("type", "json_object");
                        w.WriteEndObject();
                        break;
                }
            }

            if (config.RequireParameters)
            {
                w.WriteStartObject("provider");
                w.WriteBoolean("require_parameters", true);
                w.WriteEndObject();
            }

            w.WriteBoolean("stream", false);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>OpenAI requires <c>^[a-zA-Z0-9_-]{1,64}$</c> for <c>json_schema.name</c>.</summary>
    internal static string SchemaName(string purposeTag)
    {
        var name = InvalidNameChars().Replace(purposeTag, "_");
        if (name.Length == 0)
            name = "reply";
        return name.Length > 64 ? name[..64] : name;
    }

    public static ParsedChat ParseResponse(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new LlmProtocolException("body is not JSON", e);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new LlmProtocolException("body is not a JSON object");

            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                int? code = error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number
                    && c.TryGetInt32(out var ci) ? ci : null;
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString()
                    : error.GetRawText();
                return new ParsedChat(string.Empty, string.Empty, null, null, 0, 0, 0, null, code ?? 0, message);
            }

            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                throw new LlmProtocolException("no choices");

            var choice = choices[0];
            if (!choice.TryGetProperty("message", out var message2) || message2.ValueKind != JsonValueKind.Object)
                throw new LlmProtocolException("choice has no message");

            var content = message2.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.String
                ? ct.GetString()
                : null;
            var finish = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String
                ? fr.GetString()
                : null;

            int prompt = 0, completion = 0, reasoning = 0;
            decimal? cost = null;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                prompt = IntOf(usage, "prompt_tokens");
                completion = IntOf(usage, "completion_tokens");
                if (usage.TryGetProperty("completion_tokens_details", out var details)
                    && details.ValueKind == JsonValueKind.Object)
                    reasoning = IntOf(details, "reasoning_tokens");
                if (usage.TryGetProperty("cost", out var costEl) && costEl.ValueKind == JsonValueKind.Number)
                    cost = costEl.GetDecimal();
            }

            return new ParsedChat(
                StringOf(root, "id"),
                StringOf(root, "model"),
                content,
                finish,
                prompt,
                completion,
                reasoning,
                cost,
                null,
                null);
        }
    }

    /// <summary>
    /// Removes one surrounding Markdown code fence (<c>```json … ```</c>), which some models add in
    /// <c>json_object</c> or <c>none</c> mode. Anything else is returned trimmed and unchanged.
    /// </summary>
    public static string StripCodeFence(string content)
    {
        var match = CodeFence().Match(content);
        return match.Success ? match.Groups["body"].Value.Trim() : content.Trim();
    }

    private static int IntOf(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static string StringOf(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : string.Empty;

    [GeneratedRegex("[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidNameChars();

    [GeneratedRegex(@"^\s*```[a-zA-Z]*\s*\n(?<body>[\s\S]*?)\n?```\s*$")]
    private static partial Regex CodeFence();
}
