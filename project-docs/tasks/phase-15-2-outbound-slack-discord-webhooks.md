# Giai đoạn 15.2 — Outbound Slack / Discord Webhooks & Transactional Outbox

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F1`, `F2`, `F3`), §4.1 & §6.1.
> **Trạng thái:** 📋 Kế hoạch chi tiết (`T15.2.1` → `T15.2.5`).

---

## 1. Mục tiêu của Sub-phase 15.2

1. Thay thế `NoOpExternalAlertSink` (seam đã mở sẵn từ Phase 14.5) bằng **`WebhookOutboxExternalAlertSink`** ghi vào bảng `webhook_delivery_outbox`.
2. Bổ sung sự kiện thông báo **`MemberJoined`** trong `InvitationService.AcceptAsync` để thông báo cho Owner/Managers trên kênh Slack/Discord khi có thành viên mới gia nhập Workspace.
3. Xây dựng bộ kiểm duyệt URL chống SSRF (**`WebhookUrlValidator`**) và bộ định dạng bản tin phong phú (**`SlackBlockKitFormatter`** & **`DiscordEmbedFormatter`**).
4. Xây dựng tiến trình nền **`WebhookOutboxDispatcher`** (`BackgroundService` + `WebhookOutboxRunner` scoped để test trực tiếp) hỗ trợ retry theo cấp số nhân và tự động ngắt mạch (circuit breaker) khi endpoint lỗi liên tiếp `>= 10` lần.
5. Cung cấp Minimal API quản trị **`WorkspaceWebhooksEndpoints`** (`MapWorkspaceWebhooksEndpoints()`) kèm tính năng **Gửi thử (Test Webhook)** và xem lịch sử 20 lần gửi gần nhất.

---

## 2. Danh sách Tasks Chi tiết (`T15.2.1` → `T15.2.5`)

### Task `T15.2.1` — Mở rộng `IExternalAlertSink` & Bổ sung Sự kiện `MemberJoined`

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IExternalAlertSink.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/NotificationService.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/INotificationWriter.cs`
  - `src/Modules/Board/TeamNexus.Modules.Board/Services/InvitationService.cs`
- **Chi tiết thực hiện:**
  1. **Mở rộng `ExternalAlertMessage` (append-only, tương thích ngược):**
     ```csharp
     public sealed record ExternalAlertMessage(
         Guid WorkspaceId,
         string Type,
         string Title,
         string Message,
         string? PayloadJson,
         IReadOnlyList<Guid> RecipientUserIds,
         Guid? NotificationId = null,
         string? DedupeKey = null);
     ```
  2. **Truyền `NotificationId` và `DedupeKey` từ `NotificationService`:**
     - Trong `NotifyManagersAsync` (Observer & Agent) và `NotifyUsersAsync` (Member alerts): truyền `NotificationId` của bản ghi thông báo đầu tiên vừa lưu và `DedupeKey = $"{type}:{workspaceId}:{notificationId}"`.
  3. **Bắn thông báo `MemberJoined` trong `InvitationService.AcceptAsync`:**
     - Thêm `public const string MemberJoined = "MemberJoined";` vào `MemberNotificationTypes` (`src/Modules/Board/TeamNexus.Modules.Board/Services/INotificationWriter.cs`).
     - Khi người dùng chấp nhận lời mời vào Workspace, truy vấn danh sách `Admin`/`Manager` của Workspace (trừ chính người vừa vào) và gọi `INotificationWriter.NotifyAsync(new MemberNotification(...))` với tiêu đề `"Thành viên mới tham gia không gian làm việc"` và nội dung tên/email thành viên mới.

---

### Task `T15.2.2` — Xây dựng `WebhookUrlValidator` (SSRF Guard) & Payload Formatters (`Slack` / `Discord`)

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WebhookUrlValidator.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/SlackBlockKitFormatter.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/DiscordEmbedFormatter.cs`
- **Chi tiết thực hiện:**
  1. **`WebhookUrlValidator`:**
     - Chỉ chấp nhận giao thức `https://`, không có user-info (`user:pass@`), độ dài `<= 1024`.
     - Với `Provider = "slack"`: host bắt buộc là `hooks.slack.com` và path bắt đầu bằng `/services/`.
     - Với `Provider = "discord"`: host bắt buộc là `discord.com` hoặc `discordapp.com` và path bắt đầu bằng `/api/webhooks/`.
     - Chặn mọi địa chỉ IP literal, `localhost`, loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`), RFC1918 private IPs (`10.0.0.0/8`, `172.16.0.0/12`, `192.168.0.0/16`).
  2. **`SlackBlockKitFormatter`:**
     - Sinh JSON có `text` (fallback cho thông báo đẩy trên điện thoại) và mảng `blocks`:
       - `header` (`plain_text`, cắt gọn an toàn `<= 150` ký tự): `"🔔 {Title}"` (hoặc `"🚨 {Title}"` cho `ObserverAlert`).
       - `section` (`mrkdwn`): nội dung chi tiết + trường `Loại sự kiện` & `Không gian làm việc`.
       - `actions`: nút `"Mở trong TeamNexus"` trỏ tới deep-link `https://app.teamnexus.cloud/workspaces/{workspaceId}...`.
  3. **`DiscordEmbedFormatter`:**
     - Sinh JSON với `username = "TeamNexus AI"` và mảng `embeds`:
       - `title`, `description`, `url` (deep-link), `color` (`15548997` — đỏ `#ED4245` cho `ObserverAlert`/`Critical`; `5195493` — Indigo `#4F46E5` cho các loại khác), `fields`, `footer` (`"TeamNexus Hybrid AI Workspace"`), `timestamp` (UTC ISO-8601).

---

### Task `T15.2.3` — Triển khai `WebhookOutboxExternalAlertSink` & `WebhookOutboxDispatcher`

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Options/IntegrationsOptions.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WebhookOutboxExternalAlertSink.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WebhookOutboxRunner.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WebhookOutboxDispatcher.cs`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/AiModule.cs`
  - `src/TeamNexus.Api/appsettings.json`
- **Chi tiết thực hiện:**
  1. **`WebhookOutboxExternalAlertSink`:**
     - Khi `PublishAsync(ExternalAlertMessage message, CancellationToken ct)` được gọi:
       - Tìm tất cả `workspace_webhook_endpoints` của `message.WorkspaceId` có `IsActive == true`.
       - Kiểm tra CSV `SubscribedEvents` của từng endpoint có chứa `message.Type` (hoặc ánh xạ chuẩn hóa từ `MemberNotificationTypes` / `ObserverAlert` / `AgentActionPending` / `ClarificationNeeded`).
       - Format payload JSON tương ứng với `endpoint.Provider` (`slack` | `discord`) và thêm bản ghi `WebhookDeliveryOutbox` (`Status = "Pending"`, `AttemptCount = 0`, `MaxAttempts = 5`, `NextAttemptAt = UtcNow`).
  2. **`WebhookOutboxRunner` (scoped) & `WebhookOutboxDispatcher` (`BackgroundService`):**
     - Tách logic quét và gửi vào `WebhookOutboxRunner.ProcessBatchAsync(CancellationToken ct)` (theo đúng mẫu `AgentRunReaperRunner` / `DailyDigestRunner` để bài test Integration gọi trực tiếp không cần chờ timer).
     - Sử dụng named `HttpClient` `"ExternalWebhookClient"` với `AllowAutoRedirects = false`, `Timeout = 10s`.
     - Khi nhận HTTP `2xx`: đánh dấu `Status = "Sent"`, `SentAt = UtcNow`, reset `endpoint.FailureCount = 0`, cập nhật `endpoint.LastSuccessAt = UtcNow`.
     - Khi nhận HTTP `429` / `5xx` hoặc lỗi mạng: tăng `AttemptCount` và `endpoint.FailureCount`, lưu `LastError` (tối đa 500 ký tự, tuyệt đối không chứa URL webhook bí mật). Nếu `AttemptCount >= MaxAttempts` → `Status = "Failed"`; ngược lại lên lịch `NextAttemptAt` theo bậc `5s → 30s → 2m → 10m → 30m`.
     - Nếu `endpoint.FailureCount >= 10` (cấu hình qua `Integrations:Outbox:CircuitBreakerThreshold`): tự động đặt `endpoint.IsActive = false` để bảo vệ hệ thống.

---

### Task `T15.2.4` — Xây dựng `WorkspaceWebhooksEndpoints` (Minimal APIs — Quản trị & Gửi thử)

- **Files tạo mới (`[NEW]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/DTOs/Integrations/WebhookDtos.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/IWorkspaceWebhookService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WorkspaceWebhookService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/WorkspaceWebhooksEndpoints.cs`
- **Chi tiết thực hiện:**
  - Định nghĩa `MapWorkspaceWebhooksEndpoints(this IEndpointRouteBuilder endpoints)` trên group `/api/workspaces/{workspaceId:guid}/integrations/webhooks` (gắn `.WithTags("Integrations")`, `.AddEndpointFilter<DomainExceptionFilter>()`, `.RequireAuthorization()`, và `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` cho `POST`/`PATCH`/`DELETE`; kiểm tra quyền Manager/Admin qua `IWorkspaceAccess.RequireManagerAsync`).
  - Các endpoints:
    1. `GET /`: Trả danh sách endpoints (URL được che bớt phần token cuối `https://hooks.slack.com/services/T.../B.../****abcd` hoặc trả kèm theo quyền Admin) + số liệu thống kê 24 giờ qua (`sentLast24h`, `failedLast24h`, `pendingCount`).
    2. `POST /`: Tạo endpoint mới (tối đa 10 endpoints/workspace; kiểm tra `WebhookUrlValidator`; mặc định `SubscribedEvents = "ObserverAlert,AgentActionPending,ClarificationNeeded"`).
    3. `PATCH /{endpointId:guid}`: Cập nhật tên, URL, `SubscribedEvents`, hoặc bật lại `IsActive = true` (đồng thời reset `FailureCount = 0`).
    4. `DELETE /{endpointId:guid}`: Xóa endpoint và các bản ghi Outbox liên quan.
    5. `POST /{endpointId:guid}/test`: Tạo một bản ghi Outbox `EventType = "TestPing"` và kích hoạt gửi ngay lập tức để trả kết quả thành công/thất bại kèm mã HTTP cho người dùng.
    6. `GET /{endpointId:guid}/deliveries`: Trả về 20 bản ghi `webhook_delivery_outbox` mới nhất của endpoint đó.

---

### Task `T15.2.5` — Viết Unit & Integration Tests cho Outbound Webhooks

- **Files tạo mới (`[NEW]`):**
  - `tests/TeamNexus.Api.Tests/Pure/WebhookUrlValidatorTests.cs`
  - `tests/TeamNexus.Api.Tests/Pure/WebhookPayloadFormatterTests.cs`
  - `tests/TeamNexus.Api.Tests/Integration/WorkspaceWebhooksApiTests.cs`
- **Chi tiết kiểm thử:**
  - **Pure tests (`>= 15` tests):** Kiểm tra chặn SSRF (`http://`, `localhost`, `127.0.0.1`, `10.0.0.1`, `169.254.169.254`, sai domain, sai path prefix); kiểm tra cấu trúc JSON của `SlackBlockKitFormatter` và `DiscordEmbedFormatter`.
  - **Integration tests (`>= 12` tests):** Kiểm tra CRUD endpoints, phân quyền (Member thường bị `403`, người ngoài workspace bị `404`), lọc theo `SubscribedEvents`, gửi thử `/test`, retry backoff khi mock HTTP trả `500`, và circuit breaker sau 10 lần lỗi liên tiếp.

---

## 3. Tiêu chí Nghiệm thu (Definition of Done — Sub-phase 15.2)

- [ ] Khi `NotificationService` phát cảnh báo `ObserverAlert`, `AgentActionPending`, `ClarificationNeeded`, `TaskAssigned`, `CommentMentioned`, hoặc `MemberJoined`, bản ghi `webhook_delivery_outbox` được tạo tự động cho các endpoint đăng ký sự kiện tương ứng.
- [ ] Lỗi mạng hoặc lỗi từ Slack/Discord tuyệt đối không làm hỏng giao dịch nghiệp vụ chính của người dùng.
- [ ] Toàn bộ bài test trong `WebhookUrlValidatorTests`, `WebhookPayloadFormatterTests` và `WorkspaceWebhooksApiTests` đều PASS.

