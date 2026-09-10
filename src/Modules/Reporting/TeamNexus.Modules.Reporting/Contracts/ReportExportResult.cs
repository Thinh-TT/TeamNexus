namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Kết quả một lần xuất báo cáo (Phase 6 §3): nội dung file **trong RAM**, kèm metadata để tầng HTTP (§5)
/// dựng header <c>Content-Disposition</c>/<c>Content-Type</c> mà không phải biết định dạng.
/// <para>
/// Không có đường nào ghi file ra disk/blob: <see cref="Content"/> được trả thẳng qua
/// <c>Results.File(...)</c> (Phase 6 §0 D11 — file generate on-demand, không lưu trữ).
/// </para>
/// </summary>
public sealed record ReportExportResult(
    byte[] Content,
    string FileName,
    string ContentType,
    int Rows,
    bool RowCapReached,
    string PeriodLabel,
    string ScopeLabel);
