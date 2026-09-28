using System.Text.Json;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class JsonSchemaLiteTests
{
    /// <summary>The impressions.md schema from the spec (§5), which uses most supported keywords.</summary>
    private const string Impressions = """
        {"type":"object","required":["impressions"],"properties":{"impressions":{"type":"array","maxItems":12,
        "items":{"type":"object","required":["target","kind","text","importance"],"properties":{
        "target":{"type":"string"},"kind":{"enum":["social","general"]},
        "text":{"type":"string","description":"one sentence, character's point of view"},
        "importance":{"type":"integer","minimum":1,"maximum":5}}}}}}
        """;

    private static IReadOnlyList<string> Validate(string schema, string instance)
    {
        using var doc = JsonDocument.Parse(instance);
        return JsonSchemaLite.Parse(schema).Validate(doc.RootElement);
    }

    [Test]
    public void ValidInstancePasses()
    {
        var errors = Validate(Impressions,
            """{"impressions":[{"target":"Bob","kind":"social","text":"Bob was kind.","importance":3}]}""");

        Assert.That(errors, Is.Empty);
    }

    [Test]
    public void ErrorsCarryJsonPaths()
    {
        var errors = Validate(Impressions,
            """{"impressions":[{"target":"Bob","kind":"romantic","text":"x","importance":7},{"kind":"general"}]}""");

        Assert.That(errors, Is.EquivalentTo(new[]
        {
            "$.impressions[0].kind: must be one of \"social\", \"general\"",
            "$.impressions[0].importance: must be <= 5",
            "$.impressions[1]: missing required property 'target'",
            "$.impressions[1]: missing required property 'text'",
            "$.impressions[1]: missing required property 'importance'",
        }));
    }

    [TestCase("3", true)]
    [TestCase("3.0", true)]
    [TestCase("3.5", false)]
    [TestCase("\"3\"", false)]
    public void IntegerAcceptsIntegralNumbersOnly(string value, bool valid)
    {
        var errors = Validate("""{"type":"integer"}""", value);

        Assert.That(errors, valid ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void MaxItemsIsEnforced()
    {
        var errors = Validate("""{"type":"array","maxItems":2}""", "[1,2,3]");

        Assert.That(errors, Is.EqualTo(new[] { "$: at most 2 item(s) allowed, got 3" }));
    }

    [Test]
    public void AdditionalPropertiesFalseRejectsExtras()
    {
        var errors = Validate("""{"type":"object","properties":{"a":{}},"additionalProperties":false}""",
            """{"a":1,"b":2}""");

        Assert.That(errors, Is.EqualTo(new[] { "$: property 'b' is not allowed" }));
    }

    [Test]
    public void ExtrasAreAllowedByDefault()
    {
        // JSON Schema default; the spec templates do not set additionalProperties.
        Assert.That(Validate("""{"type":"object","properties":{"a":{}}}""", """{"a":1,"b":2}"""), Is.Empty);
    }

    [Test]
    public void WrongTypeStopsDeeperChecks()
    {
        var errors = Validate(Impressions, """{"impressions":"none"}""");

        Assert.That(errors, Is.EqualTo(new[] { "$.impressions: expected array, got string" }));
    }

    [TestCase("""{"type":"object","oneOf":[]}""", "$.oneOf: keyword not supported")]
    [TestCase("""{"type":"strng"}""", "$.type: type must be one of")]
    [TestCase("""{"required":["a"],"properties":{}}""", "$.required: 'a' is not in properties")]
    [TestCase("""{"additionalProperties":{}}""", "$.additionalProperties: only a boolean is supported")]
    [TestCase("not json", "not valid JSON")]
    public void UnsupportedOrBrokenSchemaIsADefinitionError(string schema, string message)
    {
        var ex = Assert.Throws<JsonSchemaDefinitionException>(() => JsonSchemaLite.Parse(schema));

        Assert.That(ex!.Message, Does.Contain(message));
    }

    [Test]
    public void CanonicalJsonIsCompact()
    {
        var schema = JsonSchemaLite.Parse("{ \"type\" : \"object\",\n \"required\": [ ] }");

        Assert.That(schema.CanonicalJson, Is.EqualTo("""{"type":"object","required":[]}"""));
    }
}
