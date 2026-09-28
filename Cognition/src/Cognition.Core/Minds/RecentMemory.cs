using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;
using Cognition.Core.Scheduling;

namespace Cognition.Core.Minds;

/// <summary>
/// One thing that happened, with names already in words (RA-06). <see cref="Flagged"/> marks an event a
/// scenario considers important. <see cref="Text"/> is the line, for <see cref="WorldEventKind.Spoke"/>.
/// </summary>
public sealed record ObservedEvent(
    double AtSeconds,
    WorldEventKind Kind,
    ActionVerb? Verb = null,
    string? TargetName = null,
    string? ItemName = null,
    string? Reason = null,
    string? Text = null,
    bool Flagged = false);

/// <summary>A thought the character already had. Stored as-is; the filter is not asked (RMe-01).</summary>
public sealed record OwnThought(double AtSeconds, string Text);

/// <summary>What one pass over a personal day produced. <see cref="FlaggedKept"/> counts flagged events whose sentence survived.</summary>
public sealed record RecentMemoryResult(
    IReadOnlyList<MemoryEntry> Memories,
    int FlaggedEvents,
    int FlaggedKept);

/// <summary>
/// The character facts the memory filter reads. Phrases, not numbers (P7).
/// </summary>
public sealed record MemoryFilterContext(
    string Name,
    string Job,
    string PersonalitySummary,
    string ImmediateGoals,
    string MediumGoals);

/// <summary>
/// Turns a day's raw events into recent memories (T1.21, RMe-01, RS-11). Identical events whose successive
/// gaps stay within <c>memory.aggregate_window_s</c> become one sentence before Jev is asked. Own speech and
/// thoughts skip that question. Past <c>memory.recent_hard_cap</c>, the lowest-importance memory goes, and a
/// speech or a thought goes only when nothing else is left.
/// </summary>
public sealed class RecentMemory
{
    /// <summary>Own speech and thoughts sit at the top of the 1–5 scale, with deep thoughts (Q-16).</summary>
    public const int ProtectedImportance = 5;

    private static readonly HashSet<string> FilterPlaceholders =
        ["name", "job", "personality_summary", "immediate_goals", "medium_goals", "event_text"];

    private readonly MemoryConfig _memory;
    private readonly Vocabulary _vocabulary;
    private readonly JevTemplate _filter;
    private readonly string _keepId;
    private readonly string _importanceId;
    private readonly double _keepAt;
    private readonly string _jevModel;
    private readonly IJevClient _jev;
    private readonly Scheduler _scheduler;

    public RecentMemory(CognitionConfig config, PromptLibrary prompts, IJevClient jev, Scheduler scheduler)
    {
        var filter = prompts.Jev("memory_filter");
        var names = Placeholders.NamesIn(filter.StateTemplate).ToHashSet(StringComparer.Ordinal);
        var drift = names.Where(n => !FilterPlaceholders.Contains(n)).Concat(FilterPlaceholders.Where(n => !names.Contains(n)))
            .Order(StringComparer.Ordinal).ToList();
        if (drift.Count > 0)
            throw new ArgumentException($"template '{filter.Name}': placeholders do not match recent memory ({string.Join(", ", drift)})");

        var keep = filter.Questions.SingleOrDefault(q => q.Type == JevQuestionType.Noul);
        var importance = filter.Questions.SingleOrDefault(q => q.Type == JevQuestionType.Score);
        if (keep?.ThresholdKey is null || !config.Thresholds.Probabilities.TryGetValue(keep.ThresholdKey, out var threshold) || importance is null)
            throw new PromptLoadException(["memory_filter: needs one Noul with its own threshold_key and one Score (RJ-18)"]);

        _memory = config.Memory;
        _vocabulary = prompts.Vocabulary;
        _filter = filter;
        _keepId = keep.Id;
        _importanceId = importance.Id;
        _keepAt = threshold;
        _jevModel = config.Providers.Jev.Model;
        _jev = jev;
        _scheduler = scheduler;
    }

    public async Task<RecentMemoryResult> RecordAsync(double now, int personalDay, MemoryFilterContext who,
        IReadOnlyList<MemoryEntry> already, IReadOnlyList<ObservedEvent> events, IReadOnlyList<OwnThought> thoughts,
        CancellationToken ct = default)
    {
        var flagged = events.Count(e => e.Flagged);
        var keptFlagged = 0;
        var added = new List<MemoryEntry>();
        foreach (var thought in thoughts)
        {
            if (string.IsNullOrWhiteSpace(thought.Text))
                continue;
            added.Add(Entry(personalDay, thought.AtSeconds, MemorySource.Thought, SentenceThought(thought.Text), ProtectedImportance));
        }

        var at = now;
        foreach (var group in Groups(events))
        {
            var sentence = Sentence(group);
            if (group[0].Kind == WorldEventKind.Spoke)
            {
                added.Add(Entry(personalDay, group[0].AtSeconds, MemorySource.OwnSpeech, sentence, ProtectedImportance));
                keptFlagged += group.Count(e => e.Flagged);
                continue;
            }

            while (!_scheduler.TryAcquire(ScheduleRole.Jev, at))
                at += 1;

            var rendered = _filter.Render(_jevModel, new JevRenderInput(Values(who, sentence)));
            var answer = await _jev.EvaluateAsync(rendered.Request, ct);
            if (answer.Answers.TryGetValue(_keepId, out var keep) && keep is NoulAnswer noul && noul.PYes >= _keepAt
                && answer.Answers.TryGetValue(_importanceId, out var importance) && importance is ScoreAnswer score)
            {
                added.Add(Entry(personalDay, group[0].AtSeconds, MemorySource.Event, sentence, ImportanceOf(score)));
                keptFlagged += group.Count(e => e.Flagged);
            }
        }

        var memories = Cap(already.Concat(added).OrderBy(m => m.AtSeconds ?? 0).ToList());
        return new RecentMemoryResult(memories, flagged, keptFlagged);
    }

    private List<MemoryEntry> Cap(List<MemoryEntry> memories)
    {
        while (memories.Count > _memory.RecentHardCap)
        {
            var drop = 0;
            for (var i = 1; i < memories.Count; i++)
            {
                if (CompareDrop(memories[i], memories[drop]) < 0)
                    drop = i;
            }

            memories.RemoveAt(drop);
        }

        return memories;
    }

    private static int CompareDrop(MemoryEntry candidate, MemoryEntry current)
    {
        var byProtection = Protection(candidate).CompareTo(Protection(current));
        if (byProtection != 0)
            return byProtection;
        var byImportance = (candidate.Importance ?? 0).CompareTo(current.Importance ?? 0);
        if (byImportance != 0)
            return byImportance;
        return (candidate.AtSeconds ?? 0).CompareTo(current.AtSeconds ?? 0);
    }

    private static int Protection(MemoryEntry memory) =>
        memory.Source is MemorySource.OwnSpeech or MemorySource.Thought ? 1 : 0;

    /// <summary>The most probable level, as 1–5. A tie takes the higher level. The weighted score is not used (RJ-19).</summary>
    public static int ImportanceOf(ScoreAnswer score)
    {
        var best = 0;
        var bestP = double.NegativeInfinity;
        for (var i = 0; i < score.Probabilities.Count; i++)
        {
            if (score.Probabilities[i] < bestP)
                continue;
            bestP = score.Probabilities[i];
            best = i;
        }

        return Math.Clamp(best + 1, 1, 5);
    }

    private List<List<ObservedEvent>> Groups(IReadOnlyList<ObservedEvent> events)
    {
        var groups = new List<List<ObservedEvent>>();
        foreach (var ev in events.OrderBy(e => e.AtSeconds))
        {
            var open = groups.LastOrDefault(g => Same(g[0], ev) && ev.AtSeconds - g[^1].AtSeconds <= _memory.AggregateWindowS);
            if (open is null)
                groups.Add([ev]);
            else
                open.Add(ev);
        }

        return groups;
    }

    private static bool Same(ObservedEvent a, ObservedEvent b) =>
        a.Kind == b.Kind && a.Verb == b.Verb && a.TargetName == b.TargetName && a.ItemName == b.ItemName
        && a.Reason == b.Reason && a.Text == b.Text;

    private string Sentence(IReadOnlyList<ObservedEvent> group)
    {
        var first = group[0];
        var times = TimesWord(group.Count);
        if (first.Kind == WorldEventKind.Spoke)
            return _vocabulary.Memory("said", ("text", Quote(first.Text)));
        if (first.Kind == WorldEventKind.Damaged)
            return first.Reason is { Length: > 0 } reason
                ? _vocabulary.Memory(times is null ? "hurt_kind" : "hurt_kind_again", ("kind", reason), ("times", times ?? ""))
                : _vocabulary.Memory(times is null ? "hurt" : "hurt_again", ("times", times ?? ""));
        if (first.Kind == WorldEventKind.FellAsleep)
            return first.TargetName is { Length: > 0 } where
                ? _vocabulary.Memory("fell_asleep_where", ("where", where))
                : _vocabulary.Memory("fell_asleep");
        if (first.Kind == WorldEventKind.Woke)
            return _vocabulary.Memory("woke");
        if (first.Kind == WorldEventKind.Collapsed)
            return _vocabulary.Memory(times is null ? "collapsed" : "collapsed_again", ("times", times ?? ""));
        if (first.Kind == WorldEventKind.LostConsciousness)
            return _vocabulary.Memory(times is null ? "unconscious" : "unconscious_again", ("times", times ?? ""));

        var verb = first.Verb is { } v ? _vocabulary.Word(v) : _vocabulary.Memory("something");
        var what = What(first);
        if (first.Kind == WorldEventKind.ActionFailed)
        {
            if (first.Reason is { Length: > 0 } reason)
                return _vocabulary.Memory(times is null ? "failed_because" : "failed_because_again",
                    ("verb", verb), ("what", what), ("reason", reason), ("times", times ?? ""));
            return _vocabulary.Memory(times is null ? "failed" : "failed_again",
                ("verb", verb), ("what", what), ("times", times ?? ""));
        }

        return _vocabulary.Memory(times is null ? "did" : "did_again", ("verb", verb), ("what", what), ("times", times ?? ""));
    }

    private string SentenceThought(string text) => _vocabulary.Memory("thought", ("text", text.Trim()));

    private string What(ObservedEvent ev)
    {
        if (ev.ItemName is { Length: > 0 } item && ev.TargetName is { Length: > 0 } target)
        {
            var key = ev.Verb switch
            {
                ActionVerb.Give => "give_what",
                ActionVerb.Put => "put_what",
                ActionVerb.Take => "take_what",
                ActionVerb.Lock or ActionVerb.Unlock => "lock_what",
                _ => "use_what",
            };
            return _vocabulary.Memory(key, ("item", item), ("target", target));
        }

        return ev.TargetName ?? ev.ItemName ?? _vocabulary.Memory("something");
    }

    private string? TimesWord(int count) => count switch
    {
        <= 1 => null,
        <= 3 => _vocabulary.Memory("times_couple"),
        <= 9 => _vocabulary.Memory("times_several"),
        _ => _vocabulary.Memory("times_many"),
    };

    private static string Quote(string? text) => (text ?? "").Replace("\"", "'", StringComparison.Ordinal).Trim();

    private static Dictionary<string, string> Values(MemoryFilterContext who, string sentence) => new(StringComparer.Ordinal)
    {
        ["name"] = who.Name,
        ["job"] = who.Job,
        ["personality_summary"] = who.PersonalitySummary,
        ["immediate_goals"] = who.ImmediateGoals,
        ["medium_goals"] = who.MediumGoals,
        ["event_text"] = sentence,
    };

    private static MemoryEntry Entry(int day, double at, MemorySource source, string text, int importance) =>
        new(0, MemoryLevel.Recent, source, day, null, at, text, null, importance);
}
