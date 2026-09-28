using System.Net;

namespace Cognition.Core.Providers;

public abstract class LlmException : Exception
{
    protected LlmException(string message, Exception? inner = null) : base(message, inner)
    {
    }

    /// <summary>What the failed call still cost (a reply was generated and billed); null when nothing was billed.</summary>
    public UsageInfo? Usage { get; internal set; }

    protected static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}

/// <summary>Non-success HTTP status (or an error object in a 200 body) after retries, or a non-retryable one.</summary>
public sealed class LlmApiException : LlmException
{
    public HttpStatusCode StatusCode { get; }
    public string Body { get; }

    public LlmApiException(HttpStatusCode statusCode, string body, int attempts)
        : base($"LLM API returned {(int)statusCode} {statusCode} after {attempts} attempt(s): {Truncate(body)}")
    {
        StatusCode = statusCode;
        Body = body;
    }
}

public sealed class LlmTimeoutException : LlmException
{
    public LlmTimeoutException(TimeSpan timeout, int attempts)
        : base($"LLM request timed out after {timeout.TotalMilliseconds:0} ms on each of {attempts} attempt(s)")
    {
    }
}

public sealed class LlmTransportException : LlmException
{
    public LlmTransportException(int attempts, Exception inner)
        : base($"LLM request failed to connect after {attempts} attempt(s): {inner.Message}", inner)
    {
    }
}

/// <summary>A 2xx body that is not a usable chat completion (no choices, empty content, length cut-off).</summary>
public sealed class LlmProtocolException : LlmException
{
    public LlmProtocolException(string message, Exception? inner = null)
        : base("Unexpected LLM response: " + message, inner)
    {
    }
}

/// <summary>The reply did not match the request's JSON Schema, including after the repair round.</summary>
public sealed class LlmSchemaException : LlmException
{
    public IReadOnlyList<string> Errors { get; }
    public string Reply { get; }

    public LlmSchemaException(IReadOnlyList<string> errors, string reply)
        : base($"LLM reply does not match the schema: {string.Join("; ", errors)} | reply: {Truncate(reply)}")
    {
        Errors = errors;
        Reply = reply;
    }
}

/// <summary>A template's <c>## SCHEMA</c> uses JSON Schema features the local validator does not support.</summary>
public sealed class JsonSchemaDefinitionException : Exception
{
    public JsonSchemaDefinitionException(string message) : base("Invalid JSON Schema: " + message)
    {
    }
}
