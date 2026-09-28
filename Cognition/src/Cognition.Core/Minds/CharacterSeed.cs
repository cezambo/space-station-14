using Cognition.Core.Config;

namespace Cognition.Core.Minds;

/// <summary>
/// An authored character (<c>fixtures/characters/&lt;id&gt;.json</c>): only what a designer writes. Base
/// stubbornness is derived from tags (RD-01) and everything else starts at day-1 defaults.
/// </summary>
public sealed record CharacterSeed(
    string Id,
    Guid StableGuid,
    Ss14Profile Ss14Profile,
    SeedPersonality Personality,
    SeedGoals Goals);

public sealed record SeedPersonality(
    BigFive Traits,
    IReadOnlyList<string> Tags,
    IReadOnlyList<Preference> Likes,
    IReadOnlyList<Preference> Dislikes);

public sealed record SeedGoal(string Id, string Text, Priority Priority, string? SuccessCheck);

public sealed record SeedGoals(IReadOnlyList<SeedGoal> Medium, IReadOnlyList<SeedGoal> Long);

public static class Stubbornness
{
    /// <summary>
    /// RD-01: the value of the one tag listed in <c>opinion.stubbornness_by_tag</c>, else the default.
    /// More than one such tag (e.g. <c>stubborn</c> and <c>fickle</c>) is contradictory and rejected (Q-5).
    /// </summary>
    public static int Base(IReadOnlyList<string> tags, OpinionConfig opinion)
    {
        var matched = tags.Where(opinion.StubbornnessByTag.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        return matched.Count switch
        {
            0 => opinion.StubbornnessDefault,
            1 => opinion.StubbornnessByTag[matched[0]],
            _ => throw new MindFormatException(
                $"tags {string.Join(", ", matched.Select(t => $"'{t}'"))} each set base stubbornness; use only one"),
        };
    }
}

public static class CharacterSeeds
{
    public const int FirstDay = 1;

    public static Mind ToMind(CharacterSeed seed, CognitionConfig config)
    {
        var p = seed.Personality;
        return new Mind(
            Id: seed.Id,
            StableGuid: seed.StableGuid,
            Ss14Profile: seed.Ss14Profile,
            Personality: new Personality(p.Traits, p.Tags, Stubbornness.Base(p.Tags, config.Opinion), p.Likes, p.Dislikes),
            Goals: new Goals([], Goals(seed.Goals.Medium, Horizon.Medium), Goals(seed.Goals.Long, Horizon.Long)),
            Opinions: Opinions.None,
            Emotion: new EmotionState(new Dictionary<string, double> { ["neutral"] = 1.0 }, [], 0),
            ThinkingBudget: new ThinkingBudget(config.Budget.DailyUnits, config.Budget.DailyUnits),
            Sleep: new SleepState(FirstDay, 0, 0),
            Control: new ControlState(ControlMode.Ai),
            Acquaintances: new Dictionary<string, Acquaintance>(),
            Version: 0);
    }

    private static List<Goal> Goals(IReadOnlyList<SeedGoal> goals, Horizon horizon) =>
        goals.Select(g => new Goal(g.Id, g.Text, horizon, GoalStatus.Active, g.Priority, g.SuccessCheck, FirstDay)).ToList();

    /// <summary>Loads every <c>*.json</c> seed and builds its mind; all problems are reported together.</summary>
    public static IReadOnlyList<Mind> LoadDirectory(string dir, CognitionConfig config)
    {
        var errors = new List<string>();
        var minds = new List<Mind>();
        foreach (var file in Directory.GetFiles(dir, "*.json").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            try
            {
                var seed = MindJson.Deserialize<CharacterSeed>(File.ReadAllText(file), name);
                if (seed.Id + ".json" != name)
                    errors.Add($"{name}: id '{seed.Id}' must match the file name");
                var mind = ToMind(seed, config);
                errors.AddRange(MindValidator.Validate(mind).Select(e => $"{name}: {e}"));
                minds.Add(mind);
            }
            catch (MindFormatException ex)
            {
                errors.Add(ex.Message.StartsWith(name, StringComparison.Ordinal) ? ex.Message : $"{name}: {ex.Message}");
            }
        }

        foreach (var dup in minds.GroupBy(m => m.StableGuid).Where(g => g.Count() > 1))
        {
            errors.Add($"stableGuid {dup.Key} is used by {string.Join(", ", dup.Select(m => m.Id).Order(StringComparer.Ordinal))}");
        }

        if (errors.Count > 0)
            throw new MindFormatException("character seeds are invalid:\n  " + string.Join("\n  ", errors));
        return minds;
    }
}
