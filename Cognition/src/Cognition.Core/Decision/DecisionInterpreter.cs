using Cognition.Core.Config;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;

namespace Cognition.Core.Decision;

/// <summary>
/// What a decision call means (T1.16). <see cref="Intent"/> is set for an action the menus offered.
/// Thinking and "keep going" have no intent. <see cref="Detail"/> is <see cref="LowConfidence"/>,
/// <see cref="UnknownOption"/> or <see cref="MissingAnswer"/> when nothing is done.
/// </summary>
public sealed record InterpretedDecision(
    ActionIntent? Intent,
    string? SpeakIntent,
    bool SpeakToEveryone,
    ThinkModes? Think,
    bool GoalBlocked,
    string? Emotion,
    string? Detail,
    string? Question)
{
    public const string LowConfidence = "low_confidence";
    public const string UnknownOption = "unknown_option";
    public const string MissingAnswer = "missing_answer";

    public bool KeepsCurrentAction => Intent is null && Think is null;

    public static InterpretedDecision Keep(bool goalBlocked = false, string? emotion = null) =>
        new(null, null, false, null, goalBlocked, emotion, null, null);

    public static InterpretedDecision Failed(string detail, string question, bool goalBlocked = false) =>
        new(null, null, false, null, goalBlocked, null, detail, question);
}

/// <summary>
/// Turns one decision call into a single offered action (T1.16, RJ-06, RJ-17, RJ-18). Only the sub-menu for
/// the chosen category is read. Confidence below that question's own threshold becomes "keep the current
/// action", logged as <c>low_confidence</c>. A key that was not offered never becomes an intent.
/// </summary>
public sealed class DecisionInterpreter
{
    private readonly Dictionary<string, double> _choiceThreshold = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _staticOptions = new(StringComparer.Ordinal);
    private readonly double _goalBlocked;

    public DecisionInterpreter(JevTemplate decision, ThresholdsConfig thresholds)
    {
        var errors = new List<string>();
        foreach (var question in decision.Questions)
        {
            if (question.Type != JevQuestionType.Choice)
                continue;
            if (question.ThresholdKey is null || !thresholds.Probabilities.TryGetValue(question.ThresholdKey, out var threshold))
                errors.Add($"decision.{question.Id}: choice questions need their own threshold_key (RJ-18)");
            else
                _choiceThreshold[question.Id] = threshold;
            if (question.Options is { } options)
                _staticOptions[question.Id] = options.ToDictionary(o => o.Key, o => o.Value, StringComparer.Ordinal);
        }

        if (!thresholds.Probabilities.TryGetValue("goal_blocked", out _goalBlocked))
            errors.Add("thresholds.goal_blocked: required");
        if (errors.Count > 0)
            throw new PromptLoadException(errors);
    }

    public InterpretedDecision Interpret(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers)
    {
        var goalBlocked = ReadGoalBlocked(menus, answers);
        if (menus.NothingToDecide)
            return InterpretedDecision.Keep(goalBlocked, ReadEmotion(menus, answers));

        var category = Read(DecisionQuestions.ActionCategory, menus, answers);
        if (category.Failure is { } categoryFailure)
            return InterpretedDecision.Failed(categoryFailure, DecisionQuestions.ActionCategory, goalBlocked);
        if (category.Option!.Category is not { } chosen)
            return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, DecisionQuestions.ActionCategory, goalBlocked);

        var emotion = ReadEmotion(menus, answers);
        var decision = chosen switch
        {
            ActionCategory.Nothing => InterpretedDecision.Keep(goalBlocked, emotion),
            ActionCategory.Move => Move(menus, answers, goalBlocked),
            ActionCategory.Speak => Speak(menus, answers, goalBlocked),
            ActionCategory.Think => Think(menus, answers, goalBlocked),
            ActionCategory.Sleep => Sleep(menus, answers, goalBlocked),
            _ => FromAction(DecisionQuestions.MenuOf(chosen), menus, answers, goalBlocked),
        };
        return decision with { Emotion = emotion };
    }

    private InterpretedDecision Move(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers, bool goalBlocked)
    {
        if (Available(DecisionQuestions.MoveTarget, menus))
        {
            var target = Read(DecisionQuestions.MoveTarget, menus, answers);
            if (target.Failure is { } failure)
                return InterpretedDecision.Failed(failure, DecisionQuestions.MoveTarget, goalBlocked);
            if (target.Option!.DestinationRef is { } destination)
                return new InterpretedDecision(new ActionIntent(ActionVerb.Move, DestinationRef: destination), null, false, null, goalBlocked, null, null, null);
        }

        if (!Available(DecisionQuestions.MoveDirection, menus))
            return InterpretedDecision.Failed(InterpretedDecision.MissingAnswer, DecisionQuestions.MoveDirection, goalBlocked);
        var direction = Read(DecisionQuestions.MoveDirection, menus, answers);
        if (direction.Failure is { } directionFailure)
            return InterpretedDecision.Failed(directionFailure, DecisionQuestions.MoveDirection, goalBlocked);
        if (direction.Option!.Direction is not { } compass)
            return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, DecisionQuestions.MoveDirection, goalBlocked);

        var extent = Read(DecisionQuestions.MoveExtent, menus, answers);
        if (extent.Failure is { } extentFailure)
            return InterpretedDecision.Failed(extentFailure, DecisionQuestions.MoveExtent, goalBlocked);
        if (!TryExtent(extent.Option!.Key, out var howFar))
            return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, DecisionQuestions.MoveExtent, goalBlocked);
        return new InterpretedDecision(new ActionIntent(ActionVerb.Move, Direction: compass, Extent: howFar), null, false, null, goalBlocked, null, null, null);
    }

    private InterpretedDecision Speak(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers, bool goalBlocked)
    {
        var target = Read(DecisionQuestions.SpeakTarget, menus, answers);
        if (target.Failure is { } targetFailure)
            return InterpretedDecision.Failed(targetFailure, DecisionQuestions.SpeakTarget, goalBlocked);
        var intent = Read(DecisionQuestions.SpeakIntent, menus, answers);
        if (intent.Failure is { } intentFailure)
            return InterpretedDecision.Failed(intentFailure, DecisionQuestions.SpeakIntent, goalBlocked);
        var option = target.Option!;
        var action = new ActionIntent(ActionVerb.Speak, option.Everyone ? null : option.ListenerRef, Volume: SpeechVolume.Normal);
        return new InterpretedDecision(action, intent.Option!.Key, option.Everyone, null, goalBlocked, null, null, null);
    }

    private InterpretedDecision Think(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers, bool goalBlocked)
    {
        var mode = Read(DecisionQuestions.ThinkMode, menus, answers);
        if (mode.Failure is { } failure)
            return InterpretedDecision.Failed(failure, DecisionQuestions.ThinkMode, goalBlocked);
        if (mode.Option!.Think == ThinkModes.None)
            return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, DecisionQuestions.ThinkMode, goalBlocked);
        return new InterpretedDecision(null, null, false, mode.Option.Think, goalBlocked, null, null, null);
    }

    private InterpretedDecision Sleep(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers, bool goalBlocked)
    {
        var where = Read(DecisionQuestions.SleepWhere, menus, answers);
        if (where.Failure is { } failure)
            return InterpretedDecision.Failed(failure, DecisionQuestions.SleepWhere, goalBlocked);
        var option = where.Option!;
        if (option.Action is { } sleep)
            return new InterpretedDecision(IntentOf(sleep), null, false, null, goalBlocked, null, null, null);
        if (option.DestinationRef is { } bed)
            return new InterpretedDecision(new ActionIntent(ActionVerb.Move, DestinationRef: bed), null, false, null, goalBlocked, null, null, null);
        return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, DecisionQuestions.SleepWhere, goalBlocked);
    }

    private InterpretedDecision FromAction(string menuId, DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers, bool goalBlocked)
    {
        var chosen = Read(menuId, menus, answers);
        if (chosen.Failure is { } failure)
            return InterpretedDecision.Failed(failure, menuId, goalBlocked);
        if (chosen.Option!.Action is not { } action)
            return InterpretedDecision.Failed(InterpretedDecision.UnknownOption, menuId, goalBlocked);
        return new InterpretedDecision(IntentOf(action), null, false, null, goalBlocked, null, null, null);
    }

    private static ActionIntent IntentOf(Affordance action) => new(action.Verb, action.TargetRef, action.ItemRef);

    private static bool TryExtent(string key, out MoveExtent extent) => key switch
    {
        "one_step" => Set(MoveExtent.OneStep, out extent),
        "short" => Set(MoveExtent.Short, out extent),
        "medium" => Set(MoveExtent.Medium, out extent),
        "until_obstacle" => Set(MoveExtent.UntilObstacle, out extent),
        _ => Set(default, out extent, false),
    };

    private static bool Set(MoveExtent value, out MoveExtent extent, bool ok = true)
    {
        extent = value;
        return ok;
    }

    private bool ReadGoalBlocked(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers)
    {
        if (!menus.Included.Contains(DecisionQuestions.GoalBlocked))
            return false;
        return answers.GetValueOrDefault(DecisionQuestions.GoalBlocked) is NoulAnswer noul && noul.PYes >= _goalBlocked;
    }

    private string? ReadEmotion(DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers)
    {
        if (!menus.Included.Contains(DecisionQuestions.Emotion))
            return null;
        if (answers.GetValueOrDefault(DecisionQuestions.Emotion) is not ChoiceAnswer choice)
            return null;
        if (choice.Confidence < _choiceThreshold[DecisionQuestions.Emotion])
            return null;
        var offered = Options(DecisionQuestions.Emotion, menus);
        return offered.ContainsKey(choice.Choice) ? choice.Choice : null;
    }

    private static bool Available(string id, DecisionMenus menus) =>
        menus.Included.Contains(id) || menus.Fixed.ContainsKey(id);

    private (MenuOption? Option, string? Failure) Read(string id, DecisionMenus menus, IReadOnlyDictionary<string, JevAnswer> answers)
    {
        if (menus.Fixed.TryGetValue(id, out var fixedOption))
            return (fixedOption, null);
        if (!menus.Included.Contains(id) && !_staticOptions.ContainsKey(id))
            return (null, InterpretedDecision.MissingAnswer);

        if (answers.GetValueOrDefault(id) is not ChoiceAnswer choice)
            return (null, InterpretedDecision.MissingAnswer);
        if (choice.Confidence < _choiceThreshold[id])
            return (null, InterpretedDecision.LowConfidence);
        var options = Options(id, menus);
        return options.TryGetValue(choice.Choice, out var option)
            ? (option, null)
            : (null, InterpretedDecision.UnknownOption);
    }

    private Dictionary<string, MenuOption> Options(string id, DecisionMenus menus)
    {
        if (menus.Asked.TryGetValue(id, out var asked))
            return asked.ToDictionary(o => o.Key, StringComparer.Ordinal);
        if (_staticOptions.TryGetValue(id, out var staticOptions))
            return staticOptions.ToDictionary(kv => kv.Key, kv => new MenuOption(kv.Key, kv.Value), StringComparer.Ordinal);
        return new Dictionary<string, MenuOption>(StringComparer.Ordinal);
    }
}
