using Cognition.Core.Config;

namespace Cognition.Core.Providers;

/// <summary>Running USD total for one live run, shared by every client in the run (RDev-04).</summary>
public sealed class LiveCostGuard
{
    private readonly object _lock = new();
    private decimal _spent;

    public decimal CapUsd { get; }

    public LiveCostGuard(decimal capUsd)
    {
        if (capUsd <= 0)
            throw new ArgumentOutOfRangeException(nameof(capUsd), "cost cap must be positive");
        CapUsd = capUsd;
    }

    public decimal SpentUsd
    {
        get
        {
            lock (_lock)
            {
                return _spent;
            }
        }
    }

    public void EnsureRoomFor(decimal estimateUsd)
    {
        lock (_lock)
        {
            if (_spent + estimateUsd > CapUsd)
                throw new CostCapExceededException(_spent, estimateUsd, CapUsd);
        }
    }

    public void Add(decimal usd)
    {
        lock (_lock)
        {
            _spent += usd;
        }
    }
}

/// <summary>Refuses to send a Jev request that could push the run over its cap.</summary>
public sealed class CostGuardedJevClient : IJevClient
{
    /// <summary>chars/4 undercounts some text; estimate high so the cap is never crossed.</summary>
    public const double EstimateSafetyFactor = 1.5;

    private readonly IJevClient _inner;
    private readonly LiveCostGuard _guard;
    private readonly JevProviderConfig _config;
    private readonly double _charsPerToken;

    public CostGuardedJevClient(IJevClient inner, LiveCostGuard guard, JevProviderConfig config, double charsPerToken)
    {
        _inner = inner;
        _guard = guard;
        _config = config;
        _charsPerToken = charsPerToken;
    }

    public async Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
    {
        _guard.EnsureRoomFor(JevCost.EstimateUsd(request, _config, _charsPerToken, EstimateSafetyFactor));
        var response = await _inner.EvaluateAsync(request, ct);
        _guard.Add(response.Usage.CostUsd);
        return response;
    }
}
