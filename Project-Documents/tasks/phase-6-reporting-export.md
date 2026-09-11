# Giai đoạn 6 – Báo cáo & Xuất dữ liệu

> **Mục tiêu:** Xây module `Reporting` tổng hợp **tiến độ** (trạng thái hiện tại của board: tổng/hoàn thành/đang mở/quá hạn)
> và **hiệu suất** (trong một cửa sổ thời gian: số task tạo & hoàn thành, thời gian hoàn thành trung bình, tỉ lệ đúng hạn,
> throughput, hoạt động theo `activity_logs`, sức khoẻ dự án theo `ai_observer_runs`) — tất cả **chỉ đọc** — rồi **xuất
> on-demand** ra **PDF (QuestPDF)** và **Excel (ClosedXML)** trả về dưới dạng luồng byte trong RAM, **không lưu file trên server**.
>
> **Công nghệ:** ASP.NET Core (.NET 10) Modular Monolith · EF Core (Npgsql, read-only projection) · QuestPDF 2026.8.0 ·
> ClosedXML 0.105.1 · Minimal API + endpoint filters (domain-error) · React + TypeScript + Vite + Ant Design · Vitest.
>
> **Tham chiếu:** `03-roadmap.md` (Giai đoạn 6, 4 ô hoàn thiện) · `02-tech-stack-decisions.md` §1 & §2.5 & §3 · `04-database-design.md`
> §1.1, §3.7, §4, §5, §7 · contract bàn giao ở `tasks/phase-5-ai-observer.md` §8 ("Bàn giao Giai đoạn 6") và
> `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`.

---

## 0. Tiền đề & quyết định kiến trúc (đã chốt)

Đọc hết mục này trước khi code — các quyết định dưới đây đã chốt, **không chọn lại**.

**Trạng thái đầu vào (đã kiểm tra thực tế trong repo):**

- **Giai đoạn 6 hoàn toàn trống:** grep `QuestPDF|ClosedXML|Reporting|Export|Pdf|Excel` trong `src/` → **0 kết quả**
  (duy nhất 1 comment trong `TeamNexus.Persistence.csproj`). Chưa có project `Reporting`, chưa có package, chưa có migration.
- Giai đoạn 5 đã xong và **không đổi nữa**: `activity_logs` (event store append-only), `notifications`, `ai_observer_runs`
  (có `summary` jsonb) đã có entity/config/migration `20260910105154_Phase5AiObserverSchema`. `dotnet ef migrations list` = **5 migration applied**.
- Tái dùng được ngay: `IWorkspaceAccess.RequireManagerAsync` (module Board), `DomainExceptionFilter`, `NotFoundException` /
  `ForbiddenException` / `BoardModuleException`, `AntiforgeryValidationEndpointFilter` (Shared), `AddTeamNexusPersistence`.
- `src/Modules/Board/.../Endpoints/CurrentUser.cs` là `internal` ⇒ mỗi module tự có bản copy helper đọc `ClaimTypes.NameIdentifier`
  (module Ai đã làm với `AiEndpointHelpers`). Module Reporting sẽ làm tương tự.
- `AntiforgeryValidationEndpointFilter` gọi `IsRequestValidAsync` **không phân biệt method** ⇒ chỉ gắn cho POST/PUT/DELETE;
  **mọi endpoint của Giai đoạn 6 là GET** nên **không** gắn CSRF (đúng tiền lệ Phase 4 §3.2 / Phase 5 §5.2).
- `frontend/src/shared/api/httpClient.ts`: base `/api`, `withCredentials: true`, tự gắn `X-XSRF-TOKEN` cho method mutating,
  tự refresh 401, tự retry 1 lần khi 403 CSRF. ⇒ **download phải đi qua axios** (`responseType: 'blob'`), không dùng `window.open`.
- `frontend/src/app/router.tsx` chưa có route báo cáo; `BoardListPage` và `BoardView` là 2 điểm vào hợp lý (đã có sẵn pattern
  role check `GET /api/workspaces` trong `BoardView` để ẩn nút theo Manager/Admin).
- NuGet đã kiểm tra khả dụng: **QuestPDF 2026.8.0**, **ClosedXML 0.105.1**. `Directory.Build.props` = `net10.0`, nullable + implicit usings.

| # | Quyết định | Lý do / ghi chú |
|---|---|---|
| **D1** | **Tạo module mới `TeamNexus.Modules.Reporting`** tại `src/Modules/Reporting/TeamNexus.Modules.Reporting/`; thêm vào `TeamNexus.sln` (folder solution `Reporting`) + 2 dòng trong `Program.cs` | Đã chốt sẵn ở `02` §1 ("`Auth`, `Board`, `Ai`, `Reporting`") và `04` §1 (cùng dòng "Module `Auth`, `Board`, `Ai`, `Reporting`") |
| **D2** | **KHÔNG migration, KHÔNG entity, KHÔNG bảng/bảng audit báo cáo.** Báo cáo là **projection read-only** trên `tasks`, `boards`, `board_columns`, `activity_logs`, `ai_observer_runs` | Đúng `04` §3.7 ("Không tạo bảng lưu trữ") và tinh thần "tránh over-scope" của `02` §5 |
| **D3** | **Aggregation là hàm thuần** `ReportAggregator.Build(...)`: không `DbContext`, không `HttpClient`, không `DateTime.Now` (nhận `Now` từ snapshot), không I/O | Cùng pattern `ObserverSignalDetector.Analyze` (Phase 5 §3) ⇒ verify không cần DB/AI, là điểm tựa test xUnit ở Giai đoạn 8 |
| **D4** | Reporting tham chiếu **Shared + Persistence + Board** (dùng `IWorkspaceAccess`, `DomainExceptionFilter`, hằng enum chung) và **có bản copy hằng số riêng** (`ReportActionTypes`, `ReportSeverities`); **KHÔNG** tham chiếu module Ai | Tránh coupling `Reporting → Ai` (module Ai không đổi một dòng nào ở Giai đoạn 6). Chi phí: 1 file hằng số nhỏ, đã ghi giá trị khớp `activity_logs.action` |
| **D5** | **Quyền: Manager/Admin** cho **cả 3** endpoint; enforce **trong service** bằng `IWorkspaceAccess.RequireManagerAsync` (Member ⇒ **403**, workspace lạ ⇒ **404**) | Quyết định của người dùng + nhất quán tuyệt đối với Observer (Phase 5 §5.3) |
| **D6** | **Phạm vi: workspace, lọc tuỳ chọn `?boardId=`** (quyết định của người dùng). `boardId` không thuộc workspace/không thấy ⇒ **404** | Một implementation dùng cho cả trang Báo cáo (toàn workspace) và nút xuất trên từng board |
| **D7** | **Cửa sổ thời gian `?from=&to=`** (quyết định của người dùng), mặc định **30 ngày gần nhất**, clamp ≤ `MaxRangeDays` (365) và đánh cờ `clamped`; `from > to` ⇒ **400** | `activity_logs` bị prune 30 ngày ⇒ cửa sổ mặc định khớp dữ liệu thật còn lại; không cần cột DB mới |
| **D8** | **`progress` = trạng thái HIỆN TẠI**; **`performance` + `activity`/`health` = trong cửa sổ**. Mọi **thời lượng trả bằng giờ (`double`, 1 chữ số thập phân)**, mọi **tỉ lệ trả bằng %** (`double`, 1 chữ số thập phân) | `hours` tránh mất nghĩa khi demo dữ liệu nhỏ; `int days` sẽ ra 0 với task hoàn thành trong ngày |
| **D9** | **Định nghĩa metric chốt** (ghi vào `metricDefinitions` của response **và** header mỗi bảng trong PDF/Excel). Xem bảng công thức §1.3 | Xác định được, verify được, không phụ thuộc lịch sử chuyển cột (log chỉ giữ 30 ngày) |
| **D10** | **Hiệu suất theo người** = mảng `byAssignee[]` (gom theo `tasks.assignee_id`, `null` ⇒ nhóm "Chưa gán"). **Không** gọi là "báo cáo cá nhân" | Đủ mạnh cho portfolio, không over-scope |
| **D11** | **File = `byte[]` sinh trong RAM** → `Results.File(bytes, contentType, fileDownloadName)`. **Không** ghi disk/temp/blob/DB; trả kèm `Content-Length` + `Content-Disposition` + header `X-Report-Row-Cap-Reached` | Đúng tiêu chí roadmap R4 ("generate on-demand, không lưu trữ vĩnh viễn") + `02` §3 ("File export tạm thời") |
| **D12** | Endpoint **GET (đọc, idempotent)** ⇒ **không** gắn CSRF filter. Frontend tải bằng axios `responseType: 'blob'` + `URL.createObjectURL` | GET để `httpClient` tự refresh 401 (raw `window.open` sẽ vỡ luồng refresh); blob để lỗi JSON đọc được thay vì tab trắng |
| **D13** | **Cap an toàn:** `MaxExportRows` (5000) cắt số dòng chi tiết (theo bảng/người/action) + cờ `rowCapReached`; `MaxActionsPerReport` (200) cho phần hoạt động | Bảo vệ free-tier quota; hành vi xác định, có cờ báo cho UI/PDF |
| **D14** | **QuestPDF Community**: đặt `QuestPDF.Settings.License = LicenseType.Community` **một lần duy nhất** trong `ReportingModule.AddReportingModule` + ghi rõ điều kiện license trong README module | Bắt buộc về mặt kỹ thuật (QuestPDF ném exception nếu chưa set license) |
| **D15** | **ClosedXML không có chart native** ⇒ dùng **data bar + autofilter + freeze pane + format số/ngày**; **không** hứa biểu đồ Excel | Tránh hứa hẹn không làm được |
| **D16** | Xuất báo cáo **không** ghi `activity_logs` / `notifications` / `ai_action_logs`; **không** đi qua Accountability Layer | Báo cáo là lớp *đọc*; tránh tự nhiễm dữ liệu cho Observer |
| **D17** | **Không** tạo project xUnit (để Giai đoạn 8). Verify backend bằng **harness tạm ngoài workspace** + API thật + PostgreSQL thật | Nhất quán Phase 2–5 (Phase 5 §0 D18) |
| **D18** | Mọi ngưỡng/cap nằm trong config section `Reports` (**không** hard-code); có `Reports:Enabled` (mặc định `true`) làm công tắc an toàn ⇒ `false` trả **503** | Demo/tinh chỉnh không cần build lại; tắt nhanh trên production |
| **D19** | Thứ tự đăng ký trong `Program.cs`: `AddBoardModule()` → `AddAiModule()` → **`AddReportingModule()`** | Giữ nguyên bất biến "Board đăng ký trước Ai" (bẫy DI đã ghi ở Phase 5 §2); Reporting thêm **sau** cùng để không đổi resolve của 2 module cũ |
| **D20** | `Reports:Enabled=false` ⇒ **503** `{ error: "Reporting is disabled." }` (không phải 404/403) | UI phân biệt được "tính năng đang tắt" với "không có quyền" |

**Non-goals Giai đoạn 6 (ghi rõ để không over-scope):** bảng/lịch sử báo cáo đã sinh (không có bảng mới); lưu file trên S3/blob/`/temp`;
gửi email báo cáo định kỳ; biểu đồ native trong Excel; logo/font ngoài trong PDF; export CSV/JSON; lọc theo label/priority/assignee
trong báo cáo (chỉ có board + khoảng thời gian); phân tích AI trên báo cáo; SignalR cho tiến trình render; i18n báo cáo (tiếng Việt cố định);
test xUnit; dựng lại lịch sử chuyển cột từ `activity_logs`.

**Luồng tổng thể:**

```
GET /api/workspaces/{id}/reports/summary?boardId=&from=&to=      (Manager/Admin)
GET /api/workspaces/{id}/reports/export?format=pdf|excel&…      (Manager/Admin)
GET /api/workspaces/{id}/reports/boards                          (Manager/Admin — đổ dropdown)
        │
        ▼
ReportService (nơi DUY NHẤT chạm DB)
  1. RequireManagerAsync(workspaceId, userId)           → 403 Member / 404 workspace lạ
  2. Validate tham số (format/from/to)                  → 400
  3. boardId? ⇒ xác nhận thuộc workspace               → 404
  4. Nạp bounded: boards → board_columns → tasks (+assignee.DisplayName)
                  → activity_logs(trong cửa sổ, chỉ action/user/created_at)
                  → ai_observer_runs(Completed trong cửa sổ, parse summary phòng thủ)
  5. ReportAggregator.Build(snapshot, thresholds, now)  → ReportSummary  (hàm THUẦN)
  6. format? ⇒ ReportPdfRenderer / ReportExcelRenderer.Render(summary) → byte[]
        │
        ▼
  Results.File(bytes, contentType, fileName)  + Content-Length + X-Report-Row-Cap-Reached
  (KHÔNG ghi DB, KHÔNG ghi file, KHÔNG đụng Accountability Layer)
```

---

## 1. Backend – Aggregation thuần (không chạm DB)

> **Trạng thái: ✅ ĐÃ HIỆN THỰC & VERIFY (76 check PASS).** File: `Contracts/ReportSummary.cs`,
> `Services/ReportSnapshots.cs`, `Services/ReportThresholds.cs`, `Services/ReportAggregator.cs`,
> `Options/ReportsOptions.cs`, `ReportingModule.cs`, README module; project đã vào `.sln` + `Program.cs` +
> `appsettings.json`. Harness thuần ngoài workspace (`%TEMP%\tn-rep-s1-verify`, đã xoá) chạy **76/76 PASS**:
> progress/overdue biên (B1–B20), thời lượng & mẫu số rỗng (B21–B33), gom nhóm + tie-break + dữ liệu lạ
> (B34–B39b), hoạt động & cap (B40–B45), sức khoẻ AI (B46–B48), quy tắc cửa sổ & options (B49–B57c),
> tất định + metricDefinitions + scope (B58–B61c), hiệu năng 1000 task (B62–B63), bất biến "file thuần"
> (B64–B65), wiring module (B66–B68). `dotnet build TeamNexus.sln` **0 warning / 0 error**;
> `git diff` **không** chạm `src/Modules/Board|Ai|Persistence`; vẫn **5 migration** (không migration mới).
>
> **Điều chỉnh so với bản sơ bộ (có chủ ý, đã cập nhật ngay trong §1.1/§1.4):**
> - `ReportSummary` + các record con (domain model) được tạo **ở §1** (không phải §4.1) vì
>   `ReportAggregator.Build` trả chính type đó — §4.1 chỉ còn `ReportExportResult.cs`.
> - `ReportRange` thêm `Label` (nhãn "dd/MM/yyyy – dd/MM/yyyy (UTC)") để PDF/Excel/filename dùng chung.
> - `ReportWorkspaceSnapshot.Actions` (list `ReportActionCount`) → `Activity: ReportActivitySnapshot`
>   (`TotalActions`, `ActiveUsers`, `ByAction`) vì khối `activity` cần `activeUsers`.
> - `ReportsOptions` thêm key `ExcludeDoneOverdue` (mặc định `true`) — tường minh hoá cách đếm `overdue`
>   (task đã xong không bị coi là quá hạn, khớp `ObserverSignalDetector`) và cho phép verify case B17.
> - Thứ tự sort chốt (thay cho "SortOrder" còn mơ hồ): bảng `(Total desc, Open desc, Overdue desc, tên asc,
>   GUID asc)`; `byAction`/health `(Count desc, tên asc)`.
> - §1 **không** thêm `PackageReference` QuestPDF/ClosedXML (chỉ cần ở §4) ⇒ harness §1 không kéo dependency ngoài.

### 1.1 Snapshot contract (`Services/ReportSnapshots.cs`)

Tạo các record **public** (để harness thuần + test Giai đoạn 8 dùng được):

```csharp
// ReportRange có thêm Label (nhãn hiển thị sẵn cho PDF/Excel/filename) so với bản sơ bộ.
public sealed record ReportRange(DateTimeOffset From, DateTimeOffset To, int Days, bool Clamped, string Label);

public sealed record ReportBoardSnapshot(Guid BoardId, string Name);

public sealed record ReportColumnSnapshot(Guid ColumnId, Guid BoardId, string Name, bool IsDone);

public sealed record ReportTaskSnapshot(
    Guid TaskId, Guid BoardId, string BoardName, Guid ColumnId, string ColumnName, bool IsDoneColumn,
    string Title, Guid? AssigneeId, string? AssigneeName, string? Priority,
    DateTimeOffset? DueDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? CompletedAt);

public sealed record ReportActionCount(string Action, int Count);

/// <summary>Sự kiện trong cửa sổ, gom theo action, kèm số người dùng riêng biệt.</summary>
/// <remarks>
/// Điều chỉnh so với bản sơ bộ: snapshot mang <see cref="ReportActivitySnapshot"/> thay vì
/// <c>IReadOnlyList&lt;ReportActionCount&gt;</c> vì khối <c>activity</c> còn cần <c>activeUsers</c> —
/// loader §3 vốn đã SELECT <c>action, user_id, created_at</c> nên gom được ngay tại tầng đọc, aggregator
/// không cần biết về user.
/// </remarks>
public sealed record ReportActivitySnapshot(
    int TotalActions, int ActiveUsers, IReadOnlyList<ReportActionCount> ByAction);

public sealed record ReportFindingCount(string Type, string Severity, int Count);

public sealed record ReportWorkspaceSnapshot(
    Guid WorkspaceId, string WorkspaceName, ReportRange Range,
    IReadOnlyList<ReportBoardSnapshot> Boards,
    IReadOnlyList<ReportColumnSnapshot> Columns,
    IReadOnlyList<ReportTaskSnapshot> Tasks,
    ReportActivitySnapshot Activity,
    IReadOnlyList<ReportFindingCount> Findings,
    int RunsScanned,
    DateTimeOffset Now);
```

> `ReportColumnsSnapshot` **không** tái dùng `ObserverColumnSnapshot` của module Ai (D4: không tham chiếu Ai) — cùng hình dạng nhưng
> là record riêng của Reporting. Điều này là **cố ý**, không phải trùng lặp do sơ suất.

### 1.2 `Services/ReportAggregator.cs` — `public static ReportSummary Build(ReportWorkspaceSnapshot snapshot, ReportThresholds thresholds)`

- **Thuần tuyệt đối:** không `DateTime.Now` (dùng `snapshot.Now`), không `DbContext`, không `HttpClient`, không `Guid.NewGuid()`,
  không random. Cùng input ⇒ output giống hệt (điều kiện verify "tất định": build 2 lần → serialize giống byte-đối-byte).
- **Không chia cho 0:** mọi tỉ lệ/% khi mẫu số = 0 ⇒ trả `0.0` (không `NaN`/`Infinity`).
- **Sort xác định:** mọi mảng trả ra sort theo `SortOrder` giảm dần rồi tới `Name`/`Id` **tăng dần** (tie-break ổn định).
- **Cap:** áp `thresholds.MaxExportRows` cho `byBoard`/`byAssignee`/`activity.byAction` và `MaxActionsPerReport` cho danh sách hoạt động;
  khi bị cắt ⇒ `truncated.rowCapReached = true` (đây là **cờ duy nhất**, không có cờ riêng cho từng mảng).

### 1.3 Bảng công thức & định nghĩa metric (nguồn duy nhất — copy vào `metricDefinitions` của response)

| Metric | Công thức | Ghi chú biên |
|---|---|---|
| `progress.total` | số task có `deleted_at IS NULL` (trong scope) | Global query filter của `Tasks` đã loại soft-delete |
| `progress.done` | số task có `CompletedAt != null` **hoặc** `IsDoneColumn = true` | Dữ liệu cũ (done ở cột `is_done=false`) vẫn được tính; ghi rõ trong `metricDefinitions` |
| `progress.open` | `total − done` | |
| `progress.overdue` | `IsDoneColumn = false` **và** `DueDate != null` **và** `DueDate < Now` | `DueDate == Now` ⇒ **chưa** quá hạn; task ở cột done ⇒ không tính quá hạn (khớp `ObserverSignalDetector`) |
| `progress.donePercent` | `done / total × 100` | `total = 0` ⇒ `0.0` |
| `performance.createdInRange` | số task có `CreatedAt ∈ [From, To]` | |
| `performance.completedInRange` | số task có `CompletedAt ∈ [From, To]` | Task done trước cửa sổ **không** tính lại (khác `progress.done`) |
| `performance.avgCompletionHours` | trung bình `(CompletedAt − CreatedAt)` của task có `CompletedAt ∈ [From, To]` | Không có mẫu ⇒ `null` (≠ `0`); trả `null` để UI hiện "—" |
| `performance.avgLeadTimeHours` | trung bình `(CompletedAt − CreatedAt)` của task có `CompletedAt != null` **và** `DueDate != null`, trong cửa sổ | Tách khỏi `avgCompletionHours` để thấy ảnh hưởng của hạn; cùng công thức thời lượng nhưng mẫu khác — ghi rõ trong `metricDefinitions` |
| `performance.onTimeRate` | `% task có CompletedAt ≤ DueDate` trong số task done **có `DueDate`** và `CompletedAt ∈ [From, To]` | Task không có `DueDate` ⇒ **loại khỏi mẫu** (không tính là đúng hạn); mẫu 0 ⇒ `null` |
| `performance.overdueRate` | `overdue / open × 100` | `open = 0` ⇒ `0.0` |
| `performance.throughputPerWeek` | `completedInRange / (Days / 7)` | `Days ≤ 0` ⇒ `0.0` |
| `performance.completedAtMissing` | số task `done` nhưng `CompletedAt == null` | Minh bạch dữ liệu bẩn; bị loại khỏi mọi metric thời lượng |
| `byBoard[]` / `byAssignee[]` | cùng bộ metric con (`total`, `done`, `open`, `overdue`, `completedInRange`, `avgCompletionHours`, `onTimeRate`) | `AssigneeId = null` ⇒ gom vào 1 dòng `assigneeName = "Chưa gán"` |
| `activity.totalActions` | số `activity_logs` có `CreatedAt ∈ [From, To]` trong scope | `activity_logs` có `board_id` ⇒ khi `boardId` được chọn, lọc theo `board_id` |
| `activity.byAction[]` | đếm theo `action` (giá trị từ `ReportActionTypes`) | Hành động lạ (text tự do) vẫn hiển thị nguyên giá trị |
| `activity.activeUsers` | số `user_id` distinct (bỏ `null`) trong cửa sổ | |
| `activity.actionsPerDay` | `totalActions / Days` | |
| `health.runsScanned` | số `ai_observer_runs` `Completed` có `StartedAt ∈ [From, To]` (theo scope) | Parse `summary` **phòng thủ**: JSON hỏng ⇒ bỏ qua row đó (giống `ObserverService.ReadInt/ReadBool`) |
| `health.findingsBySeverity[]` | cộng dồn `summary.findings[].severity` | Severity lạ ⇒ gom vào `"Unknown"` |
| `health.signalsByType[]` | cộng dồn `summary.signalsByType` | |

### 1.4 `ReportThresholds` (`Services/ReportThresholds.cs`)

Record build từ `ReportsOptions`: `MaxExportRows`, `MaxActionsPerReport`, `MaxRangeDays`, `DefaultRangeDays`, `ExcludeDoneOverdue`.
`BuildRange(from, to, now)` → `ReportRange` *(tên thật khi hiện thực; bản sơ bộ gọi là `Respond`)* — clamp `from/to`,
`Days = ceil((To − From).TotalDays)` (min 1), `Clamped = (yêu cầu > MaxRangeDays)`, `Label` dựng sẵn; `to` ở
tương lai bị kéo về `now`; `from > to` ⇒ cửa sổ 1 ngày và **không** throw (validate là việc của §3).

---

## 2. Backend – Options, config, DI

### 2.1 `Options/ReportsOptions.cs`

- [ ] `public const string SectionName = "Reports";` + bind trong `ReportingModule`.
- [ ] `ToThresholds()`; `EffectiveRange(DateTimeOffset? from, DateTimeOffset? to, DateTimeOffset now)` (mặc định `now − DefaultRangeDays`, clamp `MaxRangeDays`, `To = min(to ?? now, now)`); `PdfPageSize` parse **strict** (`A4`/`A3`/`Letter`, giá trị lạ ⇒ fallback `A4` + `LogWarning` — **không** throw lúc khởi động).

| Key | Mặc định | Ghi chú |
|---|---|---|
| `Enabled` | `true` | `false` ⇒ cả 3 endpoint trả **503** `{ error: "Reporting is disabled." }` |
| `DefaultRangeDays` | `30` | khi thiếu `from`/`to`; clamp 1..`MaxRangeDays` |
| `MaxRangeDays` | `365` | vượt ⇒ cắt + `period.clamped = true` |
| `MaxExportRows` | `5000` | cap dòng chi tiết cho PDF/Excel/summary |
| `MaxActionsPerReport` | `200` | cap dòng hoạt động |
| `PdfPageSize` | `"A4"` | `A4` \| `A3` \| `Letter` |
| `CompanyName` | `"TeamNexus"` | header/footer PDF + sheet "Tổng quan" |
| `ExcelDateFormat` | `"dd/MM/yyyy HH:mm"` | format hiển thị trong `.xlsx` |
| `IncludeHealthSection` | `true` | tắt phần sức khoẻ AI trong báo cáo |

- [ ] Thêm section `"Reports"` vào `src/TeamNexus.Api/appsettings.json` (đủ key như bảng, **không** chứa secret).
- [ ] Log lúc khởi động **một dòng**: `Reporting module: enabled={Enabled}, defaultRange={DefaultRangeDays}d, maxRange={MaxRangeDays}d, maxExportRows={MaxExportRows}, pdfPageSize={PdfPageSize}` (không có secret).

### 2.2 `ReportingModule.cs` (DI entry point)

- [ ] `AddReportingModule(this IServiceCollection services, IConfiguration configuration)`:
  ```csharp
  services.AddOptions<ReportsOptions>().Bind(configuration.GetSection(ReportsOptions.SectionName));

  // QuestPDF Community license (D14): set once, before any document is created.
  QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

  services.AddScoped<IReportService, ReportService>();
  services.AddScoped<IReportPdfRenderer, ReportPdfRenderer>();
  services.AddScoped<IReportExcelRenderer, ReportExcelRenderer>();
  services.AddScoped<IReportExportService, ReportExportService>();
  services.AddTransient<DomainExceptionFilter>();   // từ module Board (map BoardModuleException → { error, status })
  LogResolvedConfiguration(configuration);
  ```
- [ ] `MapReportingModuleEndpoints(this IEndpointRouteBuilder endpoints)` → `endpoints.MapReportingEndpoints();`.
- [ ] `TeamNexus.Modules.Reporting.csproj`:
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` +
  `ProjectReference` → `TeamNexus.Shared`, `TeamNexus.Persistence`, `TeamNexus.Modules.Board` +
  `PackageReference` → `QuestPDF` **2026.8.0**, `ClosedXML` **0.105.1** (pin chính xác, không wildcard).
- [ ] Thêm project vào `TeamNexus.sln` (folder `Modules` → `Reporting`, đặt cạnh folder `Ai`).
- [ ] `src/TeamNexus.Api/Program.cs`: thêm `using TeamNexus.Modules.Reporting;`, `builder.Services.AddReportingModule(builder.Configuration);` **sau** `AddAiModule`, và `app.MapReportingModuleEndpoints();` sau `MapAiModuleEndpoints()`.
- [ ] **Không** sửa bất kỳ file nào của module Board/Ai/Persistence (xem bất biến §9 nhóm A của §8).

---

## 3. Backend – `ReportService` (nơi duy nhất chạm DB)

> **Trạng thái: ✅ ĐÃ HIỆN THỰC & VERIFY (37 check PASS).** File: `Services/IReportService.cs`,
> `Services/ReportService.cs`, `Services/ReportingExceptions.cs`, `Contracts/ReportBoardOption.cs`,
> `Contracts/ReportExportResult.cs`, `Contracts/IReportExportService.cs`; `ReportingModule` đã đăng ký
> `IReportService`. Harness (`%TEMP%\tn-rep-s3-verify`, đã xoá) dùng **DI thật + PostgreSQL thật**
> (fixture seed bằng raw SQL, hard-delete kết thúc) + **stub `IReportExportService`** (không cần renderer §4):
> nhóm P1–P6 quyền/disabled, P7–P11 scope, P12–P20 số liệu khớp fixture, P21–P24 cửa sổ, P25–P28 export,
> P29–P32 bất biến read-only, P33–P35 số truy vấn/tất định, P36 log. `dotnet build TeamNexus.sln`
> **0 warning / 0 error**; DB về baseline (0 row fixture); `git diff` **không** chạm Board/Ai/Persistence.
>
> **Điều chỉnh contract so với §3.1/§4.1/§5.1 (có chủ ý — để §3 build được độc lập trước §4/§5):**
> - `IReportService.GetSummaryAsync` trả **domain** `ReportSummary` (không trả `ReportSummaryResponse`):
>   renderer §4 cần domain model, nên nếu §3 trả DTO thì §3–§5 phải code cùng lúc. `ReportSummaryResponse` +
>   `ToResponse()` vẫn thuộc **§5.1** (đúng tiền lệ module Ai: `ObserverScanOutcome` ≠ `ObserverScanResponse`).
> - `BuildExportAsync` trả `ReportExportResult` — type này **tạo ở §3** (`Contracts/ReportExportResult.cs`).
> - `ListBoardsAsync` trả `IReadOnlyList<ReportBoardOption>` (domain type mới); DTO `ReportBoardOptionResponse` vẫn ở §5.1.
> - `Contracts/IReportExportService.cs` (interface + hằng `ReportExportFormats` pdf/excel + `ContentType`/`FileExtension`)
>   được tạo **ở §3** để service chỉ phụ thuộc interface; §4 cắm `ReportExportService` + 2 renderer, và
>   `ReportingModule` đăng ký implementation ở §4.
> - Thêm 3 exception domain (`ReportingDisabledException` 503 / `InvalidReportFormatException` 400 /
>   `InvalidReportRangeException` 400) kế thừa `BoardModuleException` ⇒ dùng lại `DomainExceptionFilter` của Board.
> - `ReportsOptions` thêm key `ExcludeDoneOverdue` (đã ghi ở §1) và **một mốc `UtcNow` cho cả request**:
>   `Period.To`, quy tắc quá hạn và `GeneratedAt` dùng chung `now` ⇒ `GeneratedAt == Period.To` (verify P24).
> - `activeUsers` = số user khác nhau **trong cả cửa sổ** (1 truy vấn `Distinct().Count()` riêng), **không**
>   phải tổng count-distinct theo từng action — cách cộng theo action đếm trùng người (harness bắt: 5 thay vì 2).
> - Một request summary = **8 truy vấn** (workspace, boards, columns, tasks, activity ×2, observer runs), không N+1.
>
> **Cập nhật sau §4 (footgun đã được bỏ):** host validate service graph lúc build (`ValidateOnBuild`), nên
> `ReportService` không thể thiếu `IReportExportService`. Ban đầu `AddReportingModule` chỉ thêm
> **`NoReportExportService`** (no-op throw `NotSupportedException`) khi chưa có implementation nào — nhưng cách đó
> khiến production phụ thuộc **thứ tự đăng ký** ở `Program.cs`. **§4 đã sửa tận gốc:** `AddReportingModule`
> **tự** đăng ký `IReportPdfRenderer`/`IReportExcelRenderer`/`IReportExportService → ReportExportService` trước,
> rồi mới xét fallback ⇒ **không còn yêu cầu "phải đăng ký trước"**; fallback chỉ còn là lưới an toàn cho host
> validate. Đã verify: graph resolve ra `ReportExportService` thật; app boot `GET /api/health` 200.

### 3.1 Interface (`Services/IReportService.cs`)

```csharp
public interface IReportService
{
    Task<ReportSummary> GetSummaryAsync(
        Guid workspaceId, Guid? boardId, DateTimeOffset? from, DateTimeOffset? to,
        Guid userId, CancellationToken ct = default);

    Task<ReportExportResult> BuildExportAsync(
        Guid workspaceId, Guid? boardId, string format, DateTimeOffset? from, DateTimeOffset? to,
        Guid userId, CancellationToken ct = default);

    Task<IReadOnlyList<ReportBoardOption>> ListBoardsAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default);
}
```

### 3.2 `ReportService` — thứ tự thực thi cố định

- [x] Một mốc `DateTimeOffset.UtcNow` (`now`) cho CẢ request, lấy **trước** `EnsureEnabled` — dùng chung cho `Period.To`, quy tắc quá hạn và `GeneratedAt` ⇒ `GeneratedAt == Period.To` (verify P24).
- [x] `Enabled = false` ⇒ ném `ReportingDisabledException` (503) **trước** khi chạm DB (fail nhanh; verify P6 với user rác).
- [x] `IWorkspaceAccess.RequireManagerAsync(workspaceId, userId, ct)` ⇒ 403 Member / 404 workspace lạ (không lộ sự tồn tại).
- [x] Validate `format` (chỉ ở đường export) **strict**: `pdf`/`excel` (`ReportExportFormats.Normalize`, case-insensitive + trim); thiếu/rỗng/khác ⇒ `InvalidReportFormatException` (400) — **trước** mọi truy vấn.
- [x] Validate `from > to` ⇒ `InvalidReportRangeException` (400) rồi clamp qua `ReportsOptions.EffectiveRange(from, to, now)`.
- [x] `boardId.HasValue` ⇒ nạp board và kiểm tra `board.WorkspaceId == workspaceId` ⇒ sai/đã xoá mềm ⇒ **404** `"Board not found."`.
- [x] **Nạp bounded** (mọi query `AsNoTracking()`, tổng **8 truy vấn**/request summary):
  - `Workspaces` → `Name`;
  - `Boards` (scope) → `{ Id, Name }`;
  - `BoardColumns` (theo board ids) → `{ Id, BoardId, Name, IsDone }`;
  - `Tasks` (theo board ids) → chiếu thẳng `ReportTaskSnapshot` (`IsDone = Column != null && Column.IsDone`, `AssigneeName = Assignee != null ? Assignee.DisplayName : null`, `Priority` → string) (**một** query, không N+1);
  - `activity_logs` ×2 trong cửa sổ: (a) gom theo `Action` + `Count`, (b) `Distinct().Count()` cho `ActiveUsers` — chỉ `{ Action, UserId, CreatedAt }`, **không** đọc `payload`. Khi scope board: `BoardId == boardId \|\| BoardId == null`;
  - `ai_observer_runs` `Status == Completed` + `StartedAt ∈ [From, To]` (scope) → `Summary` (parse phòng thủ: JSON hỏng/`NULL`/không phải object ⇒ **bỏ qua** row, không tính `runsScanned`).
- [x] Gọi `ReportAggregator.Build(snapshot, thresholds)` → `ReportSummary`.
- [x] Đường export ⇒ `IReportExportService.Build(summary, format, options)` → `ReportExportResult` (stub trong harness nhận **đúng** `ReportSummary` của đường summary — verify P25).
- [x] **Bất biến §3:** không `SaveChangesAsync`, không `ExecuteUpdate/ExecuteDelete`, không `Add/Remove`, không transaction ghi, không đọc/ghi file (verify P29–P31).
- [x] `ListBoardsAsync`: `RequireManagerAsync` ⇒ 3 truy vấn (boards + đếm task theo board + đếm cột `is_done`), ghép rồi sort `Name` asc / `Id`.

### 3.3 Exception contract (`Services/ReportingExceptions.cs`)

- [x] `ReportingDisabledException : BoardModuleException` (503), `InvalidReportRangeException : BoardModuleException` (400),
      `InvalidReportFormatException : BoardModuleException` (400) — nhờ kế thừa `BoardModuleException`, `DomainExceptionFilter`
      có sẵn map thành `{ "error": "…" }` **không cần viết filter mới**.
- [x] Ghi rõ trong docstring: tái dùng `NotFoundException`/`ForbiddenException` của Board (403/404) để mã lỗi **giống hệt** Phase 5.

---

## 4. Backend – Renderer (thuần đầu vào `ReportSummary`, không DB)

> **Trạng thái: ✅ ĐÃ HIỆN THỰC & VERIFY (43 check PASS).** File: `Contracts/ReportFileName.cs`,
> `Contracts/ReportLabels.cs`, `Services/IReportPdfRenderer.cs` + `ReportPdfRenderer.cs`,
> `Services/IReportExcelRenderer.cs` + `ReportExcelRenderer.cs`, `Services/ReportExportService.cs`
> (interface + implementation), `Endpoints/ReportingEndpoints.cs` (khung); csproj thêm **QuestPDF 2026.8.0**
> + **ClosedXML 0.105.1**. Harness ngoài workspace (`%TEMP%\tnv4`, đã xoá; **không** DB) chạy **43/43 PASS**:
> C1–C6 tên file, C7–C14 PDF (đọc lại bằng **PdfPig**), C15–C24 Excel (round-trip bằng **ClosedXML**),
> C25–C28 `ReportExportService`, C29–C33 bất biến (không IO/DB/file rác), C34–C36 hiệu năng.
> `dotnet build TeamNexus.sln` **0 warning / 0 error**; `dotnet ef migrations list` vẫn **5 migration**;
> `git diff` **không** chạm Board/Ai/Persistence; app boot thật `GET /api/health` **200** + log
> `Reporting module: enabled=True, …`. Hiệu năng đo thật: PDF đầy đủ **13ms**/59KB, Excel **21ms**;
> bảng lớn (10 board + 50 người + 30 action ⇒ 4 trang) PDF **36ms**, Excel **68ms**.
>
> **Điều chỉnh so với bản sơ bộ (có chủ ý):**
> - **Bỏ yêu cầu thứ tự đăng ký** (thay cảnh báo "§4 phải đăng ký TRƯỚC `AddReportingModule`"): `AddReportingModule`
>   **tự** đăng ký `IReportPdfRenderer`/`IReportExcelRenderer`/`IReportExportService → ReportExportService` rồi mới
>   xét fallback `NoReportExportService` ⇒ không còn footgun; harness §3 (`IReportExportService` được đăng ký đè)
>   vẫn tương thích vì DI lấy đăng ký cuối.
> - **QuestPDF license set trong constructor `ReportPdfRenderer`** (không ở `AddReportingModule`) để renderer tự đủ —
>   đã bắt được thực tế: gọi renderer trực tiếp mà chưa set license thì QuestPDF ném ngay.
> - **API data bar**: ClosedXML 0.105 **không** có `cell.AddDataBar()` như câu chữ §4.4; API thật là
>   `cell.AddConditionalFormat().DataBar(color, showValue)` — đã dùng và verify (`ConditionalFormats` round-trip).
> - **Thêm `Contracts/ReportLabels.cs`**: nhãn tiếng Việt cho action/signal/severity (mirror
>   `notification.types`/`NotificationItem.tsx`), kèm mã gốc trong ngoặc để truy vết (D24).
> - **`Endpoints/ReportingEndpoints.cs`** được tạo ở §4 (khung `MapReportingEndpoints`), `MapReportingModuleEndpoints`
>   đổi sang block ⇒ `Program.cs` vẫn không phải sửa ở §5.
> - `Rows` của `ReportExportResult` chốt nghĩa (D25): tổng dòng chi tiết `byBoard + byAssignee + byAction +
>   signalsByType + findingsBySeverity`, dùng chung cho cả 2 định dạng.
> - `ReportPdfRenderer` nhận `(ReportSummary, ReportsOptions)` (không chỉ `ReportSummary`) vì cần khổ trang/tên đơn vị/
>   cờ `IncludeHealthSection`; interface đặt ở `Services/` cạnh implementation (tiền lệ §3), không ở `Contracts/`.

### 4.1 `Contracts/ReportSummary.cs` + `ReportExportResult.cs`

- [x] **`ReportSummary` + record con (`ReportRange`/`ReportProgress`/`ReportPerformance`/`ReportBoardRow`/`ReportAssigneeRow`/`ReportActivity`/`ReportHealth`/`ReportTruncation`) đã tạo ở §1** tại `Contracts/ReportSummary.cs` — nguồn duy nhất cho cả aggregation, response JSON và 2 renderer.
- [x] **`ReportExportResult(byte[] Content, string FileName, string ContentType, int Rows, bool RowCapReached, string PeriodLabel, string ScopeLabel)` đã tạo ở §3** (`Contracts/ReportExportResult.cs`) vì `IReportService.BuildExportAsync` trả chính type này.

### 4.2 `Contracts/ReportFileName.cs` (public static, thuần — verify không cần renderer)

- [x] `Build(string scopeLabel, string extension, DateTimeOffset generatedAt)` ⇒ `teamnexus-report-<slug>-<yyyyMMdd-HHmm>.<ext>`.
- [x] `Slug`: bỏ dấu tiếng Việt (map `đ→d`, `Đ→D`, chuẩn hoá NFD + strip combining marks), chỉ giữ `[a-z0-9-]`, gộp `-`, cắt ≤ 40 ký tự; rỗng ⇒ `"workspace"`.
- [x] Bảo đảm **ASCII-only** (yêu cầu của header `Content-Disposition`): verify có case tên board tiếng Việt ("Bảng Kiểm Thử Đợt 2" ⇒ `bang-kiem-thu-dot-2`).
- [x] `Normalize` còn lọc ký tự điều khiển/đường dẫn (`..`, `/`, `\`, `"`) ⇒ tên file an toàn cho header; có bảng map literal dự phòng nếu runtime bật invariant globalization.
- [x] *(mới)* `Contracts/ReportLabels.cs`: nhãn tiếng Việt cho action/signal/severity + helper `WithCode` (D24).

### 4.3 `IReportPdfRenderer` / `ReportPdfRenderer`

- [x] `byte[] Render(ReportSummary report, ReportsOptions options)` — QuestPDF `Document.Create(...)`, **không** ghi file; `document.GeneratePdf()` trả `byte[]`.
- [x] Bố cục (đã hiện thực đủ 9 mục, verify bằng text-extract C11/C12/C14):
  1. **Trang bìa:** tiêu đề "Báo cáo Tiến độ & Hiệu suất", tên workspace (+ tên board nếu scope board), `PeriodLabel`, `GeneratedAt` (UTC, ghi rõ "UTC"), `CompanyName`.
  2. **Tiến độ (hiện tại):** bảng `total/done/open/overdue/donePercent` + thanh tiến độ.
  3. **Hiệu suất (trong cửa sổ):** bảng metric + **cột định nghĩa** lấy từ `metricDefinitions` (báo cáo tự giải thích).
  4. **Theo bảng:** bảng `byBoard[]`.
  5. **Theo người:** bảng `byAssignee[]` (chỉ hiển thị khi Manager/Admin — đúng quyền truy cập).
  6. **Hoạt động:** `byAction[]` + `activeUsers` + `actionsPerDay`.
  7. **Sức khoẻ AI** (nếu `IncludeHealthSection`): `signalsByType[]` + `findingsBySeverity[]` + `runsScanned`.
  8. **Ghi chú cuối:** "Sinh tự động lúc … · Không lưu trên server · Nguồn: tasks/activity_logs/ai_observer_runs" + cảnh báo **`rowCapReached`** ("Dữ liệu đã được cắt ở N dòng").
  9. Footer mọi trang: `CompanyName` + `x / y`.
- [x] **Tiếng Việt có dấu render đúng (đã verify bằng PdfPig text-extract):** dùng font mặc định có sẵn của QuestPDF (Lato) — **không** trỏ tới font hệ thống (máy CI/deploy không có). Verify bằng text-extract ở §8 nhóm C.
- [x] Page size theo `ReportsOptions.PdfPageSize`; `A3`/`Letter` truyền vào `Page` size, giá trị lạ ⇒ A4 + log Warning (verify C13/C13b).

### 4.4 `IReportExcelRenderer` / `ReportExcelRenderer`

- [x] `byte[] Render(ReportSummary report, ReportsOptions options)` — `XLWorkbook` → `MemoryStream` → `byte[]` (**không** ghi file).
- [x] **5 sheet, tên cố định (verify bằng tên):** `Tổng quan`, `Theo bảng`, `Theo người`, `Hoạt động`, `Sức khoẻ AI`.
- [x] Mỗi sheet: hàng 1 = tiêu đề báo cáo (workspace/board + period + generatedAt) — ghi chú ngay trong file; hàng header bảng **in đậm + nền màu**; `FreezeRows(1..2)`; `SetAutoFilter()`; `Columns().AdjustToContents()` (cap width ~60 để không tràn).
- [x] Format dùng literal format code (`0.0`, `0.0"%"`, `0`) + ngày theo `Reports:ExcelDateFormat` ⇒ không phụ thuộc culture.
- [x] **Data bar** cho cột số task và `%` — API thật: `cell.AddConditionalFormat().DataBar(color, true)` (ClosedXML 0.105 **không** có `cell.AddDataBar()`): (`< 50%` đỏ, `< 80%` vàng, còn lại xanh) — thay cho chart (D15).
- [x] Không dùng công thức dẫn xuất: mọi giá trị **literal** (verify round-trip: không cell `#REF!`/`#N/A`/`#NAME?`/`#VALUE!`).
- [x] `ContentType` = `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` (verify C26).

### 4.5 `Services/IReportExportService.cs` + `ReportExportService`

- [x] `ReportExportResult Build(ReportSummary summary, string format, ReportsOptions options)` — chọn renderer theo `format`
      (`ReportExportFormats.Pdf = "pdf"`, `ReportExportFormats.Excel = "excel"`), build tên file bằng `ReportFileName.Build`, đếm `Rows` (số dòng chi tiết đã ghi), set `ContentType`.
- [x] **Đường export render lại trên cùng `ReportSummary`** mà `ReportService` đã dựng cho `GET .../reports/summary` (một nguồn số liệu duy nhất
      cho cả JSON lẫn PDF/Excel ⇒ 3 bề mặt không thể lệch nhau); **không** có truy vấn thứ hai trong renderer.

---

## 5. Backend – DTO, Endpoints

> **Trạng thái: ✅ ĐÃ HIỆN THỰC & VERIFY (34 check PASS).** File: `DTOs/ReportDtos.cs` (16 record + `From(...)`),
> `Services/ReportingExceptions.cs` (+`InvalidReportParameterException` 400), `Endpoints/ReportingEndpointHelpers.cs`,
> `Endpoints/ReportingEndpoints.cs` (3 route), `src/TeamNexus.Api/Program.cs` (+`public partial class Program`).
> Harness (`%TEMP%\tnv5`, đã xoá) boot **API thật 3 instance** (cổng tạm, `dotnet run --no-build`) + PostgreSQL
> thật + **JWT tự ký** trong cookie `access_token`: D1–D4 summary shape, D5–D8 scope, D9–D13 strict ISO-8601,
> D14–D20 export (PDF/Excel qua HTTP, mở lại bằng PdfPig/ClosedXML), D21–D22 boards, D25–D28 quyền/auth,
> D29 disabled 503, D30 hạ cap ⇒ header `true`, D31 OpenAPI tag `Reports` + 3 route, D32 bất biến dữ liệu.
> `dotnet build TeamNexus.sln` **0 warning / 0 error**; DB về baseline (0 row fixture); `git diff` **không** chạm
> Board/Ai/Persistence; OpenAPI `Reports` đủ 3 route.
>
> **⚠️ Bug thật đã bắt được & sửa (nhờ harness):** header `X-Report-Period` đưa **nguyên** nhãn kỳ của domain
> (dùng en dash `–`, U+2013) ⇒ Kestrel ném `Invalid non-ASCII or control character in header` ⇒ **mọi request
> export trả 500** (`D14/D16` fail đúng như vậy). Đã sửa bằng `Ascii(...)` (en/em dash → `-`, bỏ dấu còn sót,
> ký tự ngoài ASCII in được → `-`); bản có dấu vẫn nằm trong file PDF/Excel.
>
> **Quyết định & điều chỉnh (tiếp D1–D28):** **D29** thêm `public partial class Program { }` (bắt buộc để test
> HTTP bằng entry point thật); **D30** endpoint là tầng mỏng, không lặp validate của §3; **D31** `from`/`to`
> parse strict ISO-8601 ở endpoint + `InvalidReportParameterException` (400 `{error}`); **D32** `boardId`/
> `workspaceId` **cố ý** để framework bind (sai ⇒ 400 ProblemDetails, ghi known note); **D33** export trả
> `Results.File(...)` + 3 header phụ (`X-Report-Row-Cap-Reached`, `X-Report-Rows`, `X-Report-Period` — ASCII);
> **D34** DTO ở `DTOs/`, domain giữ ở `Contracts/`; **D35** `metricDefinitions` trả nguyên map; **D37** hợp đồng
> JSON đóng băng cho §6 (xem khối "🔻 BÀN GIAO §6").

### 5.1 DTO (`DTOs/ReportDtos.cs`)

- [x] ```csharp
  public sealed record ReportPeriodResponse(DateTimeOffset From, DateTimeOffset To, int Days, bool Clamped);
  public sealed record ReportScopeResponse(string Type, Guid? BoardId, string? BoardName);
  public sealed record ReportProgressResponse(int Total, int Done, int Open, int Overdue, double DonePercent);
  public sealed record ReportPerformanceResponse(
      int CreatedInRange, int CompletedInRange, double? AvgCompletionHours, double? AvgLeadTimeHours,
      double? OnTimeRate, double OverdueRate, double ThroughputPerWeek, int CompletedAtMissing);
  public sealed record ReportBoardRowResponse(
      Guid BoardId, string BoardName, int Total, int Done, int Open, int Overdue,
      int CompletedInRange, double? AvgCompletionHours, double? OnTimeRate);
  public sealed record ReportAssigneeRowResponse(
      Guid? AssigneeId, string AssigneeName, int Total, int Done, int Open, int Overdue,
      int CompletedInRange, double? AvgCompletionHours, double? OnTimeRate);
  public sealed record ReportActionRowResponse(string Action, int Count);
  public sealed record ReportActivityResponse(
      int TotalActions, IReadOnlyList<ReportActionRowResponse> ByAction, int ActiveUsers, double ActionsPerDay);
  public sealed record ReportSeverityRowResponse(string Severity, int Count);
  public sealed record ReportSignalTypeRowResponse(string Type, int Count);
  public sealed record ReportHealthResponse(
      int RunsScanned, IReadOnlyList<ReportSignalTypeRowResponse> SignalsByType,
      IReadOnlyList<ReportSeverityRowResponse> FindingsBySeverity);
  public sealed record ReportTruncationResponse(bool RowCapReached, int MaxRows);
  public sealed record ReportSummaryResponse(
      Guid WorkspaceId, string WorkspaceName, ReportScopeResponse Scope, ReportPeriodResponse Period,
      DateTimeOffset GeneratedAt, ReportProgressResponse Progress, ReportPerformanceResponse Performance,
      IReadOnlyList<ReportBoardRowResponse> ByBoard, IReadOnlyList<ReportAssigneeRowResponse> ByAssignee,
      ReportActivityResponse Activity, ReportHealthResponse Health,
      IReadOnlyDictionary<string, string> MetricDefinitions, ReportTruncationResponse Truncated);
  public sealed record ReportBoardOptionResponse(Guid Id, string Name, int TaskCount, int IsDoneColumns);
  ```
- [x] `ReportSummaryResponse.From(ReportSummary)` (một chiều domain → HTTP, không lặp khởi tạo rải rác — tiền lệ `ObserverRunFinding.From`).
      **Bắt buộc ở §5** (không phải §3): `IReportService` trả domain `ReportSummary`, endpoint §5 map sang DTO;
      gồm cả `ReportBoardOptionResponse.From(ReportBoardOption)` cho `GET .../reports/boards`.
      `ReportRange` (domain) → `ReportPeriodResponse` (DTO) suy ra `From/To/Days/Clamped`; `PeriodLabel` dùng cho header `X-Report-Period`.
- [x] `MetricDefinitions` là **map tiếng Việt** `tên metric → mô tả` (D9) ⇒ PDF/Excel/UI cùng nguồn mô tả, không có tài liệu metric thứ hai.

### 5.2 `Endpoints/ReportingEndpoints.cs`

- [x] Group `/api/workspaces/{workspaceId:guid}/reports` `.WithTags("Reports")` + `.AddEndpointFilter<DomainExceptionFilter>()`; mọi route `.RequireAuthorization()`; **không** route nào gắn `AntiforgeryValidationEndpointFilter` (đều GET — D12).

| # | Method + path | Query | Thành công | Lỗi |
|---|---|---|---|---|
| 1 | `GET /api/workspaces/{workspaceId}/reports/summary` | `boardId?`, `from?`, `to?` | **200** `ReportSummaryResponse` | 400 `{ error }` (from/to/format) · 403 Member · 404 workspace/board lạ · 503 tắt |
| 2 | `GET /api/workspaces/{workspaceId}/reports/export` | `format=pdf\|excel` (**bắt buộc**), `boardId?`, `from?`, `to?` | **200** file bytes + `Content-Disposition: attachment; filename="…"` + `Content-Length` + `X-Report-Row-Cap-Reached: true\|false` + `X-Report-Period` | như trên (400 khi thiếu/sai `format`) |
| 3 | `GET /api/workspaces/{workspaceId}/reports/boards` | — | **200** `ReportBoardOptionResponse[]` | 403 · 404 · 503 |

- [x] Handler trả `Results.File(result.Content, result.ContentType, fileDownloadName: result.FileName)` ⇒ `FileContentResult` in-memory;
      thêm 2 header tuỳ biến qua `http.Response.Headers.Append(...)` (ghi trong docstring: header là *thông tin phụ*, UI **không** phụ thuộc nó để hiển thị đúng).
- [x] Parse `from`/`to` **strict ISO-8601** (`DateTimeOffset.TryParse` với `CultureInfo.InvariantCulture` + `DateTimeStyles.AssumeUniversal`):
      giá trị rác ⇒ **400** `{ error: "Unknown value for from 'xyz'. Expected ISO-8601 date-time." }` — cùng tinh thần `ParseIsRead` Phase 5 §5.2.
- [x] Đọc `userId` bằng `Endpoints/ReportingEndpointHelpers.cs` (bản copy `RequireUserId` — board's `CurrentUser` là `internal`).
- [x] **Không** trả `SkippedReason`/chi tiết nội bộ ra HTTP; body lỗi **luôn** `{ "error": "…" }` (trừ 400 ProblemDetails do framework,
      ví dụ `?boardId=abc` — ghi chú như Phase 5 §5.5).

### 5.3 Mã lỗi & contract cho UI

| Status | Khi nào | `error` thật | Gợi ý UI |
|---|---|---|---|
| 400 | `from`/`to` sai định dạng, `from > to`; `format` thiếu/sai | `"Unknown value for from 'xyz'. Expected ISO-8601 date-time."` / `"Unknown report format 'csv'. Expected pdf or excel."` | `Alert` lỗi, giữ nguyên filter đang chọn |
| 401 | hết phiên | — | `httpClient` tự refresh; vẫn lỗi ⇒ điều hướng login |
| 403 | Member gọi route Reports (không phải Manager/Admin) | `"Requires Manager or Admin role in this workspace."` | **Ẩn** nút "Báo cáo" với Member; nếu vẫn gặp ⇒ `Alert` |
| 404 | workspace lạ / `boardId` không thuộc workspace | `"Workspace not found or you are not a member."` / `"Board not found."` | `Alert` + reload + reset filter board |
| 503 | `Reports:Enabled=false` | `"Reporting is disabled."` | `Alert` "Tính năng báo cáo đang tạm tắt" |
| 400 (framework) | query sai kiểu (`?boardId=abc`) ⇒ **ProblemDetails** (không có `error`) | — | xử lý theo status 400 |

---

## 🔻 BÀN GIAO §6 (§6.1–§6.4) — backend Giai đoạn 6 đã xong & verify

> Backend đã verify hết: §1 **76/76**, §3 **37/37**, §4 **43/43**, §5 **34/34**. Ba endpoint + DTO **đóng băng**
> (đổi gì cũng phải sửa cả hai phía). Cứ code theo contract dưới đây, **không** hỏi lại.

### 1) Ba route (base `/api`, dùng `frontend/src/shared/api/httpClient.ts`)

| # | Method + path | Query | Thành công | Ghi chú |
|---|---|---|---|---|
| 1 | `GET /workspaces/{workspaceId}/reports/summary` | `boardId?`, `from?`, `to?` | **200** `ReportSummaryResponse` | `from`/`to` ISO-8601 (có/không offset đều được; `?from=` rỗng = thiếu); thiếu cả hai ⇒ 30 ngày |
| 2 | `GET /workspaces/{workspaceId}/reports/export` | `format=pdf\|excel` (**bắt buộc**), `boardId?`, `from?`, `to?` | **200** file bytes | `responseType: 'blob'`; header phụ `x-report-row-cap-reached`, `x-report-rows`, `x-report-period` (**ASCII**) |
| 3 | `GET /workspaces/{workspaceId}/reports/boards` | — | **200** `ReportBoardOptionResponse[]` | sort theo tên; dùng cho dropdown bộ lọc |

`httpClient` tự gắn `X-XSRF-TOKEN` cho method mutating — 3 route này đều **GET** nên **không** cần CSRF.

### 2) Kiểu TS cần mirror (viết tay theo `DTOs/ReportDtos.cs`)

```ts
export type ReportScopeType = 'workspace' | 'board'
export type ReportFormat = 'pdf' | 'excel'

export interface ReportPeriodResponse { from: string; to: string; days: number; clamped: boolean }
export interface ReportScopeResponse { type: ReportScopeType; boardId: string | null; boardName: string | null }
export interface ReportProgressResponse { total: number; done: number; open: number; overdue: number; donePercent: number }
export interface ReportPerformanceResponse {
  createdInRange: number; completedInRange: number
  avgCompletionHours: number | null; avgLeadTimeHours: number | null; onTimeRate: number | null
  overdueRate: number; throughputPerWeek: number; completedAtMissing: number
}
export interface ReportBoardRowResponse {
  boardId: string; boardName: string; total: number; done: number; open: number; overdue: number
  completedInRange: number; avgCompletionHours: number | null; onTimeRate: number | null
}
export interface ReportAssigneeRowResponse {
  assigneeId: string | null; assigneeName: string
  total: number; done: number; open: number; overdue: number
  completedInRange: number; avgCompletionHours: number | null; onTimeRate: number | null
}
export interface ReportActionRowResponse { action: string; count: number }
export interface ReportActivityResponse {
  totalActions: number; byAction: ReportActionRowResponse[]; activeUsers: number; actionsPerDay: number
}
export interface ReportSeverityRowResponse { severity: string; count: number }
export interface ReportSignalTypeRowResponse { type: string; count: number }
export interface ReportHealthResponse {
  runsScanned: number; signalsByType: ReportSignalTypeRowResponse[]; findingsBySeverity: ReportSeverityRowResponse[]
}
export interface ReportTruncationResponse { rowCapReached: boolean; maxRows: number }
export interface ReportSummaryResponse {
  workspaceId: string; workspaceName: string
  scope: ReportScopeResponse; period: ReportPeriodResponse; generatedAt: string
  progress: ReportProgressResponse; performance: ReportPerformanceResponse
  byBoard: ReportBoardRowResponse[]; byAssignee: ReportAssigneeRowResponse[]
  activity: ReportActivityResponse; health: ReportHealthResponse
  metricDefinitions: Record<string, string>
  truncated: ReportTruncationResponse
}
export interface ReportBoardOptionResponse { id: string; name: string; taskCount: number; isDoneColumns: number }
```

### 3) JSON thật (chụp từ lần verify §5 — dùng làm fixture test)

```json
// GET /api/workspaces/{wsId}/reports/boards → 200
[ { "id": "0f2a3b41-3333-4333-8333-cccccccc0002", "name": "Board Beta", "taskCount": 1, "isDoneColumns": 0 },
  { "id": "0f2a3b41-3333-4333-8333-cccccccc0001", "name": "Bảng Alpha", "taskCount": 5, "isDoneColumns": 1 } ]

// GET /api/workspaces/{wsId}/reports/summary?boardId=…&from=2026-08-01T00:00:00Z&to=2026-09-10T15:00:00Z → 200 (rút gọn)
{ "workspaceId": "0f2a3b41-2222-4222-8222-bbbbbbbb0001",
  "workspaceName": "Workspace Kiểm Thử",
  "scope": { "type": "board", "boardId": "0f2a3b41-3333-4333-8333-cccccccc0001", "boardName": "Bảng Alpha" },
  "period": { "from": "2026-08-01T00:00:00+00:00", "to": "2026-09-10T15:00:00+00:00", "days": 41, "clamped": false },
  "generatedAt": "2026-09-10T15:37:25.75+00:00",
  "progress": { "total": 5, "done": 3, "open": 2, "overdue": 2, "donePercent": 60.0 },
  "performance": { "createdInRange": 4, "completedInRange": 2, "avgCompletionHours": 636.0,
                   "avgLeadTimeHours": 504.0, "onTimeRate": 0.0, "overdueRate": 100.0,
                   "throughputPerWeek": 0.3, "completedAtMissing": 1 },
  "byBoard": [ { "boardId": "…", "boardName": "Bảng Alpha", "total": 5, "done": 3, "open": 2, "overdue": 2,
                 "completedInRange": 2, "avgCompletionHours": 636.0, "onTimeRate": 0.0 } ],
  "byAssignee": [ { "assigneeId": "…", "assigneeName": "S5 Member", "total": 3, "done": 2, "open": 1,
                    "overdue": 1, "completedInRange": 2, "avgCompletionHours": 636.0, "onTimeRate": 0.0 },
                  { "assigneeId": null, "assigneeName": "Chưa gán", "total": 2, "done": 1, "open": 1,
                    "overdue": 1, "completedInRange": 0, "avgCompletionHours": null, "onTimeRate": null } ],
  "activity": { "totalActions": 3, "byAction": [ { "action": "TaskCreated", "count": 1 },
                 { "action": "TaskDeleted", "count": 1 }, { "action": "TaskMoved", "count": 1 } ],
                "activeUsers": 2, "actionsPerDay": 0.1 },
  "health": { "runsScanned": 1,
              "signalsByType": [ { "type": "OverdueTask", "count": 3 }, { "type": "StalledTask", "count": 2 } ],
              "findingsBySeverity": [ { "severity": "Unknown", "count": 3 }, { "severity": "High", "count": 1 },
                                      { "severity": "Medium", "count": 1 } ] },
  "metricDefinitions": { "total": "Số task hiện có trong phạm vi báo cáo (không tính task đã xoá mềm).",
                         "done": "Task có completed_at, hoặc đang nằm ở cột được đánh dấu is_done.", "…": "…" },
  "truncated": { "rowCapReached": false, "maxRows": 5000 } }

// Lỗi thật
{ "error": "Unknown value for from 'bogus'. Expected ISO-8601 date-time." }   // 400
{ "error": "Unknown report format 'CSV'. Expected pdf or excel." }           // 400
{ "error": "Requires Manager or Admin role in this workspace." }             // 403 (Member)
{ "error": "Workspace not found or you are not a member." }                  // 404
{ "error": "Board not found." }                                             // 404
{ "error": "Reporting is disabled." }                                       // 503
```

### 4) Điểm cần chú ý khi code UI (đã chốt, đừng code sai kỳ vọng)

- **Dữ liệu thật là tiếng Việt có dấu** (`"Bảng Alpha"`, `"Chưa gán"`); JSON escape `\u1EA3ng` — `JSON.parse`
  (axios tự làm) trả đúng ký tự, **không** cần decode tay.
- **`double?` = `null` khi mẫu số 0** ⇒ UI hiển thị `—`, **không** hiển thị `0` (đã có tiền lệ ở §1 D8).
- **`metricDefinitions`** là `Record<string,string>` ⇒ có thể hiển thị tooltip/mô tả ngay trên UI, cùng nguồn
  với PDF/Excel (không có tài liệu metric thứ hai).
- **`scope.type === 'board'`** ⇒ bộ lọc đang giới hạn 1 board; tiêu đề nên hiện `workspaceName › boardName`.
- **`truncated.rowCapReached`** ⇒ hiển thị cảnh báo "dữ liệu chi tiết đã bị cắt ở `maxRows` dòng" (số liệu tổng
  vẫn đầy đủ).
- **Export là tải file**: dùng `responseType: 'blob'` rồi `URL.createObjectURL` (D12) — **không** `window.open`,
  vì như vậy sẽ mất auto-refresh 401 của `httpClient`.
- **Lỗi export trả về Blob** (do `responseType: 'blob'`): phải `await blob.text()` → `JSON.parse` → lấy `.error`
  (đúng như §6.1 `utils/reportError.ts`); nếu không UI sẽ hiện "undefined".
- **Member**: ẩn nút "Báo cáo"/"Xuất báo cáo" (backend đã 403 cả 3 route, **đừng** chỉ dựa vào 403).
- **`Reports:Enabled=false`** ⇒ 503 `"Reporting is disabled."` (không phải 404) ⇒ có thể hiện "tính năng đang tạm tắt".
- **`generatedAt`/`period.from|to`** là ISO-8601 có offset `+00:00` ⇒ format bằng `dayjs` như các màn hình khác.

---

## 6. Frontend – `src/features/reporting/`

### 6.1 Types, API, utils

- [x] `types/reporting.types.ts`: mirror §5.1 (`ReportPeriod`, `ReportScope`, `ReportProgress`, `ReportPerformance`, `ReportBoardRow`, `ReportAssigneeRow`, `ReportActionRow`, `ReportActivity`, `ReportHealth`, `ReportSummary`, `ReportBoardOption`, `ReportFormat = 'pdf' | 'excel'`, `ReportExportResult`).
- [x] `services/reportingApi.ts` (qua `httpClient` base `/api`; **không** tự set `X-XSRF-TOKEN`):
  - `getReportSummary(workspaceId, params?: { boardId?: string; from?: string; to?: string }): Promise<ReportSummary>`
  - `listReportBoards(workspaceId): Promise<ReportBoardOption[]>`
  - `downloadReport(workspaceId, params: { format: ReportFormat; boardId?: string; from?: string; to?: string }): Promise<{ blob: Blob; fileName: string; rowCapReached: boolean }>`
    — `httpClient.get(url, { params, responseType: 'blob' })`; tên file đọc từ `content-disposition` (`filename="…"` **và** `filename*=UTF-8''…`, fallback `teamnexus-report.<ext>`); cờ đọc từ header `x-report-row-cap-reached`.
- [x] `utils/reportDownload.ts`: `saveBlob(blob, fileName)` — tạo `<a>` với `download`, `URL.createObjectURL`, `click()`, `URL.revokeObjectURL` (dọn ở `finally`).
- [x] `utils/reportError.ts`: `extractErrorMessage(err: unknown): Promise<{ status?: number; message: string }>` — vì `responseType: 'blob'`,
      lỗi JSON trở thành `Blob`: nếu `err.response.data instanceof Blob` ⇒ `await data.text()` → `JSON.parse` → lấy `.error`;
      fallback chuỗi tiếng Việt theo status (400/403/404/503). **Bắt buộc** — nếu thiếu, mọi lỗi export hiển thị "undefined".
- [x] `utils/reportFormat.ts`: format `null` giờ ⇒ `'—'`, `%` ⇒ `x.toFixed(1) + '%'` — dùng chung cho UI (tránh mỗi component tự format một kiểu).

### 6.2 Hooks

- [x] `hooks/useReportSummary.ts`: state `{ report, status: 'idle'|'loading', error, httpStatus, params }`; `reload(params?)`; `setBoard(id?)`, `setRange(presetOrFromTo)`; lỗi 401/403/404/503 map đúng; cleanup `ignore` flag chống set state sau unmount (pattern `BoardListPage`).
- [x] `hooks/useReportExport.ts`: state `{ exporting: 'pdf'|'excel'|null, error, httpStatus }`; `exportReport(format)` — chặn gọi trùng khi `exporting != null`; gọi API → `saveBlob` → `message.success("Đã tải báo cáo")`; lỗi ⇒ `extractErrorMessage` + `message.error`.
- [x] Cả 2 hook **không** phụ thuộc SignalR, **không** gọi lại API khi component unmount.

### 6.3 Components & page

- [x] `components/ReportFilters.tsx`: `Segmented` preset (7 / 30 / 90 ngày) + `DatePicker.RangePicker` (đồng bộ 2 chiều với Segmented) + `Select` board (option "Toàn workspace" + `listReportBoards`); `onChange(params)` debounce nhẹ (không gọi API mỗi lần gõ ngày).
- [x] `components/ReportSummaryPanel.tsx`: hàng `Statistic` (Tổng · Hoàn thành · Đang mở · Quá hạn · Đúng hạn · Hoàn thành TB (giờ) · Throughput/tuần) + `Progress` `donePercent` + bảng `byBoard` + bảng `byAssignee` + card hoạt động (`byAction`) + card sức khoẻ AI (ẩn khi `health.runsScanned = 0`); `Empty` khi `total = 0`; hiển thị cảnh báo khi `truncated.rowCapReached`.
- [x] `components/ReportExportDrawer.tsx`: `Radio.Group` (PDF / Excel) + 2 nút "Tải PDF" / "Tải Excel" (nút tương ứng `loading` + disable khi đang xuất) + `Alert` lỗi + ghi chú "File sinh tại chỗ, **không** lưu trên server".
- [x] `components/ReportTable.tsx`: wrapper bảng Ant Design dùng chung (columns + `rowKey` + `loading` + `Empty`) cho `byBoard`/`byAssignee`/`byAction`.
- [x] `pages/ReportsPage.tsx` (`/workspaces/:workspaceId/reports`, đọc `?boardId=` từ URL): layout giống `BoardListPage` (Header + `Content` maxWidth),
      role check qua `GET /api/workspaces` (copy pattern `BoardView`), **Member ⇒ `Result status="403"`** + **không** render số liệu;
      nút "Làm mới" + nút "Xuất báo cáo" mở `ReportExportDrawer`.
- [x] `index.ts` export types/api/hooks/components/pages.

### 6.4 Tích hợp

- [x] `app/router.tsx`: thêm route `<Route path="/workspaces/:workspaceId/reports" element={<ProtectedRoute><ReportsPage /></ProtectedRoute>} />`.
- [x] `features/board/pages/BoardListPage.tsx`: thêm nút **"Báo cáo"** (`BarChartOutlined`) cạnh "Tạo Bảng Mới" ⇒ `navigate(`/workspaces/${workspaceId}/reports`)`.
- [x] `features/board/components/BoardView.tsx`: thêm nút **"Báo cáo"** (`BarChartOutlined`) cạnh nút "AI Observer", dùng state `isManagerOrAdmin` đã có để **ẩn với Member**; `onClick` ⇒ `navigate(`/workspaces/${workspaceId}/reports?boardId=${boardId}`)`.
- [x] **Không** sửa `shared/api/httpClient.ts`, `features/board/stores/boardStore.ts`, `features/board/hooks/useBoardHub.ts`, `features/ai/*`.

---

## 7. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–5)

### 7.1 Verify backend (harness tạm ngoài workspace + API thật + PostgreSQL thật)

> Cùng cách Phase 3 §Verify §4 / Phase 4 §5.1 / Phase 5 §7.1: harness đặt **ngoài workspace** (`%TEMP%`), tự ký JWT bằng
> `Jwt:SigningKey` trong User Secrets, **xoá sau khi chạy**, không commit. Fixture tự tạo rồi hard-delete; DB về baseline;
> API đã stop; harness đã xoá. Vì Giai đoạn 6 không gọi AI ⇒ **không tốn token** và không cần `FakeAiProvider` cho đường export.
> ⚠️ Nhắc lại 2 lưu ý Phase 5: (a) `dotnet ef` **không** dùng `--no-build`; (b) `GET /api/auth/antiforgery` **sau** khi gắn JWT mới

> ## ✅ TRẠNG THÁI: §7.1 HOÀN TẤT — **74/74 check PASS** (nhóm A–F, harness `%TEMP%\tn71` đã xoá)
>
> | Nhóm | Nội dung | Kết quả |
> |---|---|---|
> | **A** | schema/migration/kiến trúc (9) | **9/9** |
> | **B** | hàm thuần: thresholds/range/filename/labels/aggregator (20) | **20/20** |
> | **C** | renderer PDF+Excel round-trip (14) | **14/14** |
> | **D** | HTTP API thật, 3 instance (22) | **22/22** |
> | **E** | bất biến dữ liệu & không lưu file (3) | **3/3** |
> | **F** | regression & build (6) | **6/6** |
>
> **Số liệu đo thật:** 5 migration (không phát sinh) · model snapshot **zero-diff** · `dotnet build` **0 warning/0 error** ·
> 1000 task build **1ms** · PDF **517ms** (61.925 B, 2 trang) · Excel **860ms** (15.942 B, đúng 5 sheet) ·
> export qua HTTP **61.868 B** · `Reporting module:` log **đúng 1 dòng** · `git diff` Board/Ai/Persistence **rỗng** ·
> `Program.cs` diff `15 + / 0 −` · DB về **baseline 0 row** sau cleanup.
> *(Ghi chú: 517ms/860ms là lần render **nguội** trong suite; đo **sau warm-up** ở §4 là 13ms/21ms — xem báo cáo test.)*
>
> **3 bug thật đã bắt được và sửa trong toàn Giai đoạn 6** (chi tiết ở báo cáo test):
> 1. `activeUsers` cộng count-distinct **theo từng action** ⇒ đếm trùng người (5 thay vì 2) → dùng 1 truy vấn `Distinct().Count()` cho cả cửa sổ.
> 2. `Period.To` và `GeneratedAt` lấy **2 mốc thời gian khác nhau** → dùng một `UtcNow` cho cả request (`GeneratedAt == Period.To`).
> 3. Header `X-Report-Period` chứa **en dash (U+2013)** ⇒ Kestrel ném ⇒ **mọi request export 500** → `Ascii(...)` hoá header.
> 4. *(hạ tầng)* Đăng ký `IReportService` làm **`dotnet ef` hỏng** (`ValidateOnBuild` không resolve được `IReportExportService`) → fallback có điều kiện, về sau bỏ hẳn footgun ở §4 (D21).

> echo đúng (token ẩn danh ⇒ 403) — nhưng Giai đoạn 6 toàn GET nên **không** cần CSRF.

**Ma trận check (mã hoá để báo cáo giống Phase 4/5):**

1. **A. Không xâm lấn — ✅ 9/9 PASS:** `git diff --name-only` chỉ gồm file **mới** + `Program.cs` / `appsettings.json` / `TeamNexus.sln`; `git diff --name-only` chỉ gồm file **mới** + `Program.cs` / `appsettings.json` / `TeamNexus.sln`;
   `git diff -- 'src/Modules/Board/*' 'src/Modules/Ai/*' 'src/TeamNexus.Persistence/*'` **rỗng**;
   `dotnet ef migrations list` vẫn **5 migration** (`20260910105154_Phase5AiObserverSchema` là cuối) và `TeamNexusDbContextModelSnapshot.cs` **zero-diff**;
   `TeamNexus.Modules.Ai.csproj`/`Board.csproj` không đổi; Board không có `using TeamNexus.Modules.Reporting`.
2. **B. Aggregation thuần — ✅ 20/20 PASS** (harness chỉ `ProjectReference` tới project Reporting — không DB/HTTP/AI):
   workspace rỗng (0 board/task) ⇒ mọi số 0, `donePercent = 0.0`, `avgCompletionHours = null`, **không** `NaN`/`Infinity`;
   `DueDate == Now` ⇒ **không** overdue; task cột `is_done` quá hạn ⇒ **không** overdue; `DueDate = null` ⇒ không overdue;
   `CompletedAt < DueDate` ⇒ on-time; `CompletedAt > DueDate` ⇒ late; task done **không có** `DueDate` ⇒ **loại** khỏi `onTimeRate`
   (mẫu 0 ⇒ `null`); task done thiếu `CompletedAt` ⇒ `done` vẫn tăng + `completedAtMissing = 1` + **không** vào `avgCompletionHours`;
   task done **trước** cửa sổ ⇒ **không** vào `completedInRange` nhưng **vẫn** vào `progress.done`;
   `throughputPerWeek` với `Days = 7/14/0`; `overdueRate` khi `open = 0`;
   gom nhóm theo board (2 board) & theo người (`assigneeId = null` ⇒ 1 dòng "Chưa gán", 2 assignee ⇒ 2 dòng);
   `activity` đếm đúng theo `action` + `activeUsers` loại `null`; `health` cộng dồn 2 run + **JSON summary hỏng ⇒ bỏ qua run đó không throw**;
   `metricDefinitions` có đủ key của mọi metric xuất hiện trong response;
   **tất định**: build 2 lần từ cùng snapshot ⇒ JSON **giống hệt** (so `JsonElement.DeepEquals` hoặc so chuỗi sau khi normalize);
   cap: `MaxExportRows = 3` + 10 board ⇒ 3 dòng + `rowCapReached = true`; `MaxActionsPerReport = 2` + 5 action ⇒ 2 dòng + cờ;
   1000 task ⇒ build < 50ms; **sort** ổn định khi 2 board cùng số liệu (tie-break theo tên).
3. **C. Renderer — ✅ 14/14 PASS** (harness dựng `ReportSummary` tay, mở lại file bằng PdfPig/ClosedXML):
   **PDF:** `Content[0..4] == "%PDF-"`, có `%%EOF`, `Length > 8KB`, `Content-Length` (endpoint) == `Length`,
   **text extract chứa** tên workspace + tên board + chuỗi có dấu tiếng Việt ("Báo cáo", "Hoàn thành", "Hiệu suất") ⇒ **không vỡ dấu**,
   chứa cảnh báo cắt dòng khi `rowCapReached = true`, chứa `GeneratedAt`;
   **Excel:** bắt đầu `PK\x03\x04`, mở lại bằng `ClosedXML` ⇒ đúng **5 sheet** (`Tổng quan`, `Theo bảng`, `Theo người`, `Hoạt động`, `Sức khoẻ AI`),
   ô header đúng nhãn, `Tổng quan` có `total/done` **khớp** aggregator, có `AutoFilter` + freeze pane, **không** cell `#REF!`/`#N/A`,
   data bar tồn tại ở cột %; cả 2 format trên cùng 1 summary ⇒ số liệu **khớp nhau** và khớp `GET summary` (§ nhóm D).
4. **D. HTTP API thật — ✅ 22/22 PASS** (API thật 3 instance + PostgreSQL thật + JWT tự ký qua cookie `access_token`):
   ≥ **P33: số truy vấn / request summary — ĐÃ ĐO = 8** (workspace, boards, columns, tasks, activity ×2, observer runs); nếu §4/§5 thêm truy vấn thì phải tăng budget này một cách có ý thức.
   *Đã verify trước ở §3 (harness DI thật + PostgreSQL thật, 37/37 PASS)*: quyền 403/404/503, scope board/workspace, số liệu khớp fixture,
   cửa sổ + clamp, export delegation, bất biến read-only, 8 truy vấn, tất định, log — xem khối "Trạng thái" đầu §3.
   Còn lại cho §7.1 nhóm D: **qua HTTP thật** (JWT + endpoint + CSRF/GET + mã lỗi framework + OpenAPI):
   `GET summary` (Manager) ⇒ **200** + shape đầy đủ (11 nhóm) + `generatedAt` ISO-8601;
   `?boardId` đúng ⇒ `scope.type = "board"` + số liệu chỉ của board đó; `boardId` **của workspace khác** ⇒ **404** `"Board not found."`;
   `?from=bogus` ⇒ **400** `{error}` (domain, **không** ProblemDetails); `?from > ?to` ⇒ **400**;
   `?from` cách đây 400 ngày ⇒ `period.clamped = true` + `period.days = 365`; thiếu `from`/`to` ⇒ `days = 30`;
   `GET export?format=pdf` ⇒ **200** + `Content-Type: application/pdf` + `Content-Disposition` chứa `attachment; filename="…pdf"` + bytes `%PDF-`;
   `format=excel` ⇒ `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` + bytes `PK`;
   `format=CSV`/`format=` (rỗng)/thiếu ⇒ **400** `{error}`;
   `GET boards` ⇒ **200** danh sách board + `taskCount`;
   **Member ⇒ 403 cả 3 route** (`"Requires Manager or Admin role in this workspace."`);
   **5 route không auth ⇒ 401**; workspace lạ ⇒ **404**;
   `Reports__Enabled=false` ⇒ **503** `"Reporting is disabled."` (cả 3 route);
   header `X-Report-Row-Cap-Reached` xuất hiện (`false` bình thường, `true` khi hạ cap);
   `?boardId=abc` ⇒ **400 ProblemDetails** (framework — ghi known note như Phase 5);
   OpenAPI document có tag `Reports` + **đủ 3 route**.
5. **E. Bất biến dữ liệu & không lưu file — ✅ 3/3 PASS:** đếm row trước/sau 1 lần `summary` + 2 lần `export`
   (`tasks`, `activity_logs`, `notifications`, `ai_observer_runs`, `ai_action_logs`) ⇒ **không đổi**;
   **không** file mới trong `%TEMP%`, thư mục app, hay `wwwroot` (so danh sách file trước/sau);
   `git status --porcelain` **rỗng** sau khi chạy harness (không sinh artifact);
   response export **không** có redirect/`X-Accel-Redirect` ⇒ byte đến từ RAM.
6. **F. Regression & build — ✅ 6/6 PASS:** `dotnet build TeamNexus.sln` ⇒ **0 warning / 0 error**;
   boot API thật ⇒ `GET /api/health` **200** + log `Reporting module: enabled=True, …` xuất hiện **đúng 1 dòng**;
   `Observer__Enabled=false` + `Reports__Enabled=false` ⇒ boot sạch, health 200;
   FE: `npm run lint` (oxlint 0/0) + `npx tsc -b` + `npm run build` + `npm test` sạch.

### 7.2 Frontend (Vitest + Testing Library)

> ## 🔻 BÀN GIAO §6 + §7.2 CHO ANTIGRAVITY
>
> **Backend Giai đoạn 6 đã xong & verify §7.1 (74/74 PASS nhóm A–F; cộng §1/§3/§4/§5 = 264/264 check)** — 3 endpoint
> `GET /api/workspaces/{id}/reports/{summary|export|boards}`, DTO và renderer PDF/Excel **sẽ không đổi nữa**.
> Contract đầy đủ (kiểu TS + JSON thật + bảng lỗi + lưu ý UI) ở mục **"🔻 BÀN GIAO §6"** phía trên — cứ code theo, **không** hỏi lại contract.
>
> **Baseline frontend đo ngày 2026-09-10 (trước khi làm §6):**
>
> | Lệnh | Kết quả |
> |---|---|
> | `npm run lint` (oxlint) | **0 warning / 0 error** (69 files, 116 rules) |
> | `npx tsc -b` | **exit 0** |
> | `npx vitest run` | **22 files / 122 tests PASS** (~33s) |
>
> ⇒ Không có test nào đỏ sẵn. DoD sau §6 + §7.2: 4 lệnh trên vẫn sạch (**số test phải tăng**, không giảm),
> cộng `npm run build` (tsc + vite) sạch.
>
> **Cách verify frontend — theo đúng pattern đang có, KHÔNG dựng backend:**
> - Mock `httpClient` (axios instance) giống `features/ai/services/__tests__/notificationApi.test.ts`: `vi.mock` module
>   `shared/api`, assert `method/URL/params` cho 3 hàm mới (kể cả nhánh `responseType: 'blob'`).
> - Hook: `renderHook` + `waitFor` (pattern `useNotifications.test.ts`); map lỗi 400/403/404/503; chặn gọi trùng khi đang export; cleanup đúng.
> - Component: `@testing-library/react` + `jsdom` (đã cấu hình ở `vitest.config.ts` / `src/test/setup.ts`); assert **text tiếng Việt có dấu** trực tiếp.
> - **Fixture**: copy khối "JSON thật" ở mục BÀN GIAO §6 (đã chụp từ lần verify thật) làm `mockResolvedValueOnce` — **không** tự bịa shape, **không** cần seed DB/chạy API.
> - Cập nhật `features/board/components/__tests__/BoardView.test.tsx`: nút "Báo cáo" **hiện** với Manager/Admin, **ẩn** với Member.
>
> **Sau khi xong §6 + §7.2:** (1) tick §6.1–§6.4 + §7.2 trong file này; (2) chạy 4 lệnh DoD và ghi **số test thật** vào §7.2;
> (3) bổ sung ảnh chụp (trang Báo cáo + drawer xuất PDF/Excel) vào `Project-Documents/report/phase-6-reporting-test-report.md`
> (mục "Phần UI — bổ sung sau §6") và tick 4 ô `03-roadmap.md` (dòng 67–70) — lúc đó Giai đoạn 6 mới khép lại.
>
> **Runbook dev:** `dotnet run --project src/TeamNexus.Api` (đặt `Observer__Enabled=false`) + `npm run dev`; vào workspace
> có role **Manager/Admin** → nút "Báo cáo" → chọn kỳ/board → "Xuất báo cáo".

- [x] `services/__tests__/reportingApi.test.ts`: đúng method/URL/params cho 3 hàm; nhánh `responseType: 'blob'`; parse `content-disposition`
      (có `filename` chuẩn, có `filename*=UTF-8''`, **không** có ⇒ fallback); đọc header `x-report-row-cap-reached`.
- [x] `utils/__tests__/reportDownload.test.ts`: `saveBlob` gọi `createObjectURL` → `click` → `revokeObjectURL`; `extractErrorMessage`
      với `Blob` JSON (đọc ra `{error}`), với `Blob` không phải JSON, và với lỗi mạng (không có `response`).
- [x] `hooks/__tests__/useReportSummary.test.ts`: `idle → loading → idle`; `reload` set `report`; lỗi 403/404/503 map đúng message; đổi `boardId` ⇒ gọi API với param mới.
- [x] `hooks/__tests__/useReportExport.test.ts`: `exportReport('pdf')` ⇒ `exporting = 'pdf'` → gọi API → `saveBlob` → về `null`; gọi trùng bị chặn; lỗi ⇒ `error` + `httpStatus` và **không** tải file.
- [x] `components/__tests__/ReportSummaryPanel.test.tsx`: render số liệu từ fixture; `Empty` khi `total = 0`; cảnh báo khi `rowCapReached`; hiển thị `'—'` khi `avgCompletionHours = null`.
- [x] `components/__tests__/ReportExportDrawer.test.tsx`: 2 lựa chọn PDF/Excel; nút tương ứng `loading`/`disabled` khi đang xuất; `Alert` khi lỗi.
- [x] `components/__tests__/ReportFilters.test.tsx`: preset 7/30/90 gọi `onChange` đúng khoảng; chọn board ⇒ param `boardId`.
- [x] `pages/__tests__/ReportsPage.test.tsx`: Manager ⇒ render panel + nút xuất; Member ⇒ `403` và **không** render số liệu.
- [x] Cập nhật `features/board/components/__tests__/BoardView.test.tsx`: nút "Báo cáo" **hiện** với Manager/Admin, **ẩn** với Member, `onClick` điều hướng đúng URL có `?boardId=`.
- [x] DoD: `npm run lint` (oxlint 0 warn/0 err) + `npx tsc -b` + `npm run build` + `npm test` **sạch** (30 test files / 155 tests PASS).

### 7.3 Điều kiện hoàn thành Giai đoạn 6 (map `03-roadmap.md` dòng 67–70)

- [x] Tổng hợp dữ liệu **tiến độ/hiệu suất** board — verify §7.1 nhóm B (B1–B30) + D (summary 200, số liệu khớp fixture)
- [x] Xuất **PDF** (QuestPDF) đúng định dạng, **đọc được** — verify §7.1 nhóm C (`%PDF-` + `%%EOF` + text extract tiếng Việt có dấu)
- [x] Xuất **Excel** (ClosedXML) đúng định dạng, **mở được** — verify §7.1 nhóm C (5 sheet, mở lại bằng ClosedXML, số liệu khớp)
- [x] File **generate on-demand, không lưu trữ vĩnh viễn trên server** — verify §7.1 nhóm E (5 bảng không đổi + không file mới + `git status` sạch + không redirect)
- [x] UI gọi API thật & bộ kiểm thử frontend hoàn tất (§6 + §7.2)

---

## 8. Edge cases, failure modes & bàn giao Giai đoạn 8

**Edge cases / failure modes:**
- **Workspace 0 board / 0 task** ⇒ report "rỗng" hợp lệ (không 500); PDF/Excel vẫn sinh được (bảng rỗng + dòng "Không có dữ liệu").
- **Board soft-delete giữa chừng** ⇒ global query filter loại khỏi projection; `?boardId=` của board đã xoá ⇒ **404**.
- **Task soft-deleted** ⇒ không xuất hiện; `activity_logs` của task đó **vẫn** đếm trong phần hoạt động (event store append-only) — ghi rõ trong `metricDefinitions`.
- **Task done nhưng `completed_at` null** (dữ liệu cũ) ⇒ vào `progress.done`, bị loại khỏi mọi metric thời lượng ⇒ `completedAtMissing` cho UI biết.
- **Task done ở cột `is_done = false`** ⇒ vẫn tính `done` (quy tắc `CompletedAt != null OR IsDoneColumn`) — ghi rõ trong `metricDefinitions`.
- **`completed_at` ngoài cửa sổ** ⇒ vẫn vào `progress` (hiện tại), **không** vào `completedInRange`/`avgCompletionHours` (theo cửa sổ).
- **`?from > ?to`** ⇒ **400**; **khoảng quá `MaxRangeDays`** ⇒ cắt + `period.clamped = true` + ghi chú trên PDF/Excel.
- **Múi giờ** ⇒ nhận ISO-8601 có/không `Z`, chuẩn hoá UTC khi so sánh; báo cáo hiển thị nhãn **UTC** (tránh hiểu sai giờ).
- **Xuất khối lượng lớn** ⇒ cap `MaxExportRows`/`MaxActionsPerReport` + header `X-Report-Row-Cap-Reached` + ghi chú trong file (không render vô hạn).
- **QuestPDF chưa set license** ⇒ ném exception ngay khi tạo document; đã fix bằng D14 + verify nhóm F (API boot được).
- **Font tiếng Việt** ⇒ dùng font bundled của QuestPDF, **không** phụ thuộc font hệ thống; verify text-extract nhóm C.
- **`Content-Disposition` với tên tiếng Việt** ⇒ filename luôn **ASCII** (`ReportFileName.Slug`) ⇒ không lỗi header.
- **`Reports:Enabled = false`** ⇒ **503** `"Reporting is disabled."` (không phải 404) để UI phân biệt với "không có quyền".
- **Client gọi export bằng GET trực tiếp (không qua axios)** ⇒ không có auto-refresh 401: UI **bắt buộc** dùng `reportingApi.downloadReport` (D12).
- **Xuất báo cáo không sinh dữ liệu mới** ⇒ không `activity_logs`, không `notifications`, không `ai_action_logs` (D16 + verify E).
- **`ai_observer_runs.summary` hỏng/rỗng** ⇒ bỏ qua run đó, phần `health` vẫn trả 200 (không 500).

**Bàn giao Giai đoạn 8 (Test & Deploy):**
- Harness §7.1 nhóm B là **điểm tựa** chuyển thành test xUnit: `ReportAggregator.Build`, `ReportFileName.Build`, `ReportThresholds`/`EffectiveRange` đều là hàm `public static`/thuần.
- Renderer có thể test bằng xUnit chỉ với **byte signature + `ClosedXML` đọc lại** (không cần DB).
- `Reports:Enabled=false` là công tắc an toàn cho CI; PDF/Excel render là CPU-bound ⇒ cân nhắc `MaxExportRows` thấp hơn trên free-tier khi deploy.
- CI (GitHub Actions) nên cache NuGet; QuestPDF/ClosedXML là 2 dependency **duy nhất** mới của solution ⇒ cập nhật `Dependabot`/`dotnet list package` trong pipeline.

---

## 9. Tài liệu

- [x] Tạo `Project-Documents/tasks/phase-6-reporting-export.md` (file này — checklist như trên).
- [x] Cập nhật `Project-Documents/04-database-design.md`:
  - §3.7 (Module Reporting): **khẳng định không bảng**, ghi rõ nguồn projection (`tasks` / `board_columns` / `boards` / `activity_logs` / `ai_observer_runs`), quyền Manager/Admin, cửa sổ mặc định 30 ngày + clamp 365, cap `MaxExportRows`, "byte trong RAM — không lưu file" ⇒ *đã làm ở bước lập kế hoạch*;
  - §4/§5: ghi chú **không** phát sinh enum/index mới ở Giai đoạn 6;
  - §7: thêm gạch đầu dòng "Báo cáo là projection read-only: không soft-delete, không retention riêng, không ghi dữ liệu";
  - §8 "Giả định chính": thêm dòng "Giai đoạn 6 đã được chốt ở bước lập kế hoạch theo `tasks/phase-6-reporting-export.md` §0" (tiền lệ Phase 4/5).
- [x] Tạo `src/Modules/Reporting/TeamNexus.Modules.Reporting/README.md`: 3 endpoint (bảng đầy đủ), options `Reports`, **bảng công thức metric** (copy §1.3), luồng load→aggregate→render, quyền Manager/Admin, hành vi cap/empty/503, license QuestPDF Community, runbook harness (nhắc "toàn GET nên không cần CSRF").
- [x] Cập nhật `README.md` root: thêm dòng `Modules/Reporting/` vào cây `src/` + mục **"Trạng thái (Giai đoạn 6 – Báo cáo & Xuất dữ liệu)"**.
- [x] Cập nhật `Project-Documents/03-roadmap.md`: tick 4 ô Giai đoạn 6 + ghi chú trạng thái (giống khối "Trạng thái" của Giai đoạn 5).
- [x] Tạo `Project-Documents/report/phase-6-reporting-test-report.md` (format Phase 4/5: môi trường, phương pháp, 6 nhóm check có mã + **số liệu thật**, bug thật đã sửa, known gaps, ảnh chụp PDF/Excel + màn hình Báo cáo).

---

## Checklist Hoàn thiện Giai đoạn 6

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 8 (Hoàn thiện, Test & Deploy).

- [x] **Module & DI**: project `TeamNexus.Modules.Reporting` trong `.sln`, `AddReportingModule`/`MapReportingModuleEndpoints` gọi trong `Program.cs` (sau Ai), `Program.cs` chỉ +2 dòng — verify nhóm A + F
- [x] **Không schema**: 0 migration mới, snapshot **zero-diff**, module Board/Ai/Persistence **không đổi** — verify nhóm A
- [x] **Aggregation thuần**: `ReportAggregator.Build` (progress/performance/byBoard/byAssignee/activity/health/metricDefinitions) tất định, không chia 0, sort xác định, có cap — verify B1–B30
- [x] **Options & config**: section `Reports` trong `appsettings.json`, clamp range, `Enabled=false` ⇒ 503, log khởi động 1 dòng — verify D + F
- [x] **3 endpoint** (`GET …/reports/summary`, `…/reports/export`, `…/reports/boards`) đúng quyền Manager/Admin, mã lỗi `{ error }`, không CSRF (GET) — verify D1–D25
- [x] **Xuất PDF (QuestPDF)** đúng định dạng, đọc được, tiếng Việt có dấu, có cảnh báo cắt dòng — verify C
- [x] **Xuất Excel (ClosedXML)** đúng định dạng, mở được, 5 sheet, số liệu khớp aggregator — verify C
- [x] **On-demand, không lưu file**: bytes trong RAM, `Results.File`, không TEMP/artifact, 5 bảng không đổi — verify E1–E7
- [x] **Frontend**: `src/features/reporting/` (types/api/utils/hooks/components/page) + route `/workspaces/:id/reports` + nút "Báo cáo" ở `BoardListPage`/`BoardView` (ẩn với Member) — verify §7.2
- [x] **Test FE**: API/utils/hooks/components/page + cập nhật `BoardView.test.tsx`; `oxlint` 0/0 + `tsc -b` + `vite build` + `vitest run` sạch
- [x] **Tài liệu**: `04-database-design.md` (§3.7/§7/§8), README module Reporting, `README.md` root, `03-roadmap.md` (4 ô), báo cáo test — **đã làm hết**

---

## 10. Thứ tự thực hiện (đề xuất)

1. **Tài liệu trước** (§9): file này + cập nhật `04-database-design.md` §3.7/§7/§8 (commit tài liệu, theo tiền lệ Phase 4 §1.4 / Phase 5 §1.5).
2. **§2**: tạo project + `.sln` + `Program.cs` + `appsettings.json` + options ⇒ `dotnet build` xanh (chưa có endpoint).
3. **§1**: `ReportSnapshots` + `ReportAggregator` + `ReportThresholds` ⇒ harness nhóm **B** (thuần, nhanh nhất để bắt bug công thức).
4. **§3**: `ReportService` (loader) + exceptions ⇒ harness dựng snapshot thật từ DB dev.
5. **§4**: `ReportFileName` → `ReportPdfRenderer` → `ReportExcelRenderer` → `ReportExportService` ⇒ harness nhóm **C**.
6. **§5**: DTO + endpoints ⇒ harness nhóm **D/E/F** (API thật + PostgreSQL thật + JWT tự ký).
7. **§6 + §7.2**: frontend + test ⇒ DoD FE.
8. **§9 còn lại**: README module/root, tick roadmap, viết `report/phase-6-reporting-test-report.md` + ảnh chụp.
