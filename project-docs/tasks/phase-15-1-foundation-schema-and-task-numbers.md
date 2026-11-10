# Giai đoạn 15.1 — Nền tảng Database (Migration #15), Mã công việc `TN-123`, Cột `is_in_review` & Port `ITaskAutomation`

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F4`, `F5`, `F6`, `F8`) & §5 (Database Schema Design).
> **Trạng thái:** 📋 Kế hoạch chi tiết (`T15.1.1` → `T15.1.5`).

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

- [ ] `dotnet build TeamNexus.sln --no-incremental` đạt `0 Warning(s), 0 Error(s)`.
- [ ] `dotnet ef migrations has-pending-model-changes` báo không có thay đổi model treo.
- [ ] Mọi task tạo mới qua `POST /api/boards/{boardId}/tasks` đều nhận `taskNumber >= 1` tăng dần theo từng workspace và trả về trong `TaskResponse`.
- [ ] Tìm kiếm công việc với từ khóa `TN-1` hoặc `#1` trả về đúng thẻ có `taskNumber = 1`.
- [ ] Bật đồng thời 2 trong 3 cờ (`isDone`, `isClarification`, `isInReview`) trên cùng 1 cột bị từ chối với `400 Bad Request` và thông báo tiếng Việt rõ ràng.
- [ ] Tài khoản `IsSystem = true` bị chặn hoàn toàn ở `AuthService`.

