using System.Text;
using Cognition.Core.Config;

namespace Cognition.Core.Decision;

/// <summary>The §9.3 blocks of the decision context, in the order they are budgeted.</summary>
public enum ContextBlock
{
    Instructions,
    General,
    Goals,
    Personality,
    Emotion,
    CurrentAction,
    Perception,
    RecentMemory,
    DailyMemory,
    Opinions,
    Menus,
}

public static class ContextBlocks
{
    /// <summary>snake_case name used in <c>cognition.toml</c> and telemetry.</summary>
    public static string Key(string pascal)
    {
        var sb = new StringBuilder();
        foreach (var ch in pascal)
        {
            if (char.IsUpper(ch) && sb.Length > 0)
                sb.Append('_');
            sb.Append(char.ToLowerInvariant(ch));
        }

        return sb.ToString();
    }

    public static string Key(this ContextBlock block) => Key(block.ToString());
}

/// <summary>
/// Estimates tokens as characters / (chars per token), with one ratio per block type (T1.14). Starts from
/// <c>context.chars_per_token_initial</c> and is fitted to billed input tokens with normalized least mean
/// squares: providers bill a whole call, so each observation spreads the error over blocks by their size.
/// Not thread-safe; use one per scheduler.
/// </summary>
public sealed class TokenEstimator
{
    // Tokens per character, bounded so that a bad observation cannot make a block free or absurd.
    private const double MinRate = 1.0 / 8;
    private const double MaxRate = 1.0;

    private readonly double[] _rate;
    private readonly double _step;

    public TokenEstimator(ContextConfig config)
    {
        _rate = Enumerable.Repeat(Math.Clamp(1.0 / config.CharsPerTokenInitial, MinRate, MaxRate),
            Enum.GetValues<ContextBlock>().Length).ToArray();
        _step = config.CalibrationRate;
    }

    public int Observations { get; private set; }

    public double CharsPerToken(ContextBlock block) => 1.0 / _rate[(int)block];

    public int Estimate(ContextBlock block, int chars) => (int)Math.Ceiling(chars * _rate[(int)block]);

    public int Estimate(IReadOnlyDictionary<ContextBlock, int> chars) => chars.Sum(kv => Estimate(kv.Key, kv.Value));

    /// <summary>Feeds back the billed input tokens of a call whose context had these block sizes.</summary>
    public void Observe(IReadOnlyDictionary<ContextBlock, int> chars, long billedInputTokens)
    {
        if (billedInputTokens <= 0 || chars.Count == 0)
            return;
        var predicted = chars.Sum(kv => kv.Value * _rate[(int)kv.Key]);
        var norm = chars.Sum(kv => (double)kv.Value * kv.Value);
        if (norm <= 0)
            return;
        var error = billedInputTokens - predicted;
        foreach (var (block, n) in chars)
        {
            _rate[(int)block] = Math.Clamp(_rate[(int)block] + (_step * error * n / norm), MinRate, MaxRate);
        }

        Observations++;
    }
}
