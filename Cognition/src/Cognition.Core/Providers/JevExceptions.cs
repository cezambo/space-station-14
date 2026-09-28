using System.Net;

namespace Cognition.Core.Providers;

public abstract class JevException : Exception
{
    protected JevException(string message, Exception? inner = null) : base(message, inner)
    {
    }
}

/// <summary>The request breaks a documented API limit; it was not sent.</summary>
public sealed class JevValidationException : JevException
{
    public IReadOnlyList<string> Errors { get; }

    public JevValidationException(IReadOnlyList<string> errors)
        : base("Invalid Jev request: " + string.Join("; ", errors))
    {
        Errors = errors;
    }
}

/// <summary>Non-success HTTP status after retries were exhausted or for a non-retryable status.</summary>
public sealed class JevApiException : JevException
{
    public HttpStatusCode StatusCode { get; }
    public string Body { get; }

    public JevApiException(HttpStatusCode statusCode, string body, int attempts)
        : base($"Jev API returned {(int)statusCode} {statusCode} after {attempts} attempt(s): {Truncate(body)}")
    {
        StatusCode = statusCode;
        Body = body;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";
}

public sealed class JevTimeoutException : JevException
{
    public JevTimeoutException(TimeSpan timeout, int attempts)
        : base($"Jev request timed out after {timeout.TotalMilliseconds:0} ms on each of {attempts} attempt(s)")
    {
    }
}

public sealed class JevTransportException : JevException
{
    public JevTransportException(int attempts, Exception inner)
        : base($"Jev request failed to connect after {attempts} attempt(s): {inner.Message}", inner)
    {
    }
}

/// <summary>A 2xx response whose body does not match the request (missing answer, wrong type, unknown option).</summary>
public sealed class JevProtocolException : JevException
{
    public JevProtocolException(string message, Exception? inner = null) : base("Unexpected Jev response: " + message, inner)
    {
    }
}

/// <summary>The live run would exceed its USD cap (RDev-04); the request (Jev or LLM) was not sent.</summary>
public sealed class CostCapExceededException : Exception
{
    public CostCapExceededException(decimal spent, decimal estimate, decimal cap)
        : base($"Live cost cap reached: spent ${spent:0.000000} + next ~${estimate:0.000000} > cap ${cap:0.00}")
    {
    }
}
