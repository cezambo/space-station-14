using Cognition.Core.Perception;
using Cognition.Core.Prompts;

namespace Cognition.Core.Tests.Prompts;

[TestFixture]
public sealed class VocabularyTests
{
    private static string PromptsDir => Path.Combine(RepoPaths.CognitionRoot, "prompts");

    private static string RepoText => File.ReadAllText(Path.Combine(PromptsDir, Vocabulary.FileName));

    private static List<string> ErrorsOf(string yaml)
    {
        var errors = new List<string>();
        Vocabulary.Parse(yaml, errors);
        return errors;
    }

    [Test]
    public void RepositoryVocabularyCoversEveryCategory()
    {
        var v = Vocabulary.Load(PromptsDir);

        Assert.That(Enum.GetValues<DistanceBand>().Select(v.Word), Has.All.Not.Empty);
        Assert.That(Enum.GetValues<Compass>().Select(v.Word), Has.All.Not.Empty);
        Assert.That(Enum.GetValues<NeedBand>().Select(v.Word), Has.All.Not.Empty);
        Assert.That(Enum.GetValues<DayPhase>().Select(v.Word), Has.All.Not.Empty);
        Assert.That(Enum.GetValues<BudgetBand>().Select(v.Word), Has.All.Not.Empty);
        Assert.That(v.Word(Compass.NorthEast), Is.EqualTo("northeast"));
        Assert.That(v.Word(DistanceBand.WithinReach), Is.EqualTo("within reach"));
    }

    [Test]
    public void SensationWords()
    {
        var v = Vocabulary.Load(PromptsDir);

        Assert.That(v.Word(new Sensation(SensationKind.ThinAir)), Is.EqualTo("thin air"));
        Assert.That(v.Word(new Sensation(SensationKind.Gas, "plasma")), Is.EqualTo("smell of plasma"));
        Assert.That(v.Word(new Sensation(SensationKind.Puddle, "blood")), Is.EqualTo("a puddle of blood"));
        Assert.That(v.NoticeableGases, Does.Contain("plasma").And.Not.Contain("oxygen"));
        Assert.Throws<KeyNotFoundException>(() => v.Word(new Sensation(SensationKind.Gas, "oxygen")));
    }

    [Test]
    public void PromptLibraryExposesTheVocabulary()
    {
        Assert.That(PromptLibrary.Load(PromptsDir).Vocabulary.Word(NeedBand.Critical), Is.EqualTo("critical"));
    }

    [Test]
    public void MissingWordIsALoadError()
    {
        var errors = ErrorsOf(RepoText.Replace("  far: \"far\"\n", "", StringComparison.Ordinal));

        Assert.That(errors, Is.EqualTo(new[] { "vocabulary.yaml: distance.far is missing" }));
    }

    [Test]
    public void UnknownWordAndSectionAreLoadErrors()
    {
        var errors = ErrorsOf(RepoText.Replace("  far: \"far\"\n", "  far: \"far\"\n  very_far: \"x\"\n", StringComparison.Ordinal)
            + "colour:\n  red: \"red\"\n");

        Assert.That(errors, Is.EquivalentTo(new[]
        {
            "vocabulary.yaml: distance.very_far is not a DistanceBand value",
            "vocabulary.yaml: unknown section 'colour'",
        }));
    }

    [Test]
    public void EmptyWordIsALoadError()
    {
        var errors = ErrorsOf(RepoText.Replace("  near: \"near\"", "  near: \"\"", StringComparison.Ordinal));

        Assert.That(errors, Is.EqualTo(new[] { "vocabulary.yaml: distance.near must be a non-empty string" }));
    }

    [Test]
    public void PhraseMustUseItsPlaceholders()
    {
        var errors = ErrorsOf(RepoText.Replace("holding: \"holding {{items}}\"", "holding: \"holding {{item}}\"",
            StringComparison.Ordinal));

        Assert.That(errors, Is.EqualTo(new[] { "vocabulary.yaml: perception.holding must use exactly {{items}}" }));
    }

    [Test]
    public void PhrasesFillPlaceholdersLiterally()
    {
        var v = Vocabulary.Load(PromptsDir);

        Assert.That(v.Phrase("known", ("name", "{{x}} Bob")), Is.EqualTo("{{x}} Bob (known)"));
        Assert.That(v.Word(SpeechVolume.Shout), Is.EqualTo("shouting"));
        Assert.That(v.NeedName("oxygen"), Is.EqualTo("breathing"));
    }

    [TestCase("WithinReach", "within_reach")]
    [TestCase("NorthEast", "north_east")]
    [TestCase("Ok", "ok")]
    public void SnakeCase(string pascal, string expected)
    {
        Assert.That(Vocabulary.SnakeCase(pascal), Is.EqualTo(expected));
    }
}
