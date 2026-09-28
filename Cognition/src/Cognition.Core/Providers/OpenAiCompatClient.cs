using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Cognition.Core.Config;

namespace Cognition.Core.Providers;

/// <summary>One HTTP attempt, for diagnostics and record/replay. Never contains the API key.</summary>
public sealed record LlmExchange(
    string PurposeTag,
    int Round,
    int Attempt,
    string RequestJson,
    int? StatusCode,
    IReadOnlyDictionary<string, string> ResponseHeaders,
    string? ResponseBody,
    TimeSpan Elapsed,
    string? Error);

public sealed class OpenAiCompatClientOptions
{
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = (d, ct) => Task.Delay(d, ct);

    /// <summary>Uniform [0, 1) source for jitter.</summary>
    public Func<double> NextRandom { get; init; } = Random.Shared.NextDouble;

    /// <summary>A retry-after longer than this fails the call instead of stalling it.</summary>
    public TimeSpan MaxRetryAfter { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Builds the user message of the schema repair round from the validation errors. It is prompt text, so it
    /// comes from <c>prompts/</c> (P7). Null disables the repair round: an invalid reply fails at once.
    /// </summary>
    public Func<string, string>? RepairMessage { get; init; }

    public Action<LlmExchange>? OnExchange { get; init; }
}

/// <summary>
/// Client for any OpenAI-compatible <c>/chat/completions</c> endpoint: OpenRouter, llama.cpp, Ollama, tinyllm
/// (RM-03). Model and <c>reasoning_effort</c> come from the role's config (RM-04). Replies to schema requests
/// are validated locally; one repair round sends the errors back to the model.
/// </summary>
public sealed class OpenAiCompatClient : ILlmClient, IDisposable
{
    private static readonly string[] RequestIdHeaders = ["x-request-id", "x-generation-id"];

    private readonly LlmProviderConfig _config;
    private readonly string? _apiKey;
    private readonly OpenAiCompatClientOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly ConcurrentDictionary<string, JsonSchemaLite> _schemas = new(StringComparer.Ordinal);

    public LlmProviderConfig Config => _config;

    public OpenAiCompatClient(LlmProviderConfig config, string? apiKey, HttpMessageHandler? handler = null,
        OpenAiCompatClientOptions? options = null)
    {
        _config = config;
        _apiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;
        _options = options ?? new OpenAiCompatClientOptions();
        _http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        _http.Timeout = Timeout.InfiniteTimeSpan;
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Cognition", "0.1"));
        _endpoint = new Uri(config.BaseUrl.TrimEnd('/') + "/chat/completions");
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    public static int MaxCompletionTokens(LlmRequest request, LlmProviderConfig config) =>
        request.MaxOutputTokens + config.ReasoningAllowanceTokens;

    internal static List<ChatMessage> InitialMessages(LlmRequest request)
    {
        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            messages.Add(new ChatMessage("system", request.SystemPrompt));
        messages.Add(new ChatMessage("user", request.UserPrompt));
        return messages;
    }

    /// <summary>The first-round request body; what the replay key hashes (RA-03).</summary>
    public static string CanonicalRequest(LlmRequest request, LlmProviderConfig config)
    {
        var schema = request.JsonSchema is null ? null : JsonSchemaLite.Parse(request.JsonSchema);
        return OpenAiWireFormat.SerializeRequest(config, InitialMessages(request), MaxCompletionTokens(request, config),
            schema, request.PurposeTag);
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (request.MaxOutputTokens <= 0)
            throw new ArgumentException("MaxOutputTokens must be positive", nameof(request));
        if (string.IsNullOrWhiteSpace(request.UserPrompt))
            throw new ArgumentException("UserPrompt must not be empty", nameof(request));

        var schema = request.JsonSchema is null ? null : _schemas.GetOrAdd(request.JsonSchema, JsonSchemaLite.Parse);
        var messages = InitialMessages(request);

        var total = new RoundTotals();
        var first = await RoundAsync(request, messages, schema, 1, total, ct);
        if (schema is null)
            return total.Build(first.Content, first.Chat, repairs: 0);

        var (text, errors) = Check(first.Content, schema);
        if (errors.Count == 0)
            return total.Build(text, first.Chat, repairs: 0);
        if (_options.RepairMessage is null)
            throw new LlmSchemaException(errors, first.Content) { Usage = total.Usage };

        messages.Add(new ChatMessage("assistant", first.Content));
        messages.Add(new ChatMessage("user", _options.RepairMessage(string.Join("\n", errors))));
        var second = await RoundAsync(request, messages, schema, 2, total, ct);
        (text, errors) = Check(second.Content, schema);
        if (errors.Count > 0)
            throw new LlmSchemaException(errors, second.Content) { Usage = total.Usage };
        return total.Build(text, second.Chat, repairs: 1);
    }

    private static (string Text, IReadOnlyList<string> Errors) Check(string content, JsonSchemaLite schema)
    {
        var text = OpenAiWireFormat.StripCodeFence(content);
        try
        {
            using var doc = JsonDocument.Parse(text);
            return (text, schema.Validate(doc.RootElement));
        }
        catch (JsonException e)
        {
            return (text, [$"$: not valid JSON ({e.Message})"]);
        }
    }

    private sealed record RoundResult(ParsedChat Chat, string Content);

    /// <summary>Usage and timing summed over rounds, so a repaired reply reports what it really cost.</summary>
    private sealed class RoundTotals
    {
        public int InputTokens;
        public int OutputTokens;
        public int ReasoningTokens;
        public decimal CostUsd;
        public bool CostReported = true;
        public int Attempts;
        public TimeSpan Latency;

        public UsageInfo Usage => new(InputTokens, OutputTokens, CostUsd);

        public LlmResponse Build(string text, ParsedChat last, int repairs) => new(text, Usage, last.Model)
        {
            RequestId = last.Id,
            Latency = Latency,
            Attempts = Attempts,
            SchemaRepairs = repairs,
            ReasoningTokens = ReasoningTokens,
            CostReported = CostReported,
        };
    }

    private async Task<RoundResult> RoundAsync(LlmRequest request, IReadOnlyList<ChatMessage> messages,
        JsonSchemaLite? schema, int round, RoundTotals total, CancellationToken ct)
    {
        var maxTokens = MaxCompletionTokens(request, _config);
        var json = OpenAiWireFormat.SerializeRequest(_config, messages, maxTokens, schema, request.PurposeTag);
        var (chat, attempts, latency) = await SendAsync(request.PurposeTag, round, json, ct);

        total.Attempts += attempts;
        total.Latency += latency;
        total.InputTokens += chat.PromptTokens;
        total.OutputTokens += chat.CompletionTokens;
        total.ReasoningTokens += chat.ReasoningTokens;
        total.CostUsd += chat.CostUsd ?? LlmCost.Usd(chat.PromptTokens, chat.CompletionTokens, _config);
        total.CostReported &= chat.CostUsd is not null;

        if (chat.FinishReason == "length")
        {
            throw new LlmProtocolException(
                $"reply cut off at max_completion_tokens={maxTokens} ({chat.ReasoningTokens} reasoning tokens); "
                + "raise the template's output limit or reasoning_allowance_tokens")
            { Usage = total.Usage };
        }

        if (string.IsNullOrWhiteSpace(chat.Content))
        {
            throw new LlmProtocolException($"empty content (finish_reason={chat.FinishReason ?? "null"})")
            {
                Usage = total.Usage,
            };
        }

        return new RoundResult(chat, chat.Content);
    }

    private async Task<(ParsedChat Chat, int Attempts, TimeSpan Latency)> SendAsync(string purposeTag, int round,
        string json, CancellationToken ct)
    {
        var timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
        var maxAttempts = _config.MaxRetries + 1;

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, _endpoint);
            message.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (_apiKey is not null)
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
                Report(purposeTag, round, attempt, json, null, null, null, stopwatch.Elapsed, "timeout");
                if (attempt >= maxAttempts)
                    throw new LlmTimeoutException(timeout, attempt);
                await _options.Delay(Backoff(attempt), ct);
                continue;
            }
            catch (HttpRequestException e)
            {
                Report(purposeTag, round, attempt, json, null, null, null, stopwatch.Elapsed, e.Message);
                if (attempt >= maxAttempts)
                    throw new LlmTransportException(attempt, e);
                await _options.Delay(Backoff(attempt), ct);
                continue;
            }

            stopwatch.Stop();
            using (response)
            {
                Report(purposeTag, round, attempt, json, (int)response.StatusCode, HttpRetry.HeadersOf(response), body,
                    stopwatch.Elapsed, null);

                if (!response.IsSuccessStatusCode)
                {
                    if (!HttpRetry.IsRetryableStatus(response.StatusCode) || attempt >= maxAttempts)
                        throw new LlmApiException(response.StatusCode, body, attempt);
                    var delay = HttpRetry.RetryDelay(response, Backoff(attempt), _options.MaxRetryAfter,
                        _config.BackoffJitter, _options.NextRandom());
                    if (delay is null)
                        throw new LlmApiException(response.StatusCode, body, attempt);
                    await _options.Delay(delay.Value, ct);
                    continue;
                }

                var chat = OpenAiWireFormat.ParseResponse(body);
                if (chat.ErrorCode is { } code)
                {
                    var status = (HttpStatusCode)code;
                    if (!HttpRetry.IsRetryableStatus(status) || attempt >= maxAttempts)
                        throw new LlmApiException(status, body, attempt);
                    await _options.Delay(Backoff(attempt), ct);
                    continue;
                }

                if (chat.FinishReason == "error")
                {
                    if (attempt >= maxAttempts)
                        throw new LlmApiException(HttpStatusCode.BadGateway, body, attempt);
                    await _options.Delay(Backoff(attempt), ct);
                    continue;
                }

                if (chat.Id.Length == 0)
                    chat = chat with { Id = RequestIdOf(response) };
                return (chat, attempt, stopwatch.Elapsed);
            }
        }
    }

    internal TimeSpan Backoff(int attempt) =>
        HttpRetry.Backoff(attempt, _config.BackoffInitialMs, _config.BackoffMaxMs, _config.BackoffJitter,
            _options.NextRandom());

    private static string RequestIdOf(HttpResponseMessage response)
    {
        foreach (var name in RequestIdHeaders)
        {
            if (response.Headers.TryGetValues(name, out var values) && values.FirstOrDefault() is { Length: > 0 } id)
                return id;
        }

        return "local-" + Guid.NewGuid().ToString("N");
    }

    private void Report(string purposeTag, int round, int attempt, string json, int? status,
        IReadOnlyDictionary<string, string>? headers, string? body, TimeSpan elapsed, string? error)
    {
        _options.OnExchange?.Invoke(new LlmExchange(
            purposeTag,
            round,
            attempt,
            json,
            status,
            headers ?? new Dictionary<string, string>(),
            body,
            elapsed,
            error));
    }
}
