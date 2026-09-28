using System.Globalization;

namespace Cognition.Eval;

/// <summary>
/// Shared flags. Live calls require both <c>--live</c> and an explicit <c>--max-cost-usd</c>
/// (AGENTS.md rule 8, RDev-04).
/// </summary>
internal sealed record CliOptions(
    string ConfigPath,
    bool Live,
    decimal? MaxCostUsd,
    IReadOnlyDictionary<string, string> Extra)
{
    public const string DefaultConfigPath = "cognition.toml";

    public static CliOptions Parse(string[] args)
    {
        var config = DefaultConfigPath;
        var live = false;
        decimal? maxCost = null;
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config":
                    config = Value(args, ref i);
                    break;
                case "--live":
                    live = true;
                    break;
                case "--max-cost-usd":
                    maxCost = decimal.Parse(Value(args, ref i), CultureInfo.InvariantCulture);
                    break;
                default:
                    if (!args[i].StartsWith("--", StringComparison.Ordinal))
                        throw new ArgumentException($"unexpected argument '{args[i]}'");
                    extra[args[i][2..]] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                        ? args[++i]
                        : "true";
                    break;
            }
        }

        return new CliOptions(config, live, maxCost, extra);
    }

    public int Int(string name, int fallback)
    {
        return Extra.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : fallback;
    }

    public string String(string name, string fallback)
    {
        return Extra.GetValueOrDefault(name, fallback);
    }

    private static string Value(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"{args[i]} needs a value");
        return args[++i];
    }
}
