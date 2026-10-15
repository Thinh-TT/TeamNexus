using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// OpenAI-compatible embeddings client (Giai đoạn 14.7 Track B §B3): <c>POST {BaseUrl}/embeddings</c>.
/// <para>
/// <b>No retry</b>, like <c>DeepSeekAiProvider</c> and the Jev client. An embedding pass is a background job
/// over a bounded batch; retrying inside the client would hold a scope open while the provider is unwell, and a
/// chunk that fails is simply picked up by the next pass (its <c>embedding</c> is still NULL).
/// </para>
/// <para>
/// Every failure — transport, non-2xx, malformed body, wrong vector length — is mapped to
/// <see cref="EmbeddingProviderException"/>. The API key never appears in a log line or an exception message.
/// </para>
/// </summary>
public sealed class OpenRouterEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Cap on the error body copied into an exception message (the Jev client's precedent).</summary>
    private const int MaxErrorBodyChars = 500;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingOptions _options;
    private readonly ILogger<OpenRouterEmbeddingProvider> _logger;

    public OpenRouterEmbeddingProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<EmbeddingOptions> options,
        ILogger<OpenRouterEmbeddingProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        if (texts.Count == 0)
        {
            // No inputs, no call: a request with an empty batch is rejected by every provider and would only
            // burn a round trip.
            return [];
        }

        var client = _httpClientFactory.CreateClient(KnowledgeBaseService.HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.EmbeddingsUri)
        {
            Content = JsonContent.Create(new EmbeddingsBody(_options.EffectiveModel, texts), options: Json),
        };

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The message carries the URL, never the key: "why can't we reach it" is the only useful
            // information here, and a secret in a log is a permanent leak.
            throw new EmbeddingProviderException(
                $"Không gọi được dịch vụ embedding ({_options.EmbeddingsUri}).", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var body = await ReadBodyAsync(response, ct);

                throw new EmbeddingProviderException(
                    $"Dịch vụ embedding trả về HTTP {(int)response.StatusCode}: {body}");
            }

            EmbeddingsResponse? parsed;

            try
            {
                parsed = await response.Content.ReadFromJsonAsync<EmbeddingsResponse>(Json, ct);
            }
            catch (JsonException ex)
            {
                throw new EmbeddingProviderException(
                    "Không đọc được phản hồi embedding (JSON không hợp lệ).", ex);
            }

            if (parsed?.Data is null || parsed.Data.Count != texts.Count)
            {
                throw new EmbeddingProviderException(
                    $"Phản hồi embedding trả về {(parsed?.Data?.Count ?? 0)} vector cho {texts.Count} đoạn văn bản.");
            }

            // Ordered by the provider's own `index` rather than by array position: the OpenAI-compatible
            // contract does not promise ordering, and mixing up two chunks' vectors would silently make
            // retrieval return the wrong text for a query — the hardest possible bug to notice.
            var ordered = parsed.Data.OrderBy(item => item.Index).ToList();

            var vectors = new List<ReadOnlyMemory<float>>(ordered.Count);

            foreach (var item in ordered)
            {
                if (item.Embedding is null || item.Embedding.Length == 0)
                {
                    throw new EmbeddingProviderException(
                        "Phản hồi embedding thiếu vector cho một trong các đoạn văn bản.");
                }

                if (item.Embedding.Length != _options.Dimensions)
                {
                    throw new EmbeddingProviderException(
                        $"Vector embedding có {item.Embedding.Length} chiều, schema yêu cầu "
                        + $"{_options.Dimensions}. Kiểm tra Embeddings:Model và Embeddings:Dimensions.");
                }

                vectors.Add(item.Embedding);
            }

            _logger.LogDebug(
                "Embedding: {Count} vector ({Dimensions} chiều) từ model {Model}.",
                vectors.Count,
                _options.Dimensions,
                _options.EffectiveModel);

            return vectors;
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);

            return body.Length <= MaxErrorBodyChars ? body : body[..MaxErrorBodyChars];
        }
        catch
        {
            // Reading a failure body is best-effort diagnostics; it must never replace the real error.
            return "(không đọc được nội dung lỗi)";
        }
    }

    /// <summary>Request body of the OpenAI-compatible embeddings endpoint.</summary>
    private sealed record EmbeddingsBody(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    /// <summary>Response envelope; only the fields this client needs are modelled.</summary>
    private sealed record EmbeddingsResponse(
        [property: JsonPropertyName("data")] List<EmbeddingsItem>? Data);

    /// <summary>One vector plus its position in the request batch.</summary>
    private sealed record EmbeddingsItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
