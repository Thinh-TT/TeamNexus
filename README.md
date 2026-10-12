# TeamNexus

[![ci-backend](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml)
[![ci-web](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml)

Trợ lý điều phối không gian làm việc thông minh — nền tảng quản lý công việc & giao tiếp nhóm
kết hợp Kanban real-time với AI Agent (xem `project-docs/`).

## 🚀 Live Demo (Production)

- 🌐 **Web App (Frontend)**: [https://app.teamnexus.cloud](https://app.teamnexus.cloud) *(Host trên Vercel, quản lý DNS & SSL qua Cloudflare)*
- ⚙️ **API Backend**: [https://api.teamnexus.cloud](https://api.teamnexus.cloud) *(Host trên Render với Docker .NET 10; Base API: `/api`)*
- ✉️ **Transactional Email**: `TeamNexus <noreply@teamnexus.cloud>` *(Xác thực SPF, DKIM, DMARC qua Cloudflare DNS + Resend API)*
- 🗄️ **Database**: Neon Serverless PostgreSQL (Singapore `ap-southeast-1`, **11/11** EF Core migrations)

> 💡 **Lưu ý Cold-start**: Do chạy trên Free Tier của Render, khi không có request trong 15 phút máy chủ sẽ tạm ngủ. Request đầu tiên có thể mất ~30–60 giây để máy chủ thức dậy. Giao diện frontend đã tích hợp sẵn cơ chế auto-reconnect & thông báo tiếng Việt mượt mà.

## Trạng thái (Giai đoạn 14.5) — AI Agent Chat cấp Board

Một cửa AI duy nhất cho cả board: nút nổi góc phải-dưới mở **panel chat docked (~1/4 bề rộng, kéo được)**; **kéo thẻ từ cột vào panel để đính kèm** (tối đa 8 thẻ/lượt); câu trả lời **stream SSE** và **transcript được lưu ở DB** nên hội thoại sống qua reload.

Khi AI cho rằng nên thay đổi gì đó, nó **đề xuất** (khối JSON trong câu trả lời) và người dùng bấm **"Gửi đề xuất"** ⇒ `ai_action_logs` ở trạng thái **`Pending`** ⇒ **Manager duyệt** mới ghi dữ liệu thật (3 dạng: `CreateSubtasks`, `UpdateTaskFields`, `MoveTaskToColumn`). Bất biến của Accountability Layer **không đổi**: *không có dữ liệu nghiệp vụ nào bị ghi khi chưa `Approved`*.

Giai đoạn này cũng củng cố nền cho Giai đoạn 15/16: lịch sử AI **cấp workspace** (`GET /api/workspaces/{id}/ai-actions` — log tạo board trước đây **không hiện ở đâu**), seam **`IExternalAlertSink`** (Phase 15 cắm Slack/Discord/webhook mà **không** phải sửa Observer/Agent/member path), reaper dọn run của **task đã xoá**, và retention 90 ngày cho transcript.

Chi tiết: `project-docs/tasks/phase-14-5-foundation-and-agent-chat.md` · `project-docs/report/phase-14-5-foundation-and-agent-chat-test-report.md`.

## Trạng thái (Giai đoạn 14.6 — Track A) — Hoàn thiện trải nghiệm & bịt rò rỉ ID

Bốn ma sát phát hiện khi kiểm thử thực chiến trên bản live đã được xử lý, **không migration nào**:

- **Đính kèm thẻ không cần kéo-thả:** nút **"Đính kèm thẻ"** mở popover tìm nhanh theo tiêu đề/người phụ trách, và menu `...` của mỗi thẻ Kanban có **"Gửi vào AI Chat"** (tự mở panel + gắn thẻ). Kéo-thả vẫn giữ nguyên.
- **Không còn rò GUID:** cả hai system prompt đều cấm in raw ID, **và** có bộ lọc cưỡng chế hai đầu — `AiChatAnswerSanitizer` (lọc transcript trước khi ghi DB) + `redactIdentifiers` (lọc phần hiển thị trên client). Khối JSON đề xuất **không** bị lọc.
- **Transcript của thẻ đã bền:** tab *"Hỏi AI về thẻ này"* ghi/nạp `agent_chat_messages` qua `task_id` (`GET /api/tasks/{taskId}/agent-chat/messages`) ⇒ F5 không mất hội thoại. Dùng đúng cột `task_id` mà Migration 11 đã chừa sẵn.
- **Hỏi tiến độ bảng không còn trả lời rỗng:** khi chưa gắn thẻ nào, server tự thêm **Lightweight Board Digest** (số thẻ mỗi cột + top thẻ theo ưu tiên/hạn chót, trần 2.000 ký tự).

Track B (Multi-Agent Personas, Proactive Auto-Healing, ReAct tool-calling, Workspace RAG) **vẫn là kế hoạch**.
Chi tiết: `project-docs/tasks/phase-14-6-ai-agent-enhancements.md` · `project-docs/report/phase-14-6-track-a-test-report.md`.

## Kiến trúc

**Modular Monolith** ASP.NET Core (.NET 10) + React (TypeScript, Vite) + PostgreSQL (EF Core).

```
src/                        # Backend (.NET)
  TeamNexus.Api/            #   Entry point: Program.cs, DI, middleware, OpenAPI/Scalar
  TeamNexus.Persistence/    #   Single DbContext + entities + EF migrations (one migration chain)
  Modules/Auth/TeamNexus.Modules.Auth/   #   Module Auth (Identity, OAuth, JWT – Phase 1)
  Modules/Board/TeamNexus.Modules.Board/ #   Module Board (Kanban CRUD + SignalR – Phase 2)
  Modules/Ai/TeamNexus.Modules.Ai/       #   Module Ai (Smart Setup, Accountability, Observer, Agent Executor – Phase 3–5, 7)
  Modules/Reporting/TeamNexus.Modules.Reporting/  #   Module Reporting (PDF/Excel on-demand – Phase 6, xem tasks/phase-6-reporting-export.md)
  Shared/TeamNexus.Shared/  #   Contracts & helpers dùng chung
frontend/                   # Web (React + TS + Vite + Ant Design)
TeamNexus.sln
project-docs/          # Spec, tech decisions, roadmap, DB design, tasks
```

## Chạy ở local (dev)

Yêu cầu: .NET SDK 10, Node.js ≥ 22.12 (khuyến nghị 24 — đúng bản CI dùng; `vitest` yêu cầu `^22.12.0 || ^24.0.0 || >=26.0.0`).

```bash
# 1) Backend – http://localhost:5000  (Scalar UI: http://localhost:5000/scalar)
dotnet run --project src/TeamNexus.Api

# 2) Frontend – http://localhost:5173  (proxy /api → backend :5000)
cd frontend
npm install
npm run dev
```

### Migration (EF Core)

Một chuỗi migration duy nhất trên `TeamNexusDbContext` (xem `project-docs/04-database-design.md` §1.1). Hiện có **11** migration, mới nhất là `Phase14_5AgentChat` (Giai đoạn 14.5 — bảng `agent_chat_messages` lưu transcript của AI Agent Chat cấp board).

```bash
dotnet ef migrations list   --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
dotnet ef database update   --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
```

> ⚠️ **Trong môi trường sandbox/agent** (named pipe của MSBuild bị chặn), `dotnet build` mặc định **fail im lặng** với
> "0 Warning(s) / 0 Error(s)", và `dotnet ef` (tự build nội bộ) cũng fail theo. Khi đó dùng đúng quy trình sau — nếu bỏ qua,
> `migrations add --no-build` sẽ đọc **DLL cũ trong `bin`** và sinh migration **sai một cách im lặng**:
>
> ```bash
> dotnet build TeamNexus.sln -m:1 -nr:false    # bắt buộc: chạy 1 node, không tái dùng node
> dotnet ef migrations add <Tên> --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
> dotnet ef database update      --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
> ```
>
> Thứ tự bắt buộc: **sửa source → build 0/0 → mới `migrations add`**.

### Cấu hình & secrets
- Mọi secret local (OAuth Client ID/Secret, connection string, JWT key) đặt trong **User Secrets**:
  `dotnet user-secrets init --project src/TeamNexus.Api` rồi `dotnet user-secrets set "<Key>" "<Value>"`.
- Connection string PostgreSQL đọc từ `ConnectionStrings:DefaultConnection`, ví dụ:
  `dotnet user-secrets set --project src/TeamNexus.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=TeamNexus;Username=postgres;Password=..."`.
- Frontend override backend URL qua `frontend/.env` (xem `.env.example`).
- Mục `Cors:AllowedOrigins` cho phép gọi trực tiếp từ origin Vite (`http://localhost:5173`).
