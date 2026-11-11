# Giai đoạn 15.3 — Inbound GitHub Webhook Core, `HMAC-SHA256` & PR State Machine (`TN-123`)

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §3.2 (`D17`, `D18`, `D20`, `D22`), §4.2, §6.2, §6.3 & §7.
> **Trạng thái:** ✅ ĐÃ HOÀN THÀNH (`T15.3.1` → `T15.3.5` hoàn tất, 0 warnings/errors, 645/645 Pure tests pass, 14 integration tests).

---

## 1. Mục tiêu của Sub-phase 15.3

1. Cho phép mỗi Workspace bật tích hợp GitHub với **`WebhookToken` (GUID riêng biệt)** và **`WebhookSecret` (32 bytes ngẫu nhiên)** được mã hóa an toàn bằng ASP.NET Core `IDataProtectionProvider`.
2. Xác thực chữ ký **`X-Hub-Signature-256` (`HMAC-SHA256`)** trên raw request body bằng phép so sánh hằng thời gian (`CryptographicOperations.FixedTimeEquals`) và đảm bảo **idempotency tuyệt đối** theo `X-GitHub-Delivery`.
3. Xây dựng bộ phân tích mã công việc **`TaskKeyParser`** nhận diện `TN-123`, `tn-123`, `#123` trong tiêu đề PR, tên nhánh (`feat/TN-42-auth`), mô tả PR và bình luận.
4. Hiện thực hóa **PR State Machine**: tự động liên kết Pull Request vào `task_external_links`, tự động kéo thẻ sang cột `In Review` (`is_in_review = true`) khi mở PR và sang cột `Done` (`is_done = true` + đóng dấu `CompletedAt`) khi merge PR.
5. Ghi nhận mọi lần tự động chuyển cột bởi GitHub PR vào bảng **`ai_action_logs` (`AiActionLog` với `Action = AiActionTypes.GitHubPrMoveTask`, `EntityType = AiEntityTypes.Board`, `EntityId = boardId`, `Status = AiActionStatus.Approved`, `RequestedByUserId = SystemUsers.GitHubBotUserId`, `DecidedByUserId = SystemUsers.GitHubBotUserId`, kèm `BeforeSnapshot` và `AppliedSnapshot` chứa `updatedTaskId` + `fromColumnId`)** để Owner/Manager có thể bấm **Hoàn tác hành động (Undo)** bất kỳ lúc nào mà không cần sửa constraint `ck_ai_action_logs_status`.

---

## 2. Danh sách Tasks Chi tiết (`T15.3.1` → `T15.3.5`)

### Task `T15.3.1` — Mã hóa Secret (`GitHubSecretProtector`), Xác thực `HMAC-SHA256` & `TaskKeyParser` [x]

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IGitHubSecretProtector.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubSecretProtector.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IGitHubSignatureValidator.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubSignatureValidator.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/TaskKeyParser.cs`
- **Chi tiết thực hiện:**
  1. **`GitHubSecretProtector`:**
     - Sử dụng `IDataProtectionProvider.CreateProtector("TeamNexus.Integrations.GitHub.WebhookSecret.v1")`.
     - Cung cấp `GenerateNewSecret()` (32 bytes từ `RandomNumberGenerator.GetBytes(32)` → 64 ký tự hex), `Protect(string plaintext)` và `Unprotect(string ciphertext)`.
  2. **`GitHubSignatureValidator`:**
     - Nhận `ReadOnlySpan<byte> rawBody`, `string secretPlaintext`, `string? signatureHeader`.
     - Kiểm tra tiền tố `sha256=`, giải mã hex sang 32 bytes, tính `HMACSHA256.HashData(Encoding.UTF8.GetBytes(secretPlaintext), rawBody)` và so sánh bằng `CryptographicOperations.FixedTimeEquals`.
  3. **`TaskKeyParser`:**
     - Sử dụng `[GeneratedRegex(@"(?<![A-Z0-9])(?:TN-|#)(\d{1,10})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]`.
     - Trích xuất danh sách `IReadOnlyList<long>` các số `task_number` duy nhất (`> 0`), giới hạn tối đa **5 mã** trên mỗi sự kiện để tránh lạm dụng.

---

### Task `T15.3.2` — Xây dựng `WorkspaceGitHubIntegrationEndpoints` (Minimal APIs — Quản trị & Xoay vòng Secret) [x]

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/DTOs/Integrations/GitHubIntegrationDtos.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IWorkspaceGitHubIntegrationService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WorkspaceGitHubIntegrationService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/WorkspaceGitHubIntegrationEndpoints.cs`
- **Chi tiết thực hiện:**
  - Định nghĩa `MapWorkspaceGitHubIntegrationEndpoints(this IEndpointRouteBuilder endpoints)` trên group `/api/workspaces/{workspaceId:guid}/integrations/github` (gắn `.WithTags("Integrations")`, `.AddEndpointFilter<DomainExceptionFilter>()`, `.RequireAuthorization()`, `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` cho `PUT`/`POST`/`DELETE`; kiểm tra quyền Manager/Admin qua `IWorkspaceAccess.RequireManagerAsync`).
  - Các endpoints:
    1. `GET /`: Trả về cấu hình hiện tại (`IsEnabled`, `WebhookPayloadUrl`, `DefaultBoardId`, `IssueLabelFilter`, `AutoMoveOnPr`, `AutoCreateTasksFromIssues`, `SyncIssueComments`, `SecretHint` gồm 4 ký tự cuối, **không bao giờ trả lại plaintext secret**).
    2. `PUT /`: Khởi tạo hoặc cập nhật cấu hình.
       - Kiểm tra `DefaultBoardId` (nếu có) phải thuộc chính `workspaceId` này.
       - Trong lần khởi tạo đầu tiên: sinh `WebhookToken = Guid.NewGuid()`, sinh secret mới và trả về `WebhookSecretPlaintext` **duy nhất 1 lần** trong response.
    3. `POST /rotate-secret`: Sinh secret 32 bytes mới, mã hóa lưu DB và trả về `WebhookSecretPlaintext` **duy nhất 1 lần**.
    4. `DELETE /`: Tắt hoặc xóa cấu hình tích hợp GitHub của Workspace.
    5. `GET /deliveries`: Trả về 30 bản ghi `github_webhook_deliveries` gần nhất để người dùng kiểm tra trạng thái nhận sự kiện (`Processed`, `Ignored`, `Failed`).

---

### Task `T15.3.3` — Triển khai PR State Machine & Tích hợp `AiActionLog` Undo trong `GitHubWebhookService` [x]

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IGitHubWebhookService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Appliers/GitHubPrMoveTaskApplier.cs`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/AiActionTypes.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/AiModule.cs`
- **Chi tiết thực hiện:**
  1. **Idempotency & Audit (`github_webhook_deliveries`):**
     - Kiểm tra `(integration.Id, deliveryId)` đã tồn tại chưa; nếu đã tồn tại thì trả ngay `200 OK` (`DuplicateDelivery = true`).
  2. **Sự kiện `ping`:**
     - Ghi nhận `github_webhook_deliveries` với `EventName = "ping"`, `Status = "Processed"`, `Summary = "GitHub ping thành công"`.
  3. **Sự kiện `pull_request` (`opened`, `edited`, `reopened`, `ready_for_review`, `converted_to_draft`, `closed`):**
     - Dùng `TaskKeyParser` quét `pull_request.title`, `pull_request.head.ref`, `pull_request.body`.
     - Tra cứu `task_numbers` theo `(workspaceId, taskNumber)` để lấy danh sách `BoardTask` hợp lệ.
     - Xác định trạng thái PR:
       - `closed` + `merged == true` → `State = "merged"`
       - `closed` + `merged == false` → `State = "closed"`
       - `draft == true` → `State = "draft"`
       - Ngược lại → `State = "open"`
     - Upsert `TaskExternalLink` (`Provider = "github"`, `LinkType = "pull_request"`, `ExternalId = "{repoFullName}#{prNumber}"`, `ExternalUrl`, `Title`, `State`).
     - Nếu `integration.AutoMoveOnPr == true`:
       - Khi `State == "open"` (`opened`, `reopened`, `ready_for_review`): nếu trên board của task có cột `IsInReview == true` và task hiện **chưa** ở cột `IsInReview` cũng như **chưa** ở cột `IsDone`, gọi `ITaskAutomation.MoveToSystemColumnAsync(task.Id, SystemColumnTarget.InReview, ...)` và tạo bản ghi `AiActionLog` (`Action = AiActionTypes.GitHubPrMoveTask`, `EntityType = AiEntityTypes.Board`, `EntityId = task.BoardId`, `Status = AiActionStatus.Approved`, `RequestedByUserId = SystemUsers.GitHubBotUserId`, `DecidedByUserId = SystemUsers.GitHubBotUserId`, `DecidedAt = UtcNow`, `BeforeSnapshot` và `AppliedSnapshot` chứa `updatedTaskId`, `fromColumnId`, `position`, `completedAt` trước khi chuyển).
       - Khi `State == "merged"` (`closed` + `merged == true`): nếu trên board của task có cột `IsDone == true` và task hiện **chưa** ở cột `IsDone`, gọi `ITaskAutomation.MoveToSystemColumnAsync(task.Id, SystemColumnTarget.Done, ...)` và tạo bản ghi `AiActionLog` (`Action = AiActionTypes.GitHubPrMoveTask`, `EntityType = AiEntityTypes.Board`, `EntityId = task.BoardId`, `Status = AiActionStatus.Approved`, kèm `BeforeSnapshot` và `AppliedSnapshot`).
       - Khi `State == "draft"` hoặc `State == "closed"` (unmerged): chỉ cập nhật trạng thái badge trên `task_external_links`, **không** tự ý kéo lùi thẻ.
  4. **Hỗ trợ Hoàn tác (Undo) trong `AiActionService`:**
     - Đảm bảo khi Manager/Owner bấm **Hoàn tác** (`POST /api/ai-actions/{logId}/undo`) trên bản ghi `GitHubPrMoveTask` (`Status = AiActionStatus.Approved`), thẻ được trả về đúng `fromColumnId`, `position` và `completedAt` (tuân thủ nghiêm ngặt quy tắc **Bẫy P2**: Applier tuyệt đối không mở transaction lồng).

---

### Task `T15.3.4` — Xây dựng `GitHubWebhookEndpoints` (`POST /api/webhooks/github/{webhookToken:guid}`) [x]

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/GitHubWebhookEndpoints.cs`
- **Chi tiết thực hiện:**
  - Định nghĩa `MapGitHubWebhookEndpoints(this IEndpointRouteBuilder endpoints)` cho route `POST /api/webhooks/github/{webhookToken:guid}` với `.AllowAnonymous()` và `.AddEndpointFilter<DomainExceptionFilter>()`.
  - **Không** gắn `AntiforgeryValidationEndpointFilter` vì đây là endpoint server-to-server xác thực bằng `HMAC-SHA256`.
  - Giới hạn kích thước body `<= 262_144` bytes (256 KB — vượt ngưỡng trả `413 Payload Too Large` hoặc `400 Bad Request`).
  - Đọc raw body bytes, kiểm tra các headers `X-GitHub-Event`, `X-GitHub-Delivery`, `X-Hub-Signature-256`:
    - Không tìm thấy `webhookToken` hoặc `IsEnabled == false` → `404 Not Found`.
    - Thiếu hoặc sai chữ ký `X-Hub-Signature-256` → `401 Unauthorized` (tuyệt đối không ghi DB để chống làm đầy bảng bởi request giả mạo).
    - Chữ ký hợp lệ → chuyển vào `IGitHubWebhookService.HandleDeliveryAsync(...)` và trả `200 OK`.

---

### Task `T15.3.5` — Viết Unit & Integration Tests cho GitHub PR Webhook & Undo [x]

- **Files tạo mới (`[NEW]`):**
  - `tests/TeamNexus.Api.Tests/Pure/TaskKeyParserTests.cs` (18 pure tests)
  - `tests/TeamNexus.Api.Tests/Pure/GitHubSignatureValidatorTests.cs` (20 pure tests)
  - `tests/TeamNexus.Api.Tests/Integration/GitHubWebhookPrTests.cs` (14 integration tests)
- **Chi tiết kiểm thử:**
  - **Pure tests (`38` tests > `15` mục tiêu):** Kiểm tra `TaskKeyParser` với nhiều định dạng (`TN-1`, `tn-42`, `#99`, nhánh `feature/TN-12-login`, loại bỏ `BTN-5` hoặc số quá dài, giới hạn 5 mã); kiểm tra `GitHubSignatureValidator` với chữ ký đúng, chữ ký sai, body bị sửa 1 byte, header rỗng.
  - **Integration tests (`14` tests > `12` mục tiêu):**
    - Khởi tạo GitHub Integration → nhận `WebhookSecretPlaintext` đúng 1 lần → gọi lại `GET` không lộ secret → `rotate-secret` đổi secret thành công.
    - Gửi `ping` với chữ ký HMAC hợp lệ → `200 OK` và lưu `github_webhook_deliveries`.
    - Gửi `pull_request.opened` (`draft = true`) có mã `TN-1` → gắn `TaskExternalLink` (`state = draft`), **không** chuyển cột.
    - Gửi `pull_request.ready_for_review` (`draft = false`) → chuyển thẻ `TN-1` sang cột `In Review` (`is_in_review = true`) và sinh `AiAction` (`GitHubPrMoveTask`).
    - Gửi `pull_request.closed` (`merged = true`) → chuyển thẻ `TN-1` sang cột `Done` (`is_done = true`), `CompletedAt != null`.
    - Gửi lại cùng `X-GitHub-Delivery` → idempotent `200 OK`, không tạo link/action trùng.
    - Gọi `POST /api/ai-actions/{actionId}/undo` trên `GitHubPrMoveTask` → thẻ quay về đúng cột trước đó và khôi phục `CompletedAt`.

---

## 3. Tiêu chí Nghiệm thu (Definition of Done — Sub-phase 15.3)

- [x] Request không có hoặc sai chữ ký `X-Hub-Signature-256` bị từ chối `401 Unauthorized` và không ghi bất kỳ dòng nào vào DB.
- [x] Pull Request chứa mã `TN-{number}` tự động liên kết với thẻ tương ứng và chuyển cột chính xác theo trạng thái `draft` / `open` / `merged`.
- [x] Mọi lần tự động chuyển cột bởi PR đều xuất hiện trong `Lịch sử Hành động AI` và có thể **Hoàn tác (Undo)** thành công 100%.
- [x] Solution build sạch `0 Warning(s), 0 Error(s)` với `--no-incremental`.
- [x] Model drift EF Core hoàn toàn sạch: `No changes have been made to the model since the last migration`.
- [x] Bộ test Pure đạt **645 passed / 0 failed / 0 skipped** (+38 pure unit tests mới).

---

## 4. Nhật ký Thi hành (Execution Log)

### 4.1. Quyết định Kỹ thuật then chốt (`K1` → `K4`)

- **`K1` (Tuân thủ nghiêm ngặt 4 Module Boundaries):** Mọi thành phần mã nguồn của Sub-phase 15.3 được tổ chức hoàn toàn bên trong `TeamNexus.Modules.Ai` (`Services/Integrations`, `DTOs/Integrations`, `Endpoints`, `Services/Appliers`), giao tiếp với `Board` qua các narrow ports (`ITaskAutomation`, `ITaskService`) theo đúng chiều phụ thuộc một chiều `Ai → Board`.
- **`K2` (Mô hình Accountability Layer & Reversible Undo):** Chuyển cột tự động do PR trigger được lưu vào `ai_action_logs` dưới dạng `AiActionLog` với `Action = AiActionTypes.GitHubPrMoveTask`, `Status = AiActionStatus.Approved`, `RequestedByUserId = DecidedByUserId = SystemUsers.GitHubBotUserId`. Snapshot lưu đầy đủ `updatedTaskId`, `fromColumnId`, `targetColumnId`, `fromPosition`, `fromCompletedAt` cho phép khôi phục nguyên trạng thái thẻ khi người dùng bấm Undo.
- **`K3` (Tránh bẫy P2 - Không lồng transaction trong Applier):** `GitHubPrMoveTaskApplier` không tự mở `BeginTransactionAsync()`. Applier ủy quyền cho `ITaskService.MoveTaskAsync` (đã lo sắp xếp vị trí và cập nhật trạng thái `CompletedAt`), và chỉ gọi trực tiếp `_db.SaveChangesAsync(ct)` khi thao tác snapshot DB.
- **`K4` (Bảo mật Secret Zero-Leakage & Chống Timing Attack):** Khóa bí mật webhook được mã hóa at-rest bằng `IDataProtectionProvider` với purpose string riêng biệt. Endpoint chỉ trả về `WebhookSecretPlaintext` đúng 1 lần khi tạo hoặc xoay vòng secret. Mọi thao tác kiểm tra chữ ký HMAC sử dụng `CryptographicOperations.FixedTimeEquals`.

### 4.2. Độ lệch có chủ đích (`L1`)

- **`L1` (Độc lập hóa hành động chuyển thẻ khi PR chứa nhiều mã công việc):** Khi một Pull Request tham chiếu nhiều mã công việc (ví dụ `TN-1, TN-2`), thay vì gom vào 1 log duy nhất dạng batch, hệ thống ghi nhận mỗi task thành một `AiActionLog` độc lập. Điều này giúp người quản lý có thể hoàn tác từng task riêng biệt mà không làm ảnh hưởng lẫn nhau.

### 4.3. Lỗi thực tế đã phát hiện & khắc phục (`BUG1` → `BUG4`)

- **`BUG1` (`Convert.TryFromHexString` không tồn tại trong .NET 10 `Convert`):** Phát hiện lỗi biên dịch `CS0117` trong `GitHubSignatureValidator.cs`. Khắc phục bằng cách sử dụng `Convert.FromHexString` bọc trong khối `try/catch` bắt `FormatException`.
- **`BUG2` (Trùng/mất hằng số `AiActionTypes.CreateKnowledgeDocument`):** Khi thêm `GitHubPrMoveTask` vào `AiActionTypes.cs`, hằng số Phase 14.7 bị thay thế ngoài ý muốn khiến `CreateKnowledgeDocumentApplier.cs` báo lỗi `CS0117`. Đã khôi phục đầy đủ cả hai hằng số.
- **`BUG3` (Kiểu dữ liệu `Status` trong `AiActionLogDetailResponse` là `string`):** Trong test tích hợp `GitHubWebhookPrTests.cs`, assertion ban đầu so sánh với `AiActionStatus.Undone` (enum) dẫn đến lỗi `CS1503`. Khắc phục bằng cách so sánh với chuỗi `"Undone"`.
- **`BUG4` (Thiếu directive `using` cho `GitHubWebhookProcessResult` trong integration test):** Bổ sung namespace `TeamNexus.Modules.Ai.Services.Integrations` vào đầu file `GitHubWebhookPrTests.cs`.


