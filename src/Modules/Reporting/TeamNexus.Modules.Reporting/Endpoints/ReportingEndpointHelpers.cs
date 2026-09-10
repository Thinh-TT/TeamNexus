using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Modules.Reporting.Services;

namespace TeamNexus.Modules.Reporting.Endpoints;

/// <summary>
/// Helper của tầng endpoint Reporting (Phase 6 §5.2). Bản copy của <c>AiEndpointHelpers</c> vì helper của
/// module Board/Ai đều là <c>internal</c> nên không tái dùng chéo module được (cùng lý do đã ghi ở §3).
/// </summary>
internal static class ReportingEndpointHelpers
{
    /// <summary>
    /// Đọc user id từ claim <see cref="ClaimTypes.NameIdentifier"/> của JWT. Thiếu/không hợp lệ ⇒
    /// <see cref="UnauthorizedException"/> để <c>DomainExceptionFilter</c> map thành 401.
    /// </summary>
    public static Guid RequireUserId(this HttpContext http)
    {
        var value = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        throw new UnauthorizedException();
    }

    /// <summary>
    /// Parse <c>from</c>/<c>to</c> **strict ISO-8601**: rỗng/thiếu ⇒ <c>null</c> (service tự áp mặc định),
    /// giá trị sai ⇒ <b>400</b> <see cref="InvalidReportParameterException"/>.
    /// <para>
    /// <c>AssumeUniversal | AdjustToUniversal</c>: chuỗi không có offset được coi là UTC và mọi giá trị
    /// được quy về UTC trước khi so sánh với <c>timestamptz</c>, nên client gửi
    /// <c>2026-08-11T00:00:00+07:00</c> vẫn so sánh đúng.
    /// </para>
    /// </summary>
    public static DateTimeOffset? ParseIso8601(string? value, string parameterName)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        throw new InvalidReportParameterException(parameterName, trimmed);
    }
}
