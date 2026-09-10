namespace TeamNexus.Modules.Reporting.Contracts;

/// <summary>
/// Một lựa chọn board cho bộ lọc báo cáo (Phase 6 §3). Domain type: loader trả type này, tầng HTTP (§5)
/// map sang <c>ReportBoardOptionResponse</c> — cùng nguyên tắc tách domain/DTO như module Ai.
/// </summary>
public sealed record ReportBoardOption(Guid Id, string Name, int TaskCount, int IsDoneColumns);
