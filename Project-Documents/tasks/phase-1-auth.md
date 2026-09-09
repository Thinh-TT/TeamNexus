# Giai đoạn 1 – Nền tảng & Auth

> **Mục tiêu:** Khởi tạo toàn bộ hạ tầng dự án, thiết kế schema DB cơ bản và triển khai hệ thống xác thực/phân quyền hoàn chỉnh.
>
> **Công nghệ:** ASP.NET Core (Modular Monolith) · PostgreSQL (EF Core) · React + TypeScript + Vite · ASP.NET Core Identity · OAuth 2.0 (Google/GitHub) · JWT + HttpOnly Cookie

---

## 1. Khởi tạo Project

### 1.1 Backend – ASP.NET Core Modular Monolith
- [ ] Tạo solution `TeamNexus.sln` với cấu trúc thư mục modular
  - [ ] `src/TeamNexus.Api` – Entry point (ASP.NET Core Web API)
  - [ ] `src/Modules/Auth/TeamNexus.Modules.Auth` – Module Auth
  - [ ] `src/Shared/TeamNexus.Shared` – Contracts, helpers dùng chung
- [ ] Cấu hình `Program.cs`: đăng ký DI các module, middleware pipeline
- [ ] Thêm package cần thiết: `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, `Npgsql.EntityFrameworkCore.PostgreSQL`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `AspNet.Security.OAuth.GitHub`
- [ ] Cấu hình `appsettings.json` / `appsettings.Development.json`: connection string, JWT settings, OAuth client ID/secret (dùng User Secrets cho local dev)
- [ ] Verify: `dotnet build` và `dotnet run` không lỗi, swagger/scalar UI mở được

### 1.2 Frontend – React + TypeScript + Vite
- [ ] Khởi tạo project: `npm create vite@latest` với template `react-ts`
- [ ] Cài dependencies: `antd`, `axios`, `react-router-dom`, `zustand` (hoặc `jotai`), `@tanstack/react-query`
- [ ] Thiết lập cấu trúc thư mục:
  - `src/features/auth/` – các component, hook, service liên quan auth
  - `src/shared/` – UI components dùng chung, utils
  - `src/app/` – router, providers, global state
- [ ] Cấu hình Vite proxy: forward `/api/*` về backend `localhost:PORT` để tránh CORS trong dev
- [ ] Cấu hình Axios instance base với `withCredentials: true` (bắt buộc cho cookie-based auth)
- [ ] Verify: `npm run dev` không lỗi, trang chủ hiển thị được

---

## 2. Database Schema

### 2.1 Thiết kế & Migration (EF Core Code-First)
- [ ] Định nghĩa entity `ApplicationUser` (kế thừa `IdentityUser<Guid>`):
  - Thêm fields: `DisplayName`, `AvatarUrl`, `CreatedAt`
- [ ] Định nghĩa entity `Workspace`:
  - Fields: `Id (Guid)`, `Name`, `Description`, `CreatedAt`, `OwnerId (FK → ApplicationUser)`
- [ ] Định nghĩa entity `WorkspaceMember` (bảng junction):
  - Fields: `WorkspaceId`, `UserId`, `Role (enum: Admin/Manager/Member)`, `JoinedAt`
- [ ] Định nghĩa entity `Board`:
  - Fields: `Id (Guid)`, `WorkspaceId (FK)`, `Name`, `Description`, `CreatedAt`
- [ ] Định nghĩa entity `RefreshToken`:
  - Fields: `Id (Guid)`, `UserId (FK)`, `Token (string, hashed)`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByToken`
- [ ] Tạo `AuthDbContext` kế thừa `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`
- [ ] Đăng ký DbContext, cấu hình Fluent API (index, constraints)
- [ ] Tạo migration initial: `dotnet ef migrations add InitialSchema`
- [ ] Chạy migrate lên DB Neon/Supabase (hoặc PostgreSQL local): `dotnet ef database update`
- [ ] Verify: kết nối DB thành công, các bảng sinh ra đúng cấu trúc

---

## 3. Auth Backend

### 3.1 OAuth 2.0 – Google & GitHub
- [ ] Đăng ký OAuth App trên Google Cloud Console (Client ID + Secret)
- [ ] Đăng ký OAuth App trên GitHub Developer Settings (Client ID + Secret)
- [ ] Cấu hình handler trong `Program.cs`:
  ```csharp
  .AddGoogle(...)
  .AddGitHub(...)  // package: AspNet.Security.OAuth.GitHub
  ```
- [ ] Cài handler `OnCreatingTicket`: upsert `ApplicationUser` từ claims OAuth (email, name, avatar)
- [ ] Endpoint `GET /api/auth/login/{provider}` → redirect challenge OAuth
- [ ] Endpoint `GET /api/auth/callback/{provider}` → nhận callback, issue JWT + refresh token
- [ ] Verify: đăng nhập qua Google và GitHub thành công, user được tạo trong DB

### 3.2 JWT + Refresh Token (HttpOnly Cookie)
- [ ] Viết `JwtService`:
  - `GenerateAccessToken(user, roles)` → JWT (expire: 15 phút)
  - `GenerateRefreshToken()` → random string (expire: 7 ngày)
- [ ] Khi issue token: set 2 HttpOnly Cookie:
  - `access_token`: Secure, SameSite=Lax, HttpOnly, expire 15p
  - `refresh_token`: Secure, SameSite=Lax, HttpOnly, expire 7 ngày
- [ ] Refresh token lưu DB (bảng `RefreshToken`) ở dạng **hashed** (SHA-256)
- [ ] Endpoint `POST /api/auth/refresh`:
  - Đọc `refresh_token` cookie → validate hash với DB → rotate (revoke cũ, issue cặp token mới)
  - Trả lỗi `401` nếu token hết hạn hoặc đã bị revoke
- [ ] Endpoint `POST /api/auth/logout`:
  - Revoke refresh token trong DB
  - Xóa cả 2 cookie
- [ ] Cấu hình anti-CSRF: dùng `AntiForgery` middleware của ASP.NET Core, gắn token vào request header `X-XSRF-TOKEN`
- [ ] Verify: access token expire → refresh tự động → cookie mới được set; logout → cookie bị xóa, token cũ bị reject

### 3.3 Policy-based Authorization (RBAC)
- [ ] Định nghĩa 3 claim/role: `Admin`, `Manager`, `Member`
- [ ] Đăng ký Authorization Policies trong DI:
  - `"AdminOnly"` → RequireClaim role = Admin
  - `"ManagerOrAbove"` → RequireClaim role = Admin | Manager
  - `"MemberOrAbove"` → RequireClaim role = Admin | Manager | Member
- [ ] Tạo endpoint mẫu để verify từng policy:
  - `GET /api/auth/me` → trả thông tin user đang đăng nhập (yêu cầu `MemberOrAbove`)
  - `GET /api/admin/ping` → chỉ Admin (yêu cầu `"AdminOnly"`)
  - `GET /api/manager/ping` → Manager trở lên (yêu cầu `"ManagerOrAbove"`)
- [ ] Verify: gọi endpoint đúng role → 200; sai role → 403

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

- [ ] Project backend (Modular Monolith) và frontend (React/Vite) khởi tạo xong, `build` và `run` không lỗi
- [ ] Schema PostgreSQL (`User`, `Role`, `Workspace`, `Board`, `RefreshToken`) đã migrate thành công
- [ ] Đăng nhập được qua **Google OAuth** (user được tạo/cập nhật trong DB)
- [ ] Đăng nhập được qua **GitHub OAuth** (user được tạo/cập nhật trong DB)
- [ ] JWT access token + refresh token cấp qua HttpOnly Cookie, hoạt động đúng
- [ ] Rotate refresh token mỗi lần dùng (token cũ bị revoke ngay sau khi refresh)
- [ ] Cơ chế CSRF (anti-forgery token) hoạt động
- [ ] Phân quyền Policy-based cho 3 role áp dụng được trên ít nhất 1 endpoint mẫu mỗi loại
- [ ] Frontend tự động refresh token khi nhận 401, redirect login khi refresh thất bại
