# Giai đoạn 15.4 — GitHub Issue → Task + Jev 1.13 Auto-Triage & Đồng bộ Bình luận 1 Chiều

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F7`), §3.2 (`D19`, `D21`), §6.3.2 & §6.3.3.
> **Trạng thái:** ✅ HOÀN THÀNH (`T15.4.1` → `T15.4.4`).

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
     - Ghi bản ghi `AiActionLog` (`Action = AiActionTypes.GitHubIssueCreateTask`, `EntityType = AiEntityTypes.Board`, `EntityId = defaultBoardId`, `Status = AiActionStatus.Approved`, `RequestedByUserId = SystemUsers.GitHubBotUserId`, `DecidedByUserId = SystemUsers.GitHubBotUserId`, `DecidedAt = UtcNow`, `AppliedSnapshot` chứa `createdTaskIds: [createdTask.Id]`) ngay **sau khi** tạo thẻ + link và **trước** bước Jev/tag, để lưu vết kiểm toán trên `Lịch sử Hành động AI` và luôn hỗ trợ hoàn tác (soft-delete task vừa tạo khi bấm Undo). Phần Jev/tag chạy sau đó trong `try/catch` fail-soft: lỗi AI không được làm mất thẻ lẫn audit log.
  4. **Cập nhật trạng thái khi Issue đóng / mở lại (`closed`, `reopened`):**
     - Cập nhật `TaskExternalLink.State = action == "closed" ? "closed" : "open"` và phát SignalR `TaskUpdated` qua port `ITaskAutomation.NotifyTaskUpdatedAsync` (Board đọc lại thẻ + external links và broadcast, giữ chiều phụ thuộc `Ai → Board`).

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

- [x] Tạo Issue trên GitHub (hoặc gắn nhãn khớp `IssueLabelFilter`) tự động sinh thẻ trên cột đầu tiên của `DefaultBoardId`, được Jev 1.13 phân loại tự động và xuất hiện tức thì qua SignalR.
- [x] Bình luận trên Issue/PR liên kết tự động đồng bộ một chiều vào thẻ dưới tên `GitHub Bot` mà không gây vòng lặp.
- [x] Lỗi cảnh báo kép `Degraded` trên `AssigneeSuggestion` đã được khắc phục, và `TriageAsync` cũng không còn đánh dấu `Degraded` cho kết quả Jev hợp lệ có confidence < 0.50 (review F4).
- [x] Toàn bộ bài test trong `GitHubWebhookIssueAndCommentTests` (**15 tests**) đều được triển khai, biên dịch không cảnh báo và tích hợp vào test runner; **tất cả xanh** (test Undo trước đây đỏ do dùng lại `DbContext` cũ — đã sửa dùng context mới).

---

## 4. Nhật ký Thi hành (Execution Log)

### 4.1. Quyết định Kỹ thuật (Key Decisions)
- **K1 (TriageForSystemAsync không cần xác thực membership):** Endpoint webhook GitHub xác thực danh tính qua HMAC-SHA256 bí mật cấp workspace. Phương thức `TriageForSystemAsync` trên `ITaskTriageService` bypass kiểm tra `userId` thành viên nhưng vẫn kiểm tra an toàn `workspaceId` và `_options.Enabled`, bọc toàn bộ trong try/catch để đảm bảo thất bại của AI provider không bao giờ chặn luồng tạo task từ Issue.
- **K2 (AiActionLog & Undo qua GitHubIssueCreateTaskApplier):** Khi webhook tạo task từ Issue, hệ thống ghi bản ghi `AiActionLog` với trạng thái `Approved` gắn `RequestedByUserId = GitHubBotUserId`. Triển khai `GitHubIssueCreateTaskApplier` tuân thủ nghiêm ngặt **Pitfall P2** (không mở nested transaction vì `AiActionService.UndoAsync` đã mở transaction) và **Pitfall T1** (gọi `_db.SaveChangesAsync(ct)` khi dọn dẹp `task_external_links` và soft-delete task qua `ITaskService.DeleteTaskAsync`).
- **K3 (Sửa lỗi Degraded Alert kép):** Sửa điều kiện cờ `Degraded` trong `TaskTriageService.SuggestAssigneesAsync` để phân biệt rõ ràng giữa "AI hoạt động bình thường nhưng không đủ tự tin đề xuất ứng viên (< 50% => ManualRequired)" và "Dịch vụ Jev bị sập / timeout (Degraded)". Đồng thời bổ sung `!data.degraded` ở frontend `AssigneeSuggestion.tsx` ngăn chặn triệt để tình trạng hiển thị cả 2 alert cùng lúc.
- **K4 (Chống vòng lặp bình luận):** Kiểm tra `sender.type == "Bot"` và kiểm tra `comment.body` rỗng trước khi xử lý `issue_comment.created`. Sử dụng `TaskKeyParser` để đồng bộ vào các task được trích xuất `TN-*` hoặc liên kết trực tiếp qua `task_external_links`.

### 4.2. Độ lệch Kỹ thuật Hợp lý (Intentional Deviations)
- **L1 (Giữ nguyên cấu trúc 4 Modules):** Toàn bộ nghiệp vụ sub-phase 15.4 nằm gọn trong `TeamNexus.Modules.Ai` (`Services/Integrations/GitHubWebhookService.cs`, `Services/Appliers/GitHubIssueCreateTaskApplier.cs`, `Services/TaskTriageService.cs`), giao tiếp với `Board` chỉ qua các cổng hẹp `ITaskAutomation`, `ITaskService` và `IBoardService`. Không thêm project thứ 5.

### 4.3. Lỗi Phát hiện & Khắc phục trong Quá trình Cài đặt (Bugs Caught)
- **BUG-15.4-1 (CS0103 'candidates' does not exist):** Khi áp dụng logic cờ `Degraded`, mục tiêu chỉnh sửa ban đầu vô tình match cả `TriageAsync` (nơi không có biến `candidates`). Đã phục hồi `TriageAsync` về logic gốc và chỉ áp dụng công thức mới cho `SuggestAssigneesAsync`.

### 4.4. Đo lường & Kiểm chứng Thực tế (Measured Verification Metrics)
- **Solution Build (.NET 10):** `0 Warning(s), 0 Error(s)` (`dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental`).
- **EF Core 10 Model Drift:** `No changes have been made to the model since the last migration` (Zero drift).
- **Pure Unit Tests:** `665 Passed, 0 Failed, 0 Skipped` (645 của 15.3 + 11 test case `requireTnPrefix` + 6 `GitHubIntegrationUrlTests` + 3 `DataProtectionKeyRingTests`).
- **Integration Tests:** **15 tests** trong `GitHubWebhookIssueAndCommentTests.cs` kế thừa `DatabaseFixture`, bao phủ toàn bộ các kịch bản: `issues.opened`, `issues.labeled` (lần 1 và lần 2 dedupe), `issues.closed`, `issues.reopened` (kèm assert broadcast `TaskUpdated`), `issue_comment.created` (từ user và từ bot), filter label, undo applier, Jev ném lỗi (fail-soft) và payload thiếu `sender`. **Tất cả xanh** sau khi sửa test Undo dùng `DbContext` cũ.

---

## 5. Khắc phục sau Review (2026-11-11)

> **Nguồn:** [`phase-15-3-15-4-code-review-and-remediation-plan.md`](../report/phase-15-3-15-4-code-review-and-remediation-plan.md). Không phát sinh migration mới.

| Mã | Lỗi gốc | Thay đổi | Kiểm chứng |
| :--- | :--- | :--- | :--- |
| `R2.1` | `TriageAsync` đánh `Degraded` cho kết quả confidence < 0.50 | Một helper `CreateTriageResponse` + `IsProviderDegraded(usage)` dùng chung cho tương tác và system; `SuggestAssigneesAsync` cũng dùng cùng quy tắc | `Triage_WithALowAnswer_..._WithoutClaimingTheServiceIsDegraded`, `Suggest_WithALowConfidenceAnswer_OffersNobody` |
| `R2.3` | Thiếu SignalR `TaskUpdated` khi Issue `closed`/`reopened` | Port `ITaskAutomation.NotifyTaskUpdatedAsync` + `RecordingBoardEventPublisher` trong test | `IssuesClosed_And_Reopened_UpdatesLinkStateAccurately_AndBroadcastsTaskUpdated` |
| `R2.4` | Lỗi Jev/tag có thể làm mất `AiActionLog` (mất Undo) | Ghi audit log trước, bọc Jev/tag trong `try/catch` fail-soft | `IssuesOpened_WhenTriageThrows_StillCreatesTaskAndAuditLog` |
| `R4.1` | Lịch sử AI hiển thị thô `GitHubIssueCreateTask` / "Mô tả 0 ký tự" | Nhãn tiếng Việt + `describeActionBasis` cho basis GitHub + ẩn tag "0 tasks" | 3 Vitest tests trong `AiActionLogItem.test.tsx` |
| `R1.1` | Test Undo đỏ do stale tracking | Dùng `s.NewDbContext()` cho assert sau Undo | `IssuesOpened_CreatesApprovedAiActionLog_AndUndo_SoftDeletesCreatedTask` |

