using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Offline stand-in for <see cref="DeepSeekAiProvider"/> (Phase 3 §2.3), registered whenever
/// <c>DeepSeek:ApiKey</c> is empty. It returns a fixed proposal that follows the Smart Setup AI
/// schema (Phase 3 §4.1) so the whole flow — prompt building, parse, normalize, resolve
/// assignee/label, UI — can be developed and demoed with no API key, no network and no cost.
/// <para>
/// The sample deliberately spans the branches §4.3 has to handle: an invalid priority (null),
/// a task with two labels, a named (matched) assignee and a task without an assignee.
/// </para>
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    /// <summary>Number of sample tasks; <see cref="DeepSeekOptions.MaxTaskCount"/> can trim it further.</summary>
    private const int SampleTaskCount = 3;

    private const string SummaryPlaceholder = "__SUMMARY__";

    private const string TaskPlaceholder = "__TASKS__";

    /// <summary>Markers used to locate the team lead's description inside the composed prompt.</summary>
    private const string DescriptionMarker = "Mô tả công việc của trưởng nhóm:";

    private const string InstructionMarker = "Hãy phân rã mô tả trên";

    private const string TaskOne = """
        {
          "title": "Thiết kế schema PostgreSQL cho tính năng",
          "description": "Chốt bảng, cột, khóa ngoại và migration EF Core cho tính năng mới.",
          "priority": "High",
          "labels": ["backend", "database"],
          "suggestedAssignee": "Linh"
        }
        """;

    private const string TaskTwo = """
        {
          "title": "Xây API CRUD cho tính năng",
          "description": "Minimal API + service, kiểm tra quyền theo workspace role.",
          "priority": "Medium",
          "labels": ["backend"],
          "suggestedAssignee": null
        }
        """;

    private const string TaskThree = """
        {
          "title": "Viết kiểm thử và tài liệu cho tính năng",
          "description": "Test các luồng chính, cập nhật README module và checklist giai đoạn.",
          "priority": null,
          "labels": ["testing", "docs"],
          "suggestedAssignee": null
        }
        """;

    private const string ProposalTemplate = """
        {
          "summary": "__SUMMARY__",
          "tasks": [
        __TASKS__
          ]
        }
        """;

    private static readonly string[] TaskBlocks = [TaskOne, TaskTwo, TaskThree];

    private readonly DeepSeekOptions _options;
    private readonly ILogger<FakeAiProvider> _logger;

    public FakeAiProvider(IOptions<DeepSeekOptions> options, ILogger<FakeAiProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var taskCount = Math.Clamp(_options.MaxTaskCount, 1, SampleTaskCount);
        var content = BuildProposal(request.UserPrompt, taskCount);

        _logger.LogDebug(
            "FakeAiProvider: trả proposal mẫu ({Tasks} task) cho prompt {Length} ký tự.",
            taskCount,
            request.UserPrompt.Length);

        // No token accounting: this call never touches the network.
        return Task.FromResult(new AiCompletionResult(content, null, null));
    }

    /// <summary>
    /// Builds the sample proposal. The <c>summary</c> echoes a short preview of the team lead's
    /// description (not the whole prompt, which also carries board/member context) so a developer
    /// can see which request produced it. The preview is escaped with
    /// <see cref="JsonEncodedText"/> — the result is always valid JSON regardless of input.
    /// </summary>
    private static string BuildProposal(string userPrompt, int taskCount)
    {
        var description = ExtractDescription(userPrompt);
        var preview = Truncate(description, 60);
        var summary = preview.Length == 0
            ? $"Đề xuất mẫu (FakeAiProvider) — không có mô tả. {taskCount} sub-task."
            : $"Đề xuất mẫu (FakeAiProvider) cho mô tả {description.Length} ký tự: {preview}";

        var escapedSummary = JsonEncodedText.Encode(summary, JavaScriptEncoder.Default).ToString();
        var tasks = string.Join(",\n", TaskBlocks.Take(taskCount).Select(block => "    " + block));

        return ProposalTemplate
            .Replace(SummaryPlaceholder, escapedSummary, StringComparison.Ordinal)
            .Replace(TaskPlaceholder, tasks, StringComparison.Ordinal);
    }

    /// <summary>
    /// Pulls the description out of the composed user prompt (SmartSetupPrompts puts it after
    /// <c>"Mô tả công việc của trưởng nhóm:"</c>). Falls back to the whole prompt when the marker
    /// is absent, e.g. when a Phase 5 Observer prompt reuses this provider.
    /// </summary>
    private static string ExtractDescription(string userPrompt)
    {
        var marker = userPrompt.IndexOf(DescriptionMarker, StringComparison.Ordinal);
        if (marker < 0)
        {
            return userPrompt.Trim();
        }

        var start = marker + DescriptionMarker.Length;
        var end = userPrompt.IndexOf(InstructionMarker, start, StringComparison.Ordinal);
        var slice = end < 0 ? userPrompt[start..] : userPrompt[start..end];

        return slice.Trim();
    }

    private static string Truncate(string value, int limit)
        => value.Length <= limit ? value : value[..limit] + "…";
}
