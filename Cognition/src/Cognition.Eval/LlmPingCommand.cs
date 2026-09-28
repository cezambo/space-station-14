using System.Text.Json;
using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>T1.04 acceptance: live plain and schema calls per LLM role, cost-capped (RDev-04).</summary>
internal static class LlmPingCommand
{
    public static async Task<int> Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        var fixturePath = options.String("fixture", "fixtures/llm/ping.json");
        var dumpDir = options.String("dump", "../docs/reports/raw/llm-ping");
        var roleArg = options.String("role", "light");

        var config = CognitionConfigLoader.LoadFile(options.ConfigPath);
        if (!LiveJev.CheckLiveOptIn(options, config, "LLM"))
            return 2;

        LlmRole[] roles = roleArg switch
        {
            "light" => [LlmRole.Light],
            "heavy" => [LlmRole.Heavy],
            "both" => [LlmRole.Light, LlmRole.Heavy],
            _ => [],
        };
        if (roles.Length == 0)
        {
            Console.Error.WriteLine("--role must be light, heavy or both");
            return 2;
        }

        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var repairTemplate = fixture.RootElement.GetProperty("repair_message").GetString()!;
        var guard = new LiveCostGuard(options.MaxCostUsd!.Value);
        var failures = 0;

        foreach (var role in roles)
        {
            var provider = role == LlmRole.Light ? config.Providers.Light : config.Providers.Heavy;
            var apiKey = provider.ApiKeyEnv.Length == 0 ? null : SecretResolver.Require(provider.ApiKeyEnv);
            using var http = new OpenAiCompatClient(provider, apiKey, options: new OpenAiCompatClientOptions
            {
                RepairMessage = errors => repairTemplate.Replace("{{errors}}", errors, StringComparison.Ordinal),
                OnExchange = e => Dump(dumpDir, role, e),
            });
            var client = new CostGuardedLlmClient(http, guard, provider, config.Context.CharsPerTokenInitial,
                repairEnabled: true);

            Console.WriteLine($"== {role}: {provider.Model} (reasoning_effort={provider.ReasoningEffort})");
            foreach (var call in fixture.RootElement.GetProperty("calls").EnumerateArray())
            {
                var request = new LlmRequest(
                    role,
                    call.GetProperty("system").GetString()!,
                    call.GetProperty("user").GetString()!,
                    call.TryGetProperty("schema", out var schema) ? schema.GetRawText() : null,
                    call.GetProperty("max_output_tokens").GetInt32(),
                    call.GetProperty("purpose").GetString()!);
                try
                {
                    var r = await client.CompleteAsync(request, CancellationToken.None);
                    Console.WriteLine($"  {request.PurposeTag}: {r.Latency.TotalMilliseconds:0} ms, attempts {r.Attempts}, "
                        + $"repairs {r.SchemaRepairs}, {r.Usage.InputTokens} in / {r.Usage.OutputTokens} out "
                        + $"({r.ReasoningTokens} reasoning), ${r.Usage.CostUsd:0.000000}"
                        + (r.CostReported ? string.Empty : " (computed)") + $", model {r.ModelId}, id {r.RequestId}");
                    Console.WriteLine($"    {r.Text.ReplaceLineEndings(" ")}");
                }
                catch (LlmException e)
                {
                    failures++;
                    Console.WriteLine($"  {request.PurposeTag}: FAILED {e.GetType().Name}: {e.Message}");
                }
            }
        }

        Console.WriteLine($"total spent: ${guard.SpentUsd:0.000000} of cap ${guard.CapUsd:0.00}");
        Console.WriteLine($"raw exchanges written to {Path.GetFullPath(dumpDir)}");
        return failures == 0 ? 0 : 1;
    }

    private static int _dumpCounter;

    private static void Dump(string dir, LlmRole role, LlmExchange e)
    {
        Directory.CreateDirectory(dir);
        var n = Interlocked.Increment(ref _dumpCounter);
        var path = Path.Combine(dir, $"{n:0000}-{role}-{e.PurposeTag}-r{e.Round}a{e.Attempt}.json".ToLowerInvariant());
        using var doc = e.ResponseBody is { Length: > 0 } body && body.TrimStart().StartsWith('{')
            ? JsonDocument.Parse(body)
            : null;
        var record = new Dictionary<string, object?>
        {
            ["purpose"] = e.PurposeTag,
            ["round"] = e.Round,
            ["attempt"] = e.Attempt,
            ["status"] = e.StatusCode,
            ["elapsed_ms"] = Math.Round(e.Elapsed.TotalMilliseconds, 1),
            ["error"] = e.Error,
            ["response_headers"] = e.ResponseHeaders,
            ["request"] = JsonDocument.Parse(e.RequestJson).RootElement,
            ["response"] = doc?.RootElement.Clone() ?? (object?)e.ResponseBody,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
    }
}
