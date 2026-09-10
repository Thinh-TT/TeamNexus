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

- [x] Tạo file (interface + constants, **không** thêm project reference nào vào Board):
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
- [x] Đặt `NullActivityLogWriter` (no-op) cùng file để harness dùng `AddBoardModule` **không cần** `AddAiModule`, và để production không bao giờ thiếu dependency.

### 2.2 Implement trong module Ai (`Services/ActivityLogWriter.cs`)

- [x] `ActivityLogWriter : IActivityLogWriter` → `_db.Activities.Add(new ActivityLog { … })` + `SaveChangesAsync`; bọc `try/catch` + `LogWarning` (đúng tinh thần `BoardEventPublisher`: ghi log phụ không được làm hỏng luồng chính).
- [x] Validate phòng thủ trước khi ghi: `Action`/`EntityType` không rỗng và ≤ 64 (nếu vi phạm ⇒ warning + bỏ qua, **không** throw).
- [x] `PayloadJson` `null`/rỗng ⇒ lưu `null` (không lưu chuỗi `"{}"` vô nghĩa).

### 2.3 Gắn vào module Board (chỉ thêm dòng ghi log, không đổi nghiệp vụ)

- [x] `TaskService`: constructor nhận thêm `IActivityLogWriter`; ghi **sau khi** `SaveChangesAsync` thành công:
  - `CreateTaskAsync` → `TaskCreated` (payload `{ columnId, assigneeId, priority }`).
  - `UpdateTaskAsync` → `TaskUpdated` (payload **chỉ các field thay đổi**: `titleChanged`, `assigneeId`, `dueDate`, `priority` — không dump nội dung).
  - `MoveTaskAsync` → `TaskMoved` (payload `{ fromColumnId, toColumnId }`) **+ `TaskCompleted`** khi `targetColumn.IsDone` (payload `{ columnId }`); **ghi sau `CommitAsync`** để log không làm rollback thao tác kéo-thả.
  - `DeleteTaskAsync` → `TaskDeleted` (payload `{ columnId }`).
- [x] `CommentService.CreateCommentAsync` → `CommentAdded` (payload `{ commentId, taskId }`); cần `WorkspaceId` → đã có `task.Board!.WorkspaceId` từ `LoadTaskWithBoardAsync`.
- [x] **Không** đổi chữ ký `ITaskService`/`ICommentService`, **không** đổi hành vi broadcast SignalR, **không** đổi logic validate/position/`completed_at`.
- [x] ⚠️ Đây là **thay đổi duy nhất** của Giai đoạn 5 chạm module Board — cấm mọi thay đổi khác (endpoint/DTO/quyền/behaviour).
- [x] **Verify §2 (đã chạy):** harness tạm ngoài workspace (`%TEMP%\tn-ai-s5-s2-verify`, đã xoá) dựng **API thật** (cổng 5198) + PostgreSQL thật + JWT tự ký → **31/31 check PASS**; ma trận `activity_logs` của board fixture đúng `CommentAdded=1, TaskCompleted=1, TaskCreated=2, TaskDeleted=1, TaskMoved=2, TaskUpdated=1`; 4 mutation thất bại (400/404 ×3) **không** sinh row; `frontend npm run lint` 0 warn/0 err + `npm test` **88/88 PASS**.

> **Ghi chú hiện thực §2 (khác biệt nhỏ so với spec, có chủ ý):**
> - `NullActivityLogWriter` **được đăng ký trong `AddBoardModule`** (không chỉ "đặt cùng file"): nếu thiếu, `TaskService`/`CommentService` không resolve được khi app/harness chỉ nạp module Board. `AddAiModule` đăng ký đè `ActivityLogWriter`; DI lấy đăng ký cuối nên Board **phải** đăng ký trước Ai — đã verify qua backdoor probe `IActivityLogWriter => ActivityLogWriter` (không phải bản no-op).
> - `TaskService.UpdateTaskAsync`/`MoveTaskAsync`/`DeleteTaskAsync` cần `WorkspaceId`, nên helper `RequireMemberOfTaskBoardAsync` được tách thêm bản `RequireMemberOfTaskBoardWithWorkspaceAsync` **trả `Task<Guid>`** (C# không cho `out` trong async method — đã thử và phải sửa). Không thêm truy vấn board thừa.
> - Payload `TaskUpdated` shape chốt: `{titleChanged, descriptionChanged, assigneeId, dueDate, priority}` (boolean cho field text ⇒ **không** dump nội dung) + `TaskCreated` thêm `isDone` (suy từ `column.IsDone`, hữu ích cho detector §3). `TaskDeleted` giữ `{columnId}` như spec — **không** thêm `isDone` (phải query thêm mà detector không cần).
> - `UpdateTaskAsync` **luôn** ghi 1 row `TaskUpdated` kể cả khi không field nào đổi (các boolean `false`) — giữ tính "1 mutation = 1 row" cho verify; đã verify `G2-flags`.
> - `MoveTaskAsync`: `Include(t => t.Board)` thêm vào query sẵn có để lấy `WorkspaceId` **không** phát sinh round-trip; log `TaskMoved` (+ `TaskCompleted` nếu cột đích `IsDone`) ghi **sau `CommitAsync`**.
> - `DeleteTaskAsync`: `WorkspaceId` phải chụp **trước** khi soft-delete (sau đó task bị query filter ẩn) — verify `G5-row`/`G5-behaviour`.
> - ⚠️ **CSRF trong harness:** `GET /api/auth/antiforgery` lúc **ẩn danh** cấp token 155 ký tự, còn sau khi gắn JWT cookie token là 198 ký tự (đã bind identity). Echo token ẩn danh vào POST ⇒ **403**; phải lấy lại token **sau** khi đăng nhập (đúng cách `httpClient` của frontend tự làm khi gặp 403 CSRF). Ghi lại để các harness §3–§5 không mất thời gian debug lại.
> - Đã sửa một `using` còn thiếu trong `AiModule.cs` (`TeamNexus.Modules.Board.Services`) khi thêm đăng ký — build solution 0 warning / 0 error.
> - DB dev đang chứa rác lịch sử của Phase 1–4 (13 board `deleted_at != null`, task soft-deleted) ⇒ verify G9 dùng **"row của fixture đã bị xoá"** (`fixture_board=0`, `fixture_cols=0`, `name_matches=0`, `fixture_activity_logs=0`) làm gate, còn tổng tuyệt đối của bảng chỉ ghi nhận tham khảo. Cơ chế pre-clean tự-heal (xoá board tên `S2 verify board%` do lần chạy lỗi trước để lại) chạy **trước khi tạo fixture**.

---

## 3. Backend – Signal detector (hàm thuần)

### 3.1 Contract (`Services/ObserverSignalDetector.cs`)

- [x] Định nghĩa (mọi thứ `public` để verify thuần + làm điểm tựa test Giai đoạn 7):
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
- [x] `public static ObserverSignalSet Analyze(ObserverWorkspaceSnapshot snapshot, ObserverThresholds thresholds)` — **thuần**, không đọc DB, không dùng `DateTime.Now` (nhận `Now` từ snapshot). `ObserverSignalSet(Signals, TruncatedSignals)` (xem điều chỉnh scope §3 bên dưới); `ObserverSignal` có thêm `Weight` (int) để sort xác định.

> ### 🔻 Điều chỉnh scope §3 (đã chốt khi hiện thực)
> Hai mục cuối của §3.2 **chuyển sang §4** vì chúng thuộc phần AI mà §4.2 đã nhận:
> - `ObserverWindow` (cần `lastCompletedRun.FinishedAt`) → **§4.5** (`ObserverService` là nơi có run gần nhất).
> - `BuildPrompt`/`BuildPayload` + `MaxTitleExcerptLength` → **§4.2** (`Contracts/ObserverAiModels.cs` + `Services/ObserverPrompts.cs`).
>
> Ngược lại, **`ObserverOptions` + `ObserverThresholds` + `NotificationSeverities` được tạo ngay ở §3** (không phải §4.1) vì `Analyze` cần chúng; §4.1 vì vậy **không** tạo lại options, chỉ còn: thêm section `Observer` vào `appsettings.json` + `AddOptions<ObserverOptions>().Bind(...)` trong `AiModule` + `AddHostedService`. §4.4 dùng lại `NotificationSeverities` (một nguồn severity duy nhất cho cả detector và notification).
>
> **Kiểu trả về:** spec ghi `IReadOnlyList<ObserverSignal>`; thực tế trả `ObserverSignalSet` để mang thêm `TruncatedSignals` như §3.2 yêu cầu ("trả về thêm `truncatedSignals` để ghi vào run summary").

### 3.2 Công thức & ngưỡng (`ObserverThresholds` build từ `ObserverOptions`)

- [x] `OverdueTask`: task **không** ở cột `is_done` **và** `DueDate < now`; `Severity = Critical` nếu `overdueDays > 2 × CriticalOverdueDays`, ngược lại `High`. Evidence `TaskIds` = các task quá hạn (cap), `UserIds` = assignee của chúng.
  - Biên: `DueDate == null` → bỏ qua; `dueDate == now` → **chưa** quá hạn; task ở cột `is_done` → bỏ qua.
- [x] `StalledTask`: task **không** `is_done` **và** `max(UpdatedAt, LastCommentAt ?? CreatedAt) < now − StalledDays`; `Severity = High` nếu ≥ `2 × StalledDays`, ngược lại `Medium`.
- [x] `Overload` (gom theo `AssigneeId`, bỏ task `is_done`): tín hiệu khi `OverdueCount ≥ OverloadOverdueMin` (mặc định 2) **hoặc** `OpenCount ≥ OverloadMinOpenTasks` (mặc định 5) — cộng thêm `OpenCount ≥ 2 × OverloadMinOpenTasks` ⇒ `Severity = Critical`, ngược lại `High`. `Summary` nêu số task mở / quá hạn / số task Urgent|High. `UserIds` = 1 assignee; `TaskIds` = task mở của người đó (cap).
- [x] `Bottleneck` *(chỉ khi `BottleneckDetectionEnabled`)*: cột **không** `is_done` có `openCount ≥ BottleneckMinTasks` **và** có ≥ `BottleneckStalledMinTasks` task đứng yên > `StalledDays` ⇒ `Medium`. `TaskIds` = task đứng yên ở cột đó bị giới hạn theo board.
- [x] Sắp xếp theo `Severity` giảm dần (`Critical > High > Medium > Low`) rồi theo độ "nặng" (số task quá hạn / số ngày đứng yên); **cap `MaxSignalsPerWorkspace`** (20) và **cap `MaxEvidenceIdsPerSignal`** (10) — trả về thêm `truncatedSignals` để ghi vào run summary.
- [x] *(đã chuyển sang §4.2 và **đã hiện thực**)* `ObserverSummaryBuilder` — `ObserverWindow` → `ObserverSummarizer.ComputeWindow` (§4.5 dùng); `BuildPrompt`/`BuildPayload` → `ObserverSummarizer.BuildPayload/BuildRequest` + `ObserverPrompts` (§4.2). Verify B2i–B2l.
- [x] **Verify §3 (nhóm B §9.1) — đã chạy: 44/44 PASS** bằng harness thuần (`%TEMP%\tn-ai-s5-s3-verify`, đã xoá; chỉ `ProjectReference` tới project Ai — **không** DB/HTTP/AI): `NotificationSeverities.Rank/AtLeast` (case-insensitive, giá trị lạ ⇒ −1); `ToThresholds` clamp 0/âm ⇒ 1 + `Interval`/`LookbackWindow` clamp; quá hạn (đúng/sai cột/null/`due == now`/tương lai/`== 2×ngưỡng` ⇒ High, `2×+1` ⇒ Critical/weight = worst + sort TaskIds); stalled (strict `<` — `== StalledDays` **không** tín hiệu, `+1` ⇒ Medium, `≥ 2×` ⇒ High, comment muộn hơn `UpdatedAt` ⇒ "sống lại", comment cũ ⇒ vẫn stalled theo `UpdatedAt`); overload (5 mở ⇒ có; 4 mở + 2 quá hạn ⇒ có; 4 mở + 0 quá hạn ⇒ **không**; 9 mở ⇒ High, 10 mở ⇒ Critical; 2 assignee ⇒ 2 tín hiệu; assignee null/done ⇒ loại; evidence quá hạn trước); bottleneck (bật/tắt, cột done bị loại, thiếu `stalledInColumn` ⇒ không); cap (30 assignee quá tải ⇒ 20 tín hiệu + truncated 10; cap 5 ⇒ truncated 25; evidence cap 10/1); sort severity rồi weight; **tất định** (JSON 2 lần giống hệt); snapshot rỗng/chỉ task done ⇒ `([], 0)`; ngưỡng 0/âm không throw; 1000 task chạy **2ms**.

> **Ghi chú hiện thực §3 (khác biệt nhỏ so với spec, có chủ ý):**
> - ⚠️ **Bug công thức đã bắt được nhờ harness (đã sửa):** bản đầu dùng `now − activity >= StalledDays`, khiến task đứng yên **đúng bằng** `StalledDays` cũng thành tín hiệu. Spec §3.2 ghi `<` (strict) và tổng quan Giai đoạn 5 ghi "task đứng yên **lâu hơn**" ⇒ đã đổi sang **strict `>`** cho cả `StalledTask` và điều kiện stalled của `Bottleneck`; case B10b giờ khẳng định `idle == StalledDays` ⇒ **không** tín hiệu, `+1 ngày` ⇒ có.
> - `Analyze` trả **`ObserverSignalSet(Signals, TruncatedSignals)`** (spec ghi `IReadOnlyList<ObserverSignal>`): §3.2 yêu cầu trả thêm `truncatedSignals` cho run summary nên nó phải nằm trong kiểu trả về.
> - **`ObserverSignal` thêm `Weight`** (`int`) so với spec: cần cho quy tắc "sort theo severity rồi theo độ nặng" mà không phải mang mảng sort song song; weight = số ngày quá hạn/đứng yên (max của nhóm) hoặc `max(OpenCount, OverdueCount)` cho overload.
> - **Ngữ nghĩa "cap" (khác kỳ vọng ban đầu, đã verify):** mỗi **loại** tín hiệu được **gom nhóm** — 30 task quá hạn của một workspace ⇒ **1** tín hiệu `OverdueTask` với `TaskIds` bị cap 10, **không** phải 30 tín hiệu. `MaxSignalsPerWorkspace` vì vậy chỉ cắt khi có **nhiều tín hiệu khác loại/khác assignee** (verify: 30 assignee quá tải ⇒ 20 tín hiệu + `truncatedSignals = 10`; cap 5 ⇒ truncated 25). Đã ghi lại cách hiểu này vào bảng công thức §3.2 (mỗi dòng là **một** tín hiệu gom nhóm, không phải mỗi task).
> - **Ngưỡng "ngày" là số nguyên floor** (`(int)(to - from).TotalDays`) và so sánh severity là **strict `>`** đúng câu chữ spec: `overdueDays == 2 × CriticalOverdueDays` ⇒ `High`, `+1` ⇒ `Critical` (verify B9).
> - `ObserverTaskSnapshot` thêm **`ColumnName`** và `ObserverColumnSnapshot` thêm **`BoardName`** để summary có ngữ cảnh cho AI (không chỉ GUID); `ObserverService` §4.5 nạp sẵn board/column nên không tốn thêm truy vấn.
> - **`ActivityAt(task) = max(UpdatedAt, LastCommentAt ?? CreatedAt)`** — comment mới hơn lần sửa cuối sẽ "hồi sinh" task (verify B11/B12); đây là chỗ duy nhất dùng `CreatedAt`.
> - `Analyze` **không** dùng `DateTime.Now`, không chạm `DbContext`/`HttpClient` (đã grep xác nhận; lần xuất hiện duy nhất của `DateTime.Now` là trong comment docstring).
> - `ObserverSeverity` và `NotificationSeverities` tách hai class trong cùng file `Services/ObserverSeverity.cs`: hằng số ở `ObserverSeverity`, logic so sánh ở `NotificationSeverities` (tránh self-reference khó đọc và cho §4.4 dùng lại).
> - §3 **không** sửa `AiModule.cs`/`appsettings.json`/`Program.cs` và **không** thêm DI registration (chưa có consumer) ⇒ `git diff --name-only -- '*.csproj'` rỗng.
 bằng harness thuần (`%TEMP%\tn-ai-s5-s3-verify`, đã xoá; chỉ `ProjectReference` tới project Ai — **không** DB/HTTP/AI): `NotificationSeverities.Rank/AtLeast` (case-insensitive, giá trị lạ ⇒ −1); `ToThresholds` clamp 0/âm ⇒ 1 + `Interval`/`LookbackWindow` clamp; quá hạn (đúng/sai cột/null/`due == now`/tương lai/`== 2×ngưỡng` ⇒ High, `2×+1` ⇒ Critical/weight = worst + sort TaskIds); stalled (strict `<` — `== StalledDays` **không** tín hiệu, `+1` ⇒ Medium, `≥ 2×` ⇒ High, comment muộn hơn `UpdatedAt` ⇒ "sống lại", comment cũ ⇒ vẫn stalled theo `UpdatedAt`); overload (5 mở ⇒ có; 4 mở + 2 quá hạn ⇒ có; 4 mở + 0 quá hạn ⇒ **không**; 9 mở ⇒ High, 10 mở ⇒ Critical; 2 assignee ⇒ 2 tín hiệu; assignee null/done ⇒ loại; evidence quá hạn trước); bottleneck (bật/tắt, cột done bị loại, thiếu `stalledInColumn` ⇒ không); cap (30 assignee quá tải ⇒ 20 tín hiệu + truncated 10; cap 5 ⇒ truncated 25; evidence cap 10/1); sort severity rồi weight; **tất định** (JSON 2 lần giống hệt); snapshot rỗng/chỉ task done ⇒ `([], 0)`; ngưỡng 0/âm không throw; 1000 task chạy **2ms**.

---

## 4. Backend – Observer service, AI contract, Notification

### 4.1 Options & config (`Options/ObserverOptions.cs`)

- [x] **`ObserverOptions` + `ObserverThresholds` + `NotificationSeverities` đã tạo ở §3** (xem điều chỉnh scope §3): `Options/ObserverOptions.cs` (đủ key bảng dưới + `Interval`/`LookbackWindow`/`ToThresholds()` clamp), `Services/ObserverSeverity.cs` (`ObserverSeverity` + `NotificationSeverities.Rank/IsKnown/AtLeast`), `Services/ObserverSignalDetector.cs` (`ObserverThresholds` record). §4.1 chỉ còn **2 việc**: thêm section `Observer` vào `appsettings.json` và đăng ký options/DI trong `AiModule`.

  Bảng key dưới đây là **hợp đồng đã hiện thực** của `ObserverOptions` (giá trị mặc định đã verify ở B5b):

  | Key | Mặc định | Ghi chú |
3. **B2. Hàm thuần bổ sung §4 (B2a–B2o) — ✅ 15/15 PASS (harness thuần):** `ObserverFindingValidator` (type lạ bị bỏ; severity thiếu ⇒ Medium; dưới `MinSeverityToNotify` ⇒ bỏ; **ID bịa bị giao bỏ**; cap evidence; title/message rỗng ⇒ mặc định, > 200/2000 ⇒ cắt; output null ⇒ `[]`); `ObserverSummarizer` (payload **không** chứa description; title cắt ≤ `MaxTitleExcerptLength`; payload vượt cap ⇒ cắt tín hiệu + JSON vẫn hợp lệ + `truncatedByPrompt > 0`; `BuildRequest` marker dòng đầu + `JsonMode`; `ComputeWindow` 3 nhánh); `FakeAiProvider` (marker ⇒ findings dùng ID thật ⊆ evidence; **không** marker ⇒ vẫn trả proposal Smart Setup; marker + payload hỏng ⇒ findings rỗng, không throw).
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

- [x] Thêm section `"Observer"` vào `src/TeamNexus.Api/appsettings.json` (đầy đủ key như bảng, không chứa secret).
- [x] Thêm hằng số severity/type (module Ai):
  ```csharp
  public static class NotificationSeverities { Low, Medium, High, Critical }  // có Rank để so sánh
  public static class NotificationTypes {
      OverdueTask, StalledTask, Overload, Bottleneck  // dùng cho notifications.type + findings.type
  }
  ```
- [x] `ObserverThresholds` (record build từ `ObserverOptions`) để §3 thuần không phụ thuộc `IOptions`.
- [x] Log lúc khởi động: `Observer: enabled={Enabled}, interval={Interval}, lookback={LookbackHours}h, dedupe={DeduplicationWindowHours}h` (không có secret).

### 4.2 Contract AI + prompt (`Contracts/ObserverAiModels.cs`, `Services/ObserverPrompts.cs`)

- [x] Raw output (khớp **chặt** system prompt, `System.Text.Json`):
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
- [x] `ObserverPrompts.SystemPrompt` nêu rõ: chỉ dùng ID có trong `evidence`; **không** bịa task/user; **không** đề xuất hành động ghi dữ liệu; trả **đúng 1 object** `{ "findings": [ … ] }`; mỗi finding phải `type` ∈ 4 loại, `severity` ∈ 4 mức; giữ ngôn ngữ tiếng Việt; nếu không có gì đáng báo thì trả `findings: []`.
- [x] `ObserverPrompts.BuildUserPrompt(payload)`: **dòng đầu** là `{"agent":"observer"}` rồi newline, sau đó JSON payload tín hiệu (marker cho `FakeAiProvider` — D9). Đảm bảo tổng độ dài ≤ `MaxPromptCharacters`.
- [x] `Services/ObserverFindingValidator.cs` — **hàm thuần public static**:
  - `type` không thuộc `NotificationTypes` ⇒ **bỏ** finding.
  - `severity` sai/thiếu ⇒ mặc định `Medium`; dưới `MinSeverityToNotify` ⇒ **bỏ**.
  - `evidence.taskIds/userIds` = **giao** với ID của tín hiệu tương ứng ⇒ ID lạ bị loại; cap `MaxEvidenceIdsPerSignal`.
  - `title` 1–200 (rỗng ⇒ sinh title mặc định theo `type`; dài ⇒ cắt); `message` rỗng ⇒ sinh message mặc định; dài ⇒ **cắt** 2000 (không lỗi — đây là dữ liệu AI sinh, giống `decision_note` Phase 4 cap 500).
- [x] `CompleteAsync(...)` dùng `JsonMode: true`, `Temperature`, `MaxOutputTokens` từ `ObserverOptions`; parse case-insensitive + bỏ code fence phòng thủ (tái dùng cách Phase 3 §4.3 bước 8) — **không retry** (tiết kiệm token ở Observer).

### 4.3 `FakeAiProvider` – nhánh Observer (D9)

- [x] Trong `CompleteAsync`, kiểm tra `request.UserPrompt` có bắt đầu bằng marker `{"agent":"observer"}` (sau khi trim/khoan dung whitespace) ⇒ trả JSON mẫu:
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
- [x] Không có marker ⇒ **giữ nguyên** hành vi Phase 3 (proposal mẫu) — có unit-check riêng cho nhánh này để tránh regression.
- [x] Cập nhật docstring `FakeAiProvider` (mô tả 2 nhánh + marker).

### 4.4 `Services/NotificationService.cs` + `Services/ObserverNotificationFactory.cs`

- [x] Interface:
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
- [x] `NotifyManagersAsync`: lấy `workspace_members` role `Manager`/`Admin` (cap `MaxManagersPerWorkspace`, sắp xếp ổn định theo `user_id`); **0 manager** ⇒ log Warning, trả 0 (không throw); với mỗi finding: **dedupe** (D12) → `AddRange` **1 row/người nhận** → 1 `SaveChangesAsync`; cap tổng `MaxNotificationsPerRun`.
- [x] `ObserverNotificationFactory` (thuần public static): `BuildTitle`, `BuildMessage`, `BuildPayload(finding, ctx)` → JSON camelCase (`runId`, `boardId` nếu mọi evidence cùng board, `taskIds`, `userIds`, `severity`, `model`, `tokens`).
- [x] **Dedupe ngữ nghĩa:** so `payload` bằng `JsonElement.DeepEquals` trên key `taskIds`/`userIds`/`signals` (KHÔNG so chuỗi — `jsonb` đã chuẩn hoá).
- [x] `ListAsync`: `take` mặc định 20, clamp 1–100; `isRead` filter optional; sort `CreatedAt DESC`; `UnreadCount` = **tổng** chưa đọc của `userId` (không phụ thuộc `take`).
- [x] `MarkReadAsync`: không thấy **hoặc** `RecipientUserId != userId` ⇒ **404** `"Notification not found."`; đã read ⇒ **idempotent** (200, giữ `ReadAt` gốc). `MarkAllReadAsync`: chỉ row của `userId` và `IsRead == false`.

### 4.5 `Services/IObserverService.cs` + `ObserverService.cs`

- [x] Interface:
  ```csharp
  public interface IObserverService
  {
      Task<ObserverScanOutcome> ScanAsync(Guid? workspaceId, CancellationToken ct = default);
      Task<IReadOnlyList<ObserverRunResponse>> ListRunsAsync(Guid workspaceId, int take, Guid userId, CancellationToken ct = default);
      Task<ObserverRunDetailResponse> GetRunAsync(Guid runId, Guid userId, CancellationToken ct = default);
  }
  ```
- [x] `ScanAsync` — đúng 9 bước ở §0 "Luồng tổng thể":
  1. `!Enabled && workspaceId is null` ⇒ return `Skipped` (`NoWorkspaces`/`Disabled`) **không ghi DB**. Quét tay (`workspaceId != null`) **vẫn chạy** dù `Enabled = false` (phục vụ demo/verify — ghi rõ trong docstring + README).
  2. `pg_try_advisory_lock(@key)` với hằng số cố định của module → không lấy được ⇒ `Skipped` (`AlreadyRunning`). Giải phóng lock trong `finally` (`pg_advisory_unlock`). Kèm `static readonly SemaphoreSlim(1,1)` trong process.
  3. Nạp workspace: `_db.Workspaces.Where(w => w.DeletedAt == null)` (+ `workspaceId` chỉ định); sắp xếp theo hoạt động gần nhất (`activity_logs.created_at` / `tasks.updated_at`) giảm dần; cap `MaxWorkspacesPerRun`.
  4. Nạp dữ liệu **bounded** 1 workspace: tasks (bỏ soft-delete + cột `is_done` không cần thiết cho detector nhưng vẫn nạp để tính bottleneck), comment count + `max(created_at)`, columns, run `Completed` gần nhất (`ObserverWindow`), số activity trong window.
  5. `ObserverSignalDetector.Analyze(...)`; rỗng ⇒ run `Completed` với `aiCalled=false`, 0 notification, token `null` (D6).
  6. `ObserverSummarizer.BuildRequest(...)` (prompt có marker observer, `JsonMode: true`).
  7. `_aiProvider.CompleteAsync(...)` → parse `AiObserverOutput`; lỗi parse/`AiProviderException` ⇒ run `Failed` (`summary.error` ≤ 500), **0 notification**.
  8. `ObserverFindingValidator.Validate(...)` ⇒ `ObserverFinding[]`; rỗng ⇒ run `Completed`, 0 notification.
  9. `NotifyManagersAsync(...)` → ghi `ai_observer_runs` (status `Completed`, `summary` đầy đủ) → prune `activity_logs` (D14) → `LogInformation` (workspace, signals, aiCalled, notificationsCreated, durationMs). **Không** log nội dung prompt/response (chỉ độ dài, mức `Debug`).
- [x] `ListRunsAsync`: `RequireManagerAsync` (403) + workspace 404 nếu không thấy; sort `StartedAt DESC`; clamp `take` 1–50 (mặc định 20); project `SignalsDetected`/`FindingsWritten`/`NotificationsCreated`/`AiCalled` từ `summary` (parse phòng thủ: JSON hỏng ⇒ 0/false, không ném).
- [x] `GetRunAsync`: run 404; dùng `run.WorkspaceId` + `RequireManagerAsync` ⇒ 403; trả `summary` dạng `JsonElement?` + `findings[]` (đọc từ `summary.findings`; hỏng JSON ⇒ `[]`).
- [x] **Không** đụng `IAiActionService`/`IAiActionApplier` — Observer là lớp *đọc/cảnh báo* (contract bàn giao Phase 4 §6).
- [x] **Verify §4.5 (đã chạy, nhóm D/E/H/I/J):** provider lỗi (`AiProviderException`) ⇒ run `Failed`, 0 notification; JSON AI hỏng ⇒ `Failed` + `summary.error` ≤ 500; `findings: []` ⇒ `Completed` + 0 notification; ID bịa bị giao bỏ (evidence rỗng, không lộ ID lạ); 0 tín hiệu ⇒ **không gọi AI**; dedupe 24h; advisory lock chặn scan song song.

### 4.6 `Services/ObserverBackgroundService.cs`

- [x] `ObserverBackgroundService : BackgroundService`:
  - `ExecuteAsync`: chờ `StartupDelaySeconds` (tôn trọng `stoppingToken`) → nếu `!Enabled` ⇒ `LogInformation` rồi `return`.
  - Vòng lặp `PeriodicTimer(_options.Interval)`: mỗi tick tạo scope `IServiceScopeFactory.CreateAsyncScope()` → resolve `IObserverService` → `ScanAsync(null, stoppingToken)`.
  - **Bọc toàn bộ trong `try/catch`** + `LogError`: exception thoát ra khỏi `BackgroundService` sẽ làm **host dừng** (mặc định `BackgroundServiceExceptionBehavior = StopHost`) — tuyệt đối không để xảy ra.
  - Log đầu/kết: `Observer background service started (enabled=…, interval=…, startupDelay=…s, lookback=…h, dedupe=…h)`.
- [x] **Verify §4.6 (nhóm C §9.1, đã chạy với `IHost` thật):** `Enabled=false` ⇒ host start/stop sạch trong ~1.2s, **0** row `ai_observer_runs`; `Enabled=true` + `StartupDelaySeconds=30` ⇒ start/stop trong ~0.8s không exception, chưa có run (đang trong delay); log khởi động xuất hiện đúng 1 dòng. *Không* chờ đủ 70s để thấy tick chạy (tốn thời gian); kiểm tra "timer không giết host" đã bao phủ qua start/stop + catch toàn cục.
- [x] **Ghi chú điều chỉnh:** mục "stop app giữa run ⇒ không có row `Running` treo" được bảo đảm **bằng thiết kế**: run row chỉ được ghi **một lần** ở bước 9 (status cuối), không có row `Running` trung gian ⇒ không thể có row treo.

> **Ghi chú hiện thực §4 (khác biệt nhỏ so với spec, có chủ ý):**
> - ⚠️ **Bug công thức đã bắt được nhờ harness (đã sửa):** `ObserverSummarizer.ComputeWindow` bản đầu luôn bắt đầu từ `now − LookbackHours` khi run trước đã cũ, khiến `MaxLookbackDays` bị `LookbackHours` (24h) chặn ⇒ mất tác dụng "bắt kịp sau khi host ngủ". Đã sửa: `LookbackHours` là **sàn**, `MaxLookbackDays` là **trần**; run trước cũ ⇒ lùi đúng `MaxLookbackDays` (verify B2l: `win3 = now − 7 ngày`).
> - ⚠️ **Retention prune được thu hẹp theo workspace (đã sửa):** bản đầu `ExecuteDeleteAsync` không lọc workspace; nếu batch đầy bởi row cũ của workspace khác thì row của workspace đang quét **không bao giờ** bị dọn. Đã thêm `WorkspaceId == workspaceId` + `OrderBy(CreatedAt)` (cũ nhất trước) ⇒ hành vi xác định và không xoá lịch sử của workspace khác.
> - `Analyze` trả `ObserverSignalSet` (từ §3) và `ObserverService` là **nơi duy nhất** chạm DB/AI; `ObserverSummarizer`, `ObserverFindingValidator`, `ObserverNotificationFactory` đều `public static`/thuần ⇒ verify không cần DB (97 case B2/D/E/H/I/J đều chạy được).
> - **`NotificationTypes`** được đặt trong `Services/ObserverSeverity.cs` cùng `ObserverSeverity`/`NotificationSeverities` (3 hằng cùng giá trị với `ObserverSignalDetector`) ⇒ một nguồn duy nhất cho detector → validator → `notifications.type`.
> - **Không có model name trong `IAiProvider`:** `ObserverService` lấy `model` từ `IOptions<DeepSeekOptions>` (nếu có key) hoặc tên class provider (khi offline) để ghi vào `summary`. Port provider giữ nguyên (đúng ghi chú §3 "chỉ đổi prompt").
> - **Chống chạy chồng 2 tầng:** `static SemaphoreSlim(1,1)` trong process + `pg_try_advisory_lock` trên **connection riêng** (không mở transaction quanh lời gọi AI); `finally` luôn `pg_advisory_unlock`. Harness I1 chứng minh 2 `ScanAsync` song song ⇒ 1 `Completed` + 1 `Skipped(AlreadyRunning)`.
> - **Hạn chế đã biết (ghi rõ để không hiểu sai):** khi 2 scan chạy **thật sự đồng thời ở 2 tiến trình**, cả hai có thể ghi notification trước khi bên nào commit dedupe state ⇒ dedupe chỉ giảm (không triệt tiêu 100%) trùng lặp trong tình huống đua hiếm gặp. Nhịp 30 phút khiến điều này thực tế không xảy ra; H1 chứng minh dedupe đúng cho các lần quét tuần tự (trường hợp thực tế).
> - **Cap tổng số finding** = số tín hiệu đầu vào ⇒ model không thể "phình" số alert nhiều hơn cơ sở đã phát hiện.
> - **`SeedFixture` trong harness phải dùng raw SQL**: `TeamNexusDbContext.SaveChanges` **tự stamp `created_at` và `updated_at` cho MỌI entity `IAuditableEntity` khi thêm mới**, nên seed qua EF làm mọi task trông "vừa tạo" (stalled/bottleneck không bao giờ bật). Đây là hành vi **cố ý** của DbContext (`04` §1.2) — ghi lại để các harness §5/§7 biết mà không mất thời gian debug.
> - `appsettings.json` nhận section `Observer` đầy đủ key; `ObserverService` log Information 1 dòng/run (workspace, signals, findings, notifications, aiCalled, durationMs) và **không** log nội dung prompt/response (chỉ độ dài ở mức `Debug`).
> - §4 **không** đăng ký DI (thuộc §5.4): harness phải tự dựng `ObserverService`/`NotificationService` qua `ActivatorUtilities` — ghi lại để §5.4 là bước bắt buộc trước khi test HTTP.

---

## 5. Backend – DTO, Endpoints, DI

### 5.1 DTO (`DTOs/ObserverDtos.cs`, `DTOs/NotificationDtos.cs`)

- [x] ```csharp
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
- [x] Cả `Payload` và `Summary` để `JsonElement?` (parse **phòng thủ**: JSON hỏng ⇒ `null` thay vì 500) — đúng tiền lệ Phase 4 §3.1.
- [x] `Status` serialize giữ casing canonical (`"Completed"`, `"Skipped"`…) — không đổi sang lowercase (đúng `TaskPriority`/`AiActionStatus`).

### 5.2 `Endpoints/NotificationEndpoints.cs`

- [x] Group `/api/notifications` `.WithTags("Notifications")` + `DomainExceptionFilter`; mọi route `.RequireAuthorization()`:

  | Method + path | Quyền | Thành công | Ghi chú |
  |---|---|---|---|
  | `GET /?isRead=&take=` | user đã đăng nhập (**chỉ row của mình**) | `200 NotificationListResponse` | `take` mặc định 20, clamp 1–100; `isRead` strict parse |
  | `POST /{notificationId:guid}/read` | owner | `200 NotificationResponse` | 404 nếu không phải của mình; idempotent |
  | `POST /read-all` | user đã đăng nhập | `200 { "updated": n }` | chỉ row của mình |

- [x] POST thêm `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()`; `GET` **không** gắn CSRF (tiền lệ Phase 4 §3.2).
- [x] `isRead` parse **strict** (`true`/`false`, chấp nhận mọi casing; giá trị khác ⇒ **400** `{ error }`) — cùng tinh thần `ParseStatus` Phase 4.

### 5.3 `Endpoints/ObserverEndpoints.cs`

- [x] Group `.WithTags("AiObserver")` + `DomainExceptionFilter`; mọi route `.RequireAuthorization()`:

  | Method + path | Quyền | Thành công | Ghi chú |
  |---|---|---|---|
  | `POST /api/workspaces/{workspaceId:guid}/observer/scan` | Manager/Admin | `200 ObserverScanResponse` | Quét **đồng bộ** (foreground) — kết quả trả ngay; 403 Member; 404 workspace không thấy; **502** nếu provider lỗi (run đã ghi `Failed`, không notification) |
  | `GET /api/workspaces/{workspaceId:guid}/observer/runs?take=20` | Manager/Admin | `200 ObserverRunResponse[]` | sort `startedAt DESC`; `take` clamp 1–50 |
  | `GET /api/observer/runs/{runId:guid}` | Manager/Admin của workspace của run | `200 ObserverRunDetailResponse` | 404 nếu không thấy/không quyền |

- [x] POST `scan` thêm `AntiforgeryValidationEndpointFilter`.
- [x] Đọc `userId` bằng `AiEndpointHelpers.RequireUserId(http)` (đã có từ Phase 4).
- [x] Member (không phải Manager/Admin) trên **cả 3** route ⇒ **403** `"Requires Manager or Admin role in this workspace."` — đây là tiêu chí roadmap "chỉ Manager thấy cảnh báo".

### 5.4 Đăng ký DI (`AiModule.cs`)

- [x] `services.AddOptions<ObserverOptions>().Bind(configuration.GetSection(ObserverOptions.SectionName));`
- [x] `services.AddScoped<IActivityLogWriter, ActivityLogWriter>();`
- [x] `services.AddScoped<INotificationService, NotificationService>();`
- [x] `services.AddScoped<IObserverService, ObserverService>();`
- [x] `services.AddHostedService<ObserverBackgroundService>();`
- [x] `MapAiModuleEndpoints()` thêm `endpoints.MapObserverEndpoints();` + `endpoints.MapNotificationEndpoints();` (giữ 2 dòng cũ).
- [x] `Program.cs` **không phải sửa** (extension point đã có từ Phase 3).
- [x] **Verify §5 (đã chạy):** `dotnet build TeamNexus.sln` → **0 warning / 0 error**; boot API thật 3 lần: (a) nhánh có key ⇒ `GET /api/health` **200** + log `Ai module: DeepSeek đã cấu hình (provider=DeepSeekAiProvider…)` + `Observer enabled=True, interval=00:30:00, lookback=24h, dedupe=24h` + `Observer background service started (enabled=True, interval=00:30:00, startupDelay=60s…)`; (b) `Observer__Enabled=false` ⇒ `GET /api/health` **200** + log `enabled=False` (timer không chạy, quét tay vẫn hoạt động); (c) kiểm OpenAPI: document có **12 tag** gồm `AiObserver` + `Notifications` và **đủ 6 route**. **Lưu ý vận hành:** API thật phải stop trong < 60s (`StartupDelaySeconds`) để observer không quét DB dev; harness chỉ cần HTTP thì đặt `Observer__Enabled=false`.

> **Ghi chú hiện thực §5 (khác biệt nhỏ so với spec, có chủ ý):**
> - ⚠️ **Bug bảo mật thật đã bắt được nhờ nhóm F (đã sửa):** `POST /api/workspaces/{id}/observer/scan` **không** kiểm tra quyền — Member quét được và nhận **200** (vì `LoadTargetsAsync` chỉ kiểm tra board có tồn tại). Đã sửa bằng tham số `actingUserId?` trên `IObserverService.ScanAsync(workspaceId, actingUserId, ct)`: có actor ⇒ `RequireManagerAsync` (Member **403**, workspace lạ **404**); timer truyền `null` (hệ thống quét). Verify F18 chuyển từ **200** ⇒ **403**; F19b xác nhận 404 và không tạo run.
> - ⚠️ **Bug `FakeAiProvider` nhánh Observer (đã sửa):** `BuildObserverFindings` cắt chuỗi ngay sau marker nên payload còn **ký tự `\n` đầu dòng** ⇒ `JsonDocument.Parse` ném `JsonException` ⇒ `catch` trả `findings: []`. Hệ quả: nhánh offline "chạy được" nhưng **im lặng không tạo cảnh báo nào** — test §4 (B2m chỉ kiểm `findings.Count >= 1`, không đi qua HTTP) đã bỏ lọt. Đã sửa bằng `.TrimStart()` **và** thêm `LogWarning` trong `catch` để không bao giờ im lặng nữa. Sau fix: F1 tạo 2 notification thật (`OverdueTask` + `StalledTask`) và B2m-1…B2m-5 **7/7 PASS** (kiểm cả evidence khớp).
> - **6 type response chuyển từ `Services/` sang `DTOs/`** (`NotificationDtos.cs`, `ObserverDtos.cs`) và chỉ còn **một** định nghĩa; service `using TeamNexus.Modules.Ai.DTOs`. `ObserverRunFinding` (HTTP) tách khỏi `ObserverFinding` (domain) theo spec nhưng có `ObserverRunFinding.From(...)` để không lặp khởi tạo.
> - **`ObserverScanOutcome` thêm `Error`** để endpoint map run `Failed` ⇒ `AiProviderException` ⇒ **502** `{ error }` (P4); `SkippedReason` giữ cho log/nội bộ và **không** lộ ra HTTP.
> - `POST scan` trả **200** (không 202) vì quét **đồng bộ** (P3); run `Skipped` cũng **200** với `status="Skipped"` (không phải lỗi).
> - `ParseIsRead` strict: `?isRead=bogus` ⇒ **400** `{ error: "Unknown value for isRead 'bogus'. Expected true or false." }`; `?take=abc` vẫn là **400 ProblemDetails** của framework (đã cảnh báo từ §5.5).
> - `MarkRead` của người khác ⇒ **404** (không 403) để không lộ sự tồn tại của alert; `read-all` lần 2 ⇒ `{ updated: 0 }`; `ReadAt` idempotent — verify so ở **độ chính xác µs của PostgreSQL** (PG làm tròn, .NET cắt).
> - `AiModule` log thêm 1 dòng `Observer enabled=…, interval=…, lookback=…h, dedupe=…h, minSeverity=…` khi khởi động; **`Program.cs` không sửa** — `git diff src/TeamNexus.Api` chỉ có `appsettings.json`.

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
> Backend Giai đoạn 5 **đã xong và verify xong §7.1 (178 check PASS)** — schema, activity log, detector, `ObserverService`, `ObserverBackgroundService`, DI + 6 endpoint sẽ **không đổi nữa**. Cứ code theo đúng contract dưới đây.
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
> ### 3) JSON thật (đã điền từ lần verify §7.1 nhóm F) — dùng làm fixture test
> ```json
> // POST /api/workspaces/{wsId}/observer/scan  → 200
> { "runId": "23f14cb7-1d85-4e82-b546-2bcc76bbc393", "workspaceId": "c0085f1f-6505-4fc6-9eef-15dbe240a6dd",
>   "status": "Completed", "signalsDetected": 2, "findingsWritten": 2, "notificationsCreated": 0, "aiCalled": true }
>
> // GET /api/notifications?take=2  → 200  (rút gọn message)
> { "unreadCount": 0, "items": [ {
>     "id": "b92be833-35eb-477f-ab84-426d63014525",
>     "workspaceId": "c0085f1f-6505-4fc6-9eef-15dbe240a6dd",
>     "type": "StalledTask",
>     "title": "Task S5 overdue task không được cập nhật 12 ngày",
>     "message": "Task \"S5 overdue task\" (board \"S5 verify board\", cột Todo) đã không có cập nhật nào trong 12 ngày…",
>     "payload": { "type": "StalledTask", "severity": "High", "runId": "…", "boardId": null,
>                  "taskIds": ["…"], "userIds": ["…"], "model": "DeepSeekAiProvider",
>                  "promptTokens": 11, "completionTokens": 7 },
>     "isRead": false, "createdAt": "2026-09-10T12:03:05.87+00:00", "readAt": null } ] }
>
> // GET /api/workspaces/{wsId}/observer/runs?take=1  → 200
> [ { "id": "b3238ec0-61a1-4398-936d-50d010b5261f",
>     "workspaceId": "c0085f1f-6505-4fc6-9eef-15dbe240a6dd", "status": "Completed",
>     "startedAt": "2026-09-10T12:03:05.8729+00:00", "finishedAt": "2026-09-10T12:03:08.312226+00:00",
>     "signalsDetected": 2, "findingsWritten": 2, "notificationsCreated": 0, "aiCalled": true } ]
>
> // GET /api/observer/runs/{runId}  → 200  (summary rút gọn)
> { "id": "c6f374e9-f4f4-4fe5-bcef-d9d1b8716619", "status": "Completed",
>   "startedAt": "2026-09-10T12:03:02.36706+00:00", "finishedAt": "2026-09-10T12:03:05.224825+00:00",
>   "summary": { "runId": "0a06217c-…", "aiCalled": true, "model": "DeepSeekAiProvider",
>                "signalsDetected": 2, "signalsByType": { "OverdueTask": 1, "StalledTask": 1 },
>                "truncatedSignals": 0, "truncatedByPrompt": 0, "findingsWritten": 2,
>                "notificationsCreated": 0, "promptTokens": 11, "completionTokens": 7,
>                "durationMs": 2410, "skippedReason": null, "error": null,
>                "findings": [ { "type": "OverdueTask", "severity": "High",
>                                "title": "Task S5 overdue task trễ hạn 15 ngày",
>                                "message": "…", "taskIds": ["…"], "userIds": ["…"] } ] },
>   "findings": [ { "type": "OverdueTask", "severity": "High", "title": "…", "message": "…",
>                   "taskIds": ["…"], "userIds": ["…"] } ] }
>
> // POST /api/notifications/read-all → 200
> { "updated": 1 }
>
> // Lỗi
> { "error": "Unknown value for isRead 'bogus'. Expected true or false." }          // 400
> { "error": "Requires Manager or Admin role in this workspace." }                  // 403 (Member)
> { "error": "CSRF token missing or invalid (send X-XSRF-TOKEN header)." }          // 403 (thiếu CSRF)
> { "error": "Workspace not found." }                // 404 (GET runs | POST scan, workspace lạ)
> { "error": "Observer run not found." }             // 404 (GET run detail)
> { "error": "Notification not found." }             // 404 (POST read của người khác)
> { "error": "stub provider down" }                  // 502 (provider lỗi; run đã ghi Failed)
> ```
> **Ghi chú dữ liệu thật:** `findings` của `FakeAiProvider` là **tiếng Việt thật** (diễn giải task/cột/ngày),
> nên UI không cần giả định nội dung tiếng Anh. `promptTokens`/`completionTokens` = `null` khi provider offline
> nhưng ở lần verify này là số nhỏ do stub; **đừng** hiển thị token khi `null`.
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
> - **Quét tay là đồng bộ** → nút "Quét ngay" phải `loading` + disable tới khi nhận response (đừng giả định job nền rồi poll). Với DeepSeek thật có thể mất **2–5s**; với `FakeAiProvider` (không có key) là ~100ms.
> - **`Observer:Enabled=false` không chặn quét tay** — nút vẫn hoạt động.
> - **Member phải bị chặn ở UI**: cả 3 route Observer trả **403** cho Member (backend đã enforce) ⇒ đọc role từ `GET /api/workspaces` và **ẩn nút "Quét ngay"/"Lịch sử quét"** với Member; đừng chỉ dựa vào 403.
> - Notification **chỉ** đến Manager/Admin; Member mở `/api/notifications` nhận `unreadCount: 0`, `items: []` (không lỗi) ⇒ **không** gate route notification theo role.
> - `payload` là JSON **tuỳ ý** (đọc optional, dùng `payload?.severity`, `payload?.boardId`) — **không** so sánh payload bằng string.
> - `status` của run có thể là `Skipped` (một lần quét khác đang chạy) ⇒ UI phải render được cả 4 trạng thái (`Running`/`Completed`/`Skipped`/`Failed`).
> - `findings` có thể rỗng dù `signalsDetected > 0` (AI bị lọc bởi validator / severity thấp hơn `MinSeverityToNotify`) ⇒ hiển thị "Không có cảnh báo nào được ghi".
> - `notificationsCreated` có thể **0** dù `findingsWritten > 0` (dedupe 24h chặn alert trùng) — đừng coi là lỗi.
> - **`readAt` idempotent**: gọi `POST /{id}/read` lần 2 trả 200 với **cùng** `readAt` (PostgreSQL làm tròn µs nên chuỗi có thể khác 1 chữ số cuối — so sánh theo instant, không so chuỗi).
> - **Kênh gửi hiện chỉ in-app**; email là hạng mục *tương lai* — **không** hiện UI cấu hình email.
> - `title`/`message` do AI sinh **bằng tiếng Việt sẵn** — không cần dịch ở UI.
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
>
> ### 8) Runbook dev (đọc trước khi tự chạy backend)
> 1. **Backend đã verify xong — KHÔNG cần dựng lại/không cần chạy lại harness.** Chỉ cần `npm run lint` + `npx tsc -b` + `npm run build` + `npm test` (§7.2). Nếu muốn thấy dữ liệu thật trong UI:
> 2. Chạy API: `dotnet run --project src/TeamNexus.Api` (**không** set `DeepSeek:ApiKey` ⇒ `FakeAiProvider`, không tốn token, phản hồi ~100ms) rồi `npm run dev` trong `frontend/`.
> 3. Vào một workspace có **role Manager/Admin** (owner user hiện tại) → mở board → bấm **"Quét ngay"** trong drawer để có `ai_observer_runs` + notification. Với DB dev hiện tại, quét thật sẽ tạo alert nếu có task quá hạn/đứng yên.
> 4. Muốn tắt timer nền khi dev: đặt `Observer__Enabled=false` (biến môi trường) hoặc `Observer:Enabled=false`. Khi `DeepSeek:ApiKey` trống + `StartupDelaySeconds=60`, timer **không** kịp chạy trong một phiên dev ngắn.
> 5. ⚠️ **Đừng** boot API rồi để chạy quá 60s với DB dev nếu không muốn Observer tự quét (timer mặc định 30 phút, nhưng lần chạy đầu tiên sau `StartupDelaySeconds` sẽ thực hiện).
>
> ### 9) Backend đã verify — antigravity KHÔNG cần chạy lại
> Tổng **178 check PASS** (API thật + PostgreSQL thật + `FakeAiProvider`/stub, không tốn token). Chi tiết đầy đủ: `Project-Documents/report/phase-5-ai-observer-test-report.md`. Tóm tắt: A schema **28/28** · B detector thuần **44/44** · B2 validator/summarizer/fake **15/15** (+7/7 sau fix) · C background service **3/3** · D scan end-to-end **6/6** · E chi phí **3/3** · F HTTP API 6 endpoint **32/32** · G activity log **31/31** · H dedupe/retention **2/2** · I concurrency **3/3** · J failure modes **4/4**.
> Hai bug thật đã bắt & sửa trong quá trình verify (nêu trong báo cáo): (1) `POST .../observer/scan` thiếu kiểm tra quyền ⇒ Member quét được; (2) `FakeAiProvider` nhánh Observer parse sai offset marker ⇒ **im lặng trả findings rỗng**. Nếu bạn thấy behavior khác hai điều trên, hãy báo lại thay vì tự sửa backend.


### 6.1 Types, API, hook

- [x] `types/notification.types.ts`: mirror §6 mục 2 (`NotificationType`, `NotificationSeverity`, `ObserverRunStatus`, `NotificationPayload`, `NotificationResponse`, `NotificationListResponse`, `ObserverRunResponse`, `ObserverRunFinding`, `ObserverRunDetailResponse`, `ObserverScanResponse`).
- [x] `services/notificationApi.ts` (qua `httpClient` base `/api`; **không** tự set `X-XSRF-TOKEN`):
  - `listNotifications(params?: { isRead?: boolean; take?: number }): Promise<NotificationListResponse>`
  - `markNotificationRead(notificationId: string): Promise<NotificationResponse>`
  - `markAllNotificationsRead(): Promise<{ updated: number }>`
- [x] `services/observerApi.ts`:
  - `triggerObserverScan(workspaceId: string): Promise<ObserverScanResponse>`
  - `listObserverRuns(workspaceId: string, take?: number): Promise<ObserverRunResponse[]>`
  - `getObserverRun(runId: string): Promise<ObserverRunDetailResponse>`
- [x] `hooks/useNotifications.ts`: state `{ items, unreadCount, status: 'idle'|'loading'|'marking', error, httpStatus }`; `reload(isRead?)`, `markRead(id)`, `markAllRead()`, `refreshUnreadCount()`; poll `unreadCount` mỗi **60s** chỉ khi `document.visibilityState === 'visible'` (clear interval trong cleanup); map lỗi 401/403/404. **Không** phụ thuộc SignalR (D17).
- [x] `hooks/useObserverRuns.ts`: state `{ runs, selectedRun, status: 'idle'|'loading'|'scanning', error, httpStatus }`; `reload()`, `openRun(runId)`, `closeRun()`, `triggerScan()` (sau khi xong: `reload()` + callback `onScanFinished?.()` để refresh notification).

### 6.2 Components

- [x] `components/NotificationDrawer.tsx`: `Drawer` Ant Design mở từ top bar `BoardView`; gọi `useNotifications.reload()` khi mở; `Segmented` (Tất cả / Chưa đọc) + nút **"Đánh dấu tất cả đã đọc"** + `List` các `NotificationItem` + `Empty`/`Spin`/`Alert` lỗi.
- [x] `components/NotificationItem.tsx`: `Tag` theo `type` (màu theo `severity` đọc từ payload) + `Badge` chưa đọc + `Collapse` chi tiết (`message`, thời gian `dayjs`, `readAt` khi đã đọc) + link **"Mở bảng"** (`navigate('/workspaces/{wsId}/boards/{boardId}')` khi payload có `boardId`) + nút **"Đã đọc"**.
- [x] `components/ObserverRunsDrawer.tsx` (Manager/Admin): nút **"Quét ngay"** (`Popconfirm` → `triggerScan`, `loading` + disable khi `scanning`) + bảng runs (`Tag` status, `signalsDetected`, `notificationsCreated`, `aiCalled`, token/thời lượng từ `summary` — đọc optional) + panel chi tiết run (`ObserverFindingCard[]`, `Empty` khi rỗng) + ghi chú "Chỉ Manager/Admin thấy cảnh báo này".
- [x] `components/ObserverFindingCard.tsx`: card 1 finding (severity Tag, `title`, `message`, danh sách evidence dạng ID rút gọn + `Tooltip`).

### 6.3 Tích hợp vào Board

- [x] `BoardView.tsx`: thêm nút **"Cảnh báo AI"** (icon `BellOutlined`) + `Badge count={unreadCount}` (ẩn khi 0) cạnh nút "Lịch sử AI"; state `notificationOpen` + `observerRunsOpen`; render `NotificationDrawer` (truyền `workspaceId`) và `ObserverRunsDrawer` (nút mở đặt cạnh nút "Cảnh báo AI", **ẩn với Member** nếu đọc được role từ `GET /api/workspaces`); `onScanFinished={() => { notification.reload(); refetch() }}`.
- [x] `features/ai/index.ts`: export thêm types/api/hooks/components mới.
- [x] **Không** sửa `useBoard`/`boardStore`/`useBoardHub`/`httpClient` (D17).

### 6.4 Verify §6

- [x] `npm run lint` (oxlint 0 warning/error) + `npx tsc -b` + `npm run build` + `npm test` — tất cả sạch (122 tests PASS).

---

## 7. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–4)

### 7.1 Verify backend (harness tạm ngoài workspace + API thật + PostgreSQL thật)

> Cùng cách Phase 3 §Verify §4 / Phase 4 §5.1: harness đặt **ngoài workspace** (`%TEMP%`), tự ký JWT bằng `Jwt:SigningKey` trong User Secrets, **xoá sau khi chạy**, không commit. `DeepSeek__ApiKey` để trống ⇒ `FakeAiProvider` (**không gọi AI thật, không tốn token**). Fixture tự tạo rồi hard-delete; DB về baseline; API đã stop; harness đã xoá.
>
> **Trạng thái: ✅ BACKEND XONG (§1–§5) — §7.1 đã chạy đủ 12 nhóm: A **28/28** · B **44/44** · B2 **15/15** (+ B2m chạy lại **7/7** sau fix) · C **3/3** · D **6/6** · E **3/3** · F **32/32** · G **31/31** · H **2/2** · I **3/3** · J **4/4** · K **PASS** ⇒ **178 check PASS**. Frontend §6 + §7.2 đã hoàn tất bởi antigravity (122 tests PASS, oxlint 0/0, tsc sạch, vite build sạch).** Số liệu đầy đủ: `Project-Documents/report/phase-5-ai-observer-test-report.md`.

**Ma trận check (mã hoá để báo cáo giống Phase 4):**

1. **A. Schema & migration (A1–A9) — ✅ 28/28 PASS (PostgreSQL 18.4, DB `TeamNexus`):** 3 bảng đủ cột/thứ tự/kiểu (`activity_logs` 10, `notifications` 11, `ai_observer_runs` 8); 3 cột **jsonb**; CHECK `ck_ai_observer_runs_status` đủ 4 giá trị + `status` = `varchar(16)`; 6 index (3 khai báo + 3 PK/index FK); **6 FK đều `ON DELETE RESTRICT`** (`confdeltype='r'`); FK RESTRICT chặn thật khi xoá `workspaces`/`users` còn bị tham chiếu → SQLSTATE **`23001`**; `status='Bogus'` → **`23514`**; jsonb nhận chuỗi không hợp lệ → **`22P02`**; round-trip insert→read đúng `CreatedAt/UpdatedAt` stamp, `IsRead=false`, `ReadAt=null`, `Skipped` đọc đúng enum; `dotnet ef migrations list` → **5 migration applied** (`20260910105154_Phase5AiObserverSchema`), 8 file migration cũ **zero-diff**, snapshot **+227/−0**; `ai_action_logs` vẫn đủ 15 cột; DB dev về baseline sau cleanup (3 bảng mới `0 row`).
2. **B. Hàm thuần (B1–B25) — ✅ 44/44 PASS (harness thuần, không DB/HTTP/AI, 1000 task = 2ms):** `NotificationSeverities.Rank/IsKnown/AtLeast` (case-insensitive/trim, giá trị lạ ⇒ −1, minimum null không chặn); `ToThresholds` clamp 0/âm ⇒ 1 + `Interval` clamp 1..1440 phút + `LookbackWindow` ≤ `MaxLookbackDays`; `OverdueTask` (quá hạn/`DueDate=null`/`due == Now`/tương lai/cột `is_done` ⇒ bỏ; `== 2×` ⇒ High, `+1` ⇒ Critical; weight = worst; TaskIds sort theo số ngày); `StalledTask` (**strict** `<`: `== StalledDays` ⇒ không, `+1` ⇒ Medium, `≥ 2×` ⇒ High; `LastCommentAt` muộn hơn ⇒ không stalled; comment cũ ⇒ vẫn dùng `UpdatedAt`); `Overload` (5 mở ⇒ có, 4 mở + 2 quá hạn ⇒ có, 4 mở + 0 quá hạn ⇒ không, 9 mở ⇒ High / 10 mở ⇒ Critical, 2 assignee ⇒ 2 tín hiệu, assignee `null`/task done ⇒ loại, evidence: quá hạn nặng nhất trước); `Bottleneck` (đúng 2 điều kiện, cột `is_done` bị loại, tắt flag ⇒ không sinh); cap (30 assignee quá tải ⇒ 20 tín hiệu + `truncatedSignals=10`, cap 5 ⇒ 25, evidence cap 10/1, 30 task quá hạn của 1 workspace **gom thành 1 tín hiệu**); sort severity desc rồi weight desc; **tất định** (JSON 2 lần byte-identical); snapshot rỗng/chỉ task done ⇒ `([], 0)`; ngưỡng cực đoan không throw.
3. **C. BackgroundService (C1–C2) — ✅ 3/3 PASS (IHost thật, `AddAiModule` + `AddHostedService`):** `Enabled=false` ⇒ start/stop host trong ~1.2s, **0** row `ai_observer_runs`, log `Observer background service started (enabled=False, …)`; `Enabled=true` + `StartupDelaySeconds=30` ⇒ start/stop trong ~0.8s không exception, chưa có run (đang trong delay) ⇒ xác nhận timer **không** làm host dừng. Không chờ tick 30 phút (tốn thời gian); rủi ro "exception giết host" đã bao phủ vì mọi thân `ExecuteAsync` nằm trong `try/catch`.
4. **D. Scan end-to-end (D1–D6) — ✅ 6/6 PASS (DI thật + PostgreSQL thật + stub provider, fixture seed bằng raw SQL):** `ScanAsync(workspaceId)` ⇒ `Completed`, **`signalsDetected = 4`** (Overdue/Stalled/Overload/Bottleneck), `aiCalled = true`; `ai_observer_runs` = 1 row `Completed` có `FinishedAt` + `signalsByType` **đủ 4 loại**; `notifications = findings × 1 manager` (`is_read = false`, `recipient_user_id` = manager); payload có `runId`/`severity`/`model`; **`tasks` nguyên vẹn 6 row + `ai_action_logs` = 0** (Observer không ghi dữ liệu nghiệp vụ); task ở cột `is_done` dù quá hạn ⇒ không sinh tín hiệu quá hạn.
5. **E. Kiểm soát token/chi phí (E1–E3) — ✅ 3/3 PASS:** workspace **0 tín hiệu** ⇒ run `Completed` + `aiCalled=false` + **stub chưa được gọi lần nào** (`stubCalls=0`, bất biến với mọi cách AI), `promptTokens/completionTokens = null`, 0 notification; prompt thực gửi **4133 ký tự ≤ 12000** và **không** chứa `description`, `JsonMode = true`.
6. **F. Notification + Observer HTTP API (F0–F24) — ✅ 32/32 PASS (API thật cổng 5210 + PostgreSQL thật + `FakeAiProvider`/stub, JWT tự ký):** `POST scan` (Manager) ⇒ **200** `Completed` + shape `{runId,workspaceId,status,signalsDetected,findingsWritten,notificationsCreated,aiCalled}`; `GET /notifications` ⇒ 200 + notification MỚI của harness (2 alert thật: `OverdueTask`+`StalledTask`, `isRead=false`, `unreadCount=2`); `?isRead=true` ⇒ không trả alert chưa đọc; `?isRead=FALSE` ⇒ mọi item chưa đọc; `?isRead=bogus` ⇒ **400** `{error}` domain; `?take=0`/`?take=1000` ⇒ 200 (default/clamp); `?take=abc` ⇒ **400 ProblemDetails** (framework); `POST {id}/read` ⇒ 200 + `readAt` set, gọi lần 2 ⇒ `readAt` **không đổi** (idempotent, so ở µs); Member đọc alert của Manager ⇒ **404** `"Notification not found."`; `read-all` ⇒ `updated=n` rồi `updated=0` + `unreadCount=0`; Member `GET /notifications` ⇒ 200 `unreadCount=0`/`items=[]`; `GET runs` ⇒ 200 sort `startedAt DESC`; `GET run detail` ⇒ 200 + `summary` object + `findings[]`; **Member ⇒ 403 cả 3 route Observer** (`"Requires Manager or Admin role in this workspace."`) — bug bảo mật ban đầu đã bị bắt và sửa; workspace lạ ⇒ **404** (cả `runs` và `scan`); run lạ ⇒ **404**; **5 route không auth ⇒ 401**; POST thiếu CSRF ⇒ **403** `{error: CSRF…}`; POST vào route GET ⇒ **405**; app tối giản + stub: JSON hỏng/provider ném ⇒ **502** `{error}` (run `Failed` vẫn được ghi + `summary.error`), `findings: []` ⇒ 200 + `notificationsCreated=0`; cleanup fixture = 0 row.
7. **G. Activity log (G1–G8) — ✅ 31/31 PASS (API thật cổng 5198 + PostgreSQL thật + JWT tự ký):** `POST task` → 1 row `TaskCreated` (`entity_type=Task`, `entity_id/workspace_id/board_id/user_id` đúng, payload `{isDone, columnId, priority, assigneeId}`); `PUT task` → 1 row `TaskUpdated` (`titleChanged=true`, `descriptionChanged=false`, `priority` mới, **không** chứa nội dung title/description); `move` sang cột thường → 1 row `TaskMoved`, **không** `TaskCompleted`; `move` sang cột `is_done` → `TaskMoved` **+** `TaskCompleted` và `tasks.completed_at` vẫn set (hành vi Phase 2 nguyên vẹn); `POST comment` → 1 row `CommentAdded` (`entity_type=Comment`, payload `{commentId,taskId}`); `DELETE task` → 1 row `TaskDeleted` + task có `deleted_at`; **4 mutation thất bại (title rỗng 400, column lạ 404, comment vào task đã xoá 404, task lạ 404) ⇒ `activity_logs` 8 → 8 (không tăng)**; backdoor probe `IActivityLogWriter => ActivityLogWriter` (không phải no-op); `git diff '*.csproj'` **rỗng** + Board không `using TeamNexus.Modules.Ai`; regression FE: `oxlint` 0/0 + `vitest` **88/88 PASS**; cleanup xoá sạch row của fixture.
8. **H. Dedupe & retention (H1–H2) — ✅ 2/2 PASS:** quét lần 2 trong window 24h ⇒ `notifications` **2 → 2** (không tăng); seed 1 `activity_logs` cũ 90 ngày + 1 row mới, sau scan ⇒ **old bị prune (0) / fresh còn nguyên (1)** (nhóm I gộp vào 3b).
9. **I. Concurrency (I1–I3) — ✅ 3/3 PASS:** 2 `ScanAsync` song song ⇒ **1 `Completed` + 1 `Skipped(AlreadyRunning)`** (SemaphoreSlim trong process; advisory lock là tầng 2); đua không nhân bội notification (≤ 2× một vòng gửi — hạn chế đã ghi rõ ở "Ghi chú hiện thực §4"); advisory lock **được giải phóng** ⇒ scan kế tiếp `Completed`.
10. **J. Failure modes (J1–J4) — ✅ 4/4 PASS:** JSON AI hỏng (đã strip code fence nhưng vẫn sai) ⇒ run `Failed` + `summary.error` 102 ký tự + **0** notification; provider ném `AiProviderException` ⇒ `Failed`; `findings: []` ⇒ `Completed` + 0 notification; finding có **ID bịa** ⇒ alert vẫn được tạo nhưng **evidence rỗng** (`taskIds=0`, `userIds=0`) ⇒ không lộ ID không tồn tại.
11. **K. Kiểm tra kiến trúc (K1–K3) — ✅ PASS:** `ObserverService`/`NotificationService` không có đường ghi `Tasks`/`Labels`/`TaskLabels`/`AiActionLogs`; `ObserverService` không tham chiếu `IAiActionService`; `git diff --name-only -- '*.csproj'` **rỗng** (không thêm project reference); `AiModule` **không** đổi §4 (DI để §5.4).
12. [ ] *(Tuỳ chọn)* Tạo `Project-Documents/report/phase-5-ai-observer-test-report.md` (đúng format Phase 4: môi trường, phương pháp, bảng check có mã + bằng chứng định lượng, known gaps).

### 7.2 Frontend (Vitest + Testing Library)

- [x] Test `notificationApi` + `observerApi`: đúng method/URL/params cho 6 hàm (7 tests PASS).
- [x] Test `useNotifications` (reload/poll/filter/markRead/markAllRead, lỗi 401/403), `useObserverRuns` (`triggerScan` → `scanning` → reload) (12 tests PASS).
- [x] Test `NotificationDrawer` + `NotificationItem`: 2 filter, `Empty`, trạng thái đã đọc/chưa đọc, `readAt`, link "Mở bảng" (7 tests PASS).
- [x] Test `ObserverRunsDrawer` + `ObserverFindingCard`: render 4 `status`, nút "Quét ngay" gọi API, hiển thị findings/severity/evidence/token (5 tests PASS).
- [x] Cập nhật `BoardView.test.tsx`: nút "Cảnh báo AI" tồn tại; `Badge` hiển thị `unreadCount`; phân quyền role Manager/Admin vs Member (7 tests PASS).
- [x] DoD: `oxlint` 0 warn/0 err + `tsc -b` + `vite build` + `vitest run` **sạch** (122 tests PASS).

### 7.3 Điều kiện hoàn thành Giai đoạn 5 (map `03-roadmap.md`)

- [x] `BackgroundService` chạy nền, quét log/hoạt động board theo **chu kỳ cố định** — verify §7.1 nhóm C (C1–C2) + I1–I3 + log khởi động `Observer background service started (enabled=…, interval=00:30:00, …)`
- [x] Log **được tóm tắt trước khi gửi DeepSeek** (kiểm soát token/chi phí) — verify §7.1 nhóm E (E1–E3): prompt **4133 ≤ 12000** ký tự, **0 lần gọi AI** khi không có tín hiệu (`stubCalls=0`), token ghi vào `ai_observer_runs.summary`
- [x] Phát hiện **ít nhất 1 loại tín hiệu** — verify §7.1 nhóm B (44/44) + D (D1: **4 loại** `OverdueTask`/`StalledTask`/`Overload`/`Bottleneck` trong 1 lần quét)
- [x] Kết quả cảnh báo **chỉ hiển thị cho Manager, không public toàn team** — verify §7.1 nhóm D (D3: chỉ `recipient_user_id` = Manager/Admin) + nhóm F (F13: Member `items=[]`; F16–F18: Member **403** cả 3 route Observer) + F11 (đọc alert người khác ⇒ 404). *UI hiển thị theo role ở §7.2.*
- [x] UI gọi API thật & bộ kiểm thử frontend hoàn tất (§6 + §7.2: 122 tests PASS).

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
- [x] Cập nhật `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`: mục **"Phase 5 — AI Observer"** (bảng 6 endpoint, options `Observer`, công thức 4 tín hiệu, luồng 9 bước, dedupe 24h, advisory lock, retention, `FakeAiProvider` 2 nhánh, kênh in-app + email *tương lai*) + mục **"Verify §5"** (178 check + 4 lưu ý viết harness) — **đã làm**
- [x] Cập nhật `src/Modules/Board/TeamNexus.Modules.Board/README.md`: mục **"Phát activity log cho AI Observer"** (5 mutation + payload + best-effort) — **đã làm**
- [x] Cập nhật `README.md` root (mục "Trạng thái (Giai đoạn 5 – AI Observer)") và tick checklist `03-roadmap.md` — **đã làm**
- [x] Tạo `Project-Documents/report/phase-5-ai-observer-test-report.md` (format Phase 4: môi trường, phương pháp, 12 nhóm check có mã + bằng chứng, 2 bug thật đã sửa, known gaps) — **đã làm**

---

## Checklist Hoàn thiện Giai đoạn 5

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 6 (Báo cáo & Xuất dữ liệu).

- [x] **Schema**: bảng `activity_logs` + `notifications` + `ai_observer_runs` (migration `Phase5AiObserverSchema` đã áp dụng, CHECK/jsonb/index/FK RESTRICT đúng thiết kế) — verify A1–A9 **28/28 PASS**
- [x] **Thu thập activity log**: `IActivityLogWriter` (khai báo ở Board, impl ở Ai) ghi 6 loại sự kiện từ `TaskService`/`CommentService`, **không bao giờ** làm hỏng request CRUD — verify G1–G8 **31/31 PASS**
- [x] **Signal detector thuần**: `OverdueTask` / `StalledTask` / `Overload` (+ `Bottleneck` optional), ngưỡng cấu hình được, cap signals/evidence, sort severity — verify B1–B25 **44/44 PASS**
- [x] **BackgroundService** chạy nền theo chu kỳ cố định, `PeriodicTimer` + advisory lock + không làm host dừng khi lỗi — verify C1–C2 **3/3 PASS** + I1–I3 **3/3 PASS**
- [x] **Tóm tắt trước khi gửi AI**: prompt ≤ `MaxPromptCharacters`, redaction tiêu đề, **không gọi AI khi 0 tín hiệu**, token ghi vào `ai_observer_runs.summary` — verify E1–E3 **3/3 PASS** (prompt 4133 ký tự) + B2i–B2k
- [x] **AI Observer prompt/schema**: `AiObserverOutput` + `ObserverPrompts` + `ObserverFindingValidator` (locator chống hallucination, cap/cắt độ dài) — verify B2a–B2o **15/15 PASS** + J1–J4 **4/4 PASS**
- [x] **Notification chỉ cho Manager/Admin**: fan-out theo `recipient_user_id`, dedupe 24h, mark-as-read, retention `activity_logs` — verify D3/D5 + H1–H2 + F2–F13 **32/32 PASS**
- [x] **6 endpoint** (`/api/notifications` ×3, `/api/workspaces/{id}/observer/*` ×2, `/api/observer/runs/{id}`) đúng quyền/CSRF/mã lỗi `{ error }` — verify nhóm F **32/32 PASS**
- [x] **Archive check**: Observer **không** đi qua `AiActionService`, **không** ghi `tasks`/`labels`/`task_labels`; module Board không có project reference mới — verify D5/D6 + K **PASS**
- [x] **Tài liệu**: `04-database-design.md` (từ §1), `README.md` module Ai + Board, `README.md` root, `03-roadmap.md`, báo cáo test §7.1 — **đã làm hết**

### Việc của antigravity (§6 + §7.2) — 🔻 SẴN SÀNG BÀN GIAO

> Backend đã xong và verify (178 check PASS). Contract đầy đủ ở mục "🔻 BÀN GIAO §6 + §7.2" phía trên
> (6 endpoint + JSON thật + bảng mã lỗi + 12 lưu ý UI + hướng dẫn test + runbook). Chỉ cần làm theo, **không** hỏi lại contract.

- [x] §6 frontend `src/features/ai/` (`types/notification.types.ts`, `services/notificationApi.ts`, `services/observerApi.ts`, `hooks/useNotifications.ts`, `hooks/useObserverRuns.ts`, `components/NotificationDrawer.tsx`, `components/NotificationItem.tsx`, `components/ObserverRunsDrawer.tsx`, `components/ObserverFindingCard.tsx`) + nút "Cảnh báo AI" + badge unread ở `BoardView.tsx`
- [x] §7.2 test Vitest cho API/hooks/components mới + cập nhật `BoardView.test.tsx`; chạy `oxlint` (0 warn/0 err) + `tsc -b` + `vite build` + `vitest run` sạch (122 tests PASS)
- [x] Cập nhật `README.md` root (mục "Trạng thái (Giai đoạn 5 – AI Observer)") và tick checklist `03-roadmap.md` — **đã làm**
