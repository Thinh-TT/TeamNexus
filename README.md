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
