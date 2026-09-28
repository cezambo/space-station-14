using Cognition.Core.Config;

namespace Cognition.Core.Providers;

/// <summary>USD from token counts; the Jev response has no cost field (docs.typesafe.ai/api).</summary>
public static class JevCost
{
    private const decimal TokensPerMillion = 1_000_000m;

    public static decimal Usd(int inputTokens, int outputTokens, JevProviderConfig config)
    {
        return inputTokens * config.InputPriceUsdPerMtok / TokensPerMillion
               + outputTokens * config.OutputPriceUsdPerMtok / TokensPerMillion;
    }

    /// <summary>Upper-bound guess before sending, from character counts.</summary>
    public static decimal EstimateUsd(JevRequest request, JevProviderConfig config, double charsPerToken, double safetyFactor)
    {
        var chars = request.State.Length + request.Questions.Sum(kv => kv.Key.Length + JevRequestValidator.CharCount(kv.Value));
        var tokens = (int)Math.Ceiling(JevRequestValidator.EstimateTokens(chars, charsPerToken) * safetyFactor);
        return Usd(tokens, 0, config);
    }
}
