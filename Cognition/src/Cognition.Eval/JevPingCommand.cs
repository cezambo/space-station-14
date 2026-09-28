using System.Globalization;
using Cognition.Core.Providers;

namespace Cognition.Eval;

/// <summary>T1.03 acceptance: one manual live call, one question of each type.</summary>
internal static class JevPingCommand
{
    public static async Task<int> Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        var fixturePath = options.String("fixture", "fixtures/jev/ping.json");
        var dumpDir = options.String("dump", "../docs/reports/raw/jev-ping");

        using var live = LiveJev.Create(options, dumpDir);
        if (live is null)
            return 2;

        var fixture = JevFixture.Load(fixturePath);
        var request = fixture.Request(live.Config.Providers.Jev.Model);
        var response = await live.Client.EvaluateAsync(request, CancellationToken.None);

        Console.WriteLine($"model:      {response.Model}");
        Console.WriteLine($"request id: {response.RequestId}");
        Console.WriteLine($"latency:    {response.Latency.TotalMilliseconds:0} ms (attempts: {response.Attempts})");
        Console.WriteLine($"usage:      {response.Usage.InputTokens} in / {response.Usage.OutputTokens} out, ${response.Usage.CostUsd:0.000000}");
        foreach (var (id, answer) in response.Answers)
        {
            Console.WriteLine($"  {id}: {Describe(answer)}");
        }

        Console.WriteLine($"raw exchange written to {Path.GetFullPath(dumpDir)}");
        return 0;
    }

    internal static string Describe(JevAnswer answer)
    {
        return answer switch
        {
            ChoiceAnswer c => $"choice={c.Choice} confidence={F(c.Confidence)} p={{{string.Join(", ", c.Probabilities.Select(p => $"{p.Key}:{F(p.Value)}"))}}}",
            ScoreAnswer s => $"score={F(s.Score)} legend=\"{s.Legend}\" confidence={F(s.Confidence)} p=[{string.Join(", ", s.Probabilities.Select(F))}]",
            NoulAnswer n => $"noul={F(n.PYes)}",
            _ => answer.ToString(),
        };
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
