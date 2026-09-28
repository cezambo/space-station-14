using Cognition.Core.Config;
using Cognition.Core.Minds;

namespace Cognition.Core.Scheduling;

/// <summary>Why a character might need a decision before the regular interval (RJ-01).</summary>
public enum DecisionTrigger
{
    ActionCompleted,
    ActionFailed,
    Addressed,
    Damage,
    SalientEntity,
    NeedBandChanged,
    GoalCompleted,
    GoalBlocked,
}

public enum ScheduleRole
{
    Jev,
    LightLlm,
    HeavyLlm,
}

public enum ScheduleOutcome
{
    /// <summary>A Jev decision call may be made.</summary>
    Grant,

    /// <summary>Not selected. The character keeps the current action (RC-02); this is the HTN fallback stub.</summary>
    KeepCurrent,

    /// <summary>The minimum interval has not elapsed, or no trigger and the maximum interval has not elapsed.</summary>
    NotDue,

    /// <summary>Asleep or under player control: no decision call (RC-01).</summary>
    Excluded,
}

public sealed record ScheduleRequest(
    string CharacterId,
    ControlMode Control,
    bool Asleep,
    bool Idle,
    bool VisibleToPlayer,
    double? LastDecisionAt,
    IReadOnlyList<DecisionTrigger> Triggers);

public sealed record ScheduleDecision(string CharacterId, ScheduleOutcome Outcome, double Priority);

/// <summary>
/// Who may call Jev or an LLM right now (T1.17, RJ-01, RC-01, RC-02). One grant per character per call, highest
/// priority first, and never more than the role's <c>max_rps</c> grants in any one-second window. Jev is the
/// only decision provider; a character left out keeps the current action.
/// </summary>
public sealed class Scheduler
{
    private readonly DecisionConfig _decision;
    private readonly SchedulerConfig _weights;
    private readonly Dictionary<ScheduleRole, SlidingWindow> _windows;
    private double _now = double.NegativeInfinity;

    public Scheduler(CognitionConfig config)
    {
        _decision = config.Decision;
        _weights = config.Scheduler;
        _windows = new Dictionary<ScheduleRole, SlidingWindow>
        {
            [ScheduleRole.Jev] = new(config.Providers.Jev.MaxRps),
            [ScheduleRole.LightLlm] = new(config.Providers.Light.MaxRps),
            [ScheduleRole.HeavyLlm] = new(config.Providers.Heavy.MaxRps),
        };
    }

    /// <summary>
    /// Urgency of the strongest pending trigger, in [0, 1]. Damage and being addressed outrank a failed action,
    /// which outranks ordinary events (Q-13). No trigger is 0.
    /// </summary>
    public static double Urgency(IReadOnlyList<DecisionTrigger> triggers)
    {
        var best = 0.0;
        foreach (var trigger in triggers)
        {
            var value = trigger is DecisionTrigger.Damage or DecisionTrigger.Addressed ? 1.0
                : trigger is DecisionTrigger.ActionFailed or DecisionTrigger.GoalBlocked ? 0.7
                : 0.4;
            if (value > best)
                best = value;
        }

        return best;
    }

    public double Now => _now;

    public void AdvanceTo(double now)
    {
        if (double.IsNaN(now) || now < _now)
            throw new ArgumentOutOfRangeException(nameof(now), now, "scheduler time only moves forward");
        _now = now;
    }

    /// <summary>A non-decision call (speech, thinking). False when that role's window is full.</summary>
    public bool TryAcquire(ScheduleRole role, double now)
    {
        AdvanceTo(now);
        return _windows[role].TryTake(now);
    }

    public IReadOnlyList<ScheduleDecision> Plan(double now, IReadOnlyList<ScheduleRequest> characters)
    {
        AdvanceTo(now);
        var decisions = new ScheduleDecision[characters.Count];
        var ranked = new List<(int Index, double Priority)>();
        for (var i = 0; i < characters.Count; i++)
        {
            var character = characters[i];
            if (character.Asleep || character.Control != ControlMode.Ai)
            {
                decisions[i] = new ScheduleDecision(character.CharacterId, ScheduleOutcome.Excluded, 0);
                continue;
            }

            var due = Due(character, out var since, out var maxInterval);
            var priority = Priority(character, since, maxInterval);
            if (!due)
            {
                decisions[i] = new ScheduleDecision(character.CharacterId, ScheduleOutcome.NotDue, priority);
                continue;
            }

            ranked.Add((i, priority));
        }

        ranked.Sort((a, b) =>
        {
            var byPriority = b.Priority.CompareTo(a.Priority);
            return byPriority != 0 ? byPriority : string.Compare(characters[a.Index].CharacterId, characters[b.Index].CharacterId, StringComparison.Ordinal);
        });

        foreach (var (index, priority) in ranked)
        {
            var outcome = _windows[ScheduleRole.Jev].TryTake(now) ? ScheduleOutcome.Grant : ScheduleOutcome.KeepCurrent;
            decisions[index] = new ScheduleDecision(characters[index].CharacterId, outcome, priority);
        }

        return decisions;
    }

    private bool Due(ScheduleRequest character, out double since, out double maxInterval)
    {
        var waitingOnInterval = character.Triggers.Count == 0;
        maxInterval = character.Idle && waitingOnInterval ? _decision.IdleMaxIntervalS : _decision.MaxIntervalS;
        if (character.LastDecisionAt is not { } last)
        {
            since = maxInterval;
            return true;
        }

        since = _now - last;
        if (since < _decision.MinIntervalS)
            return false;
        return !waitingOnInterval || since >= maxInterval;
    }

    private double Priority(ScheduleRequest character, double since, double maxInterval)
    {
        var waited = Math.Min(1, since / maxInterval);
        var visible = character.VisibleToPlayer ? 1 : 0;
        return (_weights.WUrgency * Urgency(character.Triggers)) + (_weights.WWait * waited) + (_weights.WVisible * visible);
    }

    /// <summary>At most <c>floor(rate)</c> takes in any window of one second.</summary>
    private sealed class SlidingWindow(double perSecond)
    {
        private readonly int _limit = (int)Math.Floor(perSecond + 1e-9);
        private readonly Queue<double> _taken = new();

        public bool TryTake(double now)
        {
            while (_taken.Count > 0 && _taken.Peek() <= now - 1)
            {
                _taken.Dequeue();
            }

            if (_taken.Count >= _limit)
                return false;
            _taken.Enqueue(now);
            return true;
        }
    }
}
