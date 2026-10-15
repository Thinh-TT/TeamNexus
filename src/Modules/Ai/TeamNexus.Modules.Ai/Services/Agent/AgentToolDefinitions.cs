using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// The agent's tool whitelist (Phase 7 §4.4: <b>four</b> tools; Giai đoạn 14.7 Track B §B2 adds
/// <b>three</b> more), hand-written JSON Schemas.
/// <para>
/// Written by hand with <see cref="JsonSerializer"/> over object literals on purpose — reflection
/// over C# types would produce a schema whose property names/required list depends on compiler
/// details, and the model contract has to be reviewable text.
/// </para>
/// <para>
/// <b>Adding a tool does not widen any guardrail.</b> The new tools spend the same
/// <c>Agent:MaxToolCalls</c> budget, the same run timeout and the same token ceiling as the original four —
/// the registry counts calls, it does not multiply budgets. A test asserts this, because "one more tool"
/// is exactly the change that would otherwise quietly raise what a run may cost.
/// </para>
/// </summary>
public static class AgentToolDefinitions
{
    public const string SearchSystemDataName = "SearchSystemData";

    public const string WebSearchName = "WebSearch";

    public const string DraftOutputName = "DraftOutput";

    public const string RequestClarificationName = "RequestClarification";

    /// <summary>Giai đoạn 14.7 Track B §B2 — current load of one workspace member.</summary>
    public const string InspectMemberWorkloadName = "InspectMemberWorkload";

    /// <summary>Giai đoạn 14.7 Track B §B2 — rank workspace members by skill-slug coverage.</summary>
    public const string MatchSkillsName = "MatchSkills";

    /// <summary>Giai đoạn 14.7 Track B §B2 — ask Jev whether a draft satisfies the task's description.</summary>
    public const string ValidateAcceptanceCriteriaName = "ValidateAcceptanceCriteria";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Valid <c>SearchSystemData.scope</c> values.</summary>
    public static IReadOnlyList<string> SearchScopes { get; } = ["task", "board", "workspace"];

    /// <summary>How many members <c>MatchSkills</c> may return. Also the schema's upper bound.</summary>
    public const int MaxSkillMatches = 10;

    /// <summary>Longest single skill slug accepted by <c>MatchSkills</c>.</summary>
    public const int MaxSkillSlugLength = 40;

    /// <summary>Upper bound on the draft <c>ValidateAcceptanceCriteria</c> will submit to Jev.</summary>
    public const int MaxValidationDraftChars = 6_000;

    /// <summary>Every tool advertised to the model, in the order they are described in the prompt.</summary>
    public static IReadOnlyList<AiToolDefinition> All { get; } =
    [
        new(
            SearchSystemDataName,
            "Đọc dữ liệu nội bộ của workspace hiện tại (task, cột, người thực hiện, comment gần nhất). "
            + "Chỉ đọc được workspace của task đang chạy; không truyền được id của workspace/board/task khác.",
            Schema(new
            {
                scope = Property("string", "Phạm vi đọc dữ liệu.", ["task", "board", "workspace"]),
                query = Property("string", "Từ khoá lọc theo tiêu đề task (tuỳ chọn)."),
                limit = Property("integer", "Số task tối đa trả về (1-20, mặc định 10).", minimum: 1, maximum: 20),
            }, ["scope"])),

        new(
            WebSearchName,
            "Tìm kiếm thông tin trên web để tham khảo. Không dùng để đọc dữ liệu nội bộ.",
            Schema(new
            {
                query = Property("string", "Truy vấn tìm kiếm."),
                maxResults = Property("integer", "Số kết quả tối đa (1-5, mặc định 3).", minimum: 1, maximum: 5),
            }, ["query"])),

        new(
            InspectMemberWorkloadName,
            "Xem khối lượng công việc hiện tại của MỘT thành viên trong workspace: số thẻ đang mở, số thẻ "
            + "quá hạn, số thẻ đang làm, cùng hạn mức WIP và hệ số tải của họ. Dùng khi cần biết ai còn "
            + "nhận được việc.",
            Schema(new
            {
                userId = Property("string", "Id người dùng (GUID) — lấy từ danh sách thành viên trong ngữ cảnh."),
            }, ["userId"])),

        new(
            MatchSkillsName,
            "Tìm thành viên trong workspace có kỹ năng khớp nhất với một tập kỹ năng cần thiết. "
            + "Dùng trước khi đề xuất người phụ trách.",
            Schema(new
            {
                skills = ArrayProperty(
                    "string",
                    "Các slug kỹ năng cần có (ví dụ: csharp, postgresql, react). Ít nhất một.",
                    maxItems: 20),
                limit = Property(
                    "integer",
                    $"Số thành viên trả về (1-{MaxSkillMatches}, mặc định 5).",
                    minimum: 1,
                    maximum: MaxSkillMatches),
            }, ["skills"])),

        new(
            ValidateAcceptanceCriteriaName,
            "Hỏi mô hình quyết định (Jev) xem bản nháp kết quả có thật sự đáp ứng mô tả của task không. "
            + "Trả về mức độ chắc chắn; nếu Jev đang tắt thì trả về 'chưa xác minh được' — KHÔNG dùng tool "
            + "này để kết thúc lượt chạy.",
            Schema(new
            {
                draft = Property("string", "Nội dung nháp cần kiểm tra."),
                criteria = Property("string", "Tiêu chí nghiệm thu cụ thể (tuỳ chọn)."),
            }, ["draft"])),

        new(
            DraftOutputName,
            "Nộp kết quả cuối cùng cho task: nội dung ngắn sẽ thành bình luận, nội dung dài hoặc có "
            + "fileName/contentType sẽ thành tệp đính kèm. Gọi tool này để KẾT THÚC lượt chạy.",
            Schema(new
            {
                content = Property("string", "Nội dung kết quả (markdown hoặc văn bản thuần)."),
                fileName = Property("string", "Tên tệp (chỉ khi kết quả là tệp), ví dụ \"bao-cao.md\"."),
                contentType = Property("string", "MIME type của tệp, ví dụ \"text/markdown\" hoặc \"text/csv\"."),
            }, ["content"])),

        new(
            RequestClarificationName,
            "Hỏi lại trưởng nhóm khi thiếu thông tin để hoàn thành task. Chỉ dùng khi thật sự không thể "
            + "suy luận được; đây là cách KẾT THÚC lượt chạy thứ hai.",
            Schema(new
            {
                question = Property("string", "Câu hỏi cụ thể cần trưởng nhóm trả lời (tối đa 2000 ký tự)."),
                reason = Property("string", "Vì sao cần hỏi (tuỳ chọn)."),
            }, ["question"])),
    ];

    /// <summary>True when <paramref name="name"/> is inside the whitelist (case-insensitive).</summary>
    public static bool IsWhitelisted(string? name)
        => All.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Schema(object properties, string[] required)
        => JsonSerializer.Serialize(new
        {
            type = "object",
            properties,
            required,
            additionalProperties = false,
        }, Json);

    /// <summary>One JSON-Schema property; null entries are omitted so the schema stays readable for the model.</summary>
    private static Dictionary<string, object?> Property(
        string type, string description, string[]? @enum = null, int? minimum = null, int? maximum = null)
    {
        var property = new Dictionary<string, object?>
        {
            ["type"] = type,
            ["description"] = description,
        };

        if (@enum is not null)
        {
            property["enum"] = @enum;
        }

        if (minimum.HasValue)
        {
            property["minimum"] = minimum.Value;
        }

        if (maximum.HasValue)
        {
            property["maximum"] = maximum.Value;
        }

        return property;
    }

    /// <summary>
    /// One JSON-Schema <b>array</b> property plus its <c>items</c> schema. Separate from
    /// <see cref="Property"/> because an array without an <c>items</c> entry is not a usable contract — the
    /// model would be free to send objects, and every consumer would have to defend against it.
    /// </summary>
    private static Dictionary<string, object?> ArrayProperty(
        string itemType, string description, int? maxItems = null)
    {
        var property = new Dictionary<string, object?>
        {
            ["type"] = "array",
            ["description"] = description,
            ["items"] = new Dictionary<string, object?> { ["type"] = itemType },
        };

        if (maxItems.HasValue)
        {
            property["maxItems"] = maxItems.Value;
        }

        return property;
    }
}
