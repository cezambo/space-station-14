using System.Net;
using System.Net.Http.Headers;
using Cognition.Core.Config;
using Cognition.Core.Providers;

namespace Cognition.Core.Tests.Providers;

[TestFixture]
public sealed class JevHttpClientTests
{
    private const string ApiKey = "test-key-not-real";

    private StubHttpHandler _handler = null!;
    private List<TimeSpan> _delays = null!;
    private List<JevExchange> _exchanges = null!;

    [SetUp]
    public void SetUp()
    {
        _handler = new StubHttpHandler();
        _delays = new List<TimeSpan>();
        _exchanges = new List<JevExchange>();
    }

    [TearDown]
    public void TearDown()
    {
        _handler.Dispose();
    }

    private JevHttpClient Client(JevProviderConfig? config = null)
    {
        return new JevHttpClient(config ?? JevTestData.Config, ApiKey, _handler, new JevHttpClientOptions
        {
            Delay = (d, _) =>
            {
                _delays.Add(d);
                return Task.CompletedTask;
            },
            NextRandom = () => 0.5,
            OnExchange = _exchanges.Add,
        });
    }

    private Task<JevResponse> Evaluate(JevHttpClient client, CancellationToken ct = default)
    {
        return client.EvaluateAsync(JevTestData.OneOfEach(), ct);
    }

    private StubHttpHandler Ok(Action<HttpResponseMessage>? configure = null)
    {
        return _handler.Respond(HttpStatusCode.OK, JevTestData.OneOfEachResponse, configure);
    }

    [Test]
    public async Task PostsToSystemOneWithBearerAuthAndJsonBody()
    {
        // RM-07
        Ok();
        using var client = Client();

        await Evaluate(client);

        var (request, body) = _handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Method, Is.EqualTo(HttpMethod.Post));
            Assert.That(request.RequestUri, Is.EqualTo(new Uri("https://api.typesafe.ai/v1/systemone")));
            Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", ApiKey)));
            Assert.That(request.Content!.Headers.ContentType!.MediaType, Is.EqualTo("application/json"));
            Assert.That(body, Is.EqualTo(JevWireFormat.SerializeRequest(JevTestData.OneOfEach())));
        });
    }

    [Test]
    public async Task SuccessComputesCostFromInputTokens()
    {
        // RM-06: one million input tokens at the configured $0.042/Mtok; output tokens are free.
        Ok(r => r.Headers.Add("x-typesafe-request-id", "req_123"));
        using var client = Client();

        var response = await Evaluate(client);

        Assert.Multiple(() =>
        {
            Assert.That(response.Usage, Is.EqualTo(new UsageInfo(1_000_000, 20, 0.042m)));
            Assert.That(response.Model, Is.EqualTo("jev-1.13.0"));
            Assert.That(response.RequestId, Is.EqualTo("req_123"));
            Assert.That(response.Attempts, Is.EqualTo(1));
            Assert.That(response.Answers, Has.Count.EqualTo(3));
            Assert.That(_delays, Is.Empty);
        });
    }

    [Test]
    public async Task MissingRequestIdHeaderGetsALocalId()
    {
        Ok();
        using var client = Client();

        var response = await Evaluate(client);

        Assert.That(response.RequestId, Does.StartWith("local-"));
    }

    [Test]
    public async Task SessionCookiesAreNotKeptInExchanges()
    {
        Ok(r => r.Headers.Add("Set-Cookie", "__cf_bm=abc; path=/"));
        using var client = Client();

        await Evaluate(client);

        Assert.That(_exchanges.Single().ResponseHeaders.Keys, Has.None.EqualTo("Set-Cookie").IgnoreCase);
    }

    [Test]
    public async Task RateLimitHonorsRetryAfterMsPlusJitter()
    {
        // RM-07, RNF-05: wait at least what the server asks, plus up to 25% jitter (0.5 * 25% here).
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}", r => r.Headers.Add("retry-after-ms", "1500"));
        Ok();
        using var client = Client();

        var response = await Evaluate(client);

        Assert.Multiple(() =>
        {
            Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(1500 * 1.125) }));
            Assert.That(response.Attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task RateLimitHonorsRetryAfterSeconds()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2)));
        Ok();
        using var client = Client();

        await Evaluate(client);

        Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromSeconds(2 * 1.125) }));
    }

    [Test]
    public async Task OverloadedWithoutHeaderUsesExponentialBackoff()
    {
        // 529 Overloaded (docs.typesafe.ai/api): 500 ms, then 1000 ms, each minus 0.5 * 25% jitter.
        _handler.Respond((HttpStatusCode)529, "{\"error\":\"overloaded\"}");
        _handler.Respond(HttpStatusCode.ServiceUnavailable, "{}");
        Ok();
        using var client = Client();

        var response = await Evaluate(client);

        Assert.Multiple(() =>
        {
            Assert.That(_delays, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(437.5), TimeSpan.FromMilliseconds(875) }));
            Assert.That(response.Attempts, Is.EqualTo(3));
        });
    }

    [Test]
    public void ServerErrorsStopAfterThreeRetries()
    {
        // RM-07: at most 3 retries, so 4 attempts in total.
        for (var i = 0; i < 4; i++)
        {
            _handler.Respond(HttpStatusCode.InternalServerError, "{\"error\":\"boom\"}");
        }

        using var client = Client();

        var ex = Assert.ThrowsAsync<JevApiException>(() => Evaluate(client));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(_handler.Requests, Has.Count.EqualTo(4));
            Assert.That(_delays, Has.Count.EqualTo(3));
        });
    }

    [TestCase(HttpStatusCode.Unauthorized)]
    [TestCase(HttpStatusCode.UnprocessableEntity)]
    [TestCase(HttpStatusCode.BadRequest)]
    public void ClientErrorsAreNotRetried(HttpStatusCode status)
    {
        _handler.Respond(status, "{\"detail\":\"questions.q.type: field required\"}");
        using var client = Client();

        var ex = Assert.ThrowsAsync<JevApiException>(() => Evaluate(client));

        Assert.Multiple(() =>
        {
            Assert.That(ex!.StatusCode, Is.EqualTo(status));
            Assert.That(ex.Message, Does.Contain("field required"));
            Assert.That(_handler.Requests, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void RetryAfterBeyondLimitFailsFastInsteadOfStalling()
    {
        // RNF-05: no hangs.
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}",
            r => r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(5)));
        using var client = Client();

        Assert.ThrowsAsync<JevApiException>(() => Evaluate(client));
        Assert.That(_delays, Is.Empty);
    }

    [Test]
    public void TimeoutIsRetriedThenReported()
    {
        // RNF-05
        _handler.Hang().Hang();
        using var client = Client(JevTestData.Config with { TimeoutMs = 50, MaxRetries = 1 });

        Assert.ThrowsAsync<JevTimeoutException>(() => Evaluate(client));
        Assert.That(_handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task TimeoutThenSuccessRecovers()
    {
        _handler.Hang();
        Ok();
        using var client = Client(JevTestData.Config with { TimeoutMs = 50 });

        var response = await Evaluate(client);

        Assert.That(response.Attempts, Is.EqualTo(2));
    }

    [Test]
    public void ConnectionFailuresAreRetriedThenReported()
    {
        _handler.FailToConnect().FailToConnect();
        using var client = Client(JevTestData.Config with { MaxRetries = 1 });

        Assert.ThrowsAsync<JevTransportException>(() => Evaluate(client));
        Assert.That(_handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void CallerCancellationIsNotRetried()
    {
        _handler.Hang();
        using var client = Client();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(async () => await Evaluate(client, cts.Token), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(_handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public void InvalidRequestIsNeverSent()
    {
        using var client = Client();
        var request = JevTestData.OneOfEach() with { State = "" };

        Assert.ThrowsAsync<JevValidationException>(() => client.EvaluateAsync(request, CancellationToken.None));
        Assert.That(_handler.Requests, Is.Empty);
    }

    [Test]
    public async Task ExchangesNeverContainTheApiKey()
    {
        // RM-05
        _handler.Respond(HttpStatusCode.TooManyRequests, "{}", r => r.Headers.Add("retry-after-ms", "10"));
        Ok();
        using var client = Client();

        await Evaluate(client);

        Assert.That(_exchanges, Has.Count.EqualTo(2));
        foreach (var e in _exchanges)
        {
            var text = string.Join("|", e.RequestJson, e.ResponseBody, e.Error,
                string.Join(",", e.ResponseHeaders.Select(h => h.Key + "=" + h.Value)));
            Assert.That(text, Does.Not.Contain(ApiKey));
        }
    }

    [TestCase(200, false)]
    [TestCase(400, false)]
    [TestCase(401, false)]
    [TestCase(408, true)]
    [TestCase(422, false)]
    [TestCase(429, true)]
    [TestCase(500, true)]
    [TestCase(529, true)]
    public void RetryableStatuses(int status, bool retryable)
    {
        Assert.That(JevHttpClient.IsRetryable((HttpStatusCode)status), Is.EqualTo(retryable));
    }
}
