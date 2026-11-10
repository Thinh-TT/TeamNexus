# Giai đoạn 15.4 — GitHub Issue → Task + Jev 1.13 Auto-Triage & Đồng bộ Bình luận 1 Chiều

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F7`), §3.2 (`D19`, `D21`), §6.3.2 & §6.3.3.
> **Trạng thái:** 📋 Kế hoạch chi tiết (`T15.4.1` → `T15.4.4`).

---

## 1. Mục tiêu của Sub-phase 15.4

1. Mở rộng `ITaskTriageService` với phương thức hệ thống **`TriageForSystemAsync`** để gọi **Jev 1.13 (System-1)** phân loại tự động các công việc sinh từ GitHub Issue mà không yêu cầu `userId` thuộc `workspace_members`.
2. **Sửa dứt điểm lỗi cờ `Degraded` khi `RoutingAdvice == ManualRequired`** trong `TaskTriageService` (phát hiện thực tế từ đợt Live Test Chặng 2 & 3 trên `https://app.teamnexus.cloud/`).
3. Tự động tạo công việc từ sự kiện GitHub **`issues` (`opened`, `labeled`, `closed`, `reopened`)** có hỗ trợ bộ lọc nhãn (`IssueLabelFilter`), chống tạo trùng tuyệt đối qua `task_external_links`, và tự động gán `Priority` / `Tags` / `Assignee` theo ngưỡng độ tự tin của Jev 1.13.
4. Đồng bộ bình luận **1 chiều (GitHub → TeamNexus)** từ sự kiện **`issue_comment.created`** vào thẻ công việc dưới tên tài khoản hệ thống `GitHub Bot`.

---

## 2. Danh sách Tasks Chi tiết (`T15.4.1` → `T15.4.4`)

### Task `T15.4.1` — Bổ sung `TriageForSystemAsync` trong `ITaskTriageService` & Tinh chỉnh cờ `Degraded`

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/ITaskTriageService.cs`
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/TaskTriageService.cs`
  - `frontend/src/features/ai/components/AssigneeSuggestion.tsx`
- **Chi tiết thực hiện:**
  1. **`TriageForSystemAsync(Guid boardId, string title, string? description, CancellationToken ct = default)`:**
     - Nếu `!_options.Enabled`, trả về `null` nhẹ nhàng (không ném exception).
     - Bỏ qua bước `_access.RequireMemberAsync(board.WorkspaceId, userId, ct)` (vì caller là `GitHubWebhookService` đã xác thực HMAC-SHA256 của Workspace).
     - Gọi `_jev.TriageTaskAsync(context, ct)` trong khối `try/catch` an toàn: nếu Jev provider lỗi hoặc timeout, log warning và trả về `null` để việc tạo thẻ từ GitHub Issue luôn thành công.
  2. **Sửa lỗi hiển thị cảnh báo kép `Degraded` trong `SuggestAssigneesAsync` (từ Live Test Chặng 2 & 3):**
     - Trong `TaskTriageService.SuggestAssigneesAsync`: khi Jev phản hồi bình thường (`result.LatencyMs > 0` hoặc có `CandidateRankings`) nhưng độ tự tin `< 0.50` (`ManualRequired`), đó là kết quả đánh giá hợp lệ (*"Chưa có ứng viên đạt độ tương thích cao"*), **không phải** lỗi sập dịch vụ AI (`Degraded`).
     - Chỉ đặt `Degraded = true` khi Jev thực sự rơi vào nhánh lỗi/fallback (`result.LatencyMs == 0 && result.CandidateRankings.Count == 0` khi `candidates.Count > 0`).

---

### Task `T15.4.2` — Xử lý Sự kiện GitHub `issues` (`opened`, `labeled`, `closed`, `reopened`) + Jev 1.13 Auto-Triage

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs`
- **Chi tiết thực hiện:**
  1. **Kiểm tra điều kiện kích hoạt (`opened`, `labeled`):**
     - Yêu cầu `integration.AutoCreateTasksFromIssues == true` và `integration.DefaultBoardId != null`.
     - Kiểm tra bộ lọc nhãn `integration.IssueLabelFilter`:
       - Nếu `IssueLabelFilter` có cấu hình (VD: `"teamnexus,bug"`), tách thành tập nhãn (case-insensitive).
       - Chỉ tiếp tục nếu `issue.labels` có ít nhất 1 nhãn nằm trong `IssueLabelFilter`.
  2. **Chống tạo trùng thẻ (Deduplication):**
     - Tra cứu `task_external_links` theo `(WorkspaceId, Provider = "github", LinkType = "issue", ExternalId = "{repoFullName}#{issueNumber}")`.
     - Nếu đã tồn tại liên kết tới một task chưa xóa → bỏ qua việc tạo mới (đặc biệt quan trọng khi Issue được gắn thêm nhãn thứ 2 kích hoạt sự kiện `issues.labeled`).
  3. **Tạo thẻ & Tự động phân loại bằng Jev 1.13:**
     - Gọi `ITaskAutomation.CreateTaskFromExternalIssueAsync(defaultBoardId, $"[GH #{issueNumber}] {issueTitle}", FormatIssueDescription(issueBody, issueHtmlUrl, senderLogin), ct)`.
     - Tạo bản ghi `TaskExternalLink` (`LinkType = "issue"`, `State = "open"`).
     - Gọi `ITaskTriageService.TriageForSystemAsync(defaultBoardId, issueTitle, issueBody, ct)`:
       - Nếu `triage != null && !triage.Degraded && triage.ConfidenceScore >= 0.50`:
         - Áp dụng `SuggestedPriority`, `SuggestedDomain`, `SuggestedWorkType` (tự động gắn/tạo nhãn tương ứng trên Workspace thông qua `ITaskAutomation.ApplySystemTriageSuggestionsAsync`).
     - Ghi bản ghi `AiActionLog` (`Action = AiActionTypes.GitHubIssueCreateTask`, `EntityType = AiEntityTypes.Board`, `EntityId = defaultBoardId`, `Status = AiActionStatus.Approved`, `RequestedByUserId = SystemUsers.GitHubBotUserId`, `DecidedByUserId = SystemUsers.GitHubBotUserId`, `DecidedAt = UtcNow`, `AppliedSnapshot` chứa `createdTaskIds: [createdTask.Id]`) để lưu vết kiểm toán trên `Lịch sử Hành động AI` và hỗ trợ hoàn tác (soft-delete task vừa tạo khi bấm Undo).
  4. **Cập nhật trạng thái khi Issue đóng / mở lại (`closed`, `reopened`):**
     - Cập nhật `TaskExternalLink.State = action == "closed" ? "closed" : "open"` và phát SignalR `TaskUpdated`.

---

### Task `T15.4.3` — Xử lý Sự kiện `issue_comment.created` (Đồng bộ Bình luận 1 Chiều GitHub → TeamNexus)

- **Files chỉnh sửa (`[MODIFY]`):**
  - `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs`
- **Chi tiết thực hiện:**
  1. Yêu cầu `integration.SyncIssueComments == true`.
  2. Chống vòng lặp & bot spam: nếu `sender.type == "Bot"` hoặc `comment.body` trống → đánh dấu `Ignored` và kết thúc.
  3. Tìm các công việc đích cần nhận bình luận:
     - **Nguồn 1:** Tìm trong `task_external_links` theo `ExternalId = "{repoFullName}#{issueNumber}"` (khớp cho cả bình luận trên Issue đã liên kết lẫn bình luận trên PR đã liên kết).
     - **Nguồn 2:** Dùng `TaskKeyParser` trích xuất mã `TN-123` trong `comment.body` và tra cứu `task_numbers` trong cùng Workspace.
     - Gộp danh sách `taskId` duy nhất.
  4. Với mỗi `taskId`:
     - Định dạng nội dung bình luận Markdown (cắt gọn `comment.body` tối đa `1,800` ký tự):
       ```markdown
       💬 **@{senderLogin}** đã bình luận trên GitHub ([#{issueNumber}]({commentHtmlUrl})):

       > {truncatedCommentBody}
       ```
     - Gọi `ITaskAutomation.AddSystemCommentAsync(taskId, SystemUsers.GitHubBotUserId, formattedMarkdown, ct)` để lưu bình luận và phát SignalR `CommentAdded` tới mọi trình duyệt đang mở bảng.

---

### Task `T15.4.4` — Viết Integration Tests cho GitHub Issues, Jev Auto-Triage & Comment Sync

- **Files tạo mới (`[NEW]`):**
  - `tests/TeamNexus.Api.Tests/Integration/GitHubWebhookIssueAndCommentTests.cs`
- **Chi tiết kiểm thử (`>= 10` tests):**
  - Gửi `issues.opened` khi `IssueLabelFilter = "teamnexus"` nhưng Issue chưa có nhãn → `Ignored`, không tạo task.
  - Gửi `issues.labeled` với nhãn `"teamnexus"` → tạo đúng 1 task có `TaskNumber`, gắn `TaskExternalLink` (`LinkType = "issue"`), tự động áp dụng `Priority` và nhãn từ Jev Triage.
  - Gửi lại `issues.labeled` lần 2 trên cùng Issue → chống trùng hoạt động, vẫn chỉ có 1 task.
  - Gửi `issues.closed` → cập nhật `TaskExternalLink.State = "closed"`.
  - Gửi `issue_comment.created` trên Issue đã liên kết (và bình luận có nhắc `TN-1`) → tạo đúng 1 bình luận dưới tên `GitHub Bot` (`00000000-0000-0000-0000-00000000b071`).
  - Gửi `issue_comment.created` từ tài khoản có `sender.type = "Bot"` → tự động bỏ qua (`Ignored`).

---

## 3. Tiêu chí Nghiệm thu (Definition of Done — Sub-phase 15.4)

- [ ] Tạo Issue trên GitHub (hoặc gắn nhãn khớp `IssueLabelFilter`) tự động sinh thẻ trên cột đầu tiên của `DefaultBoardId`, được Jev 1.13 phân loại tự động và xuất hiện tức thì qua SignalR.
- [ ] Bình luận trên Issue/PR liên kết tự động đồng bộ một chiều vào thẻ dưới tên `GitHub Bot` mà không gây vòng lặp.
- [ ] Lỗi cảnh báo kép `Degraded` trên `AssigneeSuggestion` đã được khắc phục.
- [ ] Toàn bộ bài test trong `GitHubWebhookIssueAndCommentTests` đều PASS.

