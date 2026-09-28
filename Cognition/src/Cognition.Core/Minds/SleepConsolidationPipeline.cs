using Cognition.Core.Config;

namespace Cognition.Core.Minds;

/// <summary>Thrown by <see cref="SleepConsolidationPipeline.AfterCheckpoint"/> to stop a run without failing the step.</summary>
public sealed class ConsolidationHaltedException(string stepId)
    : Exception($"consolidation halted after step '{stepId}'")
{
    public string StepId { get; } = stepId;
}

/// <summary>
/// What one consolidation step sees: the working mind, memories as of the start of this sleep plus anything
/// earlier steps added, and the personal day that is ending.
/// </summary>
public sealed record ConsolidationView(Mind Mind, IReadOnlyList<MemoryEntry> Memories, int EndingDay);

/// <summary>Mind edits and memory rows one step wants committed. Returned anew each call; the pipeline applies it once.</summary>
public sealed record ConsolidationPatch(
    Mind Mind,
    IReadOnlyList<MemoryEntry>? Add = null,
    IReadOnlyList<long>? Delete = null)
{
    public static ConsolidationPatch None(Mind mind) => new(mind);
}

public interface IConsolidationStep
{
    string Id { get; }

    Task<ConsolidationPatch> ApplyAsync(ConsolidationView view, CancellationToken ct);
}

/// <summary>A step whose body is a function, for tests and for wiring later tasks.</summary>
public sealed class DelegateConsolidationStep(string id, Func<ConsolidationView, CancellationToken, Task<ConsolidationPatch>> apply)
    : IConsolidationStep
{
    public DelegateConsolidationStep(string id, Func<ConsolidationView, ConsolidationPatch> apply)
        : this(id, (view, _) => Task.FromResult(apply(view)))
    {
    }

    public string Id { get; } = id;

    public Task<ConsolidationPatch> ApplyAsync(ConsolidationView view, CancellationToken ct) => apply(view, ct);
}

public static class ConsolidationSteps
{
    public const string DailySummary = "daily_summary";
    public const string Opinions = "opinions";
    public const string Rupture = "rupture";
    public const string MediumGoals = "medium_goals";
    public const string Fortnightly = "fortnightly";
    public const string Budget = "budget";

    /// <summary>§8.3 order. Steps [1]–[5] are placeholders until T1.23–T1.26; [6] refills the budget (RS-10).</summary>
    public static IReadOnlyList<IConsolidationStep> Defaults { get; } =
    [
        new DailySummaryConsolidationStep(),
        new NoOpConsolidationStep(Opinions),
        new NoOpConsolidationStep(Rupture),
        new NoOpConsolidationStep(MediumGoals),
        new NoOpConsolidationStep(Fortnightly),
        new BudgetRefillConsolidationStep(),
    ];
}

/// <summary>
/// Placeholder for T1.23: one daily memory whose text is the day's recent memories joined. Replaced by the
/// light-LLM summary; kept deterministic so the pipeline can be tested without a model.
/// </summary>
public sealed class DailySummaryConsolidationStep : IConsolidationStep
{
    public string Id => ConsolidationSteps.DailySummary;

    public Task<ConsolidationPatch> ApplyAsync(ConsolidationView view, CancellationToken ct)
    {
        var todays = view.Memories
            .Where(m => m.Level == MemoryLevel.Recent && m.Day == view.EndingDay)
            .Select(m => m.Text)
            .ToList();
        if (todays.Count == 0)
            return Task.FromResult(ConsolidationPatch.None(view.Mind));
        var text = string.Join("; ", todays);
        var summary = new MemoryEntry(0, MemoryLevel.Daily, MemorySource.Summary, view.EndingDay, null, null, text,
            $"{todays.Count} events", null);
        return Task.FromResult(new ConsolidationPatch(view.Mind, [summary]));
    }
}

/// <summary>Step [6]: the day's budget is spent by thinking, never by consolidation (RS-10).</summary>
public sealed class BudgetRefillConsolidationStep : IConsolidationStep
{
    public string Id => ConsolidationSteps.Budget;

    public Task<ConsolidationPatch> ApplyAsync(ConsolidationView view, CancellationToken ct)
    {
        var budget = view.Mind.ThinkingBudget;
        var refilled = view.Mind with { ThinkingBudget = budget with { Remaining = budget.DailyUnits } };
        return Task.FromResult(ConsolidationPatch.None(refilled));
    }
}

public sealed class NoOpConsolidationStep(string id) : IConsolidationStep
{
    public string Id { get; } = id;

    public Task<ConsolidationPatch> ApplyAsync(ConsolidationView view, CancellationToken ct) =>
        Task.FromResult(ConsolidationPatch.None(view.Mind));
}

public sealed record ConsolidationResult(Mind Mind, IReadOnlyList<string> DeferredSteps, IReadOnlyList<string> Log, bool Resumed);

/// <summary>Checkpoint persisted between steps (RS-09). <see cref="CompletedThrough"/> is how many steps finished.</summary>
public sealed record ConsolidationCheckpoint(
    int CompletedThrough,
    IReadOnlyDictionary<string, int> FailedAttempts,
    IReadOnlyList<string> Deferred,
    Mind Working,
    IReadOnlyList<MemoryEntry> Added,
    IReadOnlyList<long> Deleted,
    long LastRecentId,
    int EndingDay,
    IReadOnlyList<MemoryEntry> Snapshot);

/// <summary>
/// §8.3 during a consolidated sleep (T1.22, RS-09/10/12). Works on a copy; each finished step is checkpointed;
/// the mind in the store stays at the prior state until one commit, which also retags memories appended during
/// the sleep onto the next day and drops the checkpoint. A step that still fails after
/// <c>consolidation.step_max_retries</c> retries is deferred to the next sleep.
/// </summary>
public sealed class SleepConsolidationPipeline
{
    private readonly IMindStore _store;
    private readonly CognitionConfig _config;
    private readonly IReadOnlyList<IConsolidationStep> _steps;

    public SleepConsolidationPipeline(IMindStore store, CognitionConfig config, IReadOnlyList<IConsolidationStep>? steps = null)
    {
        _store = store;
        _config = config;
        _steps = steps ?? ConsolidationSteps.Defaults;
        var duplicate = _steps.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key).Order(StringComparer.Ordinal).ToList();
        if (duplicate.Count > 0)
            throw new ArgumentException($"consolidation steps repeat an id: {string.Join(", ", duplicate)}");
    }

    /// <summary>Called after a step's checkpoint is saved. Throw <see cref="ConsolidationHaltedException"/> to simulate a kill.</summary>
    public Action<string>? AfterCheckpoint { get; set; }

    public async Task<ConsolidationResult> RunAsync(Guid stableGuid, CancellationToken ct = default)
    {
        var log = new List<string>();
        var mind = _store.Load(stableGuid) ?? throw new MindStoreException($"mind {stableGuid} does not exist");
        var (checkpoint, resumed) = Start(stableGuid, mind, log);

        for (var i = 0; i < _steps.Count; i++)
        {
            if (checkpoint.CompletedThrough > i)
                continue;
            checkpoint = await RunStep(stableGuid, checkpoint, _steps[i], i, log, ct).ConfigureAwait(false);
        }

        var committed = Commit(stableGuid, checkpoint, log);
        return new ConsolidationResult(committed, checkpoint.Deferred, log, resumed);
    }

    private (ConsolidationCheckpoint Checkpoint, bool Resumed) Start(Guid stableGuid, Mind mind, List<string> log)
    {
        var stored = _store.LoadCheckpoint(stableGuid);
        if (stored is not null && stored.BaseVersion == mind.Version && TryRead(stored.Body, out var checkpoint)
            && checkpoint.EndingDay == mind.Sleep.PersonalDay && checkpoint.Working.StableGuid == mind.StableGuid)
        {
            return (checkpoint, true);
        }

        if (stored is not null)
        {
            _store.DeleteCheckpoint(stableGuid);
            log.Add("consolidation checkpoint discarded: the mind changed since it was written");
        }

        var snapshot = _store.Memories(stableGuid);
        var lastRecent = snapshot.Where(m => m.Level == MemoryLevel.Recent).Select(m => m.Id).DefaultIfEmpty(0).Max();
        checkpoint = new ConsolidationCheckpoint(0, new Dictionary<string, int>(), [], mind, [], [], lastRecent,
            mind.Sleep.PersonalDay, snapshot);
        _store.SaveCheckpoint(stableGuid, mind.Version, MindJson.Serialize(checkpoint));
        return (checkpoint, false);
    }

    private async Task<ConsolidationCheckpoint> RunStep(Guid stableGuid, ConsolidationCheckpoint checkpoint, IConsolidationStep step,
        int index, List<string> log, CancellationToken ct)
    {
        while (checkpoint.CompletedThrough == index)
        {
            ct.ThrowIfCancellationRequested();
            ConsolidationPatch patch;
            try
            {
                patch = await step.ApplyAsync(View(checkpoint), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or ConsolidationHaltedException))
            {
                checkpoint = Fail(stableGuid, checkpoint, step, ex, log);
                continue;
            }

            checkpoint = checkpoint with
            {
                CompletedThrough = index + 1,
                Working = patch.Mind with
                {
                    Version = checkpoint.Working.Version,
                    Id = checkpoint.Working.Id,
                    StableGuid = checkpoint.Working.StableGuid,
                },
                Added = checkpoint.Added.Concat(patch.Add ?? []).ToList(),
                Deleted = checkpoint.Deleted.Concat(patch.Delete ?? []).ToList(),
            };
            Save(stableGuid, checkpoint);
            AfterCheckpoint?.Invoke(step.Id);
        }

        return checkpoint;
    }

    private ConsolidationCheckpoint Fail(Guid stableGuid, ConsolidationCheckpoint checkpoint, IConsolidationStep step, Exception ex,
        List<string> log)
    {
        var fails = checkpoint.FailedAttempts.GetValueOrDefault(step.Id) + 1;
        var attempts = new Dictionary<string, int>(checkpoint.FailedAttempts) { [step.Id] = fails };
        if (fails > _config.Consolidation.StepMaxRetries)
        {
            log.Add($"consolidation step '{step.Id}' failed {fails} times and is deferred to the next sleep: {ex.Message}");
            var deferred = checkpoint with
            {
                CompletedThrough = checkpoint.CompletedThrough + 1,
                FailedAttempts = attempts,
                Deferred = checkpoint.Deferred.Append(step.Id).ToList(),
            };
            Save(stableGuid, deferred);
            AfterCheckpoint?.Invoke(step.Id);
            return deferred;
        }

        log.Add($"consolidation step '{step.Id}' failed (attempt {fails}): {ex.Message}");
        var retry = checkpoint with { FailedAttempts = attempts };
        Save(stableGuid, retry);
        return retry;
    }

    private Mind Commit(Guid stableGuid, ConsolidationCheckpoint checkpoint, List<string> log)
    {
        var attempts = _config.Consolidation.CommitAttempts;
        for (var attempt = 1; ; attempt++)
        {
            var current = _store.Load(stableGuid) ?? throw new MindStoreException($"mind {stableGuid} does not exist");
            var final = checkpoint.Working with
            {
                Version = attempt == 1 ? checkpoint.Working.Version : current.Version,
                Sleep = checkpoint.Working.Sleep with
                {
                    PersonalDay = checkpoint.EndingDay + 1,
                    AwakeSeconds = 0,
                    DeferredSteps = checkpoint.Deferred.Count == 0 ? null : checkpoint.Deferred.ToList(),
                },
            };
            try
            {
                return _store.Commit(new MindCommit(final, checkpoint.Added, checkpoint.Deleted)
                {
                    RetagRecent = new MemoryRetag(checkpoint.LastRecentId, checkpoint.EndingDay + 1),
                    ClearCheckpoint = true,
                }).Mind;
            }
            catch (MindVersionConflictException ex) when (attempt < attempts)
            {
                log.Add($"consolidation commit found a newer mind (attempt {attempt}): {ex.Message}");
            }
        }
    }

    private static ConsolidationView View(ConsolidationCheckpoint checkpoint)
    {
        var deleted = checkpoint.Deleted.ToHashSet();
        var memories = checkpoint.Snapshot.Where(m => !deleted.Contains(m.Id)).Concat(checkpoint.Added).ToList();
        return new ConsolidationView(checkpoint.Working, memories, checkpoint.EndingDay);
    }

    private void Save(Guid stableGuid, ConsolidationCheckpoint checkpoint) =>
        _store.SaveCheckpoint(stableGuid, checkpoint.Working.Version, MindJson.Serialize(checkpoint));

    private static bool TryRead(string body, out ConsolidationCheckpoint checkpoint)
    {
        try
        {
            checkpoint = MindJson.Deserialize<ConsolidationCheckpoint>(body, "checkpoint");
            return true;
        }
        catch (MindFormatException)
        {
            checkpoint = null!;
            return false;
        }
    }
}
