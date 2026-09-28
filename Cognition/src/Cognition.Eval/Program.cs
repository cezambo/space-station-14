namespace Cognition.Eval;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            return Usage();

        return args[0] switch
        {
            "check-config" => CheckConfigCommand.Run(args[1..]),
            "jev-ping" => await JevPingCommand.Run(args[1..]),
            "jev-billing" => await JevBillingCommand.Run(args[1..]),
            "llm-ping" => await LlmPingCommand.Run(args[1..]),
            _ => Usage(),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Usage: dotnet run --project src/Cognition.Eval -- <command> [options]

            Commands:
              check-config [--config <path>]    Load and validate cognition.toml.
              jev-ping --live --max-cost-usd <N> [--fixture <path>] [--dump <dir>]
                                                One live Jev call (T1.03).
              jev-billing --live --max-cost-usd <N> [--repeats 20] [--tiny-repeats 3]
                          [--timeout-ms 10000] [--out <dir>]
                                                Billing gate measurement (T1.03b).
              llm-ping --live --max-cost-usd <N> [--role light|heavy|both]
                       [--fixture <path>] [--dump <dir>]
                                                Live plain + JSON schema calls per LLM role (T1.04).

            Live commands refuse to run without --live and a cap no higher than
            live_guard.max_cost_usd_per_run.
            """);
        return 2;
    }
}
