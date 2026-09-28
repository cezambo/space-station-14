using Cognition.Core.Config;
using Cognition.Sandbox;

namespace Cognition.Scenario.Tests.Sandbox;

internal static class SandboxTestKit
{
    private static readonly Lazy<CognitionConfig> LazyConfig = new(() =>
    {
        var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Cognition.sln")))
        {
            dir = dir.Parent;
        }

        return CognitionConfigLoader.LoadFile(Path.Combine(dir!.FullName, "cognition.toml"), _ => null);
    });

    public static CognitionConfig Config => LazyConfig.Value;

    public static SandboxWorld World(string map, SandboxOptions? options = null) => SandboxWorld.FromMap(map, Config, options);

    public static Agent Person(this SandboxWorld w, string id, int x, int y) =>
        w.AddAgent(id, id, GuidFor(id), $"person called {id}", new Cell(x, y));

    public static Guid GuidFor(string id) => new(Math.Abs(StringComparer.Ordinal.GetHashCode(id) % 100000) + 1, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0,
        (byte)id.Length);

    public static string G(this Agent a) => SandboxWorld.Key(a.Guid);
}
