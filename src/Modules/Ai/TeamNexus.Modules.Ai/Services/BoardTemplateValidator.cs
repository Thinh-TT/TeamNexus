using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using BoardDtos = TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The one JSON-completion helper shared by the two AI proposal flows (Phase 14 §4).
/// <para>
/// Phase 3's <c>SmartSetupService</c> already implemented "ask once, and on a malformed answer ask once
/// more with the parse error appended". The board template needs exactly that behaviour, so it is
/// extracted here rather than copied: two prompt flows that repair bad JSON differently is how one of
/// them silently starts failing after a model update.
/// </para>
/// </summary>
internal static class AiJsonCompletion
{
    /// <summary>
    /// Runs one JSON-mode completion and parses it; on a malformed answer, retries once with
    /// <paramref name="buildRetryPrompt"/>. Throws <see cref="AiProviderException"/> when both attempts
    /// fail, never <see cref="System.Text.Json.JsonException"/> — a caller should not have to know which
    /// layer produced the garbage.
    /// </summary>
    public static async Task<T> CompleteWithRetryAsync<T>(
        IAiProvider provider,
        DeepSeekOptions options,
        string systemPrompt,
        string userPrompt,
        Func<string, string> buildRetryPrompt,
        Func<string, T> parse,
        ILogger logger,
        CancellationToken ct = default)
    {
        var completion = await provider.CompleteAsync(
            new AiCompletionRequest(systemPrompt, userPrompt, options.Temperature, options.MaxTokens, JsonMode: true),
            ct);

        try
        {
            return parse(completion.Content);
        }
        catch (System.Text.Json.JsonException ex)
        {
            logger.LogWarning(ex, "AI trả về JSON không hợp lệ — thử lại một lần với prompt chặt hơn.");

            var retry = await provider.CompleteAsync(
                new AiCompletionRequest(
                    systemPrompt,
                    buildRetryPrompt(ex.Message),
                    options.Temperature,
                    options.MaxTokens,
                    JsonMode: true),
                ct);

            try
            {
                return parse(retry.Content);
            }
            catch (System.Text.Json.JsonException retryEx)
            {
                throw new AiProviderException(
                    "Không parse được JSON từ AI sau 2 lần thử (schema không khớp). "
                    + $"Chi tiết: {retryEx.Message}");
            }
        }
    }
}

/// <summary>
/// Turns the untrusted board-template answer into a proposal a human can review (Phase 14 §4).
/// <para>
/// <b>Pure and public</b> so every normalization rule is verified without a database or a model — the
/// same posture as <c>SmartSetupService.BuildProposal</c>, whose three resolution helpers
/// (<c>NormalizePriority</c>/<c>ResolveLabels</c>/<c>ResolveAssignee</c>) are reused here rather than
/// re-implemented.
/// </para>
/// </summary>
public static class BoardTemplateValidator
{
    /// <summary>
    /// Fallback board name when the model omits one: a board with no name is worse than a derived name,
    /// and the user can rename it in the preview before confirming.
    /// </summary>
    public const int FallbackNameExcerptLength = 40;

    /// <summary>
    /// Normalizes one AI answer into a proposal, or throws <see cref="BadRequestException"/> when it
    /// cannot be made usable. Throws 400 rather than returning a partial board: creating a board with
    /// one column or zero tasks is not a recoverable outcome, it is a wrong board.
    /// </summary>
    public static DTOs.BoardTemplateProposal Normalize(
        AiBoardTemplateOutput? output,
        string description,
        IReadOnlyList<BoardDtos.WorkspaceMemberResponse> members,
        IReadOnlyList<WorkspaceLabel> existingLabels,
        int maxTaskCount)
    {
        if (output is null)
        {
            throw new BadRequestException(
                "AI không trả về đề xuất bảng nào — hãy mô tả dự án chi tiết hơn và thử lại.");
        }

        var columns = NormalizeColumns(output.Columns);
        if (columns.Count < BoardTemplatePrompts.MinColumns)
        {
            throw new BadRequestException(
                $"AI phải đề xuất ít nhất {BoardTemplatePrompts.MinColumns} cột "
                + $"(nhận {columns.Count}) — hãy mô tả quy trình làm việc rõ hơn và thử lại.");
        }

        var tasks = NormalizeTasks(
            output.Tasks,
            columns,
            members,
            existingLabels,
            // The operator's own ceiling and the product's hard cap both apply; the smaller one wins.
            maxTaskCount > 0
                ? Math.Min(maxTaskCount, BoardTemplatePrompts.MaxTasks)
                : BoardTemplatePrompts.MaxTasks);

        if (tasks.Count == 0)
        {
            throw new BadRequestException(
                "AI không trả về task khởi đầu hợp lệ nào — hãy mô tả dự án chi tiết hơn và thử lại.");
        }

        return new DTOs.BoardTemplateProposal(
            TrimToNull(output.Summary),
            NormalizeBoardName(output.BoardName, description),
            CapLength(TrimToNull(output.BoardDescription), 500),
            columns,
            tasks);
    }

    /// <summary>
    /// Columns: names trimmed, over-long/duplicate names dropped, capped at
    /// <see cref="BoardTemplatePrompts.MaxColumns"/>, and <b>exactly one</b> marked done.
    /// </summary>
    public static List<DTOs.BoardTemplateColumnProposal> NormalizeColumns(
        IReadOnlyList<AiBoardTemplateColumn>? columns)
    {
        var result = new List<DTOs.BoardTemplateColumnProposal>();
        if (columns is null)
        {
            return result;
        }

        // Duplicates are DROPPED, not renamed: two lanes with the same name make `columnName` on a task
        // ambiguous, and a renamed lane would contradict the name the tasks were told to use.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var column in columns)
        {
            var name = TrimToNull(column?.Name);
            if (name is null
                || name.Length > BoardTemplatePrompts.MaxColumnNameLength
                || !seen.Add(name))
            {
                continue;
            }

            result.Add(new DTOs.BoardTemplateColumnProposal(name, column!.IsDone == true));

            if (result.Count == BoardTemplatePrompts.MaxColumns)
            {
                break;
            }
        }

        return PinSingleDoneColumn(result);
    }

    /// <summary>
    /// Guarantees exactly one done lane (decision D13). The model's own choice wins when it made one;
    /// otherwise the <b>last</b> column is promoted, because that is where a workflow ends. Two choices
    /// collapse to the first so the proposal is never ambiguous.
    /// </summary>
    public static List<DTOs.BoardTemplateColumnProposal> PinSingleDoneColumn(
        IReadOnlyList<DTOs.BoardTemplateColumnProposal> columns)
    {
        if (columns.Count == 0)
        {
            return [];
        }

        var doneIndex = -1;

        for (var i = 0; i < columns.Count; i++)
        {
            if (!columns[i].IsDone)
            {
                continue;
            }

            if (doneIndex < 0)
            {
                doneIndex = i;
            }
        }

        // No column marked, or several: fall back to the LAST one — the end of the workflow.
        if (doneIndex < 0)
        {
            doneIndex = columns.Count - 1;
        }

        return columns
            .Select((column, index) => column with { IsDone = index == doneIndex })
            .ToList();
    }

    /// <summary>
    /// Tasks: the title is mandatory and bounded, the column is matched by name, and priority/labels/
    /// assignee go through the Phase 3 resolution helpers.
    /// </summary>
    public static List<DTOs.BoardTemplateTaskProposal> NormalizeTasks(
        IReadOnlyList<AiBoardTemplateTask>? tasks,
        IReadOnlyList<DTOs.BoardTemplateColumnProposal> columns,
        IReadOnlyList<BoardDtos.WorkspaceMemberResponse> members,
        IReadOnlyList<WorkspaceLabel> existingLabels,
        int maxTasks)
    {
        var result = new List<DTOs.BoardTemplateTaskProposal>();
        if (tasks is null || maxTasks <= 0 || columns.Count == 0)
        {
            return result;
        }

        // An unknown column name lands here: the first non-done lane, i.e. where new work starts.
        var fallbackColumn = columns.FirstOrDefault(c => !c.IsDone)?.Name ?? columns[0].Name;

        foreach (var task in tasks)
        {
            if (result.Count == maxTasks)
            {
                break;
            }

            var title = TrimToNull(task?.Title);
            if (title is null || title.Length > SmartSetupService.MaxTitleLength)
            {
                continue;
            }

            var requested = TrimToNull(task!.ColumnName);
            var columnName = requested is not null
                             && columns.Any(c => string.Equals(c.Name, requested, StringComparison.OrdinalIgnoreCase))
                ? columns.First(c => string.Equals(c.Name, requested, StringComparison.OrdinalIgnoreCase)).Name
                : fallbackColumn;

            result.Add(new DTOs.BoardTemplateTaskProposal(
                title,
                CapLength(TrimToNull(task.Description), SmartSetupService.MaxTaskDescriptionLength),
                SmartSetupService.NormalizePriority(task.Priority),
                columnName,
                SmartSetupService.ResolveLabels(task.Labels, existingLabels),
                SmartSetupService.ResolveAssignee(task.SuggestedAssignee, members)));
        }

        return result;
    }

    /// <summary>
    /// Board name: the model's own name when usable, otherwise one derived from the description. Never
    /// blank — <c>boards.name</c> is required and a nameless board cannot be found again.
    /// </summary>
    public static string NormalizeBoardName(string? proposed, string description)
    {
        var name = TrimToNull(proposed);
        if (name is not null && name.Length <= BoardTemplatePrompts.MaxBoardNameLength)
        {
            return name;
        }

        if (name is not null)
        {
            return name[..BoardTemplatePrompts.MaxBoardNameLength];
        }

        // `CapLength` is nullable by contract (it passes null through); an empty or null description must
        // still produce a usable board name, so the result is coalesced rather than assumed non-null.
        var excerpt = CapLength(TrimToNull(description) ?? string.Empty, FallbackNameExcerptLength)
                      ?? string.Empty;

        return excerpt.Length == 0 ? "Bảng mới" : $"Bảng: {excerpt}";
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static string? CapLength(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
