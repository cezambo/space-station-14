using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

internal static class JevTestData
{
    public static JevProviderConfig Config { get; } =
        CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile).Providers.Jev;

    public static JevRequest OneOfEach(string state = "Help! My payouts have been failing for 3 days.")
    {
        return new JevRequest(
            "jev-1.13.0",
            state,
            new Dictionary<string, JevQuestion>
            {
                ["department"] = new ChoiceQuestion("Which team should handle this?", new Dictionary<string, string>
                {
                    ["billing"] = "Payments, invoicing, refunds",
                    ["technical"] = "Bugs, outages, integrations",
                    ["sales"] = "Pricing, upgrades, new accounts",
                }),
                ["frustration"] = new ScoreQuestion("How frustrated is the customer?", ["Calm", "Frustrated", "Very angry"]),
                ["is_urgent"] = new NoulQuestion("Does this convey urgency?"),
            },
            "test");
    }

    /// <summary>Built from the documented example answers (docs.typesafe.ai/api).</summary>
    public const string OneOfEachResponse = """
        {
          "model": "jev-1.13.0",
          "answers": {
            "department": {
              "type": "choice",
              "choice": "billing",
              "probabilities": { "billing": 0.88, "technical": 0.12, "sales": 0.0 },
              "confidence": 0.81
            },
            "frustration": {
              "type": "score",
              "score": 1.05,
              "legend": { "0": "Calm", "1": "Frustrated", "2": "Very angry" },
              "probabilities": { "0": 0.0, "1": 0.95, "2": 0.05 },
              "confidence": 0.92
            },
            "is_urgent": { "type": "noul", "noul": 0.95 }
          },
          "usage": { "input_tokens": 1000000, "output_tokens": 20 }
        }
        """;
}
