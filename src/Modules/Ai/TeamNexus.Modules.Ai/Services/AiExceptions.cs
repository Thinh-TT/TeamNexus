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
