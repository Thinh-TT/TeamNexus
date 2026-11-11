using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.Endpoints;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Ai.Endpoints;

/// <summary>
/// Server-to-server endpoint for inbound GitHub webhooks (Phase 15.3 §T15.3.4).
/// Authenticates raw payload bytes via HMAC-SHA256 and delegates to <see cref="IGitHubWebhookService"/>.
/// </summary>
public static class GitHubWebhookEndpoints
{
    public const int MaxPayloadBytes = 262_144; // 256 KB

    public static IEndpointRouteBuilder MapGitHubWebhookEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/webhooks/github/{webhookToken:guid}", HandleWebhookAsync)
            .WithTags("Integrations")
            .AllowAnonymous()
            .AddEndpointFilter<DomainExceptionFilter>();

        return endpoints;
    }

    private static async Task<IResult> HandleWebhookAsync(
        Guid webhookToken,
        HttpContext context,
        TeamNexusDbContext db,
        IGitHubSecretProtector protector,
        IGitHubSignatureValidator validator,
        IGitHubWebhookService webhookService,
        CancellationToken ct)
    {
        var request = context.Request;

        if (request.ContentLength > MaxPayloadBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var eventHeader = request.Headers["X-GitHub-Event"].ToString();
        var deliveryHeader = request.Headers["X-GitHub-Delivery"].ToString();
        var signatureHeader = request.Headers["X-Hub-Signature-256"].ToString();

        if (string.IsNullOrWhiteSpace(eventHeader) || string.IsNullOrWhiteSpace(deliveryHeader))
        {
            return Results.BadRequest(new { error = "Thiếu header X-GitHub-Event hoặc X-GitHub-Delivery." });
        }

        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Results.Unauthorized();
        }

        // Read with the limit enforced while reading, not after. `CopyToAsync` buffered the whole body first,
        // so a chunked request (ContentLength == null) could allocate far above the feature's 256 KB budget
        // before the comparison below ever ran (review F10).
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var totalBytes = 0;

        while (true)
        {
            var read = await request.Body.ReadAsync(chunk, ct);
            if (read == 0)
            {
                break;
            }

            totalBytes += read;
            if (totalBytes > MaxPayloadBytes)
            {
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            }

            buffer.Write(chunk, 0, read);
        }

        var rawBytes = buffer.ToArray();

        var integration = await db.WorkspaceGitHubIntegrations
            .FirstOrDefaultAsync(x => x.WebhookToken == webhookToken, ct);

        if (integration is null || !integration.IsEnabled)
        {
            return Results.NotFound(new { error = "Webhook không tồn tại hoặc đã bị vô hiệu hóa." });
        }

        string secret;
        try
        {
            secret = protector.Unprotect(integration.SecretCiphertext);
        }
        catch
        {
            return Results.Unauthorized();
        }

        var isValidSignature = validator.ValidateSignature(rawBytes, secret, signatureHeader);
        if (!isValidSignature)
        {
            return Results.Unauthorized();
        }

        var processResult = await webhookService.ProcessWebhookAsync(
            integration,
            eventHeader,
            deliveryHeader,
            rawBytes,
            ct);

        // A "Failed" delivery means we could not do the work (e.g. the configured board is gone). Answering
        // 200 told GitHub the event was accepted, so its own retry/redeliver never kicked in (review F11).
        if (string.Equals(processResult.Status, "Failed", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Json(processResult, statusCode: StatusCodes.Status502BadGateway);
        }

        return Results.Ok(processResult);
    }
}
