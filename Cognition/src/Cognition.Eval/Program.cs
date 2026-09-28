namespace Cognition.Eval;

public static class Program
{
    public static Task<int> Main(string[] args)
    {
        if (args.Length == 0)
            return Task.FromResult(Usage());

        return args[0] switch
        {
            "check-config" => Task.FromResult(CheckConfigCommand.Run(args[1..])),
            _ => Task.FromResult(Usage()),
        };
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Usage: dotnet run --project src/Cognition.Eval -- <command> [options]

            Commands:
              check-config [--config <path>]    Load and validate cognition.toml.
            """);
        return 2;
    }
}
