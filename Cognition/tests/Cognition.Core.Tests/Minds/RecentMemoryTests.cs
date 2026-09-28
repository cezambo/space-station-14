using Cognition.Core.Config;
using Cognition.Core.Minds;
using Cognition.Core.Perception;
using Cognition.Core.Prompts;
using Cognition.Core.Providers;
using Cognition.Core.Scheduling;

namespace Cognition.Core.Tests.Minds;

[TestFixture]
public sealed class RecentMemoryTests
{
    private static readonly Lazy<(CognitionConfig Config, PromptLibrary Prompts)> Loaded = new(() =>
    {
        var config = CognitionConfigLoader.LoadFile(RepoPaths.ConfigFile, _ => null);
        var prompts = PromptLibrary.Load(Path.Combine(RepoPaths.CognitionRoot, "prompts"), config.Thresholds);
        return (config, prompts);
    });

    private static readonly MemoryFilterContext Ana = new("Ana", "chef", "warm", "fetch the toolbox", "repair the oven");

    private static ObservedEvent Open(double at, string target, bool flagged = false) =>
        new(at, WorldEventKind.ActionCompleted, ActionVerb.Open, target, Flagged: flagged);

    [Test]
    public async Task RepeatsInsideTheWindowBecomeOneSentenceWithoutANumber()
    {
        var jev = new ScriptJev();
        var events = new[] { Open(0, "the door"), Open(9, "the door"), Open(19, "the door"), Open(29.1, "the door") };
        var result = await Service(jev).RecordAsync(0, 2, Ana, [], events, []);

        Assert.That(jev.Calls, Is.EqualTo(2));
        Assert.That(jev.Sentences[0], Does.Contain("a couple of times").And.Contain("opened the door"));
        Assert.That(jev.Sentences[0], Does.Not.Match(@"\d"));
        Assert.That(jev.Sentences[1], Is.EqualTo("I opened the door."));
        Assert.That(result.Memories, Is.Empty);
    }

    [Test]
    public async Task OwnSpeechAndThoughtsSkipTheFilter()
    {
        var jev = new ScriptJev();
        var result = await Service(jev).RecordAsync(0, 1, Ana, [],
            [new ObservedEvent(1, WorldEventKind.Spoke, Text: "Watch \"out\"")],
            [new OwnThought(2, "The door is locked.")]);

        Assert.That(jev.Calls, Is.Zero);
        Assert.That(result.Memories.Select(m => m.Text), Is.EqualTo(new[]
        {
            "I said: \"Watch 'out'\".",
            "I thought: The door is locked.",
        }));
        Assert.That(result.Memories.Select(m => m.Source), Is.EqualTo(new[] { MemorySource.OwnSpeech, MemorySource.Thought }));
        Assert.That(result.Memories.Select(m => m.Importance), Is.All.EqualTo(RecentMemory.ProtectedImportance));
        Assert.That(result.Memories.All(m => MindValidator.Validate(m).Count == 0), Is.True);
    }

    [Test]
    public async Task ADayOfEventsStaysInRangeAndKeepsFlaggedOnes()
    {
        var jev = new ScriptJev(keepWhen: text => text.Contains("notable", StringComparison.Ordinal));
        var walks = Enumerable.Range(0, 80).Select(i => new ObservedEvent(i * 2, WorldEventKind.ActionCompleted, ActionVerb.Move, "the corridor"));
        var flagged = Enumerable.Range(0, 40).Select(i => Open(1_000 + (i * 11), $"the notable locker {Word(i)}", flagged: true));
        var thoughts = Enumerable.Range(0, 10).Select(i => new OwnThought(3_000 + i, $"I should try plan {Word(i)}."));
        var result = await Service(jev).RecordAsync(0, 4, Ana, [], walks.Concat(flagged).ToList(), thoughts.ToList());

        Assert.That(result.Memories, Has.Count.InRange(30, 150));
        Assert.That(result.FlaggedEvents, Is.EqualTo(40));
        Assert.That(result.FlaggedKept, Is.EqualTo(40));
        Assert.That((double)result.FlaggedKept / result.FlaggedEvents, Is.GreaterThanOrEqualTo(0.95));
        Assert.That(result.Memories.Any(m => m.Text.Contains("the corridor", StringComparison.Ordinal)), Is.False);
        Assert.That(result.Memories.Count(m => m.Source == MemorySource.Thought), Is.EqualTo(10));
    }

    [Test]
    public async Task NineteenOfTwentyFlaggedEventsStay()
    {
        var jev = new ScriptJev(keepWhen: text => text.Contains("notable", StringComparison.Ordinal));
        var events = Enumerable.Range(0, 19).Select(i => Open(i * 11, $"the notable hatch {Word(i)}", flagged: true))
            .Append(Open(1_000, "the bin", flagged: true)).ToList();
        var result = await Service(jev).RecordAsync(0, 1, Ana, [], events, []);

        Assert.That(result.FlaggedKept, Is.EqualTo(19));
        Assert.That((double)result.FlaggedKept / result.FlaggedEvents, Is.EqualTo(0.95).Within(1e-9));
    }

    [Test]
    public async Task TheCapDropsTheLeastImportantAndSparesSpeechUntilLast()
    {
        var config = Loaded.Value.Config with { Memory = Loaded.Value.Config.Memory with { RecentHardCap = 2 } };
        var jev = new ScriptJev(keepWhen: _ => true, importance: text => text.Contains("critical", StringComparison.Ordinal) ? 4 : 0);
        var events = new[]
        {
            Open(0, "the critical notable hatch"),
            Open(30, "the notable bin"),
        };
        var result = await Service(jev, config: config).RecordAsync(0, 1, Ana, [], events,
            [new OwnThought(2, "Hold on.")]);

        Assert.That(result.Memories.Select(m => m.Source), Is.EqualTo(new[] { MemorySource.Event, MemorySource.Thought }));
        Assert.That(result.Memories[0].Text, Does.Contain("critical"));

        var tighter = Loaded.Value.Config with { Memory = Loaded.Value.Config.Memory with { RecentHardCap = 1 } };
        var one = await Service(jev, config: tighter).RecordAsync(0, 1, Ana, [],
            [new ObservedEvent(1, WorldEventKind.Spoke, Text: "Hello.")],
            [new OwnThought(5, "Later.")]);
        Assert.That(one.Memories.Single().Source, Is.EqualTo(MemorySource.Thought));
    }

    [Test]
    public async Task FilterCallsStayInsideTheJevWindow()
    {
        var jev = new ScriptJev(keepWhen: _ => true);
        var scheduler = new Scheduler(Loaded.Value.Config);
        var events = Enumerable.Range(0, 15).Select(i => Open(i * 11, $"the notable hatch {Word(i)}")).ToList();
        await Service(jev, scheduler).RecordAsync(0, 1, Ana, [], events, []);

        var rate = (int)Math.Floor(Loaded.Value.Config.Providers.Jev.MaxRps + 1e-9);
        Assert.That(jev.Calls, Is.EqualTo(15));
        Assert.That(scheduler.Now, Is.EqualTo(15 / rate));
    }

    [Test]
    public void ImportanceIsTheMostProbableLevelAndATieGoesHigher()
    {
        Assert.That(RecentMemory.ImportanceOf(new ScoreAnswer(2.4, "mid", 0.4, [0.1, 0.1, 0.1, 0.6, 0.1])), Is.EqualTo(4));
        Assert.That(RecentMemory.ImportanceOf(new ScoreAnswer(0.5, "tie", 0.5, [0.5, 0.5, 0, 0, 0])), Is.EqualTo(2));
    }

    private static RecentMemory Service(ScriptJev jev, Scheduler? scheduler = null, CognitionConfig? config = null) =>
        new(config ?? Loaded.Value.Config, Loaded.Value.Prompts, jev, scheduler ?? new Scheduler(Loaded.Value.Config));

    private static string Word(int n) => n switch
    {
        0 => "zero",
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        _ => "n" + string.Join("", n.ToString().Select(c => (char)('a' + (c - '0')))),
    };

    private sealed class ScriptJev(Func<string, bool>? keepWhen = null, Func<string, int>? importance = null) : IJevClient
    {
        public int Calls { get; private set; }
        public List<string> Sentences { get; } = [];

        public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
        {
            Calls++;
            var sentence = request.State.Split("EVENT:", 2)[^1].Trim();
            Sentences.Add(sentence);
            var keep = keepWhen?.Invoke(sentence) ?? false;
            var level = Math.Clamp(importance?.Invoke(sentence) ?? 2, 0, 4);
            var probabilities = new double[5];
            probabilities[level] = 1;
            var answers = new Dictionary<string, JevAnswer>();
            foreach (var (id, question) in request.Questions)
            {
                answers[id] = question switch
                {
                    NoulQuestion => new NoulAnswer(keep ? 0.9 : 0.1),
                    ScoreQuestion => new ScoreAnswer(level, "level", 1, probabilities),
                    _ => throw new InvalidOperationException(id),
                };
            }

            return Task.FromResult(new JevResponse("r", TimeSpan.Zero, answers, new UsageInfo(1, 1, 0)));
        }
    }
}
