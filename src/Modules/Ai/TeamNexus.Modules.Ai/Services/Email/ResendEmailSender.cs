using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Email;

/// <summary>
/// Resend API transport (Phase 11 §2.2). Registered only when <c>Email:ApiKey</c> is set.
/// <para>
/// <b>No SDK dependency.</b> Like the DeepSeek and Tavily transports, this is a plain
/// <see cref="IHttpClientFactory"/> client plus <c>System.Text.Json</c> — the two calls this phase
/// needs do not justify a NuGet package, and keeping the shape identical to the existing transports
/// means one way to read provider integrations in this repository.
/// </para>
/// <para>
/// <b>Never throws.</b> Unlike the AI transports (which surface <c>AiProviderException</c> because a
/// model answer is the point of the request), a failed email must not fail the write that triggered
/// it: the invitation row is already committed and the UI can offer "Gửi lại". Every failure path
/// returns <see cref="EmailSendResult.Failed"/>.
/// </para>
/// <para>The key is never logged and never embedded in an error message.</para>
/// </summary>
public sealed class ResendEmailSender : IEmailSender
{
    /// <summary>Provider error bodies are truncated before they reach a log or a DB column.</summary>
    private const int ErrorBodyLimit = 200;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        // Resend expects snake_case (`from`/`to`/`subject`/`html`/`text`) and null-free bodies.
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmailOptions _options;
    private readonly ILogger<ResendEmailSender> _logger;

    public ResendEmailSender(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailOptions> options,
        ILogger<ResendEmailSender> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default)
    {
        var payload = new SendRequest(
            _options.FromAddress,
            [envelope.To],
            envelope.Subject,
            envelope.HtmlBody,
            envelope.TextBody);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_options.NormalizedBaseUrl}/emails")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, RequestJson),
                Encoding.UTF8,
                "application/json"),
        };

        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        string body;
        try
        {
            var client = _httpClientFactory.CreateClient(AiModule.EmailHttpClientName);
            response = await client.SendAsync(httpRequest, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException with the caller token untouched.
            return Failure($"Resend không phản hồi trong {_options.TimeoutSeconds}s (timeout).", envelope.To);
        }
        catch (HttpRequestException ex)
        {
            return Failure($"Không gọi được Resend: {ex.Message}", envelope.To);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    $"Resend trả về {(int)response.StatusCode} {response.StatusCode}: {Truncate(body)}",
                    envelope.To);
            }

            // The id is audit data only (email_messages.provider_message_id); a body we cannot parse
            // is not a failure, because the provider already accepted the message.
            string? providerId = null;
            try
            {
                providerId = JsonSerializer.Deserialize<SendResponse>(body, ResponseJson)?.Id;
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Resend trả về body không parse được cho email tới {To}.", envelope.To);
            }

            _logger.LogInformation("Resend đã nhận email tới {To} (id={ProviderMessageId}).", envelope.To, providerId);

            return EmailSendResult.Sent(providerId);
        }
    }

    private EmailSendResult Failure(string error, string to)
    {
        _logger.LogWarning("Gửi email tới {To} thất bại: {Error}", to, error);
        return EmailSendResult.Failed(error);
    }

    private static string Truncate(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text.Length <= ErrorBodyLimit ? text : text[..ErrorBodyLimit];
    }

    /// <summary>Resend request body. Serialized snake_case ⇒ <c>from</c>/<c>to</c>/<c>subject</c>…</summary>
    private sealed record SendRequest(
        string From,
        IReadOnlyList<string> To,
        string Subject,
        string Html,
        string? Text);

    private sealed record SendResponse(string? Id);
}
