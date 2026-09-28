using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Core.Replay;

/// <summary>
/// RA-03 modes. <c>live</c>: call, never write. <c>record</c>: call and (over)write. <c>replay</c>: serve the
/// recording, or call and record on a miss. <c>replay-strict</c>: serve the recording or throw
/// <see cref="ReplayMissException"/>; never calls. Whether a call spends money is up to the inner client,
/// which is only live when built with <c>--live --max-cost-usd</c>. Failed calls are not recorded.
/// </summary>
internal static class ReplayGate
{
    public static async Task<T> RunAsync<T>(ReplayMode mode, IReplayStore store, ReplayRecord keyed,
        Func<Task<T>> call, Func<T, string> encode, Func<string, T> decode)
    {
        if (mode != ReplayMode.Live && mode != ReplayMode.Record)
        {
            var hit = store.TryLoad(keyed.Purpose, keyed.Key);
            if (hit is not null)
                return decode(hit.ResponseJson);
            if (mode == ReplayMode.ReplayStrict)
                throw new ReplayMissException(keyed.Purpose, keyed.Key, store.PathFor(keyed.Purpose, keyed.Key));
        }

        var response = await call();
        if (mode != ReplayMode.Live)
            store.Save(keyed with { ResponseJson = encode(response) });
        return response;
    }
}

public sealed class ReplayJevClient : IJevClient
{
    private readonly IJevClient _inner;
    private readonly IReplayStore _store;
    private readonly ReplayMode _mode;

    public ReplayJevClient(IJevClient inner, IReplayStore store, ReplayMode mode)
    {
        _inner = inner;
        _store = store;
        _mode = mode;
    }

    public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        var canonical = JevWireFormat.SerializeRequest(request);
        var keyed = new ReplayRecord(
            ReplayKey.Compute(canonical, request.TemplateHash, request.Model),
            "jev",
            request.PurposeTag,
            request.Model,
            request.TemplateHash,
            canonical,
            string.Empty);
        return ReplayGate.RunAsync(_mode, _store, keyed, () => _inner.EvaluateAsync(request, ct),
            ReplayCodec.WriteJev, ReplayCodec.ReadJev);
    }
}

/// <summary>Wraps the client of one LLM role; the key covers that role's model and request settings.</summary>
public sealed class ReplayLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly LlmProviderConfig _config;
    private readonly IReplayStore _store;
    private readonly ReplayMode _mode;

    public ReplayLlmClient(ILlmClient inner, LlmProviderConfig config, IReplayStore store, ReplayMode mode)
    {
        _inner = inner;
        _config = config;
        _store = store;
        _mode = mode;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var canonical = OpenAiCompatClient.CanonicalRequest(request, _config);
        var keyed = new ReplayRecord(
            ReplayKey.Compute(canonical, request.TemplateHash, _config.Model),
            "llm",
            request.PurposeTag,
            _config.Model,
            request.TemplateHash,
            canonical,
            string.Empty);
        return ReplayGate.RunAsync(_mode, _store, keyed, () => _inner.CompleteAsync(request, ct),
            ReplayCodec.WriteLlm, ReplayCodec.ReadLlm);
    }
}

/// <summary>Inner client for runs without <c>--live</c>: any replay miss fails instead of calling out.</summary>
public sealed class OfflineClient : IJevClient, ILlmClient
{
    public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct) =>
        throw new InvalidOperationException(
            $"offline: no recording for Jev purpose '{request.PurposeTag}' and live calls are not enabled (--live)");

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        throw new InvalidOperationException(
            $"offline: no recording for LLM purpose '{request.PurposeTag}' and live calls are not enabled (--live)");
}
