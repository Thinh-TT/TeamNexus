using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Gọi provider AI thất bại: HTTP ≠ 2xx, timeout, nội dung rỗng, hoặc phản hồi JSON sai →
/// <b>502 Bad Gateway</b> (Phase 3 §2.4). Kế thừa <see cref="BoardModuleException"/> để tái sử
/// dụng <c>DomainExceptionFilter</c> của module Board — filter đã map exception này thành
/// <c>{ error }</c> + status tương ứng, nên module Ai không cần filter riêng.
/// </summary>
public sealed class AiProviderException : BoardModuleException
{
    public AiProviderException(string message)
        : base(StatusCodes.Status502BadGateway, message)
    {
    }
}

/// <summary>
/// AI Agent Executor đang tắt (<c>Agent:Enabled = false</c>) → <b>503 Service Unavailable</b>
/// (Phase 7 D18), đúng tiền lệ <c>ReportingDisabledException</c>. Chỉ áp cho 3 route ghi
/// (chạy / chạy lại / huỷ): route đọc vẫn phục vụ lịch sử run đã có.
/// </summary>
public sealed class AgentDisabledException : BoardModuleException
{
    public AgentDisabledException()
        : base(StatusCodes.Status503ServiceUnavailable, "AI Agent Executor is disabled.")
    {
    }
}

/// <summary>
/// AI Task Chat đang tắt (<c>AiChat:Enabled = false</c>) → <b>503 Service Unavailable</b>
/// (Phase 14 §2.2, decision D9), cùng khuôn mẫu <see cref="AgentDisabledException"/>.
/// <para>
/// Ném <b>trước</b> mọi truy vấn: một tính năng đang tắt không được tiết lộ task nào tồn tại, và
/// cũng không được tiêu token nào.
/// </para>
/// </summary>
public sealed class AiChatDisabledException : BoardModuleException
{
    public AiChatDisabledException()
        : base(StatusCodes.Status503ServiceUnavailable, "AI Task Chat is disabled.")
    {
    }
}
