# Bàn giao phần còn lại — Giai đoạn 11 (Quản lý Member & Profile)

> **Người nhận:** Antigravity (agent/đội thực thi frontend).
> **Người giao:** phiên làm **backend §1–§6** (đã xong; xem bảng trạng thái dưới).
>
> **Tham chiếu bắt buộc — đọc trước khi code:**
> - `Project-Documents/tasks/phase-11-member-profile-management.md` — **kế hoạch gốc**: bảng quyết định **D1–D14**, phát sinh **P1–P6**, ca biên, bảng bằng chứng. Đây là hợp đồng cứng.
> - `Project-Documents/03-roadmap.md` → *Giai đoạn 11* (ô A–E) · `01-system-specification.md` §10 · `02-tech-stack-decisions.md` §2.9.
> - `Project-Documents/tasks/phase-10-remaining-frontend-handover.md` — note bàn giao mẫu trước đó (cùng văn phong, cùng mức chi tiết).
>
> **Phạm vi note này:** **§7 (toàn bộ frontend)** + **§8 (CI & tài liệu)**.
> **Backend §1–§6 ĐÃ XONG** — **không** làm lại, **không** sửa file backend (xem §7).

---

## 1. Trạng thái bàn giao

| Mục | Trạng thái |
|---|---|
| **§1 Persistence & migration** | ✅ **XONG** — 2 bảng mới (`workspace_invitations`, `email_messages`), migration `Phase11MemberProfile` (**9** migration), `has-pending-model-changes` sạch |
| **§2 Email gateway (Resend)** | ✅ **XONG** — `EmailTemplateTests` **14/14 PASS** |
| **§3 Mời member qua email** | ✅ **XONG** — `InvitationApiTests` (21 test method) |
| **§4 Gửi email nhanh + Quản lý member** | ✅ **XONG** — `QuickEmailApiTests` (13) + `WorkspaceMemberApiTests` (18) |
| **§5 Profile cá nhân** | ✅ **XONG** — `ProfileApiTests` (12) + `AvatarUrlTests` (10) |
| **§6 Notification Center + trigger** | ✅ **XONG** — `NotificationTriggerApiTests` (9) |
| **§7 Frontend** | ⬜ **Chờ bạn** |
| **§8 CI & tài liệu** | ⬜ **Chờ bạn** |

### ⛔ Ba việc **bắt buộc** làm TRƯỚC khi viết dòng code frontend đầu tiên

1. **Chạy `dotnet test` với PostgreSQL thật và xác nhận `Skipped: 0`.**
   Máy dev của phiên backend **không có PostgreSQL** (Docker daemon cũng không chạy), nên **237 test phụ thuộc DB đang bị SKIP** và **12 test method mới của §5–§6 CHƯA TỪNG ĐƯỢC CHẠY**.
   ```powershell
   $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=<pw>"
   dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
   ```
   **Kỳ vọng: `Passed: 371 / Failed: 0 / Skipped: 0`.**
   - Nếu **có test đỏ** ⇒ sửa cho xanh (test là hợp đồng, không phải code sai ý — nhưng nếu bạn tin code sai thì báo lại, kèm tên test).
   - Nếu **`total` lệch 371** ⇒ **số đo thắng tài liệu**, cập nhật §1.2 + §12.1 của task doc **và** cổng CI ở §8.
2. **Cấu hình `Email:ApiKey` cho local** (nếu muốn thấy email thật) hoặc để trống (dùng `NullEmailSender`, mọi thứ vẫn chạy và vẫn ghi `email_messages`).
   ```bash
   dotnet user-secrets set --project src/TeamNexus.Api "Email:ApiKey" "re_..."
   ```
3. **Đo baseline frontend** trước khi sửa:
   ```
   cd frontend
   npm run lint   → 0 warning / 0 error
   npx tsc -b     → exit 0
   npm test       → Test Files 47 passed (47) · Tests 279 passed (279)
   npm run build  → OK
   ```
   > ⛔ **Điều kiện "xong" của §7 là `npm test` phải ra ≥ 341 test** (279 + ~62 mới) và `tsc -b` exit 0, `lint` 0/0, `build` OK. Đây là DoD cứng.
   > Nếu baseline lệch 279 ⇒ **dừng lại và báo**, đừng tự đoán.

---

## 2. Hợp đồng API backend đã có (dùng đúng, đừng đoán)

> Tất cả endpoint đều đã có test backend. `httpClient` chung **tự** gắn `X-XSRF-TOKEN` cho POST/PUT/DELETE và **tự** refresh 401 — **không** tự đặt header CSRF.

### 2.1 Invitations

| Method | Route | Quyền | Trả |
|---|---|---|---|
| `GET` | `/api/invitations/preview?token=…` | **ẩn danh** | `200` `InvitationPreviewResponse` · `404` token rác · **`410`** hết hạn/đã huỷ |
| `POST` | `/api/invitations/accept` body `{ token }` | đăng nhập + CSRF | `200` `{ workspaceId, role, alreadyMember }` · `401` chưa đăng nhập · `403` **email tài khoản khác** · `404` · `410` |
| `GET` | `/api/workspaces/{id}/invitations` | Manager+ | `200` `InvitationResponse[]` · `403` · `404` |
| `POST` | `/api/workspaces/{id}/invitations` body `{ email, role? }` | Manager+ | **`201`** `InvitationResponse` · `400` email sai/đã là member · **`409`** đã có lời mời Pending · `403` · `404` |
| `DELETE` | `/api/workspaces/{id}/invitations/{invitationId}` | Manager+ | `204` · `404` (kể cả lời mời của workspace khác) · `409` đã Accepted |

```ts
interface InvitationResponse {
  id: string
  invitedEmail: string
  invitedRole: 'Admin' | 'Manager' | 'Member'
  status: 'Pending' | 'Accepted' | 'Cancelled' | 'Expired'
  invitedByName: string
  expiresAt: string          // ISO
  createdAt: string
  acceptedByUserId: string | null
  acceptedAt: string | null
  emailSent: boolean         // ⚠️ false ⇒ UI phải hiện "Đã tạo lời mời nhưng gửi email thất bại" + nút "Gửi lại"
}
interface InvitationPreviewResponse {
  workspaceId: string; workspaceName: string; invitedEmail: string
  invitedRole: string; invitedByName: string; expiresAt: string; status: string
}
```

> **`token` KHÔNG bao giờ được trả về** ở bất kỳ response nào — nó chỉ nằm trong email. Vì vậy **không** có API "resend"; nút "Gửi lại" = tạo lời mời mới sau khi `DELETE` lời mời cũ (xem §3.3).

### 2.2 Members

| Method | Route | Quyền | Trả |
|---|---|---|---|
| `GET` | `/api/workspaces/{id}/members` | Member+ | `200` **8 field** (xem dưới) · `403` khôn… **`404`** người ngoài workspace |
| `PUT` | `/api/workspaces/{id}/members/{memberUserId}/role` body `{ role }` | **Admin** | `204` · `403` Manager/Member · `400` (owner / chính mình / AI Agent / Admin cuối cùng / role sai) · `404` |
| `DELETE` | `/api/workspaces/{id}/members/{memberUserId}` | **Admin** | `204` · `403` · `400` (owner / chính mình / AI Agent / Admin cuối cùng) · `404` |
| `POST` | `/api/workspaces/{id}/quick-email` body `{ subject, body, recipientUserIds }` | Manager+ | `200` `{ requested, sent, failed, errors[] }` · `400` · `403` · `404` · **`429`** vượt hạn mức/giờ |

```ts
interface WorkspaceMemberResponse {
  userId: string
  displayName: string
  role: 'Admin' | 'Manager' | 'Member'
  avatarUrl: string | null
  memberType: 'human' | 'ai_agent'
  email: string | null      // Giai đoạn 11 — append ở cuối
  joinedAt: string          // Giai đoạn 11
  isOwner: boolean          // Giai đoạn 11
}
interface QuickEmailResult { requested: number; sent: number; failed: number; errors: string[] }
```

> ⚠️ **5 field đầu phải giữ nguyên tên** — `useWorkspaceMembers` và dropdown assignee (`KanbanColumn`, `TaskDetailModal`, Smart Setup) đang parse chúng. Chỉ **thêm** phần hiển thị với 3 field mới.

### 2.3 Profile

| Method | Route | Quyền | Trả |
|---|---|---|---|
| `GET` | `/api/users/me` | đăng nhập | `200` `{ id, email, displayName, avatarUrl, createdAt }` · `401` |
| `PUT` | `/api/users/me` body `{ displayName, avatarUrl }` | đăng nhập + CSRF | `200` (cùng shape) · `400` tên rỗng/>120 hoặc URL không an toàn |
| `GET` | `/api/users/me/workspaces` | đăng nhập | `200` `MyWorkspaceResponse[]` — **read-only, KHÔNG tự tạo workspace mặc định** |
| `DELETE` | `/api/users/me/workspaces/{workspaceId}` | đăng nhập + CSRF | `204` · `400` owner / Admin cuối cùng · `404` |

```ts
interface MyWorkspaceResponse {
  id: string; name: string; description: string | null
  role: 'Admin' | 'Manager' | 'Member'; ownerId: string; isOwner: boolean
}
```

> `avatarUrl` rỗng (`""`) ⇒ **xoá avatar** (server lưu `null`). URL phải **tuyệt đối** `http://`/`https://`; `javascript:`, `data:`, `file:`, `vbscript:`, `//host/x`, đường dẫn tương đối ⇒ **400**. Validate ngay ở FE bằng `isSafeHref` **đã có** ở `features/board/utils/markdown.tsx` (luật giống hệt backend `TeamNexus.Shared.Profile.AvatarUrl`).

### 2.4 Notifications (đã có từ Giai đoạn 5, chỉ **thêm type mới**)

Endpoint **không đổi**: `GET /api/notifications?isRead=&take=` · `POST /api/notifications/{id}/read` · `POST /api/notifications/read-all`.

**3 `type` mới cần thêm nhãn tiếng Việt** (đã có backend ghi):

| `type` | Nhãn gợi ý | Ai nhận |
|---|---|---|
| `TaskAssigned` | "Được giao thẻ" | người được gán task (không phải chính người gán, không phải AI Agent) |
| `CommentOnTask` | "Bình luận mới" | người phụ trách task (không phải tác giả comment, không phải AI Agent) |
| `WorkspaceInvitation` | "Lời mời workspace" | **dự trữ** — backend chưa ghi; vẫn nên map nhãn để không rơi vào fallback |

**`payload` mới cần khai trong type:** `taskId?: string`, `boardId?: string | null`, `commentId?: string`, `invitationId?: string`.
**Quan trọng:** notification của Giai đoạn 11 **KHÔNG có `severity`** ⇒ chip phải trung tính (`default`), **không** đỏ như cảnh báo Observer.

---

## 3. §7 — Frontend (chi tiết thi hành)

### 3.1 Cấu trúc thư mục **mới**

```
frontend/src/shared/components/AppHeader.tsx                    ← hạng mục G
frontend/src/shared/components/NotificationBell.tsx
frontend/src/shared/hooks/useUnreadCount.ts

frontend/src/features/ai/types/notification.types.ts            (SỬA)
frontend/src/features/ai/components/NotificationDrawer.tsx      (SỬA — hạng mục H)
frontend/src/features/ai/components/NotificationItem.tsx        (SỬA)

frontend/src/features/members/types/member.types.ts
frontend/src/features/members/services/memberApi.ts
frontend/src/features/members/utils/memberRoleLabels.ts         (THUẦN)
frontend/src/features/members/components/MembersTable.tsx
frontend/src/features/members/components/InviteMemberModal.tsx
frontend/src/features/members/components/PendingInvitationsTable.tsx
frontend/src/features/members/components/QuickEmailModal.tsx
frontend/src/features/members/pages/WorkspaceMembersPage.tsx

frontend/src/features/profile/types/profile.types.ts
frontend/src/features/profile/services/profileApi.ts
frontend/src/features/profile/pages/ProfilePage.tsx

frontend/src/features/invitations/services/invitationApi.ts
frontend/src/features/invitations/pages/AcceptInvitationPage.tsx
frontend/src/features/invitations/utils/pendingInvite.ts        (THUẦN)
```

### 3.2 Hạng mục **G** — `AppHeader.tsx` (bắt buộc, không được bỏ qua)

**Vì sao bắt buộc:** yêu cầu E nói *"badge unread + dropdown/drawer **tại header**"*. Hiện header bị **copy 5 lần** (`DashboardPage` 68–91 · `BoardListPage` 132–158 · `ReportsPage` ~120–140 · `WorkspaceSettingsPage` 92–123 · `WorkspaceActivityPage`), nên nếu không tách component thì trang mới sẽ **không bao giờ có chuông**.

```tsx
interface AppHeaderProps {
  title?: React.ReactNode
  showNotifications?: boolean      // default true
  children?: React.ReactNode       // nút theo trang (Báo cáo / Hoạt động / Cài đặt / Thành viên…)
}
```
Nội dung: logo/title · `children` · `NotificationBell` · `Dropdown` avatar (tên + **"Hồ sơ cá nhân" → `/profile`** + **"Đăng xuất"**).

> ⚠️ **Ràng buộc cứng:** `BoardView.tsx` **giữ nguyên** header riêng của nó (nó nhận nút AI Agent, Observer, filter…). Bạn **chỉ** refactor 5 trang trên. `BoardView.test.tsx` **phải còn xanh** — nếu test cũ assert text/role trong header thì **chỉ thêm** `data-testid` mới, **không** viết lại test cũ.

### 3.3 Hạng mục **H** — `NotificationDrawer` + `NotificationBell`

- `useUnreadCount()`: `notificationApi.listNotifications({ take: 1 })` khi mount + poll **60s** (đúng pattern `BoardView.tsx` 202–207 và `useNotifications` 112–154 — **không** thêm cơ chế mới). Lỗi API ⇒ trả `0`, **không** ném.
- `NotificationBell`: `Badge count={unreadCount}` + `BellOutlined`, `data-testid="notification-bell"` → mở `NotificationDrawer`.
- **Sửa `NotificationDrawer`:** đổi `workspaceId: string` ⇒ **`workspaceId?: string`** (**giữ tương thích**: `BoardView` đang truyền prop ⇒ không đổi chỗ gọi). Tiêu đề **"Trung tâm thông báo"** (D14 — bỏ chữ "AI Observer").
- `NotificationItem` nhận `workspaceId?: string`: khi **không** có workspace thì **ẩn** nút điều hướng tới task (chỉ mark-read) — **không** được crash vì `undefined` trong URL.
- `NotificationItem`: mở rộng `getTypeText` cho 3 type ở §2.4; `getSeverityTagColor` fallback `default` khi `severity` vắng.

### 3.4 Trang Members

- Route `/workspaces/:workspaceId/members` (bọc `ProtectedRoute`).
- **Nút vào trang:** thêm `Button icon={<TeamOutlined />}` **"Thành viên"** vào `children` của `AppHeader` ở `BoardListPage` — hiển thị cho **mọi** thành viên (trang chỉ đọc với Member); các nút thao tác **bên trong** ẩn/disable theo `isAdmin`/`isManagerOrAdmin` từ **`useWorkspaceRole` đã có** (Phase 10 §4.1) — **không** viết lại logic phân quyền.
- `MembersTable`: avatar + `displayName` + `email` + `Role` (`Tag`) + `memberType` (**nhãn "AI Agent"** cho agent) + `joinedAt` (`dayjs`) + badge **"Chủ sở hữu"** (`isOwner`).
  - Cột Hành động: `Select` đổi role — **chỉ Admin**, disable với **owner / agent / chính mình**; `Popconfirm` kick — **chỉ Admin**, cùng bộ disable.
- `PendingInvitationsTable`: email · role · người mời · `expiresAt` · `status` (`Tag`); nút **"Huỷ"** (`Popconfirm`) **chỉ khi `Pending`**; `Empty` khi rỗng.
- `InviteMemberModal`: `Input` email (validate FE cùng luật RFC-lite: đúng 1 `@`, domain có `.`, không khoảng trắng) + `Select` role (`Member` **mặc định** / `Manager` / `Admin`).
  - Sau khi tạo: `emailSent === false` ⇒ `message.warning` *"Đã tạo lời mời nhưng gửi email thất bại"* + nút **"Gửi lại"**.
  - **"Gửi lại" không có API riêng** (token chỉ nằm trong email): thực hiện = `DELETE` lời mời cũ ⇒ `POST` lời mời mới với cùng email/role.
- `QuickEmailModal`: `Select mode="multiple"` member **`memberType === 'human'`** (**không** hiện AI Agent) + `Input` tiêu đề (≤ 200, có đếm) + `Input.TextArea` (≤ 8000, có đếm); sau khi gửi hiện `{sent}/{requested}`; `429` ⇒ `message.error` với nội dung từ `{ error }`.

### 3.5 Trang Profile

- Route `/profile` (bọc `ProtectedRoute`); vào từ `Dropdown` avatar ở `AppHeader`.
- Tab **"Thông tin cá nhân"**: form `displayName` (1–120) + `avatarUrl` (validate `isSafeHref`) + preview `Avatar`.
  - Lưu ⇒ `PUT /api/users/me` ⇒ **gọi `useAuthStore.checkAuth()`** để header cập nhật ngay (payload `/api/auth/me` đã được backend cập nhật đồng bộ — có test).
- Tab **"Không gian làm việc của tôi"**: danh sách `MyWorkspaceResponse` (tên · role `Tag` · badge owner) + nút **"Rời"** (`Popconfirm`).
  - `isOwner` ⇒ **disable** nút + hiện lý do *"Chủ sở hữu không thể rời workspace"* và link tới `/workspaces/{id}/settings` để chuyển quyền.
- Lỗi API ⇒ `message.error` tiếng Việt; rỗng ⇒ `Empty`.

### 3.6 Trang Accept Invitation (công khai)

- Route **`/invitations/accept`** — **KHÔNG** bọc `ProtectedRoute`.
- Luồng chuẩn:
  1. Đọc `?token=`; token rỗng ⇒ `<Result status="400">`.
  2. Lưu token vào `sessionStorage` bằng `pendingInvite.ts` (`save/read/clear`, **không** ném khi rác).
  3. **Chưa** đăng nhập ⇒ `loginWithProvider('github'|'google')` (redirect full page của `useAuthStore`).
  4. **Đã** đăng nhập + có token trong `sessionStorage` ⇒ `GET /api/invitations/preview` ⇒ hiện **tên workspace · người mời · role sắp nhận · hạn** + nút **"Tham gia"** / **"Từ chối"**.
  5. "Tham gia" ⇒ `POST /api/invitations/accept { token }` ⇒ `message.success` + `navigate('/workspaces/{workspaceId}/boards')`.
     - `alreadyMember: true` ⇒ thông báo *"Bạn đã là thành viên"* rồi chuyển thẳng vào workspace.
  6. **Luôn xoá token khỏi `sessionStorage`** sau khi xử lý xong (thành công **hoặc** thất bại).
- Ca lỗi: `403` ⇒ `<Result status="403">` *"Lời mời này được gửi tới một email khác."* · `410` ⇒ `<Result status="410">` *"Lời mời đã hết hạn hoặc đã bị huỷ"* + link về `/` · `404` ⇒ `<Result status="404">`.

### 3.7 Router

`frontend/src/app/router.tsx` — thêm 3 route:
- `/workspaces/:workspaceId/members` → `WorkspaceMembersPage` (bọc `ProtectedRoute`)
- `/profile` → `ProfilePage` (bọc `ProtectedRoute`)
- `/invitations/accept` → `AcceptInvitationPage` (**công khai**, đặt **trước** route `*`)

---

## 4. Checklist test frontend — ~62 test / ~15 file

| File | Số test | Nội dung bắt buộc |
|---|---|---|
| `shared/components/__tests__/AppHeader.test.tsx` | 6 | render title; avatar + tên; dropdown có "Hồ sơ cá nhân" → `/profile`; bấm Đăng xuất gọi `logout`; `children` render; **không** có chuông khi `showNotifications={false}` |
| `shared/hooks/__tests__/useUnreadCount.test.ts` | 4 | gọi API khi mount; trả `unreadCount`; lỗi ⇒ `0` không ném; `refresh` gọi lại |
| `features/ai/components/__tests__/NotificationItem.test.tsx` **(SỬA — giữ test cũ xanh)** | +4 | 3 type mới ⇒ nhãn tiếng Việt; **không có `severity` ⇒ chip `default`** |
| `features/ai/components/__tests__/NotificationDrawer.test.tsx` **(SỬA)** | +2 | tiêu đề "Trung tâm thông báo"; **không** truyền `workspaceId` ⇒ không crash + ẩn nút điều hướng |
| `features/members/utils/__tests__/memberRoleLabels.test.ts` | 4 | nhãn role; nhãn `ai_agent`; role lạ ⇒ fallback an toàn; helper "không thao tác được" |
| `features/members/services/__tests__/memberApi.test.ts` | 8 | đúng method + URL + body; bỏ param `undefined`; map lỗi 403 |
| `features/members/components/__tests__/MembersTable.test.tsx` | 9 | đủ cột; badge owner; AI Agent hiện nhãn; **Member ⇒ KHÔNG có control**; **Manager ⇒ cũng KHÔNG**; Admin ⇒ có + disable owner/chính mình; kick hỏi xác nhận rồi gọi callback |
| `features/members/components/__tests__/InviteMemberModal.test.tsx` | 7 | email rỗng ⇒ lỗi; email sai (5 biến thể) ⇒ lỗi; role mặc định `Member`; `onInvite` đúng payload; `emailSent=false` ⇒ cảnh báo + "Gửi lại"; nhãn tiếng Việt; đóng sau thành công |
| `features/members/components/__tests__/PendingInvitationsTable.test.tsx` | 4 | render row; chỉ `Pending` có nút Huỷ; xác nhận ⇒ `onCancel`; rỗng ⇒ `Empty` |
| `features/members/components/__tests__/QuickEmailModal.test.tsx` | 6 | **không** hiện AI Agent; tiêu đề > 200 ⇒ lỗi; nội dung rỗng ⇒ lỗi; đếm ký tự; `onSend` đúng danh sách; hiện `sent/requested` |
| `features/members/pages/__tests__/WorkspaceMembersPage.test.tsx` | 4 | hiện bảng + danh sách lời mời; nút "Mời" ẩn với Member; mở modal; 403 ⇒ `<Result>` |
| `features/profile/services/__tests__/profileApi.test.ts` | 4 | 4 hàm ⇒ đúng method + URL + body |
| `features/profile/pages/__tests__/ProfilePage.test.tsx` | 8 | prefill từ `useAuthStore`; `avatarUrl` sai scheme ⇒ lỗi validate; lưu ⇒ update + `checkAuth`; danh sách workspace + role; owner ⇒ nút "Rời" disable + lý do; rời ⇒ gọi API + reload; lỗi ⇒ `message.error`; rỗng ⇒ `Empty` |
| `features/invitations/utils/__tests__/pendingInvite.test.ts` | 4 | lưu/đọc/xoá; không có ⇒ `null`; giá trị rác ⇒ không ném |
| `features/invitations/pages/__tests__/AcceptInvitationPage.test.tsx` | 9 | không token ⇒ 400; chưa đăng nhập ⇒ lưu token + redirect OAuth; preview hiện tên workspace/role/hạn; "Tham gia" ⇒ accept + navigate; 403 email khác ⇒ `<Result>`; 410 ⇒ `<Result>`; `alreadyMember` ⇒ thông báo khác; token bị xoá sau khi xong; lỗi mạng ⇒ `message.error` |

> **Kỳ vọng tổng frontend: 279 → ~341 test · 47 → ~62 file.**
> **Giữ xanh toàn bộ test cũ.** Nếu buộc phải sửa test cũ ⇒ ghi rõ **lý do + tên test** vào báo cáo.

---

## 5. §8 — CI & tài liệu

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | Nâng cổng `if ($total -ne 226)` → **`-ne 371`**; **thêm `Email__ApiKey: " "`** vào khối `env:` (một khoảng trắng — cùng trick `DeepSeek:ApiKey`; nếu thiếu, CI có thể gửi **email thật**); cập nhật comment chuỗi ("171 → 189 → 226 → **371 Giai đoạn 11**") |
| 2 | `.github/workflows/ci-web.yml` | `if ($total -le 206)` → **`-le 279`** + comment "Giai đoạn 10 là 279 → Giai đoạn 11 là ~341" |
| 3 | `Project-Documents/04-database-design.md` | **+§3.9 Module Member & Profile (Giai đoạn 11)**: mô tả `email_messages` (**bảng mới**, không có trong thiết kế gốc) + ghi chú `workspace_invitations` (partial UQ `Pending`, FK workspace **optional** vì bảng cố ý không có query filter); cập nhật bảng `NotificationType` ở §4 (thêm `TaskAssigned`, `CommentOnTask`, `WorkspaceInvitation`); thêm index mới ở §5; **sửa §6** chuỗi migration **6 → 9** (tài liệu đang lệch thực tế: 8 rồi 9) + ghi chú Giai đoạn 9/10/11; bổ sung 3 gạch đầu dòng ở §7 (token invite chỉ lưu hash; kick member để lại `tasks.assignee_id`; hạn mức email) |
| 4 | `Project-Documents/03-roadmap.md` | Giai đoạn 11: gắn link task doc + baseline + "**+1 migration (9 tổng)**" + kết quả đo thật khi xong |
| 5 | `Project-Documents/01-system-specification.md` | §10: chốt luồng accept (email tài khoản **phải khớp** lời mời; role do **người mời** chọn, người nhận không tự chọn) |
| 6 | `README.md` | +`## Trạng thái (Giai đoạn 11 – Quản lý Member & Profile)` (checklist 5 ô) + **sửa mục Migration** (đang ghi sai *"6 migration, mới nhất `Phase7AiAgentSchema`"* → **9**, mới nhất `Phase11MemberProfile`) + hướng dẫn cấu hình `Email:ApiKey` (User Secrets local / `Email__ApiKey` env trên Render) |
| 7 | `Project-Documents/report/phase-11-member-profile-management-test-report.md` | Tạo khi kết thúc: số test **thật**, bug thật bắt được, bằng chứng §12 của task doc, ảnh chụp luồng mời/accept |

---

## 6. ⛔ KHÔNG được làm

- ❌ **Không** sửa **bất kỳ** file backend nào (module Board/Ai/Shared/Persistence, migration, appsettings). Nếu tin backend sai ⇒ **báo lại kèm test đỏ**, đừng tự sửa.
- ❌ **Không** sinh migration mới. Giai đoạn 11 đã chốt **đúng 1** migration và `has-pending-model-changes` đang sạch.
- ❌ **Không** thêm type mới vào `NotificationTypes.All` (whitelist chống hallucination của Observer) — 3 type mới đã nằm ở `MemberNotificationTypes` (cả phía Board lẫn Ai).
- ❌ **Không** đổi shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `NotificationResponse`, `HubConnectionStatus`, payload SignalR. `WorkspaceMemberResponse` **chỉ** được đọc thêm 3 field mới ở cuối.
- ❌ **Không** đổi tên event SignalR, không đổi kiến trúc 1-hub-1-group (`useBoardHub.ts`).
- ❌ **Không** sửa `shared/api/httpClient.ts` hay `features/reporting/utils/reportDownload.ts`.
- ❌ **Không** thêm thư viện npm. Chỉ dùng `antd` · `dayjs` · `axios` · `react-router-dom` · `zustand` · `@microsoft/signalr` sẵn có.
- ❌ **Không** `dangerouslySetInnerHTML` ở bất kỳ đâu.
- ❌ **Không** tự đặt header CSRF trong `memberApi`/`profileApi`/`invitationApi` (interceptor đã tự gắn).
- ❌ **Không** coi **Manager** là được đổi role/kick — **chỉ Admin** (roadmap ghi rõ; backend trả **403** nếu Manager thử).
- ❌ **Không** cho người nhận lời mời tự chọn role — role do **người mời** chọn (`invited_role`).
- ❌ **Không** để `POST /api/invitations/accept` chạy khi chưa đăng nhập, và **không** quên xoá token khỏi `sessionStorage` sau khi xong.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**).
- ❌ **Không** mở rộng phạm vi sang: Dashboard / Tìm kiếm / **@mention** (Giai đoạn 12); Mobile (13); hard delete; đa assignee; template email tuỳ biến; đổi avatar bằng upload file (chỉ nhận URL).

---

## 7. Rủi ro & giả định

| # | Rủi ro | Cách xử lý |
|---|---|---|
| **R1** | **237 test backend đang SKIP** trên máy giao việc; 12 test method mới (§5–§6) **chưa từng chạy** | Việc **bắt buộc #1** ở §1. Kỳ vọng `Passed 371 / Skipped 0`. Nếu total lệch ⇒ cập nhật số thật vào task doc + cổng CI |
| **R2** | **CI có thể gửi email thật** nếu key rò từ secrets | `TeamNexusApiFactory` đã đặt `Email:ApiKey = " "`; **còn phải** thêm `Email__ApiKey: " "` vào `ci-backend.yml` (§5 #1). Làm trước khi push |
| **R3** | Refactor 5 header (hạng mục **G**) làm đỏ test cũ | Chỉ **thêm** `data-testid`; giữ nguyên text/role; chạy `npm test` **trước và sau từng trang** |
| **R4** | `NotificationDrawer` bắt buộc `workspaceId` ⇒ dễ vỡ khi dùng toàn cục | Đổi thành **optional** (§3.3) và có test riêng "không truyền `workspaceId` ⇒ không crash" |
| **R5** | Chip đỏ mặc định cho mọi notification | Notification Giai đoạn 11 **không có `severity`** ⇒ chip `default`. Có test chặn |
| **R6** | Nút "Gửi lại" tưởng có API riêng | **Không có**: token chỉ nằm trong email ⇒ "Gửi lại" = `DELETE` cũ + `POST` mới (§3.4) |
| **R7** | Nghĩ `GET /api/users/me/workspaces` giống `GET /api/workspaces` | Khác hẳn: bản mới **read-only**, **không** tự tạo workspace mặc định (backend có test chặn hồi quy) |
| **R8** | Trần `take=100` / hạn mức 100 email/giờ | Là **quyết định có chủ ý**; `429` kèm nội dung `{ error }` từ backend — hiển thị nguyên văn cho người dùng |

**Giả định:**
- Baseline frontend **279 test / 47 file** là số đo thật trên máy dev; **số đo thắng tài liệu**.
- Frontend: React 19 + TypeScript 6 + AntD 6 + Vite 8 + Vitest 5; toàn bộ nhãn UI **tiếng Việt có dấu**.
- Backend đang chạy ở `http://localhost:5000` (Vite proxy `/api`), PostgreSQL có sẵn để chạy `dotnet test`.
- Giai đoạn 9 đã merge ⇒ Identity role chỉ còn `Admin`/`User`; **mọi** cổng quyền của giai đoạn này đọc `workspace_members.role`.
