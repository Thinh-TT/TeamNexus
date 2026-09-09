# Giai đoạn 1 – Nền tảng & Auth

> **Mục tiêu:** Khởi tạo toàn bộ hạ tầng dự án, thiết kế schema DB cơ bản và triển khai hệ thống xác thực/phân quyền hoàn chỉnh.
>
> **Công nghệ:** ASP.NET Core (Modular Monolith) · PostgreSQL (EF Core) · React + TypeScript + Vite · ASP.NET Core Identity · OAuth 2.0 (Google/GitHub) · JWT + HttpOnly Cookie

---

## 1. Khởi tạo Project

### 1.1 Backend – ASP.NET Core Modular Monolith
- [x] Tạo solution `TeamNexus.sln` với cấu trúc thư mục modular
  - [x] `src/TeamNexus.Api` – Entry point (ASP.NET Core Web API)
  - [x] `src/Modules/Auth/TeamNexus.Modules.Auth` – Module Auth
  - [x] `src/Shared/TeamNexus.Shared` – Contracts, helpers dùng chung
- [x] Cấu hình `Program.cs`: đăng ký DI các module, middleware pipeline
- [x] Thêm package cần thiết: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `AspNet.Security.OAuth.GitHub`
- [x] Cấu hình `appsettings.json` / `appsettings.Development.json`: connection string, JWT settings, OAuth client ID/secret (dùng User Secrets cho local dev)
- [x] Verify: `dotnet build` và `dotnet run` không lỗi, swagger/scalar UI mở được

### 1.2 Frontend – React + TypeScript + Vite
- [x] Khởi tạo project: `npm create vite@latest` với template `react-ts`
- [x] Cài dependencies: `antd`, `axios`, `react-router-dom`, `zustand` (hoặc `jotai`), `@tanstack/react-query`
- [x] Thiết lập cấu trúc thư mục:
  - `src/features/auth/` – các component, hook, service liên quan auth
  - `src/shared/` – UI components dùng chung, utils
  - `src/app/` – router, providers, global state
- [x] Cấu hình Vite proxy: forward `/api/*` về backend `localhost:PORT` để tránh CORS trong dev
- [x] Cấu hình Axios instance base với `withCredentials: true` (bắt buộc cho cookie-based auth)
- [x] Verify: `npm run dev` không lỗi, trang chủ hiển thị được

---

## 2. Database Schema

### 2.1 Thiết kế & Migration (EF Core Code-First) — hiện thực tại project `src/TeamNexus.Persistence`
- [x] Định nghĩa entity `ApplicationUser` (kế thừa `IdentityUser<Guid>`):
  - Thêm fields: `DisplayName`, `AvatarUrl`, `CreatedAt`
- [x] Định nghĩa entity `Workspace`:
  - Fields: `Id (Guid)`, `Name`, `Description`, `CreatedAt`, `OwnerId (FK → ApplicationUser)`
- [x] Định nghĩa entity `WorkspaceMember` (bảng junction):
  - Fields: `WorkspaceId`, `UserId`, `Role (enum: Admin/Manager/Member)`, `JoinedAt`
- [x] Định nghĩa entity `Board`:
  - Fields: `Id (Guid)`, `WorkspaceId (FK)`, `Name`, `Description`, `CreatedAt`
- [x] Định nghĩa entity `RefreshToken`:
  - Fields: `Id (Guid)`, `UserId (FK)`, `TokenHash (SHA-256, không lưu token gốc)`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByTokenId (self-FK)` — theo phương án `04-database-design.md` §3.2
- [x] Tạo `TeamNexusDbContext` kế thừa `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>` (đổi tên từ `AuthDbContext` theo `04-database-design.md` §1.1 — một DbContext hợp nhất, một chuỗi migration)
- [x] Đăng ký DbContext, cấu hình Fluent API (index, constraints)
- [x] Tạo migration initial: `dotnet ef migrations add InitialSchema`
- [x] Chạy migrate lên DB Neon/Supabase (hoặc PostgreSQL local): `dotnet ef database update` — đã chạy trên PostgreSQL local (DB `TeamNexus`); Neon/Supabase để dành bước deploy
- [x] Verify: kết nối DB thành công, các bảng sinh ra đúng cấu trúc

---

## 3. Auth Backend

### 3.1 OAuth 2.0 – Google & GitHub
- [ ] Đăng ký OAuth App trên Google Cloud Console (Client ID + Secret) — *thêm sau*
- [x] Đăng ký OAuth App trên GitHub Developer Settings (Client ID + Secret)
- [x] Cấu hình handler (đặt trong `AddAuthModule` của `TeamNexus.Modules.Auth`, không nằm trực tiếp ở `Program.cs`):
  ```csharp
  .AddGitHub(...)  // package: AspNet.Security.OAuth.GitHub (Google thêm sau: .AddGoogle(...))
  ```
- [x] Upsert `ApplicationUser` từ claims OAuth (email, name, avatar) — hiện thực trong `AuthService.HandleExternalLoginAsync` (flow external-cookie, tương đương `OnCreatingTicket`)
- [x] Endpoint `GET /api/auth/login/{provider}` → redirect challenge OAuth (`github` hoạt động; `google` → 400 "chưa cấu hình")
- [x] Endpoint callback OAuth → issue JWT + refresh token: `/api/auth/callback/github` do OAuth handler xử lý handshake → `/api/auth/external-login` upsert user + cấp token
- [x] Verify: đăng nhập qua **GitHub** thành công, user được tạo/cập nhật trong DB (`users` + `user_logins` + role `Member`)
- [ ] Verify: đăng nhập qua **Google** — chưa kích hoạt (thêm sau)

### 3.2 JWT + Refresh Token (HttpOnly Cookie)
- [x] Viết `JwtService` (`src/Modules/Auth/.../Services/JwtService.cs`):
  - `GenerateAccessToken(user, roles)` → JWT (expire: 15 phút)
  - `GenerateRefreshToken()` → random string (expire: 7 ngày)
- [x] Khi issue token: set 2 HttpOnly Cookie (qua `TokenCookieService`):
  - `access_token`: Secure (khi HTTPS), SameSite=Lax, HttpOnly, expire 15p
  - `refresh_token`: Secure (khi HTTPS), SameSite=Lax, HttpOnly, expire 7 ngày
- [x] Refresh token lưu DB (bảng `RefreshToken`) ở dạng **hashed** (SHA-256)
- [x] Endpoint `POST /api/auth/refresh`:
  - Đọc `refresh_token` cookie → validate hash với DB → rotate (revoke cũ, issue cặp token mới, ghi `replaced_by_token_id`)
  - Trả lỗi `401` nếu token hết hạn, đã bị revoke, hoặc bị **dùng lại (replay → revoke cả chuỗi)**
- [x] Endpoint `POST /api/auth/logout`:
  - Revoke refresh token trong DB
  - Xóa cả 2 cookie
- [x] Cấu hình anti-CSRF: `AddAntiforgery` (ASP.NET Core) + header `X-XSRF-TOKEN`; endpoint `/api/auth/antiforgery` cấp token (cookie `XSRF-TOKEN`)
- [x] Verify: refresh → xoay token, cookie mới được set, token cũ revoked (replay bị 401 + revoke chuỗi); logout → cookie bị xóa, token cũ bị reject (verify bằng API test trên user tạm)

### 3.3 Policy-based Authorization (RBAC)
- [x] Định nghĩa 3 claim/role: `Admin`, `Manager`, `Member` (seed qua migration `InitialSchema`)
- [x] Đăng ký Authorization Policies trong DI:
  - `"AdminOnly"` → RequireClaim role = Admin
  - `"ManagerOrAbove"` → RequireClaim role = Admin | Manager
  - `"MemberOrAbove"` → RequireClaim role = Admin | Manager | Member
- [x] Tạo endpoint mẫu để verify từng policy:
  - `GET /api/auth/me` → trả thông tin user đang đăng nhập (yêu cầu `MemberOrAbove`)
  - `GET /api/admin/ping` → chỉ Admin (yêu cầu `"AdminOnly"`)
  - `GET /api/manager/ping` → Manager trở lên (yêu cầu `"ManagerOrAbove"`)
- [x] Verify: gọi endpoint đúng role → 200; sai role → 403 (test qua trình duyệt với GitHub login + gán/thu hồi role bằng SQL)

---

## 4. Auth Frontend

### 4.1 Trang & Luồng Đăng nhập
- [ ] Trang `/login` – UI đăng nhập (Ant Design):
  - Nút "Đăng nhập với Google" (redirect đến `/api/auth/login/google`)
  - Nút "Đăng nhập với GitHub" (redirect đến `/api/auth/login/github`)
- [ ] Sau khi OAuth callback thành công, backend redirect về `/` hoặc `/dashboard`
- [ ] Gọi `GET /api/auth/me` để lấy thông tin user → lưu vào global state (zustand/jotai)

### 4.2 Auth State & Protected Routes
- [ ] Hook `useAuth()`: expose `user`, `isLoading`, `isAuthenticated`, `logout()`
- [ ] `ProtectedRoute` component: redirect về `/login` nếu chưa đăng nhập
- [ ] `axios` interceptor:
  - `401` response → tự động gọi `POST /api/auth/refresh`
  - Nếu refresh thành công → retry request gốc
  - Nếu refresh thất bại (401) → logout, redirect `/login`
- [ ] Xử lý CSRF: đọc cookie `XSRF-TOKEN` → gắn vào header `X-XSRF-TOKEN` cho mỗi mutating request (POST/PUT/DELETE/PATCH)
- [ ] Trang `/dashboard` (placeholder, yêu cầu đăng nhập): hiển thị thông tin user, nút logout
- [ ] Verify: truy cập `/dashboard` khi chưa login → redirect `/login`; sau khi login → vào được dashboard, hiển thị đúng tên/avatar

---

## 5. Checklist Hoàn thiện Giai đoạn 1

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 2.

- [x] Project backend (Modular Monolith) và frontend (React/Vite) khởi tạo xong, `build` và `run` không lỗi
- [x] Schema PostgreSQL (`User`, `Role`, `Workspace`, `Board`, `RefreshToken`) đã migrate thành công
- [ ] Đăng nhập được qua **Google OAuth** (user được tạo/cập nhật trong DB) — *thêm sau*
- [x] Đăng nhập được qua **GitHub OAuth** (user được tạo/cập nhật trong DB)
- [x] JWT access token + refresh token cấp qua HttpOnly Cookie, hoạt động đúng
- [x] Rotate refresh token mỗi lần dùng (token cũ bị revoke ngay sau khi refresh)
- [x] Cơ chế CSRF (anti-forgery token) hoạt động
- [x] Phân quyền Policy-based cho 3 role áp dụng được trên ít nhất 1 endpoint mẫu mỗi loại
- [ ] Frontend tự động refresh token khi nhận 401, redirect login khi refresh thất bại — *thuộc §4*
