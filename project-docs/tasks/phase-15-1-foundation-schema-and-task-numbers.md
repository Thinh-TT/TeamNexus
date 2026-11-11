# Giai đoạn 15.1 — Nền tảng Database (Migration #15), Mã công việc `TN-123`, Cột `is_in_review` & Port `ITaskAutomation`

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F4`, `F5`, `F6`, `F8`) & §5 (Database Schema Design).
> **Trạng thái:** ✅ **ĐÃ HOÀN THÀNH (`T15.1.1` → `T15.1.5`) — 1.175/1.175 Backend Tests PASS, 0 Warning / 0 Error.**

---

## 1. Mục tiêu của Sub-phase 15.1

1. Tạo **Migration #15 (`Phase15ExternalIntegrations`)** bổ sung đầy đủ 6 bảng mới và các cột mở rộng trên `users`, `workspaces`, `board_columns`, `board_tasks`.
2. Cấp phát **mã định danh công việc tuần tự theo từng Workspace (`TN-1`, `TN-2`, ...)** cho cả công việc cũ (backfill trong migration) và công việc mới tạo.
3. Bổ sung **cờ ý nghĩa hệ thống thứ 3 (`is_in_review`)** trên `board_columns` (loại trừ lẫn nhau với `is_done` và `is_clarification`) để phục vụ tự động chuyển cột khi mở Pull Request.
4. Khởi tạo tài khoản hệ thống **`GitHub Bot`** (`github-bot@system.teamnexus.invalid`, `is_system = true`) và khóa mọi đường đăng nhập OAuth/JWT của tài khoản này.
5. Cung cấp port nội bộ **`ITaskAutomation`** trong module `Board` để module `Ai` (GitHub Webhook) có thể tạo thẻ, chuyển cột và ghi bình luận hệ thống mà không vi phạm ràng buộc `workspace_members`.

---

## 2. Danh sách Tasks Chi tiết (`T15.1.1` → `T15.1.5`)

### Task `T15.1.1` — Mở rộng 4 Entities hiện có (`ApplicationUser`, `Workspace`, `BoardColumn`, `BoardTask`)

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/TeamNexus.Persistence/Data/Entities/ApplicationUser.cs`
  - `src/TeamNexus.Persistence/Data/Configurations/ApplicationUserConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/Workspace.cs`
  - `src/TeamNexus.Persistence/Data/Configurations/WorkspaceConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/BoardColumn.cs`
  - `src/TeamNexus.Persistence/Data/Configurations/BoardColumnConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/BoardTask.cs`
  - `src/TeamNexus.Persistence/Data/Configurations/BoardTaskConfiguration.cs`
- **Chi tiết thực hiện:**
  1. **`ApplicationUser`:** Thêm `public bool IsSystem { get; set; } = false;` → map cột `is_system` (`boolean NOT NULL DEFAULT false`).
  2. **`Workspace`:** Thêm `public long NextTaskNumber { get; set; } = 1;` → map cột `next_task_number` (`bigint NOT NULL DEFAULT 1`).
  3. **`BoardColumn`:**
     - Thêm `public bool IsInReview { get; set; } = false;` → map cột `is_in_review` (`boolean NOT NULL DEFAULT false`).
     - Thêm partial unique index trong `BoardColumnConfiguration`:
       ```csharp
       builder.HasIndex(c => c.BoardId)
           .IsUnique()
           .HasDatabaseName("IX_board_columns_board_id_single_in_review")
           .HasFilter("is_in_review = true");
       ```
  4. **`BoardTask`:**
     - Thêm `public long TaskNumber { get; set; }` → map cột `task_number` (`bigint NOT NULL DEFAULT 0`).
     - Thêm navigation `public ICollection<TaskExternalLink> ExternalLinks { get; set; } = new List<TaskExternalLink>();`.

---

### Task `T15.1.2` — Tạo 6 Entities mới, EF Configurations & Migration #15 (`Phase15ExternalIntegrations`)

- **Files tạo mới (`[NEW]`):**
  - `src/TeamNexus.Persistence/Data/Entities/TaskNumberEntry.cs` & `Configurations/TaskNumberEntryConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/WorkspaceWebhookEndpoint.cs` & `Configurations/WorkspaceWebhookEndpointConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/WebhookDeliveryOutbox.cs` & `Configurations/WebhookDeliveryOutboxConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/WorkspaceGitHubIntegration.cs` & `Configurations/WorkspaceGitHubIntegrationConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/GitHubWebhookDelivery.cs` & `Configurations/GitHubWebhookDeliveryConfiguration.cs`
  - `src/TeamNexus.Persistence/Data/Entities/TaskExternalLink.cs` & `Configurations/TaskExternalLinkConfiguration.cs`
  - `src/TeamNexus.Persistence/Migrations/<Timestamp>_Phase15ExternalIntegrations.cs`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/TeamNexus.Persistence/Data/TeamNexusDbContext.cs`
- **Chi tiết thực hiện:**
  1. Định nghĩa 6 bảng theo đúng đặc tả tại `07-external-integrations-design.md` §5.2:
     - `task_numbers` (`workspace_id`, `task_number`, `task_id` — PK `(workspace_id, task_number)`, Unique `task_id`).
     - `workspace_webhook_endpoints` (lưu cấu hình Slack/Discord webhook của từng workspace).
     - `webhook_delivery_outbox` (hàng đợi gửi thông báo bất đồng bộ + retry).
     - `workspace_github_integrations` (1–1 với `workspaces`, lưu `webhook_token`, `secret_ciphertext`, `default_board_id`, các cờ tự động hóa).
     - `github_webhook_deliveries` (bảng chống trùng lặp `X-GitHub-Delivery` + nhật ký kiểm toán).
     - `task_external_links` (liên kết đa-đa giữa `board_tasks` và GitHub PR/Issue).
  2. Trong `Up()` của Migration `Phase15ExternalIntegrations`:
     - Chạy SQL backfill `task_number` cho mọi task hiện có (kể cả soft-deleted) theo thứ tự `(created_at ASC, id ASC)` trong từng workspace.
     - Chèn dữ liệu tương ứng vào `task_numbers` và cập nhật `workspaces.next_task_number = MAX(task_number) + 1`.
     - Seed tài khoản hệ thống `GitHub Bot` vào bảng `users`:
       - `Id = 00000000-0000-0000-0000-00000000b071`
       - `UserName = "github-bot"`, `NormalizedUserName = "GITHUB-BOT"`
       - `Email = "github-bot@system.teamnexus.invalid"`, `NormalizedEmail = "GITHUB-BOT@SYSTEM.TEAMNEXUS.INVALID"`
       - `DisplayName = "GitHub Bot"` *(khớp thuộc tính `ApplicationUser.DisplayName` / cột `display_name`)*
       - `IsSystem = true`

---

### Task `T15.1.3` — Bảo vệ Tài khoản Hệ thống (`IsSystem`) trong Module `Auth`

- **Files tạo mới / chỉnh sửa (`[NEW]` / `[MODIFY]`):**
  - `[NEW]` `src/Shared/TeamNexus.Shared/SystemUsers.cs`
  - `[MODIFY]` `src/Modules/Auth/TeamNexus.Modules.Auth/Services/AuthService.cs`
- **Chi tiết thực hiện:**
  - Định nghĩa hằng số dùng chung `SystemUsers.GitHubBotUserId = Guid.Parse("00000000-0000-0000-0000-00000000b071")` (trong `TeamNexus.Shared`).
  - Trong `AuthService.HandleExternalLoginAsync` và `RotateRefreshTokenAsync`: kiểm tra `if (user.IsSystem) throw new InvalidOperationException("Tài khoản hệ thống không được phép đăng nhập trực tiếp.");`.

---

### Task `T15.1.4` — Nâng cấp `ColumnService` & `ColumnDtos` hỗ trợ Cột `IsInReview`

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Board/TeamNexus.Modules.Board/DTOs/ColumnDtos.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/ColumnService.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/BoardService.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/DtoMapping.cs`
- **Chi tiết thực hiện:**
  1. **Append-only DTOs (`ColumnDtos.cs`):**
     - `ColumnResponse`: thêm `bool IsInReview = false` ở cuối.
     - `CreateColumnRequest`: thêm `bool IsInReview = false` ở cuối.
     - `UpdateColumnRequest`: thêm `bool? IsInReview = null` ở cuối.
  2. **Kiểm tra loại trừ lẫn nhau 3 cờ (`ColumnService.cs`):**
     - Thay `RequireOnlyOneSystemMeaning(bool isDone, bool isClarification)` bằng:
       ```csharp
       private static void RequireOnlyOneSystemMeaning(bool isDone, bool isClarification, bool isInReview)
       {
           var activeCount = (isDone ? 1 : 0) + (isClarification ? 1 : 0) + (isInReview ? 1 : 0);
           if (activeCount > 1)
           {
               throw new BadRequestException(
                   "Một cột chỉ có thể đóng một vai trò hệ thống (Hoàn thành, Cần làm rõ hoặc Đang duyệt PR).");
           }
       }
       ```
     - Khi bật `IsInReview = true` trên một cột, nếu trên cùng board đã có cột khác `IsInReview = true`, xử lý đồng nhất với quy tắc của `IsDone` / `IsClarification`.
  3. **Cột mặc định khi tạo Board mới (`BoardService.cs`):**
     - Giữ tương thích với các bài test hiện tại (hoặc bổ sung tùy chọn tạo cột `In Review` chuẩn). *Lưu ý:* Kiểm tra kỹ các bài test Integration của Phase 2 (`BoardApiTests`) xem có assert cứng số lượng cột mặc định khi tạo Board hay không trước khi thay đổi danh sách cột seed mặc định.

---

### Task `T15.1.5` — Cấp phát `TaskNumber` (`TN-123`), Mở rộng `TaskResponse` & Xây dựng Port `ITaskAutomation`

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/ITaskAutomation.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/TaskAutomationService.cs`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Board/TeamNexus.Modules.Board/DTOs/TaskDtos.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/DtoMapping.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/TaskReadHelpers.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/TaskService.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/TaskSearchService.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/BoardModule.cs`
- **Chi tiết thực hiện:**
  1. **Mở rộng `TaskDtos.cs`:**
     - Thêm record `TaskExternalLinkSummaryResponse(Guid Id, string Provider, string LinkType, string ExternalId, string ExternalUrl, string Title, string State, DateTimeOffset UpdatedAt)`.
     - Append `long TaskNumber = 0, IReadOnlyList<TaskExternalLinkSummaryResponse>? ExternalLinks = null` vào cuối `TaskResponse`.
  2. **Cấp phát `TaskNumber` trong `TaskService.CreateTaskAsync` & `TaskAutomationService`:**
     - Viết helper `AllocateNextTaskNumberAsync(Guid workspaceId, CancellationToken ct)` tăng `Workspace.NextTaskNumber`, gán vào `BoardTask.TaskNumber` và thêm bản ghi `TaskNumberEntry` (`task_numbers`).
  3. **Hỗ trợ tìm kiếm theo `TN-123` trong `TaskSearchService`:**
     - Nếu từ khóa tìm kiếm có dạng `TN-123`, `tn-123` hoặc `#123`, trích xuất số `123` và khớp chính xác với `t.TaskNumber == 123` bên cạnh tìm kiếm `ILIKE` theo tiêu đề/mô tả.
  4. **Triển khai `ITaskAutomation` (`TaskAutomationService`):**
     - `MoveToSystemColumnAsync(Guid taskId, SystemColumnTarget target, string reason, CancellationToken ct)` (`target`: `InReview` | `Done`): chuyển cột, cập nhật `Position`, đóng dấu `CompletedAt` (nếu sang `Done` hoặc rời `Done`), ghi `ActivityLog` (`user_id = null`), phát SignalR `TaskMoved` & `TaskUpdated`, trả về snapshot trước/sau để tạo `AiActionLog`.
     - `CreateTaskFromExternalIssueAsync(Guid boardId, string title, string? description, CancellationToken ct)`: tạo task ở cột đầu tiên của board, cấp `TaskNumber`, ghi `ActivityLog`, phát SignalR `TaskCreated`.
     - `ApplySystemTriageSuggestionsAsync(Guid taskId, string? priority, decimal? estimatedHours, IReadOnlyList<string>? tags, Guid? assigneeId, CancellationToken ct)`: áp dụng kết quả phân loại Jev 1.13 lên task vừa tạo.
     - `AddSystemCommentAsync(Guid taskId, Guid systemUserId, string markdownContent, CancellationToken ct)`: thêm bình luận dưới tên `GitHub Bot` và phát SignalR `CommentAdded`.

---

## 3. Tiêu chí Nghiệm thu (Definition of Done — Sub-phase 15.1)

- [x] `dotnet build TeamNexus.sln --no-incremental` đạt `0 Warning(s), 0 Error(s)`.
- [x] `dotnet ef migrations has-pending-model-changes` báo không có thay đổi model treo (`No changes have been made to the model since the last migration.`).
- [x] Mọi task tạo mới qua `POST /api/boards/{boardId}/tasks` đều nhận `taskNumber >= 1` tăng dần theo từng workspace và trả về trong `TaskResponse`.
- [x] Tìm kiếm công việc với từ khóa `TN-1`, `tn-1` hoặc `#1` trả về đúng thẻ có `taskNumber = 1`.
- [x] Bật đồng thời 2 trong 3 cờ (`isDone`, `isClarification`, `isInReview`) trên cùng 1 cột bị từ chối với `400 Bad Request` và thông báo tiếng Việt rõ ràng.
- [x] Tài khoản `IsSystem = true` bị chặn hoàn toàn ở `AuthService`.

---

## 4. NHẬT KÝ THI HÀNH SUB-PHASE 15.1 (`T15.1.1` → `T15.1.5`)

> **Số đo thực tế:**
> - **Backend Build:** `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` → **0 Warning(s), 0 Error(s)**.
> - **EF Core Migration #15:** `20261110154843_Phase15ExternalIntegrations` · `dotnet ef migrations has-pending-model-changes` → **No changes have been made to the model since the last migration.**
> - **Backend Tests (`xUnit` trên PostgreSQL `pgvector/pgvector:pg18`):** **Passed: 1,175 / Failed: 0 / Skipped: 0 / Total: 1,175** (Baseline 1,167 ⇒ **+8 bài kiểm thử tích hợp mới** trong `Phase15FoundationTests`).

### 4.1. Quyết định Kỹ thuật Đã Chốt (`K1..K4`)

| # | Quyết định | Lý do kỹ thuật & Bằng chứng |
|---|---|---|
| **K1** | **Truyền tên model index tường minh trong `BoardColumnConfiguration`:** `builder.HasIndex(c => c.BoardId, "IX_board_columns_board_id_single_in_review")` | Trong EF Core, gọi `builder.HasIndex(c => c.BoardId)` lần thứ hai trên cùng một thuộc tính `BoardId` mà không truyền tên model index sẽ ghi đè lên index không tên trước đó (`uq_board_columns_clarification`), làm migration sinh lệnh `DropIndex("uq_board_columns_clarification")`. Truyền tên model index tường minh giúp giữ nguyên `uq_board_columns_clarification` và tạo mới `IX_board_columns_board_id_single_in_review`. |
| **K2** | **Cấp phát `TaskNumber` nguyên tử bằng `UPDATE workspaces SET next_task_number = next_task_number + 1 ... RETURNING next_task_number - 1 AS "Value"`** | Đảm bảo hai request tạo thẻ đồng thời trên cùng workspace không bao giờ nhận trùng `task_number`, đồng thời đồng bộ lại `trackedWorkspace.Entity.NextTaskNumber` trên `ChangeTracker` để `SaveChangesAsync()` không ghi đè giá trị cũ. |
| **K3** | **Khớp Global Query Filter trên `TaskExternalLinkConfiguration` (Pitfall T6)** | `BoardTask` có Global Query Filter soft-delete (`DeletedAt == null`). Khai báo filter tương ứng trên `TaskExternalLink` (`x.Task == null || (x.Task.DeletedAt == null && ...)`) giúp triệt tiêu cảnh báo EF Core `10622` và giữ vững cổng `0 Warning`. |
| **K4** | **Khôi phục `GitHub Bot` trong `TestScenario.ResetDatabaseAsync()` và `TaskAutomationService`** | Bộ khung kiểm thử tích hợp chạy `TRUNCATE TABLE ... "users" RESTART IDENTITY CASCADE` trước mỗi test, xóa sạch dữ liệu seed từ migration. Việc tự động re-seed tài khoản `GitHub Bot` (`00000000-0000-0000-0000-00000000b071`) ngay sau `TRUNCATE` đảm bảo tính cô lập giữa các test mà vẫn giữ nguyên vẹn khóa ngoại. |

### 4.2. Lệch Kế Hoạch Có Chủ Ý (`L1..L2`)

| # | Lệch | Lý do |
|---|---|---|
| **L1** | **Giữ nguyên 3 cột mặc định khi tạo Board mới (`Cần làm`, `Đang làm`, `Hoàn thành`) trong `BoardService.CreateBoardAsync`** | Theo đúng lưu ý ở `T15.1.4` mục 3: các bài kiểm thử hiện có kỳ vọng Board mới khởi tạo với 3 cột tiêu chuẩn. Vai trò `isInReview` được bật linh hoạt khi tạo cột mới hoặc cập nhật cột hiện có qua `POST/PUT /api/boards/{boardId}/columns`. |
| **L2** | **Cập nhật đúng 1 assertion đếm số migration trong `Phase147KnowledgeTests.Schema_MigrationChainEndsWithTheTwoTrackBMigrations` (`14 → 15`)** | Bài test của Phase 14.7 Track B kiểm tra tổng số dòng trong `__EFMigrationsHistory`. Khi Sub-phase 15.1 bổ sung Migration #15 (`Phase15ExternalIntegrations`), tổng số migration tăng từ `14` lên `15` (2 migration của Track B vẫn được xác minh chính xác theo thứ tự). |

### 4.3. Bug Thật Bắt Được Trong Lúc Thi Hành (`P1..P2`)

| # | Bug | Nguyên nhân & Cách khắc phục |
|---|---|---|
| **P1** | **`InvalidOperationException: 'FromSql' or 'SqlQuery' was called with non-composable SQL` khi gọi `.SingleAsync(ct)` trên `SqlQueryRaw("UPDATE ... RETURNING")`** | Toán tử LINQ `.SingleAsync(ct)` trên `IQueryable<long>` khiến EF Core 10 cố gắng bọc câu lệnh SQL vào subquery `SELECT ... FROM (UPDATE ...) LIMIT 2`, vi phạm cú pháp SQL của PostgreSQL. **Cách sửa:** Đổi sang `.ToListAsync(ct)` rồi gọi `.Single()` trên bộ nhớ để EF Core thực thi trực tiếp câu lệnh `UPDATE ... RETURNING` mà không bọc subquery. |
| **P2** | **Race condition trong `DatabaseFixture.EnsureMigratedAsync()` khi áp dụng migration mới lên DB test đã có sẵn các migration cũ** | `WaitForMigrationsAsync()` chỉ kiểm tra `COUNT(*) FROM "__EFMigrationsHistory" > 0`. Khi DB test đã có 14 migration từ Phase 14.7, các class test chạy song song thấy `14 > 0` nên lập tức chạy `TRUNCATE` trong khi luồng đầu tiên đang chạy `ALTER TABLE` của Migration #15, gây lỗi thiếu cột hoặc deadlock `40P01`. **Cách sửa:** Bọc `EnsureMigratedAsync()` bằng `await DatabaseLock.WaitAsync()` để tuần tự hóa bước migrate trong tiến trình test trước khi bất kỳ scenario nào chạy `ResetDatabaseAsync()`. |


