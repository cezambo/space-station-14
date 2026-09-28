using System.Text.Json;
using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>Builds a cost-capped live Jev client, refusing to run without explicit opt-in (RDev-04).</summary>
internal sealed class LiveJev : IDisposable
{
    public CognitionConfig Config { get; }
    public LiveCostGuard Guard { get; }
    public IJevClient Client { get; }

    private readonly JevHttpClient _http;

    private LiveJev(CognitionConfig config, LiveCostGuard guard, JevHttpClient http)
    {
        Config = config;
        Guard = guard;
        _http = http;
        Client = new CostGuardedJevClient(http, guard, config.Providers.Jev, config.Context.CharsPerTokenInitial);
    }

    public void Dispose()
    {
        _http.Dispose();
    }

    /// <summary>Returns null and prints why when the live opt-in is missing or invalid.</summary>
    public static LiveJev? Create(CliOptions options, string? dumpDir, int? timeoutMsOverride = null)
    {
        var config = CognitionConfigLoader.LoadFile(options.ConfigPath);
        if (!options.Live || options.MaxCostUsd is null)
        {
            Console.Error.WriteLine("This command calls the real Jev API. Pass --live --max-cost-usd <N> to run it.");
            return null;
        }

        if (options.MaxCostUsd.Value <= 0 || options.MaxCostUsd.Value > config.LiveGuard.MaxCostUsdPerRun)
        {
            Console.Error.WriteLine(
                $"--max-cost-usd must be > 0 and <= live_guard.max_cost_usd_per_run ({config.LiveGuard.MaxCostUsdPerRun:0.00}).");
            return null;
        }

        var jev = config.Providers.Jev;
        if (timeoutMsOverride is { } t)
            jev = jev with { TimeoutMs = t };

        var apiKey = SecretResolver.Require(jev.ApiKeyEnv);
        var http = new JevHttpClient(jev, apiKey, options: new JevHttpClientOptions
        {
            CharsPerToken = config.Context.CharsPerTokenInitial,
            OnExchange = dumpDir is null ? null : e => Dump(dumpDir, e),
        });

        return new LiveJev(config with { Providers = config.Providers with { Jev = jev } },
            new LiveCostGuard(options.MaxCostUsd.Value), http);
    }

    private static int _dumpCounter;

    private static void Dump(string dir, JevExchange e)
    {
        Directory.CreateDirectory(dir);
        var n = Interlocked.Increment(ref _dumpCounter);
        var path = Path.Combine(dir, $"{n:0000}-{e.PurposeTag}-attempt{e.Attempt}.json");
        using var doc = e.ResponseBody is { Length: > 0 } body && body.TrimStart().StartsWith('{')
            ? JsonDocument.Parse(body)
            : null;
        var record = new Dictionary<string, object?>
        {
            ["purpose"] = e.PurposeTag,
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
