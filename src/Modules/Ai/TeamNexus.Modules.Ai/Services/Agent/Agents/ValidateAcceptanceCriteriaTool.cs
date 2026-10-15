using System.Text.Json;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>ValidateAcceptanceCriteria</c> (Giai đoạn 14.7 Track B §B2): one <c>noul</c> question to Jev asking
/// whether a draft actually satisfies the task.
/// <para>
/// <b>This tool never fails the run.</b> Jev is optional assistance, so a disabled or unreachable provider
/// produces a normal result reading "không xác minh được" rather than an error. The alternative — throwing —
/// would let an optional second opinion kill work the model had already completed, which is precisely
/// backwards.
/// </para>
/// <para>
/// <b>Only the draft and the acceptance criteria cross the wire.</b> No ids, no board or member data, and the
/// draft is truncated before the request is built: Jev bills input tokens, and a 400 KB draft would be billed
/// in full while adding no decision signal past the first few thousand characters.
/// </para>
/// <para>
/// <b>It is not a way to finish.</b> The description says so explicitly because a model that reads a
/// validation result as "approved" might stop without calling <c>DraftOutput</c> or
/// <c>RequestClarification</c> — and a run that ends without either is a run with no output.
/// </para>
/// </summary>
public sealed class ValidateAcceptanceCriteriaTool : IAgentTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The prompt shown to the model. Phrased as a question about the DRAFT, not as a verdict on the task:
    /// Jev answers a probability, and the caller decides what to do with it.
    /// </summary>
    private const string QuestionInstruction =
        "Bản nháp kết quả dưới đây đã đáp ứng đầy đủ mô tả và tiêu chí nghiệm thu của task chưa?";

    private readonly IJevDecisionService _jev;
    private readonly JevOptions _options;

    public ValidateAcceptanceCriteriaTool(
        IJevDecisionService jev,
        Microsoft.Extensions.Options.IOptions<JevOptions> options)
    {
        _jev = jev;
        _options = options.Value;
    }

    public string Name => AgentToolDefinitions.ValidateAcceptanceCriteriaName;

    public async Task<string> ExecuteAsync(
        JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var draft = ToolArguments.GetTrimmedString(arguments, "draft");

        if (draft is null)
        {
            return AgentToolRegistry.Error("'draft' is required and must not be empty.");
        }

        var criteria = ToolArguments.GetTrimmedString(arguments, "criteria");

        // Checked BEFORE the call, for the same reason the triage endpoint does it before the provider: a
        // disabled feature must not spend a token to learn it is disabled.
        if (!_options.Enabled)
        {
            return NotVerified("Jev đang tắt (Jev:Enabled=false).");
        }

        var capped = draft.Length <= AgentToolDefinitions.MaxValidationDraftChars
            ? draft
            : draft[..AgentToolDefinitions.MaxValidationDraftChars];

        var state = JsonSerializer.Serialize(new
        {
            draft = capped,
            acceptanceCriteria = criteria,
            note = "Đánh giá xem bản nháp có thoả tiêu chí nghiệm thu hay chưa.",
        }, JevJsonOptions);

        var result = await _jev.ValidateAcceptanceCriteriaAsync(state, ct);

        if (result is null)
        {
            // The service degrades to `null` for a provider failure (it logs the reason); this tool turns that
            // into an honest, non-fatal answer instead of an error object the model has to interpret.
            return NotVerified("Không gọi được mô hình quyết định (nhà cung cấp lỗi hoặc quá hạn).");
        }

        return JsonSerializer.Serialize(new
        {
            verified = result.Verified,
            confidence = result.ConfidenceScore,
            routingAdvice = result.RoutingAdvice,
            // Said out loud so the model cannot read a low-confidence "false" as a hard rejection of work it
            // could still submit: the verdict is advice, and a human still approves the output.
            note = "Đây là ý kiến tham khảo của Jev (System 1), không phải phê duyệt tự động. "
                + "Hãy tự quyết định có sửa bản nháp hay không, rồi kết thúc lượt chạy bằng DraftOutput "
                + "hoặc RequestClarification.",
        }, Json);
    }

    /// <summary>
    /// The degraded answer. <c>verified</c> is <b>null</b> — not <c>false</c> — because "we could not check"
    /// and "we checked and it fails" are different facts, and collapsing them would make the agent rework a
    /// correct draft.
    /// </summary>
    private static string NotVerified(string reason)
        => JsonSerializer.Serialize(new
        {
            verified = (bool?)null,
            confidence = (double?)null,
            routingAdvice = ConfidenceRouting.ManualRequired,
            note = $"Không xác minh được: {reason}",
        }, Json);

    /// <summary>camelCase, nulls omitted — the same shaping <c>JevDecisionService</c> uses for its state.</summary>
    private static readonly JsonSerializerOptions JevJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
