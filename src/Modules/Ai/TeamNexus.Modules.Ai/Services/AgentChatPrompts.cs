using System.Text;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Prompt contract for the board-scoped AI Agent Chat (Phase 14.5 §B, personas added in Giai đoạn 14.7
/// Track B §B1).
/// <para>
/// Two things separate this from <see cref="AiChatPrompts"/> (the task chat), and both are on purpose:
/// the model may see <b>several</b> attached tasks plus the board's columns, and it is explicitly
/// allowed to <b>propose</b> a write — but never to claim it performed one. The proposal is data; a human
/// turns it into a <c>Pending</c> action.
/// </para>
/// <para>
/// <see cref="Marker"/> doubles as the branch selector for the offline <c>FakeAiProvider</c>, exactly
/// like <c>AiChatPrompts.Marker</c>. The two markers must stay distinct: the fake answers a task-chat
/// prompt and a board-chat prompt differently, and a shared marker would make the offline suite test the
/// wrong branch.
/// </para>
/// </summary>
public static class AgentChatPrompts
{
    /// <summary>
    /// Identifying fragment of the system prompt. The fake provider recognizes it and the real one simply
    /// carries it as text, so the two can never disagree.
    /// <para>
    /// <b>⛔ This string must survive byte-for-byte.</b> It is the marker <c>FakeAiProvider.StreamAsync</c>
    /// branches on, so changing or removing it would make the offline suite test a different code path than
    /// production — a green run that proves nothing. The persona block is therefore <b>prepended</b>, never
    /// substituted for this text.
    /// </para>
    /// </summary>
    public const string Marker = "AI Agent Chat của TeamNexus";

    /// <summary>Heading of the persona block prepended when a persona other than the default is used.</summary>
    public const string PersonaHeading = "VAI TRÒ:";

    /// <summary>Fence the model is told to use for the proposal block; the parser looks for the last one.</summary>
    public const string ProposalFence = "```json";

    /// <summary>
    /// System prompt template. <c>{0}</c> is the context block from <see cref="BuildContextBlock"/>.
    /// Every rule that matters is stated here rather than in code comments, because this text <i>is</i>
    /// the contract with the model.
    /// </summary>
    public const string SystemPromptTemplate =
        """
        Bạn là AI Agent Chat của TeamNexus. Bạn trò chuyện với một thành viên trong nhóm về MỘT BẢNG
        KANBAN: nhiều thẻ cùng lúc, cấu trúc cột của bảng, và các thẻ mà người dùng đã kéo vào cuộc trò
        chuyện. Toàn bộ hiểu biết của bạn về dự án nằm trong ngữ cảnh dưới đây.

        NGỮ CẢNH BẢNG:
        {0}

        QUY TẮC BẮT BUỘC:
        1. Chỉ dùng thông tin trong ngữ cảnh trên. TUYỆT ĐỐI không bịa thẻ, người, cột, hạn chót hay số
           liệu không có trong đó. Nếu thiếu thông tin, hãy nói rõ bạn cần thêm gì.
        2. Bạn CHƯA thực hiện bất kỳ thay đổi nào. Không được nói "đã tạo", "đã sửa", "đã chuyển",
           "đã xoá". Bạn chỉ được ĐỀ XUẤT; con người sẽ duyệt (Accountability Layer) trước khi dữ liệu
           thay đổi thật.
        3. Trả lời trực tiếp, ngắn gọn, ưu tiên gạch đầu dòng khi liệt kê.
        4. Giữ nguyên ngôn ngữ tiếng Việt có dấu.
        5. Không dùng HTML; chỉ văn bản thuần (có thể xuống dòng).
        6. TUYỆT ĐỐI KHÔNG hiển thị raw ID, GUID hoặc UUID trong phần văn bản trò chuyện. Chỉ gọi tên
           hiển thị của thẻ và của cột bằng ngôn ngữ tự nhiên (ví dụ: "cột Đang làm"), tuyệt đối không
           viết kèm mã kỹ thuật kiểu "(id: <mã-cột>)". Các ID kỹ thuật chỉ được xuất hiện bên trong
           khối JSON đề xuất cuối cùng.
        7. Khi danh sách thẻ đang gắn rỗng, ngữ cảnh vẫn có phần "TỔNG QUAN BẢNG" (số thẻ mỗi cột và
           các thẻ quan trọng nhất). Hãy dùng phần đó để trả lời câu hỏi về tiến độ chung của bảng;
           không được nói rằng bạn không có dữ liệu khi phần đó tồn tại.

        NẾU BẠN MUỐN ĐỀ XUẤT MỘT THAY ĐỔI:
        - Viết phần giải thích bằng tiếng Việt trước, rồi kết thúc câu trả lời bằng ĐÚNG MỘT khối mã
          ```json ... ``` chứa đúng cấu trúc sau và không thêm gì sau khối đó:
        {
          "proposal": {
            "kind": "CreateSubtasks" | "UpdateTaskFields" | "MoveTaskToColumn",
            "summary": "lý do ngắn gọn bằng tiếng Việt, tối đa 300 ký tự",
            "payload": { ... xem bên dưới ... }
          }
        }
        - Chỉ được đề xuất ĐÚNG MỘT thay đổi cho mỗi câu trả lời.
        - Chỉ được thao tác trên các thẻ CÓ TRONG ngữ cảnh (danh sách thẻ đang gắn). Không được nhắc tới
          thẻ khác.
        - "columnId" bắt buộc phải là một id trong danh sách cột ở trên.
        - "CreateSubtasks": payload = { "tasks": [ { "title": "...", "description": "..." | null,
          "columnId": "...", "priority": "Low" | "Medium" | "High" | "Urgent" | null,
          "assigneeId": "..." | null } ] }
        - "UpdateTaskFields": payload = { "taskId": "...", "title": "..." | null,
          "description": "..." | null, "priority": "..." | null, "dueDate": "ISO-8601" | null,
          "assigneeId": "..." | null }
        - "MoveTaskToColumn": payload = { "taskId": "...", "columnId": "..." }
        - Nếu bạn KHÔNG đề xuất thay đổi nào, chỉ cần trả lời bằng văn bản và KHÔNG tạo khối json.
        """;

    /// <summary>The rendered system prompt for one board context.</summary>
    public static string BuildSystemPrompt(string contextBlock)
        => BuildSystemPrompt(contextBlock, PersonaCatalog.GeneralKey);

    /// <summary>
    /// The rendered system prompt for one board context, as one persona (Giai đoạn 14.7 Track B §B1).
    /// <para>
    /// <b>The persona block is PREPENDED, and nothing else moves.</b> <see cref="Marker"/> still opens the
    /// template body, which is what keeps <c>FakeAiProvider</c> on the board-chat branch; the persona only
    /// adds a sentence at the very top telling the model which co-worker it is answering as. The general
    /// persona adds nothing at all, so the default request is byte-identical to Phase 14.5.
    /// </para>
    /// </summary>
    public static string BuildSystemPrompt(string contextBlock, string personaKey)
    {
        var rendered = SystemPromptTemplate.Replace("{0}", contextBlock, StringComparison.Ordinal);

        var canonical = PersonaCatalog.Canonical(personaKey);

        // No persona (unknown key) or the general persona: the Phase 14.5 prompt, unchanged. Writing an
        // empty block for the default would still be harmless, but identical bytes make "did this phase
        // change the default request?" a question a diff can answer.
        if (canonical is null || string.Equals(canonical, PersonaCatalog.GeneralKey, StringComparison.Ordinal))
        {
            return rendered;
        }

        // Only the ROLE is prepended. The rules (no invented data, no claimed writes, no raw ids) live in
        // the template and therefore apply to every persona — a persona must never be able to loosen them.
        return $"{BuildPersonaBlock(canonical)}\n\n{rendered}";
    }

    /// <summary>
    /// The one-block role statement for a persona. Returns an empty string for the general persona and for an
    /// unknown key: the caller only needs to know whether there is anything to prepend.
    /// </summary>
    public static string BuildPersonaBlock(string? personaKey)
    {
        var persona = PersonaCatalog.Find(personaKey);

        if (persona is null || string.Equals(persona.Key, PersonaCatalog.GeneralKey, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        builder.AppendLine($"{PersonaHeading} {persona.DisplayName}.");

        if (persona.Skills.Count > 0)
        {
            builder.AppendLine($"Chuyên môn của bạn: {string.Join(", ", persona.Skills)}.");
        }

        builder.Append(
            "Bạn trả lời với tư cách chuyên gia ở vai trò trên, nhưng vẫn TUYỆT ĐỐI tuân thủ mọi quy tắc bên dưới "
            + "(không bịa dữ liệu, không tự nhận đã ghi dữ liệu, không hiển thị ID kỹ thuật).");

        return builder.ToString();
    }

    /// <summary>
    /// Human-readable context block fed into <see cref="BuildSystemPrompt"/>. Pure and bounded: the caller
    /// has already truncated every value, so this only shapes text — and therefore needs no database, no
    /// clock and no provider to be verifiable.
    /// </summary>
    public static string BuildContextBlock(AgentChatBoardContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();

        builder.AppendLine($"- Bảng: {context.BoardName}");

        if (!string.IsNullOrWhiteSpace(context.BoardDescription))
        {
            builder.AppendLine($"- Mô tả bảng: {context.BoardDescription}");
        }

        if (context.Columns.Count > 0)
        {
            builder.AppendLine("- Cột (theo thứ tự, id — tên — hoàn thành?):");

            foreach (var column in context.Columns)
            {
                builder.AppendLine($"  * {column.ColumnId} — {column.Name} — {(column.IsDone ? "có" : "không")}");
            }
        }

        // Giai đoạn 14.7 Track B §B3: retrieved project knowledge, placed BEFORE the digest because it is
        // reference material (conventions, a spec, a linked article) while the digest is a snapshot of the
        // board. The block arrives ALREADY rendered and already bounded: the caller owns the character budget
        // (`Jev:RagMaxContextChars`) and this method only positions the text — which keeps the prompt builder
        // pure and keeps the knowledge feature's configuration out of it.
        if (!string.IsNullOrWhiteSpace(context.KnowledgeBlock))
        {
            builder.AppendLine();
            builder.AppendLine(context.KnowledgeBlock.Trim());
        }

        if (context.AttachedTasks.Count == 0)
        {
            builder.AppendLine("- Thẻ đang gắn: (không có thẻ nào — người dùng chỉ hỏi về bảng)");

            // Phase 14.6 §A4: with nothing attached the model used to receive column NAMES and nothing else,
            // so "Sprint 1 tiến độ thế nào?" was answered with an empty refusal even though the board had
            // dozens of cards. The digest is the smallest amount of board state that makes that question
            // answerable, and it is only ever built when there is no attachment list to describe.
            var digest = BuildDigestBlock(context.Digest);

            if (digest.Length > 0)
            {
                builder.AppendLine(digest);
            }
        }
        else
        {
            builder.AppendLine($"- Thẻ đang gắn ({context.AttachedTasks.Count}):");

            foreach (var task in context.AttachedTasks)
            {
                builder.AppendLine($"  * [{task.TaskId}] {task.Title}");
                builder.AppendLine($"    Cột: {task.ColumnName} · Ưu tiên: {task.Priority ?? "(chưa đặt)"}");
                builder.AppendLine(
                    task.DueDate is { } due
                        ? $"    Hạn chót: {due:yyyy-MM-dd HH:mm} (UTC)"
                        : "    Hạn chót: (không có)");
                builder.AppendLine(
                    string.IsNullOrWhiteSpace(task.AssigneeName)
                        ? "    Người phụ trách: (chưa gán)"
                        : $"    Người phụ trách: {task.AssigneeName}");

                if (!string.IsNullOrWhiteSpace(task.Description))
                {
                    builder.AppendLine("    Mô tả:");
                    builder.AppendLine(task.Description);
                }

                if (task.Comments.Count > 0)
                {
                    builder.AppendLine($"    {task.Comments.Count} bình luận gần nhất:");

                    foreach (var comment in task.Comments)
                    {
                        builder.AppendLine(
                            $"      - {comment.AuthorName} ({comment.CreatedAt:yyyy-MM-dd HH:mm} UTC): {comment.Content}");
                    }
                }
            }
        }

        if (context.Members.Count > 0)
        {
            builder.AppendLine("- Thành viên có thể được giao việc (id — tên):");

            foreach (var member in context.Members)
            {
                builder.AppendLine($"  * {member.UserId} — {member.DisplayName}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    // =====================================================================
    // Lightweight Board Digest (Phase 14.6 §A4)
    // =====================================================================

    /// <summary>Heading of the digest section; rule 7 of the system prompt refers to it by this name.</summary>
    public const string DigestHeader = "TỔNG QUAN BẢNG";

    /// <summary>
    /// The line that <b>opens</b> the digest block. Distinct from <see cref="DigestHeader"/> on purpose: rule 7
    /// of the system prompt quotes the heading by name, so only this marker proves the digest itself is
    /// present (a test asserting on the bare heading would match the rule text and pass with the digest gone).
    /// </summary>
    public const string DigestBlockMarker = "- " + DigestHeader + ":";

    /// <summary>
    /// Renders the digest inside <see cref="AgentChatDigest.MaxChars"/> characters, or an empty string when
    /// there is nothing worth saying.
    /// <para>
    /// <b>Why the budget is enforced here and not by the caller.</b> The whole point of a <i>lightweight</i>
    /// digest is that it can never push the prompt past its cost ceiling; a board with 300 cards and 12
    /// columns would otherwise turn "summarize my board" into the most expensive request in the product. The
    /// cut is made line-by-line, so the model never receives half a task title.
    /// </para>
    /// <para>Pure: the caller loads the numbers, this only shapes text, so it needs no database to test.</para>
    /// </summary>
    public static string BuildDigestBlock(AgentChatDigest? digest)
    {
        if (digest is null)
        {
            return string.Empty;
        }

        var budget = Math.Max(0, digest.MaxChars);

        if (budget == 0 || (digest.Columns.Count == 0 && digest.TopTasks.Count == 0))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        // Silently DROPS a line that would not fit, rather than truncating it: the task list is written last,
        // so a budget that runs out costs the model the least important cards, never half of a title.
        void AppendCapped(string line)
        {
            var cost = line.Length + Environment.NewLine.Length;

            if (builder.Length + cost > budget)
            {
                return;
            }

            builder.AppendLine(line);
        }

        AppendCapped(DigestBlockMarker);
        AppendCapped($"  Tổng số: {digest.TotalTasks} thẻ");

        foreach (var column in digest.Columns)
        {
            AppendCapped($"  * {column.Name}: {column.Count}");
        }

        if (digest.TopTasks.Count > 0)
        {
            AppendCapped($"  Thẻ quan trọng nhất ({digest.TopTasks.Count}):");

            foreach (var task in digest.TopTasks)
            {
                var priority = string.IsNullOrWhiteSpace(task.Priority) ? "(chưa đặt)" : task.Priority;
                var due = task.DueDate is { } dueAt ? $"hạn {dueAt:yyyy-MM-dd HH:mm} (UTC)" : "không hạn";
                var assignee = string.IsNullOrWhiteSpace(task.AssigneeName) ? "chưa gán" : task.AssigneeName;
                var columnName = string.IsNullOrWhiteSpace(task.ColumnName) ? "(không rõ cột)" : task.ColumnName;

                AppendCapped($"  * [{priority}] {task.Title} — {columnName} · {due} · {assignee}");
            }
        }

        // TrimEnd before the length check so a block that only ever fitted its first line is still measured
        // honestly rather than being reported one newline longer than it is.
        var text = builder.ToString().TrimEnd();

        return text.Length <= budget ? text : string.Empty;
    }
}

/// <summary>
/// Everything the board-chat prompt knows, already truncated and bounded by the caller. A record rather
/// than entities so <see cref="AgentChatPrompts.BuildContextBlock"/> stays pure.
/// </summary>
/// <param name="KnowledgeBlock">
/// Giai đoạn 14.7 Track B §B3 — the <b>already rendered and already bounded</b> knowledge section retrieved
/// from the workspace's semantic memory, or <c>null</c>/blank when RAG is off or found nothing. Pre-rendered on
/// purpose: the character budget belongs to the caller that owns <c>Jev:RagMaxContextChars</c>, so this record
/// stays configuration-free and the prompt builder stays pure. Optional, so every pre-existing construction
/// site and test compiles unchanged and simply has one less section.
/// </param>
public sealed record AgentChatBoardContext(
    string BoardName,
    string? BoardDescription,
    IReadOnlyList<AgentChatContextColumn> Columns,
    IReadOnlyList<AgentChatContextTask> AttachedTasks,
    IReadOnlyList<AgentChatContextMember> Members,
    AgentChatDigest? Digest = null,
    string? KnowledgeBlock = null);

/// <summary>
/// The "lightweight board digest" (Phase 14.6 §A4): how many cards each column holds plus the handful a
/// manager would name first. Deliberately carried only when nothing is attached — see
/// <see cref="AgentChatPrompts.BuildContextBlock"/>.
/// </summary>
public sealed record AgentChatDigest(
    IReadOnlyList<AgentChatDigestColumn> Columns,
    IReadOnlyList<AgentChatDigestTask> TopTasks,
    int TotalTasks,
    int MaxChars);

/// <summary>One column's card count in the digest.</summary>
public sealed record AgentChatDigestColumn(string Name, int Count);

/// <summary>One "card worth naming first" in the digest. No id: the digest is for answering, not proposing.</summary>
public sealed record AgentChatDigestTask(
    string Title,
    string ColumnName,
    string? Priority,
    DateTimeOffset? DueDate,
    string? AssigneeName);

/// <summary>One board column as the prompt sees it: the model needs the id to propose a move.</summary>
public sealed record AgentChatContextColumn(Guid ColumnId, string Name, bool IsDone);

/// <summary>One attached task as the prompt sees it (all values already truncated).</summary>
public sealed record AgentChatContextTask(
    Guid TaskId,
    string Title,
    string ColumnName,
    string? Description,
    string? Priority,
    DateTimeOffset? DueDate,
    string? AssigneeName,
    IReadOnlyList<AiChatContextComment> Comments);

/// <summary>
/// A member the model may propose as assignee. AI Agent members are deliberately absent: the executor is
/// driven by assigning a task to the agent through the existing UI, not by a chat proposal.
/// </summary>
public sealed record AgentChatContextMember(Guid UserId, string DisplayName);
