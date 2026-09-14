# TeamNexus.Modules.Auth — Auth backend (Phase 1 §3)

OAuth (GitHub + Google) → upsert user (liên kết provider theo email) → JWT access +
refresh token qua **HttpOnly cookie** → policy-based RBAC.

## Endpoints

| Endpoint | Mô tả |
|---|---|
| `GET /api/auth/login/github` | Challenge GitHub OAuth (302 → github.com) |
| `GET /api/auth/login/google` | Challenge Google OAuth (302 → accounts.google.com, có PKCE) |
| `GET /api/auth/callback/github` | Callback URL đăng ký trong GitHub App — do OAuth handler xử lý (không phải endpoint của ta) |
| `GET /api/auth/callback/google` | Callback URL đăng ký trong Google Cloud Console — do OAuth handler xử lý |
| `GET /api/auth/external-login` | Bước nội bộ sau callback: upsert user + set JWT cookies → redirect frontend |
| `GET /api/auth/me` | `MemberOrAbove` → thông tin user + roles |
| `POST /api/auth/refresh` | CSRF + xoay refresh token, set cookie mới (401 nếu hết hạn/revoke/replay) |
| `POST /api/auth/logout` | CSRF + revoke refresh token, xóa cookies → 204 |
| `GET /api/auth/antiforgery` | Cấp anti-CSRF token (cookie `XSRF-TOKEN`) |
| `GET /api/admin/ping` | Chỉ Admin |
| `GET /api/manager/ping` | Admin hoặc Manager |

Anti-CSRF: mọi request mutating phải gửi header `X-XSRF-TOKEN` (giá trị đọc từ cookie
`XSRF-TOKEN` lấy ở `/api/auth/antiforgery`).

## Cấu hình (User Secrets của TeamNexus.Api)

```bash
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientId" "<id>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientSecret" "<secret>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientId" "<id>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientSecret" "<secret>"
dotnet user-secrets set --project src/TeamNexus.Api "Jwt:SigningKey" "<≥32 bytes>"
```

Callback URL cần đăng ký ở provider:
- **Môi trường Local (Dev):**
  - GitHub OAuth App → `http://localhost:5000/api/auth/callback/github`
  - Google Cloud Console → `http://localhost:5000/api/auth/callback/google`
    (+ Google Auth Platform: Audience `External`, Publishing status `Testing`, thêm test user)
- **Môi trường Production (`teamnexus.cloud`):**
  - **GitHub OAuth App:**
    - Homepage URL: `https://app.teamnexus.cloud`
    - Authorization callback URL: `https://api.teamnexus.cloud/api/auth/callback/github`
      *(Lưu ý thứ tự: `/callback/github`, không phải `/github/callback`)*
  - **Google Cloud Console (Web application):**
    - Authorized JavaScript origins: `https://app.teamnexus.cloud`, `https://teamnexus.cloud`
    - Authorized redirect URIs: `https://api.teamnexus.cloud/signin-google` và `https://api.teamnexus.cloud/api/auth/callback/google`

## Test nhanh (dev)

1. Chạy backend (`dotnet run --project src/TeamNexus.Api` — port 5000) + frontend (`npm run dev`).
2. Mở `http://localhost:5173` → bấm "Đăng nhập với Google" / "Đăng nhập với GitHub".
3. `GET /api/auth/me` với cookie → 200.
4. Gán role dev cho user đầu tiên (test RBAC):
   ```sql
   INSERT INTO user_roles (user_id, role_id)
   SELECT u.id, r.id FROM users u, roles r
   WHERE u.email = '<email>' AND r.name IN ('Admin','Manager');
   ```
   → `GET /api/admin/ping`, `/api/manager/ping` = 200.
   (Lưu ý: role nằm trong access token — cần đăng nhập/refresh lại để token mới có role.)
5. Xoay refresh: `POST /api/auth/refresh` kèm header `X-XSRF-TOKEN` → cookie mới;
   dùng lại cookie cũ → 401 và toàn bộ token user bị revoke.
