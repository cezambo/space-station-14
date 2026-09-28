using System.Text.Json.Nodes;
using Cognition.Core.Minds;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class MindJsonTests
{
    [Test]
    public void MindRoundTripsExactly()
    {
        var json = MindJson.Serialize(TestMinds.Ana());

        Assert.That(MindJson.Serialize(MindJson.Deserialize<Mind>(json, "t")), Is.EqualTo(json));
    }

    [Test]
    public void SnapshotUsesTheSpecLayout()
    {
        // §6: memory grouped by level, placed after goals; enums as lowercase strings.
        var snapshot = new MindSnapshot(TestMinds.Ana(), [TestMinds.Recent("I smelled smoke."), TestMinds.Daily("A long day.")]);

        var doc = JsonNode.Parse(MindJson.SerializeSnapshot(snapshot))!.AsObject();

        Assert.That(doc.Select(kv => kv.Key), Is.EqualTo(new[]
        {
            "id", "stableGuid", "ss14Profile", "personality", "goals", "memory", "opinions", "emotion", "thinkingBudget",
            "sleep", "control", "acquaintances", "version",
        }));
        Assert.That(doc["memory"]!.AsObject().Select(kv => kv.Key), Is.EqualTo(new[] { "recent", "daily", "fortnightly" }));
        Assert.That((string?)doc["goals"]!["immediate"]![0]!["status"], Is.EqualTo("active"));
        Assert.That((string?)doc["control"]!["mode"], Is.EqualTo("ai"));
        Assert.That((string?)doc["memory"]!["recent"]![0]!["source"], Is.EqualTo("event"));
    }

    [Test]
    public void SnapshotRoundTrips()
    {
        var snapshot = new MindSnapshot(TestMinds.Ana(), [TestMinds.Recent("I smelled smoke."), TestMinds.Daily("A long day.")]);
        var json = MindJson.SerializeSnapshot(snapshot);

        Assert.That(MindJson.SerializeSnapshot(MindJson.DeserializeSnapshot(json, "t")), Is.EqualTo(json));
    }

    [Test]
    public void UnknownFieldIsRejected()
    {
        var json = MindJson.Serialize(TestMinds.Ana()).Replace("\"version\":0", "\"version\":0,\"mood\":1",
            StringComparison.Ordinal);

        Assert.Throws<MindFormatException>(() => MindJson.Deserialize<Mind>(json, "t"));
    }

    [Test]
    public void MissingFieldIsRejected()
    {
        var node = JsonNode.Parse(MindJson.Serialize(TestMinds.Ana()))!.AsObject();
        node.Remove("sleep");

        Assert.Throws<MindFormatException>(() => MindJson.Deserialize<Mind>(node.ToJsonString(), "t"));
    }

    [Test]
    public void NullForNonNullableIsRejected()
    {
        var json = MindJson.Serialize(TestMinds.Ana()).Replace("\"job\":\"Chef\"", "\"job\":null", StringComparison.Ordinal);

        Assert.Throws<MindFormatException>(() => MindJson.Deserialize<Mind>(json, "t"));
    }

    [Test]
    public void EnumsMustBeNamedNotNumbered()
    {
        var json = MindJson.Serialize(TestMinds.Ana()).Replace("\"mode\":\"ai\"", "\"mode\":0", StringComparison.Ordinal);

        Assert.Throws<MindFormatException>(() => MindJson.Deserialize<Mind>(json, "t"));
    }

    [Test]
    public void SnapshotMemoryLevelMustMatchItsGroup()
    {
        var json = MindJson.SerializeSnapshot(new MindSnapshot(TestMinds.Ana(), [TestMinds.Recent("x")]));
        var doc = JsonNode.Parse(json)!.AsObject();
        var memory = doc["memory"]!.AsObject();
        var recent = memory["recent"]!.DeepClone();
        memory["recent"] = new JsonArray();
        memory["daily"] = recent;

        Assert.Throws<MindFormatException>(() => MindJson.DeserializeSnapshot(doc.ToJsonString(), "t"));
    }
}
