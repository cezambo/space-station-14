using System.Text.RegularExpressions;
using Cognition.Core.Decision;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Sandbox;
using static Cognition.Scenario.Tests.Sandbox.SandboxTestKit;

namespace Cognition.Scenario.Tests.Sandbox;

/// <summary>T1.15 against the sandbox: every option is a real affordance and describes itself (RJ-05, RJ-16, RJ-20).</summary>
[TestFixture]
public sealed partial class DecisionMenuPropertyTests
{
    [Test]
    public void MenusOverFiftyLayoutsOfferOnlyPossibleSelfDescribedActions()
    {
        var prompts = PromptLibrary.Load(Path.Combine(CognitionRoot, "prompts"), Config.Thresholds);
        var builder = new DecisionCallBuilder(prompts, Config);
        var formatter = new PerceptionFormatter(Config, prompts.Vocabulary);
        var noOne = new PerceptionContext(new Dictionary<string, string>(), []);
        var options = 0;
        var actions = 0;

        Assert.Multiple(() =>
        {
            for (var seed = 1; seed <= 50; seed++)
            {
                var w = LayoutGenerator.World(seed, Config, agents: 8, items: 80);
                w.RunFor(2);
                foreach (var a in w.Agents)
                {
                    var p = w.GetPerception(a.G());
                    var affordances = w.GetAffordances(a.G());
                    var input = new MenuInput(affordances, formatter.Labels(p, noOne), p.Self, NeedBand.Strong,
                        ThinkModes.Light | ThinkModes.Deep, true, 0);

                    var m = builder.Menus(input);

                    Assert.That(m.Warnings, Is.Empty, $"seed {seed} {a.Ref}");
                    foreach (var (id, menu) in m.Asked.Concat(m.Fixed.Select(f => KeyValuePair.Create(f.Key, (IReadOnlyList<MenuOption>)[f.Value]))))
                    {
                        Assert.That(menu.Select(o => o.Key), Is.Unique, $"seed {seed} {a.Ref} {id}");
                        foreach (var o in menu)
                        {
                            options++;
                            Assert.That(KeyPattern().IsMatch(o.Key), Is.True, o.Key);
                            Assert.That(o.Criteria, Does.Not.Contain("{{").And.Not.Contain(a.Ref), o.Key);
                            if (o.Action is { } act)
                            {
                                actions++;
                                Assert.That(affordances.Actions, Does.Contain(act), $"seed {seed} {a.Ref} {o.Key}");
                            }

                            if (o.DestinationRef is { } dest)
                                Assert.That(affordances.Destinations.Select(x => x.Ref), Does.Contain(dest), o.Key);
                            if (o.Direction is { } dir)
                                Assert.That(affordances.OpenDirections, Does.Contain(dir), o.Key);
                        }
                    }
                }
            }
        });

        TestContext.Out.WriteLine($"{options} options, {actions} backed by an action");
        Assert.That(options, Is.GreaterThan(2000));
        Assert.That(actions, Is.GreaterThan(100));
    }

    [GeneratedRegex("^[a-z0-9_]{1,40}$")]
    private static partial Regex KeyPattern();
}
