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

- [ ] Tạo enum `AiActionStatus { Pending, Approved, Rejected, Undone }` (cùng file entity).
- [ ] Tạo class `AiActionLog : IAuditableEntity` với các field (khớp `04-database-design.md` §3.5 sau khi tinh chỉnh):

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

- [ ] `builder.ToTable("ai_action_logs", table => table.HasCheckConstraint("ck_ai_action_logs_status", "\"status\" IN ('Pending', 'Approved', 'Rejected', 'Undone')"));`
- [ ] `Action`/`EntityType` `HasMaxLength(64).IsRequired()`; `DecisionNote` `HasMaxLength(500)`.
- [ ] `Status` → `.HasConversion<string>().HasMaxLength(16)`.
- [ ] `Basis` (bắt buộc) + `BeforeSnapshot`/`AfterSnapshot`/`AppliedSnapshot` → `.HasColumnType("jsonb")`.
- [ ] FK `RequestedByUser` (required) và `DecidedByUser` (nullable) → `DeleteBehavior.Restrict` (04 §7: **không cascade vật lý**).
- [ ] Index: `(EntityType, EntityId, CreatedAt)` cho liệt kê theo board; `RequestedByUserId` theo 04 §5.
- [ ] **Không** đặt query filter (log là lịch sử bất biến, không soft delete).

### 1.3 DbContext

- [ ] `TeamNexusDbContext`: thêm `public DbSet<AiActionLog> AiActionLogs => Set<AiActionLog>();` (config tự được `ApplyConfigurationsFromAssembly` nhặt).

### 1.4 Migration

- [ ] `dotnet ef migrations add Phase4AccountabilityLayer --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api`
- [ ] `dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api`
- [ ] Cập nhật `Project-Documents/04-database-design.md`: §3.5 thêm 2 cột `applied_snapshot`, `decision_note` + ghi chú quy ước `entity_type='Board'`/`entity_id=boardId`; §5 thêm dòng index `ai_action_logs (entity_type, entity_id, created_at)`.
- [ ] **Verify §1:** `dotnet build TeamNexus.sln` → 0 warning/0 error; `\d ai_action_logs` đủ cột, CHECK `status`, 2 index; 4 migration cũ không bị sửa.

---

## 2. Backend – `AiActionService` (cổng duy nhất)

### 2.1 Hằng số (`Services/AiActionTypes.cs`)

- [ ] `public static class AiActionTypes { public const string CreateSubtasks = "CreateSubtasks"; }`
- [ ] `public static class AiEntityTypes { public const string Board = "Board"; }`

### 2.2 Applier abstraction (`Services/IAiActionApplier.cs`)

- [ ] Định nghĩa:
  ```csharp
  public interface IAiActionApplier
  {
      string ActionType { get; }   // khớp AiActionLog.Action
      Task<AiActionAppliedResult> ApplyAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default);
      Task UndoAsync(AiActionLog log, AiActionContext ctx, CancellationToken ct = default);
  }

  public sealed record AiActionContext(Guid BoardId, Guid WorkspaceId, Guid ActingUserId);

  public sealed record AiActionAppliedResult(
      string EntityType,
      Guid? EntityId,
      IReadOnlyList<Guid> CreatedTaskIds,
      IReadOnlyList<Guid> CreatedLabelIds,
      IReadOnlyList<string> Warnings);
  ```
- [ ] Lý do tách applier: `AiActionService` chỉ lo guard/vòng đời/transaction; phần "ghi dữ liệu thật" của từng loại action nằm ở applier — thêm loại mới = thêm 1 class + 1 dòng DI.

### 2.3 `Services/AiActionService.cs`

- [ ] Interface `IAiActionService`:
  ```csharp
  Task<AiActionLogResponse> RequestCreateSubtasksAsync(Guid boardId, ConfirmSmartSetupRequest request, Guid userId, CancellationToken ct = default);
  Task<IReadOnlyList<AiActionLogResponse>> ListAsync(Guid boardId, AiActionStatus? status, int take, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> GetAsync(Guid logId, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> ApproveAsync(Guid logId, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> RejectAsync(Guid logId, string? note, Guid userId, CancellationToken ct = default);
  Task<AiActionLogDetailResponse> UndoAsync(Guid logId, Guid userId, CancellationToken ct = default);
  ```
- [ ] `RequestCreateSubtasksAsync(boardId, request, userId, ct)`:
  1. Load board `AsNoTracking` → không thấy/soft-deleted → **404**.
  2. `IWorkspaceAccess.RequireManagerAsync(board.WorkspaceId, userId)` → Member **403**.
  3. `ValidateConfirmRequest(request, board, columns, members, labels, maxTaskCount)` → **400** (pure static, §2.5). **Chưa ghi gì vào DB.**
  4. Tạo `AiActionLog`: `Action = CreateSubtasks`, `EntityType = Board`, `EntityId = boardId`, `Status = Pending`, `Basis = BuildBasis(...)`, `AfterSnapshot = BuildAfterSnapshotJson(request)`, `RequestedByUserId = userId`.
  5. `SaveChangesAsync` **chỉ insert log** (không có `Tasks`/`Labels`/`TaskLabels` nào được add).
  6. Trả `AiActionLogResponse` (`status = Pending`).
- [ ] `ApproveAsync(logId, userId)`:
  1. Load log → **404**; `EntityType != Board || EntityId is null` → **400**.
  2. Resolve board + `RequireManagerAsync` → **403** (board đã xoá → **404**, log vẫn `Pending`).
  3. Resolve applier theo `log.Action` (`IEnumerable<IAiActionApplier>`, so khớp kiểu `OrdinalIgnoreCase`); không có → **400** `"Unsupported AI action: …"`.
  4. `await using var tx = BeginTransactionAsync(ct);`
  5. **CAS**: `UPDATE ai_action_logs SET status='Approved', decided_by_user_id=@uid, decided_at=@now WHERE id=@id AND status='Pending'` → 0 row ⇒ **409** `"Action is not Pending (already decided)."` (rollback).
  6. `applier.ApplyAsync(log, ctx, ct)` → ghi `applied_snapshot` từ `AiActionAppliedResult` (`EntityType`, `EntityId`, `CreatedTaskIds`, `CreatedLabelIds`, `Warnings`) → `SaveChangesAsync` → `CommitAsync`.
  7. Lỗi bất kỳ ở bước 6 ⇒ rollback toàn bộ (kể cả CAS) ⇒ log **vẫn `Pending`**, không partial-write; lỗi domain của Board (`BoardModuleException`) được ném nguyên vẹn để `DomainExceptionFilter` map status.
  8. Trả `AiActionLogDetailResponse` (status `Approved`, kèm `createdTaskIds`).
- [ ] `RejectAsync(logId, note, userId)`: guard như Approve bước 1–2; **không** gọi applier; CAS `Pending → Rejected` + `decision_note` (trim, cap 500) + `decided_by/decided_at`; 0 row → **409**.
- [ ] `UndoAsync(logId, userId)`: guard bước 1–3 (applier phải resolve được); transaction + CAS `Approved → Undone` + `decision_note` (nếu có) + `decided_by/decided_at` → `applier.UndoAsync(...)` → nếu `Warnings` mới phát sinh thì merge vào `applied_snapshot` → commit; 0 row → **409**. `Pending`/`Rejected` → 409.
- [ ] `ListAsync(boardId, status?, take, userId)`: board → 404; `RequireManagerAsync` → 403; `take` mặc định 20, clamp 1–100; lọc `EntityType == Board && EntityId == boardId` (+ `Status` nếu có); sort `CreatedAt DESC`; join `users` để lấy `RequestedByName`/`DecidedByName`; `TaskCount` = số phần tử `tasks` trong `after_snapshot` (parse `JsonDocument`, lỗi parse ⇒ 0, không ném).
- [ ] `GetAsync(logId, userId)`: log → 404; dùng `EntityId` để `RequireManagerAsync` → 403; trả detail kèm `Basis`/`BeforeSnapshot`/`AfterSnapshot`/`AppliedSnapshot` dạng `JsonElement` + `CreatedTaskIds`.
- [ ] Log `Information` khi request/approve/reject/undo (log id, action, status, số task) — **không** log nội dung mô tả dài (chỉ độ dài/300 ký tự đầu trong `basis`).

### 2.4 `CreateSubtasksApplier` (`Services/Appliers/CreateSubtasksApplier.cs`)

- [ ] `ApplyAsync(log, ctx, ct)`:
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
- [ ] `UndoAsync(log, ctx, ct)`:
  1. Parse `applied_snapshot` → `CreatedTaskIds` + `CreatedLabelIds`; rỗng ⇒ return (không lỗi).
  2. Task còn tồn tại: query `_db.Tasks.IgnoreQueryFilters().Where(t => taskIds.Contains(t.Id) && t.DeletedAt == null)`; soft-delete (`DeletedAt = UtcNow`) → `SaveChangesAsync` → `IBoardEventPublisher.TaskDeleted(boardId, taskId, ct)` cho **từng** task vừa xoá (task đã bị người dùng xoá trước đó ⇒ bỏ qua, không broadcast lại).
  3. Xoá `task_labels` của các task đó (`_db.TaskLabels.Where(tl => taskIds.Contains(tl.TaskId))` → `RemoveRange`) — tránh nhãn bị "khoá" bởi task đã ẩn.
  4. Với mỗi `createdLabelId`: nếu **không còn** `task_labels` nào tham chiếu (`IgnoreQueryFilters`, sau bước 3) ⇒ `_db.Labels.Remove(label)`; ngược lại **giữ nhãn** + thêm `Warning`.
  5. `SaveChangesAsync`; ghi `Warnings` mới vào `applied_snapshot` (merge) khi có.
- [ ] ⚠️ Applier là **nơi duy nhất** trong module Ai được add/remove `Tasks`/`Labels`/`TaskLabels` (kiểm tra ở §5.1 bước 10).

### 2.5 Validate + build thuần (verify không cần HTTP/DB)

- [ ] `public static void ValidateConfirmRequest(ConfirmSmartSetupRequest request, Guid boardId, IReadOnlyList<ColumnResponse> columns, IReadOnlyList<WorkspaceMemberResponse> members, IReadOnlyList<WorkspaceLabel> labels, int maxTaskCount)`:
  - `Description` trim rỗng hoặc > `SmartSetupService.MaxDescriptionLength` (4000) → 400.
  - `Tasks` rỗng → 400; > `maxTaskCount` (`DeepSeekOptions.MaxTaskCount`, mặc định 20) → 400 (không tự cắt — đây là lệnh ghi dữ liệu, phải tường minh).
  - Mỗi task: title trim 1–`MaxTitleLength` (200); description cắt/kiểm ≤ `MaxTaskDescriptionLength` (2000); `Priority` phải parse được sang `TaskPriority` hoặc `null` (dùng `Enum.TryParse` ignoreCase); labels ≤ `MaxLabelsPerTask` (5), mỗi `Name` trim 1–`MaxLabelLength` (80), `LabelId` (khi `Exists == true`) phải nằm trong `labels` của workspace (nếu không → 400); `Assignee?.UserId` nếu có phải nằm trong `members` (nếu không → 400).
  - `Summary` cắt ≤ 2000; `ColumnId` nếu có phải thuộc `columns` (nếu không → 400).
- [ ] `public static string BuildBasis(...)` → JSON tóm tắt input (kiểm soát token/chi phí — không dump toàn văn):
  ```json
  { "boardId": "…", "boardName": "…", "workspaceId": "…",
    "descriptionLength": 812, "descriptionExcerpt": "300 ký tự đầu",
    "memberCount": 3, "columnCount": 4, "taskCount": 7, "requestedAt": "2026-…" }
  ```
  Không đưa `ApiKey`/prompt thô vào `basis`.
- [ ] `public static string BuildAppliedSnapshotJson(AiActionAppliedResult result)` → `{ "entityType": "Board", "entityId": "…", "createdTaskIds": [...], "createdLabelIds": [...], "warnings": [...], "appliedAt": "…" }`.
- [ ] Các hàm trên để `public static` (đúng pattern `SmartSetupService.BuildProposal/NormalizeTasks`) để verify logic chuẩn hoá mà không cần dựng API — và làm điểm tựa cho test backend ở Giai đoạn 7.

### 2.6 Đăng ký DI (`AiModule.cs`)

- [ ] `services.AddScoped<IAiActionService, AiActionService>();`
- [ ] `services.AddScoped<IAiActionApplier, CreateSubtasksApplier>();`
- [ ] `MapAiModuleEndpoints()` → thêm `endpoints.MapAiActionEndpoints();` (giữ `endpoints.MapSmartSetupEndpoints();`).
- [ ] `Program.cs` **không phải sửa** (extension point đã có từ Giai đoạn 3).
- [ ] **Verify §2:** `dotnet build` sạch; boot API → `GET /api/health` 200; DI resolve được `IAiActionService` (không có lỗi "unable to resolve" ở route mới).

---

## 3. Backend – Endpoints & DTO

### 3.1 DTO (`DTOs/AiActionDtos.cs`)

- [ ] ```csharp
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
      JsonElement Basis, JsonElement? BeforeSnapshot, JsonElement? AfterSnapshot, JsonElement? AppliedSnapshot,
      IReadOnlyList<Guid> CreatedTaskIds,
      DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

  public sealed record RejectAiActionRequest(string? Note);
  ```
- [ ] `Status` serialize **camelCase** (`"Pending"` — enum đóng nên giữ nguyên casing canonical như `TaskPriority`, không đổi sang lowercase).

### 3.2 `Endpoints/AiActionEndpoints.cs`

- [ ] Route group 1 (dùng lại prefix Smart Setup, cạnh endpoint generate):
  `POST /api/boards/{boardId:guid}/smart-setup/confirm` → `201` + `AiActionLogResponse` (`Location: /api/ai-actions/{id}`).
- [ ] Route group 2: `/api/boards/{boardId:guid}/ai-actions`
  - `GET /?status=Pending&take=20` → `200 AiActionLogResponse[]`.
- [ ] Route group 3: `/api/ai-actions/{logId:guid}`
  - `GET /` → `200 AiActionLogDetailResponse`
  - `POST /{logId}/approve` → `200 AiActionLogDetailResponse` (Approved)
  - `POST /{logId}/reject` → `200 AiActionLogDetailResponse` (Rejected), body `RejectAiActionRequest`
  - `POST /{logId}/undo` → `200 AiActionLogDetailResponse` (Undone)
- [ ] Mọi group: `.WithTags("AiActions")` + `.AddEndpointFilter<DomainExceptionFilter>()`; mọi route `.RequireAuthorization()`; mọi `POST` thêm `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` (header `X-XSRF-TOKEN` do `httpClient` tự gắn).
- [ ] Đọc `userId` từ claim `NameIdentifier` qua **một helper private dùng chung** trong module Ai (`AiEndpointHelpers.RequireUserId(HttpContext)`), giữ nguyên pattern `SmartSetupEndpoints` (`CurrentUser` của Board là `internal` — **không** sửa module Board, **không** đổi `CurrentUser` thành public trong phạm vi Giai đoạn 4).
- [ ] `SmartSetupEndpoints` chỉ còn `POST /` (generate). Confirm nằm ở `AiActionEndpoints` để tách rõ "đề xuất (không ghi)" khỏi "ghi qua Accountability Layer".

### 3.3 Mã lỗi & contract cho UI (body lỗi luôn là `{ "error": "…" }`)

| Status | Khi nào | Gợi ý UI |
|---|---|---|
| 400 | description rỗng/> 4000; 0 task; > `MaxTaskCount`; title sai; priority sai; labels quá 5/quá dài; `labelId` khác workspace; assignee không phải member; `columnId` khác board; board 0 cột (lúc approve); `action` không có applier | `Alert` lỗi + giữ nguyên dữ liệu người dùng đã nhập |
| 401 | hết phiên | `httpClient` tự refresh; vẫn lỗi → điều hướng login |
| 403 | Member (không phải Manager/Admin) hoặc thiếu CSRF | `Alert` "Bạn cần quyền Manager/Admin trong workspace này" |
| 404 | board/log không tồn tại hoặc không thấy | `Alert` + đóng modal/drawer |
| 409 | log không còn `Pending` (đã duyệt/từ chối/hoàn tác ở nơi khác) | `Alert` "Hành động này đã được xử lý ở nơi khác" + `reload()` danh sách |
| 502 | (chỉ ở endpoint generate Phase 3) | giữ nguyên xử lý cũ |

---

## 4. Frontend – `src/features/ai/`

> **Bàn giao (theo convention Phase 3):** §4 + §5.2 do **antigravity** đảm nhiệm. Backend §1–§3 đã verify end-to-end; đọc hết §3 (route/DTO/mã lỗi) trước khi code.

### 4.1 Types, API, hook

- [ ] `types/aiAction.types.ts`: mirror DTO §3.1 (camelCase) + `export type AiActionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Undone'` + `AiActionType = 'CreateSubtasks'`.
- [ ] `services/aiActionApi.ts` (qua `httpClient` base `/api`; **không** tự set `X-XSRF-TOKEN`):
  - `confirmSmartSetup(boardId, payload: ConfirmSmartSetupRequest): Promise<AiActionLog>`
  - `listAiActions(boardId, params?: { status?: AiActionStatus; take?: number }): Promise<AiActionLog[]>`
  - `getAiAction(logId): Promise<AiActionLogDetail>`
  - `approveAiAction(logId): Promise<AiActionLogDetail>`
  - `rejectAiAction(logId, note?: string): Promise<AiActionLogDetail>`
  - `undoAiAction(logId): Promise<AiActionLogDetail>`
- [ ] `hooks/useAiActions.ts`: state `{ logs, pendingCount, status: 'idle'|'loading'|'approving'|'rejecting'|'undoing', error, httpStatus }`; `reload(status?)`; `approve/reject/undo` (cập nhật log trong list theo response, set `error` + map status như bảng §3.3, đặc biệt **409 → reload list**); sau `approve`/`undo` thành công gọi callback `onBoardChanged?.()` để board refetch.

### 4.2 `SmartSetupModal` – chuyển "Xác nhận" sang luồng Pending thật

- [ ] `useSmartSetup`: bỏ `confirm()` đồng bộ. Thêm `submitConfirm(boardId, columnId?)` **async**; status mới `'submitting'` và `'pending'`; thêm field `actionLog: AiActionLogDetail | null`; `reset()` xoá `actionLog`.
  - `submitConfirm` gọi `aiActionApi.confirmSmartSetup` với `{ description, summary, tasks (đã bỏ `tempId`), columnId }`; giữ `tasks` trong state để UI hiển thị đúng cái vừa gửi.
- [ ] `SmartSetupModal` bước 2: nút **"Xác nhận đề xuất (n)"** → `loading={status === 'submitting'}`, **disable** khi đang gửi (không double-submit). Thêm `Select` cột đích (mặc định cột đầu tiên) truyền vào `submitConfirm` — cột lấy từ prop `columns` (đã có sẵn ở `BoardView`).
- [ ] Bước 3 (`status === 'pending' || 'confirmed'`): **xoá hẳn** text "sẽ được áp dụng qua Accountability Layer ở Giai đoạn 4" và thay bằng:
  - `Result`/`Alert`: "Đã tạo yêu cầu chờ duyệt" + `log id` + thời điểm + `Tag` trạng thái `Pending` (gold).
  - Nút **"Duyệt & áp dụng"** → `aiActionApi.approveAiAction(logId)`; thành công → `Result.success` "Đã áp dụng n sub-tasks vào bảng" + gọi `onApplied?.()`.
  - Nút **"Từ chối"** → `Input.TextArea` lý do (≤ 500, tuỳ chọn) + `rejectAiAction`.
  - Nút **"Hoàn tác"** (chỉ hiện sau khi đã duyệt) → `undoAiAction` + thông báo số task đã gỡ.
  - Lỗi 409 → `Alert` "Hành động đã được xử lý ở nơi khác" + hiển thị trạng thái mới lấy từ response/lỗi.
- [ ] Prop mới của modal: `columns?: ColumnResponse[]`, `onApplied?: () => void`.

### 4.3 `AiActionHistoryDrawer` + badge ở BoardView

- [ ] `components/AiActionHistoryDrawer.tsx`: `Drawer` Ant Design mở từ top bar BoardView; gọi `useAiActions.reload()` khi mở + sau mỗi thao tác.
  - `ListView` các `AiActionLogItem`: `Tag` trạng thái (Pending = gold, Approved = green, Rejected = red, Undone = default), `action`, tên người yêu cầu/người quyết định, thời gian, `taskCount`, `decisionNote`.
  - Filter theo status (Segmented: Tất cả / Chờ duyệt / Đã duyệt / Từ chối / Đã hoàn tác) + `Empty` + `Spin` + `Alert` lỗi.
- [ ] `components/AiActionLogItem.tsx`: `Collapse` chi tiết — preview danh sách task trong `after_snapshot` (title/priority/assignee/labels), `basis` (JSON pretty), `applied_snapshot` khi có, `before_snapshot` khi có; nút hành động **Duyệt / Từ chối** (Pending) và **Hoàn tác** (Approved) — cùng xử lý 409 như §4.2.
- [ ] `BoardView.tsx`: thêm nút **"Lịch sử AI"** (icon `HistoryOutlined`) + `Badge count={pendingCount}` cạnh nút "AI Smart Setup"; giữ `AiActionHistoryDrawer` trong state `aiHistoryOpen`; truyền `columns` + `onApplied={() => refetch()}` xuống `SmartSetupModal`. (Real-time task mới đã tự cập nhật qua `useBoardHub` → `applyTaskCreated`; `refetch()` bổ sung để đồng bộ nhãn mới tạo.)
- [ ] `features/ai/index.ts`: export thêm types/api/hook/components mới.

### 4.4 Verify §4

- [ ] `npm run lint` (oxlint 0 warning/error) + `npx tsc -b` + `npm run build` + `npm test` — tất cả sạch.

---

## 5. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–3)

### 5.1 Verify backend (harness tạm ngoài workspace + API thật + PostgreSQL thật)

> Cùng cách Phase 3 §Verify §4: harness đặt ngoài workspace, tự ký JWT bằng `Jwt:SigningKey` trong User Secrets, **xoá sau khi chạy**, không commit.

1. [ ] Migration: `ai_action_logs` đủ cột/CHECK/index đúng thiết kế §1.
2. [ ] `POST .../smart-setup/confirm` (Manager, CSRF hợp lệ) → **201**, body `status = "Pending"`, `ai_action_logs` +1 với `basis` + `after_snapshot` là JSON hợp lệ; **`tasks` / `labels` / `task_labels` KHÔNG đổi** (yêu cầu cốt lõi "log trước, ghi sau").
3. [ ] `POST /ai-actions/{id}/approve` → **200** `Approved`; `tasks` +N đúng `column_id` đích, `position` nối cuối cột, priority/description đúng; nhãn `exists=false` được tạo + gắn qua `task_labels`; `assignee_id` đúng cho assignee còn là member; `applied_snapshot` có `createdTaskIds`/`createdLabelIds`; log có `decided_by_user_id` + `decided_at`; client SignalR thứ 2 nhận `TaskCreated` cho từng task.
4. [ ] `approve` lần 2 → **409**, `tasks` không tăng (idempotency); `approve` sau `reject`/`undo` → 409.
5. [ ] `reject` (kèm note) → **200** `Rejected` + `decision_note`; `tasks`/`labels` không đổi; `reject` lần 2 → 409.
6. [ ] `undo` (sau approve) → **200** `Undone`; các task có `deleted_at` (API `GET /api/boards/{id}/tasks` trả về số cũ), `task_labels` của chúng bị xoá, nhãn do action tạo bị xoá **nếu không còn ai dùng**, `TaskDeleted` broadcast; `undo` lần 2 → 409; case nhãn đang được task khác dùng ⇒ nhãn **giữ lại** + warning trong `applied_snapshot`.
7. [ ] Guards: không auth → 401; thiếu CSRF → 403; Member → 403 *(nếu DB local chỉ có 1 user thì ghi rõ "chưa verify được" như Phase 3 §Verify §4)*; board 404; log 404; title rỗng → 400; > `MaxTaskCount` → 400; `labelId` khác workspace → 400; assignee không phải member → 400; `columnId` khác board → 400; board 0 cột → 400 khi approve.
8. [ ] `GET /api/boards/{boardId}/ai-actions?status=Pending` → đúng danh sách, sort `created_at DESC`, `take` clamp 1–100, `taskCount` khớp `after_snapshot`; `GET /api/ai-actions/{id}` trả đủ 4 JSON + `createdTaskIds`.
9. [ ] Harness hàm thuần (không cần HTTP/DB): `ValidateConfirmRequest` 12 case biên (description 0/4000/4001; tasks 0/1/21; title 0/200/201; priority `"urgent"`/`"Critical"`; labels 6 nhãn; label 81 ký tự; `labelId` ngoài workspace; assignee ngoài member; `columnId` ngoài board); `BuildBasis` (không lộ nội dung dài); `BuildAppliedSnapshotJson` round-trip parse.
10. [ ] Kiểm tra kiến trúc "cổng duy nhất": `rg "_db\.(Tasks|Labels|TaskLabels)\.(Add|Remove)" src/Modules/Ai` → chỉ khớp trong `Services/Appliers/CreateSubtasksApplier.cs`.
11. [ ] *(Tuỳ chọn)* `Project-Documents/report/phase-4-accountability-test-report.md` + ảnh chụp drawer/luồng duyệt (theo tiền lệ `report/phase-2-kanban-test-report.md`).

### 5.2 Frontend (Vitest + Testing Library)

- [ ] Test `aiActionApi`: đúng method/URL/body cho `confirmSmartSetup` (`/boards/{boardId}/smart-setup/confirm`), `listAiActions` (query `status`/`take`), `approve/reject/undo` (`/ai-actions/{id}/...`).
- [ ] Test `useAiActions`: `idle → loading → idle`; `approve` cập nhật log trong list; **409 → set error + reload**; `pendingCount` đúng.
- [ ] Test `AiActionHistoryDrawer` + `AiActionLogItem`: render theo 4 status, nút chỉ hiện đúng trạng thái, gọi đúng api, hiển thị `decisionNote`, `Empty` khi rỗng.
- [ ] Cập nhật `useSmartSetup.test.ts`: `submitConfirm` gọi API và chuyển `generating → ready → submitting → pending`; lỗi 400/403/409 map đúng message.
- [ ] Cập nhật `SmartSetupModal.test.tsx`: nút "Xác nhận đề xuất" **có** gọi `aiActionApi.confirmSmartSetup`; bước 3 hiển thị `log id` + `Pending`; nút "Duyệt & áp dụng" gọi `approveAiAction`; nút "Hoàn tác" gọi `undoAiAction`; **không còn** text "Giai đoạn 4".
- [ ] `BoardView.test.tsx`: nút "Lịch sử AI" tồn tại + mở drawer; `Badge` hiển thị số pending.

### 5.3 Điều kiện hoàn thành Giai đoạn 4 (map `03-roadmap.md`)

- [ ] Bảng `AiActionLog` lưu `action`, `basis` (tóm tắt input), timestamp, actor, trạng thái — verify ở §5.1 bước 1–3, 8.
- [ ] `AiActionService` là điểm **duy nhất** mọi hành động AI đi qua trước khi ghi dữ liệu thật — verify ở §5.1 bước 2 (confirm không ghi) + bước 10 (không còn đường ghi khác).
- [ ] Smart Setup (Giai đoạn 3) đã chuyển sang luồng `Pending → Approve/Reject` qua service này — verify ở §5.1 bước 2–5 + §5.2.
- [ ] Undo hoạt động cho ít nhất 1 loại hành động AI (`CreateSubtasks`) — verify ở §5.1 bước 6.

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

- [ ] Tạo `Project-Documents/tasks/phase-4-accountability-layer.md` (checklist như trên).
- [ ] Cập nhật `Project-Documents/04-database-design.md` §3.5 (2 cột mới + quy ước `entity_type`/`entity_id`) và §5 (index mới) — **làm ngay cùng bước lập kế hoạch này** để spec và task doc không lệch.
- [ ] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`: bảng endpoint mới, vòng đời log, applier registry, mã 409, contract bàn giao Giai đoạn 5.
- [ ] Cập nhật `README.md` root (mục "Trạng thái (Giai đoạn 4 – Accountability Layer)") và tick checklist `03-roadmap.md` khi hoàn tất.

> Ghi chú: các mục ở §7 (trừ `04-database-design.md`) chỉ tick khi **hiện thực xong** Giai đoạn 4 — file task doc này không tự tick.

---

## Checklist Hoàn thiện Giai đoạn 4

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 5 (AI Observer).

- [ ] Bảng `ai_action_logs` (`AiActionLog`) lưu action, `basis` (tóm tắt input), timestamp, actor (`requested_by`/`decided_by`), trạng thái `Pending/Approved/Rejected/Undone` — migration `Phase4AccountabilityLayer` đã áp dụng
- [ ] Service `AiActionService` là **điểm duy nhất** mọi hành động AI đi qua trước khi ghi dữ liệu thật (applier registry theo `action`; chỉ applier được chạm `Tasks`/`Labels`/`TaskLabels`)
- [ ] Smart Setup (Giai đoạn 3) đã chuyển sang luồng `Pending → Approve/Reject` qua service này — `confirm` **không** ghi `tasks`/`labels`; chỉ `approve` mới ghi
- [ ] Chức năng Undo hoạt động cho ít nhất 1 loại hành động AI (`CreateSubtasks`): soft-delete task đã tạo + dọn `task_labels` + nhãn không còn tham chiếu, có broadcast real-time

### Việc của antigravity (§4 + §5.2)

- [ ] §4 frontend `src/features/ai/` (`types/aiAction.types.ts`, `services/aiActionApi.ts`, `hooks/useAiActions.ts`, `components/AiActionHistoryDrawer.tsx`, `components/AiActionLogItem.tsx`) + chuyển `SmartSetupModal`/`useSmartSetup` sang luồng Pending thật + nút "Lịch sử AI" ở `BoardView.tsx`
- [ ] §5.2 test Vitest cho `aiActionApi` / `useAiActions` / `AiActionHistoryDrawer` / `AiActionLogItem` + cập nhật `useSmartSetup.test.ts`, `SmartSetupModal.test.tsx`, `BoardView.test.tsx`; chạy `oxlint` (0 warn/0 err) + `tsc -b` + `vite build` + `vitest run` sạch
- [ ] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`, `README.md` root và tick `03-roadmap.md` (sau khi backend verify xong)
