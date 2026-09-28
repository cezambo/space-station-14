using Cognition.Core.Minds;

namespace Cognition.Core.Tests.Minds;

internal static class TestMinds
{
    public static Mind Ana(Guid? guid = null) => new(
        Id: "npc_07",
        StableGuid: guid ?? Guid.Parse("7b1c0000-0000-4000-8000-000000000007"),
        Ss14Profile: new Ss14Profile("Ana Souza", "Human", 34, "Chef", "Keeps a knife roll on her belt."),
        Personality: new Personality(
            new BigFive(0.6, 0.8, 0.4, 0.7, 0.3),
            ["stubborn", "protective"],
            8,
            [new Preference("cooking for others", Strength.Strong)],
            [new Preference("wasting food", Strength.Moderate)]),
        Goals: new Goals(
            [new Goal("g1", "Find something to eat", Horizon.Immediate, GoalStatus.Active, Priority.High, "holding food", 12)],
            [new Goal("g2", "Keep the kitchen stocked", Horizon.Medium, GoalStatus.Active, Priority.Medium, null, 3)],
            []),
        Opinions: new Opinions(
            [],
            [new Opinion("npc_03", OpinionKind.Social, "I trust Bob to look after the crew.", Valence.Positive, [],
                [new Impression("Bob refused to help while I was bleeding", 12)], 8, 9, 2, null)]),
        Emotion: new EmotionState(
            new Dictionary<string, double> { ["joy"] = 0.5, ["neutral"] = 0.5 },
            [new EmotionModifier("sadness", 0.3, "a friend left the station", 10, 7)],
            2),
        ThinkingBudget: new ThinkingBudget(20, 20),
        Sleep: new SleepState(12, 1430, 41),
        Control: new ControlState(ControlMode.Ai),
        Acquaintances: new Dictionary<string, Acquaintance> { ["npc_03"] = new("Bob", 2) },
        Version: 0);

    public static MemoryEntry Recent(string text, int importance = 3, int day = 12) =>
        new(0, MemoryLevel.Recent, MemorySource.Event, day, null, 100, text, null, importance);

    public static MemoryEntry Daily(string text, int day = 12) =>
        new(0, MemoryLevel.Daily, MemorySource.Summary, day, null, null, text, "A short day.", null);
}
