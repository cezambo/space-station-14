namespace Cognition.Core.Providers;

/// <summary>
/// Local checks against documented API limits (docs.typesafe.ai/api, /models), so a bad request
/// fails before it costs anything (RM-07).
/// </summary>
public static class JevRequestValidator
{
    public const int MaxChoiceOptions = 255;
    public const int MinChoiceOptions = 2;
    public const int MinScoreLevels = 2;
    public const int MaxScoreLevels = 10;

    /// <summary>Documented budget for the state plus the single longest question.</summary>
    public const int MaxStatePlusLongestQuestionTokens = 32_000;

    /// <summary>Documented budget for the state plus all questions combined.</summary>
    public const int MaxRequestTokens = 64_000;

    public static IReadOnlyList<string> Validate(JevRequest request, double charsPerToken)
    {
        var e = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Model))
            e.Add("model: must not be empty");
        if (string.IsNullOrWhiteSpace(request.State))
            e.Add("state: must not be empty");
        if (request.Questions.Count == 0)
            e.Add("questions: at least one question is required");

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, question) in request.Questions)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                e.Add("questions: question id must not be empty");
                continue;
            }

            if (!seenIds.Add(id))
                e.Add($"questions.{id}: duplicate id (ids are compared case-insensitively)");

            ValidateQuestion(id, question, e);
        }

        if (charsPerToken > 0 && request.Questions.Count > 0)
        {
            var stateTokens = EstimateTokens(request.State.Length, charsPerToken);
            var questionTokens = request.Questions.Values.Select(q => EstimateTokens(CharCount(q), charsPerToken)).ToList();
            if (stateTokens + questionTokens.Max() > MaxStatePlusLongestQuestionTokens)
                e.Add($"state plus longest question is ~{stateTokens + questionTokens.Max()} tokens (limit {MaxStatePlusLongestQuestionTokens})");
            if (stateTokens + questionTokens.Sum() > MaxRequestTokens)
                e.Add($"request is ~{stateTokens + questionTokens.Sum()} tokens (limit {MaxRequestTokens})");
        }

        return e;
    }

    public static void ThrowIfInvalid(JevRequest request, double charsPerToken)
    {
        var errors = Validate(request, charsPerToken);
        if (errors.Count > 0)
            throw new JevValidationException(errors);
    }

    private static void ValidateQuestion(string id, JevQuestion question, List<string> e)
    {
        if (string.IsNullOrWhiteSpace(question.Instructions))
            e.Add($"questions.{id}.instructions: must not be empty");

        switch (question)
        {
            case ChoiceQuestion c:
                if (c.Criteria.Count is < MinChoiceOptions or > MaxChoiceOptions)
                    e.Add($"questions.{id}.criteria: a choice needs {MinChoiceOptions} to {MaxChoiceOptions} options, got {c.Criteria.Count}");
                var seenOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var option in c.Criteria.Keys)
                {
                    if (string.IsNullOrWhiteSpace(option))
                        e.Add($"questions.{id}.criteria: option key must not be empty");
                    else if (!seenOptions.Add(option))
                        e.Add($"questions.{id}.criteria.{option}: duplicate option (compared case-insensitively)");
                }

                break;
            case ScoreQuestion s:
                if (s.Levels.Count is < MinScoreLevels or > MaxScoreLevels)
                    e.Add($"questions.{id}.criteria: a score needs {MinScoreLevels} to {MaxScoreLevels} levels, got {s.Levels.Count}");
                for (var i = 0; i < s.Levels.Count; i++)
                {
                    if (string.IsNullOrWhiteSpace(s.Levels[i]))
                        e.Add($"questions.{id}.criteria[{i}]: level description must not be empty");
                }

                break;
            case NoulQuestion n:
                if (string.IsNullOrWhiteSpace(n.WhenTrue) != string.IsNullOrWhiteSpace(n.WhenFalse))
                    e.Add($"questions.{id}.criteria: give both when_true and when_false, or neither");
                break;
            default:
                e.Add($"questions.{id}: unsupported question type {question.GetType().Name}");
                break;
        }
    }

    internal static int CharCount(JevQuestion q)
    {
        return q.Instructions.Length + q switch
        {
            ChoiceQuestion c => c.Criteria.Sum(kv => kv.Key.Length + kv.Value.Length),
            ScoreQuestion s => s.Levels.Sum(l => l.Length),
            NoulQuestion n => (n.WhenTrue?.Length ?? 0) + (n.WhenFalse?.Length ?? 0),
            _ => 0,
        };
    }

    internal static int EstimateTokens(int chars, double charsPerToken)
    {
        return (int)Math.Ceiling(chars / charsPerToken);
    }
}
