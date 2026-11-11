using System.Net;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// Offline stand-in for Slack/Discord (Phase 15.2): no suite can ever reach a real webhook. Records every
/// request and answers with a configurable status (default 200).
/// </summary>
public sealed class StubWebhookHandler : HttpMessageHandler
{
    private readonly List<(Uri Url, string Body)> _requests = [];

    /// <summary>Status returned for every call; may be changed between passes.</summary>
    public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

    public IReadOnlyList<(Uri Url, string Body)> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        lock (_requests)
        {
            _requests.Add((request.RequestUri!, body));
        }

        return new HttpResponseMessage(Status);
    }
}

