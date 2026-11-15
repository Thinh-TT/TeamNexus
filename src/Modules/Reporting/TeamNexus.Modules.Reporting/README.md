# TeamNexus.Modules.Reporting — Phân hệ Phân tích Tiến độ & Xuất Báo cáo

Phân hệ chịu trách nhiệm tổng hợp số liệu **tiến độ (Progress)**, **hiệu suất (Performance)**, **chuỗi thời gian Burndown / Velocity (Progress Series)**, **hoạt động (Activity)** và **sức khỏe AI (AI Health)** của một Workspace (hỗ trợ lọc theo từng Board), đồng thời kết xuất báo cáo **PDF (QuestPDF)** và **Excel (ClosedXML)** trực tiếp trên bộ nhớ RAM theo yêu cầu (on-demand).

---

## 1. Điểm cuối HTTP (Endpoints)

Nhóm đường dẫn `/api/workspaces/{workspaceId}/reports` (tag OpenAPI `Reports`). Tất cả đều là truy vấn chỉ đọc (`GET`):

| Phương thức & Đường dẫn | Tham số Query | Phân quyền | Mô tả chức năng |
|---|---|---|---|
| `GET /summary` | `boardId?`, `from?`, `to?` (ISO-8601) | `Manager` / `Admin` | Trả về `ReportSummaryResponse` tổng hợp 22 chỉ số tiến độ, hiệu suất, theo bảng, theo thành viên, hoạt động và tín hiệu AI Observer |
| `GET /export` | `format=pdf\|excel`, `boardId?`, `from?`, `to?` | `Manager` / `Admin` | Kết xuất tệp PDF hoặc Excel `.xlsx` kèm header `Content-Disposition`, `X-Report-Row-Cap-Reached`, `X-Report-Rows`, `X-Report-Period` |
| `GET /boards` | — | `Manager` / `Admin` | Trả về danh sách `ReportBoardOptionResponse[]` để hiển thị bộ lọc bảng trên trang Báo cáo |
| `GET /progress-series` | `boardId?`, `from?`, `to?`, `bucket=date\|week`, `tzOffsetMinutes?` | `Member+` | Trả về chuỗi thời gian `ProgressSeriesResponse` (`buckets[]` với `created`, `completed`, `remainingOpen`, `overdueSnapshot`) phục vụ biểu đồ **Burndown & Velocity** |

---

## 2. Kiến trúc & Nguyên tắc Thiết kế

1. **Độc lập và Không Trạng thái (Read-Only & Stateless):**
   - Phân hệ `Reporting` không sở hữu bảng riêng và không thực hiện bất kỳ thao tác ghi dữ liệu nào (`SaveChangesAsync` = `0`).
   - Chỉ tham chiếu `TeamNexus.Shared`, `TeamNexus.Persistence` và `TeamNexus.Modules.Board` (tuyệt đối không tham chiếu `TeamNexus.Modules.Ai`).
2. **Hàm Thuần Tất định (`ReportAggregator`):**
   - `ReportAggregator.Build(snapshot, thresholds)` và `ReportAggregator.BuildProgressSeries(...)` là các **hàm thuần (pure functions)** không chạm CSDL, không gọi mạng và không phụ thuộc `DateTime.Now`.
   - `ReportSummary` là nguồn số liệu duy nhất dùng chung cho cả JSON API, trình kết xuất PDF và trình kết xuất Excel, đảm bảo số liệu khớp nhau 100% giữa mọi định dạng.
3. **Kết xuất PDF & Excel Trực tiếp trên RAM:**
   - `ReportPdfRenderer` (QuestPDF Community License): Tạo tài liệu PDF đa trang gồm trang bìa, thanh tiến độ, bảng hiệu suất kèm định nghĩa chỉ số, thống kê theo bảng/thành viên/hoạt động và sức khỏe AI.
   - `ReportExcelRenderer` (ClosedXML): Tạo sổ làm việc `.xlsx` gồm 5 trang tính (`Tổng quan`, `Theo bảng`, `Theo người`, `Hoạt động`, `Sức khoẻ AI`) tích hợp thanh dữ liệu điều kiện (Data Bars), bộ lọc tự động (AutoFilter) và cố định dòng tiêu đề (Freeze Panes).
   - Tên tệp tải về được chuẩn hóa ASCII (`ReportFileName`) theo mẫu `teamnexus-report-<slug>-<yyyyMMdd-HHmm>.<ext>`.

---

## 3. Bảng Công thức Chỉ số Cốt lõi

| Chỉ số | Công thức tính | Quy tắc biên |
|---|---|---|
| `progress.total` | Tổng số thẻ chưa xóa mềm trong phạm vi | Bộ lọc toàn cục tự động loại `deleted_at != null` |
| `progress.done` | Thẻ thuộc cột `is_done = true` **hoặc** có `completed_at != null` | Đảm bảo tương thích ngược với dữ liệu lịch sử |
| `progress.open` | `total − done` | — |
| `progress.overdue` | `!is_done && due_date != null && due_date < Now` | Có thể cấu hình qua `Reports:ExcludeDoneOverdue` |
| `progress.donePercent` | `done / total × 100` (làm tròn 1 chữ số thập phân) | `total = 0` → `0.0` |
| `performance.createdInRange` | Số thẻ có `created_at ∈ [From, To]` | — |
| `performance.completedInRange` | Số thẻ có `completed_at ∈ [From, To]` | — |
| `performance.avgCompletionHours` | Trung bình `completed_at − created_at` (giờ) | Mẫu bằng `0` → `null` |
| `performance.onTimeRate` | Tỷ lệ `%` thẻ hoàn thành có `completed_at ≤ due_date` | Thẻ không có `due_date` không tính vào mẫu |
| `performance.throughputPerWeek` | `completedInRange / (Days / 7.0)` | — |
| `progressSeries.buckets[]` | Chuỗi thời gian theo ngày (`date`, tối đa 60 ngày) hoặc tuần (`week`, bắt đầu từ Thứ Hai) | Tính toán chính xác theo múi giờ địa phương `tzOffsetMinutes` (`[-840..840]`) |

---

## 4. Cấu hình (`Reports` trong `appsettings.json`)

| Khóa cấu hình | Giá trị mặc định | Ý nghĩa |
|---|---|---|
| `Reports:Enabled` | `true` | Đặt `false` sẽ trả về `503 Service Unavailable` trên mọi điểm cuối báo cáo |
| `Reports:DefaultRangeDays` | `30` | Khoảng thời gian mặc định khi không truyền `from` / `to` |
| `Reports:MaxRangeDays` | `365` | Giới hạn cửa sổ thời gian tối đa (vượt ngưỡng tự động cắt và gắn cờ `clamped = true`) |
| `Reports:MaxExportRows` | `5000` | Giới hạn số dòng chi tiết tối đa trong báo cáo |
| `Reports:MaxActionsPerReport` | `200` | Giới hạn số dòng thống kê loại hành động |
| `Reports:PdfPageSize` | `"A4"` | Khổ giấy PDF (`A4`, `A3`, `Letter`) |
| `Reports:CompanyName` | `"TeamNexus"` | Tên đơn vị hiển thị ở tiêu đề/chân trang PDF và trang tính Tổng quan Excel |
