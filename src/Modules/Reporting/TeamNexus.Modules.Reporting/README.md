# TeamNexus.Modules.Reporting — Báo cáo & Xuất dữ liệu (Phase 6)

Module Giai đoạn 6: tổng hợp **tiến độ** + **hiệu suất** của một workspace (lọc được theo board) và
**xuất on-demand** ra PDF (QuestPDF) / Excel (ClosedXML). Báo cáo là lớp **chỉ đọc**.

> **Trạng thái:** §1 (aggregation thuần) **đã xong & verify** — 76 check PASS bằng harness thuần ngoài
> workspace (không DB/HTTP/AI). §2 options/DI + §3 `ReportService` **đã xong & verify** — 37 check PASS bằng
> harness DI + PostgreSQL thật (fixture raw SQL, hard-delete sau khi chạy). §4 renderer PDF/Excel
> **đã xong & verify** — 43 check PASS ngoài workspace (QuestPDF + ClosedXML round-trip + PdfPig đọc lại file).
> §5 DTO + 3 endpoint **đã xong & verify** — **34 check PASS** bằng API thật (Kestrel + PostgreSQL thật + JWT
> tự ký qua cookie), app boot thật `GET /api/health` 200. **Backend Giai đoạn 6 đã đủ**; còn §6 frontend —
> xem `Project-Documents/tasks/phase-6-reporting-export.md` (mục "🔻 BÀN GIAO §6").

## Endpoints (Phase 6 §5.2)

Group `/api/workspaces/{workspaceId}/reports`, tag OpenAPI `Reports`, tất cả **GET** (đọc, idempotent) nên
**không** cần CSRF; quyền **Manager/Admin** enforce trong service (`IReportService`).

| Method + path | Query | Thành công | Lỗi |
|---|---|---|---|
| `GET /summary` | `boardId?`, `from?`, `to?` (ISO-8601) | **200** `ReportSummaryResponse` | 400 `{error}` (from/to sai, from>to) · 401 · 403 · 404 · 503 |
| `GET /export` | `format=pdf\|excel` (**bắt buộc**), `boardId?`, `from?`, `to?` | **200** file bytes + `Content-Disposition: attachment; filename="…"` + `Content-Length` + `X-Report-Row-Cap-Reached` + `X-Report-Rows` + `X-Report-Period` | 400 (thiếu/sai `format`) · 401 · 403 · 404 · 503 |
| `GET /boards` | — | **200** `ReportBoardOptionResponse[]` | 401 · 403 · 404 · 503 |

Mã lỗi (body luôn `{ "error": "…" }`, trừ 400 do framework bind):

| Status | `error` thật | Ghi chú |
|---|---|---|
| 400 | `Unknown value for from 'bogus'. Expected ISO-8601 date-time.` | parse strict ISO-8601 (endpoint), cùng tinh thần `ParseIsRead` Phase 5 |
| 400 | `'from' must be earlier than or equal to 'to'.` | service §3 |
| 400 | `Unknown report format 'CSV'. Expected pdf or excel.` | service §3 |
| 400 (framework) | ProblemDetails, **không** có `error` | `?boardId=abc` — biết trước, xử lý theo status |
| 401 | — | thiếu cookie `access_token` |
| 403 | `Requires Manager or Admin role in this workspace.` | Member |
| 404 | `Workspace not found or you are not a member.` / `Board not found.` | workspace lạ / board không thuộc workspace hoặc đã xoá mềm |
| 503 | `Reporting is disabled.` | `Reports:Enabled=false` (JSON, **không** phải file) |

Ghi chú hiện thực §5:
- **`Program.cs` chỉ đổi 1 dòng**: thêm `public partial class Program { }` (D29) để harness có type public
  cho entry point; pipeline/route không đổi (extension point `MapReportingModuleEndpoints` đã có từ §4).
- **Endpoint là tầng mỏng** (D30): validate nghiệp vụ đã ở §3; endpoint chỉ parse tham số → gọi service →
  map DTO → set header phụ.
- **Header HTTP chỉ nhận ASCII**: nhãn kỳ của domain dùng en dash `–` cho bản in, nên header `X-Report-Period`
  phải qua `Ascii(...)`. ⚠️ Bug thật đã bắt được nhờ harness §5: đưa nguyên nhãn có `–` vào header làm Kestrel
  ném ⇒ **mọi request export thành 500**.
- **`?from=` (rỗng) coi như thiếu** ⇒ dùng mặc định 30 ngày; chuỗi có offset (`+07:00`) được quy về UTC
  (`AdjustToUniversal`) nên so sánh đúng.
- 3 header `X-Report-*` là **thông tin phụ** — UI **không** được phụ thuộc để hiển thị đúng (tên file nằm
  trong `Content-Disposition`, số liệu nằm trong file).

## Quyết định kiến trúc (chốt ở `phase-6-reporting-export.md` §0)

| # | Quyết định |
|---|---|
| D1/D2 | Module `Reporting` **riêng**, **không** entity, **không** migration, **không** bảng mới |
| D3 | Aggregation là **hàm thuần** `ReportAggregator.Build(snapshot, thresholds)` — không DB/HTTP/`DateTime.Now` |
| D4 | Chỉ tham chiếu `Shared` + `Persistence` + `Board`; hằng số cần dùng được **copy** sang `Contracts/ReportSummary.cs` (`ReportActionTypes`, `ReportSeverities`), **không** tham chiếu module Ai |
| D5 | Quyền **Manager/Admin** của workspace (enforce trong service ở §3) |
| D8 | `progress` = trạng thái **hiện tại**; `performance`/`activity`/`health` = **trong cửa sổ**; thời lượng trả bằng **giờ**, tỉ lệ bằng **%** |
| D13 | Cap `Reports:MaxExportRows` / `MaxActionsPerReport` + cờ `rowCapReached` duy nhất cho cả báo cáo |
| D14 | QuestPDF tier **Community**, set trong `ReportPdfRenderer` (renderer tự đủ, không phụ thuộc thứ tự DI) |
| D15 | Excel: **data bar + autofilter + freeze pane** thay cho chart (ClosedXML không có chart native) |
| D18 | Mọi ngưỡng/cap nằm trong config section `Reports`; `Enabled=false` ⇒ endpoint trả 503 (§5) |
| D21 | `AddReportingModule` tự đăng ký renderer + `ReportExportService` ⇒ **không** còn phụ thuộc thứ tự gọi |
| D22 | Tên file **ASCII-only** (`teamnexus-report-<slug>-<yyyyMMdd-HHmm>.<ext>`) để header `Content-Disposition` luôn hợp lệ |
| D24 | Nhãn tiếng Việt cho action/signal/severity (mirror `NotificationItem.tsx`), kèm mã gốc để truy vết |
| D25 | `ReportExportResult.Rows` = tổng dòng chi tiết (board + người + action + signal + severity), dùng chung 2 định dạng |

## Cấu trúc

```
Contracts/ReportSummary.cs       # domain model (ReportSummary + record con) + hằng số dùng chung
Contracts/ReportBoardOption.cs   # lựa chọn board cho bộ lọc (domain type, §5 map sang DTO)
Contracts/ReportExportResult.cs  # file bytes + metadata trả về HTTP (§5)
Contracts/ReportExportFormats.cs # hằng pdf|excel + ContentType/FileExtension
DTOs/ReportDtos.cs              # DTO HTTP (§5) + From(...) map một chiều từ domain
Endpoints/ReportingEndpointHelpers.cs # RequireUserId + parse ISO-8601 strict
Contracts/ReportFileName.cs      # tên file ASCII-only (slug bỏ dấu tiếng Việt) — hàm thuần
Contracts/ReportLabels.cs        # nhãn tiếng Việt cho action/signal/severity
Services/ReportSnapshots.cs      # dữ liệu thô loader §3 chiếu vào (task/board/column/activity/finding)
Services/ReportThresholds.cs     # ngưỡng thuần + BuildRange() — nơi DUY NHẤT tính cửa sổ thời gian
Services/ReportAggregator.cs     # hàm thuần: snapshot → ReportSummary
Services/ReportingExceptions.cs  # 503 (tắt) / 400 (format, khoảng) — kế thừa BoardModuleException
Services/IReportService.cs       # cổng đọc (§3): GetSummaryAsync / BuildExportAsync / ListBoardsAsync
Services/ReportService.cs        # loader read-only (nơi DUY NHẤT chạm DB)
Services/IReportPdfRenderer.cs   # cổng render PDF (§4)
Services/ReportPdfRenderer.cs    # QuestPDF: bìa → tiến độ → hiệu suất → bảng/người → hoạt động → sức khoẻ AI
Services/IReportExcelRenderer.cs # cổng render Excel (§4)
Services/ReportExcelRenderer.cs  # ClosedXML: 5 sheet + data bar + autofilter + freeze pane
Services/ReportExportService.cs  # IReportExportService + chọn renderer/đặt tên file/đếm Rows (§4)
Services/NoReportExportService.cs # fallback (chỉ dùng khi thiếu implementation thật) — throw NotSupportedException
Options/ReportsOptions.cs        # POCO bind section "Reports" + ToThresholds()/EffectiveRange()
Endpoints/ReportingEndpoints.cs   # 3 route GET (summary / export / boards) — tầng mỏng, tag Reports
ReportingModule.cs               # AddReportingModule / MapReportingModuleEndpoints
```

> ✅ **Đăng ký DI (D21):** `AddReportingModule` **tự** đăng ký `IReportPdfRenderer`, `IReportExcelRenderer`
> và `IReportExportService → ReportExportService`, rồi mới xét fallback `NoReportExportService` (chỉ thêm khi
> chưa có implementation nào). Vì vậy **không** còn yêu cầu "phải đăng ký trước `AddReportingModule`";
> fallback chỉ còn là lưới an toàn cho host validate service graph lúc build.

> ⚠️ **Đăng ký `IReportExportService` (quan trọng cho §4):** host validate service graph lúc build
> (`ValidateOnBuild`), nên `ReportService` **không thể** thiếu `IReportExportService`. Vì vậy
> `AddReportingModule` tự thêm `NoReportExportService` (throw `NotSupportedException` khi gọi export)
> **chỉ khi** chưa có implementation nào. §4 phải đăng ký `IReportExportService` → `ReportExportService`
> **trước** `AddReportingModule` để bản thật thắng; nếu đăng ký sau thì bản no-op vẫn được chọn và mọi
> request export sẽ 500. Đã verify cả 2 nhánh (có/không stub).

`ReportSummary` là **nguồn số liệu duy nhất** cho cả response JSON, PDF và Excel ⇒ ba bề mặt không thể
lệch nhau. DTO HTTP (§5) là type riêng trong `DTOs/`, map bằng `.From(...)`.

## §3 `IReportService` — loader read-only (nơi duy nhất chạm DB)

| Method | Trả về | Quyền | Lỗi |
|---|---|---|---|
| `GetSummaryAsync(workspaceId, boardId?, from?, to?, userId, ct)` | `ReportSummary` (domain) | Manager/Admin | 400 khoảng ngược thứ tự · 403 Member · 404 workspace/board lạ · 503 tắt |
| `BuildExportAsync(workspaceId, boardId?, format, from?, to?, userId, ct)` | `ReportExportResult` | Manager/Admin | như trên, thêm **400** khi `format` thiếu/sai (validate **trước** khi tốn truy vấn) |
| `ListBoardsAsync(workspaceId, userId, ct)` | `IReadOnlyList<ReportBoardOption>` | Manager/Admin | 403/404/503 |

Thứ tự thực thi cố định: `Reports:Enabled=false` ⇒ 503 (chưa chạm DB) → `RequireManagerAsync` → validate
`format` → validate khoảng (`from > to` ⇒ 400) → scope board (404 nếu không thuộc/đã xoá) → nạp snapshot →
`ReportAggregator.Build` → (export) `IReportExportService.Build`.

Ghi chú hiện thực:
- **Một mốc `DateTimeOffset.UtcNow` cho cả request** (`now`): `Period.To`, quy tắc quá hạn và `GeneratedAt`
  dùng chung mốc nên báo cáo tự nhất quán (`GeneratedAt == Period.To`).
- **`activity_logs` scope board**: giữ sự kiện của board **và** sự kiện cấp workspace (`board_id IS NULL`).
- **`activeUsers`** = số user khác nhau **trong cả cửa sổ** (một truy vấn `Distinct().Count()` riêng). Cố ý
  **không** cộng count-distinct theo từng action — cách đó đếm trùng người xuất hiện ở nhiều loại hành động
  (đã bị harness bắt: 5 thay vì 2).
- **8 truy vấn / request summary** (workspace, boards, columns, tasks, activity ×2, observer runs) — không N+1.
- `ai_observer_runs.summary`: chỉ lấy run `Completed` trong cửa sổ; JSON hỏng / `NULL` / không phải object ⇒
  **bỏ qua run đó** (`runsScanned` không tính), không throw.
- **Không** ghi bảng (kể cả `activity_logs`/`notifications`/`ai_action_logs`), không file, không transaction ghi
  — verify bằng đếm row trước/sau + grep `SaveChanges`/`DbSet.Add`/`Execute*`.

## §4 renderer — PDF (QuestPDF) + Excel (ClosedXML)

| Thành phần | Hành vi |
|---|---|
| `ReportPdfRenderer.Render(summary, options)` | QuestPDF `Document.Create(...).GeneratePdf()` → `byte[]`; khổ trang theo `Reports:PdfPageSize` (`A4`/`A3`/`Letter`, giá trị lạ ⇒ A4 + log Warning) |
| `ReportExcelRenderer.Render(summary, options)` | ClosedXML `XLWorkbook` → `MemoryStream` → `byte[]`; **5 sheet**: `Tổng quan`, `Theo bảng`, `Theo người`, `Hoạt động`, `Sức khoẻ AI` |
| `ReportExportService.Build(summary, format, options)` | chọn renderer, đặt tên file ASCII (`ReportFileName`), đếm `Rows` (D25), set `ContentType` |

Nội dung PDF (theo §4.3): bìa (tiêu đề + phạm vi + kỳ + mốc lập báo cáo UTC + tên đơn vị) → tiến độ hiện tại kèm
thanh tiến độ → hiệu suất kèm **cột định nghĩa** lấy từ `MetricDefinitions` → theo bảng → theo người → hoạt động
→ sức khoẻ AI (nếu `Reports:IncludeHealthSection`) → ghi chú cuối + cảnh báo `rowCapReached`; footer mọi trang có
tên đơn vị và `x / y`.

Ghi chú hiện thực §4:
- **Font tiếng Việt**: dùng bộ font mặc định của QuestPDF (Lato bundled) — **không** trỏ font hệ thống. Verify
  bằng cách mở lại PDF và đọc text: `BÁO CÁO TIẾN ĐỘ & HIỆU SUẤT`, `Quá hạn (OverdueTask)`, `Nghiêm trọng`,
  `Chưa gán` đều đọc đúng dấu.
- **QuestPDF license** được set trong constructor `ReportPdfRenderer` (idempotent) ⇒ harness/test gọi renderer
  trực tiếp vẫn chạy, không phụ thuộc `AddReportingModule`.
- **Data bar**: API thật của ClosedXML 0.105 là `cell.AddConditionalFormat().DataBar(color, showValue)` —
  **không** có `cell.AddDataBar()` như câu chữ trong file task (đã ghi chú điều chỉnh ở §4.4).
- **Số/ngày literal format code** (`0.0`, `0.0"%"`, `0`) ⇒ không phụ thuộc culture; mọi giá trị là literal
  (không công thức) nên không thể sinh `#REF!`. `double?` null ⇒ ô ghi `—` (không phải 0).
- **Không IO/DB trong renderer**: không `File.*`/`StreamWriter`/`DbContext`/`DateTime.Now` (verify bằng grep) và
  không tạo file nào trên disk (verify bằng so danh sách file trước/sau).
- **D25 `Rows`** = `ByBoard + ByAssignee + ByAction + SignalsByType + FindingsBySeverity` — cùng công thức cho cả
  2 định dạng.
- Hiệu năng đo thật (sau warm-up): báo cáo đầy đủ PDF **13ms** (59 KB, 12 dòng chi tiết), Excel **21ms**;
  bảng lớn (10 board + 50 người + 30 action ⇒ 4 trang PDF) PDF **36ms**, Excel **68ms**.

## Công thức metric (nguồn sự thật — copy nguyên văn)

| Metric | Công thức | Biên |
|---|---|---|
| `progress.total` | số task không soft-delete trong scope | query filter của `Tasks` đã loại `deleted_at` |
| `progress.done` | `is_done` **hoặc** `completed_at != null` | dữ liệu cũ (done ở cột `is_done=false`) vẫn tính |
| `progress.open` | `total − done` | |
| `progress.overdue` | `!is_done && due_date != null && due_date < Now` | `due == Now` ⇒ **chưa** quá hạn; tắt `Reports:ExcludeDoneOverdue` thì task xong trễ hạn cũng được đếm |
| `progress.donePercent` | `done / total × 100` | `total = 0` ⇒ `0.0` |
| `performance.createdInRange` | `created_at ∈ [From, To]` | |
| `performance.completedInRange` | `completed_at ∈ [From, To]` | task xong **trước** cửa sổ vẫn ở `progress.done`, không tính lại |
| `performance.avgCompletionHours` | trung bình `completed_at − created_at` của mẫu trên | mẫu 0 ⇒ `null` (UI hiện "—") |
| `performance.avgLeadTimeHours` | như trên nhưng chỉ task có `due_date` | mẫu 0 ⇒ `null` |
| `performance.onTimeRate` | `% completed_at ≤ due_date` | task không `due_date` **bị loại khỏi mẫu**; mẫu 0 ⇒ `null` |
| `performance.overdueRate` | `overdue / open × 100` | `open = 0` ⇒ `0.0` |
| `performance.throughputPerWeek` | `completedInRange / (Days / 7)` | |
| `performance.completedAtMissing` | task cột `done` thiếu `completed_at` | bị loại khỏi mọi metric thời lượng |
| `byBoard[]` / `byAssignee[]` | cùng bộ metric con | `assignee_id = null` gom thành `"Chưa gán"` |
| `activity.*` | từ `activity_logs` trong cửa sổ: `totalActions`, `byAction`, `activeUsers`, `actionsPerDay` | giá trị `action` lạ vẫn giữ nguyên văn |
| `health.*` | từ `ai_observer_runs.summary` của run `Completed` trong cửa sổ: `runsScanned`, `signalsByType`, `findingsBySeverity` | severity lạ ⇒ `"Unknown"` |

Bất biến: mọi `double` làm tròn **1 chữ số** (`AwayFromZero`), không bao giờ `NaN`/`Infinity`; mọi bảng
sort tường minh (`Total desc → Open desc → Overdue desc → tên asc → GUID asc`; action/health: `Count desc`
→ tên asc) ⇒ **build 2 lần cho JSON giống hệt**; cap chỉ cắt dòng (không cắt `totalActions`).

`ReportSummary.MetricDefinitions` trả kèm mô tả tiếng Việt cho **cả 22 metric** — PDF/Excel/UI cùng đọc
một nguồn, không có tài liệu metric thứ hai.

## Cấu hình (`Reports` trong `appsettings.json`)

| Key | Mặc định | Ghi chú |
|---|---|---|
| `Enabled` | `true` | `false` ⇒ endpoint báo cáo trả **503** |
| `DefaultRangeDays` | `30` | clamp `1..MaxRangeDays` |
| `MaxRangeDays` | `365` | vượt ⇒ cắt cửa sổ + cờ `period.clamped` |
| `MaxExportRows` | `5000` | cap dòng chi tiết (board/người/action) |
| `MaxActionsPerReport` | `200` | cap dòng hoạt động |
| `PdfPageSize` | `"A4"` | `A4` \| `A3` \| `Letter`; giá trị lạ ⇒ `TryGetPdfPageSize` trả `false` + fallback A4 (log warning ở §4) |
| `CompanyName` | `"TeamNexus"` | header/footer PDF + sheet "Tổng quan" |
| `ExcelDateFormat` | `"dd/MM/yyyy HH:mm"` | |
| `IncludeHealthSection` | `true` | bật/tắt khối "Sức khoẻ AI" trong báo cáo (renderer §4 đọc; aggregator luôn tính) |
| `ExcludeDoneOverdue` | `true` | `false` ⇒ task xong nhưng trễ hạn cũng vào `overdue` |

Log khởi động (không secret):

```
Reporting module: enabled=True, defaultRange=30d, maxRange=365d, maxExportRows=5000, maxActions=200, pdfPageSize=A4.
```

## Verify

Harness §7.1 nhóm B nằm **ngoài workspace** (`%TEMP%`, chỉ `ProjectReference` tới project này — không DB/HTTP/AI),
chạy xong là xoá, không commit. Cách dựng lại: console app `net10.0` reference project này, gọi
`ReportAggregator.Build(...)` với snapshot dựng tay và assert các case trong bảng §7.1 nhóm B của file task.
Các case đã chạy: progress/overdue biên, `completed_at`/`is_done`/`completedAtMissing`, mẫu số 0 ⇒ `null`,
làm tròn, cửa sổ (`default 30d`, `to` tương lai, clamp 365d, ceil, `from > to`), cap + `rowCapReached`,
tie-break sort, board 0 task, board lạ, list `null`, `metricDefinitions`, scope, tất định JSON, 1000 task,
và bất biến "file thuần" (strip comment rồi grep `DateTime.Now`/EF/IO/HttpClient/`Guid.NewGuid`).

> ⚠️ Hai lưu ý cho harness §3–§5 (tiền lệ Phase 5): `dotnet ef` **không** dùng `--no-build`;
> `ReportService` sẽ là **nơi duy nhất** chạm DB — không thêm truy vấn nào vào aggregator/renderer.
