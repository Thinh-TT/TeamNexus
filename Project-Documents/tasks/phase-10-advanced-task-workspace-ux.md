# Giai đoạn 10 — Nâng cao Task & Workspace UX (Kế hoạch chia task)

> **Trạng thái thi hành:** ✅ **§1 (Backend Task UX) XONG** — 2 bug thật đã bắt & sửa (§1.3) · ✅ **§2 (Backend Workspace & Activity) XONG** · ⬜ §3 · ⬜ §4 · ⬜ §5.
>
> 📤 **§3 + §4 + §5 đã được bàn giao:** `tasks/phase-10-remaining-frontend-handover.md`
> (note dành cho Antigravity: baseline, hợp đồng API thật, danh sách test, DoD, bằng chứng, thứ tự thi hành, danh sách "không được làm").

> **Nguồn:** `Project-Documents/03-roadmap.md` → *Giai đoạn 10: Nâng cao Task & Workspace UX*.
> **Tiền đề:** Giai đoạn 7 (AI Agent Executor) · Giai đoạn 8 (Test/CI/Deploy) · Giai đoạn 9 (Đơn giản hoá Role) — **đã merge**.
> **Nhánh:** `feat/phase10-advanced-task-workspace-ux`
> **Schema:** ⛔ **KHÔNG migration.** `tasks.due_date` / `tasks.priority` / `tasks.description` đã có từ Phase 2
> (`04-database-design.md` §3.2 dòng 295–296; `BoardTaskConfiguration` `ck_tasks_priority`), và `activity_logs` đã có từ Phase 5.
> Nếu bất kỳ bước nào dưới đây đòi hỏi đổi schema ⇒ **dừng lại và báo**, đừng tự sinh migration.

---

## 0. Mục tiêu & 5 ô hoàn thiện

Hoàn thiện UI cho các field task **đã có schema nhưng chưa có giao diện**, thêm quản lý workspace cơ bản, và hiển thị lịch sử hoạt động.

| # | Yêu cầu roadmap | Trạng thái đầu kỳ | Việc phải làm |
|---|---|---|---|
| **A** | Task set/hiển thị **Due Date**, card hiển thị **badge đỏ khi quá hạn** — nối với tín hiệu `OverdueTask` của AI Observer | Set được (modal) · badge hạn chót **đã có** nhưng "quá hạn" chỉ là **đổi màu chữ**, chưa phải badge; **0 test** | §3.1, §3.3: tách hàm thuần + **badge đỏ + viền trái đỏ** + test |
| **B** | Task set/hiển thị **Priority** (Low/Medium/High/Urgent), **badge màu** trên card | ✅ UI đã xong; backend có field nhưng **validate sai** (2 bug, §1.3) và **0 test** | ✅ **§1 XONG** — 18 test case + sửa BUG-1/BUG-2 |
| **C** | Task có **Description markdown cơ bản** (đậm, code inline, link) trong modal chi tiết | Chỉ có `Input.TextArea` thuần; repo **không có** thư viện markdown | §3.2, §3.4: renderer thuần **tự viết** + toggle Soạn/Xem trước |
| **D** | **Workspace Settings** (Admin): sửa tên/mô tả, chuyển ownership, xoá workspace (có xác nhận) | **Chưa có gì**: không service, không endpoint, không trang | §2.1–§2.3, §4.3 |
| **E** | **Activity Log UI** (Manager/Admin): trang lịch sử hoạt động workspace — tận dụng `activity_logs` từ Phase 5 | Bảng có **6 action** đang ghi, nhưng **không** có API đọc và **không** có UI | §2.4, §4.4 |

**Ngoài 5 ô trên, giai đoạn này còn 3 hạng mục kỹ thuật bắt buộc phát sinh** (phát hiện khi khảo sát code — xem §1.1):

- **F** — Nút vào Settings/Activity **chưa tồn tại** ở `BoardListPage`/`BoardView` (§4.4).
- **G** — Logic phân quyền bị **copy 2 lần** ở `BoardView.tsx` (209–227) và `ReportsPage.tsx` (46–74) ⇒ tách `useWorkspaceRole` dùng chung (§4.1, §4.2).
- **H** — Nghi vấn bug: `onChange={handleSaveMetadata}` của Select Priority / DatePicker / Select cột trong `TaskDetailModal` có thể gửi **giá trị CŨ** (§3.4b — phải chứng minh bằng test trước khi sửa).

---

## 1. Baseline & bối cảnh đã khảo sát

### 1.1 Đã có sẵn — **KHÔNG làm lại** (bằng chứng trong repo)

| Hạng mục | Bằng chứng (file · dòng) |
|---|---|
| `tasks.due_date` (`timestamptz`), `tasks.priority` (`text` + CHECK `Low/Medium/High/Urgent`), `tasks.description` (`text`) | `04-database-design.md` 295–296 · `Persistence/Data/Entities/BoardTask.cs` 32, 39, 41 · `Configurations/BoardTaskConfiguration.cs` 13 |
| Backend **đã nhận & trả** cả 3 field | `DTOs/TaskDtos.cs` (`CreateTaskRequest`, `UpdateTaskRequest`, `TaskResponse`) |
| Validate priority ⇒ **400** `{ error }` khi sai | `Services/TaskService.cs` `ParsePriority` |
| Map ra response ở **cả 3** đường trả task | `Services/DtoMapping.cs` `MapTask` (20–43) · gọi từ `TaskService.GetTasksAsync`, `GetTaskByIdAsync`, `BoardService.GetBoardByIdAsync` |
| Ghi `activity_logs` cho 6 sự kiện task/comment | `Services/TaskService.cs` · `Services/CommentService.cs` · hằng số tại `Services/IActivityLogWriter.cs` 8–30 |
| Modal đã có Priority Select + DatePicker + Description | `frontend/src/features/board/components/TaskDetailModal.tsx` 437–445, 673–707 |
| Card đã có badge priority + hạn chót + badge AI Agent | `frontend/src/features/board/components/TaskCard.tsx` 24–37, 79–80, 117–155, 211–229 |
| Phân quyền workspace: `RequireMemberAsync` (404), `RequireManagerAsync` (403) | `Services/WorkspaceAccess.cs` |
| Role workspace `Admin/Manager/Member` (+ CHECK), `member_type` `human/ai_agent` | `Persistence/Data/Entities/WorkspaceMember.cs` · `WorkspaceMemberConfiguration` 13–21 |
| `GET /api/workspaces` (list + role + **tự tạo workspace mặc định**) | `src/TeamNexus.Api/Program.cs` **94–141** ⚠️ code inline trong entry point |
| `activity_logs` append-only, `board_id` NULL cho sự kiện cấp workspace, FK không navigation | `Persistence/Data/Entities/ActivityLog.cs` 21, 27 · `ActivityLogConfiguration` 12–14 |

### 1.2 Baseline đo tại máy dev

```
npm run lint                        → 0 warning / 0 error (107 file, 116 rule)
npx tsc -b                          → exit 0
npm test                            → Test Files 37 passed (37) · Tests 206 passed (206)
npm run build                       → OK (14.32s)
dotnet build TeamNexus.sln -m:1 -nr:false → 0 Warning(s) / 0 Error(s)
dotnet test (PostgreSQL 18 thật)    → Passed: 171 · Failed: 0 · Skipped: 0
dotnet ef migrations list           → 8 migration (mới nhất 20260913180740_Phase9ModelSync)
dotnet ef migrations has-pending-model-changes → "No changes have been made to the model since the last migration."
```

> ⚠️ **Khi máy không có PostgreSQL** (`TEAMNEXUS_TEST_DB` chưa đặt hoặc sai mật khẩu), `dotnet test` ra
> `Passed: 98 / Skipped: 73 / Total: 171` và **vẫn exit 0**. Đó **không** phải bằng chứng đạt DoD.
> Đặt DB thật trước khi đo:
> `$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=<pw>"`
> (fixture tự `CREATE DATABASE` + tự chạy migration — xem `tests/TeamNexus.Api.Tests/Infrastructure/DatabaseFixture.cs`).
> **Số 171 phải được cập nhật khi thêm test** — cả trong tài liệu này **và** trong
> `.github/workflows/ci-backend.yml` (dòng `if ($total -ne 171) { throw … }`).
> **Số hiện tại sau §1 là 189.**

---

## 2. Bảng quyết định kiến trúc (D1–D12) — chốt sẵn, không chọn lại

| # | Quyết định | Lý do / ràng buộc |
|---|---|---|
| **D1** | **Rút handler inline `GET /api/workspaces` khỏi `Program.cs`** thành `WorkspaceService` + `WorkspacesEndpoints` **trong module Board** (không tạo module mới) | `Program.cs` phải là chỗ lắp ráp, không phải chỗ chứa nghiệp vụ; module Board đã sở hữu `workspace_members`, `IWorkspaceAccess`, `DomainExceptionFilter`. **Giữ nguyên** side effect tự tạo workspace mặc định (`DashboardPage` phụ thuộc nó) |
| **D2** | Quyền: **đọc** = Member+ · **sửa tên/mô tả** = Manager+ · **chuyển owner & xoá** = Admin workspace **hoặc** `workspaces.owner_id` | Khớp `RequireManagerAsync` sẵn có; owner luôn là Admin nên cổng "owner hoặc Admin" chỉ rộng hơn ở ca dữ liệu cũ (chưa kịp set role) |
| **D3** | Chuyển owner **không** hạ cấp ai: **giữ** role của owner cũ, **nâng** owner mới lên `Admin` nếu chưa; **từ chối `member_type = 'ai_agent'`** | Không có "hạ cấp ngầm" ngoài dự kiến; agent là pseudo-member, không sở hữu workspace |
| **D4** | Xoá workspace = **soft delete** (`DeletedAt`), trả **204 No Content** | DB design §7 ("no physical cascade"); `WorkspaceConfiguration` 28 đã có query filter `DeletedAt == null` ⇒ boards/tasks/members tự vô hình. Roadmap chỉ yêu cầu "với xác nhận" |
| **D5** | Ghi `activity_logs` cho **3 thao tác workspace** (`WorkspaceUpdated` / `WorkspaceOwnerTransferred` / `WorkspaceDeleted`) với `board_id = NULL` | `ActivityLog.cs` 21 + 27 ghi rõ `board_id` NULL = "workspace-level events" ⇒ **không** migration. Dùng `IActivityLogWriter` sẵn có (best-effort, **không bao giờ ném**) |
| **D6** | API activity: `GET /api/workspaces/{id}/activity` — **Manager+**, phân trang **keyset** (`take` mặc định 50 / trần 200, cursor `(createdAt, id)`), filter `boardId` / `entityType` / `action` | Bảng append-only **không có** index cho `OFFSET`; keyset + index `(workspace_id, created_at)` (đã có, `ActivityLogConfiguration` 55) là đường rẻ nhất |
| **D7** | **Không thêm thư viện npm.** Markdown "cơ bản" = renderer **tự viết**, trả **React element** (không `dangerouslySetInnerHTML`) | Roadmap giới hạn đúng "in đậm, code inline, link"; thêm dependency cho 3 cú pháp là cái giá không đáng, và `dangerouslySetInnerHTML` mở đường XSS |
| **D8** | Tạo `useWorkspaceRole` **dùng chung**; refactor `BoardView` / `ReportsPage` / `DashboardPage` sang dùng nó | Xoá hạng mục **G**; một nguồn sự thật cho `isManagerOrAdmin` / `isAdmin` / `isOwner` |
| **D9** | **KHÔNG** đổi: shape `HubConnectionStatus`, tên event SignalR, shape `TaskResponse`, 3 đường trả task, `httpClient.ts`, `reportDownload.ts`, kiến trúc 1-hub-1-group | Bảo toàn hợp đồng đã verify ở Giai đoạn 2–9 (bài học "đừng phá thứ đang xanh") |
| **D10** | Nút **"Cài đặt"** / **"Hoạt động"** đặt cạnh nút **"Báo cáo"** ở `BoardListPage` + `BoardView`, **ẩn** với role thấp | Cùng pattern đã dùng cho "Báo cáo" (`BoardListPage.tsx` 176–182) |
| **D11** | Trang Activity dùng nút **"Tải thêm"** (không infinite scroll / không IntersectionObserver) | Test được bằng Vitest + `user-event`, không cần mock API trình duyệt |
| **D12** | Tên action mới thêm vào `ObserverActivityActions` (**hằng `const string`**) — **không** thêm cột/enum/CHECK | `action` là free text theo DB design §4 (xem chú thích `IActivityLogWriter.cs` 5–7) |

---

## 3. §1 — Backend: Task UX (đóng ô **B**, bịt test cho **A/C**) — ✅ **XONG**

> **Trạng thái: ✅ ĐÃ THI HÀNH & VERIFY.** `TaskFieldsApiTests` = **13 test method / 18 test case PASS**
> (1 `[Theory]` × 6 `InlineData`), `Skipped: 0`, trên PostgreSQL 18 thật.
> **2 bug thật đã bắt & sửa** (chi tiết ở §1.3) — cả hai đều là lỗi *im lặng* mà không test nào phủ trước đây.

> **Kết luận khảo sát: backend đã đủ field, KHÔNG cần thêm tính năng nào.**
> Việc của §1 là **bịt lỗ hổng test** — trước đây không có test nào khẳng định `description` / `dueDate` / `priority`
> đi trọn vòng HTTP, nên bất kỳ ai sửa `DtoMapping` hay `TaskService` đều có thể phá ô A/B/C mà CI vẫn xanh.
> Chính lỗ hổng đó đã giấu **2 bug thật** — xem §1.3.

### 1.1 File sửa
**1 file:** `Services/TaskService.cs` — **chỉ** để sửa 2 bug ở §1.3 (`ParsePriority` + `ToUtc`).
Không thêm field, không đổi DTO, không đổi endpoint, không migration.

### 1.2 Test mới — `tests/TeamNexus.Api.Tests/Integration/TaskFieldsApiTests.cs` (mới) — ✅ đã viết

Dùng đúng hạ tầng đang có: `DatabaseFixture.CreateScenarioAsync` + `TestHttpClient` + `TestJwt`
(theo mẫu `Integration/KanbanApiTests.cs`), PostgreSQL thật, **không** EF InMemory.

| # | Test | Kỳ vọng | Kết quả |
|---|---|---|---|
| F-1 | `CreateTask_PersistsDescriptionDueDateAndPriority` | 201; response echo đúng cả 3 | ✅ |
| F-2 | `TaskFields_AreResolvedOnTheTaskListPath` | `/tasks` vẫn đủ 3 field (đường trả task #1) | ✅ |
| F-3 | `TaskFields_AreResolvedOnTheSingleTaskPath` | `/tasks/{id}` vẫn đủ 3 field (đường #2) | ✅ |
| F-4 | `TaskFields_AreResolvedOnTheNestedBoardPath` | `/workspaces/{w}/boards/{b}` — task lồng trong column vẫn đủ 3 field (**đường #3**, hồi quy đúng kiểu Phase 7 §3.3) | ✅ |
| F-5 | `UpdateTask_ChangesPriority` | 200; `priority = "Urgent"` | ✅ |
| F-6 | `UpdateTask_AcceptsPriorityInAnyCaseAndStoresTheCanonicalName` | `"urgent"` ⇒ 200, lưu canonical `Urgent` (enum **và** DB) | ✅ |
| F-7 | `UpdateTask_WithAnUnknownPriority_Returns400` (**Theory ×6**: `"Critical"`, `"1"`, `"+1"`, `"99"`, `"HIGHEST"`, `""`) | **400** `{ error }` nêu `Low…Urgent`; **không** ghi gì vào DB | ✅ (sau khi sửa **BUG-1**) |
| F-8 | `UpdateTask_WithANullDueDate_ClearsTheDeadline` | 200; `dueDate = null` (cả response **và** DB) | ✅ |
| F-9 | `UpdateTask_WithAnOffsetDueDate_RoundTripsTheSameInstant` | `+07:00` ⇒ 200, **cùng instant**; DB lưu UTC | ✅ (sau khi sửa **BUG-2**) |
| F-10 | `UpdateTask_WithANullPriority_ClearsThePriority` | 200; `priority = null` | ✅ |
| F-11 | `UpdateTask_WithABlankDescription_StoresNull` | `"   "` ⇒ 200; `description = null` | ✅ |
| F-12 | `UpdateTask_StoresMarkdownDescriptionVerbatim` | markdown lưu **nguyên văn** (không escape/biến đổi; §3 xử lý phần render) | ✅ |
| F-13 | `UpdateTask_LogsPriorityAndDueDateButNeverTheDescriptionText` | 1 row `TaskUpdated`; payload có `priority`/`dueDate`/`descriptionChanged` và **không** chứa nội dung mô tả | ✅ |

**Ghi chú kỹ thuật cho người đọc test:**
- Mọi DTO trong file test đều là `record` với **property khởi tạo mặc định** (`Id`, `Title`, `DueDate?`…),
  không dùng positional record. Lý do: `TaskResponse` có constructor không tham số nên **field mà API ngừng gửi sẽ
  deserialize thành `null`** thay vì ném — đó chính là thứ khiến các test này *có thể đỏ* khi có hồi quy.
- F-13 parse `activity_logs.payload` bằng `JsonDocument` và assert **cả hai nửa** của quy tắc Phase 5 §2.3:
  ghi **có** `priority`/`dueDate`, và **tuyệt đối không** chứa phần text của mô tả.

### 1.3 🐞 **2 bug thật đã bắt được (đây là giá trị chính của §1)**

#### BUG-1 — `priority: "1"` bị **âm thầm** lưu thành `Medium`

`TaskService.ParsePriority` cũ chỉ dùng `Enum.TryParse<TaskPriority>(value, ignoreCase: true, out var priority)`.
Nhưng `Enum.TryParse` **không phải validator**: nó parse cả **chuỗi số** thành member theo **chỉ số**
(`"0"`→`Low`, `"1"`→`Medium`, `"3"`→`Urgent`) — đúng như tài liệu .NET. Hệ quả:

| Client gửi | Trước khi sửa | Hậu quả |
|---|---|---|
| `"1"` | **200 OK**, lưu `Medium` | Client xin ưu tiên kiểu số, nhận một ưu tiên **khác hẳn** mà không có cảnh báo |
| `"99"` | `(TaskPriority)99` lọt qua ⇒ `SaveChanges` vi phạm `ck_tasks_priority` ⇒ **500** (không phải 400) | Lỗi hạ tầng lộ ra ngoài thay vì lỗi validate |

**Sửa:** thêm `Enum.IsDefined(priority)` **và** `IsNumericString(value)` (helper mới, chỉ nhận dấu + chữ số,
cố ý **không** phân loại `"Urgent"` là số). Từ nay `"1"`, `"+1"`, `"99"`, `"Critical"`, `"HIGHEST"`, `""` đều ⇒ **400**.
`"urgent"` / `"URGENT"` vẫn ⇒ `Urgent` (hợp đồng cũ giữ nguyên).

#### BUG-2 — `dueDate` có offset (ví dụ `+07:00`) làm **mọi** request trả **500**

Npgsql từ chối `DateTimeOffset` có offset ≠ 0 khi ghi vào `timestamptz`:
`System.ArgumentException: Cannot write DateTimeOffset with Offset=07:00:00 … only offset 0 (UTC) is supported.`
Ngoại lệ ném ra từ `SaveChangesAsync` — tức **sau** khi request đã qua validate — nên client thấy **500**,
dù đây hoàn toàn là input hợp lệ theo ISO-8601.

- **Vì sao chưa ai thấy:** `TaskDetailModal` hiện gửi `.toISOString()` ⇒ luôn là `Z`, nên đường UI không chạm bug.
  Nhưng **bất kỳ client nào gửi offset theo múi giờ** (Flutter ở Giai đoạn 13, Postman, mobile, một UI mới
  gửi `+07:00`) đều nhận 500. Đây là bug **âm thầm nằm sẵn trong hợp đồng REST**.
- **Sửa:** helper `ToUtc(DateTimeOffset?)` ⇒ `.ToUniversalTime()`, áp ở **cả** `CreateTaskAsync` và
  `UpdateTaskAsync`; payload activity ghi `task.DueDate` (giá trị **đã chuẩn hoá**) chứ không ghi giá trị thô
  của request. Giá trị là một **instant** nên chuẩn hoá UTC bảo toàn ý nghĩa và giữ cột DB ở dạng canonical.
- **Phạm vi:** `DueDate` là **đường ghi `DateTimeOffset` duy nhất lấy từ client** trong toàn bộ API
  (đã soát hết: các `DateTimeOffset` khác đều là tham số nội bộ hoặc do server sinh, ví dụ `from`/`to` của
  Reporting đã được `ReportThresholds.BuildRange` chuẩn hoá).

### 1.4 Con số sau §1
`dotnet test` — **189 test case** (171 baseline + 18 mới), **0 failed / 0 skipped** trên PostgreSQL 18 thật.
`dotnet build TeamNexus.sln -m:1 -nr:false` = **0 warning / 0 error**.
`dotnet ef migrations list` = **8** (không đổi), `has-pending-model-changes` = không có.
> ⚠️ Cổng CI `.github/workflows/ci-backend.yml` (đang chốt `if ($total -ne 171)`) **phải nâng lên 189** ở §5.
> Nếu chưa nâng, CI sẽ **fail** ngay khi §1 được đẩy lên.

---

## 4. §2 — Backend: Workspace & Activity (đóng ô **D** phần server + ô **E** phần server) — ✅ **XONG**

> **Trạng thái: ✅ ĐÃ THI HÀNH & VERIFY.** `WorkspaceApiTests` = **30 test method / 37 test case PASS**
> (2 `[Theory]`: ×5 `InlineData` cho cursor hỏng, ×2 cho tên rỗng), `Skipped: 0`, trên PostgreSQL 18 thật.
> **Không có bug mới** — nhưng 4 test đỏ đầu tiên đều là **lỗi ở chính cách viết test**, đã sửa (xem §2.7);
> tất cả đều đáng ghi lại vì chúng là những cái bẫy mà người viết test tiếp theo sẽ gặp lại.

### 2.0 File đã tạo/sửa (thực tế)

| Loại | File |
|---|---|
| **Mới** | `DTOs/WorkspaceDtos.cs` · `Services/WorkspaceService.cs` · `Services/WorkspaceActivityService.cs` · `Endpoints/WorkspacesEndpoints.cs` |
| **Mới** | `tests/TeamNexus.Api.Tests/Integration/WorkspaceApiTests.cs` |
| Sửa | `Services/IActivityLogWriter.cs` (+3 action, +`ObserverEntityTypes.Workspace`) |
| Sửa | `BoardModule.cs` (DI) · `Endpoints/BoardEndpoints.cs` (`MapWorkspacesEndpoints`) |
| Sửa | `src/TeamNexus.Api/Program.cs` — **xoá** handler inline `GET /workspaces` (94–141) |
| Sửa | `.github/workflows/ci-backend.yml` — cổng test `171` → **`226`** |

### 2.1 File **mới** — `src/Modules/Board/TeamNexus.Modules.Board/DTOs/WorkspaceDtos.cs`

```csharp
/// <summary>Một workspace người gọi đang tham gia. 4 field đầu KHÔNG được đổi thứ tự/tên:
/// frontend Giai đoạn 1–9 đang parse { id, name, description, role }.</summary>
public sealed record WorkspaceSummaryResponse(
    Guid Id, string Name, string? Description, string Role,
    Guid OwnerId, bool IsOwner);          // 2 field Phase 10 — LUÔN append ở cuối

public sealed record WorkspaceDetailResponse(
    Guid Id, string Name, string? Description,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    Guid OwnerId, string OwnerDisplayName,
    int MemberCount, int BoardCount, string CurrentUserRole);

public sealed record UpdateWorkspaceRequest(string Name, string? Description);

public sealed record TransferOwnershipRequest(Guid NewOwnerId);

public sealed record WorkspaceActivityItemResponse(
    Guid Id, Guid? BoardId, Guid? UserId, string? UserDisplayName,
    string EntityType, Guid? EntityId, string Action,
    JsonElement? Payload, DateTimeOffset CreatedAt);

public sealed record WorkspaceActivityPageResponse(
    IReadOnlyList<WorkspaceActivityItemResponse> Items,
    string? NextCursor, bool HasMore);
```

### 2.2 File **mới** — `Services/WorkspaceService.cs`

```csharp
public interface IWorkspaceService
{
    Task<IReadOnlyList<WorkspaceSummaryResponse>> ListForUserAsync(Guid userId, CancellationToken ct = default);
    Task<WorkspaceDetailResponse> GetAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
    Task UpdateAsync(Guid workspaceId, UpdateWorkspaceRequest request, Guid userId, CancellationToken ct = default);
    Task TransferOwnershipAsync(Guid workspaceId, TransferOwnershipRequest request, Guid userId, CancellationToken ct = default);
    Task DeleteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}
```

**`ListForUserAsync`** — chuyển **nguyên xi** logic `Program.cs` 94–141:
1. `WorkspaceMembers.Include(Workspace).Where(UserId == userId && Workspace != null)` (query filter đã ẩn workspace soft-deleted).
2. **Nếu rỗng** ⇒ tạo workspace mặc định `Name = "Không Gian Làm Việc Chính"`, `Description = "Workspace mặc định để quản lý bảng Kanban"`, `OwnerId = userId`, + membership `Role = Admin`; `SaveChanges`; thêm vào list đang trả.
3. `Select` ra `WorkspaceSummaryResponse`, **append** `OwnerId = wm.Workspace.OwnerId`, `IsOwner = wm.Workspace.OwnerId == userId` **sau** 4 field cũ.

> Giữ nguyên đúng chuỗi tiếng Việt và thứ tự như code cũ — `DashboardPage` và `BoardView` đang đọc `{ id, name, role }`.

**`GetAsync`** — `RequireMemberAsync` (404 khi không phải thành viên) rồi **3 query**, không N+1:
`MemberCount` = `WorkspaceMembers.CountAsync(w => w.WorkspaceId == id)`,
`BoardCount` = `Boards.CountAsync(b => b.WorkspaceId == id)`,
`OwnerDisplayName` = 1 query `Users` theo `OwnerId` (fallback `"Không xác định"` nếu user đã bị xoá cứng).

**`UpdateAsync`**
- `RequireManagerAsync` ⇒ **403** với Member.
- Validate tên: `string.IsNullOrWhiteSpace` hoặc `Trim().Length > 120` ⇒ `BadRequestException` (**400**) — cùng ngưỡng `BoardService.ValidateBoardName`.
- `Description` = `TrimToNull` (rỗng ⇒ `null`); lưu, `UpdatedAt` tự cập nhật qua `IAuditableEntity`.
- Ghi activity `WorkspaceUpdated`, payload `{ nameChanged, ownerId }` (**không** chứa nội dung mô tả — cùng nguyên tắc chống phình token của Phase 5 §2.3).

**`TransferOwnershipAsync`**
1. `membership = await RequireManagerAsync(...)`.
2. Nếu **không** phải `workspace.OwnerId == userId` **và** `membership.Role != WorkspaceRole.Admin` ⇒ `ForbiddenException` (**403**).
3. `newOwnerId == workspace.OwnerId` ⇒ `BadRequestException` "Owner mới phải khác owner hiện tại." (**400**).
4. Tra `WorkspaceMembers` của `newOwnerId`: không có ⇒ **400** "Người nhận không phải thành viên của workspace này."; `MemberType == AiAgent` ⇒ **400** "Không thể chuyển quyền sở hữu cho AI Agent."
5. Một transaction: `workspace.OwnerId = newOwnerId`, `workspace.UpdatedAt = now`, `newOwnerMembership.Role = WorkspaceRole.Admin` (nâng nếu chưa). **Không** đổi role owner cũ (D3).
6. Commit ⇒ ghi activity `WorkspaceOwnerTransferred`, payload `{ previousOwnerId, newOwnerId }`.

**`DeleteAsync`** — owner/Admin (như bước 2 ở trên) ⇒ `workspace.DeletedAt = now`; `SaveChanges` ⇒ ghi activity `WorkspaceDeleted` (payload `{ name }`) ⇒ **204**.
> `IActivityLogWriter` chạy **sau** khi workspace đã bị ẩn, nhưng nó dùng `_db.Activities.Add` **không** navigation ⇒ vẫn ghi được (đọc lại thì không, vì endpoint đã 404 — đây là **hành vi dự kiến**, ghi vào §8).

### 2.3 File **mới** — `Endpoints/WorkspacesEndpoints.cs`

```csharp
var group = endpoints.MapGroup("/api/workspaces")
    .WithTags("Workspaces")
    .AddEndpointFilter<DomainExceptionFilter>();
```

| Method | Route | Quyền | Endpoint filter | Ghi chú |
|---|---|---|---|---|
| `GET` | `/api/workspaces` | đăng nhập (`.RequireAuthorization()`) | — | thay thế handler trong `Program.cs` |
| `GET` | `/api/workspaces/{workspaceId:guid}` | Member+ | — | |
| `PUT` | `/api/workspaces/{workspaceId:guid}` | Manager+ | `AntiforgeryValidationEndpointFilter` | trả **204** |
| `PUT` | `/api/workspaces/{workspaceId:guid}/owner` | Admin/owner | `AntiforgeryValidationEndpointFilter` | trả **204** |
| `DELETE` | `/api/workspaces/{workspaceId:guid}` | Admin/owner | `AntiforgeryValidationEndpointFilter` | trả **204** |
| `GET` | `/api/workspaces/{workspaceId:guid}/activity` | Manager+ | — (**GET**, không CSRF — đúng tiền lệ Phase 6 D12) | |

Handler chỉ: lấy `http.RequireUserId()` → gọi service → map DTO/`Results.NoContent()`. **Không** chứa nghiệp vụ, **không** tự `try/catch` (đã có `DomainExceptionFilter`).

### 2.4 File **mới** — `Services/WorkspaceActivityService.cs`

```csharp
public interface IWorkspaceActivityService
{
    Task<WorkspaceActivityPageResponse> GetAsync(
        Guid workspaceId, Guid userId,
        Guid? boardId, string? entityType, string? action,
        int? take, string? before, CancellationToken ct = default);
}
```

- `RequireManagerAsync` ⇒ **403** cho Member (đúng yêu cầu roadmap "Manager/Admin").
- **Cursor**: `before` = `"{CreatedAt:O}|{Id}"`. Parse hỏng (thiếu `|`, ngày/GUID sai) ⇒ `BadRequestException` **400** `{ error }` — **không** được 500. Đúng định dạng ⇒
  `WHERE (CreatedAt < cursorTime) OR (CreatedAt == cursorTime AND Id < cursorId)` (keyset, sắp `CreatedAt DESC, Id DESC`).
- `take`: null ⇒ **50**; `<= 0` ⇒ **1**; `> 200` ⇒ **200** (clamp, không lỗi).
- **Actor**: **KHÔNG** thêm navigation property vào `ActivityLog` (config cố ý khai FK không navigation để EF không thừa hưởng query filter — chú thích `ActivityLogConfiguration` 12–14). Dùng
  `.LeftJoin(_db.Users, a => a.UserId, u => (Guid?)u.Id, (a, u) => new { a, u })` ⇒ `UserDisplayName = u.DisplayName` (null ⇒ UI hiển thị "Hệ thống").
- `Payload` là `jsonb` map sang `string` (`ActivityLogConfiguration` 35–36) ⇒ `string.IsNullOrWhiteSpace` ⇒ `null`, ngược lại `JsonSerializer.Deserialize<JsonElement>(...)` (camelCase — đúng quy ước `JsonSerializerDefaults.Web` của Phase 5 A6).
- Lấy **`take + 1`** row ⇒ `HasMore = rows.Count > take`; `NextCursor` = cursor của item **cuối cùng được giữ lại** (null khi `HasMore == false`).

### 2.5 File **sửa**

| File | Thay đổi |
|---|---|
| `Services/IActivityLogWriter.cs` | Thêm `ObserverActivityActions.WorkspaceUpdated` / `.WorkspaceOwnerTransferred` / `.WorkspaceDeleted`; `ObserverEntityTypes.Workspace = "Workspace"` |
| `BoardModule.cs` | `services.AddScoped<IWorkspaceService, WorkspaceService>();` + `services.AddScoped<IWorkspaceActivityService, WorkspaceActivityService>();` |
| `Endpoints/BoardEndpoints.cs` | `endpoints.MapWorkspacesEndpoints();` (cạnh `MapMembersEndpoints`) |
| `src/TeamNexus.Api/Program.cs` | **Xoá** khối `api.MapGet("/workspaces", …)` (dòng **94–141**) và comment "---- Health & Workspace endpoints ----" cho gọn (đổi thành `---- Health ----`). **Giữ** `api.MapGet("/health")`, **giữ nguyên thứ tự** `AddBoardModule` → `AddAiModule` → `AddReportingModule` (bất biến đã chốt ở Phase 5 §2) |

### 2.6 Test mới — `tests/TeamNexus.Api.Tests/Integration/WorkspaceApiTests.cs`

| # | Test | Kỳ vọng |
|---|---|---|
| W-1 | `GET /api/workspaces` khi user **chưa có** membership | 200; **đúng 1** workspace, `name = "Không Gian Làm Việc Chính"`, `role = "Admin"`, `isOwner = true` |
| W-2 | Gọi `GET /api/workspaces` **lần 2** | 200; vẫn **1** workspace/1 membership (không tạo trùng) |
| W-3 | `GET /api/workspaces` khi đã có membership | 200; `id`/`name`/`role`/`description` **giữ nguyên shape cũ** (dùng `JsonDocument` assert có đủ **6** key) |
| W-4 | `GET /api/workspaces/{id}` với Member | 200; `memberCount`/`boardCount` đúng; `ownerDisplayName` = tên chủ sở hữu |
| W-5 | `GET /api/workspaces/{id}` với user **ngoài** workspace | **404** (không rò rỉ sự tồn tại) |
| W-6 | `GET /api/workspaces/{id}` sau khi workspace bị soft-delete | **404** |
| W-7 | `PUT /api/workspaces/{id}` (Manager) tên + mô tả mới | **204**; `GET` lại thấy giá trị mới |
| W-8 | `PUT /api/workspaces/{id}` (Member) | **403** |
| W-9 | `PUT …` tên rỗng / chỉ khoảng trắng / **121 ký tự** | **400**; biên **120 ký tự** ⇒ **204** |
| W-10 | `PUT …` thiếu header CSRF | **403** |
| W-11 | `PUT /api/workspaces/{id}/owner` (owner) chuyển cho **member thường** | 204; `ownerId` đổi; role người nhận = **`Admin`**; role owner cũ **giữ nguyên** |
| W-12 | `PUT …/owner` chuyển cho **chính owner** | **400** |
| W-13 | `PUT …/owner` chuyển cho user **ngoài** workspace | **400** |
| W-14 | `PUT …/owner` chuyển cho **AI Agent** của workspace | **400** |
| W-15 | `PUT …/owner` gọi bởi **Manager không phải owner** | **403** (Manager **không** tự chuyển owner) |
| W-16 | `DELETE /api/workspaces/{id}` (owner) | **204**; `GET /{id}` ⇒ **404**; `GET /api/workspaces` không còn id đó; `workspace_members` vẫn còn row trong DB (soft delete) |
| W-17 | `DELETE /api/workspaces/{id}` gọi bởi **Manager không phải owner** | **403** |
| W-18 | `GET /api/workspaces/{id}/activity` với **Member** | **403** |
| W-19 | `GET …/activity` (Manager) sau khi tạo task + kéo task + comment | 200; thấy `TaskCreated`, `TaskMoved`, `CommentAdded`; item mới nhất **đứng đầu** |
| W-20 | `GET …/activity?boardId=…` | Chỉ item của board đó; item `boardId = null` bị loại |
| W-21 | `GET …/activity?action=TaskMoved` và `?entityType=Comment` | Lọc đúng |
| W-22 | `GET …/activity?take=1000` | Clamp về **200** (assert số item ≤ 200 khi seed 201 row) |
| W-23 | Phân trang: `take=2` rồi `before=<nextCursor>` | Trang 2 **không trùng** item trang 1; đi hết bảng **không mất** row nào |
| W-24 | `GET …/activity?before=rac&khong-hop-le` | **400** `{ error }` (không 500) |
| W-25 | `GET …/activity` của workspace A | **Không** lộ activity của workspace B |
| W-26 | `PUT …`, `PUT …/owner`, `DELETE …` | Mỗi thao tác sinh **đúng 1** row `activity_logs` tương ứng (`entity_type = "Workspace"`, `board_id IS NULL`) — ca `DELETE` assert trực tiếp qua `db.Activities` |
| W-27 | `GET …/activity` khi có item `payload = NULL` và item actor `user_id = NULL` | 200; `payload = null`, `userDisplayName = null` (không ném) |

**Đo:** `dotnet test` tăng **37 test case** cho §2 (37 test method, trong đó 2 `[Theory]` × 5 và × 2 `InlineData`
= 30 method + 7 case mở rộng) ⇒ tổng **226**, `Skipped: 0`. Đã chốt số thật vào §9 và cổng CI.

### 2.7 🪤 **4 cái bẫy khi viết test đã gặp (ghi lại để không lặp)**

Không phải bug sản phẩm — cả 4 đều là **test sai**, nhưng đều là bẫy mà người viết test kế tiếp sẽ gặp lại
ở §4 (frontend) hoặc Giai đoạn 11.

| # | Triệu chứng | Nguyên nhân thật | Cách đúng |
|---|---|---|---|
| **T1** | `Activity_ListsTheWorkspaceHistoryNewestFirst` và `Activity_FiltersByBoard…` thấy activity **rỗng** | `TestScenario.CreateTaskAsync` ghi **thẳng bằng EF**, bỏ qua endpoint ⇒ **không** đi qua `IActivityLogWriter` nên không có row nào | Mọi thao tác cần sinh activity phải đi qua **HTTP API** (`POST /api/boards/{b}/tasks`, rồi comment/move). Chỉ seed EF khi test **không** quan tâm activity |
| **T2** | `DeleteWorkspace_SoftDeletes…`: đếm membership ra **0** dù row vẫn còn | `WorkspaceMemberConfiguration` có query filter ẩn membership của workspace đã soft-delete — **đúng như thiết kế**, đó chính là hành vi đang được kiểm | Đếm bằng `db.WorkspaceMembers.IgnoreQueryFilters()` |
| **T3** | `…_WithoutTheAntiforgeryHeader_Returns403` ra **204** | `TestHttpClient.PutJsonAsync` **tự gắn lại** `X-XSRF-TOKEN` trên mọi request ⇒ `RemoveAntiforgeryHeader()` bị vô hiệu | Gửi bằng `client.Http.SendAsync(request)` (**bỏ qua** wrapper), đúng như `AuthApiTests.Refresh_WithoutTheXsrfHeader_IsRejected` |
| **T4** | `…_WithoutTheAntiforgeryHeader` ném `InvalidOperationException: No antiforgery token yet` | Gọi `scenario.ClearAntiforgeryToken()` rồi dùng request **có đi qua wrapper** ⇒ wrapper tự gắn header và ném vì token đã bị xoá | Dùng đúng T3 — **không** xoá token, chỉ đi vòng qua wrapper |

---

## 5. §3 — Frontend: Task UX (đóng ô **A**, **C**, **H**)

### 3.1 File **mới** — `frontend/src/features/board/utils/taskDueDate.ts` (hàm **thuần**)

```ts
/** Một "task đã xong" không bao giờ bị coi là quá hạn. */
export function isTaskCompleted(task: Pick<TaskResponse,'completedAt'>, isDoneColumn?: boolean): boolean
/** So sánh theo NGÀY (không theo giờ) — hạn hôm nay KHÔNG phải quá hạn. */
export function isOverdue(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): boolean
/** Số ngày quá hạn (>= 1 khi quá hạn, 0 khi không). */
export function overdueDays(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): number
/** Nhãn hiển thị: 'Quá hạn N ngày' | 'Hôm nay' | 'DD/MM'. */
export function dueDateLabel(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): string | null
```

- `now` là **tham số** (không đọc `Date.now()` bên trong) ⇒ test tất định, không cần `vi.useFakeTimers()`.
- So sánh bằng **mốc ngày** (`startOf('day')`) qua `dayjs` (**đã có** trong `package.json`) ⇒ "hạn hôm nay" không bị coi là quá hạn lúc 08:00.
- **Không** import React, không I/O.

### 3.2 File **mới** — `frontend/src/features/board/utils/markdown.tsx` (thuần + render)

```ts
export type MarkdownNode =
  | { kind: 'text'; value: string }
  | { kind: 'strong'; value: string }
  | { kind: 'em'; value: string }
  | { kind: 'code'; value: string }
  | { kind: 'link'; text: string; href: string }   // href LUÔN được validate

export function parseInlineMarkdown(text: string): MarkdownNode[]
export function isSafeHref(href: string): boolean
export function renderMarkdown(text: string | null | undefined): React.ReactNode
```

**Cú pháp hỗ trợ (đúng phạm vi roadmap — "in đậm, code inline, link"):**

| Cú pháp | Kết quả |
|---|---|
| `**đậm**` | `<strong>` |
| `*nghiêng*` | `<em>` |
| `` `code` `` | `<code>` |
| `[nhãn](https://x)` | `<a href="https://x" target="_blank" rel="noopener noreferrer">nhãn</a>` |
| `\n` | `<br />` |
| `~~gạch~~`, `# H1`, bảng, ảnh | **Ngoài phạm vi** ⇒ render nguyên văn (không ném) |

**Quy tắc an toàn (bắt buộc):**
- `isSafeHref` chỉ chấp nhận scheme `http:` / `https:` (so sau khi `trim()`, `toLowerCase()`); `javascript:`, `data:`, `file:`, `vbscript:` ⇒ **render thành text thường**, không tạo `<a>`.
- **Tuyệt đối không** `dangerouslySetInnerHTML`. HTML thô trong mô tả (`<img onerror=…>`) ⇒ hiện **nguyên văn**.
- Cặp dấu chưa đóng (`**x`, `` `x ``, `[x](`) ⇒ giữ nguyên văn bản gốc, không ném, không cắt cụt.
- `null` / `undefined` / `""` ⇒ trả `null` (để UI hiện placeholder "Chưa có mô tả").

### 3.3 File **sửa** — `frontend/src/features/board/components/TaskCard.tsx`

- Thay tính toán `isOverdue` inline (dòng 79–80) bằng `isOverdue(task, new Date(), isDoneColumn)` + `overdueDays(...)` + `dueDateLabel(...)` từ §3.1 ⇒ **một nguồn sự thật**, dùng lại được ở chỗ khác.
- Khi quá hạn, **thêm** (không thay thế phần đang có):
  - `borderLeft: '3px solid #ef4444'` trên card.
  - `Tag` đỏ `color="error"` nhỏ, `data-testid="task-card-overdue-badge"`, nội dung `Quá hạn N ngày`.
- Giữ **nguyên**: `getPriorityConfig` + badge priority, labels, badge AI Agent, clamp 2 dòng câu hỏi làm rõ, avatar agent, badge số bình luận, tooltip hạn chót.
- Giữ **nguyên chữ ký props** `{ task, isDoneColumn, onClick, isDragOverlay }` (không phá 2 chỗ gọi: `KanbanColumn`, `BoardView` drag overlay).
- Quy tắc: task trong cột `is_done` hoặc có `completedAt` ⇒ **không** badge đỏ (đã có `isCompleted`).

### 3.4 File **sửa** — `frontend/src/features/board/components/TaskDetailModal.tsx`

**(a) Đóng ô C — markdown Description**
- Thêm `Segmented` **"Soạn" / "Xem trước"** phía trên textarea Mô tả (dòng 438–445).
- Tab "Xem trước": `renderMarkdown(Form.useWatch('description', form))`; rỗng ⇒ `Typography.Text type="secondary"` "Chưa có mô tả".
- Giữ **nguyên** `Form.Item name="description"` + hành vi `onBlur={handleSaveMetadata}` (không đổi hợp đồng form/không đổi payload).

**(b) Đóng ô H — nghi vấn gửi giá trị CŨ (làm SAU khi test ở §5.6 đã viết)**
- Hiện tại: `onChange={handleSaveMetadata}` trên Select priority, DatePicker, Select cột.
  AntD v6 đọc giá trị từ `form.getFieldsValue()` **bên trong** `handleSaveMetadata`; `onChange` của control chạy
  đồng bộ cùng nhịp ⇒ **payload có thể vẫn là giá trị cũ**, và thao tác chỉ "được cứu" nhờ lần `onBlur` sau đó của Title/Description.
- **Quy trình bắt buộc:** viết test `TaskDetailModal` cho ca "đổi Priority ⇒ `onUpdateTask` nhận `priority` MỚI" **trước**.
  - Test **đỏ** ⇒ sửa theo hướng: `onChange={(v) => { form.setFieldValue('priority', v ?? null); handleSaveMetadata() }}` (làm tương tự cho DatePicker & Select cột). **Không** đổi `onUpdateTask`/`UpdateTaskRequest`.
  - Test **xanh** ⇒ **không sửa** gì thêm; ghi lại vào báo cáo là "không tái hiện được với AntD v6", kèm tên test.

### 3.5 File **sửa** — `KanbanColumn.tsx`
- **Không đổi.** Quick-add giữ nguyên `title` + `assignee` (D9). Lý do ghi vào tài liệu: ô A/B đã có đường set đầy đủ ở modal chi tiết; nhồi thêm priority/due date vào ô thêm nhanh làm UI chật mà không có yêu cầu nào trong roadmap.

### 3.6 Test frontend (Vitest + Testing Library)

| File | Số test | Nội dung |
|---|---|---|
| `utils/__tests__/taskDueDate.test.ts` (**mới**) | ~9 | `dueDate = null` ⇒ `isOverdue=false`, `dueDateLabel=null`; hạn **hôm nay** ⇒ không quá hạn; hạn **1 ngày trước** ⇒ quá hạn 1; **3 ngày trước** ⇒ 3; task có `completedAt` ⇒ không quá hạn (dù dueDate quá khứ); trong cột `is_done` ⇒ không quá hạn; hạn tương lai ⇒ `DD/MM`; mốc nửa đêm; task xong ⇒ `isTaskCompleted` true |
| `utils/__tests__/markdown.test.ts` (**mới**) | ~11 | `**đậm**`; `*nghiêng*`; `` `code` ``; `[nhãn](https://x)`; trộn cả 4 trong 1 câu; text thuần; `**` chưa đóng ⇒ nguyên văn; `[x](javascript:alert(1))` ⇒ `isSafeHref=false` và **không** sinh node link; `[x](data:text/html,…)` ⇒ unsafe; xuống dòng; `''` ⇒ `[]` |
| `utils/__tests__/renderMarkdown.test.tsx` (**mới**) | ~4 | render ra `<strong>`/`<code>`/`<a href>`; link có `rel="noopener noreferrer"`; HTML thô **không** thành thẻ; `null` ⇒ `null` |
| `components/__tests__/TaskCard.test.tsx` (**sửa — giữ 4 test cũ xanh**) | +5 | quá hạn ⇒ có `task-card-overdue-badge` + chữ `Quá hạn`; task xong ⇒ **không có** badge đỏ; hạn tương lai ⇒ hiện `DD/MM`; không hạn ⇒ không render phần hạn; **giữ nguyên** test priority tag & labels |
| `components/__tests__/TaskDetailModal.test.tsx` (**mới**) | ~9 | preview description render `<strong>`; toggle Soạn ⇄ Xem trước; rỗng ⇒ placeholder; **đổi Priority ⇒ `onUpdateTask` nhận `priority` MỚI** (test bắt ô H); đổi ngày ⇒ ISO đúng; xoá priority ⇒ `priority: null`; blur description ⇒ gọi update; đổi cột ⇒ gọi `onMoveTask`; vẫn có `data-testid="assignee-select"` |

**Đo:** tổng frontend mới **+38** ⇒ từ **206** lên **244** test, `37` → **41** file test.

---

## 6. §4 — Frontend: Workspace Settings & Activity (đóng ô **D**, **E**, **F**, **G**)

### 4.1 File **mới** — `frontend/src/shared/hooks/useWorkspaceRole.ts`

```ts
export type WorkspaceRole = 'Admin' | 'Manager' | 'Member'

/** HÀM THUẦN — test không cần render. */
export function resolveWorkspaceRole(
  rows: Array<{ id: string; role: string; ownerId?: string }>,
  workspaceId: string, userId?: string | null
): { role: WorkspaceRole | null; isMember: boolean; isManagerOrAdmin: boolean; isAdmin: boolean; isOwner: boolean }

export function useWorkspaceRole(workspaceId?: string): WorkspaceRoleState & { loading: boolean; reload: () => void }
```

- Gọi `GET /workspaces` (một lần; `useState` + `useEffect` với cờ `ignore` — cùng pattern hiện có, không thêm React Query).
- `role` so **không phân biệt hoa thường** (backend trả `Admin`/`Manager`/`Member` từ `Enum.ToString()`).
- `isOwner` = `isMember && userId != null && row.ownerId === userId`; **giữ tương thích** khi backend cũ chưa trả `ownerId` (⇒ `isOwner = false`, không ném).

### 4.2 File **sửa** — refactor (đóng ô **G**), **giữ nguyên hành vi**

| File | Thay đổi |
|---|---|
| `features/board/components/BoardView.tsx` (209–227) | Thay `httpClient.get('/workspaces')` + so role bằng `const { isManagerOrAdmin } = useWorkspaceRole(workspaceId)` |
| `features/reporting/pages/ReportsPage.tsx` (46–74) | Như trên; **giữ** `checkingRole` (dùng `loading` của hook) và nhánh `<Result 403>` |
| `features/auth/pages/DashboardPage.tsx` (29–38) | Dùng hook để lấy workspace đầu tiên (vẫn giữ input GUID thủ công — đổi Dashboard là **Giai đoạn 12**) |

> **Ràng buộc:** `BoardView.test.tsx` đang có phải **còn xanh**; nếu test cũ mock `httpClient.get('/workspaces')` thì giữ mock tương đương, **không** viết lại test cũ.

### 4.3 `frontend/src/features/workspace/` (**thư mục mới**)

```
types/workspace.types.ts          WorkspaceSummary, WorkspaceDetail, UpdateWorkspaceRequest,
                                  TransferOwnershipRequest, WorkspaceActivityItem,
                                  WorkspaceActivityPage, WorkspaceRole
services/workspaceApi.ts          list() · get() · update() · transferOwnership() ·
                                  deleteWorkspace() · getActivity()
hooks/useWorkspaceDetail.ts       { detail, loading, error, reload, save, transfer, remove }
hooks/useWorkspaceActivity.ts     { items, hasMore, nextCursor, loading, loadMore(), reload() }
components/WorkspaceSettingsModal.tsx
components/ActivityFeedItem.tsx
utils/activityLabels.ts           (hàm THUẦN)
pages/WorkspaceSettingsPage.tsx
pages/WorkspaceActivityPage.tsx
```

- **`workspaceApi`** dùng `httpClient` chung (⇒ tự gắn `X-XSRF-TOKEN` cho `PUT`/`DELETE`, tự refresh 401 — **không** tự đặt header CSRF).
  - `deleteWorkspace` (không đặt tên `delete` — tránh che từ khoá).
  - `getActivity(workspaceId, { boardId, action, entityType, take, before })` ⇒ bỏ mọi param `undefined` khỏi query string.
- **`WorkspaceSettingsModal`** — 2 tab:
  - **Thông tin**: `name` (required, tối đa 120), `description` (tối đa 2000, `Input.TextArea`); nút **Lưu** (disable khi không phải Manager+ hoặc form không đổi).
  - **Nguy hiểm** (chỉ owner/Admin thấy):
    - **Chuyển ownership**: `Select` member **lọc `memberType === 'human'`** (loại AI Agent), disable với chính owner hiện tại; `Popconfirm` xác nhận.
    - **Xoá workspace**: `Popconfirm` yêu cầu **gõ đúng tên workspace** vào `Input` mới cho bấm nút `Xoá` (chống xoá nhầm); mọi cảnh báo tiếng Việt.
- **`ActivityFeedItem`** + **`activityLabels.ts`**: map `action` → nhãn tiếng Việt; fallback an toàn cho action lạ (trả chính chuỗi action, **không ném**); enrich payload (`priority`, `fromColumnId`/`toColumnId` → tên cột nếu tra được); actor `null` ⇒ **"Hệ thống"**.
  - Nhãn gợi ý: `TaskCreated` "đã tạo thẻ" · `TaskUpdated` "đã cập nhật thẻ" · `TaskMoved` "đã chuyển thẻ" · `TaskCompleted` "đã hoàn thành thẻ" · `TaskDeleted` "đã xoá thẻ" · `CommentAdded` "đã bình luận" · `WorkspaceUpdated` "đã cập nhật workspace" · `WorkspaceOwnerTransferred` "đã chuyển quyền sở hữu" · `WorkspaceDeleted` "đã xoá workspace".
- **`WorkspaceActivityPage`**: bộ lọc `Segmented` (Tất cả / Thẻ / Bình luận / Workspace) + `Select` board (lấy từ `boardApi.getBoards`); danh sách; nút **"Tải thêm"** khi `hasMore`; `Empty` khi rỗng; `Spin` khi `loading`; không phải Manager+ ⇒ `<Result status="403">` + nút quay lại (theo `ReportsPage`).
- **`WorkspaceSettingsPage`**: nạp `useWorkspaceDetail`; không phải Manager+ ⇒ 403; nút mở modal; hiển thị owner, số member, số board, ngày tạo.

### 4.4 File **sửa** — định tuyến & điều hướng (đóng ô **F**)

| File | Thay đổi |
|---|---|
| `app/router.tsx` | Thêm `/workspaces/:workspaceId/settings` → `WorkspaceSettingsPage`; `/workspaces/:workspaceId/activity` → `WorkspaceActivityPage` (đều bọc `ProtectedRoute`) |
| `features/board/pages/BoardListPage.tsx` | Thêm nút **"Cài đặt"** (`SettingOutlined`) + **"Hoạt động"** (`HistoryOutlined`) cạnh **"Báo cáo"** (172–194); chỉ render khi `isManagerOrAdmin` (dùng hook §4.1) |
| `features/board/components/BoardView.tsx` | Thêm 2 nút tương tự trong nhóm action ở header (cạnh "Báo cáo"/"Lịch sử AI") |

### 4.5 Test frontend

| File | Số test | Nội dung |
|---|---|---|
| `shared/hooks/__tests__/useWorkspaceRole.test.ts` (**mới**) | ~6 | `resolveWorkspaceRole` thuần: Admin/Manager/Member/không-thành-viên/role chữ thường/`ownerId` khớp `userId` ⇒ `isOwner`; `ownerId` vắng ⇒ không ném |
| `features/workspace/services/__tests__/workspaceApi.test.ts` (**mới**) | ~7 | 6 hàm gọi đúng method + URL + body; `getActivity` truyền query đúng và **bỏ** param `undefined` |
| `features/workspace/utils/__tests__/activityLabels.test.ts` (**mới**) | ~5 | mỗi action ⇒ nhãn tiếng Việt; action lạ ⇒ fallback an toàn; payload rỗng ⇒ nhãn trần |
| `features/workspace/components/__tests__/ActivityFeedItem.test.tsx` (**mới**) | ~5 | render nhãn + tên actor; actor `null` ⇒ "Hệ thống"; payload `null` không crash; enrich tên cột; icon theo `entityType` |
| `features/workspace/components/__tests__/WorkspaceSettingsModal.test.tsx` (**mới**) | ~8 | validate tên rỗng; Lưu gọi `onSave`; Member ⇒ disable/ẩn tab Thông tin; **danh sách chuyển owner KHÔNG chứa AI Agent**; gõ sai tên ⇒ nút xoá disabled; gõ đúng ⇒ gọi `onDelete`; cảnh báo tiếng Việt hiện đúng |
| `features/workspace/pages/__tests__/WorkspaceActivityPage.test.tsx` (**mới**) | ~4 | hiển thị items; bấm "Tải thêm" ⇒ gọi `loadMore`; đổi filter ⇒ gọi lại API với filter mới; không phải Manager ⇒ 403 |

**Đo:** tổng frontend sau §3+§4 = **206 + 38 + 35 = 279** test · **41 + 6 = 47** file test.

---

## 7. §5 — Cấu hình, CI & tài liệu

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-web.yml` | Nâng cổng `if ($total -le 187)` → **`-le 206`** (baseline thật cuối Giai đoạn 8); sửa comment cho khớp ("baseline cuối Giai đoạn 7 là 187 → Giai đoạn 8 là 206 → Giai đoạn 10 là 279") |
| 2 | `.github/workflows/ci-backend.yml` | ✅ **ĐÃ NÂNG** — `if ($total -ne 226)` (chuỗi: 171 Giai đoạn 9 → 189 §1 → **226 §2**) |
| 3 | `Project-Documents/03-roadmap.md` | Giai đoạn 10: gắn link tài liệu này + baseline + "⛔ không migration" + 2 bug thật (theo mẫu Giai đoạn 6/7) |
| 4 | `README.md` | Thêm `## Trạng thái (Giai đoạn 10 – Nâng cao Task & Workspace UX)` với checklist theo 5 ô + nêu rõ **không** thêm migration |
| 5 | `Project-Documents/04-database-design.md` | **KHÔNG sửa.** Nếu thấy cần ⇒ tức là đã đi sai (xem "Không được làm" ở §10) |
| 6 | `Project-Documents/report/phase-10-...-test-report.md` | Tạo khi kết thúc: số test thật, bug thật bắt được, bằng chứng §9 |

---

## 8. Ca biên & chế độ lỗi (bắt buộc xử lý)

| Ca | Hành vi chốt |
|---|---|
| `GET /api/workspaces` khi user **không có** membership | Tự tạo workspace mặc định (**giữ nguyên** hành vi cũ — `DashboardPage` phụ thuộc) |
| Xoá workspace | Soft delete ⇒ `GET /{id}` **404**, `GET /api/workspaces` không còn id đó; `workspace_members`/`boards` **không** bị hard delete; `activity_logs` cũ **vẫn còn** trong DB nhưng **không** đọc được qua API (kỳ vọng đã biết) |
| Chuyển owner cho chính owner | **400** `{ error }` |
| Chuyển owner cho AI Agent | **400** `{ error }` |
| Chuyển owner cho người ngoài workspace | **400** `{ error }` |
| Owner cũ sau khi chuyển | **Giữ nguyên** role (không hạ cấp ngầm) |
| Manager (không phải owner) gọi chuyển owner / xoá workspace | **403** |
| Member gọi `PUT /api/workspaces/{id}` | **403** |
| User ngoài workspace gọi bất kỳ route `/{id}` | **404** (không rò rỉ sự tồn tại) |
| Cursor activity sai định dạng / GUID hỏng | **400** `{ error }`, **không** 500 |
| `take` âm / `0` / `> 200` | Clamp `[1, 200]`, mặc định **50** |
| Nhiều row cùng `CreatedAt` | Sắp phụ theo `Id` ⇒ keyset **không** mất/không lặp row |
| Payload activity `NULL` hoặc JSON lạ | UI hiển thị nhãn action trần, **không** crash |
| Activity trỏ tới board đã xoá | Vẫn hiển thị; tên board/cột fallback về GUID rút gọn |
| Markdown chứa `javascript:` / HTML thô | Render **text thường**, không tạo link/thẻ HTML |
| Markdown chưa đóng (`**x`) | Render nguyên văn, **không** ném |
| Mô tả `null`/`""` ở chế độ Xem trước | Placeholder "Chưa có mô tả" |
| Task quá hạn nhưng ở cột `is_done` / có `completedAt` | **Không** badge đỏ |
| `priority` là **số** (`"1"`, `"99"`) hoặc rỗng | **400** (BUG-1 đã sửa — xem §1.3) |
| `dueDate` có offset (vd `+07:00`) | **200**; chuẩn hoá UTC, giữ đúng instant (BUG-2 đã sửa — xem §1.3) |
| 403/404 ở trang Settings/Activity | `<Result>` + nút quay lại (không màn hình trắng) |
| Mất mạng / 401 khi gọi API workspace | `httpClient` interceptor tự refresh (đã có); lỗi còn lại ⇒ `message.error` tiếng Việt |

---

## 9. DoD & bằng chứng phải nộp

### 9.1 Con số mục tiêu

| Chỉ số | Baseline | Sau §1 | Sau §2 | Sau cả Giai đoạn 10 |
|---|---|---|---|
| Backend `dotnet test` (PostgreSQL 18 thật) | **171** (0 fail / **0 skip**) | ✅ **189** (0 fail / **0 skip**) | ✅ **226** (0 fail / **0 skip**) | **≥ 281** |
| Backend `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 / 0 | ✅ 0 / 0 | **0 / 0** |
| Frontend `npm test` | **206** (37 file) | 206 (chưa chạm) | **279** (47 file) |
| Frontend `npm run lint` | 0 warning / 0 error | 0 / 0 | **0 / 0** |
| Frontend `npx tsc -b` | exit 0 | exit 0 | **exit 0** |
| Frontend `npm run build` | OK | OK | **OK** |
| `dotnet ef migrations list` | **8** | ✅ **8 (KHÔNG ĐỔI)** | **8 (KHÔNG ĐỔI)** |
| `has-pending-model-changes` | không có | ✅ **không có** | **không có** |

> **Cổng "xanh giả" vẫn phải giữ:** `dotnet test` trả `Skipped: 0` khi đã đặt `TEAMNEXUS_TEST_DB`;
> local không có DB thì ra `98 / 73 / 171` và **vẫn exit 0** ⇒ không được dùng làm bằng chứng.
> Nhóm test thuần (`tests/…/Pure/`) chạy được **không cần** DB.

### 9.2 Bảng bằng chứng

| # | Bằng chứng | Cách đo | Ngưỡng |
|---|---|---|---|
| 1 | `npm run lint` | `frontend/` | 0 warning / 0 error |
| 2 | `npx tsc -b` | `frontend/` | exit 0 |
| 3 | `npm test` | `frontend/` | **≥ 279**, 0 fail — **dán số thật** |
| 4 | `npm run build` | `frontend/` | OK |
| 5 | `dotnet build TeamNexus.sln -m:1 -nr:false` | repo root | 0 warning / 0 error |
| 6 | `dotnet test tests/TeamNexus.Api.Tests/…csproj` (đã đặt `TEAMNEXUS_TEST_DB`) | PostgreSQL 18 thật | **≥ 281** passed / 0 failed / **0 skipped** (sau §2 đang là **226**) |
| 7 | `dotnet ef migrations list` + `has-pending-model-changes` | `--no-build` | **8** / không pending |
| 8 | `docker run --name teamnexus-pg -e POSTGRES_PASSWORD=… -p 5432:5432 -d postgres:18` | nếu máy chưa có DB | fixture tự `CREATE DATABASE TeamNexus_Test` |
| 9 | Ảnh/ghi chú 1 lượt thao tác thật trên trình duyệt | local | đổi tên workspace · chuyển owner · xoá workspace có xác nhận gõ tên · xem Activity · preview markdown · badge đỏ quá hạn |
| 10 | 2 workflow CI xanh | GitHub Actions | `ci-backend` (không skip, `total = 226`) + `ci-web` (`total ≥ 279`) |

### 9.3 Điều kiện "xong"
Cả **5 ô** (A–E) + **3 hạng mục phát sinh** (F, G, H) đóng bằng bằng chứng ở §9.2; hai cổng CI đã nâng baseline và chạy xanh; `03-roadmap.md` + `README.md` đã ghi **số thật**; báo cáo tại `Project-Documents/report/phase-10-advanced-task-workspace-ux-test-report.md`.

---

## 10. ⛔ KHÔNG được làm (để không phá thứ đã verify)

- ❌ **Không sinh migration**, không sửa `04-database-design.md`, không thêm cột/bảng/enum DB. `activity_logs.action` là **free text** ⇒ tên action mới **không** cần constraint.
- ❌ **Không đổi shape** các hợp đồng đã verify: `TaskResponse` (17 field, thứ tự), `BoardResponse`, `ColumnResponse`, `WorkspaceMemberResponse`, `HubConnectionStatus`, payload SignalR.
- ❌ **Không đổi tên event SignalR** và **không** đổi kiến trúc 1-hub-1-group (`useBoardHub.ts`).
- ❌ **Không sửa** `shared/api/httpClient.ts` (CSRF + refresh rotation đã được 189 test backend chứng minh phía sau).
- ❌ **Không sửa** `features/reporting/utils/reportDownload.ts` (pattern tải blob đã verify).
- ❌ **Không thêm thư viện npm** (markdown, date, editor…) — `dayjs` · `antd` · `zustand` · `axios` · `@microsoft/signalr` là đủ.
- ❌ **Không dùng `dangerouslySetInnerHTML`** ở bất kỳ đâu (kể cả để "tiện" render markdown).
- ❌ **Không** tự đặt header CSRF trong `workspaceApi` (đã tự động ở interceptor).
- ❌ **Không** mở rộng phạm vi sang: mời member qua email, đổi role member, kick member (**Giai đoạn 11**); Dashboard/Tìm kiếm/@mention (**Giai đoạn 12**); hard delete workspace; gửi email.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**; nếu buộc phải sửa thì ghi rõ lý do vào báo cáo).

---

## 11. Rủi ro & giả định

| # | Mục | Xử lý |
|---|---|---|
| **R1** | Rút `GET /api/workspaces` khỏi `Program.cs` chạm 3 chỗ FE đang parse (`DashboardPage` 31, `BoardView` 212, `ReportsPage` 50) và có thể chạm test đang seed membership | Chỉ **append** 2 field mới ở cuối; chạy `dotnet test` **trước và sau** bước rút code; `BoardView.test.tsx` phải còn xanh |
| **R2** | Ô **H** (Select gửi giá trị cũ) có thể **không** tái hiện được với AntD v6 | **Test trước, sửa sau** (§3.4b). Test xanh ⇒ **không sửa**, ghi lại là "không tái hiện" |
| **R3** | Phình phạm vi (Settings/Activity dễ kéo theo Dashboard/Member management) | Danh sách "⛔ KHÔNG được làm" ở §10 là hợp đồng cứng |
| **R4** | Trần `take = 200` activity có thể chưa đủ workspace lớn | Filter + keyset cho phép lấy tiếp; trần là **quyết định có chủ ý** (chống payload lớn); nâng sau nếu có yêu cầu |
| **R5** | `LeftJoin` + `jsonb` map sang `string` dễ sai kiểu khi deserialize | Test W-27 phủ `payload = NULL`; DTO dùng `JsonElement?` (không `object`) |
| **R6** | Xoá workspace có thể để lại "rác" vô hình | Đã kiểm: `WorkspaceConfiguration` 28 (query filter) + FK toàn `Restrict` + `WorkspaceMemberConfiguration` 59 ⇒ boards/tasks/members tự ẩn, không cascade. Test W-16 assert `workspace_members` **vẫn** còn row |
| **R7** | CSS/BEM: badge đỏ mới có thể trùng với tag priority `Urgent` (cũng đỏ) | Badge quá hạn dùng `data-testid` riêng + icon `ClockCircleOutlined`/`WarningOutlined` để phân biệt bằng **icon**, không chỉ bằng màu |

**Giả định:**
- Baseline **171 backend / 206 frontend (37 file) / 8 migration** là số **đo thật** tại §1.2 trên máy dev; nếu CI hoặc máy khác lệch ⇒ **số đo thắng tài liệu**, cập nhật lại §1.2 + cổng CI.
- Giai đoạn 9 (role simplification) đã merge ⇒ Identity role chỉ còn `Admin`/`User`, workspace role **không** đổi (Admin/Manager/Member). Mọi cổng quyền của giai đoạn này đọc `workspace_members.role`, **không** đọc Identity role.
- PostgreSQL 18 sẵn sàng (local hoặc Docker) cho `dotnet test`; không có DB thì nhóm DB **skip** và **không** được tính là đạt DoD.
- Frontend: React 19 + TypeScript 6 + Ant Design 6 + Vite 8 + Vitest 5; toàn bộ nhãn UI **tiếng Việt có dấu**.
- Endpoint mới **không** đụng mobile (Flutter — Giai đoạn 13) nên không cần giữ tương thích ngược cho client khác ngoài `GET /api/workspaces`.
