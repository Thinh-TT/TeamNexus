# TeamNexus

Trợ lý điều phối không gian làm việc thông minh — nền tảng quản lý công việc & giao tiếp nhóm
kết hợp Kanban real-time với AI Agent (xem `Project-Documents/`).

## Kiến trúc

**Modular Monolith** ASP.NET Core (.NET 10) + React (TypeScript, Vite) + PostgreSQL (EF Core).

```
src/                        # Backend (.NET)
  TeamNexus.Api/            #   Entry point: Program.cs, DI, middleware, OpenAPI/Scalar
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
- Frontend override backend URL qua `frontend/.env` (xem `.env.example`).
- Mục `Cors:AllowedOrigins` cho phép gọi trực tiếp từ origin Vite (`http://localhost:5173`).

## Trạng thái (Giai đoạn 1 – Nền tảng & Auth)

- [x] §1 Khởi tạo Project — backend modular monolith + frontend Vite build/run được
- [ ] §2 Schema PostgreSQL (EF Core migration)
- [ ] §3 Auth Backend (OAuth Google/GitHub, JWT + refresh token HttpOnly cookie, RBAC)
- [ ] §4 Auth Frontend (login, protected routes, auto-refresh)
