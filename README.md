# TeamNexus

Trợ lý điều phối không gian làm việc thông minh — nền tảng quản lý công việc & giao tiếp nhóm
kết hợp Kanban real-time với AI Agent (xem `Project-Documents/`).

## Kiến trúc

**Modular Monolith** ASP.NET Core (.NET 10) + React (TypeScript, Vite) + PostgreSQL (EF Core).

```
src/                        # Backend (.NET)
  TeamNexus.Api/            #   Entry point: Program.cs, DI, middleware, OpenAPI/Scalar
  TeamNexus.Persistence/    #   Single DbContext + entities + EF migrations (one migration chain)
  Modules/Auth/TeamNexus.Modules.Auth/   #   Module Auth (Identity, OAuth, JWT – Phase 1)
  Shared/TeamNexus.Shared/  #   Contracts & helpers dùng chung
frontend/                   # Web (React + TS + Vite + Ant Design)
TeamNexus.sln
Project-Documents/          # Spec, tech decisions, roadmap, DB design, tasks
```

## Chạy ở local (dev)

Yêu cầu: .NET SDK 10, Node.js ≥ 22.

```bash
# 1) Backend – http://localhost:5000  (Scalar UI: http://localhost:5000/scalar)
dotnet run --project src/TeamNexus.Api

# 2) Frontend – http://localhost:5173  (proxy /api → backend :5000)
cd frontend
npm install
npm run dev
```

### Cấu hình & secrets

- Mọi secret local (OAuth Client ID/Secret, connection string, JWT key) đặt trong **User Secrets**:
  `dotnet user-secrets init --project src/TeamNexus.Api` rồi `dotnet user-secrets set "<Key>" "<Value>"`.
- Connection string PostgreSQL đọc từ `ConnectionStrings:DefaultConnection`, ví dụ:
  `dotnet user-secrets set --project src/TeamNexus.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=TeamNexus;Username=postgres;Password=..."`.
- Frontend override backend URL qua `frontend/.env` (xem `.env.example`).
- Mục `Cors:AllowedOrigins` cho phép gọi trực tiếp từ origin Vite (`http://localhost:5173`).

## Trạng thái (Giai đoạn 1 – Nền tảng & Auth)

- [x] §1 Khởi tạo Project — backend modular monolith + frontend Vite build/run được
- [x] §2 Schema PostgreSQL (EF Core migration) — `TeamNexusDbContext` tại `src/TeamNexus.Persistence`, migration `InitialSchema` đã áp dụng lên DB `TeamNexus` local
- [x] §3 Auth Backend — GitHub OAuth ✅ (Google chờ credentials), JWT + refresh HttpOnly cookie + rotate, CSRF, RBAC policies + endpoint mẫu
- [x] §4 Auth Frontend (login, protected routes, auto-refresh)

## Trạng thái (Giai đoạn 2 – Kanban Core)

- [x] §1 Schema Kanban (EF migration `Phase2KanbanSchema`) — thêm `board_columns`, `tasks`, `labels`, `task_labels`, `task_comments` (đã migrate lên DB local)
- [x] §2 Backend Module Board — CRUD Board/Column/Task + Label/Comment (Minimal API, quyền theo workspace_members.role), migration `Phase2BoardColumnIsDone` (cột `is_done`) — verify 40 check API
- [x] §3 SignalR Real-time — BoardHub tại `/hubs/board`, JoinBoard/LeaveBoard theo group `board-{id}` (verify membership), broadcast Task/Column/Comment events qua `IBoardEventPublisher` — verify 14 check WebSocket (2 client cùng board + group isolation)
- [x] §4 Frontend Kanban UI (drag & drop, useBoardHub, modals, filters)
- [x] §5 Kiểm thử & hoàn thiện (45 automated tests Vitest + Testing Library, build & lint 100% PASS, live browser testing)

## Trạng thái (Giai đoạn 3 – AI Smart Setup)

- [x] §1–§4 Backend Module Ai — `IAiProvider`, `DeepSeekAiProvider` (OpenAI-compatible) & `FakeAiProvider`, `POST /api/boards/{boardId}/smart-setup`, `GET /api/workspaces/{workspaceId}/members`, chuẩn hoá & resolve assignee/label, không ghi DB
- [x] §5 Frontend UI Smart Setup — `src/features/ai/` (`smartSetupApi`, `useSmartSetup`, `SmartSetupModal`, `ProposedTaskItem`), tích hợp nút "AI Smart Setup" vào header Kanban Board
- [x] §6 Kiểm thử & hoàn thiện (64 automated tests Vitest + Testing Library, 0 lint warnings/errors, TypeScript & Vite build 100% PASS)

## Trạng thái (Giai đoạn 4 – Accountability Layer)

- [x] §1 Schema Accountability (EF migration `Phase4AccountabilityLayer`) — thêm `ai_action_logs` (`jsonb`, CHECK status, audit fields, FK RESTRICT, indexes)
- [x] §2–§3 Backend Module Ai — Cổng trung gian `AiActionService`, applier registry (`CreateSubtasksApplier`), 6 endpoints (`confirm`, `ai-actions` list, detail, `approve`, `reject`, `undo`), CAS chống apply trùng, transaction rollback an toàn
- [x] §4 Frontend UI Accountability — `src/features/ai/` (`aiActionApi`, `useAiActions`, `SmartSetupModal` với luồng Pending/Approve/Reject/Undo, `AiActionHistoryDrawer`, `AiActionLogItem`), nút "Lịch sử AI" + badge pending tại header Kanban Board
- [x] §5 Kiểm thử & hoàn thiện — 54 backend checks PASS (harness API thật + DB PostgreSQL thật + SignalR client thật), 88 frontend automated tests Vitest + Testing Library PASS, 0 lint warning/error, build .NET & Vite 100% PASS

## Trạng thái (Giai đoạn 5 – AI Observer) — Backend hoàn tất, frontend đã bàn giao

- [x] §1 Schema Observer (EF migration `Phase5AiObserverSchema`) — thêm `activity_logs` (event store append-only), `notifications` (1 row/người nhận), `ai_observer_runs` (audit + token); `jsonb`, CHECK `status`, FK RESTRICT, index `(workspace_id, created_at)` / `(recipient_user_id, is_read)`
- [x] §2–§4 Backend Module Ai — thu thập `activity_logs` từ Board (`IActivityLogWriter`), 4 tín hiệu (`OverdueTask`/`StalledTask`/`Overload`/`Bottleneck`) tính bằng hàm thuần, prompt tóm tắt trước khi gọi AI (0 tín hiệu ⇒ **không gọi AI**), validator chống hallucination, notification chỉ cho Manager/Admin + dedupe 24h, `ObserverBackgroundService` (PeriodicTimer + advisory lock) và prune retention
- [x] §5 Backend – DTO/Endpoints/DI — 6 endpoint (`/api/notifications` ×3, `/api/workspaces/{id}/observer/scan|runs`, `/api/observer/runs/{id}`) với quyền Manager/Admin, CSRF, mã lỗi `{ error }`; `Program.cs` không đổi
- [x] §7.1 Verify backend — **178 check PASS** (API thật + PostgreSQL thật + `FakeAiProvider`, không tốn token): schema 28, detector/validator thuần 44+15, background service 3, scan end-to-end 6, chi phí 3, HTTP API 32, activity log 31, dedupe/concurrency/failure 2+3+4
- [ ] §6 + §7.2 Frontend — `src/features/ai/` (`notificationApi`, `observerApi`, `useNotifications`, `useObserverRuns`, `NotificationDrawer`, `NotificationItem`, `ObserverRunsDrawer`, `ObserverFindingCard`) + nút "Cảnh báo AI" & badge ở `BoardView` — **bàn giao antigravity**, contract chốt tại `Project-Documents/tasks/phase-5-ai-observer.md` mục "🔻 BÀN GIAO"



