using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Offline stand-in for <see cref="TavilyWebSearchProvider"/> (Phase 7 §4.3b), registered whenever
/// <c>Tavily:ApiKey</c> is empty. Returns a small fixed set of results pointing at
/// <c>https://example.test/…</c> so the loop, the tool-result cap and the trace can be verified
/// without network access and without spending search credits (same philosophy as
/// <see cref="FakeAiProvider"/>).
/// </summary>
public sealed class FakeWebSearchProvider : IWebSearchProvider
{
    private static readonly WebSearchResult[] Sample =
    [
        new(
            "TeamNexus — tài liệu tham chiếu (mẫu, offline)",
            "https://example.test/teamnexus/docs",
            "Kết quả mẫu do FakeWebSearchProvider sinh ra: không gọi mạng, không tốn credit Tavily."),
        new(
            "Kanban + AI Agent: gợi ý thiết kế vòng lặp tool-calling",
            "https://example.test/kanban/ai-agent",
            "Vòng lặp nên có whitelist tool, cap số lượt gọi và luôn kết thúc bằng một hành động rõ ràng."),
        new(
            "Guardrail cho agent tự động (mẫu)",
            "https://example.test/ai/guardrails",
            "Giới hạn số tool-call, thời gian chạy và ngân sách token là ba ngưỡng tối thiểu."),
    ];

    private readonly ILogger<FakeWebSearchProvider> _logger;

    public FakeWebSearchProvider(IOptions<AgentOptions> agentOptions, ILogger<FakeWebSearchProvider> logger)
    {
        _logger = logger;
        _maxResults = agentOptions.Value.Effective.WebSearchMaxResults;
    }

    private readonly int _maxResults;

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var requested = maxResults <= 0 ? _maxResults : maxResults;
        var take = Math.Clamp(requested, 1, Math.Max(1, Math.Min(_maxResults, Sample.Length)));

        _logger.LogDebug(
            "FakeWebSearchProvider: trả {Count} kết quả mẫu cho truy vấn '{Query}' (không gọi mạng).",
            take,
            query);

        return Task.FromResult<IReadOnlyList<WebSearchResult>>(Sample.Take(take).ToList());
    }
}
