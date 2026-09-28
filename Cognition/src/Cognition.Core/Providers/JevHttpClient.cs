using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Cognition.Core.Config;

namespace Cognition.Core.Providers;

/// <summary>One HTTP attempt, for diagnostics and (later) record/replay. Never contains the API key.</summary>
public sealed record JevExchange(
    string PurposeTag,
    int Attempt,
    string RequestJson,
    int? StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    TimeSpan Elapsed,
    string? Error);

public sealed class JevHttpClientOptions
{
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (d, ct) => Task.Delay(d, ct);

    /// <summary>Uniform [0, 1) source for jitter.</summary>
    public Func<double> NextRandom { get; init; } = Random.Shared.NextDouble;

    public double CharsPerToken { get; init; } = 4.0;

    /// <summary>A retry-after longer than this fails the call instead of stalling it (RNF-05).</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(30);

    public Action<JevExchange>? OnExchange { get; init; }
}

/// <summary>
/// HTTP client for <c>POST {base_url}/systemone</c> with Bearer auth (RM-07, RNF-05).
/// There is no official C# SDK; behaviour mirrors the official SDK retry policy.
/// </summary>
public sealed class JevHttpClient : IJevClient, IDisposable
{
    private readonly JevProviderConfig _config;
    private readonly string _apiKey;
    private readonly JevHttpClientOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;

    public JevHttpClient(JevProviderConfig config, string apiKey, HttpMessageHandler? handler = null,
        JevHttpClientOptions? options = null)
    {
        _config = config;
        _apiKey = apiKey;
        _options = options ?? new JevHttpClientOptions();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Cognition", "0.1"));
        _endpoint = new Uri(config.BaseUrl.TrimEnd('/') + "/systemone");
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary><c>x-typesafe-request-id</c> is what the live API sends (observed 2026-09-28).</summary>
    private static readonly string[] RequestIdHeaders = ["x-typesafe-request-id", "x-request-id"];

    public static bool IsRetryable(HttpStatusCode status) => HttpRetry.IsRetryableStatus(status);

    public async Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        JevRequestValidator.ThrowIfInvalid(request, _options.CharsPerToken);

        var json = JevWireFormat.SerializeRequest(request);
        var timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
        var maxAttempts = _config.MaxRetries + 1;

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(timeout);

            var stopwatch = Stopwatch.StartNew();
            HttpResponseMessage response;
            string body;
            try
            {
                response = await _http.SendAsync(message, HttpCompletionOption.ResponseContentRead, attemptCts.Token);
                body = await response.Content.ReadAsStringAsync(attemptCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                Report(request, attempt, json, null, null, null, stopwatch.Elapsed, "timeout");
                if (attempt >= maxAttempts)
                    throw new JevTimeoutException(timeout, attempt);
                await _options.Delay(Backoff(attempt), ct);
                continue;
            }
            catch (HttpRequestException e)
            {
                Report(request, attempt, json, null, null, null, stopwatch.Elapsed, e.Message);
                if (attempt >= maxAttempts)
                    throw new JevTransportException(attempt, e);
                await _options.Delay(Backoff(attempt), ct);
                continue;
            }

            stopwatch.Stop();
            using (response)
            {
                Report(request, attempt, json, response, HttpRetry.HeadersOf(response), body, stopwatch.Elapsed, null);

                if (response.IsSuccessStatusCode)
                    return BuildResponse(request, response, body, stopwatch.Elapsed, attempt);

                if (!IsRetryable(response.StatusCode) || attempt >= maxAttempts)
                    throw new JevApiException(response.StatusCode, body, attempt);

                var delay = RetryDelay(response, attempt);
                if (delay is null)
                    throw new JevApiException(response.StatusCode, body, attempt);
                await _options.Delay(delay.Value, ct);
            }
        }
    }

    private JevResponse BuildResponse(JevRequest request, HttpResponseMessage response, string body, TimeSpan latency,
        int attempt)
    {
        var parsed = JevWireFormat.ParseResponse(body, request);
        var cost = JevCost.Usd(parsed.InputTokens, parsed.OutputTokens, _config);
        return new JevResponse(
            RequestIdOf(response),
            latency,
            parsed.Answers,
            new UsageInfo(parsed.InputTokens, parsed.OutputTokens, cost))
        {
            Model = parsed.Model,
            Attempts = attempt,
        };
    }

    internal TimeSpan Backoff(int attempt) =>
        HttpRetry.Backoff(attempt, _config.BackoffInitialMs, _config.BackoffMaxMs, _config.BackoffJitter,
            _options.NextRandom());

    /// <summary>Null when the server asks for more than <see cref="JevHttpClientOptions.MaxRetryAfter"/>.</summary>
    internal TimeSpan? RetryDelay(HttpResponseMessage response, int attempt) =>
        HttpRetry.RetryDelay(response, Backoff(attempt), _options.MaxRetryAfter, _config.BackoffJitter,
            _options.NextRandom());

    internal static TimeSpan? RetryAfterOf(HttpResponseMessage response) => HttpRetry.RetryAfterOf(response);

    private static string RequestIdOf(HttpResponseMessage response)
    {
        foreach (var name in RequestIdHeaders)
        {
            if (response.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } id)
                return id;
        }

        return "local-" + Guid.NewGuid().ToString("N");
    }

    private void Report(JevRequest request, int attempt, string json, HttpResponseMessage? response,
        IReadOnlyDictionary<string, string>? headers, string? body, TimeSpan elapsed, string? error)
    {
        _options.OnExchange?.Invoke(new JevExchange(
            request.PurposeTag,
            attempt,
            json,
            response is null ? null : (int)response.StatusCode,
            headers ?? new Dictionary<string, string>(),
            body,
            elapsed,
            error));
    }
}
