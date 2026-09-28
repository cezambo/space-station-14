using Cognition.Core.Config;

namespace Cognition.Eval;

internal static class CheckConfigCommand
{
    public static int Run(string[] args)
    {
        var options = CliOptions.Parse(args);
        try
        {
            var config = CognitionConfigLoader.LoadFile(options.ConfigPath);
            Console.WriteLine($"OK: {options.ConfigPath}");
            Console.WriteLine($"  replay mode: {config.Replay.Mode}");
            Console.WriteLine($"  jev model:   {config.Providers.Jev.Model}");
            Console.WriteLine($"  light model: {config.Providers.Light.Model} ({config.Providers.Light.ReasoningEffort})");
            Console.WriteLine($"  heavy model: {config.Providers.Heavy.Model} ({config.Providers.Heavy.ReasoningEffort})");
            return 0;
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }
}
