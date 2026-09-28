using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class JevRequestValidatorTests
{
    private static IReadOnlyList<string> Validate(JevRequest r) => JevRequestValidator.Validate(r, 4.0);

    private static JevRequest With(string id, JevQuestion q)
    {
        return JevTestData.OneOfEach() with { Questions = new Dictionary<string, JevQuestion> { [id] = q } };
    }

    private static Dictionary<string, string> Options(int n)
    {
        return Enumerable.Range(0, n).ToDictionary(i => $"o{i}", i => $"Option {i}");
    }

    [Test]
    public void ValidRequestPasses()
    {
        Assert.That(Validate(JevTestData.OneOfEach()), Is.Empty);
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(255, true)]
    [TestCase(256, false)]
    public void ChoiceOptionCountLimits(int options, bool valid)
    {
        // RM-07, RJ-04
        var errors = Validate(With("q", new ChoiceQuestion("Pick.", Options(options))));

        Assert.That(errors, valid ? Is.Empty : Has.Some.Contains("options"));
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(10, true)]
    [TestCase(11, false)]
    public void ScoreLevelCountLimits(int levels, bool valid)
    {
        var errors = Validate(With("q", new ScoreQuestion("Rate.", Enumerable.Range(0, levels).Select(i => $"Level {i}").ToList())));

        Assert.That(errors, valid ? Is.Empty : Has.Some.Contains("levels"));
    }

    [Test]
    public void EmptyInstructionsAreRejected()
    {
        var errors = Validate(With("q", new NoulQuestion("  ")));

        Assert.That(errors, Has.Some.EqualTo("questions.q.instructions: must not be empty"));
    }

    [Test]
    public void DuplicateIdsDifferingOnlyInCaseAreRejected()
    {
        var request = JevTestData.OneOfEach() with
        {
            Questions = new Dictionary<string, JevQuestion>
            {
                ["goal_blocked"] = new NoulQuestion("Blocked?"),
                ["Goal_Blocked"] = new NoulQuestion("Blocked?"),
            },
        };

        Assert.That(Validate(request), Has.Some.Contains("duplicate id"));
    }

    [Test]
    public void DuplicateOptionsDifferingOnlyInCaseAreRejected()
    {
        var errors = Validate(With("q", new ChoiceQuestion("Pick.",
            new Dictionary<string, string> { ["north"] = "Go north", ["North"] = "Go north" })));

        Assert.That(errors, Has.Some.Contains("duplicate option"));
    }

    [Test]
    public void NoulCriteriaMustComeInPairs()
    {
        var errors = Validate(With("q", new NoulQuestion("Urgent?", WhenTrue: "Yes, urgent")));

        Assert.That(errors, Has.Some.Contains("when_true and when_false"));
    }

    [Test]
    public void EmptyStateAndNoQuestionsAreRejected()
    {
        var request = JevTestData.OneOfEach() with { State = "", Questions = new Dictionary<string, JevQuestion>() };

        Assert.That(Validate(request), Has.Some.StartsWith("state:").And.Some.StartsWith("questions:"));
    }

    [Test]
    public void StateOverDocumentedContextBudgetIsRejected()
    {
        var request = JevTestData.OneOfEach(new string('x', 4 * 32_000));

        Assert.That(Validate(request), Has.Some.Contains("limit 32000"));
    }

    [Test]
    public void ThrowIfInvalidListsAllErrors()
    {
        var request = JevTestData.OneOfEach() with { Model = "", State = "" };

        var ex = Assert.Throws<JevValidationException>(() => JevRequestValidator.ThrowIfInvalid(request, 4.0));

        Assert.That(ex!.Errors, Has.Count.EqualTo(2));
    }
}
