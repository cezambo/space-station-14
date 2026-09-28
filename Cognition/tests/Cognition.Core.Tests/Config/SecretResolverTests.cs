using Cognition.Core.Config;

namespace Cognition.Core.Tests.Config;

[TestFixture]
public sealed class SecretResolverTests
{
    [Test]
    public void ReturnsTrimmedValue()
    {
        var value = SecretResolver.Require("MY_KEY", _ => "  abc  ");

        Assert.That(value, Is.EqualTo("abc"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void MissingValueNamesTheVariable(string? value)
    {
        // RM-05
        var ex = Assert.Throws<ConfigException>(() => SecretResolver.Require("MY_KEY", _ => value));

        Assert.That(ex!.Message, Does.Contain("MY_KEY"));
    }
}
