using Cognition.Core.Config;

namespace Cognition.Core.Providers;

/// <summary>USD from configured prices, for endpoints that do not report <c>usage.cost</c> and for cap estimates.</summary>
public static class LlmCost
{
    public static decimal Usd(int inputTokens, int outputTokens, LlmProviderConfig config) =>
        (inputTokens * config.InputPriceUsdPerMtok + outputTokens * config.OutputPriceUsdPerMtok) / 1_000_000m;

    /// <summary>
    /// Upper bound: input from characters (scaled by <paramref name="safetyFactor"/>), output at its full
    /// limit, and a repair round (input + first reply in, full output again) when a schema reply may need it.
    /// </summary>
    public static decimal EstimateUsd(LlmRequest request, LlmProviderConfig config, double charsPerToken,
        double safetyFactor, bool repairEnabled)
    {
        var chars = request.SystemPrompt.Length + request.UserPrompt.Length;
        var input = (int)Math.Ceiling(chars / charsPerToken * safetyFactor);
        var output = OpenAiCompatClient.MaxCompletionTokens(request, config);
        if (request.JsonSchema is null || !repairEnabled)
            return Usd(input, output, config);
        return Usd(input + input + output, output + output, config);
    }
}

/// <summary>Refuses to send an LLM request that could push the run over its cap (RDev-04).</summary>
public sealed class CostGuardedLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;
    private readonly LiveCostGuard _guard;
    private readonly LlmProviderConfig _config;
    private readonly double _charsPerToken;
    private readonly bool _repairEnabled;

    public CostGuardedLlmClient(ILlmClient inner, LiveCostGuard guard, LlmProviderConfig config, double charsPerToken,
        bool repairEnabled)
    {
        _inner = inner;
        _guard = guard;
        _config = config;
        _charsPerToken = charsPerToken;
        _repairEnabled = repairEnabled;
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        _guard.EnsureRoomFor(LlmCost.EstimateUsd(request, _config, _charsPerToken,
            CostGuardedJevClient.EstimateSafetyFactor, _repairEnabled));
        try
        {
            var response = await _inner.CompleteAsync(request, ct);
            _guard.Add(response.Usage.CostUsd);
            return response;
        }
        catch (LlmException e) when (e.Usage is not null)
        {
            _guard.Add(e.Usage.CostUsd);
            throw;
        }
    }
}

/// <summary>Dispatches each request to the client configured for its <see cref="LlmRole"/>.</summary>
public sealed class RoleRoutedLlmClient : ILlmClient
{
    private readonly IReadOnlyDictionary<LlmRole, ILlmClient> _clients;

    public RoleRoutedLlmClient(IReadOnlyDictionary<LlmRole, ILlmClient> clients)
    {
        _clients = clients;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (!_clients.TryGetValue(request.Role, out var client))
            throw new InvalidOperationException($"no LLM client configured for role {request.Role}");
        return client.CompleteAsync(request, ct);
    }
}
