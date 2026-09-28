using System.Globalization;
using System.Net;

namespace Cognition.Core.Providers;

/// <summary>Retry timing and header helpers shared by the Jev and LLM HTTP clients.</summary>
internal static class HttpRetry
{
    /// <summary>Cloudflare session cookies; never useful and not worth persisting.</summary>
    private static readonly HashSet<string> DroppedHeaders = new(StringComparer.OrdinalIgnoreCase) { "Set-Cookie" };

    public static bool IsRetryableStatus(HttpStatusCode status)
    {
        var code = (int)status;
        return code is 408 or 429 or >= 500 and <= 599;
    }

    /// <summary>Exponential backoff; jitter subtracts up to <paramref name="jitter"/> of the delay.</summary>
    public static TimeSpan Backoff(int attempt, int initialMs, int maxMs, double jitter, double random)
    {
        var baseMs = Math.Min(initialMs * Math.Pow(2, attempt - 1), maxMs);
        return TimeSpan.FromMilliseconds(baseMs * (1 - jitter * random));
    }

    /// <summary>
    /// The server-requested wait with up to <paramref name="jitter"/> added on top, so it is never shorter
    /// than asked; <paramref name="fallback"/> when none is given; null when the ask exceeds <paramref name="maxWait"/>.
    /// </summary>
    public static TimeSpan? RetryDelay(HttpResponseMessage response, TimeSpan fallback, TimeSpan maxWait, double jitter,
        double random)
    {
        var asked = RetryAfterOf(response);
        if (asked is null)
            return fallback;
        if (asked.Value > maxWait)
            return null;
        return asked.Value * (1 + jitter * random);
    }

    /// <summary>Honors <c>retry-after-ms</c> first, then <c>Retry-After</c> (seconds or HTTP date).</summary>
    public static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("retry-after-ms", out var msValues)
            && double.TryParse(msValues.FirstOrDefault(), NumberStyles.Float, CultureInfo.InvariantCulture, out var ms)
            && ms >= 0)
            return TimeSpan.FromMilliseconds(ms);

        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> HeadersOf(HttpResponseMessage response)
    {
        var headers = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in response.Headers.Concat(response.Content.Headers))
        {
            if (!DroppedHeaders.Contains(name))
                headers[name] = string.Join(", ", values);
        }

        return headers;
    }
}
