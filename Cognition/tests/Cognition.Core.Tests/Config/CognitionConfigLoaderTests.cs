using Cognition.Core.Config;

namespace Cognition.Core.Tests.Config;

[TestFixture]
public sealed class CognitionConfigLoaderTests
{
    private static readonly Func<string, string?> NoEnv = _ => null;

    private static string RepoToml => File.ReadAllText(RepoPaths.ConfigFile);

    private static CognitionConfig Parse(string toml, Func<string, string?>? env = null)
    {
        return CognitionConfigLoader.Parse(toml, RepoPaths.CognitionRoot, env ?? NoEnv);
    }

    private static string Mutate(string from, string to)
    {
        var toml = RepoToml;
        Assert.That(toml, Does.Contain(from), "mutation anchor missing from cognition.toml");
        return toml.Replace(from, to, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ErrorsOf(string toml)
    {
        var ex = Assert.Throws<ConfigException>(() => Parse(toml));
        return ex!.Errors;
    }

    [Test]
    public void RepositoryConfigIsValid()
    {
        // RM-04
        var config = Parse(RepoToml);

        Assert.Multiple(() =>
        {
            Assert.That(config.Providers.Jev.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(config.Providers.Jev.ApiKeyEnv, Is.EqualTo("TYPESAFE_API_KEY"));
            Assert.That(config.Providers.Light.ApiKeyEnv, Is.EqualTo("OPENROUTER_API_KEY"));
            Assert.That(config.Providers.Heavy.ReasoningEffort, Is.EqualTo("max"));
            Assert.That(config.Replay.Mode, Is.EqualTo(ReplayMode.Replay));
            Assert.That(config.Decision.Fanout, Is.EqualTo(FanoutMode.Full));
            Assert.That(config.LiveGuard.MaxCostUsdPerRun, Is.EqualTo(2.00m));
            Assert.That(config.Emotion.Labels, Has.Count.EqualTo(9));
            Assert.That(config.Emotion.DurationMapDays["about a week"], Is.EqualTo(7));
            Assert.That(config.Opinion.StubbornnessByTag["stubborn"], Is.EqualTo(8));
            Assert.That(config.Opinion.SynergyIncrement, Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void TemporalRegexIsUnescapedOnce()
    {
        var config = Parse(RepoToml);

        Assert.That(config.Opinion.TemporalRegex, Does.StartWith(@"\b(yesterday|"));
    }

    [Test]
    public void IntegerThresholdsAreScoreLevelsNotProbabilities()
    {
        // RJ-18, RJ-19
        var config = Parse(RepoToml);

        Assert.Multiple(() =>
        {
            Assert.That(config.Thresholds.ScoreLevels["memory_importance_new_opinion"], Is.EqualTo(4));
            Assert.That(config.Thresholds.Probabilities, Does.Not.ContainKey("memory_importance_new_opinion"));
            Assert.That(config.Thresholds.Probabilities["goal_relevance"], Is.EqualTo(0.60));
        });
    }

    [Test]
    public void RelativePathsResolveAgainstConfigDirectory()
    {
        var config = Parse(RepoToml);

        Assert.That(config.ResolvePath(config.Replay.Dir),
            Is.EqualTo(Path.GetFullPath(Path.Combine(RepoPaths.CognitionRoot, "fixtures/replay"))));
    }

    [TestCase("live", ReplayMode.Live)]
    [TestCase("record", ReplayMode.Record)]
    [TestCase("replay", ReplayMode.Replay)]
    [TestCase("replay-strict", ReplayMode.ReplayStrict)]
    public void EnvVarOverridesReplayMode(string value, ReplayMode expected)
    {
        var config = Parse(RepoToml, name => name == CognitionConfigLoader.ReplayModeEnvVar ? value : null);

        Assert.That(config.Replay.Mode, Is.EqualTo(expected));
    }

    [Test]
    public void InvalidEnvOverrideIsRejected()
    {
        var ex = Assert.Throws<ConfigException>(() =>
            Parse(RepoToml, name => name == CognitionConfigLoader.ReplayModeEnvVar ? "sometimes" : null));

        Assert.That(ex!.Errors, Has.Some.Contains(CognitionConfigLoader.ReplayModeEnvVar));
    }

    [Test]
    public void SecretInFileIsRejectedWithoutEchoingIt()
    {
        // RM-05
        const string fakeSecret = "sk-test-0000000000000000";
        var errors = ErrorsOf(Mutate("api_key_env = \"TYPESAFE_API_KEY\"",
            $"api_key_env = \"TYPESAFE_API_KEY\"\napi_key = \"{fakeSecret}\""));

        Assert.Multiple(() =>
        {
            Assert.That(errors, Has.Some.Contains("providers.jev.api_key").And.Contains("RM-05"));
            Assert.That(string.Join("\n", errors), Does.Not.Contain(fakeSecret));
        });
    }

    [TestCase("jev-latest")]
    [TestCase("jev-preview")]
    [TestCase("")]
    public void JevModelMustBePinned(string model)
    {
        // RM-02
        var errors = ErrorsOf(Mutate("model = \"jev-1.13.0\"", $"model = \"{model}\""));

        Assert.That(errors, Has.Some.Contains("providers.jev.model"));
    }

    [Test]
    public void JevRpsAboveCeilingIsRejected()
    {
        // RC-01
        var errors = ErrorsOf(Mutate("max_rps = 10 #", "max_rps = 11 #"));

        Assert.That(errors, Has.Some.Contains("providers.jev.max_rps").And.Contains("RC-01"));
    }

    [Test]
    public void JevRetriesAboveThreeAreRejected()
    {
        // RM-07
        var errors = ErrorsOf(Mutate("max_retries = 3", "max_retries = 4"));

        Assert.That(errors, Has.Some.Contains("providers.jev.max_retries"));
    }

    [Test]
    public void ApiKeyEnvMustLookLikeAnEnvVarName()
    {
        // RM-05
        var errors = ErrorsOf(Mutate("api_key_env = \"TYPESAFE_API_KEY\"", "api_key_env = \"sk-abc123\""));

        Assert.That(errors, Has.Some.Contains("providers.jev.api_key_env"));
    }

    [Test]
    public void UnknownKeyIsReported()
    {
        var errors = ErrorsOf(Mutate("[speech]", "[speech]\nmin_intervall_s = 3.0"));

        Assert.That(errors, Has.Some.EqualTo("speech.min_intervall_s: unknown key"));
    }

    [Test]
    public void MissingKeyIsReported()
    {
        var errors = ErrorsOf(Mutate("max_sentences = 2", string.Empty));

        Assert.That(errors, Has.Some.EqualTo("speech.max_sentences: missing"));
    }

    [Test]
    public void WrongTypeIsReported()
    {
        var errors = ErrorsOf(Mutate("max_sentences = 2", "max_sentences = \"two\""));

        Assert.That(errors, Has.Some.StartsWith("speech.max_sentences: expected integer"));
    }

    [Test]
    public void InvalidEnumIsReported()
    {
        var errors = ErrorsOf(Mutate("fanout = \"full\"", "fanout = \"partial\""));

        Assert.That(errors, Has.Some.Contains("decision.fanout"));
    }

    [Test]
    public void DistanceBandsMustBeStrictlyIncreasing()
    {
        // RP-05
        var errors = ErrorsOf(Mutate("band_near = 5.0", "band_near = 20.0"));

        Assert.That(errors, Has.Some.Contains("perception.band_medium"));
    }

    [Test]
    public void ProbabilityThresholdOutOfRangeIsRejected()
    {
        // RJ-18
        var errors = ErrorsOf(Mutate("goal_relevance = 0.60", "goal_relevance = 1.60"));

        Assert.That(errors, Has.Some.EqualTo("thresholds.goal_relevance: must be in [0, 1]"));
    }

    [Test]
    public void InvalidTemporalRegexIsRejected()
    {
        var toml = RepoToml;
        var start = toml.IndexOf("temporal_regex = ", StringComparison.Ordinal);
        var end = toml.IndexOf('\n', start);
        var errors = ErrorsOf(toml[..start] + "temporal_regex = \"(unclosed\"" + toml[end..]);

        Assert.That(errors, Has.Some.StartsWith("opinion.temporal_regex: invalid regex"));
    }

    [Test]
    public void AllErrorsAreReportedTogether()
    {
        var toml = Mutate("max_sentences = 2", "max_sentences = \"two\"")
            .Replace("dir = \"logs\"", "dir = \"logs\"\nlevel = \"debug\"", StringComparison.Ordinal);

        var errors = ErrorsOf(toml);

        Assert.That(errors, Has.Count.EqualTo(2));
    }

    [Test]
    public void SyntaxErrorIsAConfigException()
    {
        Assert.Throws<ConfigException>(() => Parse("[providers.jev\nmodel = "));
    }

    [Test]
    public void MissingFileIsAConfigException()
    {
        var ex = Assert.Throws<ConfigException>(() => CognitionConfigLoader.LoadFile("does/not/exist.toml"));

        Assert.That(ex!.Message, Does.Contain("not found"));
    }
}
