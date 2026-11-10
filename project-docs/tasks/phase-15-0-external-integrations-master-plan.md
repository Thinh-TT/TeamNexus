# Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài (Slack, Discord & GitHub Webhooks) — Kế hoạch Tổng thể

> **Trạng thái:** 📋 **SẴN SÀNG THI HÀNH (Đã hoàn tất Kiểm thử Local, Rà soát Code & Live Test 6 Chặng trên Production).**
> **Nguồn thiết kế:** [`project-docs/07-external-integrations-design.md`](../07-external-integrations-design.md) + [`project-docs/03-roadmap.md`](../03-roadmap.md) §15.
> **Tiền đề đã merge:** Giai đoạn 1 → **14.99** (14 EF Core migrations, 1,167 bài test Backend xUnit, 744 bài test Frontend Vitest).
> **Cấu trúc tài liệu chia task:**
> 1. [`phase-15-0-external-integrations-master-plan.md`](./phase-15-0-external-integrations-master-plan.md) *(file này — Tổng quan, Nguyên tắc bất biến & Kết quả Live Test đầu kỳ)*
> 2. [`phase-15-1-foundation-schema-and-task-numbers.md`](./phase-15-1-foundation-schema-and-task-numbers.md) *(Sub-phase 15.1 — Migration #15, Mã `TN-123`, Cột `is_in_review`, `GitHub Bot` & `ITaskAutomation`)*
> 3. [`phase-15-2-outbound-slack-discord-webhooks.md`](./phase-15-2-outbound-slack-discord-webhooks.md) *(Sub-phase 15.2 — Outbound Slack Block Kit / Discord Embeds, SSRF Guard & Transactional Outbox)*
> 4. [`phase-15-3-inbound-github-webhooks-and-pr-automation.md`](./phase-15-3-inbound-github-webhooks-and-pr-automation.md) *(Sub-phase 15.3 — Inbound GitHub Webhook, HMAC-SHA256, `TaskKeyParser` & PR State Machine + Undo)*
> 5. [`phase-15-4-github-issues-jev-triage-and-comments.md`](./phase-15-4-github-issues-jev-triage-and-comments.md) *(Sub-phase 15.4 — GitHub Issue → Task + Jev 1.13 Auto-Triage & Đồng bộ Comment 1 chiều)*
> 6. [`phase-15-5-frontend-integrations-and-e2e-verification.md`](./phase-15-5-frontend-integrations-and-e2e-verification.md) *(Sub-phase 15.5 — Giao diện React 19 / Ant Design v6, Vitest / xUnit Gates & Đồng bộ Tài liệu)*

---

## 0. Nguyên tắc bất biến của Giai đoạn 15 (vi phạm = dừng lại và báo cáo)

1. **Giữ nguyên Modular Monolith 4 Modules (`Auth`, `Board`, `Ai`, `Reporting`) & 100% Minimal APIs.** Không tạo project `.csproj` thứ 5. Toàn bộ dịch vụ và endpoints tích hợp đặt trong `TeamNexus.Modules.Ai` (dưới `Services/Integrations`, `Endpoints/*Endpoints.cs`, `DTOs/Integrations`, đăng ký vào `AiModule.AddAiModule` & `AiModule.MapAiModuleEndpoints`), giao tiếp với `Board` qua port hẹp `ITaskAutomation`. Tuyệt đối không dùng MVC `*Controller.cs` (theo chuẩn [`AGENTS.md`](../../AGENTS.md)).
2. **`Ai → Board` là quan hệ một chiều.** `Board` tuyệt đối không `using TeamNexus.Modules.Ai.*`.
3. **Đúng 1 Migration duy nhất (`Phase15ExternalIntegrations` — Migration #15).** Sau khi tạo migration, `dotnet ef migrations has-pending-model-changes` phải trả về `"No changes have been made to the model since the last migration."`
4. **Hợp đồng DTO chỉ thêm trường ở cuối (Append-Only).** Mọi trường mới trên `TaskResponse` (`TaskNumber`, `ExternalLinks`) và `ColumnResponse` (`IsInReview`) phải đặt ở cuối danh sách tham số constructor với giá trị mặc định để không làm gãy bất kỳ call-site hay bài test cũ nào (chuẩn bị sẵn sàng cho Flutter Mobile ở Phase 16).
5. **Accountability Layer & Khả năng Hoàn tác (`AiActionLog` Undo).** Mọi hành động tự động từ GitHub Webhook làm thay đổi trạng thái thẻ (`GitHubPrMoveTask`, `GitHubIssueCreateTask`) đều phải ghi bản ghi `AiActionLog` (`EntityType = AiEntityTypes.Board`, `EntityId = boardId`, `Status = AiActionStatus.Approved`, `RequestedByUserId = SystemUsers.GitHubBotUserId`, kèm `BeforeSnapshot` và `AppliedSnapshot` chứa `updatedTaskId`/`fromColumnId` hoặc `createdTaskIds`) để hiển thị trên cả Lịch sử AI cấp Board lẫn Workspace và cho phép Owner/Manager bấm **Hoàn tác hành động (Undo)** qua `POST /api/ai-actions/{logId}/undo` mà không phải sửa constraint `ck_ai_action_logs_status`.
6. **Không rò rỉ Bí mật (Zero Secret Leakage) & Chống SSRF.**
   - `WebhookSecret` của GitHub chỉ trả về dạng plaintext **đúng 1 lần** lúc khởi tạo hoặc xoay vòng (`rotate-secret`), lưu DB dưới dạng mã hóa qua `IDataProtectionProvider`.
   - Mọi URL Outbound Webhook phải đi qua `WebhookUrlValidator` (chỉ cho phép HTTPS tới `hooks.slack.com`, `discord.com`, `discordapp.com`; cấm tuyệt đối localhost, loopback, link-local, RFC1918 private IPs).

---

## 1. Khảo sát đầu kỳ (Codebase Audit & Live Test 6 Chặng trên `https://app.teamnexus.cloud/`)

### 1.1. Số đo Baseline trước khi vào Phase 15
- **Backend Build:** `dotnet build TeamNexus.sln --no-incremental` → **0 Warning(s), 0 Error(s)**.
- **Backend Tests (`xUnit`):** **1,167 tests** (567 Pure tests chạy trực tiếp PASS 100%; 600 PostgreSQL Integration tests).
- **Frontend Lint & Typecheck:** `oxlint` **0 warnings / 0 errors** (288 files); `npx tsc -b` **exit 0**.
- **Frontend Tests (`Vitest`):** **123/123 test files PASS (744/744 tests PASS)**.
- **EF Core Migrations:** **14 migrations** (`20260909070708_InitialSchema` → `20261015122250_Phase147KnowledgeBase`).

### 1.2. Kết quả Live Test 6 Chặng bằng Browser trên `https://app.teamnexus.cloud/`

| Chặng | Phạm vi | Kết quả | Phát hiện thực tế quan trọng cho Phase 15 |
| :--- | :--- | :---: | :--- |
| **Chặng 0** | Pre-flight, Health & Theme | ✅ **PASS** | `https://api.teamnexus.cloud/api/health` trả `200 OK`. Giao diện Production chạy chuẩn **Luminous Enterprise v4.2** (`Be Vietnam Pro`, `#4f46e5`). |
| **Chặng 1** | Auth & Workspace Shell | ✅ **PASS** | Phiên đăng nhập `Trần Trường Thịnh_3944` (`Admin`/`Owner`) trên Workspace `Không Gian Làm Việc Chính` (`#7C02014A`). Sidebar 8 mục, SignalR `Đã kết nối`, `Trung tâm thông báo` hoạt động mượt. |
| **Chặng 2** | Kanban, Jev Triage & Lịch | ✅ **PASS** | Bảng `Sprint 1` (`3 cột · 5 thẻ`), chuyển đổi `Bảng Kanban` / `Lịch` mượt. Jev System-1 Auto-Triage phản hồi trong **`943 ms`** với **`AI tự tin 98%`**. |
| **Chặng 3** | `TaskDetailModal` & AI Chat | ✅ **PASS** | Header của `TaskDetailModal` đang hiển thị mã rút gọn 8 ký tự hex `#850FB54F` → **Điểm chạm chính xác để nâng cấp lên `TN-123` ở Sub-phase 15.5**. |
| **Chặng 4** | Accountability Layer (Undo) | ✅ **PASS** | Modal `Lịch sử Hành động AI` hiển thị 5 tab lọc, nút `Duyệt & áp dụng` và `Hoàn tác hành động` hoạt động ổn định → **Sẵn sàng tái sử dụng cho `GitHubPrMoveTask`**. |
| **Chặng 5** | AI Observer & RAG `pgvector` | ✅ **PASS** | 17 lịch sử quét Observer (`Jev System 1: Critical 91%`), cảnh báo tiếng Việt trong `Trung tâm thông báo` → **Nguồn dữ liệu thực tế sẽ đẩy ra Slack/Discord ở Sub-phase 15.2**. |
| **Chặng 6** | Báo cáo, Cài đặt & Hồ sơ | ✅ **PASS** | Trang `/workspaces/:id/settings` hiện chỉ có 1 thẻ `Thông tin chung` + nút `Chỉnh Sửa Cài Đặt` (`data-testid="open-settings-modal-btn"`) → **Điểm chạm sạch để nâng cấp thành `Tabs` (`Cài đặt chung` + `Tích hợp Slack / Discord / GitHub`) ở Sub-phase 15.5**. |

### 1.3. Phát hiện từ Live Test được đưa vào xử lý trong Phase 15
- **Bug nhỏ về cờ `Degraded` khi Jev Smart Assignee trả về `ManualRequired`:**
  - *Hiện tượng trên Production:* Khi bấm `Gợi ý người làm (Jev)` trên thẻ `#850FB54F`, API trả về trong `513 ms` nhưng hiển thị đồng thời cảnh báo vàng *"Dịch vụ AI tạm không khả dụng"* và *"Chưa có ứng viên đạt độ tương thích cao"*.
  - *Nguyên nhân:* `TaskTriageService.cs` dòng 131 & 197 đang gán `Degraded: result.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired`. Khi Jev chạy thành công (`LatencyMs > 0`) nhưng độ tự tin `< 0.50`, `RoutingAdvice` là `ManualRequired` khiến `Degraded` bị bật oan.
  - *Cách xử lý:* Sửa dứt điểm trong **Task `T15.4.1`** (`Degraded` chỉ bằng `true` khi provider thực sự lỗi / fallback `LatencyMs == 0` hoặc thông báo lỗi).

---

## 2. Bản đồ Tổng hợp 5 Sub-phases & 24 Tasks

| Sub-phase | File Tài liệu Chi tiết | Mã Tasks | Trọng tâm Kỹ thuật | Cổng Kiểm thử (Gate) |
| :--- | :--- | :---: | :--- | :--- |
| **15.1** | [`phase-15-1-foundation-schema-and-task-numbers.md`](./phase-15-1-foundation-schema-and-task-numbers.md) | `T15.1.1` → `T15.1.5` | Migration #15 (`Phase15ExternalIntegrations`), 6 bảng mới, mã `TN-123` (`task_numbers`), cột `is_in_review`, tài khoản hệ thống `GitHub Bot`, port `ITaskAutomation`. | Build 0 lỗi, `has-pending-model-changes` sạch, Pure + Integration tests cho `TaskNumber` & `IsInReview` PASS. |
| **15.2** | [`phase-15-2-outbound-slack-discord-webhooks.md`](./phase-15-2-outbound-slack-discord-webhooks.md) | `T15.2.1` → `T15.2.5` | Mở rộng `IExternalAlertSink`, sự kiện `MemberJoined`, `WebhookUrlValidator` (SSRF guard), Slack Block Kit & Discord Embed formatters, `WebhookOutboxExternalAlertSink`, `WebhookOutboxDispatcher`, `WorkspaceWebhooksEndpoints`. | Pure tests (SSRF + Formatters) & Integration tests (`WorkspaceWebhooksApiTests`) PASS. |
| **15.3** | [`phase-15-3-inbound-github-webhooks-and-pr-automation.md`](./phase-15-3-inbound-github-webhooks-and-pr-automation.md) | `T15.3.1` → `T15.3.5` | `GitHubSecretProtector` (`DataProtection`), `GitHubSignatureValidator` (`HMAC-SHA256`), `TaskKeyParser` (`TN-123`/`#123`), `WorkspaceGitHubIntegrationEndpoints`, `GitHubWebhookEndpoints`, PR State Machine + `AiActionLog` Undo (`GitHubPrMoveTask`). | Pure tests (`TaskKeyParser`, `HMAC-SHA256`) & Integration tests (`GitHubWebhookPrTests`) PASS. |
| **15.4** | [`phase-15-4-github-issues-jev-triage-and-comments.md`](./phase-15-4-github-issues-jev-triage-and-comments.md) | `T15.4.1` → `T15.4.4` | `ITaskTriageService.TriageForSystemAsync` (+ fix `Degraded`), luồng `issues` (`opened`/`labeled`/`closed`/`reopened`) + Jev 1.13 Auto-Triage, luồng `issue_comment.created` đồng bộ 1 chiều qua `GitHub Bot`. | Integration tests (`GitHubWebhookIssueAndCommentTests`) PASS. |
| **15.5** | [`phase-15-5-frontend-integrations-and-e2e-verification.md`](./phase-15-5-frontend-integrations-and-e2e-verification.md) | `T15.5.1` → `T15.5.5` | Types & API client, badge `TN-123` + chip PR/Issue trên `TaskCard` & `TaskDetailModal`, vai trò cột `isInReview` trên `KanbanColumn`, tab `Tích hợp` trên `WorkspaceSettingsPage`, Vitest tests & cập nhật tài liệu dự án. | `oxlint` 0/0, `tsc -b` exit 0, Vitest 100% PASS, `dotnet test` 100% PASS. |

