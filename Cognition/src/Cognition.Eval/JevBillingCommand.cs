using System.Globalization;
using System.Text;
using System.Text.Json;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>
/// T1.03b billing gate (RC-05): the same ~3k-token state with 1, 5, 10 (and the full 13) questions,
/// plus the same question sets over a tiny state to isolate question tokens. Reports billed input
/// tokens per call and latency p50/p95.
/// </summary>
internal static class JevBillingCommand
{
    private sealed record Trial(string Kind, string Set, int Repeat);

    private sealed record Result(
        string Kind,
        string Set,
        int Repeat,
        int InputTokens,
        int OutputTokens,
        double LatencyMs,
        int Attempts,
        decimal CostUsd,
        string Model,
        string ActionCategory,
        string? Error);

    public static async Task<int> Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        var fixturePath = options.String("fixture", "fixtures/jev/billing/questions.json");
        var outDir = options.String("out", "../docs/reports/raw/T1.03b");
        var repeats = options.Int("repeats", 20);
        var tinyRepeats = options.Int("tiny-repeats", 3);
        var seed = options.Int("seed", 1303);
        var timeoutMs = options.Int("timeout-ms", 10_000);

        using var live = LiveJev.Create(options, Path.Combine(outDir, "exchanges"), timeoutMs);
        if (live is null)
            return 2;

        var fixture = JevFixture.Load(fixturePath);
        if (fixture.TinyState is null || fixture.Sets.Count == 0)
        {
            Console.Error.WriteLine("fixture needs 'tiny_state' and 'sets'");
            return 2;
        }

        var model = live.Config.Providers.Jev.Model;
        var trials = PlanTrials(fixture, repeats, tinyRepeats, seed);
        Console.WriteLine($"{trials.Count} calls planned, cap ${live.Guard.CapUsd:0.00}, per-attempt timeout {timeoutMs} ms");

        var results = new List<Result>();
        foreach (var trial in trials)
        {
            var request = fixture.Request(model, fixture.Sets[trial.Set], trial.Kind == "tiny" ? fixture.TinyState : null);
            try
            {
                var r = await live.Client.EvaluateAsync(request, CancellationToken.None);
                var action = r.Answers.TryGetValue("action_category", out var a) && a is ChoiceAnswer c ? c.Choice : "";
                results.Add(new Result(trial.Kind, trial.Set, trial.Repeat, r.Usage.InputTokens, r.Usage.OutputTokens,
                    r.Latency.TotalMilliseconds, r.Attempts, r.Usage.CostUsd, r.Model, action, null));
            }
            catch (CostCapExceededException e)
            {
                Console.Error.WriteLine(e.Message);
                break;
            }
            catch (JevException e)
            {
                results.Add(new Result(trial.Kind, trial.Set, trial.Repeat, 0, 0, 0, 0, 0, "", "", e.Message));
            }

            if (results.Count % 10 == 0)
                Console.WriteLine($"  {results.Count}/{trials.Count} done, ${live.Guard.SpentUsd:0.000000} spent");
        }

        Directory.CreateDirectory(outDir);
        WriteCsv(Path.Combine(outDir, "calls.csv"), results);
        var summary = Summarize(results, fixture, live.Config.Providers.Jev.TimeoutMs);
        File.WriteAllText(Path.Combine(outDir, "summary.json"),
            JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));

        PrintSummary(summary, live.Guard.SpentUsd);
        Console.WriteLine($"raw data written to {Path.GetFullPath(outDir)}");
        return results.Any(r => r.Error is not null) ? 1 : 0;
    }

    private static List<Trial> PlanTrials(JevFixture fixture, int repeats, int tinyRepeats, int seed)
    {
        var random = new Random(seed);
        var trials = new List<Trial>();
        for (var rep = 1; rep <= repeats; rep++)
        {
            var order = fixture.Sets.Keys.OrderBy(_ => random.Next()).ToList();
            trials.AddRange(order.Select(set => new Trial("full", set, rep)));
        }

        for (var rep = 1; rep <= tinyRepeats; rep++)
        {
            trials.AddRange(fixture.Sets.Keys.Select(set => new Trial("tiny", set, rep)));
        }

        return trials;
    }

    private static Dictionary<string, object> Summarize(List<Result> results, JevFixture fixture, int configTimeoutMs)
    {
        var groups = new List<Dictionary<string, object>>();
        foreach (var g in results.Where(r => r.Error is null).GroupBy(r => (r.Kind, r.Set)).OrderBy(g => g.Key.Kind)
                     .ThenBy(g => int.Parse(g.Key.Set, CultureInfo.InvariantCulture)))
        {
            var input = g.Select(r => (double)r.InputTokens).ToList();
            var latency = g.Select(r => r.LatencyMs).ToList();
            groups.Add(new Dictionary<string, object>
            {
                ["kind"] = g.Key.Kind,
                ["questions"] = int.Parse(g.Key.Set, CultureInfo.InvariantCulture),
                ["n"] = g.Count(),
                ["input_tokens_min"] = input.Min(),
                ["input_tokens_median"] = Percentile(input, 50),
                ["input_tokens_max"] = input.Max(),
                ["output_tokens_median"] = Percentile(g.Select(r => (double)r.OutputTokens).ToList(), 50),
                ["latency_ms_p50"] = Math.Round(Percentile(latency, 50), 1),
                ["latency_ms_p95"] = Math.Round(Percentile(latency, 95), 1),
                ["latency_ms_max"] = Math.Round(latency.Max(), 1),
                ["over_config_timeout"] = latency.Count(l => l > configTimeoutMs),
                ["retries"] = g.Sum(r => r.Attempts - 1),
                ["cost_usd_mean"] = Math.Round(g.Average(r => r.CostUsd), 8),
                ["models"] = g.Select(r => r.Model).Distinct().ToList(),
                ["action_category_counts"] = g.GroupBy(r => r.ActionCategory).ToDictionary(x => x.Key, x => x.Count()),
            });
        }

        return new Dictionary<string, object>
        {
            ["state_chars"] = fixture.State.Length,
            ["tiny_state_chars"] = fixture.TinyState!.Length,
            ["config_timeout_ms"] = configTimeoutMs,
            ["errors"] = results.Where(r => r.Error is not null).Select(r => $"{r.Kind}/{r.Set}#{r.Repeat}: {r.Error}").ToList(),
            ["total_calls"] = results.Count,
            ["total_input_tokens"] = results.Sum(r => r.InputTokens),
            ["total_cost_usd"] = results.Sum(r => r.CostUsd),
            ["groups"] = groups,
        };
    }

    private static void PrintSummary(Dictionary<string, object> summary, decimal spent)
    {
        Console.WriteLine();
        Console.WriteLine("kind  qs    n  in_tok(min/med/max)     out_med  lat_p50  lat_p95  >timeout  retries");
        foreach (var g in (List<Dictionary<string, object>>)summary["groups"])
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{g["kind"],-5} {g["questions"],3} {g["n"],4}  {g["input_tokens_min"],6}/{g["input_tokens_median"],6}/{g["input_tokens_max"],6}  {g["output_tokens_median"],8}  {g["latency_ms_p50"],7}  {g["latency_ms_p95"],7}  {g["over_config_timeout"],8}  {g["retries"],7}"));
        }

        var errors = (List<string>)summary["errors"];
        Console.WriteLine($"errors: {errors.Count}; spent ${spent:0.000000}; input tokens {summary["total_input_tokens"]}");
        foreach (var e in errors.Take(10))
        {
            Console.WriteLine("  " + e);
        }
    }

    /// <summary>Nearest-rank percentile.</summary>
    private static double Percentile(List<double> values, double p)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var rank = (int)Math.Ceiling(p / 100.0 * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }

    private static void WriteCsv(string path, List<Result> results)
    {
        var sb = new StringBuilder("kind,questions,repeat,input_tokens,output_tokens,latency_ms,attempts,cost_usd,model,action_category,error\n");
        foreach (var r in results)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"{r.Kind},{r.Set},{r.Repeat},{r.InputTokens},{r.OutputTokens},{r.LatencyMs:0.0},{r.Attempts},{r.CostUsd:0.00000000},{r.Model},{r.ActionCategory},\"{r.Error?.Replace("\"", "'", StringComparison.Ordinal)}\"\n");
        }

        File.WriteAllText(path, sb.ToString());
    }
}
