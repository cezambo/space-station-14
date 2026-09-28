using Cognition.Core.Providers;

namespace Cognition.Core.Telemetry;

/// <summary>Logs every Jev call, successful or not. Wrap outermost, around the replay client.</summary>
public sealed class TelemetryJevClient : IJevClient
{
    private readonly IJevClient _inner;
    private readonly ITelemetrySink _sink;
    private readonly Func<double> _clock;

    public TelemetryJevClient(IJevClient inner, ITelemetrySink sink, Func<double> clock)
    {
        _inner = inner;
        _sink = sink;
        _clock = clock;
    }

    public async Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        var t = _clock();
        try
        {
            var r = await _inner.EvaluateAsync(request, ct);
            _sink.Write(new CallRecord(t, TelemetryContext.CharacterId, "jev", request.PurposeTag, r.Model, r.RequestId,
                r.Replayed, r.Usage.InputTokens, r.Usage.OutputTokens, 0, r.Replayed ? 0m : r.Usage.CostUsd,
                r.Usage.CostUsd, r.Latency.TotalMilliseconds, r.Attempts, 0, TelemetryJson.Answers(r.Answers), null));
            return r;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            _sink.Write(new CallRecord(t, TelemetryContext.CharacterId, "jev", request.PurposeTag, request.Model,
                string.Empty, false, 0, 0, 0, 0m, 0m, 0, 0, 0, null, $"{e.GetType().Name}: {e.Message}"));
            throw;
        }
    }
}

/// <summary>Logs every LLM call for one role. Failed calls log what they were billed (<see cref="LlmException.Usage"/>).</summary>
public sealed class TelemetryLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly ITelemetrySink _sink;
    private readonly Func<double> _clock;
    private readonly string _model;

    public TelemetryLlmClient(ILlmClient inner, ITelemetrySink sink, Func<double> clock, string configuredModel)
    {
        _inner = inner;
        _sink = sink;
        _clock = clock;
        _model = configuredModel;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var t = _clock();
        var role = request.Role.ToString().ToLowerInvariant();
        try
        {
            var r = await _inner.CompleteAsync(request, ct);
            _sink.Write(new CallRecord(t, TelemetryContext.CharacterId, role, request.PurposeTag, r.ModelId, r.RequestId,
                r.Replayed, r.Usage.InputTokens, r.Usage.OutputTokens, r.ReasoningTokens,
                r.Replayed ? 0m : r.Usage.CostUsd, r.Usage.CostUsd, r.Latency.TotalMilliseconds, r.Attempts,
                r.SchemaRepairs, null, null));
            return r;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var usage = (e as LlmException)?.Usage;
            _sink.Write(new CallRecord(t, TelemetryContext.CharacterId, role, request.PurposeTag, _model, string.Empty,
                false, usage?.InputTokens ?? 0, usage?.OutputTokens ?? 0, 0, usage?.CostUsd ?? 0m,
                usage?.CostUsd ?? 0m, 0, 0, 0, null, $"{e.GetType().Name}: {e.Message}"));
            throw;
        }
    }
}
