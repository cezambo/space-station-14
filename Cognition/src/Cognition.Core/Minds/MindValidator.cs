namespace Cognition.Core.Minds;

/// <summary>Structural invariants of a mind; limits that are tuning knobs are checked by their owners.</summary>
public static class MindValidator
{
    public static IReadOnlyList<string> Validate(Mind m)
    {
        var e = new List<string>();
        if (string.IsNullOrWhiteSpace(m.Id))
            e.Add("id: must not be empty");
        if (m.StableGuid == Guid.Empty)
            e.Add("stableGuid: must not be empty (RD-04)");
        if (string.IsNullOrWhiteSpace(m.Ss14Profile.Name))
            e.Add("ss14Profile.name: must not be empty");
        if (m.Ss14Profile.Age < 0)
            e.Add("ss14Profile.age: must be ≥ 0");

        var t = m.Personality.Traits;
        foreach (var (name, v) in new[]
                 {
                     ("openness", t.Openness), ("conscientiousness", t.Conscientiousness), ("extraversion", t.Extraversion),
                     ("agreeableness", t.Agreeableness), ("neuroticism", t.Neuroticism),
                 })
        {
            if (v is < 0 or > 1 || double.IsNaN(v))
                e.Add($"personality.traits.{name}: must be in [0, 1] (RD-01)");
        }

        if (m.Personality.BaseStubbornness < 1)
            e.Add("personality.baseStubbornness: must be ≥ 1");
        if (m.Personality.Tags.Distinct(StringComparer.Ordinal).Count() != m.Personality.Tags.Count)
            e.Add("personality.tags: duplicate tag");

        CheckGoals("immediate", m.Goals.Immediate, Horizon.Immediate, e);
        CheckGoals("medium", m.Goals.Medium, Horizon.Medium, e);
        CheckGoals("long", m.Goals.Long, Horizon.Long, e);
        foreach (var id in m.Goals.All.GroupBy(g => g.Id).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            e.Add($"goals: duplicate id '{id}'");
        }

        CheckOpinions("general", m.Opinions.General, OpinionKind.General, e);
        CheckOpinions("social", m.Opinions.Social, OpinionKind.Social, e);

        for (var i = 0; i < m.Emotion.Modifiers.Count; i++)
        {
            var mod = m.Emotion.Modifiers[i];
            if (mod.Intensity is <= 0 or > 1)
                e.Add($"emotion.modifiers[{i}].intensity: must be in (0, 1] (RE-08)");
            if (mod.DurationDays <= 0)
                e.Add($"emotion.modifiers[{i}].durationDays: must be > 0");
            if (string.IsNullOrWhiteSpace(mod.Reason))
                e.Add($"emotion.modifiers[{i}].reason: must not be empty");
        }

        if (m.Emotion.LastDistribution.Values.Any(p => p is < 0 or > 1))
            e.Add("emotion.lastDistribution: probabilities must be in [0, 1]");
        if (m.Emotion.DecisionsSinceLastCheck < 0)
            e.Add("emotion.decisionsSinceLastCheck: must be ≥ 0");

        if (m.ThinkingBudget.DailyUnits <= 0)
            e.Add("thinkingBudget.dailyUnits: must be > 0");
        if (m.ThinkingBudget.Remaining < 0 || m.ThinkingBudget.Remaining > m.ThinkingBudget.DailyUnits)
            e.Add("thinkingBudget.remaining: must be in [0, dailyUnits] (RG-03)");

        if (m.Sleep.PersonalDay < 0)
            e.Add("sleep.personalDay: must be ≥ 0");
        if (m.Sleep.AwakeSeconds < 0)
            e.Add("sleep.awakeSeconds: must be ≥ 0");
        if (m.Sleep.Fatigue is < 0 or > 100)
            e.Add("sleep.fatigue: must be in [0, 100]");

        foreach (var (id, a) in m.Acquaintances)
        {
            if (id == m.Id)
                e.Add($"acquaintances.{id}: a character cannot be its own acquaintance");
            if (string.IsNullOrWhiteSpace(a.KnownName))
                e.Add($"acquaintances.{id}.knownName: must not be empty");
        }

        if (m.Version < 0)
            e.Add("version: must be ≥ 0");
        return e;
    }

    public static IReadOnlyList<string> Validate(MemoryEntry m)
    {
        var e = new List<string>();
        var at = $"memory[{m.Id}]";
        if (string.IsNullOrWhiteSpace(m.Text))
            e.Add($"{at}.text: must not be empty");
        if (m.Day < 0)
            e.Add($"{at}.day: must be ≥ 0");
        switch (m.Level)
        {
            case MemoryLevel.Recent:
                if (m.Importance is not (>= 1 and <= 5))
                    e.Add($"{at}.importance: recent memories need importance 1-5");
                break;
            case MemoryLevel.Daily:
                if (string.IsNullOrWhiteSpace(m.ShortText))
                    e.Add($"{at}.shortText: daily memories need a one-sentence short version");
                break;
            case MemoryLevel.Fortnightly:
                if (m.ToDay is null || m.ToDay < m.Day)
                    e.Add($"{at}.toDay: fortnightly memories need toDay ≥ day");
                break;
        }

        return e;
    }

    private static void CheckGoals(string list, IReadOnlyList<Goal> goals, Horizon horizon, List<string> e)
    {
        foreach (var g in goals)
        {
            if (g.Horizon != horizon)
                e.Add($"goals.{list}: goal '{g.Id}' has horizon '{g.Horizon}'");
            if (string.IsNullOrWhiteSpace(g.Id) || string.IsNullOrWhiteSpace(g.Text))
                e.Add($"goals.{list}: goals need an id and text (RD-05)");
        }
    }

    private static void CheckOpinions(string list, IReadOnlyList<Opinion> opinions, OpinionKind kind, List<string> e)
    {
        foreach (var o in opinions)
        {
            if (o.Kind != kind)
                e.Add($"opinions.{list}: opinion on '{o.Target}' has kind '{o.Kind}'");
            if (string.IsNullOrWhiteSpace(o.NuanceDescription))
                e.Add($"opinions.{list}: opinion on '{o.Target}' needs a nuanceDescription");
            if (o.StubbornnessBase < 1 || o.Stubbornness < 0)
                e.Add($"opinions.{list}: opinion on '{o.Target}' has invalid stubbornness");
        }

        foreach (var target in opinions.GroupBy(o => o.Target).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            e.Add($"opinions.{list}: more than one opinion on '{target}'");
        }
    }
}
