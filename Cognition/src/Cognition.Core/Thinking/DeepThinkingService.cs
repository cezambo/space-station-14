using System.Text.Json;
using Cognition.Core.Config;
using Cognition.Core.Minds;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;
using Cognition.Core.Scheduling;

namespace Cognition.Core.Thinking;

public enum ThinkOutcome
{
    /// <summary>The thought is a memory and the immediate goals are the new plan. The cost was paid.</summary>
    Thought,

    /// <summary>The remaining budget cannot pay this mode. Nothing was called (RG-03).</summary>
    Unaffordable,

    /// <summary>That model's rate window is full (RC-01).</summary>
    Deferred,

    /// <summary>The reply was not the schema's JSON. Nothing was charged.</summary>
    Invalid,
}

/// <summary>
/// One deliberate thought (T1.19, RG-02, RG-03). <see cref="Cost"/> is 0 unless <see cref="Outcome"/> is
/// <see cref="ThinkOutcome.Thought"/>, and <see cref="RemainingAfter"/> is never below zero when the request's
/// remaining budget was not.
/// </summary>
public sealed record ThinkResult(
    ThinkOutcome Outcome,
    ThinkModes Mode,
    int Cost,
    int RemainingAfter,
    MemoryEntry? Thought,
    IReadOnlyList<Goal> ImmediateGoals,
    string? Detail)
{
    public const string Schema = "schema";
    public const string EmptyThought = "empty_thought";
    public const string BlankGoal = "blank_goal";
}

/// <summary>
/// Phrases already written for <c>deep_think.md</c> (P7). <see cref="Mode"/> is light or deep.
/// <see cref="RemainingUnits"/> is the mind's budget before this thought. <see cref="ReservedGoalIds"/> are
/// ids already used by other horizons, so new immediate goals do not collide.
/// </summary>
public sealed record ThinkRequest(
    string CharacterId,
    double Now,
    ThinkModes Mode,
    int RemainingUnits,
    int PersonalDay,
    double AtSeconds,
    string FullProfile,
    string MediumGoals,
    string LongGoals,
    string ImmediateGoals,
    string Fortnightly,
    string Daily,
    string Recent,
    string Opinions,
    string PerceptionBlock,
    string EmotionSummary,
    IReadOnlySet<string>? ReservedGoalIds = null);

/// <summary>
/// Re-plans immediate goals when Jev chooses to think (T1.19, RG-02, RG-03). Light and deep use
/// <c>deep_think.md</c>; only the model and the budget cost change. A reply that fails the schema is dropped
/// and costs nothing. The thought comes back as a recent memory for the caller to store.
/// </summary>
public sealed class DeepThinkingService
{
    /// <summary>Room for a short thought and up to three goals. A request size, not a calibrated threshold (Q-15).</summary>
    public const int MaxOutputTokens = 600;

    /// <summary>Own thoughts sit at the top of the 1–5 importance scale until the memory filter (T1.21) keeps them itself.</summary>
    public const int ThoughtImportance = 5;

    private static readonly HashSet<string> Placeholders =
    [
        "full_profile", "medium_goals", "long_goals", "immediate_goals", "fortnightly", "daily", "recent",
        "opinions", "perception_block", "emotion_summary",
    ];

    private readonly BudgetConfig _budget;
    private readonly LlmTemplate _template;
    private readonly JsonSchemaLite _schema;
    private readonly ILlmClient _llm;
    private readonly Scheduler _scheduler;

    public DeepThinkingService(CognitionConfig config, PromptLibrary prompts, ILlmClient llm, Scheduler scheduler)
    {
        var template = prompts.Llm("deep_think");
        if (template.Schema is null)
            throw new PromptLoadException(["deep_think: the thought reply needs a SCHEMA"]);
        var names = template.PlaceholderNames.ToHashSet(StringComparer.Ordinal);
        var drift = names.Where(n => !Placeholders.Contains(n)).Concat(Placeholders.Where(n => !names.Contains(n)))
            .Order(StringComparer.Ordinal).ToList();
        if (drift.Count > 0)
            throw new ArgumentException($"template '{template.Name}': placeholders do not match the thinking service ({string.Join(", ", drift)})");

        _budget = config.Budget;
        _template = template;
        _schema = JsonSchemaLite.Parse(template.Schema);
        _llm = llm;
        _scheduler = scheduler;
    }

    public async Task<ThinkResult> ThinkAsync(ThinkRequest request, CancellationToken ct = default)
    {
        if (request.Mode is not (ThinkModes.Light or ThinkModes.Deep))
            throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "a thought is light or deep");

        var cost = request.Mode == ThinkModes.Deep ? _budget.DeepCost : _budget.LightCost;
        if (request.RemainingUnits < cost)
            return Stop(request, ThinkOutcome.Unaffordable, null);

        var role = request.Mode == ThinkModes.Deep ? ScheduleRole.HeavyLlm : ScheduleRole.LightLlm;
        if (!_scheduler.TryAcquire(role, request.Now))
            return Stop(request, ThinkOutcome.Deferred, null);

        LlmResponse response;
        try
        {
            var rendered = _template.Render(Values(request), request.Mode == ThinkModes.Deep ? LlmRole.Heavy : LlmRole.Light, MaxOutputTokens);
            response = await _llm.CompleteAsync(rendered.Request, ct);
        }
        catch (LlmSchemaException)
        {
            return Stop(request, ThinkOutcome.Invalid, ThinkResult.Schema);
        }

        if (!TryRead(response.Text, request, out var thought, out var goals, out var detail))
            return Stop(request, ThinkOutcome.Invalid, detail);

        var memory = new MemoryEntry(0, MemoryLevel.Recent, MemorySource.Thought, request.PersonalDay, null,
            request.AtSeconds, thought, null, ThoughtImportance);
        return new ThinkResult(ThinkOutcome.Thought, request.Mode, cost, request.RemainingUnits - cost, memory, goals, null);
    }

    private static ThinkResult Stop(ThinkRequest request, ThinkOutcome outcome, string? detail) =>
        new(outcome, request.Mode, 0, request.RemainingUnits, null, [], detail);

    private bool TryRead(string text, ThinkRequest request, out string thought, out List<Goal> goals, out string detail)
    {
        thought = "";
        goals = [];
        detail = ThinkResult.Schema;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return false;
        }

        using (doc)
        {
            if (_schema.Validate(doc.RootElement).Count > 0)
                return false;
            var root = doc.RootElement;
            thought = (root.GetProperty("thought").GetString() ?? "").Trim();
            if (thought.Length == 0)
            {
                detail = ThinkResult.EmptyThought;
                return false;
            }

            var reserved = new HashSet<string>(request.ReservedGoalIds ?? new HashSet<string>(), StringComparer.Ordinal);
            var next = 1;
            foreach (var item in root.GetProperty("immediate_goals").EnumerateArray())
            {
                var goalText = (item.GetProperty("text").GetString() ?? "").Trim();
                var check = (item.GetProperty("success_check").GetString() ?? "").Trim();
                if (goalText.Length == 0 || check.Length == 0)
                {
                    detail = ThinkResult.BlankGoal;
                    return false;
                }

                string id;
                do
                {
                    id = "i" + next.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    next++;
                }
                while (!reserved.Add(id));

                var priority = Enum.Parse<Priority>(item.GetProperty("priority").GetString()!, ignoreCase: true);
                goals.Add(new Goal(id, goalText, Horizon.Immediate, GoalStatus.Active, priority, check, request.PersonalDay));
            }

            return true;
        }
    }

    private static Dictionary<string, string> Values(ThinkRequest request) => new(StringComparer.Ordinal)
    {
        ["full_profile"] = request.FullProfile,
        ["medium_goals"] = request.MediumGoals,
        ["long_goals"] = request.LongGoals,
        ["immediate_goals"] = request.ImmediateGoals,
        ["fortnightly"] = request.Fortnightly,
        ["daily"] = request.Daily,
        ["recent"] = request.Recent,
        ["opinions"] = request.Opinions,
        ["perception_block"] = request.PerceptionBlock,
        ["emotion_summary"] = request.EmotionSummary,
    };
}
