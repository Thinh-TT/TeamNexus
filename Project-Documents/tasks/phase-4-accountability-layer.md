# Giai đoạn 4 – Accountability Layer

> **Mục tiêu:** Xây bảng `ai_action_logs` + service `AiActionService` làm **cổng trung gian duy nhất** cho mọi hành động AI ghi dữ liệu (vòng đời `Pending → Approved / Rejected → Undone`), rồi gắn vào luồng AI Smart Setup của Giai đoạn 3: nút "Xác nhận" ở UI **không ghi DB nữa** mà tạo một `AiActionLog` ở trạng thái `Pending`; chỉ khi con người **Duyệt** mới sinh `tasks`/`labels`/`task_labels` thật, và **Hoàn tác** được hành động đó.
>
> **Công nghệ:** ASP.NET Core (Modular Monolith) · EF Core (PostgreSQL, `jsonb`) · Minimal API + endpoint filters (CSRF/domain-error) · React + TypeScript + Vite + Ant Design · Vitest.
>
> **Tham chiếu:** `03-roadmap.md` (Giai đoạn 4) · `02-tech-stack-decisions.md` §2.4 · `04-database-design.md` §3.5 · contract bàn giao ở `tasks/phase-3-ai-smart-setup.md` §7 và `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` §"Contract bàn giao Giai đoạn 4".

---

## 0. Tiền đề & quyết định kiến trúc (đã chốt)

Đọc hết mục này trước khi code — các quyết định dưới đây đã chốt, **không chọn lại**.

| # | Quyết định | Lý do / ghi chú |
|---|---|---|
| **D1** | **Không tạo module mới.** Entity log ở `TeamNexus.Persistence`, service + endpoint ở module `TeamNexus.Modules.Ai` | Khớp `02` §1: module `Ai` = Smart Setup + Observer + Accountability |
| **D2** | `AiActionLog` + enum `AiActionStatus { Pending, Approved, Rejected, Undone }` đặt ở `src/TeamNexus.Persistence/Data/Entities/`; hằng `AiActionTypes` (`CreateSubtasks`) + `AiEntityTypes` (`Board`) đặt ở module Ai | `status` là enum đóng (có `CHECK`), `action`/`entity_type` là text mở (04 §4: enum mở rộng thì dùng text tự do) |
| **D3** | Cột jsonb map sang **`string`** + `.HasColumnType("jsonb")` (không dùng owned-JSON/POCO) | Npgsql hỗ trợ string ↔ jsonb; tránh owned type, serialize/deserialize DTO đơn giản, dễ verify |
| **D4** | `IAiActionService` là **cổng duy nhất**: mọi hành động AI ghi dữ liệu đi qua nó → `IAiActionApplier` chọn theo `Action` | Yêu cầu roadmap "service duy nhất"; applier registry cho phép Giai đoạn 5+ thêm loại action mới **không sửa** service |
| **D5** | Vòng đời: `Pending → Approved → Undone`; `Pending → Rejected`. **Mọi chuyển trạng thái khác → 409** | `Rejected`/`Undone` là trạng thái cuối |
| **D6** | Chống apply trùng: đổi trạng thái bằng **CAS** (`UPDATE ai_action_logs SET status=… WHERE id=@id AND status='Pending'`) **trong cùng transaction** với bước ghi dữ liệu; 0 row → **409** | Schema không có concurrency token; CAS + transaction cho idempotency thật ở tầng DB |
| **D7** | Khi apply, **chỉ gọi service Board hiện có**: `ITaskService.CreateTaskAsync`, `ILabelService.CreateLabelAsync/AttachToTaskAsync`, `IColumnService.GetColumnsAsync`, `IWorkspaceAccess`, `IBoardEventPublisher` | Reuse thay vì viết lại insert; tự có validate 1–200/priority + broadcast SignalR `TaskCreated`/`TaskDeleted`. **Không sửa module Board** |
| **D8** | Quyền: `confirm` **và** mọi thao tác trên log = **Manager/Admin** của workspace chứa board. Người tạo được tự duyệt (MVP) | Cùng gate với `SmartSetupService` §4.3 bước 2 (`IWorkspaceAccess.RequireManagerAsync`). Tách vai trò người tạo ≠ người duyệt: non-goal |
| **D9** | Cột đích khi apply: body `columnId?`; thiếu → **cột đầu tiên theo `position`**; board không có cột → **400** | `SmartSetupProposal` (Phase 3 §4.2) không mang column |
| **D10** | Undo (`CreateSubtasks`): soft-delete các task đã tạo + broadcast `TaskDeleted`; xoá `task_labels` của chúng; xoá nhãn **do chính action này tạo** nếu không còn task nào khác tham chiếu; task đã bị người dùng xoá trước đó → bỏ qua | Roadmap chỉ cần Undo cho 1 loại action; tránh rác nhãn workspace |
| **D11** | `after_snapshot` = proposal người dùng đã sửa (đúng contract Phase 3); **`applied_snapshot`** = dữ liệu ghi thật (taskIds/labelIds/warnings) → cơ sở revert chính xác. `before_snapshot` = `null` với `CreateSubtasks` | Không nhồi id vào `after_snapshot`; giữ nguyên ý nghĩa "đề xuất" |
| **D12** | Validate **phía server trước khi ghi log** (pure static, verify được): description 1–4000; 1..`MaxTaskCount` task; title 1–200; description ≤ 2000; priority ∈ enum hoặc `null`; labels ≤ 5 nhãn × ≤ 80 ký tự; `labelId` phải thuộc workspace; `assigneeUserId` phải là member workspace; `columnId` phải thuộc board | Không tin client (proposal đã bị sửa ở UI); dùng lại hằng số `SmartSetupService.*` |
| **D13** | Liệt kê log theo **board**: `GET /api/boards/{boardId}/ai-actions` dùng `entity_type='Board'` + `entity_id=boardId`. **Không** thêm cột `workspace_id` | Đủ cho UI (drawer trong BoardView); queue pending xuyên workspace để Giai đoạn 5 nếu cần |
| **D14** | Màu nhãn auto-create: palette cố định xoay vòng theo index — `#6366F1`, `#10B981`, `#F59E0B`, `#EF4444`, `#8B5CF6` | `SmartSetupLabelSuggestion` không mang màu; không mở rộng UI Phase 3 |
| **D15** | **Không** thêm SignalR event riêng cho AI action: drawer tự `reload()` khi mở và sau mỗi thao tác; task mới đã real-time sẵn qua `TaskCreated` (`useBoardHub`) | Tránh sửa module Board; broadcast event cho AI action là hạng mục optional (§5.3) |
| **D16** | **Không** tạo project xUnit (để Giai đoạn 7). Verify backend bằng harness probe tạm ngoài workspace + API thật + PostgreSQL thật (đúng cách Phase 2–3) | Nhất quán repo |

**Non-goals Giai đoạn 4 (ghi rõ để không over-scope):** sửa/thu hồi một log `Pending` (phải `Reject` rồi confirm lại); separation of duties; time-window cho Undo; queue pending xuyên workspace; SignalR event riêng cho AI action; test xUnit; undo cho action khác ngoài `CreateSubtasks`.

**Luồng tổng thể:**

```
POST /boards/{id}/smart-setup            (Phase 3 — chỉ đọc, không ghi DB)
        │  proposal (người dùng sửa trên UI)
        ▼
POST /boards/{id}/smart-setup/confirm    → AiActionLog { Pending }   ← KHÔNG ghi tasks/labels
        │
        ├── POST /ai-actions/{id}/approve → applier.ApplyAsync → tasks/labels/task_labels + SignalR
        ├── POST /ai-actions/{id}/reject  → chỉ đổi trạng thái
        └── POST /ai-actions/{id}/undo    → soft-delete tasks + dọn nhãn (chỉ từ Approved)
```

---

## 1. Backend – Schema & Migration

### 1.1 Entity `AiActionLog` (`src/TeamNexus.Persistence/Data/Entities/AiActionLog.cs`)

- [x] Tạo enum `AiActionStatus { Pending, Approved, Rejected, Undone }` (cùng file entity).
- [x] Tạo class `AiActionLog : IAuditableEntity` với các field (khớp `04-database-design.md` §3.5 sau khi tinh chỉnh):

  | Field | Kiểu C# | Ghi chú |
  |---|---|---|
  | `Id` | `Guid` | PK, sinh ở app layer |
  | `Action` | `string` | ≤ 64, giá trị từ `AiActionTypes` (text tự do — 04 §4) |
  | `EntityType` | `string` | ≤ 64, giá trị từ `AiEntityTypes`; với `CreateSubtasks` = `"Board"` |
  | `EntityId` | `Guid?` | `boardId` với `CreateSubtasks` |
  | `Basis` | `string` (jsonb) | **not null** — tóm tắt input (cơ sở của hành động) |
  | `BeforeSnapshot` | `string?` (jsonb) | `null` với `CreateSubtasks` (chưa có gì để revert) |
  | `AfterSnapshot` | `string?` (jsonb) | Proposal đã validate (sẽ ghi khi Approved) |
  | `AppliedSnapshot` | `string?` (jsonb) | Dữ liệu **đã ghi thật** khi Approve (taskIds/labelIds/warnings) — cơ sở Undo |
  | `Status` | `AiActionStatus` | text + CHECK, mặc định `Pending` |
  | `RequestedByUserId` | `Guid` | FK → `users` |
  | `DecidedByUserId` | `Guid?` | FK → `users` |
  | `DecidedAt` | `DateTimeOffset?` | |
  | `DecisionNote` | `string?` | ≤ 500 — lý do từ chối/hoàn tác (tuỳ chọn) |
  | `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | tự stamp qua `IAuditableEntity` |

  Navigation: `RequestedByUser` / `DecidedByUser` (`ApplicationUser?`).

### 1.2 Fluent config (`Data/Configurations/AiActionLogConfiguration.cs`)

- [x] `builder.ToTable("ai_action_logs", table => table.HasCheckConstraint("ck_ai_action_logs_status", "\"status\" IN ('Pending', 'Approved', 'Rejected', 'Undone')"));`
- [x] `Action`/`EntityType` `HasMaxLength(64).IsRequired()`; `DecisionNote` `HasMaxLength(500)`.
- [x] `Status` → `.HasConversion<string>().HasMaxLength(16)`.
- [x] `Basis` (bắt buộc) + `BeforeSnapshot`/`AfterSnapshot`/`AppliedSnapshot` → `.HasColumnType("jsonb")`.
- [x] FK `RequestedByUser` (required) và `DecidedByUser` (nullable) → `DeleteBehavior.Restrict` (04 §7: **không cascade vật lý**).
- [x] Index: `(EntityType, EntityId, CreatedAt)` cho liệt kê theo board; `RequestedByUserId` theo 04 §5.
- [x] **Không** đặt query filter (log là lịch sử bất biến, không soft delete).

> **Ghi chú hiện thực §1 (khác biệt nhỏ so với spec, có chủ ý):**
> - `Status` mặc định bằng **initializer trong entity** (`= AiActionStatus.Pending`), không dùng `HasDefaultValue` — tránh DB default cho cột enum→string.
> - `Basis` khởi tạo `"{}"` (JSON hợp lệ) để hết CS8618 mà vẫn insert được row rỗng trước khi service điền.
> - Index `requested_by_user_id` **không khai báo tường minh**: EF FK index convention tự sinh `ix_ai_action_logs_requested_by_user_id` (đúng dòng index ở 04 §5); FK `decided_by_user_id` cũng sinh index phụ `ix_ai_action_logs_decided_by_user_id` — chấp nhận (không đổi schema/spec).
> - **jsonb KHÔNG round-trip byte-identical:** PostgreSQL chuẩn hoá `jsonb` (đổi thứ tự key, thêm khoảng trắng sau dấu `:`) — verify thực tế cho thấy `{"boardId":"b-1",…}` đọc lại thành `{"boardId": "b-1", …}` và key `after_snapshot` bị xếp lại. ⇒ **§2.3/§2.5 tuyệt đối không so sánh snapshot bằng string equality**; parse `JsonElement` (`JsonElement.DeepEquals`) rồi so sánh ngữ nghĩa.

### 1.3 DbContext

- [x] `TeamNexusDbContext`: thêm `public DbSet<AiActionLog> AiActionLogs => Set<AiActionLog>();` (config tự được `ApplyConfigurationsFromAssembly` nhặt).

### 1.4 Migration

- [x] `dotnet ef migrations add Phase4AccountabilityLayer --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api` — sinh `20260910073621_Phase4AccountabilityLayer.cs` + `.Designer.cs`
- [x] `dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api` — đã áp lên PostgreSQL local (DB `TeamNexus`)
- [x] Cập nhật `Project-Documents/04-database-design.md`: §3.5 thêm 2 cột `applied_snapshot`, `decision_note` + ghi chú quy ước `entity_type='Board'`/`entity_id=boardId`; §5 thêm dòng index `ai_action_logs (entity_type, entity_id, created_at)` — *đã làm ở bước lập kế hoạch (commit `bab22d3`)*
- [x] **Verify §1:** `dotnet build TeamNexus.sln` → **0 warning / 0 error**; migration sinh đúng 15 cột (4 `jsonb`, `status` varchar(16), `decision_note` varchar(500)), `ck_ai_action_logs_status`, 2 FK `users` RESTRICT, 3 index; `dotnet ef migrations list` → 4 migration **đã applied**; 3 migration cũ không bị sửa (`git diff` chỉ +109 dòng snapshot cho entity mới); **33 check PASS** bằng harness tạm (information_schema/pg_constraint/pg_indexes + round-trip insert→read→delete + probe CHECK 23514 + probe FK 23503), bảng về 0 row sau verify, harness đã xoá.

---

## 2. Backend – `AiActionService` (cổng duy nhất)

### 2.1 Hằng số (`Services/AiActionTypes.cs`)

- [x] `public static class AiActionTypes { public const string CreateSubtasks = "CreateSubtasks"; }`
- [x] `public static class AiEntityTypes { public const string Board = "Board"; }`

### 2.2 Applier abstraction (`Services/IAiActionApplier.cs`)

- [x] Định nghĩa:
  ```csharp
  public interface IAiActionApplier
  {
      string ActionType { get; }   // khớp AiActionLog.Action
      Task<AiActionAppliedResult> ApplyAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default);
      Task<IReadOnlyList<string>> UndoAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default); // warnings
  }

  public sealed record AiActionContext(Guid BoardId, Guid WorkspaceId, Guid ActingUserId);

  public sealed record AiActionAppliedResult(
      string EntityType,
      Guid? EntityId,
      IReadOnlyList<Guid> CreatedTaskIds,
      IReadOnlyList<Guid> CreatedLabelIds,
      IReadOnlyList<string> Warnings);
  ```
- [x] Lý do tách applier: `AiActionService` chỉ lo guard/vòng đời/transaction; phần "ghi dữ liệu thật" của từng loại action nằm ở applier — thêm loại mới = thêm 1 class + 1 dòng DI.

### 2.3 `Services/AiActionService.cs`

- [x] Interface `IAiActionService`:
  ```csharp
  Task<AiActionLogResponse> RequestCreateSubtasksAsync(Guid boardId, ConfirmSmartSetupRequest request, Guid userId, CancellationToken ct = default);
  Task<IReadOnlyList<AiActionLogResponse>> ListAsync(Guid boardId, AiActionStatus? status, int take, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> GetAsync(Guid logId, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> ApproveAsync(Guid logId, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> RejectAsync(Guid logId, string? note, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> UndoAsync(Guid logId, Guid userId, CancellationToken ct = default);
  ```
- [x] `RequestCreateSubtasksAsync(boardId, request, userId, ct)`:
  1. Load board `AsNoTracking` → không thấy/soft-deleted → **404**.
  2. `IWorkspaceAccess.RequireManagerAsync(board.WorkspaceId, userId)` → Member **403**.
  3. `ValidateConfirmRequest(request, board, columns, members, labels, maxTaskCount)` → **400** (pure static, §2.5). **Chưa ghi gì vào DB.**
  4. Tạo `AiActionLog`: `Action = CreateSubtasks`, `EntityType = Board`, `EntityId = boardId`, `Status = Pending`, `Basis = BuildBasis(...)`, `AfterSnapshot = BuildAfterSnapshotJson(request)`, `RequestedByUserId = userId`.
  5. `SaveChangesAsync` **chỉ insert log** (không có `Tasks`/`Labels`/`TaskLabels` nào được add).
  6. Trả `AiActionLogResponse` (`status = Pending`).
- [x] `ApproveAsync(logId, userId)`:
  1. Load log → **404**; `EntityType != Board || EntityId is null` → **400**.
  2. Resolve board + `RequireManagerAsync` → **403** (board đã xoá → **404**, log vẫn `Pending`).
  3. Resolve applier theo `log.Action` (`IEnumerable<IAiActionApplier>`, so khớp kiểu `OrdinalIgnoreCase`); không có → **400** `"Unsupported AI action: …"`.
  4. `await using var tx = BeginTransactionAsync(ct);`
  5. **CAS**: `UPDATE ai_action_logs SET status='Approved', decided_by_user_id=@uid, decided_at=@now WHERE id=@id AND status='Pending'` → 0 row ⇒ **409** `"Action is not Pending (already decided)."` (rollback).
  6. `applier.ApplyAsync(log, ctx, ct)` → ghi `applied_snapshot` từ `AiActionAppliedResult` (`EntityType`, `EntityId`, `CreatedTaskIds`, `CreatedLabelIds`, `Warnings`) → `SaveChangesAsync` → `CommitAsync`.
  7. Lỗi bất kỳ ở bước 6 ⇒ rollback toàn bộ (kể cả CAS) ⇒ log **vẫn `Pending`**, không partial-write; lỗi domain của Board (`BoardModuleException`) được ném nguyên vẹn để `DomainExceptionFilter` map status.
  8. Trả `AiActionLogDetailResponse` (status `Approved`, kèm `createdTaskIds`).
- [x] `RejectAsync(logId, note, userId)`: guard như Approve bước 1–2; **không** gọi applier; CAS `Pending → Rejected` + `decision_note` (trim, cap 500) + `decided_by/decided_at`; 0 row → **409**.
- [x] `UndoAsync(logId, userId)`: guard bước 1–3 (applier phải resolve được); transaction + CAS `Approved → Undone` + `decided_by/decided_at` → `applier.UndoAsync(...)` → nếu `Warnings` mới phát sinh thì `MergeUndoWarnings` vào `applied_snapshot` → commit; 0 row → **409**. `Pending`/`Rejected` → 409. *(Không set `decision_note` khi undo — giữ nguyên note từ chối trước đó nếu có.)*
- [x] `ListAsync(boardId, status?, take, userId)`: board → 404; `RequireManagerAsync` → 403; `take` mặc định 20, clamp 1–100; lọc `EntityType == Board && EntityId == boardId` (+ `Status` nếu có); sort `CreatedAt DESC`; join `users` để lấy `RequestedByName`/`DecidedByName`; `TaskCount` = số phần tử `tasks` trong `after_snapshot` (parse `JsonDocument`, lỗi parse ⇒ 0, không ném).
- [x] `GetAsync(logId, userId)`: log → 404; dùng `EntityId` để `RequireManagerAsync` → 403; trả detail kèm `Basis`/`BeforeSnapshot`/`AfterSnapshot`/`AppliedSnapshot` dạng `JsonElement?` + `CreatedTaskIds`.
- [x] Log `Information` khi request/approve/reject/undo (log id, action, status, số task) — **không** log nội dung mô tả dài (chỉ độ dài/300 ký tự đầu trong `basis`).

### 2.4 `CreateSubtasksApplier` (`Services/Appliers/CreateSubtasksApplier.cs`)

- [x] `ApplyAsync(log, ctx, ct)`:
  1. Parse `after_snapshot` → `ConfirmSmartSetupRequest` (dùng `JsonSerializerDefaults.Web`).
  2. Resolve cột đích (D9): `request.ColumnId` có giá trị ⇒ verify thuộc board (nếu không → **400**); `null` ⇒ cột đầu tiên theo `Position` (`IColumnService.GetColumnsAsync(boardId, ctx.ActingUserId, ct)`); board không có cột → **400**.
  3. Load lại danh sách nhãn workspace + member (bắt buộc kiểm tra tại thời điểm apply, không tin dữ liệu lúc confirm).
  4. Với mỗi nhãn có `Exists == false`: `ILabelService.CreateLabelAsync(workspaceId, new CreateLabelRequest(name, paletteColor(index)), userId)`; nếu `ConflictException` (nhãn vừa được tạo song song) ⇒ query lại nhãn theo tên (case-insensitive) và dùng id đó (không fail); nhãn `Exists == true` ⇒ dùng `LabelId` đã validate thuộc workspace.
  5. Với mỗi task (theo thứ tự trong proposal):
     - `assigneeUserId`: nếu có trong danh sách member ⇒ dùng; nếu **không còn là member** ⇒ gán `null` và thêm `Warnings` (`"Assignee <tên> is no longer a member; task created unassigned."`).
     - `ITaskService.CreateTaskAsync(boardId, new CreateTaskRequest(columnId, title, description, assigneeId, null, priority), ctx.ActingUserId, ct)` → task nối cuối cột (`position = max + 1`), tự broadcast `TaskCreated`.
     - Với mỗi nhãn đã resolve: `ILabelService.AttachToTaskAsync(task.Id, new AttachLabelRequest(labelId), ctx.ActingUserId, ct)`.
     - Thu `CreatedTaskIds`.
  6. Trả `AiActionAppliedResult(AiEntityTypes.Board, boardId, taskIds, createdLabelIds, warnings)`.
- [x] `UndoAsync(log, ctx, ct)`:
  1. Parse `applied_snapshot` → `CreatedTaskIds` + `CreatedLabelIds`; rỗng ⇒ return `[]` (không lỗi).
  2. Task còn tồn tại: `_db.Tasks.IgnoreQueryFilters().Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null)` → `ITaskService.DeleteTaskAsync(taskId, ctx.ActingUserId, ct)` (soft delete + broadcast `TaskDeleted`) cho **từng** task active; task đã bị người dùng xoá trước đó ⇒ bỏ qua, không broadcast lại.
  3. Xoá `task_labels` của các task đó (`_db.TaskLabels.IgnoreQueryFilters().Where(tl => taskIds.Contains(tl.TaskId))` → `RemoveRange`) — bắt buộc `IgnoreQueryFilters` vì filter của `TaskLabelConfiguration` ẩn junction của task đã soft-delete.
  4. Với mỗi `createdLabelId`: nếu **không còn** `task_labels` nào tham chiếu (`IgnoreQueryFilters`, sau bước 3) ⇒ `ILabelService.DeleteLabelAsync(workspaceId, id, userId)`; ngược lại **giữ nhãn** + thêm warning; nhãn đã biến mất (`NotFoundException`) ⇒ warning, không fail.
  5. Service merge warnings vào `applied_snapshot` (khoá `undoWarnings`) khi có.
- [x] ⚠️ Applier là **nơi duy nhất** trong module Ai được add/remove `Tasks`/`Labels`/`TaskLabels`; mọi write nghiệp vụ vẫn qua service Board (`CreateTaskAsync`/`CreateLabelAsync`/`AttachToTaskAsync`/`DeleteTaskAsync`/`DeleteLabelAsync`), chỉ việc dọn junction `task_labels` là ghi trực tiếp `_db` (kiểm tra ở §5.1 bước 10).

### 2.5 Validate + build thuần (verify không cần HTTP/DB)

- [x] `public static void ValidateConfirmRequest(ConfirmSmartSetupRequest request, Guid boardId, IReadOnlyList<ColumnResponse> columns, IReadOnlyList<WorkspaceMemberResponse> members, IReadOnlyList<WorkspaceLabel> labels, int maxTaskCount)`:
  - `Description` trim rỗng hoặc > `SmartSetupService.MaxDescriptionLength` (4000) → 400.
  - `Tasks` rỗng → 400; > `maxTaskCount` (`DeepSeekOptions.MaxTaskCount`, mặc định 20) → 400 (không tự cắt — đây là lệnh ghi dữ liệu, phải tường minh).
  - Mỗi task: title trim 1–`MaxTitleLength` (200); description cắt/kiểm ≤ `MaxTaskDescriptionLength` (2000); `Priority` phải parse được sang `TaskPriority` hoặc `null` (dùng `Enum.TryParse` ignoreCase); labels ≤ `MaxLabelsPerTask` (5), mỗi `Name` trim 1–`MaxLabelLength` (80), `LabelId` (khi `Exists == true`) phải nằm trong `labels` của workspace (nếu không → 400); `Assignee?.UserId` nếu có phải nằm trong `members` (nếu không → 400).
  - `Summary` **cắt** ≤ 2000 khi lưu `after_snapshot` (không 400 — đây là field tóm tắt, không phải dữ liệu nghiệp vụ); `ColumnId` nếu có phải thuộc `columns` (nếu không → 400).
- [x] `public static string BuildBasis(...)` → JSON tóm tắt input (kiểm soát token/chi phí — không dump toàn văn):
  ```json
  { "boardId": "…", "boardName": "…", "workspaceId": "…",
    "descriptionLength": 812, "descriptionExcerpt": "300 ký tự đầu",
    "memberCount": 3, "columnCount": 4, "taskCount": 7, "requestedAt": "2026-…" }
  ```
  Không đưa `ApiKey`/prompt thô vào `basis`.
- [x] `public static string BuildAfterSnapshotJson(ConfirmSmartSetupRequest request)` → serialize camelCase (Web defaults), cap `Summary` như trên.
- [x] `public static string BuildAppliedSnapshotJson(AiActionAppliedResult result)` → `{ "entityType": "Board", "entityId": "…", "createdTaskIds": [...], "createdLabelIds": [...], "warnings": [...], "appliedAt": "…" }`.
- [x] `public static string MergeUndoWarnings(string? appliedSnapshot, IReadOnlyList<string> warnings)` → thêm khoá `undoWarnings` vào snapshot cũ trên `JsonElement` (không nối chuỗi).
- [x] `public static JsonElement? ParseJson` / `CountTasks` / `ReadGuidArray` / `BuildResponse` / `BuildDetailResponse` — phòng thủ (JSON hỏng ⇒ `null`/`0`, không ném).
- [x] Các hàm trên để `public static` (đúng pattern `SmartSetupService.BuildProposal/NormalizeTasks`) để verify logic chuẩn hoá mà không cần dựng API — và làm điểm tựa cho test backend ở Giai đoạn 7.
- [x] Khi đọc snapshot từ DB để so sánh/merge (vd. merge `Warnings` vào `applied_snapshot` ở §2.3 `UndoAsync`): **parse `JsonElement` rồi so sánh ngữ nghĩa**, không so sánh chuỗi — `jsonb` đã bị PostgreSQL chuẩn hoá thứ tự key/khoảng trắng (ghi chú §1.2).

### 2.6 Đăng ký DI (`AiModule.cs`)

- [x] `services.AddScoped<IAiActionService, AiActionService>();`
- [x] `services.AddScoped<IAiActionApplier, CreateSubtasksApplier>();`
- [x] `MapAiModuleEndpoints()` → thêm `endpoints.MapAiActionEndpoints();` (giữ `endpoints.MapSmartSetupEndpoints();`) — **hoàn tất ở §3.2**.
- [x] `Program.cs` **không phải sửa** (extension point đã có từ Giai đoạn 3).
- [x] **Verify §2:** `dotnet build TeamNexus.sln` → **0 warning / 0 error**; harness tạm ngoài workspace với DI thật (`AddTeamNexusPersistence` + `AddBoardModule` + `AddAiModule`; `DeepSeek` trống ⇒ `FakeAiProvider`, không gọi mạng) → **83/83 check PASS**: 25 check hàm thuần (validate 20 case biên, `BuildBasis`, cap `Summary`, snapshots, `MergeUndoWarnings`, `CountTasks`/`ParseJson`, `BuildResponse`/`BuildDetailResponse`) + 58 check tích hợp DB (fixtures tự tạo rồi hard-delete, DB về baseline đúng số row ban đầu): request chỉ ghi log · approve qua service Board · 409 khi lặp · undo/soft-delete/dọn junction + nhãn · reject + note cap 500 · rollback khi cột đích bị xoá giữa chừng (log vẫn `Pending`, 0 task) · 403/404 · list sort/filter/clamp. Harness đã xoá.

> **Ghi chú hiện thực §2 (khác biệt nhỏ so với spec, có chủ ý):**
> - **`UndoAsync` trả `IReadOnlyList<string>` (warnings)** thay vì `Task` như bản nháp §2.2 — service cần warnings để merge vào `applied_snapshot`; đã cập nhật lại đúng chữ ký ở §2.2.
> - **CAS bằng `ExecuteUpdateAsync`; không bao giờ track entity log** (đọc `AsNoTracking`): nếu vừa `ExecuteUpdate` vừa `SaveChanges` trên entity tracked thì `SaveChanges` sẽ ghi đè `status` cũ trở lại. Mọi thay đổi row log (CAS + `applied_snapshot` + `decided_*` + `decision_note`) đi qua `ExecuteUpdateAsync`.
> - **`DTOs/AiActionDtos.cs` tạo ngay ở §2** (như tiền lệ Phase 3 §3.2) vì service cần kiểu; §3.1 chỉ tiêu thụ, không tạo lại (`RejectAiActionRequest` cũng có sẵn cho §3).
> - **Bắt buộc `IgnoreQueryFilters()`** ở Undo khi query/đếm `task_labels`: filter của `TaskLabelConfiguration` ẩn junction của task đã soft-delete, nếu không dùng sẽ tưởng nhãn "không còn tham chiếu" hoặc để lại row mồ côi.
> - **`ApplyAsync` dùng `ITaskService.DeleteTaskAsync`** (không tự set `DeletedAt`) ⇒ giữ nguyên broadcast `TaskDeleted`; nhờ vậy applier không cần `IBoardEventPublisher`.
> - `Board` trong `AiActionService` phải dùng alias `BoardEntity` (namespace `TeamNexus.Modules.Board` che type) — đúng pattern `TaskService`/`ColumnService`.
> - `Summary` dài **không** gây 400 mà bị cap 2000 khi lưu (khác câu chữ §2.5 bản nháp); validate chỉ áp cho dữ liệu nghiệp vụ.
> - Cột đích `IsDone = true` ⇒ `CreateTaskAsync` set `completed_at` (giữ nguyên hành vi Board, chưa chặn trong §2).

---

## 3. Backend – Endpoints & DTO

### 3.1 DTO (`DTOs/AiActionDtos.cs`)

- [x] ```csharp
  public sealed record ConfirmSmartSetupRequest(
      string Description,
      string? Summary,
      IReadOnlyList<SmartSetupTaskProposal> Tasks,
      Guid? ColumnId);

  public sealed record AiActionLogResponse(
      Guid Id, string Action, string EntityType, Guid? EntityId, string Status,
      Guid RequestedByUserId, string? RequestedByName,
      Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset? DecidedAt,
      string? DecisionNote, int TaskCount,
      DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

  public sealed record AiActionLogDetailResponse(
      Guid Id, string Action, string EntityType, Guid? EntityId, string Status,
      Guid RequestedByUserId, string? RequestedByName,
      Guid? DecidedByUserId, string? DecidedByName, DateTimeOffset? DecidedAt,
      string? DecisionNote,
      JsonElement? Basis, JsonElement? BeforeSnapshot, JsonElement? AfterSnapshot, JsonElement? AppliedSnapshot,
      IReadOnlyList<Guid> CreatedTaskIds,
      DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

  public sealed record RejectAiActionRequest(string? Note);
  ```
  *Đã tạo ở §2 (service cần kiểu trước khi có endpoint — đúng tiền lệ Phase 3 §3.2); §3 chỉ tiêu thụ. Cả 4 snapshot để `JsonElement?` (parse phòng thủ, JSON hỏng ⇒ `null` thay vì 500).*
- [x] `Status` serialize **camelCase** (`"Pending"` — enum đóng nên giữ nguyên casing canonical như `TaskPriority`, không đổi sang lowercase) — verify: `"status":"Pending"` trong response thật.

### 3.2 `Endpoints/AiActionEndpoints.cs`

- [x] Route group 1 (dùng lại prefix Smart Setup, cạnh endpoint generate):
  `POST /api/boards/{boardId:guid}/smart-setup/confirm` → `201` + `AiActionLogResponse` (`Location: /api/ai-actions/{id}`) — verify thật: 201 + `Location: /api/ai-actions/df7a2abb-…`; `POST /smart-setup` (generate) vẫn 200 ⇒ 2 route cùng prefix không ambiguous.
- [x] Route group 2: `/api/boards/{boardId:guid}/ai-actions`
  - `GET /?status=Pending&take=20` → `200 AiActionLogResponse[]` — verify: 200 + 1 phần tử; `?status=pending` (chữ thường) 200; `?status=Bogus` **400** `{ error }`; `?status=2` **400** (parse theo tên, không nhận số); `?take=abc` **400** (binding của framework — ProblemDetails, xem ghi chú dưới).
- [x] Route group 3: `/api/ai-actions/{logId:guid}`
  - `GET /` → `200 AiActionLogDetailResponse` (verify: `basis`/`afterSnapshot` object, `appliedSnapshot: null`, `createdTaskIds: []`)
  - `POST /{logId}/approve` → `200 AiActionLogDetailResponse` (Approved) — verify: `createdTaskIds` 3 phần tử + `decidedByName`
  - `POST /{logId}/reject` → `200 AiActionLogDetailResponse` (Rejected), body `RejectAiActionRequest` — verify: có note ⇒ `decisionNote` đúng; **không body** vẫn 200 (`decisionNote: null`) nhờ bind `RejectAiActionRequest?`
  - `POST /{logId}/undo` → `200 AiActionLogDetailResponse` (Undone)
- [x] Mọi group: `.WithTags("AiActions")` + `.AddEndpointFilter<DomainExceptionFilter>()`; mọi route `.RequireAuthorization()`; mọi `POST` thêm `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` (header `X-XSRF-TOKEN` do `httpClient` tự gắn) — verify: POST thiếu CSRF → **403** `{ error: "CSRF token missing or invalid…" }`; POST không auth → **401** (authorization middleware chạy trước filter).
- [x] Đọc `userId` từ claim `NameIdentifier` qua **một helper private dùng chung** trong module Ai (`AiEndpointHelpers.RequireUserId(HttpContext)`), giữ nguyên pattern `SmartSetupEndpoints` (`CurrentUser` của Board là `internal` — **không** sửa module Board, **không** đổi `CurrentUser` thành public trong phạm vi Giai đoạn 4) — `SmartSetupEndpoints` đã refactor sang helper này (bỏ parse claim inline + `using System.Security.Claims`).
- [x] `SmartSetupEndpoints` chỉ còn `POST /` (generate). Confirm nằm ở `AiActionEndpoints` để tách rõ "đề xuất (không ghi)" khỏi "ghi qua Accountability Layer".
- [x] `AiModule.MapAiModuleEndpoints()` gọi thêm `MapAiActionEndpoints()`; `Program.cs` **không** đổi.

### 3.3 Mã lỗi & contract cho UI (body lỗi luôn là `{ "error": "…" }`)

| Status | Khi nào | Gợi ý UI |
|---|---|---|
| 400 | description rỗng/> 4000; 0 task; > `MaxTaskCount`; title sai; priority sai; labels quá 5/quá dài; `labelId` khác workspace; assignee không phải member; `columnId` khác board; board 0 cột (lúc approve); `action` không có applier; `?status=` sai (parse theo tên) | `Alert` lỗi + giữ nguyên dữ liệu người dùng đã nhập |
| 401 | hết phiên | `httpClient` tự refresh; vẫn lỗi → điều hướng login |
| 403 | Member (không phải Manager/Admin) hoặc thiếu CSRF | `Alert` "Bạn cần quyền Manager/Admin trong workspace này" |
| 404 | board/log không tồn tại hoặc không thấy | `Alert` + đóng modal/drawer |
| 405 | gọi sai method (vd. `POST /api/ai-actions/{id}`) | lỗi lập trình — không hiển thị cho người dùng cuối |
| 409 | log không còn `Pending` (đã duyệt/từ chối/hoàn tác ở nơi khác) | `Alert` "Hành động này đã được xử lý ở nơi khác" + `reload()` danh sách |
| 502 | (chỉ ở endpoint generate Phase 3) | giữ nguyên xử lý cũ |

> **Ghi chú hiện thực §3 (khác biệt nhỏ so với spec, có chủ ý):**
> - `status` bind dạng `string?` rồi parse **strict theo tên** (`Enum.GetNames`) thay vì bind enum trực tiếp: giữ đúng contract `{ error }` cho lỗi nghiệp vụ và từ chối cả giá trị số (`?status=2` → 400).
> - `take` bind `int?` → `take ?? 0`; service lo default 20 + clamp 1..100. **`?take=abc` trả 400 dạng ProblemDetails của framework** (kèm bảng lỗi ở trên) — khác `{ error }`; client chỉ cần xử lý theo status 400.
> - `RejectAiActionRequest?` **nullable** để POST không body = từ chối không lý do (200), không bị 400 do binding.
> - `GET` **không** gắn CSRF filter; chỉ POST mới cần `X-XSRF-TOKEN`.
> - Tag `AiActions` cho cả 3 group (kể cả confirm nằm dưới prefix `smart-setup`) để Scalar gom chung lớp Accountability; endpoint generate Phase 3 vẫn tag `SmartSetup`.
> - Verify §3: build 0 warning/0 error + **36/36 check PASS** bằng probe HTTP thật (API chạy nền cổng 5195 ở `Development` + `DeepSeek__ApiKey` rỗng ⇒ `FakeAiProvider`, **không gọi AI, không tốn token**; JWT tự ký qua `JwtService` đặt trong cookie `access_token`; CSRF lấy từ cookie `XSRF-TOKEN` sau `GET /api/auth/antiforgery`), fixture tự tạo rồi hard-delete, `ai_action_logs` về đúng baseline, API đã stop (cổng 5195 đóng), harness đã xoá.

---

## 4. Frontend – `src/features/ai/`

> ## 🔻 BÀN GIAO §4 + §5.2 CHO ANTIGRAVITY (backend đã xong 100%, verify 54/54)
>
> Backend Giai đoạn 4 (schema + `AiActionService` + endpoint) **đã hoàn tất và verify end-to-end**: 54/54 check PASS với API thật + PostgreSQL thật + **SignalR client thật** (xem §5.1 và `Project-Documents/report/phase-4-accountability-test-report.md`). Việc còn lại **chỉ là frontend §4 + test §5.2** — backend sẽ không đổi nữa, cứ code theo đúng contract dưới đây.
>
> ### 1) Sáu endpoint (base `/api`, dùng `frontend/src/shared/api/httpClient.ts`)
> | # | Method + path | Body | Thành công | Ghi chú |
> |---|---|---|---|---|
> | 1 | `POST /boards/{boardId}/smart-setup/confirm` | `ConfirmSmartSetupRequest` | **201** `AiActionLog` + header `Location: /api/ai-actions/{id}` | Tạo log `Pending` — **chưa** ghi task nào |
> | 2 | `GET /boards/{boardId}/ai-actions?status=&take=` | — | **200** `AiActionLog[]` | `status` ∈ `Pending\|Approved\|Rejected\|Undone` (nhận mọi casing, **không** nhận số); `take` 1–100, mặc định 20, `take=0`/thiếu ⇒ 20; sort `createdAt DESC` |
> | 3 | `GET /ai-actions/{logId}` | — | **200** `AiActionLogDetail` | Kèm `basis`/`beforeSnapshot`/`afterSnapshot`/`appliedSnapshot` + `createdTaskIds` |
> | 4 | `POST /ai-actions/{logId}/approve` | — | **200** detail (`Approved`) | Chỉ khi đang `Pending`; else **409** |
> | 5 | `POST /ai-actions/{logId}/reject` | `{ note?: string }` **(body tuỳ chọn)** | **200** detail (`Rejected`) | Gọi **không body** vẫn 200 (`decisionNote: null`); `note` bị cap 500 ở server |
> | 6 | `POST /ai-actions/{logId}/undo` | — | **200** detail (`Undone`) | Chỉ khi đang `Approved`; else **409** |
>
> `httpClient` **tự** gắn `X-XSRF-TOKEN` cho POST ⇒ **không** tự set header, không tự gọi `/auth/antiforgery` (đúng như §5 Phase 3).
>
> ### 2) Kiểu TS cần mirror (viết tay theo `src/Modules/Ai/TeamNexus.Modules.Ai/DTOs/AiActionDtos.cs`)
> ```ts
> export type AiActionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Undone'
> export type AiActionType = 'CreateSubtasks'
>
> export interface ConfirmSmartSetupRequest {
>   description: string
>   summary: string | null
>   tasks: SmartSetupTaskProposal[]   // tái dùng type có sẵn ở features/ai/types/smartSetup.types.ts (bỏ tempId)
>   columnId: string | null
> }
> export interface AiActionLog {
>   id: string; action: AiActionType | string; entityType: 'Board' | string; entityId: string | null
>   status: AiActionStatus
>   requestedByUserId: string; requestedByName: string | null
>   decidedByUserId: string | null; decidedByName: string | null; decidedAt: string | null
>   decisionNote: string | null
>   taskCount: number
>   createdAt: string; updatedAt: string
> }
> export interface AiActionLogDetail extends AiActionLog {
>   basis: unknown | null
>   beforeSnapshot: unknown | null
>   afterSnapshot: {
>     description: string; summary: string | null; columnId: string | null
>     tasks: SmartSetupTaskProposal[]
>   } | null
>   appliedSnapshot: {
>     entityType: string; entityId: string | null
>     createdTaskIds: string[]; createdLabelIds: string[]
>     warnings: string[]
>     appliedAt: string
>     undoWarnings?: string[]        // CHỈ xuất hiện khi Undo có cảnh báo
>   } | null
>   createdTaskIds: string[]         // [] khi chưa duyệt
> }
> ```
>
> ### 3) JSON thật (rút gọn) từ lần verify §5.1 — dùng làm fixture test
> `POST confirm` → 201:
> ```json
> {"id":"df7a2abb-…","action":"CreateSubtasks","entityType":"Board","entityId":"0f58d6b7-…",
>  "status":"Pending","requestedByUserId":"01a08554-…","requestedByName":"Thinh-TT",
>  "decidedByUserId":null,"decidedByName":null,"decidedAt":null,"decisionNote":null,
>  "taskCount":3,"createdAt":"2026-09-10T08:01:23.968555+00:00","updatedAt":"2026-09-10T08:01:23.968555+00:00"}
> ```
> `GET /ai-actions/{id}` (sau khi approve) — các phần quan trọng:
> ```json
> {"status":"Approved",
>  "basis":{"boardId":"…","boardName":"P4S3 board 1","workspaceId":"…","descriptionLength":63,
>           "descriptionExcerpt":"Xây dựng tính năng bình luận cho thẻ kanban (Phase 4 §3 verify)",
>           "memberCount":1,"columnCount":2,"taskCount":3,"requestedAt":"…"},
>  "beforeSnapshot":null,
>  "afterSnapshot":{"description":"…","summary":"3 sub-task","columnId":"…","tasks":[
>      {"title":"Thiết kế schema bình luận","description":"Bảng comments","priority":"High",
>       "labels":[{"labelId":"7d65c629-…","name":"backend","exists":true}],
>       "assignee":{"userId":"01a08554-…","displayName":"Thinh-TT","matched":true}}]},
>  "appliedSnapshot":{"entityType":"Board","entityId":"0f58d6b7-…",
>      "createdTaskIds":["f6211917-…","7e8e068e-…","3007e29e-…"],
>      "createdLabelIds":["0113ad0b-…"],"warnings":[],"appliedAt":"…"},
>  "createdTaskIds":["f6211917-…","7e8e068e-…","3007e29e-…"]}
> ```
> Khi `undo` và nhãn do action tạo vẫn còn task khác dùng: `appliedSnapshot.undoWarnings = ["Label <id> is still used by other tasks and was kept."]`. Khi assignee rời workspace: `appliedSnapshot.warnings = ["Assignee 'X' is no longer a workspace member; the task was created unassigned."]`.
>
> ### 4) Body lỗi (luôn `{ "error": "…" }` — trừ 400 do framework, xem ghi chú)
> | Status | `error` thật | UI nên làm |
> |---|---|---|
> | 400 | `"Description must be 1–4000 characters."`, `"At least one sub-task is required."`, `"A maximum of 20 sub-tasks can be applied in one AI action (received 21)."`, `"Sub-task title must be 1–200 characters."`, `"Priority must be one of: Low, Medium, High, Urgent."`, `"Label 'x' does not belong to this workspace."`, `"Assignee is not a member of this workspace."`, `"Target column does not belong to this board."`, `"Unknown status 'X'. Expected one of: …"` | `Alert` lỗi, **giữ nguyên** dữ liệu người dùng đã sửa trong modal |
> | 401 | — (middleware) | `httpClient` tự refresh; vẫn lỗi → điều hướng login |
> | 403 | `"Requires Manager or Admin role in this workspace."` / `"CSRF token missing or invalid (send X-XSRF-TOKEN header)."` | `Alert` "Bạn cần quyền Manager/Admin trong workspace này" |
> | 404 | `"Board not found."` / `"AI action not found."` | `Alert` + đóng modal/drawer + `reload()` |
> | 409 | `"This AI action is no longer Pending (it was already decided elsewhere)."` / `"Only an Approved AI action can be undone."` | `Alert` "Hành động này đã được xử lý ở nơi khác" + `reload()` |
> | 400 (framework) | body là **ProblemDetails** (không có `error`), ví dụ `?take=abc` | chỉ cần xử lý theo status 400; đừng kỳ vọng `error` |
>
> ### 5) Điểm cần chú ý khi code UI (đã verify, đừng code sai kỳ vọng)
> - **`confirm` KHÔNG ghi task** — sau confirm, bảng Kanban chưa đổi. Task chỉ xuất hiện sau `approve` (và đã tự realtime qua SignalR `TaskCreated` → `useBoardHub`/`applyTaskCreated`).
> - **Nhãn mới KHÔNG có SignalR event** ⇒ sau `approve`/`undo` thành công hãy gọi `refetch()` (từ `useBoard`) để đồng bộ `workspaceLabels`.
> - **Cột `isDone = true`**: task tạo vào cột Done sẽ có `completedAt` ngay ⇒ nên mặc định `Select` cột đích là **cột đầu tiên không phải Done** (hoặc cảnh báo khi chọn cột Done).
> - Task do action tạo được **nối cuối cột** (`position = max + 1`), không chèn vào giữa.
> - `afterSnapshot.appliedSnapshot` là `null` cho tới khi duyệt; `createdTaskIds` là `[]` khi chưa duyệt; `beforeSnapshot` luôn `null` với `CreateSubtasks`.
> - `appliedSnapshot.undoWarnings` **có thể vắng mặt** — UI phải đọc optional (`?.length`).
> - **Không so sánh snapshot bằng string** (PostgreSQL chuẩn hoá `jsonb`: đổi thứ tự key/khoảng trắng) — chỉ render pretty JSON.
> - `take` ngoài 1..100 bị clamp (không lỗi); `status` phải đúng **tên** (`Pending`…), gửi số (`2`) sẽ 400.
> - Reject không cần lý do (body rỗng vẫn 200); note > 500 bị cắt (không lỗi).
> - Modal Phase 3 hiện có text "sẽ được áp dụng qua Accountability Layer ở Giai đoạn 4" → **phải xoá**, thay bằng luồng thật (bước 3 trong §4.2).
>
> ### 6) Hướng dẫn §5.2 (test) — theo đúng pattern đang có
> - Mock `httpClient` (axios instance) giống `src/features/board/services/__tests__/boardApi.test.ts`, **không** dựng backend: assert method/URL/body cho 6 hàm (`/boards/{id}/smart-setup/confirm`, `/boards/{id}/ai-actions?status=…&take=…`, `/ai-actions/{id}`, `/ai-actions/{id}/approve|reject|undo`).
> - `useAiActions`: `idle → loading → idle`; `approve` cập nhật phần tử trong `logs`; **409 ⇒ `error` + gọi lại list**; `pendingCount` = số `Pending`.
> - `AiActionHistoryDrawer`/`AiActionLogItem`: render đủ 4 trạng thái; nút chỉ hiện đúng trạng thái (`Pending` → Duyệt/Từ chối; `Approved` → Hoàn tác; `Rejected`/`Undone` → không nút); `Empty` khi rỗng; hiển thị `decisionNote`.
> - `useSmartSetup`/`SmartSetupModal`: `submitConfirm` **có** gọi API; chuỗi `generating → ready → submitting → pending`; bước 3 hiện `log id` + `Pending`; nút Duyệt gọi `approveAiAction`; nút Hoàn tác gọi `undoAiAction`; **không còn** chuỗi "Giai đoạn 4"; nút Xác nhận **disable** khi `submitting` (chống double-submit).
> - `BoardView.test.tsx`: nút "Lịch sử AI" tồn tại, mở drawer, `Badge` hiển thị số pending.
> - DoD: `npm run lint` (oxlint 0 warn/0 err) + `npx tsc -b` + `npm run build` + `npm test` **sạch** (§4.4).
>
> ### 7) Sau khi xong §4 + §5.2
> - Tick các checkbox §4.1–§4.4, §5.2 và 2 dòng "Việc của antigravity" ở cuối file.
> - Bổ sung ảnh chụp (drawer + luồng duyệt) vào `Project-Documents/report/phase-4-accountability-test-report.md` (mục "Phần UI — bổ sung sau §4").
> - Xoá câu "Còn §5.2 (UI gọi API thật) do antigravity" ở §5.3 sau khi test xong.

### 4.1 Types, API, hook

- [x] `types/aiAction.types.ts`: mirror DTO §3.1 (camelCase) + `export type AiActionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Undone'` + `AiActionType = 'CreateSubtasks'`.
- [x] `services/aiActionApi.ts` (qua `httpClient` base `/api`; **không** tự set `X-XSRF-TOKEN`):
  - `confirmSmartSetup(boardId, payload: ConfirmSmartSetupRequest): Promise<AiActionLog>`
  - `listAiActions(boardId, params?: { status?: AiActionStatus; take?: number }): Promise<AiActionLog[]>`
  - `getAiAction(logId): Promise<AiActionLogDetail>`
  - `approveAiAction(logId): Promise<AiActionLogDetail>`
  - `rejectAiAction(logId, note?: string): Promise<AiActionLogDetail>`
  - `undoAiAction(logId): Promise<AiActionLogDetail>`
- [x] `hooks/useAiActions.ts`: state `{ logs, pendingCount, status: 'idle'|'loading'|'approving'|'rejecting'|'undoing', error, httpStatus }`; `reload(status?)`; `approve/reject/undo` (cập nhật log trong list theo response, set `error` + map status như bảng §3.3, đặc biệt **409 → reload list**); sau `approve`/`undo` thành công gọi callback `onBoardChanged?.()` để board refetch.

### 4.2 `SmartSetupModal` – chuyển "Xác nhận" sang luồng Pending thật

- [x] `useSmartSetup`: bỏ `confirm()` đồng bộ. Thêm `submitConfirm(boardId, columnId?)` **async**; status mới `'submitting'` và `'pending'`; thêm field `actionLog: AiActionLogDetail | null`; `reset()` xoá `actionLog`.
  - `submitConfirm` gọi `aiActionApi.confirmSmartSetup` với `{ description, summary, tasks (đã bỏ `tempId`), columnId }`; giữ `tasks` trong state để UI hiển thị đúng cái vừa gửi.
- [x] `SmartSetupModal` bước 2: nút **"Xác nhận đề xuất (n)"** → `loading={status === 'submitting'}`, **disable** khi đang gửi (không double-submit). Thêm `Select` cột đích (mặc định cột đầu tiên) truyền vào `submitConfirm` — cột lấy từ prop `columns` (đã có sẵn ở `BoardView`).
- [x] Bước 3 (`status === 'pending' || 'confirmed'`): **xoá hẳn** text "sẽ được áp dụng qua Accountability Layer ở Giai đoạn 4" và thay bằng:
  - `Result`/`Alert`: "Đã tạo yêu cầu chờ duyệt" + `log id` + thời điểm + `Tag` trạng thái `Pending` (gold).
  - Nút **"Duyệt & áp dụng"** → `aiActionApi.approveAiAction(logId)`; thành công → `Result.success` "Đã áp dụng n sub-tasks vào bảng" + gọi `onApplied?.()`.
  - Nút **"Từ chối"** → `Input.TextArea` lý do (≤ 500, tuỳ chọn) + `rejectAiAction`.
  - Nút **"Hoàn tác"** (chỉ hiện sau khi đã duyệt) → `undoAiAction` + thông báo số task đã gỡ.
  - Lỗi 409 → `Alert` "Hành động đã được xử lý ở nơi khác" + hiển thị trạng thái mới lấy từ response/lỗi.
- [x] Prop mới của modal: `columns?: ColumnResponse[]`, `onApplied?: () => void`.

### 4.3 `AiActionHistoryDrawer` + badge ở BoardView

- [x] `components/AiActionHistoryDrawer.tsx`: `Drawer` Ant Design mở từ top bar BoardView; gọi `useAiActions.reload()` khi mở + sau mỗi thao tác.
  - `ListView` các `AiActionLogItem`: `Tag` trạng thái (Pending = gold, Approved = green, Rejected = red, Undone = default), `action`, tên người yêu cầu/người quyết định, thời gian, `taskCount`, `decisionNote`.
  - Filter theo status (Segmented: Tất cả / Chờ duyệt / Đã duyệt / Từ chối / Đã hoàn tác) + `Empty` + `Spin` + `Alert` lỗi.
- [x] `components/AiActionLogItem.tsx`: `Collapse` chi tiết — preview danh sách task trong `after_snapshot` (title/priority/assignee/labels), `basis` (JSON pretty), `applied_snapshot` khi có, `before_snapshot` khi có; nút hành động **Duyệt / Từ chối** (Pending) và **Hoàn tác** (Approved) — cùng xử lý 409 như §4.2.
- [x] `BoardView.tsx`: thêm nút **"Lịch sử AI"** (icon `HistoryOutlined`) + `Badge count={pendingCount}` cạnh nút "AI Smart Setup"; giữ `AiActionHistoryDrawer` trong state `aiHistoryOpen`; truyền `columns` + `onApplied={() => refetch()}` xuống `SmartSetupModal`. (Real-time task mới đã tự cập nhật qua `useBoardHub` → `applyTaskCreated`; `refetch()` bổ sung để đồng bộ nhãn mới tạo.)
- [x] `features/ai/index.ts`: export thêm types/api/hook/components mới.

### 4.4 Verify §4

- [x] `npm run lint` (oxlint 0 warning/error) + `npx tsc -b` + `npm run build` + `npm test` — tất cả sạch.

---

## 5. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–3)

### 5.1 Verify backend (harness tạm ngoài workspace + API thật + PostgreSQL thật)

> Cùng cách Phase 3 §Verify §4: harness đặt ngoài workspace, tự ký JWT bằng `Jwt:SigningKey` trong User Secrets, **xoá sau khi chạy**, không commit.
>
> **Trạng thái: ✅ ĐÃ CHẠY — 54/54 check PASS** (`ALL CHECKS PASSED`, exit 0). API chạy nền cổng **5196** ở `Development` với `DeepSeek__ApiKey` rỗng ⇒ `FakeAiProvider` (**không gọi AI thật, không tốn token**); JWT tự ký qua `JwtService` trong cookie `access_token`; CSRF lấy từ cookie `XSRF-TOKEN`; **SignalR client thật** (`Microsoft.AspNetCore.SignalR.Client` 8.0.24) join group `board-{boardId}`. Fixture (gồm user thứ 2 + membership Manager) tự tạo rồi hard-delete; `ai_action_logs` về đúng baseline; API đã stop (cổng 5196 đóng); harness đã xoá. Báo cáo: `Project-Documents/report/phase-4-accountability-test-report.md`.

1. [x] Migration: `ai_action_logs` đủ cột/CHECK/index đúng thiết kế §1 — **A1–A4 PASS**: 15 cột đúng thứ tự, 4 cột `jsonb`, `ck_ai_action_logs_status`, 3 index (`entity_type/entity_id/created_at` + 2 FK).
2. [x] `POST .../smart-setup/confirm` (Manager, CSRF hợp lệ) → **201**, `status = "Pending"`, `ai_action_logs` +1 với `basis` + `after_snapshot` JSON hợp lệ (`jsonb_typeof(basis)='object'`, `jsonb_array_length(after_snapshot->'tasks')=3`); **`tasks`/`labels`/`task_labels` KHÔNG đổi** (1/1/1 trước ↔ sau) — **C13, C13b PASS**.
3. [x] `POST /ai-actions/{id}/approve` → **200** `Approved`; 3 task đúng `column_id` đích, `position ≥ 1` (nối cuối cột), priority `High` + description đúng; nhãn `exists=false` được tạo + gắn qua `task_labels` (`labels` 1→2, `links` 1→3); `assignee_id` đúng cho assignee còn là member; `applied_snapshot` có 3 `createdTaskIds` + 1 `createdLabelIds`; log có `decided_by_user_id` + `decided_at`; **SignalR client nhận đúng 3 `TaskCreated`** — **C14–C18 PASS**.
4. [x] `approve` lần 2 → **409**, `tasks` không tăng; `approve` sau `reject`/`undo` → 409 — **C19, C24, C27 PASS**.
5. [x] `reject` (kèm note) → **200** `Rejected` + `decision_note` + `decided_at`; `tasks`/`labels` không đổi; `reject` lần 2 → 409 — **C26, C27 PASS**.
6. [x] `undo` (sau approve) → **200** `Undone`; task do action tạo có `deleted_at` (active 4→1), `task_labels` của chúng bị xoá (links 3→2), nhãn do action tạo **giữ lại khi còn task khác dùng** + `undoWarnings` ≥ 1; nhãn không còn tham chiếu thì **bị xoá** (labels 3→2) và `undoWarnings = 0`; **SignalR nhận đúng 3 `TaskDeleted`**; `undo` lần 2 → 409 — **C21–C25 PASS**.
7. [x] Guards: không auth → **401**; thiếu CSRF → **403**; Member → **403** (confirm + list, dùng user thật ở workspace role `Member`); board **404**; log **404**; title rỗng **400**; 21 task (> `MaxTaskCount`) **400**; `labelId` khác workspace **400**; assignee không phải member **400**; `columnId` khác board **400**; board 0 cột khi approve **400** + log **vẫn `Pending`**, 0 task — **C2–C12, C30 PASS**. Ngoài ra: **C29** — assignee rời workspace sau confirm ⇒ task tạo **không gán** + `warnings` ≥ 1 (không fail cả action).
8. [x] `GET /api/boards/{boardId}/ai-actions` → 4 log sort `created_at DESC`; `?status=Pending` (board 3) → 1; `?status=Rejected` → 1; `take=1000` clamp, `take=0` về default; `taskCount` = 3 khớp `after_snapshot`; `GET /api/ai-actions/{id}` trả đủ 4 JSON + `createdTaskIds` — **C31–C36 PASS**.
9. [x] Harness hàm thuần (không cần HTTP/DB): `ValidateConfirmRequest` (description 0/4000/4001; tasks 0/20/21; title 0/200/201; priority `"urgent"`/`"Critical"`; 6 nhãn; nhãn 81 ký tự; `labelId` ngoài workspace; assignee ngoài member; `columnId` ngoài board; task `null`); `BuildBasis` (excerpt ≤ 300); `BuildAppliedSnapshotJson` round-trip + `MergeUndoWarnings` — **B1–B9 PASS**.
10. [x] Kiểm tra kiến trúc "cổng duy nhất": `_db.(Tasks|Labels|TaskLabels).(Add|AddRange|Remove|RemoveRange)` trong `src/Modules/Ai` → **chỉ 1 match**: `_db.TaskLabels.RemoveRange(links)` trong `Services/Appliers/CreateSubtasksApplier.cs`; đường ghi log duy nhất là `_db.AiActionLogs.Add(log)` trong `AiActionService` (đúng thiết kế).
11. [x] *(Tuỳ chọn)* `Project-Documents/report/phase-4-accountability-test-report.md` — **đã tạo** (phần backend + frontend).

### 5.2 Frontend (Vitest + Testing Library)

- [x] Test `aiActionApi`: đúng method/URL/body cho `confirmSmartSetup` (`/boards/{boardId}/smart-setup/confirm`), `listAiActions` (query `status`/`take`), `approve/reject/undo` (`/ai-actions/{id}/...`).
- [x] Test `useAiActions`: `idle → loading → idle`; `approve` cập nhật log trong list; **409 → set error + reload**; `pendingCount` đúng.
- [x] Test `AiActionHistoryDrawer` + `AiActionLogItem`: render theo 4 status, nút chỉ hiện đúng trạng thái, gọi đúng api, hiển thị `decisionNote`, `Empty` khi rỗng.
- [x] Cập nhật `useSmartSetup.test.ts`: `submitConfirm` gọi API và chuyển `generating → ready → submitting → pending`; lỗi 400/403/409 map đúng message.
- [x] Cập nhật `SmartSetupModal.test.tsx`: nút "Xác nhận đề xuất" **có** gọi `aiActionApi.confirmSmartSetup`; bước 3 hiển thị `log id` + `Pending`; nút "Duyệt & áp dụng" gọi `approveAiAction`; nút "Hoàn tác" gọi `undoAiAction`; **không còn** text "Giai đoạn 4".
- [x] `BoardView.test.tsx`: nút "Lịch sử AI" tồn tại + mở drawer; `Badge` hiển thị số pending.

### 5.3 Điều kiện hoàn thành Giai đoạn 4 (map `03-roadmap.md`)

- [x] Bảng `AiActionLog` lưu `action`, `basis` (tóm tắt input), timestamp, actor, trạng thái — verify ở §5.1 bước 1–3, 8 (A1–A4, C13/C13b, C14, C31–C36).
- [x] `AiActionService` là điểm **duy nhất** mọi hành động AI đi qua trước khi ghi dữ liệu thật — verify ở §5.1 bước 2 (confirm không ghi) + bước 10 (không còn đường ghi khác).
- [x] Smart Setup (Giai đoạn 3) đã chuyển sang luồng `Pending → Approve/Reject` qua service này — verify backend ở §5.1 bước 2–5 + C26/C27, C30 (rollback giữ `Pending`) và verify frontend ở §5.2.
- [x] Undo hoạt động cho ít nhất 1 loại hành động AI (`CreateSubtasks`) — verify ở §5.1 bước 6 (C21–C25, gồm cả `TaskDeleted` realtime) và verify frontend ở §5.2.

---

## 6. Edge cases, failure modes & bàn giao Giai đoạn 5

**Edge cases / failure modes:**
- **Double-approve / đua 2 manager** → CAS `WHERE status='Pending'` chỉ cho 1 request thắng, request kia **409**; không tạo task trùng.
- **Apply lỗi giữa chừng** (ví dụ task thứ 3 vi phạm validate Board) → rollback cả transaction (kể cả CAS) → log **vẫn `Pending`**, không partial-write, client retry được.
- **Board bị soft-delete sau khi confirm** → approve trả **404**, log giữ `Pending`.
- **Cột bị xoá sau khi confirm** → `columnId` không còn ⇒ server fallback cột đầu tiên còn lại; board hết cột ⇒ **400**.
- **Assignee rời workspace sau confirm** → task tạo **không gán** + `warnings[]` trong `applied_snapshot` (không fail cả action).
- **Nhãn vừa được tạo song song** (Conflict 409 ở `CreateLabelAsync`) → applier query lại theo tên và dùng id hiện có, không fail.
- **Task do action tạo đã bị người dùng xoá trước khi Undo** → bỏ qua, vẫn `Undone`; không broadcast `TaskDeleted` lần hai.
- **Nhãn của action đang được task khác dùng** → Undo **giữ nhãn** + warning (không phá dữ liệu người khác).
- **`action` không có applier** (log cũ/tương lai) → **400** rõ ràng, không im lặng.
- **`after_snapshot` hỏng JSON** (dữ liệu bất thường) → `ListAsync` trả `taskCount = 0`; approve/undo → 400/502-style error có message, không crash.
- **Note từ chối > 500** → cắt còn 500 (không 400) để không chặn thao tác.
- **Frontend 409** ở bất kỳ nút nào → `Alert` "đã được xử lý ở nơi khác" + reload list/log.
- **Token/chi phí:** `basis` chỉ chứa tóm tắt (độ dài + 300 ký tự đầu), không dump toàn văn mô tả; `after_snapshot` giới hạn tự nhiên bởi `MaxTaskCount` × độ dài field đã cap ở §2.5.

**Bàn giao Giai đoạn 5 (AI Observer):**
- Mọi loại hành động AI **ghi dữ liệu** mới (AssignMember, SetLabels, MoveTasks, …) **phải** thêm một `IAiActionApplier` + đăng ký DI, không được gọi `_db.Tasks/Labels` trực tiếp từ service khác.
- `ai_action_logs` là nguồn audit sẵn sàng cho Observer đọc (`action`, `status`, `decided_at`, `applied_snapshot`) — không cần bảng mới.
- `notifications` (Giai đoạn 5) **không** đi qua `AiActionService`: đó là lớp AI *đọc/cảnh báo*, không *ghi* dữ liệu nghiệp vụ.
- Nếu Giai đoạn 5 cần queue pending xuyên workspace ⇒ thêm cột `workspace_id` (hoặc join qua `boards`) — hiện tại chủ ý **không** có (D13).

---

## 7. Tài liệu

- [x] Tạo `Project-Documents/tasks/phase-4-accountability-layer.md` (checklist như trên).
- [x] Cập nhật `Project-Documents/04-database-design.md` §3.5 (2 cột mới + quy ước `entity_type`/`entity_id`) và §5 (index mới).
- [x] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`: bảng endpoint mới, vòng đời log, applier registry, mã 409, contract bàn giao Giai đoạn 5.
- [x] Cập nhật `README.md` root (mục "Trạng thái (Giai đoạn 4 – Accountability Layer)") và tick checklist `03-roadmap.md` khi hoàn tất.

---

## Checklist Hoàn thiện Giai đoạn 4

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 5 (AI Observer).

- [x] Bảng `ai_action_logs` (`AiActionLog`) lưu action, `basis` (tóm tắt input), timestamp, actor (`requested_by`/`decided_by`), trạng thái `Pending/Approved/Rejected/Undone` — migration `Phase4AccountabilityLayer` đã áp dụng
- [x] Service `AiActionService` là **điểm duy nhất** mọi hành động AI đi qua trước khi ghi dữ liệu thật (applier registry theo `action`; chỉ applier được chạm `Tasks`/`Labels`/`TaskLabels`)
- [x] Smart Setup (Giai đoạn 3) đã chuyển sang luồng `Pending → Approve/Reject` qua service này — `confirm` **không** ghi `tasks`/`labels`; chỉ `approve` mới ghi
- [x] Chức năng Undo hoạt động cho ít nhất 1 loại hành động AI (`CreateSubtasks`): soft-delete task đã tạo + dọn `task_labels` + nhãn không còn tham chiếu, có broadcast real-time

### Việc của antigravity (§4 + §5.2)

- [x] §4 frontend `src/features/ai/` (`types/aiAction.types.ts`, `services/aiActionApi.ts`, `hooks/useAiActions.ts`, `components/AiActionHistoryDrawer.tsx`, `components/AiActionLogItem.tsx`) + chuyển `SmartSetupModal`/`useSmartSetup` sang luồng Pending thật + nút "Lịch sử AI" ở `BoardView.tsx`
- [x] §5.2 test Vitest cho `aiActionApi` / `useAiActions` / `AiActionHistoryDrawer` / `AiActionLogItem` + cập nhật `useSmartSetup.test.ts`, `SmartSetupModal.test.tsx`, `BoardView.test.tsx`; chạy `oxlint` (0 warn/0 err) + `tsc -b` + `vite build` + `vitest run` sạch
- [x] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`, `README.md` root và tick `03-roadmap.md` (sau khi backend verify xong)

