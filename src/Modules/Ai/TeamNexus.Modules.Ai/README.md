# TeamNexus.Modules.Ai — Phân hệ Trí tuệ Nhân tạo Lai, Giải trình, RAG & Tích hợp Hệ sinh thái

Phân hệ trung tâm hiện thực hóa kiến trúc **Hybrid AI (TypeSafe Jev 1.13 System-1 + DeepSeek System-2)**, **Lớp Giải trình Minh bạch (AI Accountability Layer)**, **Tác nhân Tự chủ Đa Nhân cách (Multi-Agent Personas & 7 ReAct Tools)**, **Cơ sở Tri thức Ngữ nghĩa (`pgvector` Semantic RAG)**, **Quan sát Chủ động & Tự phục hồi (`AiObserverService` & `RecoveryPlan`)**, **Hội thoại Thời gian thực qua SSE**, **Email Giao dịch & Daily Digest**, cùng **Tích hợp Hệ sinh thái Mở (Slack, Discord, GitHub Webhooks)**.

> **Bất biến kiến trúc:** Phân hệ `Ai` được phép tham chiếu `TeamNexus.Modules.Board` (chiều một chiều `Ai → Board`), đồng thời cung cấp các triển khai thật ghi đè lên các cổng mặc định (`IActivityLogWriter`, `INotificationWriter`, `IEmailGateway`, `IAiAgentResolver`) đã đăng ký trước ở `BoardModule`.

---

## 1. Kiến trúc 10 Phân khu Chức năng trong `TeamNexus.Modules.Ai`

### 1.1. Nhà cung cấp AI & Chế độ Giả lập Tất định (Providers & Deterministic Offline Fakes)
Khi khóa API tương ứng trong cấu hình (`DeepSeek:ApiKey`, `Jev:ApiKey`, `Embeddings:ApiKey`, `Tavily:ApiKey`, `Email:ApiKey`) để trống, `AiModule` tự động đăng ký các bộ giả lập ngoại tuyến tất định (`0` token, `0` kết nối mạng) phục vụ kiểm thử tự động và phát triển cục bộ:

| Cổng giao tiếp (Port) | Triển khai Thực tế (Khi có API Key) | Triển khai Ngoại tuyến Tất định (Khi rỗng Key) |
|---|---|---|
| `IAiProvider` | `DeepSeekAiProvider` (OpenAI-compatible `/chat/completions`, SSE & Tool Calling) | `FakeAiProvider` (tất định theo ngữ cảnh & `AgentChatPrompts.Marker`) |
| `IJevDecisionProvider` | `OpenRouterJevDecisionProvider` (`POST /api/alpha/decisions`) | `FakeJevDecisionProvider` (điều khiển qua sentinel `FAKE:JEV_HIGH\|MED\|LOW\|ERROR\|SLOW`) |
| `IEmbeddingProvider` | `OpenRouterEmbeddingProvider` (`POST /embeddings`, `1536` chiều) | `FakeEmbeddingProvider` (sinh vector chuẩn hóa tất định từ băm SHA-256) |
| `IWebSearchProvider` | `TavilySearchProvider` (REST API tìm kiếm Web thời gian thực) | `FakeWebSearchProvider` (trả về kết quả mẫu tất định) |
| `IEmailSender` | `ResendEmailSender` (HTTP API) / `SmtpEmailSender` (MailKit) | `NullEmailSender` (ghi log an toàn, không gửi mạng) |

---

### 1.2. Lớp Giải trình Minh bạch (`AiActionService` & 10 `IAiActionApplier`)
Mọi hành động ghi hoặc biến đổi dữ liệu nghiệp vụ do AI đề xuất hoặc do GitHub Webhook tự động hóa đều đi qua bảng `ai_action_logs` với máy trạng thái so-sánh-và-hoán-đổi (CAS) nguyên tử và hỗ trợ **Hoàn tác (`Undo`) 100%**:

| `AiActionType` | Bộ thực thi (`IAiActionApplier`) | Hành vi `ApplyAsync` & `UndoAsync` |
|---|---|---|
| `SmartSetup` | `SmartSetupApplier` | Khởi tạo cột, nhãn và thẻ cho bảng mới; `Undo` xóa mềm toàn bộ phần tử vừa tạo |
| `CreateBoardTemplate` | `CreateBoardTemplateApplier` | Tạo bảng Kanban mới kèm cột (`is_done`) và 5–10 thẻ mẫu; `Undo` xóa mềm bảng |
| `CreateSubtasks` | `CreateSubtasksApplier` | Chia nhỏ thẻ công việc thành các thẻ con; `Undo` xóa mềm danh sách `createdTaskIds` |
| `UpdateTaskFields` | `UpdateTaskFieldsApplier` | Cập nhật tiêu đề, mô tả, độ ưu tiên, hạn chót hoặc người nhận; `Undo` khôi phục từ `before_snapshot` |
| `MoveTaskToColumn` | `MoveTaskToColumnApplier` | Di chuyển thẻ sang cột đích; `Undo` đưa thẻ về cột và vị trí cũ |
| `PostComment` | `PostCommentApplier` | Đăng kết quả của AI Agent hoặc lưu câu trả lời AI Chat thành bình luận; `Undo` xóa mềm bình luận |
| `ReassignTask` | `ReassignTaskApplier` | Điều chuyển người phụ trách thẻ; `Undo` khôi phục `assignee_id` ban đầu |
| `RecoveryPlan` | `RecoveryPlanApplier` | Thực thi 4 chiến lược tự phục hồi (`ReassignTask`, `SplitSubtasks`, `ExtendDeadline`, `EscalateManager`) |
| `CreateKnowledgeDocument` | `CreateKnowledgeDocumentApplier` | Tạo mới hoặc xóa mềm (`kind="Delete"`) tài liệu RAG trong `workspace_knowledge_documents` |
| `GitHubPrMoveTask` / `GitHubIssueCreateTask` | `GitHubPrMoveTaskApplier` / `GitHubIssueCreateTaskApplier` | Ghi nhận tự động hóa từ GitHub ở trạng thái `Approved` kèm snapshot để quản trị viên có thể `Undo` |

> **Quy tắc bất biến cho Applier:** `AiActionService.ApproveAsync` và `UndoAsync` đã mở sẵn giao dịch CSDL. Các lớp `IAiActionApplier` **không bao giờ** được gọi `_db.Database.BeginTransactionAsync()`, và nếu sửa trực tiếp trên `TeamNexusDbContext` thì phải gọi `await _db.SaveChangesAsync(ct)`.

---

### 1.3. Hệ thống 1 — TypeSafe Jev 1.13 (`IJevDecisionService` & `ConfidenceRouting`)
- **Giao thức:** Gọi `POST {Jev:BaseUrl}{Jev:DecisionsPath}` (`/api/alpha/decisions`) với các câu hỏi có kiểu tĩnh (`choice`, `score`, `noul`).
- **Định tuyến Độ tự tin (`ConfidenceRouting`):**
  - `confidence ≥ 0.85` → `DirectAssign` (gán trực tiếp phương án tối ưu).
  - `0.50 ≤ confidence < 0.85` → `OfferChoices` (hiển thị tối đa 3 phương án kèm `%` độ tự tin).
  - `confidence < 0.50` → `ManualRequired` (xóa trắng mọi trường gợi ý về `null` để chống ảo giác).
- **Bảo vệ Quyền riêng tư (Zero-GUID / Zero-PII):** Khi xếp hạng ứng viên (`RecommendAssigneeAsync`), hệ thống ánh xạ thành viên sang nhãn ẩn danh (`cand-1`, `cand-2`) và không gửi `Guid` hay `email` ra ngoài.

---

### 1.4. Quan sát Chủ động, Dự báo Rủi ro & Kế hoạch Tự phục hồi (`AiObserverService`)
- Tiến trình nền `AiObserverBackgroundService` định kỳ quét các Workspace có hoạt động mới để phát hiện 4 nhóm tín hiệu:
  1. `OverdueTask`: Thẻ đã quá hạn chót.
  2. `AtRiskDeadline`: Thẻ chưa hoàn thành, còn `< 20%` quỹ thời gian (sàn cửa sổ $\ge 2$ ngày) và không có cập nhật trong `48 giờ` (dùng chung quy tắc tất định `DeadlineRiskRules` với chỉ số `ProjectHealth.Compute`).
  3. `Bottleneck`: Cột có số lượng thẻ mở vượt ngưỡng.
  4. `WorkloadOverload`: Thành viên đang gánh số thẻ mở vượt ngưỡng WIP.
- Tích hợp **Jev System-1 Pre-Filter** (`AssessObserverSignalsAsync` & `DecideRecoveryActionAsync`) để thẩm định mức độ nghiêm trọng và tự động đề xuất `RecoveryPlan` khi cần thiết.

---

### 1.5. Tác nhân Thực thi Đa Nhân cách (`AiAgentExecutorService`, Personas & 7 ReAct Tools)
- **4 Nhân cách Chuyên gia (`PersonaCatalog`):**
  - `general` (*TeamNexus Agent*), `scrum_master` (*AI Scrum Master*), `tech_lead` (*AI Tech Lead*), `qa_security` (*AI QA & Security*).
- **Vòng lặp ReAct 7 Công cụ:**
  1. `SearchSystemData`: Tìm kiếm thẻ công việc, bình luận và các đoạn tài liệu ngữ nghĩa RAG (`IWorkspaceKnowledgeStore`).
  2. `WebSearch`: Tìm kiếm thông tin Internet qua Tavily API.
  3. `InspectMemberWorkload`: Kiểm tra tải công việc hiện tại của thành viên (`IMemberWorkloadReader`).
  4. `MatchSkills`: Đối sánh yêu cầu kỹ năng với hồ sơ thành viên trong Workspace.
  5. `ValidateAcceptanceCriteria`: Thẩm định bản thảo đầu ra so với tiêu chí nghiệm thu bằng Jev System-1.
  6. `DraftOutput`: Gửi bản thảo kết quả vào `AiActionLog` (`Pending`) và chuyển thẻ sang cột hoàn thành/chờ duyệt.
  7. `RequestClarification`: Đăng câu hỏi làm rõ và tự động chuyển thẻ sang cột `is_clarification`.
- **Ngân sách bảo vệ (Guardrails):** Tối đa `15` lượt gọi công cụ, thời gian chạy `5 phút`, ngân sách `50.000 tokens` và `20` lượt gọi LLM mỗi lần thực thi (`agent_runs`). Cố vấn nền `AgentRunReaperRunner` tự động thu hồi các lượt chạy bị treo khi máy chủ khởi động lại.

---

### 1.6. Hội thoại Ngữ cảnh Thời gian thực qua SSE (`AiTaskChatService` & `AiBoardChatService`)
- Giao thức luồng sự kiện đồng nhất: `event: meta` → chuỗi `event: delta` → `event: done` (hoặc `event: error`).
- Hỗ trợ đính kèm nhiều thẻ công việc (`attachedTaskIds`), nén tóm tắt bảng (`BoardDigestBuilder`), chọn nhân cách chuyên gia (`?persona=`) và trích xuất các đề xuất hành động có cấu trúc (`CreateSubtasks`, `UpdateTaskFields`, `MoveTaskToColumn`).
- Bộ lọc `AiChatAnswerSanitizer` tự động thay thế mọi chuỗi UUID thô trong văn bản trả lời bằng tên hiển thị thân thiện trước khi lưu và phát luồng SSE.

---

### 1.7. Cơ sở Tri thức Ngữ nghĩa RAG với PostgreSQL `pgvector` (`WorkspaceKnowledgeService`)
- Lưu trữ tài liệu tại `workspace_knowledge_documents` và các đoạn văn bản kèm vector `embedding vector(1536)` tại `workspace_knowledge_chunks` (chỉ mục **HNSW `vector_cosine_ops`**).
- Mọi thao tác thêm/xóa tài liệu đều đi qua `AiActionType.CreateKnowledgeDocument`. Sau khi được phê duyệt, `KnowledgeEmbeddingRunner` chia đoạn (`KnowledgeTextChunker`) và sinh vector nhúng bất đồng bộ ở tiến trình nền.

---

### 1.8. Email Giao dịch & Bản tin Hàng ngày (`EmailGateway` & `DailyDigestRunner`)
- Gửi email lời mời tham gia Workspace và bản tin tổng hợp công việc mỗi sáng (**Daily Digest**).
- Ghi vết toàn bộ lịch sử gửi vào bảng `email_messages` kèm khóa chống trùng lặp `idempotency_key` (`UNIQUE` partial index) để đảm bảo mỗi người dùng nhận tối đa 1 bản tin/ngày ngay cả khi container khởi động lại nhiều lần.

---

### 1.9. Tích hợp Hệ sinh thái Bên ngoài (`Services/Integrations`)
- **Outbound (Slack & Discord):** `WebhookOutboxExternalAlertSink` và `WebhookOutboxRunner` gửi cảnh báo qua bảng `webhook_delivery_outbox` với cơ chế thử lại lũy tiến (`5s → 30s → 2m → 10m → 30m`), cầu chì tự ngắt sau 10 lỗi liên tiếp và bộ lọc chống SSRF `WebhookUrlValidator`.
- **Inbound (GitHub Webhooks):** `GitHubWebhookEndpoints` (`POST /api/webhooks/github/{webhookToken:guid}`) xác thực chữ ký `HMAC-SHA256` thời gian hằng (`WebhookSignatureVerifier`), chống phát lại qua `github_webhook_deliveries`, phân tích mã thẻ `TN-123` (`TaskKeyParser`), tự động chuyển cột PR (`is_in_review` / `is_done`), tự động tạo thẻ từ Issue kèm `TriageForSystemAsync`, đồng bộ bình luận một chiều và hỗ trợ hoàn tác 100%.

---

## 2. Tổng hợp các Điểm cuối HTTP (`Endpoints/*Endpoints.cs`)

| Tệp Điểm cuối | Các Đường dẫn Chính |
|---|---|
| `AiEndpoints.cs` | `POST /api/boards/{boardId}/ai/smart-setup` · `POST /api/workspaces/{workspaceId}/ai/board-template` · `GET /api/workspaces/{workspaceId}/ai-actions` · `POST /api/ai-actions/{id}/approve\|reject\|undo` |
| `ObserverEndpoints.cs` | `GET /api/workspaces/{workspaceId}/observer/runs` · `POST /api/workspaces/{workspaceId}/observer/scan` · `GET /api/notifications` · `PUT /api/notifications/{id}/read` · `PUT /api/notifications/read-all` |
| `AgentEndpoints.cs` | `POST /api/tasks/{taskId}/agent/run` · `GET /api/tasks/{taskId}/agent/runs` · `GET /api/agent-runs/{runId}` |
| `AiChatEndpoints.cs` | `POST /api/tasks/{taskId}/ai-chat/stream` (SSE) · `POST /api/tasks/{taskId}/ai-chat/save-comment` |
| `BoardAgentChatEndpoints.cs` | `GET|DELETE /api/boards/{boardId}/agent-chat/session` · `POST /api/boards/{boardId}/agent-chat/stream?persona=` (SSE) · `POST /api/boards/{boardId}/agent-chat/proposals/{proposalId}/confirm` |
| `JevEndpoints.cs` | `GET|PUT /api/workspaces/{workspaceId}/taxonomies` · `PUT /api/workspaces/{workspaceId}/members/{userId}/profile` · `POST /api/boards/{boardId}/tasks/triage` · `POST /api/boards/{boardId}/tasks/{taskId}/assignee-suggestions` |
| `KnowledgeEndpoints.cs` | `GET|POST /api/workspaces/{workspaceId}/knowledge/documents` · `DELETE /api/workspaces/{workspaceId}/knowledge/documents/{docId}` |
| `WebhookEndpoints.cs` | `GET|POST /api/workspaces/{workspaceId}/webhooks` · `PUT|DELETE .../webhooks/{endpointId}` · `POST .../webhooks/{endpointId}/test` |
| `GitHubIntegrationEndpoints.cs` | `GET|PUT /api/workspaces/{workspaceId}/github-integration` · `POST .../github-integration/rotate-secret` · `GET|POST /api/tasks/{taskId}/external-links` · `DELETE .../external-links/{linkId}` |
| `GitHubWebhookEndpoints.cs` | `POST /api/webhooks/github/{webhookToken:guid}` (`AllowAnonymous`, giới hạn 256 KB, xác thực `HMAC-SHA256`) |
