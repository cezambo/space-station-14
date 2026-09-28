using Cognition.Core.Minds;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class MindValidatorTests
{
    [Test]
    public void SampleMindIsValid()
    {
        Assert.That(MindValidator.Validate(TestMinds.Ana()), Is.Empty);
    }

    private static IReadOnlyList<string> ErrorsOf(Func<Mind, Mind> change) => MindValidator.Validate(change(TestMinds.Ana()));

    [Test]
    public void TraitsOutsideZeroOne()
    {
        var errors = ErrorsOf(m => m with
        {
            Personality = m.Personality with { Traits = m.Personality.Traits with { Neuroticism = 1.2 } },
        });

        Assert.That(errors, Is.EqualTo(new[] { "personality.traits.neuroticism: must be in [0, 1] (RD-01)" }));
    }

    [Test]
    public void GoalInTheWrongHorizonList()
    {
        var errors = ErrorsOf(m => m with
        {
            Goals = m.Goals with { Long = [m.Goals.Medium[0] with { Id = "g3" }] },
        });

        Assert.That(errors, Is.EqualTo(new[] { "goals.long: goal 'g3' has horizon 'Medium'" }));
    }

    [Test]
    public void DuplicateGoalIds()
    {
        var errors = ErrorsOf(m => m with
        {
            Goals = m.Goals with { Medium = [m.Goals.Medium[0] with { Id = "g1" }] },
        });

        Assert.That(errors, Is.EqualTo(new[] { "goals: duplicate id 'g1'" }));
    }

    [Test]
    public void BudgetOverspend()
    {
        // RG-03
        var errors = ErrorsOf(m => m with { ThinkingBudget = new ThinkingBudget(20, -1) });

        Assert.That(errors, Has.Some.Contains("thinkingBudget.remaining"));
    }

    [Test]
    public void TwoOpinionsOnOneTarget()
    {
        var errors = ErrorsOf(m => m with
        {
            Opinions = m.Opinions with { Social = [m.Opinions.Social[0], m.Opinions.Social[0]] },
        });

        Assert.That(errors, Is.EqualTo(new[] { "opinions.social: more than one opinion on 'npc_03'" }));
    }

    [Test]
    public void EmptyGuid()
    {
        Assert.That(ErrorsOf(m => m with { StableGuid = Guid.Empty }), Is.EqualTo(new[] { "stableGuid: must not be empty (RD-04)" }));
    }

    [TestCase(0)]
    [TestCase(6)]
    [TestCase(null)]
    public void RecentMemoryNeedsImportanceOneToFive(int? importance)
    {
        var memory = TestMinds.Recent("x") with { Importance = importance };

        Assert.That(MindValidator.Validate(memory), Has.Some.Contains("importance"));
    }

    [Test]
    public void FortnightlyNeedsARange()
    {
        var memory = new MemoryEntry(0, MemoryLevel.Fortnightly, MemorySource.Summary, 10, 5, null, "x", null, null);

        Assert.That(MindValidator.Validate(memory), Has.Some.Contains("toDay"));
    }
}
