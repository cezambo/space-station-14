using Cognition.Core.Config;

namespace Cognition.Core.Tests.Foundation;

[TestFixture]
public sealed class FoundationTests
{
    [Test]
    public void CoreHasNoEngineOrGameReferences()
    {
        // RA-01
        var references = typeof(CognitionConfig).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

        Assert.That(references.Where(n => n.StartsWith("Robust", StringComparison.Ordinal)
                                          || n.StartsWith("Content", StringComparison.Ordinal)),
            Is.Empty);
    }

    [Test]
    public void CoreProjectOnlyReferencesProjectsInsideCognition()
    {
        // RA-01
        var csproj = File.ReadAllText(Path.Combine(RepoPaths.CognitionRoot, "src/Cognition.Core/Cognition.Core.csproj"));

        Assert.That(csproj, Does.Not.Contain("ProjectReference"));
    }

    [Test]
    public void TestRunsDefaultToReplayStrict()
    {
        // RDev-04
        Assert.That(Environment.GetEnvironmentVariable(CognitionConfigLoader.ReplayModeEnvVar), Is.EqualTo("replay-strict"));

        var config = CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile);

        Assert.That(config.Replay.Mode, Is.EqualTo(ReplayMode.ReplayStrict));
    }
}
