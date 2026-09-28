using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class OpenAiCompatClientTests
{
    private const string ApiKey = "test-key-not-real";
    private const string Schema = """{"type":"object","required":["full","short"],"properties":{"full":{"type":"string"},"short":{"type":"string"}}}""";

    private static readonly LlmProviderConfig Config = new(
        BaseUrl: "https://openrouter.ai/api/v1",
        Model: "z-ai/glm-5.3-flash",
        ReasoningEffort: "low",
        ApiKeyEnv: "OPENROUTER_API_KEY",
        TimeoutMs: 1000,
        MaxRps: 5,
        MaxRetries: 2,
        BackoffInitialMs: 500,
        BackoffMaxMs: 5000,
        BackoffJitter: 0.25,
        StructuredOutput: StructuredOutputMode.JsonSchema,
        RequireParameters: true,
        ReasoningAllowanceTokens: 1000,
        InputPriceUsdPerMtok: 0.15m,
        OutputPriceUsdPerMtok: 0.50m);

    private StubHttpHandler _handler = null!;
    private List<TimeSpan> _delays = null!;
    private List<LlmExchange> _exchanges = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHttpHandler();
        _delays = new List<TimeSpan>();
        _exchanges = new List<LlmExchange>();
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    private OpenAiCompatClient Client(LlmProviderConfig? config = null, string? apiKey = ApiKey, bool repair = true)
    {
        return new OpenAiCompatClient(config ?? Config, apiKey, _handler, new OpenAiCompatClientOptions
        {
            Delay = (d, _) =>
            {
                _delays.Add(d);
                return Task.CompletedTask;
            },
            NextRandom = () => 0.5,
            OnExchange = _exchanges.Add,
            RepairMessage = repair ? errors => "FIX:" + errors : null,
        });
    }

    private static LlmRequest Plain() => new(LlmRole.Light, "sys", "user text", null, 100, "speech");

    private static LlmRequest WithSchema() => new(LlmRole.Light, "sys", "user text", Schema, 200, "daily_summary");

    private static string Chat(string content, string finish = "stop", decimal? cost = 0.0001m, int reasoning = 5)
    {
        var costJson = cost is null ? string.Empty : $",\"cost\":{cost.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return JsonSerializer.Serialize(new
        {
            id = "gen-1",
            model = "z-ai/glm-5.3-flash",
            choices = new[] { new { index = 0, finish_reason = finish, message = new { role = "assistant", content } } },
        })[..^1] + $",\"usage\":{{\"prompt_tokens\":100,\"completion_tokens\":20,\"completion_tokens_details\":{{\"reasoning_tokens\":{reasoning}}}{costJson}}}}}";
    }

    private StubHttpHandler Ok(string content, string finish = "stop", decimal? cost = 0.0001m) =>
        _handler.Respond(HttpStatusCode.OK, Chat(content, finish, cost));

    [Test]
    public async Task PostsChatCompletionWithRoleConfig()
    {
        // RM-03, RM-04
        Ok("Hello there.");
        using var client = Client();

        await client.CompleteAsync(Plain(), CancellationToken.None);

        var (request, body) = _handler.Requests.Single();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://openrouter.ai/api/v1/chat/completions")));
            Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", ApiKey)));
            Assert.That(root.GetProperty("model").GetString(), Is.EqualTo("z-ai/glm-5.3-flash"));
            Assert.That(root.GetProperty("reasoning_effort").GetString(), Is.EqualTo("low"));
            Assert.That(root.GetProperty("max_completion_tokens").GetInt32(), Is.EqualTo(1100));
            Assert.That(root.GetProperty("messages")[0].GetProperty("role").GetString(), Is.EqualTo("system"));
            Assert.That(root.GetProperty("messages")[1].GetProperty("content").GetString(), Is.EqualTo("user text"));
            Assert.That(root.GetProperty("provider").GetProperty("require_parameters").GetBoolean(), Is.True);
            Assert.That(root.TryGetProperty("response_format", out _), Is.False, "plain text request");
        });
    }

    [Test]
    public async Task PlainReplyReportsUsageAndCost()
    {
        // RM-06: OpenRouter's usage.cost is used as is.
        Ok("Hello there.", cost: 0.000123m);
        using var client = Client();

        var response = await client.CompleteAsync(Plain(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("Hello there."));
            Assert.That(response.ModelId, Is.EqualTo("z-ai/glm-5.3-flash"));
            Assert.That(response.RequestId, Is.EqualTo("gen-1"));
            Assert.That(response.Usage, Is.EqualTo(new UsageInfo(100, 20, 0.000123m)));
            Assert.That(response.ReasoningTokens, Is.EqualTo(5));
            Assert.That(response.CostReported, Is.True);
        });
    }

    [Test]
    public async Task MissingCostIsComputedFromConfiguredPrices()
    {
        // Local endpoints report tokens only.
        Ok("Hi.", cost: null);
        using var client = Client();

        var response = await client.CompleteAsync(Plain(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Usage.CostUsd, Is.EqualTo((100 * 0.15m + 20 * 0.50m) / 1_000_000m));
            Assert.That(response.CostReported, Is.False);
        });
    }

    [Test]
    public async Task SchemaRequestSendsJsonSchemaResponseFormat()
    {
        Ok("""{"full":"a","short":"b"}""");
        using var client = Client();

        await client.CompleteAsync(WithSchema(), CancellationToken.None);

        using var doc = JsonDocument.Parse(_handler.Requests.Single().Body);
        var format = doc.RootElement.GetProperty("response_format");
        Assert.Multiple(() =>
        {
            Assert.That(format.GetProperty("type").GetString(), Is.EqualTo("json_schema"));
            Assert.That(format.GetProperty("json_schema").GetProperty("name").GetString(), Is.EqualTo("daily_summary"));
            Assert.That(format.GetProperty("json_schema").GetProperty("schema").GetRawText(), Is.EqualTo(Schema));
        });
    }

    [Test]
    public async Task JsonObjectModeSendsNoSchema()
    {
        Ok("""{"full":"a","short":"b"}""");
        using var client = Client(Config with { StructuredOutput = StructuredOutputMode.JsonObject });

        await client.CompleteAsync(WithSchema(), CancellationToken.None);

        using var doc = JsonDocument.Parse(_handler.Requests.Single().Body);
        Assert.That(doc.RootElement.GetProperty("response_format").GetRawText(), Is.EqualTo("""{"type":"json_object"}"""));
    }

    [Test]
    public async Task LocalEndpointSendsNoAuthNoEffortNoProvider()
    {
        // RM-03
        Ok("Hi.");
        var local = Config with
        {
            BaseUrl = "http://127.0.0.1:8080/v1",
            ReasoningEffort = "",
            ApiKeyEnv = "",
            RequireParameters = false,
        };
        using var client = Client(local, apiKey: null);

        await client.CompleteAsync(Plain(), CancellationToken.None);

        var (request, body) = _handler.Requests.Single();
        using var doc = JsonDocument.Parse(body);
        Assert.Multiple(() =>
        {
            Assert.That(request.RequestUri, Is.EqualTo(new Uri("http://127.0.0.1:8080/v1/chat/completions")));
            Assert.That(request.Headers.Authorization, Is.Null);
            Assert.That(doc.RootElement.TryGetProperty("reasoning_effort", out _), Is.False);
            Assert.That(doc.RootElement.TryGetProperty("provider", out _), Is.False);
        });
    }

    [Test]
    public async Task CodeFencedJsonIsAccepted()
    {
        Ok("```json\n{\"full\":\"a\",\"short\":\"b\"}\n```");
        using var client = Client();

        var response = await client.CompleteAsync(WithSchema(), CancellationToken.None);

        Assert.That(response.Text, Is.EqualTo("""{"full":"a","short":"b"}"""));
    }

    [Test]
    public async Task InvalidReplyIsRepairedOnceWithTheErrors()
    {
        Ok("""{"full":"a"}""");
        Ok("""{"full":"a","short":"b"}""");
        using var client = Client();

        var response = await client.CompleteAsync(WithSchema(), CancellationToken.None);

        using var repair = JsonDocument.Parse(_handler.Requests[1].Body);
        var messages = repair.RootElement.GetProperty("messages");
        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("""{"full":"a","short":"b"}"""));
            Assert.That(response.SchemaRepairs, Is.EqualTo(1));
            Assert.That(response.Attempts, Is.EqualTo(2));
            Assert.That(response.Usage, Is.EqualTo(new UsageInfo(200, 40, 0.0002m)), "both rounds are billed");
            Assert.That(messages.GetArrayLength(), Is.EqualTo(4));
            Assert.That(messages[2].GetProperty("role").GetString(), Is.EqualTo("assistant"));
            Assert.That(messages[2].GetProperty("content").GetString(), Is.EqualTo("""{"full":"a"}"""));
            Assert.That(messages[3].GetProperty("content").GetString(),
                Is.EqualTo("FIX:$: missing required property 'short'"));
            Assert.That(_exchanges.Select(e => e.Round), Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void SecondInvalidReplyFailsWithUsage()
    {
        Ok("not json at all");
        Ok("""{"short":"b"}""");
        using var client = Client();

        var ex = Assert.ThrowsAsync<LlmSchemaException>(() => client.CompleteAsync(WithSchema(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Errors, Is.EqualTo(new[] { "$: missing required property 'full'" }));
            Assert.That(ex.Usage, Is.EqualTo(new UsageInfo(200, 40, 0.0002m)));
            Assert.That(_handler.Requests, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void WithoutRepairMessageInvalidReplyFailsAtOnce()
    {
        Ok("""{"full":"a"}""");
        using var client = Client(repair: false);

        Assert.ThrowsAsync<LlmSchemaException>(() => client.CompleteAsync(WithSchema(), CancellationToken.None));
        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void LengthCutOffIsAProtocolErrorWithUsage()
    {
        Ok("Hello th", finish: "length");
        using var client = Client();

        var ex = Assert.ThrowsAsync<LlmProtocolException>(() => client.CompleteAsync(Plain(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.Message, Does.Contain("max_completion_tokens=1100"));
            Assert.That(ex.Usage, Is.Not.Null);
        });
    }

    [Test]
    public void EmptyContentIsAProtocolError()
    {
        Ok("");
        using var client = Client();

        Assert.ThrowsAsync<LlmProtocolException>(() => client.CompleteAsync(Plain(), CancellationToken.None));
    }

    [Test]
    public async Task RetryableStatusIsRetriedWithBackoff()
    {
        _handler.Respond(HttpStatusCode.BadGateway, """{"error":{"code":502,"message":"model down"}}""");
        Ok("Hi.");
        using var client = Client();

        var response = await client.CompleteAsync(Plain(), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(response.Attempts, Is.EqualTo(2));
            Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(500 * (1 - 0.25 * 0.5)) }));
        });
    }

    [Test]
    public async Task RetryAfterHeaderIsHonored()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"slow down"}}""",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)));
        Ok("Hi.");
        using var client = Client();

        await client.CompleteAsync(Plain(), CancellationToken.None);

        Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(2 * (1 + 0.25 * 0.5)) }));
    }

    [TestCase(HttpStatusCode.PaymentRequired)]
    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.BadRequest)]
    public void NonRetryableStatusFailsAtOnce(HttpStatusCode status)
    {
        // 402 = out of credits: the owner tops up manually, so never retry.
        _handler.Respond(status, """{"error":{"code":400,"message":"no"}}""");
        using var client = Client();

        var ex = Assert.ThrowsAsync<LlmApiException>(() => client.CompleteAsync(Plain(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(status));
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ErrorObjectInOkBodyIsRetriedWhenRetryable()
    {
        // Errors after the model started are sent with status 200 (docs: errors-and-debugging).
        _handler.Respond(HttpStatusCode.OK, """{"error":{"code":502,"message":"provider error"}}""");
        Ok("Hi.");
        using var client = Client();

        var response = await client.CompleteAsync(Plain(), CancellationToken.None);

        Assert.That(response.Attempts, Is.EqualTo(2));
    }

    [Test]
    public void RetriesStopAtMaxRetries()
    {
        for (var i = 0; i < 3; i++)
        {
            _handler.Respond(HttpStatusCode.ServiceUnavailable, "{}");
        }

        using var client = Client();

        var ex = Assert.ThrowsAsync<LlmApiException>(() => client.CompleteAsync(Plain(), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
            Assert.That(_handler.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public void TimeoutIsRetriedThenReported()
    {
        _handler.Hang().Hang().Hang();
        using var client = Client(Config with { TimeoutMs = 20 });

        Assert.ThrowsAsync<LlmTimeoutException>(() => client.CompleteAsync(Plain(), CancellationToken.None));
        Assert.That(_exchanges.Select(e => e.Error), Is.All.EqualTo("timeout"));
    }

    [Test]
    public void ExchangesNeverContainTheApiKey()
    {
        // RM-05
        Ok("Hi.");
        using var client = Client();

        Assert.DoesNotThrowAsync(() => client.CompleteAsync(Plain(), CancellationToken.None));

        var dump = JsonSerializer.Serialize(_exchanges);
        Assert.That(dump, Does.Not.Contain(ApiKey));
    }

    [Test]
    public void RequestSerializationIsDeterministic()
    {
        // RA-03: the replay key hashes these bytes.
        Ok("""{"full":"a","short":"b"}""");
        Ok("""{"full":"a","short":"b"}""");
        using var client = Client();

        Assert.DoesNotThrowAsync(() => client.CompleteAsync(WithSchema(), CancellationToken.None));
        Assert.DoesNotThrowAsync(() => client.CompleteAsync(WithSchema(), CancellationToken.None));

        Assert.That(_handler.Requests[0].Body, Is.EqualTo(_handler.Requests[1].Body));
    }

    [Test]
    public async Task RoleRouterDispatchesByRole()
    {
        Ok("light");
        using var light = Client();
        using var heavyHandler = new StubHttpHandler();
        heavyHandler.Respond(HttpStatusCode.OK, Chat("heavy"));
        using var heavy = new OpenAiCompatClient(Config with { Model = "z-ai/glm-5.3" }, ApiKey, heavyHandler);
        var router = new RoleRoutedLlmClient(new Dictionary<LlmRole, ILlmClient>
        {
            [LlmRole.Light] = light,
            [LlmRole.Heavy] = heavy,
        });

        var response = await router.CompleteAsync(Plain() with { Role = LlmRole.Heavy }, CancellationToken.None);

        using var doc = JsonDocument.Parse(heavyHandler.Requests.Single().Body);
        Assert.Multiple(() =>
        {
            Assert.That(response.Text, Is.EqualTo("heavy"));
            Assert.That(doc.RootElement.GetProperty("model").GetString(), Is.EqualTo("z-ai/glm-5.3"));
            Assert.That(_handler.Requests, Is.Empty);
        });
    }

    [Test]
    public void CostGuardRefusesBeforeSending()
    {
        // RDev-04: heavy output price * (max output + reasoning allowance) exceeds a tiny cap.
        using var client = Client();
        var guarded = new CostGuardedLlmClient(client, new LiveCostGuard(0.0001m), Config, 4.0, repairEnabled: true);

        Assert.ThrowsAsync<CostCapExceededException>(() => guarded.CompleteAsync(WithSchema(), CancellationToken.None));
        Assert.That(_handler.Requests, Is.Empty);
    }

    [Test]
    public void CostGuardCountsTheCostOfFailedCalls()
    {
        Ok("""{"full":"a"}""");
        using var client = Client(repair: false);
        var guard = new LiveCostGuard(1m);
        var guarded = new CostGuardedLlmClient(client, guard, Config, 4.0, repairEnabled: false);

        Assert.ThrowsAsync<LlmSchemaException>(() => guarded.CompleteAsync(WithSchema(), CancellationToken.None));
        Assert.That(guard.SpentUsd, Is.EqualTo(0.0001m));
    }
}
