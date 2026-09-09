# TeamNexus.Modules.Auth — Auth backend (Phase 1 §3)

OAuth (GitHub trước, Google sau) → upsert user → JWT access + refresh token qua
**HttpOnly cookie** → policy-based RBAC.

## Endpoints

| Endpoint | Mô tả |
|---|---|
| `GET /api/auth/login/github` | Challenge GitHub OAuth (302 → github.com) |
| `GET /api/auth/callback/github` | Callback URL đăng ký trong GitHub App — do OAuth handler xử lý (không phải endpoint của ta) |
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
dotnet user-secrets set --project src/TeamNexus.Api "Jwt:SigningKey" "<≥32 bytes>"
# Google (bước sau): Authentication:Google:ClientId / ClientSecret
```

GitHub OAuth App cần **Authorization callback URL** =
`http://localhost:5000/api/auth/callback/github`.

## Test nhanh (dev)

1. Chạy backend (`dotnet run --project src/TeamNexus.Api` — port 5000).
2. Mở `http://localhost:5000/api/auth/login/github` → cấp quyền → tự redirect về
   `http://localhost:5173`. (Nhánh frontend chưa có UI login — chỉ cần kiểm tra cookie
   + gọi API.)
3. `GET /api/auth/me` với cookie → 200.
4. Gán role dev cho user đầu tiên (test RBAC):
   ```sql
   INSERT INTO user_roles (user_id, role_id)
   SELECT u.id, r.id FROM users u, roles r
   WHERE u.email = '<email-github>' AND r.name IN ('Admin','Manager');
   ```
   → `GET /api/admin/ping`, `/api/manager/ping` = 200.
5. Xoay refresh: `POST /api/auth/refresh` kèm header `X-XSRF-TOKEN` → cookie mới;
   dùng lại cookie cũ → 401 và toàn bộ token user bị revoke.
