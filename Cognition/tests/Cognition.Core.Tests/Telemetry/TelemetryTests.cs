using Cognition.Core.Config;
using Cognition.Core.Providers;
using Cognition.Core.Replay;
using Cognition.Core.Telemetry;
using Cognition.Core.Tests.Providers;

namespace Cognition.Core.Tests.Telemetry;

[TestFixture]
public sealed class TelemetryTests
{
    private string _dir = null!;
    private MemorySink _sink = null!;
    private double _now;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cognition-telemetry-" + Guid.NewGuid().ToString("N"));
        _sink = new MemorySink();
        _now = 12.5;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private sealed class MemorySink : ITelemetrySink
    {
        public List<CallRecord> Calls { get; } = new();
        public List<DecisionRecord> Decisions { get; } = new();

        public void Write(CallRecord record) => Calls.Add(record);

        public void Write(DecisionRecord record) => Decisions.Add(record);
    }

    private sealed class FixedJev(bool replayed = false) : IJevClient
    {
        public Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken ct)
        {
            var answers = new Dictionary<string, JevAnswer>
            {
                ["action"] = new ChoiceAnswer("help", 0.81234, new Dictionary<string, double> { ["help"] = 0.81234 }),
                ["fatigue"] = new ScoreAnswer(2.2, "Clearly tired", 0.6, [0.1, 0.1, 0.6, 0.2]),
                ["danger"] = new NoulAnswer(0.97),
            };
            return Task.FromResult(new JevResponse("req_1", TimeSpan.FromMilliseconds(287.44), answers,
                new UsageInfo(2420, 3, 0.00010164m))
            { Model = "jev-1.13.0", Replayed = replayed });
        }
    }

    private sealed class FailingLlm : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
            throw new LlmSchemaException(["$: missing required property 'short'"], "{}")
            {
                Usage = new UsageInfo(100, 20, 0.0002m),
            };
    }

    private static LlmRequest Speech() => new(LlmRole.Light, "sys", "hi", null, 80, "speech");

    [Test]
    public async Task CallLineHasFixedKeysAndAnswerSummary()
    {
        // RNF-06
        var client = new TelemetryJevClient(new FixedJev(), _sink, () => _now);
        using (TelemetryContext.ForCharacter("maya"))
        {
            await client.EvaluateAsync(JevTestData.OneOfEach() with { PurposeTag = "decision" }, CancellationToken.None);
        }

        const string expected = """
            {"t":12.5,"kind":"call","character":"maya","role":"jev","purpose":"decision","model":"jev-1.13.0","request_id":"req_1","replayed":false,"input_tokens":2420,"output_tokens":3,"reasoning_tokens":0,"usd":0.00010164,"usd_recorded":0.00010164,"latency_ms":287.4,"attempts":1,"schema_repairs":0,"answers":{"action":{"choice":"help","confidence":0.8123},"fatigue":{"score":2.2,"legend":"Clearly tired","confidence":0.6},"danger":{"p_yes":0.97}},"error":null}
            """;
        Assert.That(TelemetryJson.Line(_sink.Calls.Single()), Is.EqualTo(expected));
    }

    [Test]
    public async Task ReplayedCallsSpendNothing()
    {
        // RDev-04: replay costs zero; the recorded cost stays visible.
        var client = new TelemetryJevClient(new FixedJev(replayed: true), _sink, () => _now);

        await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);

        var r = _sink.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Replayed, Is.True);
            Assert.That(r.UsdSpent, Is.Zero);
            Assert.That(r.UsdRecorded, Is.EqualTo(0.00010164m));
        });
    }

    [Test]
    public async Task CharacterContextFlowsAcrossAwaitsAndRestores()
    {
        var client = new TelemetryJevClient(new FixedJev(), _sink, () => _now);

        using (TelemetryContext.ForCharacter("maya"))
        {
            await Task.Yield();
            using (TelemetryContext.ForCharacter("bob"))
            {
                await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);
            }

            await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);
        }

        await client.EvaluateAsync(JevTestData.OneOfEach(), CancellationToken.None);

        Assert.That(_sink.Calls.Select(c => c.Character), Is.EqualTo(new[] { "bob", "maya", null }));
    }

    [Test]
    public void FailedLlmCallLogsErrorAndBilledUsage()
    {
        var client = new TelemetryLlmClient(new FailingLlm(), _sink, () => _now, "z-ai/glm-5.3-flash");

        Assert.ThrowsAsync<LlmSchemaException>(() => client.CompleteAsync(Speech(), CancellationToken.None));

        var r = _sink.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.Role, Is.EqualTo("light"));
            Assert.That(r.Model, Is.EqualTo("z-ai/glm-5.3-flash"));
            Assert.That(r.UsdSpent, Is.EqualTo(0.0002m));
            Assert.That(r.Error, Does.StartWith("LlmSchemaException: "));
        });
    }

    [Test]
    public void LedgerTotalsPerRoleCharacterAndHour()
    {
        // RM-06
        var ledger = new CostLedger();
        CallRecord Call(string role, string? character, decimal usd) =>
            new(0, character, role, "p", "m", "id", false, 100, 10, 0, usd, usd, 1, 1, 0, null, null);

        ledger.Write(Call("jev", "maya", 0.001m));
        ledger.Write(Call("jev", "bob", 0.002m));
        ledger.Write(Call("light", "maya", 0.004m));
        ledger.Write(Call("jev", "maya", 0m) with { Replayed = true, UsdRecorded = 0.5m });

        Assert.Multiple(() =>
        {
            Assert.That(ledger.All, Is.EqualTo(new CostLedger.Totals(4, 400, 40, 0.007m)));
            Assert.That(ledger.ByRole["jev"].Usd, Is.EqualTo(0.003m));
            Assert.That(ledger.ByCharacter["maya"].Calls, Is.EqualTo(3));
            Assert.That(ledger.ByCharacter["maya"].Usd, Is.EqualTo(0.005m));
            Assert.That(ledger.UsdPerHour(TimeSpan.FromMinutes(30)), Is.EqualTo(0.014m));
        });
    }

    [Test]
    public void DecisionLineIsCompact()
    {
        var line = TelemetryJson.Line(new DecisionRecord(3.25, "maya", "decision", "move:kitchen", 0.812345, null));

        Assert.That(line, Is.EqualTo(
            """{"t":3.25,"kind":"decision","character":"maya","purpose":"decision","decision":"move:kitchen","confidence":0.8123,"detail":null}"""));
    }

    [Test]
    public void JsonlSinkAppendsLfTerminatedLines()
    {
        var path = Path.Combine(_dir, "run.jsonl");
        using (var sink = new JsonlTelemetrySink(path))
        {
            sink.Write(new DecisionRecord(1, "a", "p", "x", 0.5, null));
        }

        using (var sink = new JsonlTelemetrySink(path))
        {
            sink.Write(new DecisionRecord(2, "a", "p", "y", 0.5, null));
        }

        var text = File.ReadAllText(path);
        Assert.Multiple(() =>
        {
            Assert.That(text.Split('\n'), Has.Length.EqualTo(3), "two lines plus the trailing newline");
            Assert.That(text, Does.Not.Contain("\r"));
        });
    }

    [Test]
    public async Task ReplayStrictRunsWriteByteIdenticalLogs()
    {
        // T1.05/T1.06 acceptance with the real wrapping order: telemetry(replay(inner)).
        var store = new FileReplayStore(Path.Combine(_dir, "replay"));
        var jevConfig = JevTestData.Config;

        async Task<byte[]> Run(ReplayMode mode, string runId)
        {
            var t = 0.0;
            var path = Path.Combine(_dir, runId + ".jsonl");
            using (var sink = new JsonlTelemetrySink(path))
            {
                var jev = new TelemetryJevClient(new ReplayJevClient(new FixedJev(), store, mode), sink, () => t);
                foreach (var character in new[] { "maya", "bob" })
                {
                    using (TelemetryContext.ForCharacter(character))
                    {
                        t += 1.5;
                        await jev.EvaluateAsync(JevTestData.OneOfEach(character) with { Model = jevConfig.Model },
                            CancellationToken.None);
                        sink.Write(new DecisionRecord(t, character, "decision", "help", 0.81, null));
                    }
                }
            }

            return await File.ReadAllBytesAsync(path);
        }

        await Run(ReplayMode.Record, "record");
        var first = await Run(ReplayMode.ReplayStrict, "replay1");
        var second = await Run(ReplayMode.ReplayStrict, "replay2");

        Assert.Multiple(() =>
        {
            Assert.That(second, Is.EqualTo(first));
            Assert.That(System.Text.Encoding.UTF8.GetString(first), Does.Contain("\"replayed\":true,"));
            Assert.That(System.Text.Encoding.UTF8.GetString(first), Does.Contain("\"usd\":0,"));
        });
    }
}
