using System.Net;
using System.Text;

namespace Cognition.Core.Tests.Providers;

/// <summary>Scripted HTTP responses; records every request. No network.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _script = new();

    public List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

    public StubHttpHandler Respond(HttpStatusCode status, string body, Action<HttpResponseMessage>? configure = null)
    {
        _script.Enqueue((_, _) =>
        {
            var response = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            configure?.Invoke(response);
            return Task.FromResult(response);
        });
        return this;
    }

    public StubHttpHandler Hang()
    {
        _script.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });
        return this;
    }

    public StubHttpHandler FailToConnect()
    {
        _script.Enqueue((_, _) => throw new HttpRequestException("connection refused"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        Requests.Add((request, body));
        if (_script.Count == 0)
            throw new InvalidOperationException("StubHttpHandler: no scripted response left");
        return await _script.Dequeue()(request, ct);
    }
}
