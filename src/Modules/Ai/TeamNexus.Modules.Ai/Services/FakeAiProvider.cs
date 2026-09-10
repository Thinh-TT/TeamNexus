using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Offline stand-in for <see cref="DeepSeekAiProvider"/> (Phase 3 §2.3), registered whenever
/// <c>DeepSeek:ApiKey</c> is empty. It answers <b>two</b> prompt shapes with no network and no cost:
/// <list type="number">
///   <item><b>Smart Setup</b> (Phase 3): a fixed proposal following the §4.1 schema, deliberately
///   spanning the normalization branches (invalid priority → null, a task with two labels, a named
///   matched assignee, a task without assignee). The sample is trimmed by
///   <see cref="DeepSeekOptions.MaxTaskCount"/>.</item>
///   <item><b>AI Observer</b> (Phase 5 §4.3): identified by <see cref="ObserverPrompts.AgentMarker"/>
///   on the first line of the user prompt. It emits a findings payload that reuses the ids parsed
///   out of the summarized prompt, so the sample always passes the server-side validator.</item>
/// </list>
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

    /// <summary>camelCase JSON for the observer findings payload (Phase 5 §4.3).</summary>
    private static readonly JsonSerializerOptions AiOutputJson = new(JsonSerializerDefaults.Web);

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

        // Two shapes share this provider: Smart Setup proposals (Phase 3) and Observer findings
        // (Phase 5 §4.3). The observer prompt starts with ObserverPrompts.AgentMarker.
        if (IsObserverPrompt(request.UserPrompt))
        {
            var findings = BuildObserverFindings(request.UserPrompt);

            _logger.LogDebug(
                "FakeAiProvider: trả findings mẫu cho prompt Observer {Length} ký tự.",
                request.UserPrompt.Length);

            return Task.FromResult(new AiCompletionResult(findings, null, null));
        }

        var taskCount = Math.Clamp(_options.MaxTaskCount, 1, SampleTaskCount);
        var content = BuildProposal(request.UserPrompt, taskCount);

        _logger.LogDebug(
            "FakeAiProvider: trả proposal mẫu ({Tasks} task) cho prompt {Length} ký tự.",
            taskCount,
            request.UserPrompt.Length);

        // No token accounting: this call never touches the network.
        return Task.FromResult(new AiCompletionResult(content, null, null));
    }

    // ---- Observer branch (Phase 5 §4.3) ------------------------------------

    private static bool IsObserverPrompt(string userPrompt)
        => userPrompt.TrimStart().StartsWith(ObserverPrompts.AgentMarker, StringComparison.Ordinal);

    /// <summary>
    /// Builds a sample findings payload that <b>reuses the ids the server actually sent</b>
    /// (parsed straight out of the summarized prompt), so the response always survives
    /// <see cref="ObserverFindingValidator"/> without the harness knowing the fixture.
    /// Unparsable/empty payloads yield a well-formed empty result rather than an exception.
    /// </summary>
    private string BuildObserverFindings(string userPrompt)
    {
        Guid? firstTaskId = null;
        Guid? firstUserId = null;
        var type = NotificationTypes.OverdueTask;
        var severity = ObserverSeverity.High;

        try
        {
            var markerIndex = userPrompt.IndexOf(ObserverPrompts.AgentMarker, StringComparison.Ordinal);

            // The marker sits on its own line (`marker "\n" payload`), so the slice must be
            // TrimStart-ed: JsonDocument.Parse rejects a leading newline. Getting this wrong made
            // the fake silently answer "no findings" and masked the whole pipeline in verification.
            var json = (markerIndex < 0 ? userPrompt : userPrompt[(markerIndex + ObserverPrompts.AgentMarker.Length)..])
                .TrimStart();

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("signals", out var signals)
                && signals.ValueKind == JsonValueKind.Array
                && signals.GetArrayLength() > 0)
            {
                var first = signals[0];

                if (first.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String)
                {
                    type = NotificationTypes.Canonical(typeElement.GetString()) ?? type;
                }

                if (first.TryGetProperty("severity", out var severityElement)
                    && severityElement.ValueKind == JsonValueKind.String
                    && NotificationSeverities.IsKnown(severityElement.GetString()))
                {
                    severity = NotificationSeverities.All[NotificationSeverities.Rank(severityElement.GetString())];
                }

                if (first.TryGetProperty("evidence", out var evidence))
                {
                    firstTaskId = FirstId(evidence, "taskIds");
                    firstUserId = FirstId(evidence, "userIds");
                }
            }
        }
        catch (JsonException ex)
        {
            // Malformed payload (i.e. a prompt produced outside ObserverSummarizer): emit an
            // empty but valid document so the caller's parse path stays exercised. Logged because
            // this branch used to hide a marker/payload bug behind a silent "no findings".
            _logger.LogWarning(
                ex,
                "FakeAiProvider: không parse được payload Observer ({Length} ký tự) — trả về findings rỗng.",
                userPrompt.Length);
            return """{"findings":[]}""";
        }

        var findings = new List<object>
        {
            new
            {
                type,
                severity,
                title = "Cảnh báo tiến độ (mẫu — FakeAiProvider)",
                message = $"Tín hiệu {type} ở mức {severity}. Đây là dữ liệu mẫu vì chưa cấu hình DeepSeek:ApiKey.",
                taskIds = firstTaskId.HasValue ? new[] { firstTaskId.Value } : [],
                userIds = firstUserId.HasValue ? new[] { firstUserId.Value } : [],
            },
        };

        return JsonSerializer.Serialize(new { findings }, AiOutputJson);
    }

    private static Guid? FirstId(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                return id;
            }
        }

        return null;
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
