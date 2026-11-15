# TeamNexus – Định hướng Chuyển đổi từ Dự án Cá nhân sang Đồ án Học thuật

> **Tài liệu thứ 05 của `project-docs/`.** Bốn tài liệu trước ([`01-system-specification.md`](01-system-specification.md), [`02-tech-stack-decisions.md`](02-tech-stack-decisions.md), [`03-roadmap.md`](03-roadmap.md), [`04-database-design.md`](04-database-design.md)) mô tả **đặc tả sản phẩm, kiến trúc kỹ thuật, lộ trình thực thi và thiết kế cơ sở dữ liệu**. Tài liệu này mô tả **sự chuyển đổi chiến lược**: **TeamNexus** từ một **dự án cá nhân (Personal Portfolio Project)** được kế thừa, chuẩn hóa phương pháp luận và phát triển mở rộng thành một **Đồ án Học thuật (Academic Research & Engineering Project)** có chiều sâu nghiên cứu về Trí tuệ Nhân tạo Lai (Hybrid AI), Ngữ nghĩa học Vector (Semantic RAG) và Hệ sinh thái Mở (Open Developer Ecosystem).
>
> **Liên quan:** [`03-roadmap.md`](03-roadmap.md) (nguồn sự thật về tiến độ & số đo) · [`06-adjust-and-upgrade-with-jev.md`](06-adjust-and-upgrade-with-jev.md) (kiến trúc Hybrid AI System-1/System-2) · [`07-external-integrations-design.md`](07-external-integrations-design.md) (thiết kế tích hợp hệ sinh thái bên ngoài).

---

## 1. BỐI CẢNH & ĐỘNG LỰC CHUYỂN ĐỔI

| Tiêu chí | **Giai đoạn Dự án Cá nhân (Personal Project)** | **Giai đoạn Đồ án Học thuật (Academic Project)** |
|---|---|---|
| **Mục tiêu cốt lõi** | Xây dựng sản phẩm thực tế phục vụ hồ sơ năng lực (Portfolio), chứng minh kỹ năng Full-Stack & DevOps | Giải quyết bài toán khoa học – kỹ thuật có cơ sở lý thuyết, phương pháp luận rõ ràng và bảo vệ trước Hội đồng Học thuật |
| **Động lực hoàn thiện** | *"Hệ thống chạy ổn định, giao diện mượt, triển khai thực tế trên Internet"* | *"Kiến trúc chặt chẽ, định chuẩn hóa được hiệu quả AI, đảm bảo an toàn dữ liệu và có đóng góp kỹ thuật kiểm chứng được"* |
| **Tiêu chí đánh giá** | Tính năng hoàn chỉnh, trải nghiệm người dùng, vận hành thực tế | + **Câu hỏi nghiên cứu (RQs)**, **Kiến trúc nhận thức kép (System 1 & System 2)**, **Độ chính xác định tuyến tự động**, **Tính giải trình & hoàn tác 100%**, **Tính tái lập (Reproducibility)** |
| **Bằng chứng bắt buộc** | Kiểm thử tự động xanh, môi trường Production hoạt động | + Báo cáo nghiệm thu từng giai đoạn, số đo độ trễ & chi phí token, kiểm chứng an toàn bảo mật (SSRF, HMAC, Zero-GUID), tài liệu thiết kế kiến trúc |
| **Phạm vi hệ thống** | Nền tảng Web Kanban thời gian thực + Tác nhân AI cơ bản (Giai đoạn 1 → 13) | + **Hệ thống Trí tuệ Nhân tạo Nâng cao & Hybrid AI + Semantic RAG** (Giai đoạn 14 → 14.7)<br>+ **Tích hợp Hệ sinh thái Công cụ Phát triển Mở** (Giai đoạn 15)<br>+ **Ứng dụng Di động Đa nền tảng với Flutter** (Giai đoạn 16) |

**Điểm mấu chốt về Minh bạch Học thuật:**
Quá trình chuyển đổi **không phải** là xây dựng lại từ con số không, mà tận dụng một nền tảng phần mềm đã vận hành thực tế trên Internet làm **bàn đạp thực nghiệm (empirical testbed)**. Để đảm bảo tính minh bạch học thuật tuyệt đối, tài liệu này phân định rành mạch giữa **Nền tảng kế thừa trước chuyển đổi (Giai đoạn 1–13)** và **Khối lượng phát triển mở rộng & đóng góp mới trong Đồ án Học thuật (Giai đoạn 14–16)** tại Mục 3.

---

## 2. THÔNG TIN CHUNG & TRẠNG THÁI THỰC TẾ

* **Tên dự án:** **TeamNexus**
* **Hình thức thực hiện:** **Cá nhân** (Full-Stack Software & AI Engineering — từ kiến trúc Backend, giao diện Web, ứng dụng Mobile, đường ống AI/RAG, hạ tầng đám mây đến kiểm thử tự động).
* **Lĩnh vực:** Kỹ thuật Phần mềm & Trí tuệ Nhân tạo Ứng dụng — **Hệ thống Phân tán Thời gian thực**, **Kiến trúc Tác nhân Tự chủ có Giải trình (Accountable Agentic AI)**, **Tích hợp Hệ sinh thái DevOps & Đa nền tảng**.
* **Môi trường triển khai thực tế (Production):**
  * **Web Client:** [https://app.teamnexus.cloud](https://app.teamnexus.cloud) *(Vercel)*
  * **API Service:** [https://api.teamnexus.cloud](https://api.teamnexus.cloud) *(Render + Docker, ASP.NET Core .NET 10)*
  * **Cơ sở dữ liệu:** PostgreSQL 18 + tiện ích mở rộng vector `pgvector` *(Neon Serverless PostgreSQL)*
  * **Tên miền & Bảo mật mạng:** `teamnexus.cloud` — Cloudflare DNS, SSL/TLS Full (Strict), bảo vệ chống DDoS
  * **Email giao dịch:** `TeamNexus <noreply@teamnexus.cloud>` (xác thực SPF/DKIM/DMARC qua Cloudflare + Resend)

### 2.1. Đối chiếu Số đo Thực tế: Thời điểm Chuyển đổi vs. Hiện tại

Bảng dưới đây ghi nhận **baseline tại thời điểm bắt đầu chuyển đổi sang Đồ án Học thuật** (sau Giai đoạn 13) và **số đo thực tế đã đạt được** sau khi hoàn thành các giai đoạn mở rộng cốt lõi (Giai đoạn 14 → 15.5):

| Hạng mục đo lường | Tại thời điểm chuyển đổi (Hết Giai đoạn 13) | Trạng thái hiện tại (Sau Giai đoạn 14 → 15.5) | Tăng trưởng trong Đồ án Học thuật |
|---|---|---|---|
| **Giai đoạn hoàn thành** | **Giai đoạn 1 → 13** ✅ | **Giai đoạn 1 → 15.5** ✅ *(Giai đoạn 16 đang mở)* | **+5 giai đoạn lớn** (`14`, `14.5`, `14.6`, `14.7`, `15.1–15.5`) |
| **Kiểm thử tự động Backend (xUnit)** | **480 PASS** / 0 fail / 0 skip | **1.346 PASS** / 0 fail / 0 skip (`666` Pure + `680` Integration) | **+866 bài kiểm thử tự động (+180%)** |
| **Kiểm thử tự động Frontend (Vitest)** | **508 PASS** / 0 fail (`86` test files) | **799 PASS** / 0 fail (`129` test files) | **+291 bài kiểm thử tự động (+57%)** |
| **Tổng số bài kiểm thử toàn hệ thống** | **988 PASS** | **2.145 PASS** | **+1.157 bài kiểm thử tự động (+117%)** |
| **Số lượng Migration EF Core** | **10** (đến `Phase13DailyDigest`) | **16** (đến `Phase15DataProtectionKeyRing`) | **+6 migration**, `has-pending-model-changes` sạch |
| **Số bảng vật lý PostgreSQL** | **25 bảng** (`18` nghiệp vụ + `7` Identity) | **35 bảng** (`27` nghiệp vụ + `7` Identity + `1` DataProtection) | **+10 bảng mới** (RAG Vector, Chat, Webhooks, GitHub, Task Keys) |
| **Cổng chất lượng mã nguồn (Build Gate)** | `dotnet build` **0W/0E** · `oxlint` **0/0** · `tsc -b` **0** | `dotnet build` **0W/0E** · `oxlint` **0/0** · `tsc -b` **0** | Duy trì kỷ luật **Zero Warnings / Zero Errors** tuyệt đối |

---

## 3. PHÂN ĐỊNH KHỐI LƯỢNG: NỀN TẢNG KẾ THỪA vs. PHÁT TRIỂN MỞ RỘNG TRONG ĐỒ ÁN HỌC THUẬT

Đây là phần cốt lõi xác lập ranh giới đóng góp của đồ án:

```text
┌─────────────────────────────────────────────────────────┬─────────────────────────────────────────────────────────┐
│  PHẦN A — NỀN TẢNG KẾ THỪA TRƯỚC CHUYỂN ĐỔI (P1 → P13)  │  PHẦN B — PHÁT TRIỂN MỞ RỘNG TRONG ĐỒ ÁN (P14 → P16)    │
│  → Điều kiện xuất phát (Baseline hạ tầng & nghiệp vụ)   │  → Đóng góp nghiên cứu & kỹ thuật mới của Đồ án         │
├─────────────────────────────────────────────────────────┼─────────────────────────────────────────────────────────┤
│ • Kiến trúc Modular Monolith (.NET 10 + React 19)       │ [P14] AI TƯƠNG TÁC THỜI GIAN THỰC & DỰ BÁO SỨC KHỎE     │
│ • 4 phân hệ chuẩn: Auth, Board, Ai, Reporting           │   • AI Task Chat & Board Agent Chat qua SSE             │
│ • Xác thực OAuth2 (Google/GitHub), JWT + HttpOnly       │   • Đính kèm đa thẻ ngữ cảnh & đề xuất hành động trực   │
│   Cookie, xoay vòng Refresh Token, RBAC 2 lớp           │     tiếp trong khung hội thoại                          │
│ • Bảng Kanban thời gian thực (@dnd-kit + SignalR Hub)   │   • Tín hiệu dự báo sớm AtRiskDeadline & chỉ số         │
│ • AI Smart Setup khởi tạo dự án từ mô tả tự nhiên       │     Project Health Score (thang 100 điểm tất định)      │
│ • Lớp Giải trình AI (Accountability Layer) cơ bản:      │   • AI Board Template & tự động đồng bộ sau phê duyệt   │
│   Pending → Approve / Reject / Undo                     │                                                         │
│ • AI Observer cơ bản (Overdue, Bottleneck, Workload)    │ [P14.5 – P14.7] KIẾN TRÚC HYBRID AI, MULTI-AGENT & RAG  │
│ • AI Agent Executor cơ bản (4 tool-calls, ngân sách     │   • Kiến trúc Nhận thức Kép: Jev 1.13 (System 1) kết    │
│   15 calls / 5 phút / 50.000 tokens)                    │     hợp DeepSeek (System 2) + Confidence Routing 3 tầng │
│ • Xuất báo cáo PDF (QuestPDF) & Excel (ClosedXML)       │   • Chuẩn hóa Taxonomy & Kỹ năng: Auto-Triage phân loại │
│ • Quản lý Thành viên, Hồ sơ, Email (Resend) &           │     thẻ & Smart Assignee gợi ý người nhận việc          │
│   Notification Center                                   │   • Chủ động Tự phục hồi (RecoveryPlan 4 chiến lược)    │
│ • Dashboard, Tìm kiếm Keyset, @mention chống mạo danh,  │   • Đa Nhân cách Chuyên gia (4 Personas) & 7 ReAct Tools│
│   Chế độ Lịch biểu, Burndown/Velocity, Daily Digest     │   • Cơ sở Tri thức Ngữ nghĩa RAG với pgvector (HNSW)    │
│ • Hạ tầng Cloud Production + CI/CD (988 tests)          │                                                         │
│                                                         │ [P15] TÍCH HỢP HỆ SINH THÁI CÔNG CỤ BÊN NGOÀI           │
│                                                         │   • Transactional Outbox đẩy cảnh báo sang Slack/Discord│
│                                                         │   • GitHub Webhooks (HMAC-SHA256): tự động chuyển trạng │
│                                                         │     thái PR (In-Review/Done), tạo thẻ từ Issue kèm Jev  │
│                                                         │     Auto-Triage, đồng bộ bình luận 1 chiều & Undo 100%  │
│                                                         │                                                         │
│                                                         │ [P16] ỨNG DỤNG DI ĐỘNG ĐA NỀN TẢNG VỚI FLUTTER          │
│                                                         │   • Ứng dụng iOS/Android dùng chung REST API & SignalR  │
└─────────────────────────────────────────────────────────┴─────────────────────────────────────────────────────────┘
```

### 3.1. Chi tiết các Đóng góp Trí tuệ Nhân tạo (AI Contributions) trong Đồ án Học thuật

Trong giai đoạn chuyển đổi sang Đồ án Học thuật (Giai đoạn 14 → 14.7), phân hệ AI của TeamNexus đã được nâng cấp toàn diện từ mô hình gọi LLM đơn lẻ sang **Kiến trúc Điều phối Nhận thức Kép (Dual-System Cognitive Orchestration)** với 6 đóng góp lớn:

1. **Hội thoại AI Ngữ cảnh Thời gian thực & Đề xuất Hành động Trực tiếp (SSE Contextual Chat & Action Proposals):**
   * Xây dựng giao thức truyền tải luồng Server-Sent Events (`meta` → `delta*` → `done | error`) cho cả cấp độ Thẻ công việc (`POST /api/tasks/{id}/ai-chat/stream`) và cấp độ Bảng (`POST /api/boards/{id}/agent-chat/stream`).
   * Cho phép đính kèm nhiều thẻ công việc vào phiên chat (`attachedTaskIds`), tự động nén toàn cảnh bảng thành **Lightweight Board Digest** (giữ nguyên độ sâu ngữ cảnh nhưng tiết kiệm ngân sách token).
   * Tác nhân AI có khả năng sinh đề xuất hành động có cấu trúc ngay trong luồng chat (`CreateSubtasks`, `UpdateTaskFields`, `MoveTaskToColumn`) và lưu trữ tại `board_chat_sessions` / `board_chat_messages`.
   * Thiết lập hàng rào khử định danh hai đầu (**Two-Ended Identifier Sanitization**: `AiChatAnswerSanitizer` ở Backend và `redactIdentifiers` ở Frontend) giúp loại bỏ hoàn toàn hiện tượng rò rỉ UUID thô ra giao diện hội thoại mà vẫn bảo toàn nguyên vẹn khối JSON hành động.

2. **Kiến trúc Trí tuệ Nhân tạo Lai — Hệ thống 1 (Jev 1.13) & Hệ thống 2 (DeepSeek) kèm Định tuyến Độ tự tin (`ConfidenceRouting`):**
   * Lấy cảm hứng từ lý thuyết nhận thức kép (*Thinking, Fast and Slow*), hệ thống tách biệt rõ hai luồng xử lý:
     * **Hệ thống 1 (`IJevDecisionProvider` — TypeSafe Jev 1.13 Decisions API `/api/alpha/decisions`):** Thực hiện các quyết định phân loại có cấu trúc (`pick`, `tag`, `Order`, `noul`) với độ trễ cực thấp (`~100–300ms`), đầu ra bị khóa chặt vào tập ứng viên hợp lệ và trả về điểm tự tin định lượng `confidence ∈ [0, 1]`.
     * **Hệ thống 2 (`IAiProvider` — DeepSeek Chat/Reasoner):** Đảm nhiệm suy luận đa bước, lập kế hoạch dự án, tổng hợp tài liệu RAG và sinh văn bản giải trình chuyên sâu.
   * **Cơ chế Định tuyến theo Độ tự tin (`ConfidenceRouting`):** Kiểm soát mức độ tự chủ của AI bằng ngưỡng toán học tất định:
     * `confidence ≥ 0.85` → **`DirectAssign`**: Tự động điền sẵn gợi ý độ ưu tiên, nhãn và nhân sự.
     * `0.50 ≤ confidence < 0.85` → **`OfferChoices`**: Hiển thị top phương án kèm phần trăm độ tự tin để người dùng lựa chọn.
     * `confidence < 0.50` → **`ManualRequired`**: Tự động xóa trắng các trường gợi ý (`suggested* = null`), buộc con người quyết định thủ công để ngăn chặn ảo giác (hallucination).

3. **Chuẩn hóa Từ điển Workspace (`WorkspaceTaxonomy`), Tự động Phân loại (`Auto-Triage`) & Điều phối Nhân sự Thông minh (`Smart Assignee`):**
   * Xây dựng từ điển nhãn chuẩn hóa theo từng Workspace (`workspace_taxonomies`) và mô hình hóa năng lực thành viên qua `workspace_members.skills` kết hợp tải công việc thực tế (`activeTaskCount`, `overdueTaskCount`).
   * Khi người dùng nhập tiêu đề công việc mới, luồng **Auto-Triage** sử dụng Jev `pick` + `tag` để gán độ ưu tiên và nhãn chuẩn trong thời gian thực.
   * Luồng **Smart Assignee Dispatching** kết hợp Jev `Order` + `pick` để xếp hạng ứng viên phù hợp nhất. Để bảo vệ quyền riêng tư (**Zero PII / Zero GUID Leakage**), hệ thống ánh xạ thành viên thành nhãn ẩn danh tạm thời (`cand-1`, `cand-2`) trước khi gửi sang Jev và giải mã ngược tại máy chủ nội bộ.

4. **Quan sát Chủ động, Dự báo Sớm & Kế hoạch Tự phục hồi (`Proactive Observer` & `RecoveryPlan`):**
   * Thiết lập bộ quy tắc tất định `DeadlineRiskRules` phát hiện tín hiệu **`AtRiskDeadline`** (thẻ chưa hoàn thành, còn `< 20%` quỹ thời gian với sàn cửa sổ $\ge 2$ ngày, và không có cập nhật trong `48 giờ`), phân tầng mức độ nghiêm trọng (`Critical ≤ 24h`, `High ≤ 72h`, `Medium`).
   * Xây dựng hàm thuần **`ProjectHealth.Compute`** tính điểm Sức khỏe Dự án trên thang `0–100` từ 5 thành phần trọng số cộng đúng `100` (Quá hạn `40`, Rủi ro hạn chót `20`, Đình trệ `10`, Tuổi thọ thẻ mở `15`, Cân bằng tải nhân sự `15`).
   * Tích hợp Jev `noul` làm màng lọc tiền xử lý rủi ro (Pre-Filter) trước khi gọi DeepSeek, giúp tiết kiệm đáng kể chi phí LLM ở các chu kỳ quét nền.
   * Nâng cấp AI Observer từ cảnh báo thụ động sang **Chủ động Đề xuất Phục hồi (`RecoveryPlan` `AiActionType`)** với 4 chiến lược khả thi (`ReassignTask`, `SplitSubtasks`, `ExtendDeadline`, `EscalateManager`) có hỗ trợ hoàn tác (`Undo`) toàn phần.

5. **Đa Nhân cách Chuyên gia (`Multi-Agent Personas`) & Mở rộng Vòng lặp ReAct 7 Công cụ:**
   * Cho phép cấu hình nhiều thành viên ảo `ai_agent` trong cùng một Workspace với 4 nhân cách chuyên môn hóa (`general`, `scrum_master`, `tech_lead`, `qa_security`), được bảo vệ bởi ràng buộc duy nhất một phần (`uq_workspace_members_ai_persona`).
   * Mở rộng vòng lặp suy luận – hành động (ReAct Loop) của `AiAgentExecutorService` từ 4 lên **7 công cụ chuyên sâu**, bổ sung `InspectMemberWorkload` (kiểm tra tải nhân sự), `MatchSkills` (đối sánh kỹ năng với yêu cầu thẻ) và `ValidateAcceptanceCriteria` (kiểm định tiêu chí nghiệm thu trước khi đóng thẻ).

6. **Cơ sở Tri thức Ngữ nghĩa (Workspace Semantic RAG) với PostgreSQL `pgvector`:**
   * Thiết kế đường ống lưu trữ và truy xuất tri thức nội bộ trên bảng `workspace_knowledge_documents` và `workspace_knowledge_chunks` (cột `embedding vector(1536)` kết hợp chỉ mục láng giềng gần đúng **HNSW `vector_cosine_ops`**).
   * Tiến trình nền `KnowledgeEmbeddingRunner` tự động chia nhỏ tài liệu (`KnowledgeTextChunker`) và sinh vector nhúng bất đồng bộ với cơ chế khóa bi quan (`FOR UPDATE SKIP LOCKED`) và backoff lũy tiến.
   * Tích hợp tìm kiếm ngữ nghĩa trực tiếp vào công cụ `SearchSystemData` của AI Agent và luồng tạo đề xuất lưu trữ tài liệu mới (`CreateKnowledgeDocument` qua Accountability Layer).

### 3.2. Đóng góp Mở rộng Hệ sinh thái Mở (Giai đoạn 15 — Đã hoàn thành)

1. **Đường ống Cảnh báo Đa kênh (Slack & Discord) theo Mẫu Transactional Outbox:**
   * Hiện thực `WebhookOutboxExternalAlertSink` và `WebhookOutboxRunner` ghi nhận sự kiện cảnh báo vào bảng `webhook_delivery_outbox` trước khi phát tán sang Slack/Discord, đảm bảo tính tin cậy theo mô hình *At-Least-Once Delivery*.
   * Tích hợp cơ chế thử lại theo hàm mũ (`5s → 30s → 2m → 10m → 30m`, tối đa 5 lần) và **Cầu chì tự ngắt (Circuit Breaker)** tự động vô hiệu hóa (`is_enabled = false`) khi liên tiếp thất bại `10` lần.
   * Bảo vệ chống tấn công **SSRF (`WebhookUrlValidator`)**: bắt buộc giao thức `https://`, chỉ cho phép tên miền `hooks.slack.com` và `discord.com`/`discordapp.com`, chặn tuyệt đối địa chỉ IP trần, `localhost`, loopback và dải mạng nội bộ RFC1918.

2. **Tự động hóa Hai chiều với GitHub Webhooks & Định danh Thẻ Tuần tự (`TN-123`):**
   * Bổ sung hệ thống mã định danh thẻ thân thiện với nhà phát triển (`TN-1`, `TN-2`,... thông qua bộ đếm nguyên tử `workspaces.next_task_number` và bảng `task_numbers`).
   * Điểm cuối `POST /api/webhooks/github/{webhookToken:guid}` xác thực chữ ký số **HMAC-SHA256** bằng phép so sánh thời gian hằng (`CryptographicOperations.FixedTimeEquals`), chống phát lại (anti-replay) qua `X-GitHub-Delivery` và mã hóa bí mật ở trạng thái nghỉ bằng ASP.NET Core **DataProtection** (`data_protection_keys`).
   * **Máy trạng thái Pull Request tự động:** PR mở/reopen/ready-for-review chứa khóa `TN-123` tự động chuyển thẻ sang cột `is_in_review`; PR được **merged** tự động chuyển thẻ sang cột `is_done`.
   * **Issue-to-Task kết hợp Jev Auto-Triage:** Sự kiện mở GitHub Issue mang nhãn kích hoạt (`teamnexus`) tự động tạo thẻ công việc trên bảng mục tiêu, đồng thời gọi **Jev System-1 (`TriageForSystemAsync`)** để tự động phân loại độ ưu tiên và gắn nhãn taxonomy.
   * **Đồng bộ bình luận 1 chiều & Hoàn tác 100%:** Bình luận từ GitHub được ghi nhận thông qua tài khoản hệ thống `GitHub Bot` (`is_system = true`, bị khóa đăng nhập tương tác). Mọi hành động tự động từ GitHub đều ghi nhận đầy đủ `before_snapshot` / `applied_snapshot` trong `ai_action_logs` (`GitHubPrMoveTask`, `GitHubIssueCreateTask`), cho phép quản trị viên **Undo (Hoàn tác)** bất kỳ lúc nào.

---

## 4. TÊN ĐỀ TÀI HỌC THUẬT

**Tiếng Việt:**
> **"Nghiên cứu và Phát triển Hệ thống Điều phối Không gian Làm việc Đa nền tảng tích hợp Tác nhân Trí tuệ Nhân tạo Lai có Giải trình (TeamNexus)"**

**Tiếng Anh:**
> **"TeamNexus: A Cross-Platform Workspace Orchestration Platform with an Accountable Hybrid AI Agent Architecture and Semantic RAG"**

*Ghi chú định vị:* Tên đề tài làm nổi bật 4 trụ cột học thuật và kỹ thuật của hệ thống:
1. **Điều phối Không gian Làm việc (Workspace Orchestration):** Không chỉ lưu trữ công việc thụ động mà chủ động quan sát, dự báo rủi ro, điều phối nhân sự và tự phục hồi tiến độ.
2. **Trí tuệ Nhân tạo Lai (Hybrid AI System-1/System-2):** Kết hợp tốc độ và tính tất định của mô hình quyết định có cấu trúc (Jev 1.13) với năng lực suy luận sâu của mô hình ngôn ngữ lớn (DeepSeek).
3. **Tính Giải trình & Kiểm soát Con người (Accountability Layer):** Mọi tác động ghi dữ liệu của AI và tự động hóa ngoại vi đều minh bạch, có nhật ký trạng thái và hoàn tác được.
4. **Đa nền tảng & Hệ sinh thái Mở (Cross-Platform & Open Ecosystem):** Kết nối liền mạch Web, Mobile (Flutter), GitHub, Slack và Discord.

---

## 5. ĐẶT VẤN ĐỀ & CÂU HỎI NGHIÊN CỨU

### 5.1. Đặt vấn đề (Problem Statement)

1. **Hạn chế của các công cụ quản lý công việc truyền thống ("Máy ghi chép thụ động"):**
   Các công cụ phổ biến (Trello, Jira, Asana) phụ thuộc hoàn toàn vào việc con người tự nhập liệu, tự phân loại mức độ ưu tiên, tự tìm người phù hợp và tự rà soát tiến độ. Khi dự án mở rộng, quản lý thường chỉ phát hiện trễ hạn khi sự việc đã xảy ra.
2. **Bài toán đánh đổi giữa Độ trễ, Chi phí và Ảo giác khi tích hợp LLM thuần túy:**
   Việc sử dụng một mô hình ngôn ngữ lớn (System-2 LLM) cho mọi tác vụ nhỏ như gắn nhãn, chọn độ ưu tiên hay gợi ý người nhận việc gây ra độ trễ cao (`3–10 giây`), tốn kém token và dễ phát sinh đầu ra sai lệch cấu trúc (hallucination). Ngược lại, thiếu suy luận sâu thì AI không thể lập kế hoạch hay giải quyết các tình huống phức tạp.
3. **Thiếu ngữ cảnh tri thức nội bộ (Knowledge Silos):**
   Các tác nhân AI thông thường không nắm được quy chuẩn kỹ thuật, tài liệu kiến trúc hay quy trình riêng của từng đội ngũ, dẫn đến các đề xuất chung chung, thiếu tính khả thi.
4. **Đứt gãy giữa Quản lý Công việc và Thực tế Phát triển Phần mềm:**
   Thảo luận diễn ra trên Slack/Discord, mã nguồn và Pull Request nằm trên GitHub, trong khi thẻ công việc nằm trên bảng Kanban. Sự thiếu vắng cơ chế đồng bộ tự động có kiểm soát khiến trạng thái bảng luôn trễ pha so với tiến độ code thực tế.
5. **Rủi ro an toàn dữ liệu khi trao quyền hành động cho AI:**
   Cho phép AI hoặc webhook tự động sửa đổi dữ liệu trực tiếp trong môi trường nhiều người dùng mà không có cơ chế phê duyệt (Human-in-the-Loop) và hoàn tác (Undo) sẽ dẫn đến nguy cơ hỏng dữ liệu hàng loạt và mất niềm tin từ người dùng.

### 5.2. Câu hỏi Nghiên cứu (Research Questions — RQs)

| Mã | Câu hỏi nghiên cứu | Phương pháp giải quyết & Kiểm chứng trong Đồ án |
|:-:|---|---|
| **RQ1** | Làm thế nào để một tác nhân AI **hành động thực sự** trong hệ thống nhiều người dùng mà **không làm tổn hại tính toàn vẹn dữ liệu**? | Thiết kế **AI Accountability Layer** làm cổng ghi duy nhất (`Pending` → `Approve`/`Reject` → `Undo` dựa trên `before_snapshot` / `applied_snapshot`). Kiểm chứng qua 10 bộ `IAiActionApplier` có kiểm thử hoàn tác hai chiều. |
| **RQ2** | Làm thế nào để tối ưu hóa đồng thời **độ trễ phản hồi (< 500ms)** cho các quyết định tức thời và **độ sâu suy luận** cho các tác vụ phức tạp? | Kiến trúc **Hybrid AI**: ủy thác phân loại/xếp hạng nhanh cho **Jev 1.13 (System 1)** và suy luận đa bước cho **DeepSeek (System 2)**, kết hợp bộ định tuyến **`ConfidenceRouting`** 3 ngưỡng (`0.85` / `0.50`). |
| **RQ3** | Làm thế nào để ngăn chặn vòng lặp vô hạn, kiểm soát ngân sách token và bảo vệ quyền riêng tư khi gọi các nhà cung cấp AI bên ngoài? | Áp dụng **Guardrails định lượng** (`15` tool-calls, `5` phút, `50.000` tokens), tiền kiểm kích thước lịch sử chat trước khi gọi API, ẩn danh hóa ứng viên (`cand-N`) và khử UUID hai đầu (`AiChatAnswerSanitizer`). |
| **RQ4** | Làm thế nào để dự báo nguy cơ trễ hạn và đo lường sức khỏe dự án một cách **khách quan, tất định và giải thích được**? | Xây dựng bộ quy tắc **`DeadlineRiskRules`** (nguồn chân lý duy nhất cho cảnh báo `AtRiskDeadline`) và hàm thuần **`ProjectHealth.Compute`** (5 trọng số cộng đúng `100`), kết hợp đề xuất tự phục hồi **`RecoveryPlan`**. |
| **RQ5** | Làm thế nào để tác nhân AI truy xuất chính xác tri thức nội bộ của từng Workspace với hiệu năng cao? | Xây dựng đường ống **Semantic RAG** ngay trên PostgreSQL với tiện ích **`pgvector`** (`vector(1536)` + chỉ mục **HNSW `vector_cosine_ops`**), cô lập nghiêm ngặt theo `workspace_id`. |
| **RQ6** | Làm thế nào để tích hợp tự động hóa hai chiều với GitHub, Slack và Discord một cách **tin cậy, bảo mật và có thể hoàn tác**? | Áp dụng **Transactional Outbox** (kèm backoff + circuit breaker), xác thực **HMAC-SHA256**, chống **SSRF**, mã hóa bí mật bằng **DataProtection**, và ghi nhận mọi thay đổi từ GitHub qua `AiActionLog` để hỗ trợ **Undo 100%**. |
| **RQ7** | Làm thế nào để mở rộng trải nghiệm điều phối công việc và phê duyệt AI sang nền tảng di động mà không phải nhân bản logic nghiệp vụ? | Tái sử dụng 100% hợp đồng RESTful API (tuân thủ quy tắc *Append-Only DTO*) và `BoardHub` SignalR cho ứng dụng **Flutter** đa nền tảng (Giai đoạn 16). |

---

## 6. MA TRẬN CÔNG NGHỆ TOÀN HỆ THỐNG (TECH STACK MATRIX)

> Chi tiết lý do lựa chọn từng công nghệ được trình bày tại [`02-tech-stack-decisions.md`](02-tech-stack-decisions.md).

| Tầng / Phân hệ | Công nghệ cốt lõi | Chi tiết kỹ thuật | Vai trò trong Đồ án Học thuật |
|---|---|---|---|
| **Backend Host & API** | **ASP.NET Core (.NET 10)** | C# 13, Minimal APIs, Modular Monolith | 4 phân hệ nghiệp vụ độc lập (`Auth`, `Board`, `Ai`, `Reporting`) |
| **Real-time Engine** | **ASP.NET Core SignalR + SSE** | WebSocket (`BoardHub`) & Server-Sent Events | Đồng bộ Kanban tức thời & truyền phát luồng hội thoại AI theo từng token |
| **Cơ sở dữ liệu & Vector** | **PostgreSQL 18 + `pgvector`** | EF Core 10, `jsonb`, `vector(1536)`, HNSW index | Lưu trữ quan hệ, nhật ký JSON động và tìm kiếm tương đồng ngữ nghĩa RAG |
| **AI Hệ thống 1 (Fast)** | **TypeSafe Jev 1.13** | Decisions API (`/api/alpha/decisions`) | Phân loại Auto-Triage (`pick`, `tag`), xếp hạng Smart Assignee (`Order`), lọc rủi ro (`noul`) |
| **AI Hệ thống 2 (Slow)** | **DeepSeek API + OpenAI Embeddings** | Tool/Function Calling, `text-embedding-3-small` | Suy luận ReAct 7 công cụ, đa nhân cách (Personas), lập kế hoạch & sinh vector nhúng |
| **Web Intelligence** | **Tavily Search API** | REST Client có kiểm soát ngân sách | Cung cấp tri thức thời gian thực ngoài Internet cho công cụ `WebSearch` |
| **Tích hợp & Bảo mật (P15)** | **Webhooks + DataProtection** | HMAC-SHA256, SSRF Guard, Transactional Outbox | Kết nối Slack, Discord, GitHub PR/Issue an toàn và hoàn tác được |
| **Web Frontend** | **React 19.2 + TypeScript 6.0** | Vite 8.2, Ant Design v6 (Luminous Enterprise v4.2) | Giao diện quản trị không gian làm việc, biểu đồ trực quan & trung tâm kiểm duyệt AI |
| **Mobile Client (P16)** | **Flutter 3.x & Dart 3.x** | Material 3, `signalr_netcore`, `flutter_secure_storage` | Ứng dụng di động đa nền tảng iOS/Android đồng bộ thời gian thực |
| **Báo cáo & Email** | **QuestPDF · ClosedXML · Resend** | Xử lý luồng RAM, SPF/DKIM/DMARC | Xuất báo cáo PDF/Excel tức thời & gửi bản tin tổng hợp hàng ngày (Daily Digest) |
| **Kiểm thử & CI/CD** | **xUnit · Vitest · GitHub Actions** | `1.346` BE tests + `799` FE tests | Đảm bảo chất lượng tự động hóa và tính tái lập của toàn bộ công trình |

---

## 7. LỘ TRÌNH THỰC THI HỌC THUẬT & TRẠNG THÁI NGHIỆM THU

| Phân đoạn | Nội dung trọng tâm | Hồ sơ Đặc tả & Nghiệm thu | Trạng thái |
|:-:|---|---|:-:|
| **Phân đoạn 1** | **Chuẩn hóa Nền tảng Kế thừa & Khung Học thuật**<br>• Kiểm toán baseline Giai đoạn 1–13 (`480` BE / `508` FE tests, `10` migrations).<br>• Xác lập ranh giới kế thừa và bộ câu hỏi nghiên cứu `RQ1–RQ7`. | [`03-roadmap.md`](03-roadmap.md)<br>[`05-academic-project-transition.md`](05-academic-project-transition.md) | ✅ **HOÀN THÀNH** |
| **Phân đoạn 2** | **AI Tương tác Thời gian thực & Dự báo Sức khỏe (Giai đoạn 14)**<br>• AI Task Chat SSE, `AtRiskDeadline`, `ProjectHealth.Compute`, AI Board Template. | [`tasks/phase-14-ai-advanced.md`](tasks/phase-14-ai-advanced.md)<br>[`report/phase-14-ai-advanced-test-report.md`](report/phase-14-ai-advanced-test-report.md) | ✅ **HOÀN THÀNH** |
| **Phân đoạn 3** | **Trải nghiệm AI Native & Hội thoại Đa thẻ (Giai đoạn 14.5 & 14.6)**<br>• Bảng hội thoại AI cấp độ Board, đính kèm đa thẻ, đề xuất hành động trực tiếp, khử UUID hai đầu, chuẩn hóa giao diện Luminous Enterprise v4.2. | [`tasks/phase-14-5-board-ai-assistant-ux.md`](tasks/phase-14-5-board-ai-assistant-ux.md)<br>[`tasks/phase-14-6-ui-ux-enterprise-overhaul.md`](tasks/phase-14-6-ui-ux-enterprise-overhaul.md)<br>[`report/phase-14-6-ui-ux-enterprise-overhaul-test-report.md`](report/phase-14-6-ui-ux-enterprise-overhaul-test-report.md) | ✅ **HOÀN THÀNH** |
| **Phân đoạn 4** | **Kiến trúc Hybrid AI (Jev 1.13 + DeepSeek), Multi-Agent & Semantic RAG (Giai đoạn 14.7)**<br>• Track A: Jev System-1, Confidence Routing, Taxonomy, Auto-Triage, Smart Assignee.<br>• Track B: `RecoveryPlan`, 4 Personas, 7 ReAct Tools, `pgvector` Knowledge Base RAG. | [`06-adjust-and-upgrade-with-jev.md`](06-adjust-and-upgrade-with-jev.md)<br>[`tasks/phase-14-7-track-a-jev-system1-foundation.md`](tasks/phase-14-7-track-a-jev-system1-foundation.md)<br>[`tasks/phase-14-7-track-b-multi-agent-rag-recovery.md`](tasks/phase-14-7-track-b-multi-agent-rag-recovery.md)<br>[`report/phase-14-7-jev-hybrid-ai-test-report.md`](report/phase-14-7-jev-hybrid-ai-test-report.md) | ✅ **HOÀN THÀNH** |
| **Phân đoạn 5** | **Tích hợp Hệ sinh thái Công cụ Bên ngoài (Giai đoạn 15.1 → 15.5)**<br>• Slack/Discord Transactional Outbox + SSRF Guard + Circuit Breaker.<br>• GitHub Webhooks (HMAC-SHA256), khóa `TN-123`, PR State Machine, Issue-to-Task + Jev Triage, Comment Sync & Undo 100%. | [`07-external-integrations-design.md`](07-external-integrations-design.md)<br>[`tasks/phase-15-0-external-integrations-master-plan.md`](tasks/phase-15-0-external-integrations-master-plan.md)<br>[`report/phase-15-external-integrations-test-report.md`](report/phase-15-external-integrations-test-report.md) | ✅ **HOÀN THÀNH** |
| **Phân đoạn 6** | **Mở rộng Ứng dụng Di động Đa nền tảng với Flutter (Giai đoạn 16)**<br>• Ứng dụng Flutter (iOS/Android) kết nối chung REST API & `BoardHub` SignalR.<br>• Lưu trữ phiên làm việc an toàn (`flutter_secure_storage`) & duyệt đề xuất AI trên di động. | [`03-roadmap.md#giai-đoạn-16`](03-roadmap.md) | 🟡 **ĐANG MỞ (KẾ TIẾP)** |

---

## 8. TỔNG HỢP ĐÓNG GÓP HỌC THUẬT & GIÁ TRỊ THỰC TIỄN

1. **Mô hình Kiến trúc Nhận thức Kép (System-1 / System-2 Hybrid AI) trong Quản trị Dự án:**
   Chứng minh tính hiệu quả của việc kết hợp động cơ quyết định có cấu trúc tốc độ cao (**Jev 1.13**) với mô hình suy luận sâu (**DeepSeek**), đi kèm cơ chế định tuyến toán học **`ConfidenceRouting`** giúp loại bỏ ảo giác ở vùng độ tự tin thấp (`< 0.50`) và giảm thiểu chi phí gọi LLM.
2. **Bảo đảm Toàn vẹn Dữ liệu Tuyệt đối qua Lớp Giải trình (Accountability Layer):**
   Mọi hành động sinh dữ liệu của AI (tạo bảng, chia nhỏ công việc, cập nhật trường, lưu tài liệu RAG, tự phục hồi tiến độ) và mọi tác động tự động hóa từ GitHub Webhook đều đi qua một cổng kiểm soát duy nhất với khả năng **Hoàn tác (Undo) 100%** dựa trên ảnh chụp trạng thái (`before_snapshot` / `applied_snapshot`).
3. **Các Thuật toán Đánh giá Rủi ro và Sức khỏe Dự án Tất định, Kiểm chứng được:**
   Toàn bộ các phép đo cốt lõi (`DeadlineRiskRules`, `ProjectHealth.Compute`, `ConfidenceRouting`, `ReportAggregator.BuildProgressSeries`) được thiết kế dưới dạng **hàm thuần (pure functions)** không phụ thuộc trạng thái ngoại vi, cho phép kiểm thử đơn vị độc lập và giải thích minh bạch từng điểm số trước hội đồng.
4. **Hồ sơ Thực nghiệm Chuẩn mực & Vận hành Thực tế trên Đám mây:**
   Hệ thống không dừng ở mô hình thử nghiệm cục bộ mà đã vận hành thực tế tại `app.teamnexus.cloud` / `api.teamnexus.cloud`, được bảo chứng bởi **2.145 bài kiểm thử tự động** (`1.346` Backend + `799` Frontend), **16 migration EF Core** không trôi dạt lược đồ và hệ thống tài liệu kỹ thuật đồng bộ.

---

## 9. QUẢN TRỊ RỦI RO HỌC THUẬT & KỸ THUẬT (RISK REGISTER)

| Mã | Rủi ro nhận diện | Mức độ | Biện pháp kiểm soát đã thực thi |
|:-:|---|:-:|---|
| **R1** | **Mơ hồ giữa khối lượng dự án cá nhân cũ và khối lượng đồ án mới** | 🔴 Cao | Phân định tường minh tại Mục 3 kèm bảng đối chiếu số đo định lượng tại Mục 2.1 (`+866` BE tests, `+291` FE tests, `+6` migrations, `+10` bảng mới trong giai đoạn học thuật). |
| **R2** | **Chi phí và độ trễ API AI/Tìm kiếm vượt ngưỡng kiểm soát** | 🟡 Vừa | Áp dụng Guardrail cứng (`15` tool-calls / `5` phút / `50.000` tokens), tiền kiểm lịch sử chat (`MaxHistoryChars`), dùng Jev `noul` làm bộ lọc trước khi gọi DeepSeek, và hỗ trợ đầy đủ **Deterministic Offline Fakes** (`FakeAiProvider`, `FakeJevDecisionProvider`, `FakeEmbeddingProvider`) khi chạy kiểm thử. |
| **R3** | **Rủi ro bảo mật từ tích hợp bên thứ ba (Webhook giả mạo, SSRF, lộ khóa bí mật)** | 🟡 Vừa | Xác thực chữ ký `HMAC-SHA256` thời gian hằng, kiểm tra chống phát lại qua `delivery_id`, chặn SSRF bằng `WebhookUrlValidator`, mã hóa bí mật ở trạng thái nghỉ bằng `IDataProtectionProvider` và chỉ hiển thị khóa thô đúng một lần khi khởi tạo/xoay vòng. |
| **R4** | **Trôi dạt hợp đồng dữ liệu khi mở rộng sang ứng dụng Mobile (Giai đoạn 16)** | 🟡 Vừa | Áp dụng quy tắc bất biến **Append-Only DTO Contract** (chỉ thêm trường mới có giá trị mặc định ở cuối record) và giữ nguyên giao thức SignalR `BoardHub`. |
| **R5** | **Hạ tầng đám mây gặp sự cố mạng hoặc Cold Start khi trình diễn trực tiếp** | 🟢 Thấp | Hệ thống hỗ trợ khởi chạy cục bộ hoàn chỉnh qua Docker (`pgvector/pgvector:pg18`) với chế độ Offline Fakes tất định, đồng thời giao diện Web có cơ chế tự động kết nối lại (auto-reconnect) và thông báo trạng thái rõ ràng bằng tiếng Việt. |
