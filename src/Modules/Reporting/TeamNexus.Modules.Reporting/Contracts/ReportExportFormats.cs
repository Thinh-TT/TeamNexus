using TeamNexus.Modules.Reporting.Options;

namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Định dạng xuất báo cáo (Phase 6 §0 D12). Text tự do, so khớp **không phân biệt hoa thường** và trim ở
/// service; giá trị khác ⇒ 400 (không đoán định dạng).
/// </summary>
public static class ReportExportFormats
{
    public const string Pdf = "pdf";
    public const string Excel = "excel";

    /// <summary>Chuẩn hoá tham số <c>format</c>; trả <c>null</c> khi không phải định dạng được hỗ trợ.</summary>
    public static string? Normalize(string? format) => format?.Trim().ToLowerInvariant() switch
    {
        Pdf => Pdf,
        Excel => Excel,
        _ => null,
    };

    /// <summary>Content-Type tương ứng (dùng ở §5; đặt cạnh định dạng để hai nơi không lệch nhau).</summary>
    public static string ContentType(string format) => format switch
    {
        Pdf => "application/pdf",
        Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream",
    };

    /// <summary>Phần mở rộng file tương ứng.</summary>
    public static string FileExtension(string format) => format switch
    {
        Pdf => "pdf",
        Excel => "xlsx",
        _ => "bin",
    };
}
