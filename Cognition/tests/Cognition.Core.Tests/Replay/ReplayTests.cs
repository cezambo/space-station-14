using Cognition.Core.Config;
using Cognition.Core.Providers;
using Cognition.Core.Replay;
using Cognition.Core.Tests.Providers;

namespace Cognition.Core.Tests.Replay;

[TestFixture]
public sealed class ReplayTests
{
    private static readonly LlmProviderConfig Llm = new(
        "https://openrouter.ai/api/v1", "z-ai/glm-5.3-flash", "low", "OPENROUTER_API_KEY", 1000, 5, 2, 500, 5000, 0.25,
        StructuredOutputMode.JsonSchema, true, 1000, 0.15m, 0.50m);

    private string _dir = null!;
    private FileReplayStore _store = null!;
    private FakeJev _jev = null!;
    private FakeLlm _llm = null!;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cognition-replay-" + Guid.NewGuid().ToString("N"));
        _store = new FileReplayStore(_dir);
        _jev = new FakeJev();
        _llm = new FakeLlm();
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class FakeJev : IJevClient
    {
        public int Calls;
        public bool Fail;

        public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
        {
            Calls++;
            if (Fail)
                throw new JevTimeoutException(TimeSpan.FromSeconds(1), 1);
            var answers = new Dictionary<string, JevAnswer>
            {
                ["urgency"] = new ChoiceAnswer("high", 0.8125, new Dictionary<string, double> { ["high"] = 0.8125, ["low"] = 0.1875 }),
                ["sentiment"] = new ScoreAnswer(1.37, "Neutral", 0.61, [0.1, 0.43, 0.47]),
                ["refund"] = new NoulAnswer(0.123456789),
            };
            return Task.FromResult(new JevResponse($"req_{Calls}", TimeSpan.FromMilliseconds(287.4567), answers,
                new UsageInfo(2420, 3, 0.00010164m))
            { Model = request.Model });
        }
    }

    private sealed class FakeLlm : ILlmClient
    {
        public int Calls;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LlmResponse($"Reply {Calls} \"quoted\" é", new UsageInfo(77, 86, 0.00007611m),
                "z-ai/glm-5.3-flash")
            {
                RequestId = $"gen-{Calls}",
                Latency = TimeSpan.FromMilliseconds(4505.1),
                ReasoningTokens = 50,
                CostReported = true,
            });
        }
    }

    private static JevRequest Jev(string state = "Maya is tired.") =>
        JevTestData.OneOfEach(state) with { PurposeTag = "decision", TemplateHash = "tpl-decision-v1" };

    private static LlmRequest Speech(string user = "Say hi.") =>
        new(LlmRole.Light, "sys", user, null, 80, "speech") { TemplateHash = "tpl-speech-v1" };

    private ReplayJevClient JevClient(ReplayMode mode) => new(_jev, _store, mode);

    private ReplayLlmClient LlmClient(ReplayMode mode, LlmProviderConfig? config = null) =>
        new(_llm, config ?? Llm, _store, mode);

    private string[] RecordedFiles() =>
        Directory.Exists(_dir) ? Directory.GetFiles(_dir, "*.json", SearchOption.AllDirectories) : [];

    [Test]
    public async Task RecordWritesHumanReadableFileUnderPurpose()
    {
        // RA-03
        await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);

        var file = RecordedFiles().Single();
        var text = await File.ReadAllTextAsync(file);
        Assert.Multiple(() =>
        {
            Assert.That(Path.GetFileName(Path.GetDirectoryName(file)), Is.EqualTo("decision"));
            Assert.That(Path.GetFileNameWithoutExtension(file), Has.Length.EqualTo(32));
            Assert.That(text, Does.Contain("\"state\": \"Maya is tired.\""), "request is readable");
            Assert.That(text, Does.Contain("\"choice\": \"high\""), "response is readable");
            Assert.That(text, Does.Contain("\"template_hash\": \"tpl-decision-v1\""));
            Assert.That(text, Does.Not.Contain("\r"));
        });
    }

    [Test]
    public async Task ReplayStrictServesTheRecordingWithoutCalling()
    {
        var recorded = await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);

        var replayed = await JevClient(ReplayMode.ReplayStrict).EvaluateAsync(Jev(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_jev.Calls, Is.EqualTo(1));
            Assert.That(replayed.Replayed, Is.True);
            Assert.That(replayed.RequestId, Is.EqualTo(recorded.RequestId));
            Assert.That(replayed.Usage, Is.EqualTo(recorded.Usage));
            Assert.That(replayed.Answers["urgency"], Is.TypeOf<ChoiceAnswer>());
            Assert.That(((ChoiceAnswer)replayed.Answers["urgency"]).Probabilities["low"], Is.EqualTo(0.1875));
            Assert.That(((ScoreAnswer)replayed.Answers["sentiment"]).Probabilities, Is.EqualTo(new[] { 0.1, 0.43, 0.47 }));
            Assert.That(((NoulAnswer)replayed.Answers["refund"]).PYes, Is.EqualTo(0.123456789), "doubles round-trip exactly");
        });
    }

    [Test]
    public void ReplayStrictMissThrowsAndNeverCalls()
    {
        var ex = Assert.ThrowsAsync<ReplayMissException>(() =>
            JevClient(ReplayMode.ReplayStrict).EvaluateAsync(Jev(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Purpose, Is.EqualTo("decision"));
            Assert.That(_jev.Calls, Is.Zero);
            Assert.That(RecordedFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task ReplayRecordsOnMissThenServesTheRecording()
    {
        var client = JevClient(ReplayMode.Replay);

        var first = await client.EvaluateAsync(Jev(), CancellationToken.None);
        var second = await client.EvaluateAsync(Jev(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(_jev.Calls, Is.EqualTo(1));
            Assert.That(first.Replayed, Is.False);
            Assert.That(second.Replayed, Is.True);
        });
    }

    [Test]
    public async Task LiveNeverReadsOrWrites()
    {
        await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);
        File.Delete(RecordedFiles().Single());

        var live = await JevClient(ReplayMode.Live).EvaluateAsync(Jev(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(live.Replayed, Is.False);
            Assert.That(_jev.Calls, Is.EqualTo(2));
            Assert.That(RecordedFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task TemplateChangeInvalidatesTheRecording()
    {
        // Risk table: replay must not mask prompt changes.
        await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);

        Assert.ThrowsAsync<ReplayMissException>(() => JevClient(ReplayMode.ReplayStrict)
            .EvaluateAsync(Jev() with { TemplateHash = "tpl-decision-v2" }, CancellationToken.None));
    }

    [Test]
    public async Task ModelChangeInvalidatesTheRecording()
    {
        await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);

        Assert.ThrowsAsync<ReplayMissException>(() => JevClient(ReplayMode.ReplayStrict)
            .EvaluateAsync(Jev() with { Model = "jev-1.14.0" }, CancellationToken.None));
    }

    [Test]
    public async Task LlmReasoningEffortChangeInvalidatesTheRecording()
    {
        await LlmClient(ReplayMode.Record).CompleteAsync(Speech(), CancellationToken.None);

        Assert.ThrowsAsync<ReplayMissException>(() => LlmClient(ReplayMode.ReplayStrict, Llm with { ReasoningEffort = "high" })
            .CompleteAsync(Speech(), CancellationToken.None));
    }

    [Test]
    public async Task LlmRecordingRoundTrips()
    {
        var recorded = await LlmClient(ReplayMode.Record).CompleteAsync(Speech(), CancellationToken.None);

        var replayed = await LlmClient(ReplayMode.ReplayStrict).CompleteAsync(Speech(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(replayed.Text, Is.EqualTo(recorded.Text));
            Assert.That(replayed.Usage, Is.EqualTo(recorded.Usage));
            Assert.That(replayed.ReasoningTokens, Is.EqualTo(50));
            Assert.That(replayed.CostReported, Is.True);
            Assert.That(replayed.Replayed, Is.True);
            Assert.That(File.ReadAllText(RecordedFiles().Single()), Does.Contain("\"kind\": \"llm\""));
        });
    }

    [Test]
    public async Task FailedCallsAreNotRecorded()
    {
        _jev.Fail = true;

        Assert.ThrowsAsync<JevTimeoutException>(() => JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None));

        Assert.That(RecordedFiles(), Is.Empty);
        await Task.CompletedTask;
    }

    [Test]
    public async Task ReRecordingAnUnchangedCallWritesIdenticalBytes()
    {
        await JevClient(ReplayMode.Record).EvaluateAsync(Jev(), CancellationToken.None);
        var file = RecordedFiles().Single();
        var before = await File.ReadAllBytesAsync(file);

        var replayed = await JevClient(ReplayMode.ReplayStrict).EvaluateAsync(Jev(), CancellationToken.None);
        _store.Save(new ReplayRecord(
            ReplayKey.Compute(JevWireFormat.SerializeRequest(Jev()), "tpl-decision-v1", Jev().Model),
            "jev", "decision", Jev().Model, "tpl-decision-v1", JevWireFormat.SerializeRequest(Jev()),
            ReplayCodec.WriteJev(replayed)));

        Assert.That(await File.ReadAllBytesAsync(file), Is.EqualTo(before));
    }

    [Test]
    public async Task ScenarioRunTwiceInReplayStrictGivesByteIdenticalLogsAtZeroCost()
    {
        // T1.05 acceptance: record once, then two replay-strict runs produce the same log and make no calls.
        async Task<string> Run(ReplayMode mode)
        {
            var jev = JevClient(mode);
            var llm = LlmClient(mode);
            var log = new List<string>();
            foreach (var state in new[] { "Maya is tired.", "Maya hears a fire alarm.", "Maya is tired." })
            {
                log.Add(ReplayCodec.WriteJev(await jev.EvaluateAsync(Jev(state), CancellationToken.None)));
                log.Add(ReplayCodec.WriteLlm(await llm.CompleteAsync(Speech(state), CancellationToken.None)));
            }

            return string.Join("\n", log);
        }

        await Run(ReplayMode.Record);
        var callsAfterRecording = _jev.Calls + _llm.Calls;

        var first = await Run(ReplayMode.ReplayStrict);
        var second = await Run(ReplayMode.ReplayStrict);

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(_jev.Calls + _llm.Calls, Is.EqualTo(callsAfterRecording), "no calls while replaying");
            Assert.That(RecordedFiles(), Has.Length.EqualTo(4), "repeated requests share one recording");
        });
    }
}
