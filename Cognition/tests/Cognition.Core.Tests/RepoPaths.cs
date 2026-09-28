namespace Cognition.Core.Tests;

internal static class RepoPaths
{
    /// <summary>The <c>Cognition/</c> directory, found by walking up to <c>Cognition.sln</c>.</summary>
    public static string CognitionRoot { get; } = FindRoot();

    public static string ConfigFile => Path.Combine(CognitionRoot, "cognition.toml");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cognition.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Cognition.sln not found above the test directory");
    }
}
