using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class JevWireFormatTests
{
    [Test]
    public void SerializesDocumentedShapeInFixedOrder()
    {
        // RM-07
        var json = JevWireFormat.SerializeRequest(JevTestData.OneOfEach());

        Assert.That(json, Is.EqualTo(
            "{\"model\":\"jev-1.13.0\",\"state\":\"Help! My payouts have been failing for 3 days.\",\"questions\":{"
            + "\"department\":{\"type\":\"choice\",\"instructions\":\"Which team should handle this?\",\"criteria\":{"
            + "\"billing\":\"Payments, invoicing, refunds\",\"technical\":\"Bugs, outages, integrations\","
            + "\"sales\":\"Pricing, upgrades, new accounts\"}},"
            + "\"frustration\":{\"type\":\"score\",\"instructions\":\"How frustrated is the customer?\","
            + "\"criteria\":[\"Calm\",\"Frustrated\",\"Very angry\"]},"
            + "\"is_urgent\":{\"type\":\"noul\",\"instructions\":\"Does this convey urgency?\"}}}"));
    }

    [Test]
    public void EmptyChoiceDescriptionIsSentAsNull()
    {
        var request = JevTestData.OneOfEach() with
        {
            Questions = new Dictionary<string, JevQuestion>
            {
                ["q"] = new ChoiceQuestion("Pick one.", new Dictionary<string, string> { ["a"] = "", ["b"] = "Option b" }),
            },
        };

        Assert.That(JevWireFormat.SerializeRequest(request), Does.Contain("\"criteria\":{\"a\":null,\"b\":\"Option b\"}"));
    }

    [Test]
    public void NoulCriteriaAreSentAsTrueAndFalse()
    {
        var request = JevTestData.OneOfEach() with
        {
            Questions = new Dictionary<string, JevQuestion>
            {
                ["q"] = new NoulQuestion("Urgent?", "Explicitly time-sensitive", "No urgency expressed"),
            },
        };

        Assert.That(JevWireFormat.SerializeRequest(request),
            Does.Contain("\"criteria\":{\"true\":\"Explicitly time-sensitive\",\"false\":\"No urgency expressed\"}"));
    }

    [Test]
    public void ParsesDocumentedAnswers()
    {
        var parsed = JevWireFormat.ParseResponse(JevTestData.OneOfEachResponse, JevTestData.OneOfEach());

        var choice = (ChoiceAnswer)parsed.Answers["department"];
        var score = (ScoreAnswer)parsed.Answers["frustration"];
        var noul = (NoulAnswer)parsed.Answers["is_urgent"];
        Assert.Multiple(() =>
        {
            Assert.That(parsed.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(parsed.InputTokens, Is.EqualTo(1_000_000));
            Assert.That(parsed.OutputTokens, Is.EqualTo(20));
            Assert.That(choice.Choice, Is.EqualTo("billing"));
            Assert.That(choice.Confidence, Is.EqualTo(0.81));
            Assert.That(choice.Probabilities["technical"], Is.EqualTo(0.12));
            Assert.That(score.Score, Is.EqualTo(1.05));
            Assert.That(score.Legend, Is.EqualTo("Frustrated"));
            Assert.That(score.Probabilities, Is.EqualTo(new[] { 0.0, 0.95, 0.05 }));
            Assert.That(noul.PYes, Is.EqualTo(0.95));
        });
    }

    [Test]
    public void MissingScoreLevelsDefaultToZeroProbability()
    {
        var body = JevTestData.OneOfEachResponse.Replace("\"0\": 0.0, \"1\": 0.95, \"2\": 0.05", "\"1\": 1.0",
            StringComparison.Ordinal);

        var score = (ScoreAnswer)JevWireFormat.ParseResponse(body, JevTestData.OneOfEach()).Answers["frustration"];

        Assert.That(score.Probabilities, Is.EqualTo(new[] { 0.0, 1.0, 0.0 }));
    }

    [TestCase("\"is_urgent\": { \"type\": \"noul\", \"noul\": 0.95 }", "", "no answer for question 'is_urgent'")]
    [TestCase("\"type\": \"noul\", \"noul\"", "\"type\": \"choice\", \"noul\"", "expected a noul answer")]
    [TestCase("\"choice\": \"billing\"", "\"choice\": \"legal\"", "not one of the options")]
    [TestCase("\"2\": 0.05", "\"7\": 0.05", "unknown level '7'")]
    [TestCase("\"usage\"", "\"usage_\"", "missing 'usage'")]
    public void RejectsResponsesThatDoNotMatchTheRequest(string from, string to, string expected)
    {
        var body = JevTestData.OneOfEachResponse.Replace(from, to, StringComparison.Ordinal);
        if (to.Length == 0)
            body = body.Replace("\"confidence\": 0.92\n    },", "\"confidence\": 0.92\n    }", StringComparison.Ordinal);

        var ex = Assert.Throws<JevProtocolException>(() => JevWireFormat.ParseResponse(body, JevTestData.OneOfEach()));

        Assert.That(ex!.Message, Does.Contain(expected));
    }

    [Test]
    public void RejectsNonJsonBody()
    {
        Assert.Throws<JevProtocolException>(() => JevWireFormat.ParseResponse("<html>oops</html>", JevTestData.OneOfEach()));
    }
}
