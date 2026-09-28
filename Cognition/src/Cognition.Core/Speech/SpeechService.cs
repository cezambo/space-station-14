using System.Text.RegularExpressions;
using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;
using Cognition.Core.Scheduling;

namespace Cognition.Core.Speech;

public enum SpeechOutcome
{
    /// <summary>The line is what listeners hear and what is displayed (RL-02).</summary>
    Spoken,

    /// <summary>This character already spoke inside <c>speech.min_interval_s</c> (RJ-11).</summary>
    TooSoon,

    /// <summary>The light-LLM window, or the Jev window for a stale check, has no room (RC-01).</summary>
    Deferred,

    /// <summary>The reply arrived late and the Noul said it no longer fits, or the check could not be read (RJ-12).</summary>
    Stale,

    /// <summary>The model returned nothing that can be said.</summary>
    Blank,
}

/// <summary>
/// One attempt to speak. <see cref="BudgetCost"/> is always 0: speech does not spend thinking budget (RJ-11).
/// </summary>
public sealed record SpeechLine(
    string CharacterId,
    SpeechOutcome Outcome,
    string? Text,
    string? TargetRef,
    bool ToEveryone,
    SpeechVolume Volume,
    string? Detail)
{
    public const string MissingAnswer = "missing_answer";
    public const string Unchecked = "unchecked";

    public int BudgetCost => 0;

    public ActionIntent? ToIntent() => Outcome == SpeechOutcome.Spoken
        ? new ActionIntent(ActionVerb.Speak, ToEveryone ? null : TargetRef, Text: Text, Volume: Volume)
        : null;
}

/// <summary>
/// Facts already phrased for the speech template (P7). <see cref="Now"/> is the scheduler clock, shared with
/// the 6 s gap. <see cref="LastLinesHeard"/> is other characters' speech and stays inside the template's
/// <c>&lt;heard&gt;</c> region (RJ-07).
/// </summary>
public sealed record SpeechRequest(
    string CharacterId,
    double Now,
    string? TargetRef,
    bool ToEveryone,
    SpeechVolume Volume,
    string Name,
    string Job,
    string PersonalitySummary,
    string EmotionPrimary,
    string EmotionSecondary,
    string SpeakTarget,
    string SpeakIntent,
    string ListenerOpinion,
    string TopGoal,
    string RecentMemoryShort,
    string LastLinesHeard,
    string PerceptionNow);

/// <summary>
/// Turns a speak decision into one line (T1.18, RJ-10, RJ-11, RJ-12, RL-02). The light LLM writes it.
/// A character speaks at most once per <c>speech.min_interval_s</c>. When the reply arrives more than
/// <c>speech.stale_after_s</c> after the request, <c>speech_stale.yaml</c> has to agree before the line is said.
/// </summary>
public sealed partial class SpeechService
{
    /// <summary>Room for two short sentences. A request size, not a calibrated threshold (Q-14).</summary>
    public const int MaxOutputTokens = 80;

    private static readonly HashSet<string> SpeechPlaceholders =
    [
        "max_sentences", "name", "job", "personality_summary", "emotion_primary", "emotion_secondary",
        "speak_target", "speak_intent", "listener_opinion", "top_goal", "recent_memory_short", "last_lines_heard",
    ];

    private static readonly HashSet<string> StalePlaceholders =
        ["name", "line", "speak_target", "speak_intent", "perception_block", "recent_memory_short"];

    private readonly SpeechConfig _speech;
    private readonly string _jevModel;
    private readonly LlmTemplate _speechTemplate;
    private readonly JevTemplate _staleTemplate;
    private readonly string _staleQuestionId;
    private readonly double _staleOk;
    private readonly ILlmClient _llm;
    private readonly IJevClient _jev;
    private readonly Scheduler _scheduler;
    private readonly TimeProvider _clock;
    private readonly Dictionary<string, double> _lastSpoken = new(StringComparer.Ordinal);

    public SpeechService(CognitionConfig config, PromptLibrary prompts, ILlmClient llm, IJevClient jev, Scheduler scheduler,
        TimeProvider? clock = null)
    {
        var speech = prompts.Llm("speech");
        var stale = prompts.Jev("speech_stale");
        ExpectPlaceholders(speech.Name, speech.PlaceholderNames, SpeechPlaceholders);
        ExpectPlaceholders(stale.Name, Placeholders.NamesIn(stale.StateTemplate), StalePlaceholders);

        var noul = stale.Questions.Where(q => q.Type == JevQuestionType.Noul).ToList();
        var thresholdKey = noul.Count == 1 ? noul[0].ThresholdKey : null;
        if (thresholdKey is null || !config.Thresholds.Probabilities.TryGetValue(thresholdKey, out var threshold))
            throw new PromptLoadException([$"{stale.Name}: the stale check needs one Noul with its own threshold_key (RJ-18)"]);

        _speech = config.Speech;
        _jevModel = config.Providers.Jev.Model;
        _speechTemplate = speech;
        _staleTemplate = stale;
        _staleQuestionId = noul[0].Id;
        _staleOk = threshold;
        _llm = llm;
        _jev = jev;
        _scheduler = scheduler;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<SpeechLine> SayAsync(SpeechRequest request, CancellationToken ct = default)
    {
        if (_lastSpoken.TryGetValue(request.CharacterId, out var last) && request.Now < last + _speech.MinIntervalS)
            return Quiet(request, SpeechOutcome.TooSoon, null);

        if (!_scheduler.TryAcquire(ScheduleRole.LightLlm, request.Now))
            return Quiet(request, SpeechOutcome.Deferred, null);

        var started = _clock.GetUtcNow();
        var rendered = _speechTemplate.Render(Values(request), LlmRole.Light, MaxOutputTokens);
        var response = await _llm.CompleteAsync(rendered.Request, ct);
        var line = Clean(response.Text, _speech.MaxSentences);
        if (line.Length == 0)
            return Quiet(request, SpeechOutcome.Blank, null);

        var elapsed = (_clock.GetUtcNow() - started).TotalSeconds;
        if (elapsed > _speech.StaleAfterS)
        {
            if (!_scheduler.TryAcquire(ScheduleRole.Jev, request.Now))
                return Quiet(request, SpeechOutcome.Stale, SpeechLine.Unchecked);

            var check = _staleTemplate.Render(_jevModel, new JevRenderInput(StaleValues(request, line)));
            var answer = await _jev.EvaluateAsync(check.Request, ct);
            if (answer.Answers.TryGetValue(_staleQuestionId, out var given) && given is NoulAnswer noul)
            {
                if (noul.PYes < _staleOk)
                    return Quiet(request, SpeechOutcome.Stale, null);
            }
            else
            {
                return Quiet(request, SpeechOutcome.Stale, SpeechLine.MissingAnswer);
            }
        }

        _lastSpoken[request.CharacterId] = request.Now;
        return new SpeechLine(request.CharacterId, SpeechOutcome.Spoken, line, request.TargetRef, request.ToEveryone,
            request.Volume, null);
    }

    private static SpeechLine Quiet(SpeechRequest request, SpeechOutcome outcome, string? detail) =>
        new(request.CharacterId, outcome, null, request.TargetRef, request.ToEveryone, request.Volume, detail);

    private Dictionary<string, string> Values(SpeechRequest request) => new(StringComparer.Ordinal)
    {
        ["max_sentences"] = _speech.MaxSentences.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["name"] = request.Name,
        ["job"] = request.Job,
        ["personality_summary"] = request.PersonalitySummary,
        ["emotion_primary"] = request.EmotionPrimary,
        ["emotion_secondary"] = request.EmotionSecondary,
        ["speak_target"] = request.SpeakTarget,
        ["speak_intent"] = request.SpeakIntent,
        ["listener_opinion"] = request.ListenerOpinion,
        ["top_goal"] = request.TopGoal,
        ["recent_memory_short"] = request.RecentMemoryShort,
        ["last_lines_heard"] = request.LastLinesHeard,
    };

    private static Dictionary<string, string> StaleValues(SpeechRequest request, string line) => new(StringComparer.Ordinal)
    {
        ["name"] = request.Name,
        ["line"] = line.Replace("\"", "'", StringComparison.Ordinal),
        ["speak_target"] = request.SpeakTarget,
        ["speak_intent"] = request.SpeakIntent,
        ["perception_block"] = request.PerceptionNow,
        ["recent_memory_short"] = request.RecentMemoryShort,
    };

    private static void ExpectPlaceholders(string template, IReadOnlyList<string> actual, HashSet<string> expected)
    {
        var names = actual.ToHashSet(StringComparer.Ordinal);
        var unknown = names.Where(n => !expected.Contains(n)).Order(StringComparer.Ordinal);
        var unused = expected.Where(n => !names.Contains(n)).Order(StringComparer.Ordinal);
        var problems = unknown.Concat(unused).ToList();
        if (problems.Count > 0)
            throw new ArgumentException($"template '{template}': placeholders do not match the speech service ({string.Join(", ", problems)})");
    }

    private static string Clean(string raw, int maxSentences)
    {
        var text = Whitespace().Replace(raw, " ").Trim();
        if (text.Length >= 2 && ((text[0] == '"' && text[^1] == '"') || (text[0] == '\'' && text[^1] == '\'')))
            text = text[1..^1].Trim();
        if (text.Length == 0 || maxSentences < 1)
            return "";

        var kept = new List<string>();
        foreach (Match match in Sentences().Matches(text))
        {
            if (kept.Count == maxSentences)
                break;
            kept.Add(match.Value);
        }

        return string.Concat(kept).Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^.!?]+[.!?]+(?:\s+|$)|[^.!?]+$")]
    private static partial Regex Sentences();
}
