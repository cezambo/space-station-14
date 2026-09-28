using System.Globalization;
using System.Text.RegularExpressions;
using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Telemetry;

namespace Cognition.Core.Decision;

/// <summary>
/// Everything the decision state needs, already categorized (RA-06). Lists are in priority order: recent memory
/// oldest first, opinions most relevant first. Phrases such as the emotion or budget band come from
/// <see cref="Vocabulary"/>; this record carries no numbers other than age and day.
/// </summary>
public sealed record ContextInput(
    string Name,
    int Age,
    string Species,
    string Job,
    string StationTime,
    int Day,
    string DayPhase,
    string CurrentAction,
    string Inventory,
    string PersonalitySummary,
    string Likes,
    string Dislikes,
    string EmotionPrimary,
    string EmotionSecondary,
    string Modifiers,
    IReadOnlyList<string> ImmediateGoals,
    IReadOnlyList<string> MediumGoals,
    IReadOnlyList<string> PresentOpinions,
    PerceptionBlock Perception,
    IReadOnlyList<string> RecentMemory,
    string? LastDailyShort,
    string BudgetBand,
    int MenuChars);

public sealed record TrimStep(ContextBlock Block, int Removed);

/// <summary>
/// The filled placeholders for <c>decision.yaml</c>'s state, plus what was measured and trimmed for telemetry.
/// </summary>
public sealed record AssembledContext(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<ContextBlock, int> BlockChars,
    IReadOnlyDictionary<ContextBlock, int> BlockTokens,
    int EstimatedTokens,
    IReadOnlyList<TrimStep> Trims,
    IReadOnlyList<ContextBlock> OverBlockTarget,
    bool OverTarget,
    PerceptionBlock Perception);

public static class AssembledContextTelemetry
{
    public static ContextRecord ToRecord(this AssembledContext c, double t, string? character, string purpose) =>
        new(t, character, purpose, c.EstimatedTokens, c.OverTarget,
            c.BlockTokens.OrderBy(kv => kv.Key).Select(kv => (kv.Key.Key(), kv.Value)).ToList(),
            c.Trims.Select(s => (s.Block.Key(), s.Removed)).ToList(),
            c.OverBlockTarget.Select(b => b.Key()).ToList());
}

public sealed class ContextOverflowException(string message) : Exception(message);

/// <summary>
/// Fills the decision state template and keeps it within budget (T1.14, RJ-08, §9.3). When the estimate exceeds
/// <c>context.target_tokens</c> it trims, one item at a time: daily memory, then oldest recent memories, then
/// least relevant opinions, then least salient perception lines. Instructions, immediate goals, body and menus
/// are never trimmed. Above the target after all trims is allowed and flagged; above the hard cap throws.
/// </summary>
public sealed partial class ContextAssembler
{
    private static readonly Dictionary<string, ContextBlock> PlaceholderBlocks = new(StringComparer.Ordinal)
    {
        ["name"] = ContextBlock.General,
        ["age"] = ContextBlock.General,
        ["species"] = ContextBlock.General,
        ["job"] = ContextBlock.General,
        ["station_time"] = ContextBlock.General,
        ["day"] = ContextBlock.General,
        ["day_phase"] = ContextBlock.General,
        ["current_action"] = ContextBlock.CurrentAction,
        ["inventory"] = ContextBlock.CurrentAction,
        ["personality_summary"] = ContextBlock.Personality,
        ["likes"] = ContextBlock.Personality,
        ["dislikes"] = ContextBlock.Personality,
        ["emotion_primary"] = ContextBlock.Emotion,
        ["emotion_secondary"] = ContextBlock.Emotion,
        ["modifiers"] = ContextBlock.Emotion,
        ["immediate_goals"] = ContextBlock.Goals,
        ["medium_goals"] = ContextBlock.Goals,
        ["present_opinions"] = ContextBlock.Opinions,
        ["perception_block"] = ContextBlock.Perception,
        ["recent_memory"] = ContextBlock.RecentMemory,
        ["last_daily_short"] = ContextBlock.DailyMemory,
        ["budget_band"] = ContextBlock.Instructions,
    };

    private readonly ContextConfig _config;
    private readonly Vocabulary _v;
    private readonly PerceptionFormatter _perception;
    private readonly TokenEstimator _estimator;
    private readonly int _staticChars;

    public ContextAssembler(JevTemplate decision, ContextConfig config, Vocabulary vocabulary, PerceptionFormatter perception,
        TokenEstimator estimator)
    {
        var names = Placeholders.NamesIn(decision.StateTemplate).ToHashSet(StringComparer.Ordinal);
        var unknown = names.Where(n => !PlaceholderBlocks.ContainsKey(n)).Order(StringComparer.Ordinal).ToList();
        var unused = PlaceholderBlocks.Keys.Where(n => !names.Contains(n)).Order(StringComparer.Ordinal).ToList();
        if (unknown.Count > 0 || unused.Count > 0)
        {
            throw new ArgumentException($"template '{decision.Name}': state placeholders do not match the assembler "
                + $"(unknown: {string.Join(", ", unknown)}; unused: {string.Join(", ", unused)})", nameof(decision));
        }

        _config = config;
        _v = vocabulary;
        _perception = perception;
        _estimator = estimator;
        _staticChars = PlaceholderPattern().Replace(decision.StateTemplate, string.Empty).Length;
    }

    public AssembledContext Assemble(ContextInput input)
    {
        var perception = input.Perception;
        var memory = input.RecentMemory.ToList();
        var opinions = input.PresentOpinions.ToList();
        var daily = input.LastDailyShort;
        var trims = new Dictionary<ContextBlock, int>();

        while (true)
        {
            var values = Values(input, perception, memory, opinions, daily);
            var chars = BlockChars(values, input.MenuChars);
            var tokens = chars.ToDictionary(kv => kv.Key, kv => _estimator.Estimate(kv.Key, kv.Value));
            var total = tokens.Values.Sum();
            if (total > _config.TargetTokens)
            {
                if (daily is not null)
                {
                    daily = null;
                    Count(trims, ContextBlock.DailyMemory);
                    continue;
                }

                if (memory.Count > 0)
                {
                    memory.RemoveAt(0);
                    Count(trims, ContextBlock.RecentMemory);
                    continue;
                }

                if (opinions.Count > 0)
                {
                    opinions.RemoveAt(opinions.Count - 1);
                    Count(trims, ContextBlock.Opinions);
                    continue;
                }

                if (perception.Seen.Count > 0)
                {
                    perception = _perception.DropLeastSalient(perception, 1);
                    Count(trims, ContextBlock.Perception);
                    continue;
                }
            }

            if (total > _config.HardCapTokens)
            {
                throw new ContextOverflowException(
                    $"decision context is {total} tokens after all trims, above the hard cap {_config.HardCapTokens}");
            }

            var over = tokens.Where(kv => kv.Value > _config.BlockTargets[kv.Key.Key()]).Select(kv => kv.Key).Order().ToList();
            var steps = trims.OrderBy(kv => kv.Key).Select(kv => new TrimStep(kv.Key, kv.Value)).ToList();
            return new AssembledContext(values, chars, tokens, total, steps, over, total > _config.TargetTokens, perception);
        }
    }

    private static void Count(Dictionary<ContextBlock, int> trims, ContextBlock block) =>
        trims[block] = trims.GetValueOrDefault(block) + 1;

    private Dictionary<ContextBlock, int> BlockChars(IReadOnlyDictionary<string, string> values, int menuChars)
    {
        var chars = Enum.GetValues<ContextBlock>().ToDictionary(b => b, _ => 0);
        chars[ContextBlock.Instructions] = _staticChars;
        chars[ContextBlock.Menus] = menuChars;
        foreach (var (name, value) in values)
        {
            chars[PlaceholderBlocks[name]] += value.Length;
        }

        return chars;
    }

    private Dictionary<string, string> Values(ContextInput i, PerceptionBlock perception, List<string> memory, List<string> opinions,
        string? daily)
    {
        var none = _v.Phrase("none");
        var sep = _v.Phrase("list_separator");
        string List(IReadOnlyCollection<string> items) => items.Count == 0 ? none : string.Join(sep, items);
        string Lines(IReadOnlyCollection<string> items) =>
            items.Count == 0 ? _v.Phrase("line", ("text", none)) : string.Join("\n", items.Select(t => _v.Phrase("line", ("text", t))));

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = i.Name,
            ["age"] = i.Age.ToString(CultureInfo.InvariantCulture),
            ["species"] = i.Species,
            ["job"] = i.Job,
            ["station_time"] = i.StationTime,
            ["day"] = i.Day.ToString(CultureInfo.InvariantCulture),
            ["day_phase"] = i.DayPhase,
            ["current_action"] = i.CurrentAction,
            ["inventory"] = i.Inventory,
            ["personality_summary"] = i.PersonalitySummary,
            ["likes"] = i.Likes,
            ["dislikes"] = i.Dislikes,
            ["emotion_primary"] = i.EmotionPrimary,
            ["emotion_secondary"] = i.EmotionSecondary,
            ["modifiers"] = i.Modifiers,
            ["immediate_goals"] = List(i.ImmediateGoals.ToList()),
            ["medium_goals"] = List(i.MediumGoals.ToList()),
            ["present_opinions"] = List(opinions),
            ["perception_block"] = perception.Text,
            ["recent_memory"] = Lines(memory),
            ["last_daily_short"] = daily ?? none,
            ["budget_band"] = i.BudgetBand,
        };
    }

    [GeneratedRegex(@"\{\{[a-z_]+\}\}")]
    private static partial Regex PlaceholderPattern();
}
