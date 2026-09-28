namespace Cognition.Core.Config;

public sealed class ConfigException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigException(IReadOnlyList<string> errors)
        : base("Invalid cognition config:" + Environment.NewLine + string.Join(Environment.NewLine, errors.Select(e => "  - " + e)))
    {
        Errors = errors;
    }

    public ConfigException(string error) : this([error])
    {
    }
}
