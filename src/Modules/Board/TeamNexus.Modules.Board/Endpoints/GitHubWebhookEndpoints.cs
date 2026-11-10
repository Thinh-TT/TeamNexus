using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.Services.Integrations;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Public GitHub webhook endpoints (Phase 15 §1.3).
/// Receives asynchronous event callbacks from GitHub repositories.
/// Authentication is performed via HMAC-SHA256 signature verification in <see cref="IGitHubWebhookHandler"/>.
/// </summary>
public static class GitHubWebhookEndpoints
{
    public static IEndpointRouteBuilder MapGitHubWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/webhooks/github", HandleGitHubWebhookAsync)
            .WithTags("Webhooks");

        return endpoints;
    }

    private static async Task<IResult> HandleGitHubWebhookAsync(
        HttpContext http,
        IGitHubWebhookHandler handler)
    {
        var eventType = http.Request.Headers["X-GitHub-Event"].ToString();
        var signature = http.Request.Headers["X-Hub-Signature-256"].ToString();
        var deliveryId = http.Request.Headers["X-GitHub-Delivery"].ToString();

        using var ms = new MemoryStream();
        await http.Request.Body.CopyToAsync(ms, http.RequestAborted);
        var rawBody = ms.ToArray();

        return await handler.HandleWebhookAsync(eventType, signature, deliveryId, rawBody, http.RequestAborted);
    }
}
