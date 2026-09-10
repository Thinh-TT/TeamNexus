# Giai đoạn 5 – AI Observer

> **Mục tiêu:** Xây `BackgroundService` chạy nền quét hoạt động của **mọi workspace** theo chu kỳ cố định, phát hiện các tín hiệu bất thường (task **quá hạn**, task **đứng yên** lâu ngày, **người quá tải**, **nghẽn ở cột**), **tóm tắt tín hiệu trước khi gửi DeepSeek** (kiểm soát token/chi phí), rồi ghi cảnh báo vào `notifications` **chỉ cho Manager/Admin** trong workspace đó — không public toàn team, không ghi bất kỳ dữ liệu nghiệp vụ nào.
>
> **Công nghệ:** ASP.NET Core (.NET 10) Modular Monolith · EF Core (Npgsql, `jsonb`) · `BackgroundService` + `PeriodicTimer` · PostgreSQL advisory lock · Minimal API + endpoint filters (CSRF/domain-error) · React + TypeScript + Vite + Ant Design · Vitest.
>
> **Tham chiếu:** `03-roadmap.md` (Giai đoạn 5) · `02-tech-stack-decisions.md` §2.3 & §5 · `04-database-design.md` §3.6, §4, §5, §7 · contract bàn giao ở `tasks/phase-4-accountability-layer.md` §6 và `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` (mục "Contract bàn giao Giai đoạn 5").

---

## 0. Tiền đề & quyết định kiến trúc (đã chốt)

Đọc hết mục này trước khi code — các quyết định dưới đây đã chốt, **không chọn lại**.

**Trạng thái đầu vào (đã kiểm tra thực tế trong repo):**

- `activity_logs`, `notifications`, `ai_observer_runs` **chưa tồn tại** ở dạng entity/migration/code — mới chỉ có trong `04-database-design.md` §3.6. Toàn bộ §1–§2 là việc **mới** của Giai đoạn 5.
- `IAiProvider` (`Services/AiProvider.cs`) đã sẵn sàng tái dùng cho Observer "chỉ bằng cách đổi prompt" (đúng ghi chú `README.md` module Ai §2) → **không** sửa hợp đồng provider.
- `IWorkspaceAccess.RequireManagerAsync/RequireMemberAsync` (module Board) tái dùng nguyên trạng để gate quyền.
- `AiEndpointHelpers.RequireUserId(HttpContext)` (Phase 4 §3.2) tái dùng cho mọi endpoint mới.
- `frontend/src/shared/api/httpClient.ts` đã tự gắn `X-XSRF-TOKEN` + tự refresh 401 → frontend **không** sửa tầng API chung.

| # | Quyết định | Lý do / ghi chú |
|---|---|---|
| **D1** | **Không tạo module mới.** Entity mới ở `TeamNexus.Persistence`; mọi service/endpoint của Observer nằm trong module `TeamNexus.Modules.Ai` | Khớp `02` §1: module `Ai` = Smart Setup + Accountability + **Observer** |
| **D2** | Giữ **một DbContext / một chuỗi migration**; thêm **một** migration `Phase5AiObserverSchema` cho cả 3 bảng | Đúng `04` §1.1 |
| **D3** | **Thu thập activity log + đọc trạng thái board** (quyết định của người dùng). `IActivityLogWriter` được khai báo **trong module Board**, chỉ **implement ở module Ai** | Tránh circular dependency: `Board` **không** được tham chiếu `Ai`; Ai đã tham chiếu Board nên implement được. Không dùng EF interceptor (tránh "magic" khó verify) |
| **D4** | Đợt này phát hiện **3 tín hiệu bắt buộc**: `OverdueTask`, `StalledTask`, `Overload`; `Bottleneck` là **optional** (cờ `BottleneckDetectionEnabled`, mặc định `true`) | Roadmap chỉ cần "ít nhất 1 loại"; 3 loại cho demo đủ mạnh nhưng công thức vẫn xác định được |
| **D5** | Tín hiệu **tính bằng hàm thuần** (`ObserverSignalDetector`, dữ liệu nạp sẵn) | Cùng pattern `SmartSetupService.BuildProposal` / `AiActionService.ValidateConfirmRequest`, verify không cần AI/HTTP |
| **D6** | **Không gọi AI khi không có tín hiệu**; chỉ gọi khi `Signals.Count > 0` | Kiểm soát chi phí là yêu cầu roadmap; cũng là tiêu chí verify bắt buộc |
| **D7** | AI chỉ **diễn giải / tổng hợp / xếp hạng**, **không** sinh dữ liệu mới. Mọi `evidence.taskIds/userIds` phải **tồn tại thật** trong tín hiệu đã tính (locator chống hallucination) | Accuracy > "AI sáng tạo"; giữ vai trò Observer = lớp *đọc/cảnh báo* |
| **D8** | Hợp đồng AI đầu ra là JSON **tự định nghĩa** (`AiObserverOutput`), **không** tái dùng schema Smart Setup | Đúng "Giai đoạn 5 chỉ đổi prompt" ở README module Ai |
| **D9** | `FakeAiProvider` nhận diện prompt Observer bằng **marker** `{"agent":"observer"}` ở **dòng đầu** user prompt → trả findings mẫu; ngược lại giữ nguyên proposal Smart Setup | Verify end-to-end **không gọi AI, không tốn token** (đúng cách Phase 3–4). Findings mẫu lấy ID bằng cách parse `evidence` trong prompt ⇒ luôn đi qua được validator |
| **D10** | Một run = **một workspace** (1 row `ai_observer_runs`); fan-out người nhận ở tầng Notification | Cho phép list run theo workspace; `summary.findings[]` là dữ liệu chi tiết của run |
| **D11** | `notifications`: **1 row / 1 người nhận** (`recipient_user_id` = Manager/Admin), cap `MaxManagersPerWorkspace` (10) | `is_read`/`read_at` theo từng người; đọc đúng 1 index `(recipient_user_id, is_read)`, không cần join |
| **D12** | **Chống trùng**: bỏ finding nếu đã có notification cùng `(workspace_id, type, entityId)` trong `DeduplicationWindowHours` (24h) | Observer chạy mỗi 30 phút ⇒ không spam hộp thư Manager |
| **D13** | **Chống chạy chồng**: `pg_try_advisory_lock` (hằng số cố định của module) giữ trong suốt run; không lấy được lock ⇒ run `Skipped`; cộng thêm `static SemaphoreSlim(1,1)` trong process | An toàn khi 2 instance/timer trùng nhau; không cần bảng lock |
| **D14** | **Retention**: prune `activity_logs` cũ hơn `RetentionDays` (đúng `04` §7), chạy cuối mỗi run thành công, có cap số row xoá mỗi lần | Free-tier quota; không để bảng phình |
| **D15** | `IObserverService` đăng ký **cả** `AddScoped` (endpoint gọi tay) **và** `AddHostedService<ObserverBackgroundService>` | Cùng **một** code path cho quét định kỳ và quét on-demand (chống lệch hành vi) |
| **D16** | Kênh gửi đợt này: **chỉ in-app**. Email là hạng mục **tương lai** (ghi rõ + gợi ý interface `INotificationChannel`), **không** hiện thực ở Giai đoạn 5 | Quyết định của người dùng: "note email cho tương lai" |
| **D17** | **Không** thêm SignalR event cho notification: UI poll `unreadCount` mỗi 60s + refresh khi mở drawer / sau khi quét tay | Tránh sửa `BoardHub` (tránh rủi ro phải rebuild artifact Phase 2 khi đang làm Phase 5) |
| **D18** | **Không** tạo project xUnit (để Giai đoạn 7). Verify backend bằng harness tạm ngoài workspace + API thật + PostgreSQL thật | Nhất quán Phase 2–4 |
| **D19** | Mọi ngưỡng nằm trong config section `Observer`, **không** hard-code | Demo/tinh chỉnh không cần build lại; cũng là công tắc tắt an toàn cho production |

**Non-goals Giai đoạn 5 (ghi rõ để không over-scope):** gửi email/push; AI tự tạo/sửa task; phân tích nội dung comment bằng AI (chỉ dùng **metadata** comment: số lượng + thời điểm); sentiment/conflict-detection nâng cao; quét real-time theo event; SignalR cho notification; test xUnit; đa ngôn ngữ UI.

**Luồng tổng thể:**

```
ĐƯỜNG GHI (module Board — mọi mutation task/comment)
  TaskService.CreateTask/UpdateTask/MoveTask/DeleteTask ─┐
  CommentService.CreateComment                          ─┼─► IActivityLogWriter  (interface khai báo trong Board,
  (impl ở module Ai)                                    ─┘    implementation ở Ai)  → INSERT activity_logs

ĐƯỜNG OBSERVER (module Ai)
  ObserverBackgroundService (PeriodicTimer, IntervalMinutes)
        │ hoặc POST /api/workspaces/{id}/observer/scan  (Manager/Admin, quét tay)
        ▼
  ObserverService.ScanAsync(workspaceId?, ct)
   1. Enabled? ── không (và không chỉ định workspace) ──► Skipped, không ghi DB
   2. pg_try_advisory_lock ── không lấy được ───────────► Skipped (AlreadyRunning)
   3. Nạp workspace active (cap MaxWorkspacesPerRun) + dữ liệu bounded của từng workspace
   4. ObserverSignalDetector.Analyze(...) → ObserverSignal[]
        │ Signals rỗng ──► run Completed: aiCalled=false, 0 notification, 0 token
        ▼
   5. ObserverSummarizer.BuildRequest(...) → prompt NHỎ có marker observer (≤ MaxPromptCharacters)
   6. IAiProvider.CompleteAsync(JsonMode, Temperature 0, MaxOutputTokens) → AiObserverOutput
   7. ObserverFindingValidator: giao evidence ∩ ID tín hiệu (D7) → ObserverFinding[]
   8. Dedupe 24h (D12) → INotificationService.NotifyManagersAsync(...)
   9. Ghi ai_observer_runs (status/summary/token) → prune activity_logs (D14)
```

---

## 1. Backend – Schema & Migration

### 1.1 Entity `ActivityLog` (`src/TeamNexus.Persistence/Data/Entities/ActivityLog.cs`)

- [x] Tạo class `ActivityLog : IAuditableEntity`, **append-only**, **không** navigation tới `Workspace`/`Board` (chỉ FK thô — tránh query filter `deleted_at` của `Workspace` làm ẩn log):

  | Field | Kiểu C# | Ghi chú |
  |---|---|---|
  | `Id` | `Guid` | PK, sinh ở app layer |
  | `WorkspaceId` | `Guid` | FK → `workspaces`, required |
  | `BoardId` | `Guid?` | FK → `boards`, nullable (sự kiện cấp workspace) |
  | `UserId` | `Guid?` | FK → `users`, actor (`null` = hệ thống) |
  | `EntityType` | `string` | ≤ 64, giá trị từ `ObserverEntityTypes` (`Task` / `Comment`) |
  | `EntityId` | `Guid?` | id entity bị tác động |
  | `Action` | `string` | ≤ 64, giá trị từ `ObserverActivityActions` (text tự do — `04` §4) |
  | `Payload` | `string?` | jsonb — diff **ngắn** (`columnId`, `assigneeId`, `isDone`…), không dump toàn văn |
  | `CreatedAt` | `DateTimeOffset` | tự stamp qua `IAuditableEntity` |

- [x] `IAuditableEntity` cũng stamp `UpdatedAt` — với log append-only thì vô hại (giữ đúng interface chung, không tạo abstraction mới).

### 1.2 Fluent config (`Data/Configurations/ActivityLogConfiguration.cs`)

- [x] `builder.ToTable("activity_logs")`; `Action`/`EntityType` `HasMaxLength(64).IsRequired()`.
- [x] `Payload` → `.HasColumnType("jsonb")`.
- [x] FK `Workspace`/`Board`/`User` đều `DeleteBehavior.Restrict` (`04` §7: **không** cascade vật lý).
- [x] Index: `(WorkspaceId, CreatedAt)` (Observer quét theo chu kỳ — `04` §5); FK index `BoardId` tự sinh bởi EF.
- [x] **Không** `HasQueryFilter` (bảng sự kiện append-only; ghi chú lý do ngay trong code + `04` §3.6).

### 1.3 Entity + config `Notification` (`Data/Entities/Notification.cs`)

- [x] `Notification : IAuditableEntity` + navigation `ApplicationUser? RecipientUser`:

  | Field | Kiểu C# | Ghi chú |
  |---|---|---|
  | `Id` | `Guid` | PK |
  | `WorkspaceId` | `Guid` | FK → `workspaces`, required |
  | `RecipientUserId` | `Guid` | FK → `users`, required — **chỉ Manager/Admin** (D11) |
  | `Type` | `string` | ≤ 32, giá trị từ `NotificationTypes` |
  | `Title` | `string` | ≤ 200 |
  | `Message` | `string` | ≤ 2000 |
  | `Payload` | `string?` | jsonb: `runId`, `boardId`, `taskIds`, `userIds`, `signals[]`, `severity`, `model`, `tokens` |
  | `IsRead` | `bool` | = `false` bằng **initializer trong entity** (không dùng `HasDefaultValue` — tiền lệ Phase 4 §1.2) |
  | `CreatedAt` / `ReadAt` | `DateTimeOffset` / `DateTimeOffset?` | |

- [x] `NotificationConfiguration.cs`: `ToTable("notifications")`, `Type` `HasMaxLength(32)`, `Title` `HasMaxLength(200)`, `Message` `HasMaxLength(2000)`, `Payload` `.HasColumnType("jsonb")`, FK `RecipientUser` + `Workspace` RESTRICT, index `(RecipientUserId, IsRead)` (`04` §5), **không** query filter.

### 1.4 Entity + config `AiObserverRun` (`Data/Entities/AiObserverRun.cs`)

- [x] Tạo enum `ObserverRunStatus { Running, Completed, Skipped, Failed }` (cùng file entity).
- [x] `AiObserverRun : IAuditableEntity` với `Id`, `WorkspaceId` (FK required, RESTRICT), `StartedAt`, `FinishedAt?`, `Status` (enum → text + CHECK), `Summary` (jsonb, nullable):

  ```json
  { "signalsDetected": 3, "signalsByType": { "OverdueTask": 1, "StalledTask": 1, "Overload": 1 },
    "truncatedSignals": 0, "findingsWritten": 2, "notificationsCreated": 4,
    "aiCalled": true, "model": "deepseek-chat", "promptTokens": 812, "completionTokens": 214,
    "durationMs": 1840, "skippedReason": null, "error": null }
  ```

- [x] `AiObserverRunConfiguration.cs`: `ToTable("ai_observer_runs", t => t.HasCheckConstraint("ck_ai_observer_runs_status", "\"status\" IN ('Running', 'Completed', 'Skipped', 'Failed')"))`; `Status` `.HasConversion<string>().HasMaxLength(16)`; `Summary` `.HasColumnType("jsonb")`; index `WorkspaceId` (`04` §5); FK RESTRICT.
- [x] **Không** query filter.

### 1.5 DbContext & Migration

- [x] `TeamNexusDbContext`: thêm
  ```csharp
  public DbSet<ActivityLog> Activities => Set<ActivityLog>();
  public DbSet<Notification> Notifications => Set<Notification>();
  public DbSet<AiObserverRun> AiObserverRuns => Set<AiObserverRun>();
  ```
- [x] `dotnet ef migrations add Phase5AiObserverSchema --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api` → sinh 1 file `.cs` + `.Designer.cs` (cả 3 bảng trong **một** migration; không có phụ thuộc vòng).
- [x] `dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api` — áp lên PostgreSQL local (DB `TeamNexus`).
- [x] Cập nhật `Project-Documents/04-database-design.md` §3.6/§4/§5 — *đã làm ở bước lập kế hoạch (commit tài liệu), theo tiền lệ Phase 4 §1.4*.
- [x] **Verify §1 (đã chạy):** `dotnet build TeamNexus.sln` → **0 warning / 0 error**; harness tạm ngoài workspace (`%TEMP%\tn-ai-s5-s1-verify`, đã xoá) đọc `information_schema`/`pg_constraint`/`pg_indexes` + round-trip insert→read→delete → **28/28 check PASS**; `dotnet ef migrations list` → **5 migration đã applied** (`20260910105154_Phase5AiObserverSchema`); **4 migration cũ + 8 file cũ zero-diff**, snapshot chỉ **+227 dòng / −0**; DB dev về đúng baseline (`workspaces=2, boards=15, tasks=18, users=1, ai_action_logs=1`, 3 bảng mới `0 row`).

> **Ghi chú hiện thực §1 (khác biệt nhỏ so với spec, có chủ ý):**
> - FK khai báo **không kèm navigation** bằng `HasOne<Workspace>()` / `HasOne<Board>()` / `HasOne<ApplicationUser>()` cho `ActivityLog` và `HasOne<Workspace>()` cho `Notification`/`AiObserverRun`; **chỉ** `Notification.RecipientUser` có navigation (cần `DisplayName` để hiển thị người nhận — đúng tiền lệ `AiActionLog`). Nhờ vậy EF **không** kế thừa query filter `deleted_at` của `Workspace`/`Board`, đúng ý đồ append-only.
> - `ObserverRunStatus` có thêm `Skipped` so với bản sơ bộ `04` §3.6 (`Running/Completed/Failed`) — biểu diễn "Observer đang tắt" / "không lấy được advisory lock" mà **không** ghi notification và **không** tính là lỗi.
> - `ActivityLog` có cả `CreatedAt` + `UpdatedAt` do dùng `IAuditableEntity`; cột `updated_at` không mang thông tin (append-only) nhưng giữ đúng convention `04` §1.2.
> - Tên FK do `EFCore.NamingConventions` sinh hơi đặc biệt nhưng **trỏ đúng bảng `users`** (không phải bảng Identity mặc định): `fk_activity_logs_asp_net_users_user_id`, `fk_notifications_users_recipient_user_id` (do bảng Identity `AspNetUsers` được map thành `users` ở Phase 1). Chỉ là tên, không ảnh hưởng schema.
> - ⚠️ **`dotnet ef` không được chạy với `--no-build`** trong repo này: CLI nạp assembly cũ nên vừa bỏ qua model mới, vừa báo sai `PendingModelChangesWarning` khi `database update` (đã gặp thực tế và fix bằng `migrations remove` → build → `migrations add` lại). Luôn chạy **kèm build**.
> - **jsonb KHÔNG round-trip byte-identical** (PostgreSQL chuẩn hoá thứ tự key/khoảng trắng — Phase 4 §1.2): verify thực tế `{"b":2,"a":1}` đọc lại thành `{"a": 1, "b": 2}`; mọi so sánh `payload`/`summary` phải parse `JsonElement` rồi so **ngữ nghĩa**, tuyệt đối không so chuỗi.
> - Mã SQLSTATE đã đo thật: FK RESTRICT khi xoá cha còn bị tham chiếu → **`23001 restrict_violation`** (không phải `23503`); CHECK `status='Bogus'` → **`23514`**; jsonb nhận chuỗi không hợp lệ → **`22P02`**.

---

## 2. Backend – Activity log (đường ghi)

### 2.1 Interface khai báo trong module Board (`src/Modules/Board/TeamNexus.Modules.Board/Services/IActivityLogWriter.cs`)

- [ ] Tạo file (interface + constants, **không** thêm project reference nào vào Board):
  ```csharp
  public static class ObserverActivityActions
  {
      public const string TaskCreated = "TaskCreated";
      public const string TaskUpdated = "TaskUpdated";
      public const string TaskMoved = "TaskMoved";
      public const string TaskCompleted = "TaskCompleted";   // khi task vào cột is_done
      public const string TaskDeleted = "TaskDeleted";
      public const string CommentAdded = "CommentAdded";
  }

  public static class ObserverEntityTypes
  {
      public const string Task = "Task";
      public const string Comment = "Comment";
  }

  public interface IActivityLogWriter
  {
      /// Ghi 1 sự kiện. KHÔNG bao giờ throw (lỗi ghi log không được làm hỏng request CRUD).
      Task RecordAsync(ActivityLogEntry entry, CancellationToken ct = default);
  }

  public sealed record ActivityLogEntry(
      Guid WorkspaceId, Guid? BoardId, Guid? UserId,
      string EntityType, Guid? EntityId, string Action, string? PayloadJson = null);
  ```
- [ ] Đặt `NullActivityLogWriter` (no-op) cùng file để harness dùng `AddBoardModule` **không cần** `AddAiModule`, và để production không bao giờ thiếu dependency.

### 2.2 Implement trong module Ai (`Services/ActivityLogWriter.cs`)

- [ ] `ActivityLogWriter : IActivityLogWriter` → `_db.Activities.Add(new ActivityLog { … })` + `SaveChangesAsync`; bọc `try/catch` + `LogWarning` (đúng tinh thần `BoardEventPublisher`: ghi log phụ không được làm hỏng luồng chính).
- [ ] Validate phòng thủ trước khi ghi: `Action`/`EntityType` không rỗng và ≤ 64 (nếu vi phạm ⇒ warning + bỏ qua, **không** throw).
- [ ] `PayloadJson` `null`/rỗng ⇒ lưu `null` (không lưu chuỗi `"{}"` vô nghĩa).

### 2.3 Gắn vào module Board (chỉ thêm dòng ghi log, không đổi nghiệp vụ)

- [ ] `TaskService`: constructor nhận thêm `IActivityLogWriter`; ghi **sau khi** `SaveChangesAsync` thành công:
  - `CreateTaskAsync` → `TaskCreated` (payload `{ columnId, assigneeId, priority }`).
  - `UpdateTaskAsync` → `TaskUpdated` (payload **chỉ các field thay đổi**: `titleChanged`, `assigneeId`, `dueDate`, `priority` — không dump nội dung).
  - `MoveTaskAsync` → `TaskMoved` (payload `{ fromColumnId, toColumnId }`) **+ `TaskCompleted`** khi `targetColumn.IsDone` (payload `{ columnId }`); **ghi sau `CommitAsync`** để log không làm rollback thao tác kéo-thả.
  - `DeleteTaskAsync` → `TaskDeleted` (payload `{ columnId }`).
- [ ] `CommentService.CreateCommentAsync` → `CommentAdded` (payload `{ commentId, taskId }`); cần `WorkspaceId` → đã có `task.Board!.WorkspaceId` từ `LoadTaskWithBoardAsync`.
- [ ] **Không** đổi chữ ký `ITaskService`/`ICommentService`, **không** đổi hành vi broadcast SignalR, **không** đổi logic validate/position/`completed_at`.
- [ ] ⚠️ Đây là **thay đổi duy nhất** của Giai đoạn 5 chạm module Board — cấm mọi thay đổi khác (endpoint/DTO/quyền/behaviour).
- [ ] **Verify §2 (nhóm G §9.1):** gọi lần lượt create/update/move→Done/delete/comment qua API thật → đếm row `activity_logs` tăng đúng; `action`/`entity_type`/`entity_id`/`payload` đúng; mutation thất bại (400) **không** sinh log; test frontend Board cũ vẫn PASS.

---

## 3. Backend – Signal detector (hàm thuần)

### 3.1 Contract (`Services/ObserverSignalDetector.cs`)

- [ ] Định nghĩa (mọi thứ `public` để verify thuần + làm điểm tựa test Giai đoạn 7):
  ```csharp
  public sealed record ObserverTaskSnapshot(
      Guid TaskId, Guid BoardId, Guid ColumnId, string Title,
      Guid? AssigneeId, string? AssigneeName,
      DateTimeOffset? DueDate, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
      bool IsDoneColumn, int CommentCount, DateTimeOffset? LastCommentAt);

  public sealed record ObserverColumnSnapshot(Guid ColumnId, Guid BoardId, string Name, bool IsDone);

  public sealed record ObserverWorkspaceSnapshot(
      Guid WorkspaceId, IReadOnlyList<ObserverTaskSnapshot> Tasks,
      IReadOnlyList<ObserverColumnSnapshot> Columns, DateTimeOffset Now);

  public sealed record ObserverSignal(
      string Type, string Severity, string Summary,
      IReadOnlyList<Guid> TaskIds, IReadOnlyList<Guid> UserIds);
  ```
- [ ] `public static IReadOnlyList<ObserverSignal> Analyze(ObserverWorkspaceSnapshot snapshot, ObserverThresholds thresholds)` — **thuần**, không đọc DB, không dùng `DateTime.Now` (nhận `Now` từ snapshot).

### 3.2 Công thức & ngưỡng (`ObserverThresholds` build từ `ObserverOptions`)

- [ ] `OverdueTask`: task **không** ở cột `is_done` **và** `DueDate < now`; `Severity = Critical` nếu `overdueDays > 2 × CriticalOverdueDays`, ngược lại `High`. Evidence `TaskIds` = các task quá hạn (cap), `UserIds` = assignee của chúng.
  - Biên: `DueDate == null` → bỏ qua; `dueDate == now` → **chưa** quá hạn; task ở cột `is_done` → bỏ qua.
- [ ] `StalledTask`: task **không** `is_done` **và** `max(UpdatedAt, LastCommentAt ?? CreatedAt) < now − StalledDays`; `Severity = High` nếu ≥ `2 × StalledDays`, ngược lại `Medium`.
- [ ] `Overload` (gom theo `AssigneeId`, bỏ task `is_done`): tín hiệu khi `OverdueCount ≥ OverloadOverdueMin` (mặc định 2) **hoặc** `OpenCount ≥ OverloadMinOpenTasks` (mặc định 5) — cộng thêm `OpenCount ≥ 2 × OverloadMinOpenTasks` ⇒ `Severity = Critical`, ngược lại `High`. `Summary` nêu số task mở / quá hạn / số task Urgent|High. `UserIds` = 1 assignee; `TaskIds` = task mở của người đó (cap).
- [ ] `Bottleneck` *(chỉ khi `BottleneckDetectionEnabled`)*: cột **không** `is_done` có `openCount ≥ BottleneckMinTasks` **và** có ≥ `BottleneckStalledMinTasks` task đứng yên > `StalledDays` ⇒ `Medium`. `TaskIds` = task đứng yên ở cột đó bị giới hạn theo board.
- [ ] Sắp xếp theo `Severity` giảm dần (`Critical > High > Medium > Low`) rồi theo độ "nặng" (số task quá hạn / số ngày đứng yên); **cap `MaxSignalsPerWorkspace`** (20) và **cap `MaxEvidenceIdsPerSignal`** (10) — trả về thêm `truncatedSignals` để ghi vào run summary.
- [ ] `ObserverSummaryBuilder` (thuần, cùng file hoặc `Services/ObserverSummarizer.cs`):
  - `ObserverWindow` (thuần): `windowStart = max(now − LookbackHours, lastCompletedRun.FinishedAt ?? now − LookbackHours)`; clamp để window ≤ `MaxLookbackDays`.
  - `BuildPrompt(request)` + `BuildPayload(request)`: **cắt** title/excerpt (`MaxTitleExcerptLength = 80`), giới hạn số signal đưa vào prompt, đảm bảo `payload.Length ≤ MaxPromptCharacters`, **không** chứa description đầy đủ của task.
- [ ] **Verify §3 (nhóm B §9.1):** ~14 case thuần — quá hạn đúng/không/sai cột/null/đúng mốc `now`; stalled theo `UpdatedAt` vs `LastCommentAt`; overload đúng ngưỡng (5 mở; 4 mở + 2 quá hạn; 4 mở + 0 quá hạn → **không** tín hiệu); bottleneck bật/tắt; cap signals/evidence; sort severity; `ObserverWindow`; prompt ≤ cap + redaction.

---

## 4. Backend – Observer service, AI contract, Notification

### 4.1 Options & config (`Options/ObserverOptions.cs`)

- [ ] Tạo `ObserverOptions` (pattern `DeepSeekOptions`: POCO bind từ section `Observer`, **không** `ValidateOnStart`):

  | Key | Mặc định | Ghi chú |
  |---|---|---|
  | `Enabled` | `true` | `false` ⇒ timer không chạy (vẫn cho phép quét tay) |
  | `IntervalMinutes` | `30` | 15–30 theo `02` §2.3; clamp 1..1440 |
  | `StartupDelaySeconds` | `60` | không tranh DB lúc cold start |
  | `LookbackHours` | `24` | cửa sổ tối thiểu để tính activity |
  | `MaxLookbackDays` | `7` | clamp cửa sổ khi app ngủ lâu |
  | `MaxWorkspacesPerRun` | `10` | kiểm soát chi phí mỗi vòng |
  | `CriticalOverdueDays` | `3` | ngưỡng quá hạn nặng |
  | `OverloadOverdueMin` | `2` | số task quá hạn để coi là quá tải |
  | `StalledDays` | `7` | ngưỡng "đứng yên" |
  | `OverloadMinOpenTasks` | `5` | số task mở để coi là quá tải |
  | `BottleneckDetectionEnabled` | `true` | tín hiệu optional |
  | `BottleneckMinTasks` | `5` | số task mở ở cột để coi là nghẽn |
  | `BottleneckStalledMinTasks` | `2` | số task đứng yên ở cột đó |
  | `MaxSignalsPerWorkspace` | `20` | cap tín hiệu/workspace |
  | `MaxEvidenceIdsPerSignal` | `10` | cap evidence mỗi tín hiệu |
  | `MaxPromptCharacters` | `12000` | **"tóm tắt trước khi gửi"** (tiêu chí roadmap) |
  | `MaxOutputTokens` | `1500` | cap chi phí riêng của Observer (thấp hơn Smart Setup) |
  | `Temperature` | `0.0` | Observer cần ổn định, không sáng tạo |
  | `MaxNotificationsPerRun` | `50` | cap notification/vòng |
  | `MaxManagersPerWorkspace` | `10` | cap fan-out Manager |
  | `DeduplicationWindowHours` | `24` | chống spam |
  | `RetentionDays` | `30` | retention `activity_logs` (`04` §7) |
  | `RetentionDeleteBatchSize` | `5000` | cap số row xoá/lần |
  | `MinSeverityToNotify` | `"Medium"` | `Low\|Medium\|High\|Critical` |
  | `MaxTitleExcerptLength` | `80` | redaction tiêu đề trong prompt |
  | `Interval` | computed | `TimeSpan.FromMinutes(clamp(IntervalMinutes,1,1440))` |

- [ ] Thêm section `"Observer"` vào `src/TeamNexus.Api/appsettings.json` (đầy đủ key như bảng, không chứa secret).
- [ ] Thêm hằng số severity/type (module Ai):
  ```csharp
  public static class NotificationSeverities { Low, Medium, High, Critical }  // có Rank để so sánh
  public static class NotificationTypes {
      OverdueTask, StalledTask, Overload, Bottleneck  // dùng cho notifications.type + findings.type
  }
  ```
- [ ] `ObserverThresholds` (record build từ `ObserverOptions`) để §3 thuần không phụ thuộc `IOptions`.
- [ ] Log lúc khởi động: `Observer: enabled={Enabled}, interval={Interval}, lookback={LookbackHours}h, dedupe={DeduplicationWindowHours}h` (không có secret).

### 4.2 Contract AI + prompt (`Contracts/ObserverAiModels.cs`, `Services/ObserverPrompts.cs`)

- [ ] Raw output (khớp **chặt** system prompt, `System.Text.Json`):
  ```csharp
  public sealed class AiObserverOutput
  {
      public List<AiObserverFinding> Findings { get; set; } = [];
  }
  public sealed class AiObserverFinding
  {
      public string? Type { get; set; }        // OverdueTask|StalledTask|Overload|Bottleneck
      public string? Severity { get; set; }    // Low|Medium|High|Critical
      public string? Title { get; set; }       // ≤ 200
      public string? Message { get; set; }     // diễn giải, ≤ 2000
      public List<Guid>? TaskIds { get; set; } // ⊆ evidence của tín hiệu
      public List<Guid>? UserIds { get; set; } // ⊆ evidence của tín hiệu
  }
  ```
- [ ] `ObserverPrompts.SystemPrompt` nêu rõ: chỉ dùng ID có trong `evidence`; **không** bịa task/user; **không** đề xuất hành động ghi dữ liệu; trả **đúng 1 object** `{ "findings": [ … ] }`; mỗi finding phải `type` ∈ 4 loại, `severity` ∈ 4 mức; giữ ngôn ngữ tiếng Việt; nếu không có gì đáng báo thì trả `findings: []`.
- [ ] `ObserverPrompts.BuildUserPrompt(payload)`: **dòng đầu** là `{"agent":"observer"}` rồi newline, sau đó JSON payload tín hiệu (marker cho `FakeAiProvider` — D9). Đảm bảo tổng độ dài ≤ `MaxPromptCharacters`.
- [ ] `Services/ObserverFindingValidator.cs` — **hàm thuần public static**:
  - `type` không thuộc `NotificationTypes` ⇒ **bỏ** finding.
  - `severity` sai/thiếu ⇒ mặc định `Medium`; dưới `MinSeverityToNotify` ⇒ **bỏ**.
  - `evidence.taskIds/userIds` = **giao** với ID của tín hiệu tương ứng ⇒ ID lạ bị loại; cap `MaxEvidenceIdsPerSignal`.
  - `title` 1–200 (rỗng ⇒ sinh title mặc định theo `type`; dài ⇒ cắt); `message` rỗng ⇒ sinh message mặc định; dài ⇒ **cắt** 2000 (không lỗi — đây là dữ liệu AI sinh, giống `decision_note` Phase 4 cap 500).
- [ ] `CompleteAsync(...)` dùng `JsonMode: true`, `Temperature`, `MaxOutputTokens` từ `ObserverOptions`; parse case-insensitive + bỏ code fence phòng thủ (tái dùng cách Phase 3 §4.3 bước 8) — **không retry** (tiết kiệm token ở Observer).

### 4.3 `FakeAiProvider` – nhánh Observer (D9)

- [ ] Trong `CompleteAsync`, kiểm tra `request.UserPrompt` có bắt đầu bằng marker `{"agent":"observer"}` (sau khi trim/khoan dung whitespace) ⇒ trả JSON mẫu:
  ```json
  { "findings": [
      { "type": "OverdueTask", "severity": "High",
        "title": "Có task quá hạn cần xử lý",
        "message": "Phát hiện task đã quá hạn ở bảng …, nên rà soát lại người phụ trách.",
        "taskIds": [ "<ID đầu tiên trong evidence>" ], "userIds": [ "<ID user đầu tiên, nếu có>" ] },
      { "type": "Overload", "severity": "Medium",
        "title": "Một thành viên đang có nhiều việc mở",
        "message": "Thành viên … đang giữ nhiều task mở cùng lúc.",
        "taskIds": [], "userIds": [ "<ID user đầu tiên trong evidence>" ] } ] }
  ```
  ID lấy bằng cách **parse payload `evidence`** từ chính prompt ⇒ luôn đi qua được `ObserverFindingValidator` mà không cần biết fixture.
- [ ] Không có marker ⇒ **giữ nguyên** hành vi Phase 3 (proposal mẫu) — có unit-check riêng cho nhánh này để tránh regression.
- [ ] Cập nhật docstring `FakeAiProvider` (mô tả 2 nhánh + marker).

### 4.4 `Services/NotificationService.cs` + `Services/ObserverNotificationFactory.cs`

- [ ] Interface:
  ```csharp
  public interface INotificationService
  {
      Task<int> NotifyManagersAsync(Guid workspaceId, IReadOnlyList<ObserverFinding> findings,
          ObserverRunContext context, CancellationToken ct = default);
      Task<NotificationListResponse> ListAsync(Guid userId, bool? isRead, int take, CancellationToken ct = default);
      Task<NotificationResponse> MarkReadAsync(Guid notificationId, Guid userId, CancellationToken ct = default);
      Task<int> MarkAllReadAsync(Guid userId, CancellationToken ct = default);
      Task<int> CountUnreadAsync(Guid userId, CancellationToken ct = default);
  }
  public sealed record ObserverFinding(string Type, string Severity, string Title, string Message,
      IReadOnlyList<Guid> TaskIds, IReadOnlyList<Guid> UserIds);
  public sealed record ObserverRunContext(Guid RunId, string Model, int? PromptTokens, int? CompletionTokens);
  ```
- [ ] `NotifyManagersAsync`: lấy `workspace_members` role `Manager`/`Admin` (cap `MaxManagersPerWorkspace`, sắp xếp ổn định theo `user_id`); **0 manager** ⇒ log Warning, trả 0 (không throw); với mỗi finding: **dedupe** (D12) → `AddRange` **1 row/người nhận** → 1 `SaveChangesAsync`; cap tổng `MaxNotificationsPerRun`.
- [ ] `ObserverNotificationFactory` (thuần public static): `BuildTitle`, `BuildMessage`, `BuildPayload(finding, ctx)` → JSON camelCase (`runId`, `boardId` nếu mọi evidence cùng board, `taskIds`, `userIds`, `severity`, `model`, `tokens`).
- [ ] **Dedupe ngữ nghĩa:** so `payload` bằng `JsonElement.DeepEquals` trên key `taskIds`/`userIds`/`signals` (KHÔNG so chuỗi — `jsonb` đã chuẩn hoá).
- [ ] `ListAsync`: `take` mặc định 20, clamp 1–100; `isRead` filter optional; sort `CreatedAt DESC`; `UnreadCount` = **tổng** chưa đọc của `userId` (không phụ thuộc `take`).
- [ ] `MarkReadAsync`: không thấy **hoặc** `RecipientUserId != userId` ⇒ **404** `"Notification not found."`; đã read ⇒ **idempotent** (200, giữ `ReadAt` gốc). `MarkAllReadAsync`: chỉ row của `userId` và `IsRead == false`.

### 4.5 `Services/IObserverService.cs` + `ObserverService.cs`

- [ ] Interface:
  ```csharp
  public interface IObserverService
  {
      Task<ObserverScanOutcome> ScanAsync(Guid? workspaceId, CancellationToken ct = default);
      Task<IReadOnlyList<ObserverRunResponse>> ListRunsAsync(Guid workspaceId, int take, Guid userId, CancellationToken ct = default);
      Task<ObserverRunDetailResponse> GetRunAsync(Guid runId, Guid userId, CancellationToken ct = default);
  }
  ```
- [ ] `ScanAsync` — đúng 9 bước ở §0 "Luồng tổng thể":
  1. `!Enabled && workspaceId is null` ⇒ return `Skipped` (`NoWorkspaces`/`Disabled`) **không ghi DB**. Quét tay (`workspaceId != null`) **vẫn chạy** dù `Enabled = false` (phục vụ demo/verify — ghi rõ trong docstring + README).
  2. `pg_try_advisory_lock(@key)` với hằng số cố định của module → không lấy được ⇒ `Skipped` (`AlreadyRunning`). Giải phóng lock trong `finally` (`pg_advisory_unlock`). Kèm `static readonly SemaphoreSlim(1,1)` trong process.
  3. Nạp workspace: `_db.Workspaces.Where(w => w.DeletedAt == null)` (+ `workspaceId` chỉ định); sắp xếp theo hoạt động gần nhất (`activity_logs.created_at` / `tasks.updated_at`) giảm dần; cap `MaxWorkspacesPerRun`.
  4. Nạp dữ liệu **bounded** 1 workspace: tasks (bỏ soft-delete + cột `is_done` không cần thiết cho detector nhưng vẫn nạp để tính bottleneck), comment count + `max(created_at)`, columns, run `Completed` gần nhất (`ObserverWindow`), số activity trong window.
  5. `ObserverSignalDetector.Analyze(...)`; rỗng ⇒ run `Completed` với `aiCalled=false`, 0 notification, token `null` (D6).
  6. `ObserverSummarizer.BuildRequest(...)` (prompt có marker observer, `JsonMode: true`).
  7. `_aiProvider.CompleteAsync(...)` → parse `AiObserverOutput`; lỗi parse/`AiProviderException` ⇒ run `Failed` (`summary.error` ≤ 500), **0 notification**.
  8. `ObserverFindingValidator.Validate(...)` ⇒ `ObserverFinding[]`; rỗng ⇒ run `Completed`, 0 notification.
  9. `NotifyManagersAsync(...)` → ghi `ai_observer_runs` (status `Completed`, `summary` đầy đủ) → prune `activity_logs` (D14) → `LogInformation` (workspace, signals, aiCalled, notificationsCreated, durationMs). **Không** log nội dung prompt/response (chỉ độ dài, mức `Debug`).
- [ ] `ListRunsAsync`: `RequireManagerAsync` (403) + workspace 404 nếu không thấy; sort `StartedAt DESC`; clamp `take` 1–50 (mặc định 20); project `SignalsDetected`/`FindingsWritten`/`NotificationsCreated`/`AiCalled` từ `summary` (parse phòng thủ: JSON hỏng ⇒ 0/false, không ném).
- [ ] `GetRunAsync`: run 404; dùng `run.WorkspaceId` + `RequireManagerAsync` ⇒ 403; trả `summary` dạng `JsonElement?` + `findings[]` (đọc từ `summary.findings`; hỏng JSON ⇒ `[]`).
- [ ] **Không** đụng `IAiActionService`/`IAiActionApplier` — Observer là lớp *đọc/cảnh báo* (contract bàn giao Phase 4 §6).
- [ ] **Verify §4.5:** mock provider lỗi ⇒ run `Failed`, 0 notification; JSON AI hỏng ⇒ `Failed`; `findings: []` ⇒ `Completed` + 0 notification; ID lạ bị loại.

### 4.6 `Services/ObserverBackgroundService.cs`

- [ ] `ObserverBackgroundService : BackgroundService`:
  - `ExecuteAsync`: chờ `StartupDelaySeconds` (tôn trọng `stoppingToken`) → nếu `!Enabled` ⇒ `LogInformation("Observer đang tắt (Observer:Enabled=false)")` rồi `return`.
  - Vòng lặp `PeriodicTimer(_options.Interval)`: mỗi tick tạo scope `IServiceScopeFactory.CreateAsyncScope()` → resolve `IObserverService` → `ScanAsync(null, stoppingToken)`.
  - **Bọc toàn bộ trong `try/catch`** + `LogError`: exception thoát ra khỏi `BackgroundService` sẽ làm **host dừng** (mặc định `BackgroundServiceExceptionBehavior = StopHost`) — tuyệt đối không để xảy ra.
  - Log đầu/kết: `Observer background service started (interval=…m, delay=…s)`.
- [ ] **Verify §4.6 (nhóm C §9.1):** `Observer:IntervalMinutes=1`, `StartupDelaySeconds=5` ⇒ có ≥1 run trong ~70s; `Enabled=false` ⇒ 0 run; stop app giữa run ⇒ **không** có row `Running` treo (`FinishedAt` luôn được set nhờ `try/finally`).

---

## 5. Backend – DTO, Endpoints, DI

### 5.1 DTO (`DTOs/ObserverDtos.cs`, `DTOs/NotificationDtos.cs`)

- [ ] ```csharp
  public sealed record NotificationResponse(
      Guid Id, Guid WorkspaceId, string Type, string Title, string Message,
      JsonElement? Payload, bool IsRead, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

  public sealed record NotificationListResponse(int UnreadCount, IReadOnlyList<NotificationResponse> Items);

  public sealed record ObserverRunResponse(
      Guid Id, Guid WorkspaceId, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
      int SignalsDetected, int FindingsWritten, int NotificationsCreated, bool AiCalled);

  public sealed record ObserverRunDetailResponse(
      Guid Id, Guid WorkspaceId, string Status, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt,
      JsonElement? Summary, IReadOnlyList<ObserverRunFinding> Findings);

  public sealed record ObserverRunFinding(
      string Type, string Severity, string Title, string Message,
      IReadOnlyList<Guid> TaskIds, IReadOnlyList<Guid> UserIds);

  public sealed record ObserverScanResponse(
      Guid RunId, Guid WorkspaceId, string Status, int SignalsDetected,
      int FindingsWritten, int NotificationsCreated, bool AiCalled);
  ```
- [ ] Cả `Payload` và `Summary` để `JsonElement?` (parse **phòng thủ**: JSON hỏng ⇒ `null` thay vì 500) — đúng tiền lệ Phase 4 §3.1.
- [ ] `Status` serialize giữ casing canonical (`"Completed"`, `"Skipped"`…) — không đổi sang lowercase (đúng `TaskPriority`/`AiActionStatus`).

### 5.2 `Endpoints/NotificationEndpoints.cs`

- [ ] Group `/api/notifications` `.WithTags("Notifications")` + `DomainExceptionFilter`; mọi route `.RequireAuthorization()`:

  | Method + path | Quyền | Thành công | Ghi chú |
  |---|---|---|---|
  | `GET /?isRead=&take=` | user đã đăng nhập (**chỉ row của mình**) | `200 NotificationListResponse` | `take` mặc định 20, clamp 1–100; `isRead` strict parse |
  | `POST /{notificationId:guid}/read` | owner | `200 NotificationResponse` | 404 nếu không phải của mình; idempotent |
  | `POST /read-all` | user đã đăng nhập | `200 { "updated": n }` | chỉ row của mình |

- [ ] POST thêm `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()`; `GET` **không** gắn CSRF (tiền lệ Phase 4 §3.2).
- [ ] `isRead` parse **strict** (`true`/`false`, chấp nhận mọi casing; giá trị khác ⇒ **400** `{ error }`) — cùng tinh thần `ParseStatus` Phase 4.

### 5.3 `Endpoints/ObserverEndpoints.cs`

- [ ] Group `.WithTags("AiObserver")` + `DomainExceptionFilter`; mọi route `.RequireAuthorization()`:

  | Method + path | Quyền | Thành công | Ghi chú |
  |---|---|---|---|
  | `POST /api/workspaces/{workspaceId:guid}/observer/scan` | Manager/Admin | `200 ObserverScanResponse` | Quét **đồng bộ** (foreground) — kết quả trả ngay; 403 Member; 404 workspace không thấy; **502** nếu provider lỗi (run đã ghi `Failed`, không notification) |
  | `GET /api/workspaces/{workspaceId:guid}/observer/runs?take=20` | Manager/Admin | `200 ObserverRunResponse[]` | sort `startedAt DESC`; `take` clamp 1–50 |
  | `GET /api/observer/runs/{runId:guid}` | Manager/Admin của workspace của run | `200 ObserverRunDetailResponse` | 404 nếu không thấy/không quyền |

- [ ] POST `scan` thêm `AntiforgeryValidationEndpointFilter`.
- [ ] Đọc `userId` bằng `AiEndpointHelpers.RequireUserId(http)` (đã có từ Phase 4).
- [ ] Member (không phải Manager/Admin) trên **cả 3** route ⇒ **403** `"Requires Manager or Admin role in this workspace."` — đây là tiêu chí roadmap "chỉ Manager thấy cảnh báo".

### 5.4 Đăng ký DI (`AiModule.cs`)

- [ ] `services.AddOptions<ObserverOptions>().Bind(configuration.GetSection(ObserverOptions.SectionName));`
- [ ] `services.AddScoped<IActivityLogWriter, ActivityLogWriter>();`
- [ ] `services.AddScoped<INotificationService, NotificationService>();`
- [ ] `services.AddScoped<IObserverService, ObserverService>();`
- [ ] `services.AddHostedService<ObserverBackgroundService>();`
- [ ] `MapAiModuleEndpoints()` thêm `endpoints.MapObserverEndpoints();` + `endpoints.MapNotificationEndpoints();` (giữ 2 dòng cũ).
- [ ] `Program.cs` **không phải sửa** (extension point đã có từ Phase 3).
- [ ] **Verify §5:** `dotnet build TeamNexus.sln` → **0 warning / 0 error**; boot API cả hai nhánh (`DeepSeek:ApiKey` rỗng/có) → `GET /api/health` 200 + log `Observer background service started`; `Observer:Enabled=false` ⇒ log tắt rõ ràng; Scalar hiển thị 2 tag mới `AiObserver`/`Notifications`.

### 5.5 Mã lỗi & contract cho UI (body lỗi luôn `{ "error": "…" }`, trừ 400 do framework)

| Status | Khi nào | Gợi ý UI |
|---|---|---|
| 400 | `?isRead=`/`?take=` sai kiểu; tham số không hợp lệ | `Alert` lỗi, giữ nguyên filter đang chọn |
| 401 | hết phiên | `httpClient` tự refresh; vẫn lỗi → điều hướng login |
| 403 | Member gọi route Observer (không phải Manager/Admin); thiếu CSRF | Ẩn nút cho Member; nếu vẫn gặp → `Alert` "Cần quyền Manager/Admin" |
| 404 | workspace/run/notification không thấy hoặc không thuộc quyền | `Alert` + đóng drawer + reload |
| 502 | DeepSeek lỗi/timeout/JSON hỏng trong lúc quét | `Alert` "AI tạm thời không phản hồi — đã ghi nhận lần chạy thất bại" (run `Failed`, **không** tạo notification) |
| 400 (framework) | body là **ProblemDetails** (không có `error`), ví dụ `?take=abc` | chỉ cần xử lý theo status 400 |

---

## 6. Frontend – `src/features/ai/` (bàn giao antigravity)

> ## 🔻 BÀN GIAO §6 + §7.2 CHO ANTIGRAVITY (backend đã verify xong — điền số liệu thật sau §7.1)
>
> Backend Giai đoạn 5 (schema + activity log + `ObserverService` + BackgroundService + notification API) sẽ **không đổi nữa** sau §7.1. Cứ code theo đúng contract dưới đây.
>
> ### 1) Sáu endpoint (base `/api`, dùng `frontend/src/shared/api/httpClient.ts`)
> | # | Method + path | Body | Thành công | Ghi chú |
> |---|---|---|---|---|
> | 1 | `GET /notifications?isRead=&take=` | — | **200** `NotificationListResponse` | `isRead` ∈ `true\|false` (chữ thường chấp nhận, số/khác ⇒ 400); `take` 1–100, mặc định 20, thiếu/`0` ⇒ 20; chỉ trả row của chính mình; sort `createdAt DESC`; `unreadCount` = tổng chưa đọc (không phụ thuộc `take`) |
> | 2 | `POST /notifications/{id}/read` | — | **200** `NotificationResponse` | Idempotent; notification của người khác ⇒ **404** |
> | 3 | `POST /notifications/read-all` | — | **200** `{ updated: number }` | Chỉ row của mình |
> | 4 | `POST /workspaces/{workspaceId}/observer/scan` | — | **200** `ObserverScanResponse` | **Đồng bộ** (chờ AI xong mới trả, có thể ~2–5s với DeepSeek thật; ~100ms với `FakeAiProvider`); **bỏ qua** `Observer:Enabled`; Manager/Admin; 502 nếu AI lỗi |
> | 5 | `GET /workspaces/{workspaceId}/observer/runs?take=20` | — | **200** `ObserverRunResponse[]` | Manager/Admin; sort `startedAt DESC`; `take` 1–50 |
> | 6 | `GET /observer/runs/{runId}` | — | **200** `ObserverRunDetailResponse` | Manager/Admin của workspace của run; kèm `summary` + `findings[]` |
>
> `httpClient` **tự** gắn `X-XSRF-TOKEN` cho POST ⇒ **không** tự set header, không tự gọi `/auth/antiforgery`.
>
> ### 2) Kiểu TS cần mirror (viết tay theo `DTOs/NotificationDtos.cs` + `DTOs/ObserverDtos.cs`)
> ```ts
> export type NotificationType = 'OverdueTask' | 'StalledTask' | 'Overload' | 'Bottleneck'
> export type NotificationSeverity = 'Low' | 'Medium' | 'High' | 'Critical'
> export type ObserverRunStatus = 'Running' | 'Completed' | 'Skipped' | 'Failed'
>
> export interface NotificationPayload {
>   runId?: string
>   boardId?: string | null
>   taskIds?: string[]
>   userIds?: string[]
>   severity?: NotificationSeverity
>   model?: string | null
>   tokens?: number | null
>   [key: string]: unknown
> }
> export interface NotificationResponse {
>   id: string; workspaceId: string
>   type: NotificationType | string
>   title: string; message: string
>   payload: NotificationPayload | null
>   isRead: boolean
>   createdAt: string; readAt: string | null
> }
> export interface NotificationListResponse {
>   unreadCount: number
>   items: NotificationResponse[]
> }
> export interface ObserverRunResponse {
>   id: string; workspaceId: string; status: ObserverRunStatus
>   startedAt: string; finishedAt: string | null
>   signalsDetected: number; findingsWritten: number
>   notificationsCreated: number; aiCalled: boolean
> }
> export interface ObserverRunFinding {
>   type: string; severity: string; title: string; message: string
>   taskIds: string[]; userIds: string[]
> }
> export interface ObserverRunDetailResponse extends ObserverRunResponse {
>   summary: unknown | null
>   findings: ObserverRunFinding[]
> }
> export interface ObserverScanResponse {
>   runId: string; workspaceId: string; status: ObserverRunStatus
>   signalsDetected: number; findingsWritten: number
>   notificationsCreated: number; aiCalled: boolean
> }
> ```
>
> ### 3) JSON thật (rút gọn) — điền từ lần verify §7.1, dùng làm fixture test
> ```json
> // GET /api/notifications?take=20
> { "unreadCount": 2, "items": [ {
>     "id": "…", "workspaceId": "…", "type": "OverdueTask",
>     "title": "Có task quá hạn cần xử lý",
>     "message": "Phát hiện 3 task quá hạn ở bảng …",
>     "payload": { "runId": "…", "boardId": "…", "taskIds": ["…"], "severity": "High",
>                  "model": "fake", "tokens": null },
>     "isRead": false, "createdAt": "2026-…", "readAt": null } ] }
>
> // POST /api/workspaces/{wsId}/observer/scan
> { "runId": "…", "workspaceId": "…", "status": "Completed", "signalsDetected": 3,
>   "findingsWritten": 2, "notificationsCreated": 4, "aiCalled": true }
> ```
>
> ### 4) Body lỗi (luôn `{ "error": "…" }` — trừ 400 do framework)
> | Status | `error` thật | UI nên làm |
> |---|---|---|
> | 400 | `"Unknown value for isRead 'bogus'. Expected true or false."` | `Alert` lỗi, giữ nguyên filter |
> | 403 | `"Requires Manager or Admin role in this workspace."` / `"CSRF token missing or invalid …"` | Ẩn/không render phần Observer cho Member; `Alert` khi vẫn gặp |
> | 404 | `"Workspace not found or you are not a member."` / `"Observer run not found."` / `"Notification not found."` | `Alert` + reload + đóng chi tiết |
> | 502 | `"DeepSeek …"` / `"Không parse được JSON từ AI …"` | `Alert` "AI tạm thời không phản hồi"; run đã ghi `Failed` — **không** có notification mới |
> | 400 (framework) | ProblemDetails (không có `error`), ví dụ `?take=abc` | xử lý theo status 400 |
>
> ### 5) Điểm cần chú ý khi code UI (đã chốt, đừng code sai kỳ vọng)
> - **Quét tay là đồng bộ** → nút "Quét ngay" phải `loading` + disable tới khi nhận response (đừng giả định job nền rồi poll).
> - **`Observer:Enabled=false` không chặn quét tay** — nút vẫn hoạt động.
> - Notification **chỉ** đến Manager/Admin; Member mở `/api/notifications` nhận `unreadCount: 0` (không lỗi).
> - `payload` là JSON **tuỳ ý** (đọc optional, dùng `payload?.severity`, `payload?.boardId`) — **không** so sánh payload bằng string.
> - `status` của run có thể là `Skipped` (khi một lần quét khác đang chạy) — UI phải render được cả 4 trạng thái.
> - `findings` có thể rỗng dù `signalsDetected > 0` (AI bị lọc bởi validator / severity thấp hơn `MinSeverityToNotify`) ⇒ hiển thị "Không có cảnh báo nào được ghi".
> - **Kênh gửi hiện chỉ in-app**; email là hạng mục *tương lai* — **không** hiện UI cấu hình email.
> - Chỉ có **3 route Observer** là Manager-only; đừng gate route notification theo role (bản thân API đã lọc theo người nhận).
>
> ### 6) Hướng dẫn §7.2 (test) — theo đúng pattern đang có
> - Mock `httpClient` (axios instance) giống `boardApi.test.ts`, **không** dựng backend: assert method/URL/params cho 6 hàm.
> - `useNotifications`: `idle → loading → idle`; `reload` set `items` + `unreadCount`; `markRead` cập nhật đúng 1 phần tử + giảm `unreadCount`; `markAllRead` set tất cả `isRead = true` + `unreadCount = 0`; poll chỉ chạy khi tab visible (fake timers).
> - `useObserverRuns`: `triggerScan` → `scanning` → `reload`; state `running`/`skipped`/`failed` map đúng message.
> - `NotificationDrawer`/`NotificationItem`: render đủ `Read`/`Unread`, `Segmented` filter gọi lại API với `isRead`, `Empty` khi rỗng, nút "Đã đọc" đúng trạng thái, hiển thị `readAt`.
> - `ObserverRunsDrawer`/`ObserverFindingCard`: render 4 `status`, nút "Quét ngay" gọi API, hiển thị `signalsDetected`/`notificationsCreated`/`aiCalled`/token, findings có severity + evidence.
> - `BoardView.test.tsx`: nút "Cảnh báo AI" tồn tại + `Badge` hiển thị `unreadCount`.
> - DoD: `npm run lint` (oxlint 0 warn/0 err) + `npx tsc -b` + `npm run build` + `npm test` **sạch** (§7.2).
>
> ### 7) Sau khi xong §6 + §7.2
> - Tick các checkbox §6.1–§6.4 và §7.2 trong file này.
> - Bổ sung ảnh chụp (drawer cảnh báo + observer runs) vào `Project-Documents/report/phase-5-ai-observer-test-report.md` (mục "Phần UI — bổ sung sau §6").
> - Xoá dòng "Còn §7.2 (UI gọi API thật) do antigravity" ở §7.3 sau khi test xong.

### 6.1 Types, API, hook

- [ ] `types/notification.types.ts`: mirror §6 mục 2 (`NotificationType`, `NotificationSeverity`, `ObserverRunStatus`, `NotificationPayload`, `NotificationResponse`, `NotificationListResponse`, `ObserverRunResponse`, `ObserverRunFinding`, `ObserverRunDetailResponse`, `ObserverScanResponse`).
- [ ] `services/notificationApi.ts` (qua `httpClient` base `/api`; **không** tự set `X-XSRF-TOKEN`):
  - `listNotifications(params?: { isRead?: boolean; take?: number }): Promise<NotificationListResponse>`
  - `markNotificationRead(notificationId: string): Promise<NotificationResponse>`
  - `markAllNotificationsRead(): Promise<{ updated: number }>`
- [ ] `services/observerApi.ts`:
  - `triggerObserverScan(workspaceId: string): Promise<ObserverScanResponse>`
  - `listObserverRuns(workspaceId: string, take?: number): Promise<ObserverRunResponse[]>`
  - `getObserverRun(runId: string): Promise<ObserverRunDetailResponse>`
- [ ] `hooks/useNotifications.ts`: state `{ items, unreadCount, status: 'idle'|'loading'|'marking', error, httpStatus }`; `reload(isRead?)`, `markRead(id)`, `markAllRead()`, `refreshUnreadCount()`; poll `unreadCount` mỗi **60s** chỉ khi `document.visibilityState === 'visible'` (clear interval trong cleanup); map lỗi 401/403/404. **Không** phụ thuộc SignalR (D17).
- [ ] `hooks/useObserverRuns.ts`: state `{ runs, selectedRun, status: 'idle'|'loading'|'scanning', error, httpStatus }`; `reload()`, `openRun(runId)`, `closeRun()`, `triggerScan()` (sau khi xong: `reload()` + callback `onScanFinished?.()` để refresh notification).

### 6.2 Components

- [ ] `components/NotificationDrawer.tsx`: `Drawer` Ant Design mở từ top bar `BoardView`; gọi `useNotifications.reload()` khi mở; `Segmented` (Tất cả / Chưa đọc) + nút **"Đánh dấu tất cả đã đọc"** + `List` các `NotificationItem` + `Empty`/`Spin`/`Alert` lỗi.
- [ ] `components/NotificationItem.tsx`: `Tag` theo `type` (màu theo `severity` đọc từ payload) + `Badge` chưa đọc + `Collapse` chi tiết (`message`, thời gian `dayjs`, `readAt` khi đã đọc) + link **"Mở bảng"** (`navigate('/workspaces/{wsId}/boards/{boardId}')` khi payload có `boardId`) + nút **"Đã đọc"**.
- [ ] `components/ObserverRunsDrawer.tsx` (Manager/Admin): nút **"Quét ngay"** (`Popconfirm` → `triggerScan`, `loading` + disable khi `scanning`) + bảng runs (`Tag` status, `signalsDetected`, `notificationsCreated`, `aiCalled`, token/thời lượng từ `summary` — đọc optional) + panel chi tiết run (`ObserverFindingCard[]`, `Empty` khi rỗng) + ghi chú "Chỉ Manager/Admin thấy cảnh báo này".
- [ ] `components/ObserverFindingCard.tsx`: card 1 finding (severity Tag, `title`, `message`, danh sách evidence dạng ID rút gọn + `Tooltip`).

### 6.3 Tích hợp vào Board

- [ ] `BoardView.tsx`: thêm nút **"Cảnh báo AI"** (icon `BellOutlined`) + `Badge count={unreadCount}` (ẩn khi 0) cạnh nút "Lịch sử AI"; state `notificationOpen` + `observerRunsOpen`; render `NotificationDrawer` (truyền `workspaceId`) và `ObserverRunsDrawer` (nút mở đặt cạnh nút "Cảnh báo AI", **ẩn với Member** nếu đọc được role từ `GET /api/workspaces`); `onScanFinished={() => { notification.reload(); refetch() }}`.
- [ ] `features/ai/index.ts`: export thêm types/api/hooks/components mới.
- [ ] **Không** sửa `useBoard`/`boardStore`/`useBoardHub`/`httpClient` (D17).

### 6.4 Verify §6

- [ ] `npm run lint` (oxlint 0 warning/error) + `npx tsc -b` + `npm run build` + `npm test` — tất cả sạch.

---

## 7. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–4)

### 7.1 Verify backend (harness tạm ngoài workspace + API thật + PostgreSQL thật)

> Cùng cách Phase 3 §Verify §4 / Phase 4 §5.1: harness đặt **ngoài workspace** (`%TEMP%`), tự ký JWT bằng `Jwt:SigningKey` trong User Secrets, **xoá sau khi chạy**, không commit. `DeepSeek__ApiKey` để trống ⇒ `FakeAiProvider` (**không gọi AI thật, không tốn token**). Fixture tự tạo rồi hard-delete; DB về baseline; API đã stop; harness đã xoá.
>
> **Trạng thái: 🟡 ĐANG CHẠY — §7.1 nhóm A (schema & migration) đã **28/28 PASS**; các nhóm B–K sẽ chạy khi §2–§5 hoàn tất.** Số liệu đầy đủ điền vào `Project-Documents/report/phase-5-ai-observer-test-report.md`.

**Ma trận check (mã hoá để báo cáo giống Phase 4):**

1. **A. Schema & migration (A1–A9) — ✅ 28/28 PASS (PostgreSQL 18.4, DB `TeamNexus`):** 3 bảng đủ cột/thứ tự/kiểu (`activity_logs` 10, `notifications` 11, `ai_observer_runs` 8); 3 cột **jsonb**; CHECK `ck_ai_observer_runs_status` đủ 4 giá trị + `status` = `varchar(16)`; 6 index (3 khai báo + 3 PK/index FK); **6 FK đều `ON DELETE RESTRICT`** (`confdeltype='r'`); FK RESTRICT chặn thật khi xoá `workspaces`/`users` còn bị tham chiếu → SQLSTATE **`23001`**; `status='Bogus'` → **`23514`**; jsonb nhận chuỗi không hợp lệ → **`22P02`**; round-trip insert→read đúng `CreatedAt/UpdatedAt` stamp, `IsRead=false`, `ReadAt=null`, `Skipped` đọc đúng enum; `dotnet ef migrations list` → **5 migration applied** (`20260910105154_Phase5AiObserverSchema`), 8 file migration cũ **zero-diff**, snapshot **+227/−0**; `ai_action_logs` vẫn đủ 15 cột; DB dev về baseline sau cleanup (3 bảng mới `0 row`).
2. **B. Hàm thuần (B1–B14):** detector quá hạn (đúng/sai cột `is_done`/`DueDate=null`/`dueDate == now`), stalled theo `UpdatedAt` vs `LastCommentAt`, overload đúng ngưỡng (5 mở; 4 mở + 2 quá hạn; 4 mở + 0 quá hạn ⇒ không tín hiệu), bottleneck bật/tắt, cap `MaxSignalsPerWorkspace`/`MaxEvidenceIdsPerSignal`, sort severity, `ObserverWindow` (last run vs lookback, clamp `MaxLookbackDays`), prompt ≤ `MaxPromptCharacters` + **redaction** title (không có description đầy đủ), `BuildPayload` round-trip, `ObserverFindingValidator` (type lạ bị bỏ, severity sai ⇒ Medium, evidence ID lạ bị giao, message > 2000 bị cắt), `AiObserverOutput` parse case-insensitive + bỏ code fence, `FakeAiProvider` nhánh observer (marker) vs nhánh proposal (không marker).
3. **C. BackgroundService (C1–C3):** `IntervalMinutes=1` + `StartupDelaySeconds=5` ⇒ ≥1 run trong ~70s có row `ai_observer_runs`; `Enabled=false` ⇒ **0** run; stop app giữa run ⇒ không có row `Running` treo.
4. **D. Scan end-to-end (D1–D15):** fixture (workspace 2 Manager + 1 Member, 1 board 3 cột có 1 `is_done`, ~10 task gồm 1 task quá hạn 5 ngày, 1 task đứng yên 10 ngày, 1 assignee 6 task mở) → `POST .../observer/scan` **200**, `status = "Completed"`, `signalsDetected ≥ 3`, `findingsWritten ≥ 1`, `notificationsCreated = findings × 2`; `notifications.recipient_user_id` **chỉ** 2 Manager + `is_read = false`; `tasks`/`labels`/`task_labels`/`ai_action_logs` **không đổi** (AC5); Member gọi `GET .../observer/runs` ⇒ **403** và `GET /notifications` ⇒ **200 nhưng 0 row** (AC4); workspace không tồn tại ⇒ **404**; thiếu auth ⇒ **401**; thiếu CSRF ⇒ **403**.
5. **E. Kiểm soát token/chi phí (E1–E3):** workspace **không tín hiệu** ⇒ run `Completed` + `aiCalled=false` + `promptTokens/completionTokens = null` + **0** notification (AC2); workspace có tín hiệu ⇒ `aiCalled=true` + token có giá trị trong `summary`; prompt thực gửi ≤ `MaxPromptCharacters` và không chứa description đầy đủ (bắt qua `HttpMessageHandler` stub hoặc `Debug` log).
6. **F. Notification API (F1–F8):** `GET /notifications` sort `createdAt DESC` + `unreadCount` đúng; `?isRead=true/false` lọc đúng; `?isRead=bogus` ⇒ **400** `{ error }`; `take` clamp 1–100 + `take=0` ⇒ default; `POST /{id}/read` ⇒ `isRead=true` + `readAt` set, gọi lần 2 vẫn 200 và **không đổi** `readAt`; đọc notification của user khác ⇒ **404**; `POST /read-all` ⇒ `updated` đúng số row chưa đọc của mình; thiếu CSRF ⇒ **403**; thiếu auth ⇒ **401**.
7. **G. Activity log (G1–G6):** create/update/move→Done/delete/comment qua API thật ⇒ mỗi thao tác sinh đúng 1 row (move vào cột `is_done` sinh `TaskMoved` **+** `TaskCompleted`) với `action`/`entity_type`/`entity_id`/`payload` đúng; task soft-deleted **vẫn** có log; mutation thất bại (400 validate) **không** sinh log; board/column cũ vẫn hoạt động như trước (regression Phase 2).
8. **H. Dedupe & retention (H1–H4):** quét 2 lần liên tiếp ⇒ `notifications` **không** tăng ở lần 2; đổi `type`/entity ⇒ tạo mới; seed 1 row `activity_logs` cũ hơn `RetentionDays` ⇒ bị prune sau scan, row mới còn nguyên; prune không vượt `RetentionDeleteBatchSize`.
9. **I. Concurrency (I1–I2):** 2 request `scan` đồng thời ⇒ 1 chạy thật, 1 trả `Skipped`/`AlreadyRunning` (không notification trùng, không deadlock); advisory lock được giải phóng sau run (scan lần sau chạy bình thường).
10. **J. Failure modes (J1–J4):** provider lỗi (stub 502) ⇒ run `Failed` + `summary.error` ≤ 500 + **0** notification + workspace khác không bị ảnh hưởng; JSON AI hỏng ⇒ `Failed`; AI trả `findings: []` ⇒ `Completed` + 0 notification; AI trả ID lạ ⇒ bị validator loại.
11. **K. Kiểm tra kiến trúc (K1–K3):** `grep` trong `src/Modules/Ai` không có đường ghi `Tasks`/`Labels`/`TaskLabels` mới ngoài `CreateSubtasksApplier` (Phase 4 §5.1 bước 10 giữ nguyên kết quả); `Board.csproj` **không** thêm project reference nào; Observer **không** gọi `IAiActionService`.
12. [ ] *(Tuỳ chọn)* Tạo `Project-Documents/report/phase-5-ai-observer-test-report.md` (đúng format Phase 4: môi trường, phương pháp, bảng check có mã + bằng chứng định lượng, known gaps).

### 7.2 Frontend (Vitest + Testing Library)

- [ ] Test `notificationApi` + `observerApi`: đúng method/URL/params cho 6 hàm.
- [ ] Test `useNotifications` (reload/poll/filter/markRead/markAllRead, lỗi 401/403), `useObserverRuns` (`triggerScan` → `scanning` → reload).
- [ ] Test `NotificationDrawer` + `NotificationItem`: 2 filter, `Empty`, trạng thái đã đọc/chưa đọc, `readAt`, link "Mở bảng".
- [ ] Test `ObserverRunsDrawer` + `ObserverFindingCard`: render 4 `status`, nút "Quét ngay" gọi API, hiển thị findings/severity/evidence/token.
- [ ] Cập nhật `BoardView.test.tsx`: nút "Cảnh báo AI" tồn tại; `Badge` hiển thị `unreadCount`.
- [ ] DoD: `oxlint` 0 warn/0 err + `tsc -b` + `vite build` + `vitest run` **sạch**.

### 7.3 Điều kiện hoàn thành Giai đoạn 5 (map `03-roadmap.md`)

- [ ] `BackgroundService` chạy nền, quét log/hoạt động board theo **chu kỳ cố định** — verify §7.1 nhóm C (C1–C3) + log khởi động.
- [ ] Log **được tóm tắt trước khi gửi DeepSeek** (kiểm soát token/chi phí) — verify §7.1 nhóm E (E1–E3): prompt ≤ `MaxPromptCharacters`, 0 lần gọi AI khi không có tín hiệu, token ghi vào `ai_observer_runs.summary`.
- [ ] Phát hiện **ít nhất 1 loại tín hiệu** — verify §7.1 nhóm B + D: 3 loại `OverdueTask`/`StalledTask`/`Overload` (+ `Bottleneck` optional).
- [ ] Kết quả cảnh báo **chỉ hiển thị cho Manager, không public toàn team** — verify §7.1 nhóm D (Member 403 ở route Observer; notification chỉ gửi Manager/Admin) + verify UI §7.2.
- [ ] *(Còn §7.2 — UI gọi API thật) do antigravity.*

---

## 8. Edge cases, failure modes & bàn giao Giai đoạn 6/7

**Edge cases / failure modes:**
- **Workspace không có tín hiệu** → run `Completed`, `aiCalled = false`, 0 notification, **0 token** (không gọi AI thừa).
- **AI lỗi/timeout/JSON hỏng** → run `Failed` + `summary.error` (≤ 500); **không** notification; các workspace khác trong cùng vòng quét vẫn chạy.
- **AI trả ID không tồn tại / `type` lạ / severity thấp hơn `MinSeverityToNotify`** → bị `ObserverFindingValidator` loại; `findings` rỗng sau validate ⇒ `Completed` + 0 notification (không im lặng tạo rác).
- **AI trả `message` quá dài** → **cắt** 2000 (không lỗi — dữ liệu do AI sinh); `title` rỗng ⇒ sinh title mặc định theo `type`.
- **Đã cảnh báo cùng loại cho cùng entity trong `DeduplicationWindowHours`** → bỏ qua, không spam.
- **2 instance/timer chạy chồng** → advisory lock ⇒ run `Skipped`, 0 notification trùng; lock luôn được giải phóng trong `finally`.
- **Board/task/workspace bị soft-delete giữa chừng** → detector bỏ qua (global query filter của `Boards`/`Tasks` + đánh giá `is_done` tại thời điểm quét); run vẫn `Completed`.
- **Manager rời workspace sau khi run xong** → notification cũ giữ nguyên (lịch sử); `GET /notifications` chỉ trả row của chính người đang đăng nhập (không lộ workspace họ không còn thuộc).
- **Assignee/user bị xoá cứng** → payload giữ GUID; UI hiển thị "Không xác định" khi không resolve được tên (không crash).
- **Workspace không có Manager/Admin** → log Warning, 0 notification, run vẫn `Completed` (`notificationsCreated = 0`).
- **`Observer:Enabled = false`** → timer không chạy; **quét tay vẫn chạy** (phục vụ demo/verify) — ghi rõ trong README + verify C2.
- **DB lỗi/timeout khi quét** → run `Failed`; `BackgroundService` catch **toàn bộ** để không làm host dừng (`BackgroundServiceExceptionBehavior` mặc định `StopHost`); lần chạy sau tiếp tục bình thường.
- **Prompt vượt `MaxPromptCharacters`** → cắt theo severity ưu tiên + ghi `truncatedSignals` vào `summary` (không gửi prompt khổng lồ).
- **Cold start / app sleep (free-tier Render/Railway)** → Observer chỉ chạy khi app thức ⇒ có thể bỏ lỡ chu kỳ; **known limitation**, xử lý ở Giai đoạn 7 (cron ngoài hoặc keep-alive).
- **`take`/`isRead` sai giá trị** → `take` clamp (không lỗi), `isRead` **400** `{ error }` (strict parse, không nhận giá trị số/lạ).
- **Token/chi phí:** `MaxOutputTokens` riêng (1500) thấp hơn Smart Setup; `Temperature = 0`; **không retry** ở tầng Observer; **không** gọi AI khi 0 tín hiệu; token lưu trong `ai_observer_runs.summary` để theo dõi chi phí.

**Bàn giao Giai đoạn 6 (Reporting):**
- `activity_logs` (`action`, `payload`, `created_at`, index `(workspace_id, created_at)`) là nguồn sự kiện thô cho thống kê tiến độ/hiệu suất; `ai_observer_runs.summary` là nguồn "sức khoẻ dự án" theo thời gian.
- `notifications` là kênh duy nhất để đẩy cảnh báo tới Manager — nếu Giai đoạn 6/7 thêm kênh khác (email), thêm implementation mới bên cạnh `INotificationService` (gợi ý `INotificationChannel`), **không** sửa Observer.

**Bàn giao Giai đoạn 7 (Test & Deploy):**
- Harness verify §7.1 là **điểm tựa** để chuyển thành test xUnit (detector/validator/summary builder là hàm `public static`).
- `Observer:Enabled=false` là công tắc an toàn cho CI; cân nhắc external cron/keep-alive để Observer chạy đúng chu kỳ trên free-tier.

---

## 9. Tài liệu

- [x] Tạo `Project-Documents/tasks/phase-5-ai-observer.md` (file này — checklist như trên).
- [x] Cập nhật `Project-Documents/04-database-design.md`: §3.6 (3 bảng sau khi tinh chỉnh: `payload`/`summary` jsonb, `activity_logs` append-only **không** query filter, `notifications` fan-out theo Manager + `read_at`, `ai_observer_runs.status` thêm `Skipped`), §4 (`ObserverRunStatus`/`NotificationSeverity`), §5 (index mới), §7 (retention + advisory lock + ghi chú Observer không ghi dữ liệu nghiệp vụ) — *đã làm ở bước lập kế hoạch, theo tiền lệ Phase 4 §1.4*.
- [ ] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`: mục **"Phase 5 — AI Observer"** (bảng 6 endpoint, options `Observer`, công thức 4 tín hiệu, luồng 9 bước của `ObserverService`, dedupe 24h, advisory lock, retention, `FakeAiProvider` marker, kênh in-app + email là *tương lai*, kết quả verify §7.1).
- [ ] Cập nhật `src/Modules/Board/TeamNexus.Modules.Board/README.md`: ghi chú Board phát `IActivityLogWriter` ở 5 mutation (create/update/move/delete task + add comment), **không** đổi API/quyền công khai.
- [ ] Cập nhật `README.md` root (mục "Trạng thái (Giai đoạn 5 – AI Observer)") và tick checklist `03-roadmap.md` khi verify xong.
- [ ] Tạo `Project-Documents/report/phase-5-ai-observer-test-report.md` (theo format `phase-4-accountability-test-report.md`) — sau khi §7.1 chạy xong.

---

## Checklist Hoàn thiện Giai đoạn 5

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 6 (Báo cáo & Xuất dữ liệu).

- [x] **Schema**: bảng `activity_logs` + `notifications` + `ai_observer_runs` (migration `Phase5AiObserverSchema` đã áp dụng, CHECK/jsonb/index/FK RESTRICT đúng thiết kế) — verify A1–A9 **28/28 PASS**
- [ ] **Thu thập activity log**: `IActivityLogWriter` (khai báo ở Board, impl ở Ai) ghi 6 loại sự kiện từ `TaskService`/`CommentService`, **không bao giờ** làm hỏng request CRUD — verify G1–G6
- [ ] **Signal detector thuần**: `OverdueTask` / `StalledTask` / `Overload` (+ `Bottleneck` optional), ngưỡng cấu hình được, cap signals/evidence, sort severity — verify B1–B14
- [ ] **BackgroundService** chạy nền theo chu kỳ cố định, `PeriodicTimer` + advisory lock + không làm host dừng khi lỗi — verify C1–C3, I1–I2
- [ ] **Tóm tắt trước khi gửi AI**: prompt ≤ `MaxPromptCharacters`, redaction tiêu đề, **không gọi AI khi 0 tín hiệu**, token ghi vào `ai_observer_runs.summary` — verify E1–E3
- [ ] **AI Observer prompt/schema**: `AiObserverOutput` + `ObserverPrompts` + `ObserverFindingValidator` (locator chống hallucination, cap/cắt độ dài) — verify B9–B14, J1–J4
- [ ] **Notification chỉ cho Manager/Admin**: fan-out theo `recipient_user_id`, dedupe 24h, mark-as-read, retention `activity_logs` — verify D1–D15, F1–F8, H1–H4
- [ ] **6 endpoint** (`/api/notifications` ×3, `/api/workspaces/{id}/observer/*` ×2, `/api/observer/runs/{id}`) đúng quyền/CSRF/mã lỗi `{ error }` — verify D, F
- [ ] **Archive check**: Observer **không** đi qua `AiActionService`, **không** ghi `tasks`/`labels`/`task_labels`; module Board không có project reference mới — verify K1–K3
- [ ] **Tài liệu**: `04-database-design.md`, `README.md` module Ai + Board, `README.md` root, `03-roadmap.md`, báo cáo test §7.1

### Việc của antigravity (§6 + §7.2)

- [ ] §6 frontend `src/features/ai/` (`types/notification.types.ts`, `services/notificationApi.ts`, `services/observerApi.ts`, `hooks/useNotifications.ts`, `hooks/useObserverRuns.ts`, `components/NotificationDrawer.tsx`, `components/NotificationItem.tsx`, `components/ObserverRunsDrawer.tsx`, `components/ObserverFindingCard.tsx`) + nút "Cảnh báo AI" + badge unread ở `BoardView.tsx`
- [ ] §7.2 test Vitest cho API/hooks/components mới + cập nhật `BoardView.test.tsx`; chạy `oxlint` (0 warn/0 err) + `tsc -b` + `vite build` + `vitest run` sạch
- [ ] Cập nhật `README.md` root + tick `03-roadmap.md` (sau khi backend verify xong)
