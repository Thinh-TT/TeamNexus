using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// The agent's tool whitelist (Phase 7 §4.4): <b>exactly four</b> tools, hand-written JSON Schemas.
/// <para>
/// Written by hand with <see cref="JsonSerializer"/> over object literals on purpose — reflection
/// over C# types would produce a schema whose property names/required list depends on compiler
/// details, and the model contract has to be reviewable text.
/// </para>
/// </summary>
public static class AgentToolDefinitions
{
    public const string SearchSystemDataName = "SearchSystemData";

    public const string WebSearchName = "WebSearch";

    public const string DraftOutputName = "DraftOutput";

    public const string RequestClarificationName = "RequestClarification";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Valid <c>SearchSystemData.scope</c> values.</summary>
    public static IReadOnlyList<string> SearchScopes { get; } = ["task", "board", "workspace"];

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
}
