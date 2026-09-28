using System.Text.Json;
using Cognition.Core.Config;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>T1.07 check: render one library template with fixture values and send it live, cost-capped.</summary>
internal static class LlmTemplatePingCommand
{
    public static async Task<int> Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        var valuesPath = options.String("values", "fixtures/llm/daily_summary_values.json");
        var role = options.String("role", "light") == "heavy" ? LlmRole.Heavy : LlmRole.Light;

        var config = CognitionConfigLoader.LoadFile(options.ConfigPath);
        if (!LiveJev.CheckLiveOptIn(options, config, "LLM"))
            return 2;

        var library = PromptLibrary.Load(config.ResolvePath("prompts"), config.Thresholds);
        using var fixture = JsonDocument.Parse(File.ReadAllText(valuesPath));
        var root = fixture.RootElement;
        var template = library.Llm(root.GetProperty("template").GetString()!);
        var values = root.GetProperty("values").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!);
        var rendered = template.Render(values, role, root.GetProperty("max_output_tokens").GetInt32());
        foreach (var w in rendered.Warnings)
        {
            Console.WriteLine($"warning: {w}");
        }

        var provider = role == LlmRole.Light ? config.Providers.Light : config.Providers.Heavy;
        var apiKey = provider.ApiKeyEnv.Length == 0 ? null : SecretResolver.Require(provider.ApiKeyEnv);
        using var http = new OpenAiCompatClient(provider, apiKey,
            options: new OpenAiCompatClientOptions { RepairMessage = library.RepairMessage });
        var client = new CostGuardedLlmClient(http, new LiveCostGuard(options.MaxCostUsd!.Value), provider,
            config.Context.CharsPerTokenInitial, repairEnabled: true);

        try
        {
            var r = await client.CompleteAsync(rendered.Request, CancellationToken.None);
            Console.WriteLine($"{template.Name} via {r.ModelId}: {r.Latency.TotalMilliseconds:0} ms, repairs {r.SchemaRepairs}, "
                + $"{r.Usage.InputTokens} in / {r.Usage.OutputTokens} out ({r.ReasoningTokens} reasoning), ${r.Usage.CostUsd:0.000000}");
            using var reply = JsonDocument.Parse(r.Text);
            Console.WriteLine(JsonSerializer.Serialize(reply.RootElement, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (LlmException e)
        {
            Console.WriteLine($"FAILED {e.GetType().Name}: {e.Message}");
            return 1;
        }
    }
}
