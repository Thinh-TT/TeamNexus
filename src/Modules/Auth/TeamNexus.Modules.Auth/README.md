# TeamNexus.Modules.Auth — Phân hệ Xác thực & Phân quyền Toàn cục

Phân hệ chịu trách nhiệm xác thực OAuth 2.0 (**GitHub** & **Google**), đồng bộ tài khoản người dùng theo email, cấp phát và xoay vòng cặp **JWT Access Token + Refresh Token** qua **HttpOnly Cookie**, bảo vệ chống tấn công CSRF và thực thi lớp phân quyền toàn cục (`SystemAdminPolicy`).

---

## 1. Điểm cuối HTTP (Endpoints)

| Phương thức & Đường dẫn | Phân quyền | Mô tả chức năng |
|---|---|---|
| `GET /api/auth/login/github` | `AllowAnonymous` | Chuyển hướng (`302`) sang màn hình ủy quyền GitHub OAuth 2.0 |
| `GET /api/auth/login/google` | `AllowAnonymous` | Chuyển hướng (`302`) sang màn hình ủy quyền Google OAuth 2.0 (kèm PKCE) |
| `GET /api/auth/callback/github` | OAuth Middleware | Đường dẫn callback đăng ký trên GitHub OAuth App (do middleware xử lý) |
| `GET /api/auth/callback/google` | OAuth Middleware | Đường dẫn callback đăng ký trên Google Cloud Console (do middleware xử lý) |
| `GET /api/auth/external-login` | `AllowAnonymous` | Tiêu thụ cookie tạm từ OAuth, đồng bộ người dùng (mặc định gán vai trò `User`), phát hành cặp cookie JWT và chuyển hướng về ứng dụng Web |
| `GET /api/auth/me` | `RequireAuthorization` | Trả về thông tin định danh của người dùng hiện tại (`id`, `email`, `displayName`, `avatarUrl`, `roles`) |
| `POST /api/auth/refresh` | CSRF + Cookie | Xoay vòng Refresh Token (`token_hash` SHA-256). Nếu phát hiện tái sử dụng token đã thu hồi (replay), hệ thống trả `401` và thu hồi toàn bộ chuỗi phiên của người dùng |
| `POST /api/auth/logout` | CSRF + Cookie | Thu hồi Refresh Token hiện tại và xóa toàn bộ cookie xác thực (`204 No Content`) |
| `GET /api/auth/antiforgery` | `AllowAnonymous` | Cấp phát mã chống giả mạo yêu cầu chéo trang (đồng bộ vào cookie `XSRF-TOKEN` và header `X-XSRF-TOKEN`) |
| `GET /api/admin/ping` | `SystemAdminPolicy` | Điểm cuối kiểm tra quyền Quản trị viên Hệ thống (yêu cầu Identity Role = `Admin`) |

---

## 2. Các Quy tắc Bảo mật & Phân quyền Bất biến

1. **Mô hình Phân quyền 2 Lớp (Two-Layer RBAC):**
   - **Lớp 1 — Vai trò Toàn cục (`roles.name` = `Admin` | `User`):** Mọi người dùng đăng nhập qua OAuth mặc định nhận vai trò `User`. Chính sách `SystemAdminPolicy` chỉ dành cho quản trị cấp nền tảng (`Admin`).
   - **Lớp 2 — Vai trò theo Workspace (`workspace_members.role` = `Admin` | `Manager` | `Member`):** Quyết định toàn bộ quyền hạn nghiệp vụ trên Workspace, Bảng và Thẻ công việc (do phân hệ `Board` và `Ai` kiểm tra qua `IWorkspaceAccess`).
2. **Chặn Đăng nhập Tài khoản Hệ thống (`IsSystem = true`):**
   - Các tài khoản hệ thống nội bộ (như `GitHub Bot` — `00000000-0000-0000-0000-00000000b071` hoặc các thành viên ảo `ai_agent`) có cờ `users.is_system = true`.
   - `AuthService` từ chối mọi yêu cầu đăng nhập OAuth (`HandleExternalLoginAsync`), phát hành JWT (`IssueTokenPairAsync`) hoặc làm mới token (`RotateRefreshTokenAsync`) đối với tài khoản có `IsSystem = true`.
3. **Bảo vệ Chống CSRF:**
   - Mọi điểm cuối thay đổi trạng thái xác thực bằng cookie (`POST`, `PUT`, `PATCH`, `DELETE`) bắt buộc gắn `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` và yêu cầu trình duyệt gửi kèm header `X-XSRF-TOKEN`.

---

## 3. Cấu hình (User Secrets / Biến môi trường)

```bash
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientId" "<client-id>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientSecret" "<client-secret>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientId" "<client-id>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientSecret" "<client-secret>"
dotnet user-secrets set --project src/TeamNexus.Api "Jwt:SigningKey" "<chuỗi-bí-mật-tối-thiểu-32-bytes>"
```

### Đường dẫn Callback cần đăng ký tại Nhà cung cấp OAuth:
- **Môi trường Local (`localhost:5000`):**
  - GitHub OAuth App: `http://localhost:5000/api/auth/callback/github`
  - Google Cloud Console: `http://localhost:5000/api/auth/callback/google`
- **Môi trường Production (`teamnexus.cloud`):**
  - GitHub OAuth App: `https://api.teamnexus.cloud/api/auth/callback/github`
  - Google Cloud Console: `https://api.teamnexus.cloud/api/auth/callback/google`
