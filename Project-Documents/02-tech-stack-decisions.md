# TeamNexus – Quyết định Công nghệ & Lưu ý Phát triển

> Tài liệu này chốt lựa chọn công nghệ cho dự án TeamNexus và các lưu ý cần tuân theo trong quá trình phát triển. Bối cảnh: dự án làm solo (full-stack), mục tiêu portfolio/CV cá nhân (có thể mở rộng thành đồ án tốt nghiệp), deadline dài, triển khai Web trước — Mobile (Flutter) sau, deploy trên hạ tầng cloud free-tier/chi phí thấp.

## 1. Kiến trúc tổng thể

**Modular Monolith trên ASP.NET Core** — không tách microservices.

- Tách code theo module domain: `Auth`, `Board` (Kanban), `Ai` (Smart Setup + Observer + Accountability), `Reporting`.
- Lý do: dễ deploy trên free-tier, dễ bảo trì một mình, vẫn thể hiện được năng lực tổ chức kiến trúc cho portfolio mà không rơi vào bẫy "over-architecture".

## 2. Tech Stack theo từng tính năng

### 2.1. Auth & RBAC
- **Công nghệ:** ASP.NET Core Identity + OAuth handlers built-in (Google/GitHub), tự issue JWT + refresh token qua HttpOnly Cookie.
- **Không dùng:** Keycloak tự host — tránh tốn thêm hạ tầng/RAM không cần thiết ở quy mô solo + free-tier.
- **Mô hình phân quyền hai lớp (Giai đoạn 9):**
  - **Identity Role toàn cục** (`Admin` / `User`): gắn vào JWT claim. `User` là default cho mọi user đăng ký OAuth. `Admin` chỉ assign thủ công cho system administrator của nền tảng (platform-level: xem toàn bộ users, can thiệp workspace vi phạm...).
  - **Workspace Role** (`Admin` / `Manager` / `Member`): lưu trong `workspace_members.role`, kiểm tra tại service layer cho mọi nghiệp vụ trong workspace. Đây là lớp phân quyền thực sự — một user có thể là `Admin` ở workspace A và `Member` ở workspace B.
  - **Nguyên tắc:** mọi endpoint workspace dùng `.RequireAuthorization()` (user hợp lệ) + kiểm `workspace_members.role` trong service; chỉ endpoint platform-level dùng `SystemAdminPolicy` (Identity = Admin).
- **Lưu ý kỹ thuật:**
  - Cookie cần cấu hình `SameSite=Strict/Lax` + `Secure`.
  - Vì dùng cookie-based auth nên cần cơ chế chống CSRF riêng (anti-forgery token).
  - Refresh token nên rotate mỗi lần sử dụng (tránh replay).

### 2.2. Kanban Board Real-time
- **Công nghệ:** SignalR (native trong ASP.NET Core).
- **Lưu ý:**
  - Free-tier hosting (Render/Railway free) thường sleep sau vài phút không hoạt động → kết nối SignalR có thể bị rớt, cần xử lý auto-reconnect ở client.
  - Dùng SignalR Group theo `boardId` để chỉ broadcast đến đúng người đang xem board đó, tránh gửi thừa.
  - Cập nhật optimistic ở frontend, đồng bộ lại khi server xác nhận; xung đột chỉnh sửa đồng thời chấp nhận chiến lược last-write-wins ở giai đoạn này.

### 2.3. AI Smart Setup & AI Observer
- **Công nghệ:** DeepSeek API (schema tương thích OpenAI), gọi qua `HttpClient` riêng.
- **Lưu ý kiến trúc:** Bọc lời gọi AI qua interface `IAiProvider` để dễ đổi/thêm provider sau này mà không sửa logic nghiệp vụ.
- **Smart Setup:**
  - Ép output trả về đúng JSON schema cố định (dùng `response_format`/system prompt chặt), validate trước khi ghi DB.
  - Không tự động ghi thẳng vào DB — luôn qua bước xác nhận của con người (liên kết với Accountability Layer).
- **Observer:**
  - Chạy nền bằng `BackgroundService` (built-in .NET) — không cần Hangfire/Redis ở quy mô này.
  - Quét log định kỳ (ví dụ mỗi 15–30 phút), tóm tắt log trước khi gửi cho AI để giảm token/chi phí gọi API.
  - Kết quả cảnh báo gửi riêng cho Manager, không public toàn team.

### 2.4. Accountability Layer
- **Công nghệ:** Bảng quan hệ `AiActionLog` trong PostgreSQL (không cần event sourcing).
- **Cấu trúc log:** action, input/basis (tóm tắt prompt/dữ liệu đầu vào), timestamp, trạng thái (`Pending/Approved/Rejected/Undone`), actor liên quan.
- **Lưu ý:**
  - Mọi hành động ghi dữ liệu do AI khởi tạo phải đi qua một service duy nhất (`AiActionService`): log trước ở trạng thái `Pending`, chỉ áp dụng thật vào dữ liệu sau khi người dùng xác nhận.
  - Hỗ trợ Undo: lưu đủ thông tin để revert (soft-delete hoặc lưu diff trước/sau).

### 2.5. Xuất báo cáo (PDF/Excel)
- **PDF:** QuestPDF (license Community — miễn phí cho cá nhân/công ty nhỏ).
- **Excel:** ClosedXML (MIT license, hoàn toàn miễn phí — tránh EPPlus do đã đổi sang license thương mại).
- **Lưu ý:** Generate file theo yêu cầu (on-demand), không lưu trữ lâu dài trên server để tiết kiệm dung lượng free-tier.

### 2.6. Frontend Web
- **Công nghệ:** React + TypeScript + Vite, UI kit Ant Design
- **Kéo-thả Kanban:** `@dnd-kit/core`

### 2.7. Mobile (giai đoạn sau)
- **Công nghệ:** Flutter, gọi chung API với web.
- **Lưu ý:** Khi triển khai, dùng package `signalr_netcore` để kết nối SignalR từ Flutter; áp dụng lại kinh nghiệm cấu hình network (LAN config, cleartext HTTP cho môi trường dev) đã từng xử lý ở UniShare nếu cần test trên thiết bị thật.

### 2.9. Email Transactional (Giai đoạn 11)
- **Công nghệ:** [Resend](https://resend.com) API — free 3.000 email/tháng, không cần thẻ tín dụng, SDK .NET chính thức.
- **Dùng cho:** email mời member vào workspace (link token), gửi email nhanh từ Manager đến member(s).
- **Không dùng:** SendGrid (yêu cầu xác minh domain phức tạp hơn), SMTP tự cấu hình (không cần thiết ở quy mô này).
- **Lưu ý:** API key lưu trong User Secrets (local) và environment variable (production Render); không commit vào repo. Domain gửi email có thể dùng subdomain miễn phí của Resend cho giai đoạn đầu.

### 2.8. AI Agent Executor (Phase 7)
- **Gán task cho Agent:** thêm pseudo-member `member_type = 'ai_agent'` trong `workspace_members` — gán qua đúng dropdown/kéo-thả assignee hiện có, không xây UI riêng.
- **Vòng lặp thực thi:** mở rộng `IAiProvider`/`DeepSeekAiProvider` để hỗ trợ function-calling (tool-calling loop: plan → gọi tool → quan sát → lặp), khác với luồng gọi 1 lần của Smart Setup.
- **Bộ tool (whitelist, không cho tool tự do):**
  - `SearchSystemData` — truy vấn task/comment/board nội bộ.
  - `WebSearch` — qua **Tavily API** (free tier 1.000 credit/tháng, không cần thẻ, output đã tối ưu sẵn cho LLM agent nên không cần thêm bước scrape riêng).
  - `DraftOutput` — sinh nội dung kết quả (comment hoặc file tuỳ độ dài).
  - `RequestClarification` — chuyển task sang trạng thái chờ làm rõ kèm câu hỏi.
- **Trạng thái task mới:** thêm `Chờ làm rõ` vào enum trạng thái của module `Board`. Khi trưởng nhóm trả lời (comment), Agent **không** tự động chạy lại — cần thao tác thủ công nút "Chạy lại" để tránh việc mọi comment vô tình trigger AI.
- **Kết quả đầu ra & duyệt:** tái dùng Accountability Layer (mục 2.4) — thêm 2 applier mới `PostCommentApplier` (nội dung ngắn) và `PostAttachmentApplier` (nội dung dài dưới dạng file), đi qua đúng luồng Pending → Approve/Reject/Undo đã có, không xây accountability riêng cho Agent.
- **Theo dõi tiến trình thực thi:** bảng mới `agent_runs` (tách khỏi `ai_action_logs` vì mục đích khác — theo dõi tiến trình chạy, không phải log quyết định ghi dữ liệu): `task_id`, `status` (`Running/AwaitingClarification/AwaitingApproval/Completed/Failed`), `tool_call_trace` (jsonb), `tokens_used`, `started_at`/`finished_at`.
- **Guardrail bắt buộc (mặc định, cấu hình được qua `appsettings`):**
  - Tối đa **15 tool-call** mỗi lượt chạy.
  - Timeout **5 phút** wall-clock mỗi task.
  - Ngân sách **~50.000 token** mỗi lượt chạy.
  - Vượt ngưỡng → tự dừng, log `BudgetExceeded`, thông báo cho trưởng nhóm qua hệ thống `notifications` sẵn có — không lặp vô hạn.
- **Real-time:** broadcast thay đổi trạng thái `agent_runs` qua `BoardHub` (SignalR) đã có, để trưởng nhóm thấy card đổi trạng thái ngay khi Agent bắt đầu/dừng/hỏi lại.


## 3. Database & Hosting (free-tier)

| Thành phần | Lựa chọn | Lưu ý |
|---|---|---|
| Database | Neon hoặc Supabase (PostgreSQL serverless) | Free tier giới hạn storage/compute — theo dõi quota và chính sách sleep |
| API backend | Railway hoặc Render (free tier) | Cold start ảnh hưởng đến kết nối SignalR |
| Frontend | Vercel hoặc Netlify (free tier) | Không vấn đề đáng kể |
| File export tạm thời | Không lưu lâu dài, generate on-demand | Tránh phát sinh chi phí storage |

## 4. CI/CD & Testing

- **CI/CD:** GitHub Actions (miễn phí cho repository) — pipeline build/test → deploy.
- **Testing Backend:** xUnit.
- **Testing Frontend:** Vitest + React Testing Library.

## 5. Nguyên tắc chung khi phát triển

- Ưu tiên đơn giản, tránh "over-scope / over-architecture".
- Mọi tính năng AI đều phải đi qua Accountability Layer trước khi ảnh hưởng đến dữ liệu thật.
- Theo dõi chi phí gọi DeepSeek API — tóm tắt/nén dữ liệu đầu vào trước khi gửi để kiểm soát token.
- Ưu tiên giải pháp built-in của .NET (BackgroundService, SignalR, Identity) trước khi thêm hạ tầng phụ (Redis, message queue...) — chỉ thêm khi thực sự cần.