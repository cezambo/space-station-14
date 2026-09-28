using Cognition.Core.Config;

namespace Cognition.Scenario.Tests;

[TestFixture]
public sealed class ScenarioEnvironmentTests
{
    [Test]
    public void ScenarioRunsDefaultToReplayStrict()
    {
        // RDev-04
        Assert.That(Environment.GetEnvironmentVariable(CognitionConfigLoader.ReplayModeEnvVar), Is.EqualTo("replay-strict"));
    }
}
