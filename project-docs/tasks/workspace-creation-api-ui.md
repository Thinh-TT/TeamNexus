# Tạo không gian làm việc chủ động — API + UI

> **Trạng thái:** ✅ HOÀN THÀNH — bổ sung sau Giai đoạn 15.5 (2026-11-12).
> **Phạm vi:** module `Board` (backend) + `features/workspace` & `features/auth/pages/DashboardPage` (frontend).
> **Không** migration, **không** module/.csproj mới, **không** đổi payload cũ.

## 1. Bối cảnh & mục tiêu

Trước thay đổi này, workspace chỉ xuất hiện theo hai cách: (a) side effect "tự tạo workspace mặc định"
trong `GET /api/workspaces` khi user chưa có membership, và (b) lời mời từ workspace khác. Người dùng
**không** có cách chủ động tạo thêm workspace — thiếu cả API lẫn UI.

Mục tiêu: thêm `POST /api/workspaces` và nút **"Tạo không gian làm việc"** trên trang `/` để bất kỳ
người dùng đã đăng nhập nào cũng tạo được workspace mới (mình là Owner + Admin), rồi vào thẳng workspace
đó để tạo bảng Kanban đầu tiên.

## 2. Hợp đồng API

```
POST /api/workspaces
Auth:  cookie + RequireAuthorization + X-XSRF-TOKEN (AntiforgeryValidationEndpointFilter)
Body:  { "name": "Dự án Beta", "description": "Mô tả" | null }

201 Created
Location: /api/workspaces/{id}
{
  "id": "…", "name": "Dự án Beta", "description": "Mô tả",
  "role": "Admin", "ownerId": "<caller>", "isOwner": true
}
```

| Lỗi | Nguyên nhân |
|---|---|
| `400` | Tên rỗng/toàn khoảng trắng/>120 ký tự; mô tả >2000 ký tự |
| `401` | Thiếu/không hợp lệ phiên đăng nhập |
| `403` | Thiếu header CSRF |

Ghi chú thiết kế:
- Dùng lại `WorkspaceSummaryResponse` (contract append-only) — frontend chèn thẳng vào danh sách đang có.
- Không có `403/404` theo workspace: chưa tồn tại membership để phân quyền; người gọi trở thành Admin
  trong cùng một lần ghi.
- **Không** thêm `WorkspaceCreated` vào whitelist nào khác: `activity_logs.action` là text tự do, nên
  action mới không cần migration/CHECK.

## 3. Thay đổi Backend

| File | Thay đổi |
|---|---|
| [`WorkspaceDtos.cs`](../../src/Modules/Board/TeamNexus.Modules.Board/DTOs/WorkspaceDtos.cs) | Thêm `CreateWorkspaceRequest(string Name, string? Description)` |
| [`IActivityLogWriter.cs`](../../src/Modules/Board/TeamNexus.Modules.Board/Services/IActivityLogWriter.cs) | Thêm `ObserverActivityActions.WorkspaceCreated` |
| [`WorkspaceService.cs`](../../src/Modules/Board/TeamNexus.Modules.Board/Services/WorkspaceService.cs) | `CreateAsync`: validate tên/mô tả, tạo `Workspace` + `WorkspaceMember(Admin, Human)`, **một** `SaveChangesAsync`, ghi activity. Thêm `MaxDescriptionLength = 2000` + `RequireValidDescription` (dùng chung cho `UpdateAsync` để mô tả quá dài trả **400** thay vì 500 từ `varchar(2000)`) |
| [`WorkspacesEndpoints.cs`](../../src/Modules/Board/TeamNexus.Modules.Board/Endpoints/WorkspacesEndpoints.cs) | `MapPost("/")` + CSRF filter; handler mỏng trả `Results.Created` |

Bất biến được giữ:
- `ListForUserAsync` / `EnsureDefaultWorkspaceAsync` **không đổi** — sau khi POST, user đã có membership nên
  GET không sinh workspace mặc định thứ hai.
- Không mở transaction lồng; không gọi `IWorkspaceAccess` (chưa có membership).
- `MemberType.Human` được set tường minh để đường POST này không bao giờ tạo row AI Agent.

## 4. Thay đổi Frontend

| File | Thay đổi |
|---|---|
| [`workspace.types.ts`](../../frontend/src/features/workspace/types/workspace.types.ts) | Thêm `CreateWorkspaceRequest` |
| [`workspaceApi.ts`](../../frontend/src/features/workspace/services/workspaceApi.ts) | Thêm `create()` → `POST /workspaces` |
| [`WorkspaceCreateModal.tsx`](../../frontend/src/features/workspace/components/WorkspaceCreateModal.tsx) | **Mới**: modal tên (required + whitespace + max 120) và mô tả (max 2000); `confirmLoading` chặn double-submit; `try/catch` nuốt lỗi để modal ở lại (pattern `ColumnModal`) |
| [`DashboardPage.tsx`](../../frontend/src/features/auth/pages/DashboardPage.tsx) | Nút primary `data-testid="create-workspace-btn"` ở header card + nút trong `Empty`; handler gọi API, chèn workspace vào state, `message.success`, điều hướng `/workspaces/{id}/boards`; lỗi hiện `message.error` rồi rethrow để modal giữ nguyên |
| [`activityLabels.ts`](../../frontend/src/features/workspace/utils/activityLabels.ts) | Nhãn `WorkspaceCreated` → "đã tạo workspace" |

Lý do điều hướng tới `/boards` (không phải `/dashboard`): workspace mới **rỗng**, và trang bảng Kanban có
sẵn empty state **"Tạo bảng đầu tiên"** — đúng hành động tiếp theo.

## 5. Quyết định (K)

- **K1 — Trả `WorkspaceSummaryResponse` + `201 Created`**, không trả `WorkspaceDetailResponse`: đủ dữ liệu
  để chèn vào danh sách hiện có, không phát sinh thêm truy vấn đếm member/board.
- **K2 — Any authenticated user** được tạo; không giới hạn số workspace/user, không cần role hệ thống.
- **K3 — Một `SaveChangesAsync`** cho workspace + membership ⇒ không thể tồn tại workspace "mồ côi"
  không có Admin.
- **K4 — `WorkspaceCreated` là action mới, `board_id = NULL`**, ghi **sau** khi lưu (best-effort).
- **K5 — Hardening mô tả**: validate ≤ 2000 ở cả create **và** update; trước đó giá trị quá dài gây
  `DbUpdateException` ⇒ 500.
- **K6 — Workspace mới rỗng**: không seed board/cột; taxonomy seed lazy, AI Agent tạo lazy.

## 6. Test matrix

**Backend** — [`WorkspaceApiTests.cs`](../../tests/TeamNexus.Api.Tests/Integration/WorkspaceApiTests.cs)
(nhóm `---- create (POST /api/workspaces) ----`):
201 + quyền Admin/Owner + DB row; mô tả trắng ⇒ `null`; workspace xuất hiện trong list và **không** sinh
workspace mặc định; tạo 2 workspace giữ cả hai; tên trắng ⇒ 400 (2 case); biên 120/121; mô tả 2001 ⇒ 400
(create + update); thiếu CSRF ⇒ 403; chưa đăng nhập ⇒ 401; ghi activity `WorkspaceCreated`; user khác
không thấy workspace mới; `GET /{id}` sau tạo ⇒ `memberCount = 1`, `boardCount = 0`.

**Frontend**:
[`workspaceApi.test.ts`](../../frontend/src/features/workspace/services/__tests__/workspaceApi.test.ts)
(`create` gọi đúng `POST /workspaces` + body);
[`WorkspaceCreateModal.test.tsx`](../../frontend/src/features/workspace/components/__tests__/WorkspaceCreateModal.test.tsx)
(render field, chặn tên trắng, trim + submit, mô tả rỗng ⇒ `null`, API lỗi ⇒ modal không đóng);
[`DashboardPage.test.tsx`](../../frontend/src/features/auth/pages/__tests__/DashboardPage.test.tsx)
(tạo thành công ⇒ gọi API + điều hướng `/workspaces/{id}/boards`; thất bại ⇒ modal còn mở);
[`activityLabels.test.ts`](../../frontend/src/features/workspace/utils/__tests__/activityLabels.test.ts)
(nhãn `WorkspaceCreated`).

## 7. Nhật ký thi hành

- **L1 (không lệch contract):** không có migration mới; `04-database-design.md` không đổi.
- **BUG-1 (phát hiện khi rà soát luồng ghi):** `UpdateWorkspace` với mô tả >2000 ký tự trước đây để PostgreSQL trả
  `22001` ⇒ 500. Đã đưa `RequireValidDescription` vào cả create và update, kèm test biên (2001 ⇒ 400).
- **K7:** Modal tự đóng sau khi `onSubmit` resolve; `DashboardPage` không đóng thủ công để tránh hai nguồn
  sự thật cho trạng thái modal.

## 8. Số đo & DoD

- **Build:** `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` = **0 warning / 0 error**.
- **Migration drift:** `dotnet ef migrations has-pending-model-changes` = **"No changes have been made to the model since the last migration."** (không migration mới).
- **Backend Pure:** **666/666 PASS**.
- **Backend full:** **Passed 1346 / Failed 0 / Skipped 0** (baseline 1332 ⇒ **+14**), chạy với `TEAMNEXUS_TEST_DB` trỏ container `pgvector/pgvector:pg18` (port 5433).
- **Frontend lint:** `npm run lint` = **0 warning / 0 error** (302 file).
- **Typecheck:** `npx tsc -b` = **exit 0**.
- **Vitest:** **129/129 file — 799/799 PASS** (baseline 791 ⇒ **+8**); lần chạy xanh dùng `--maxWorkers=2`.
- **Vite build:** **exit 0** (3,341 module; cảnh báo chunk >500 kB là có sẵn).
- **CI baseline:** `ci-backend.yml` 1332 → **1346**; `ci-web.yml` 791 → **799**.

> **⚠️ Flake có sẵn trên máy dev (không do thay đổi này):** hai lần chạy `npm test` mặc định lần lượt
> timeout `ProfilePage.test.tsx > disables Rời button…` và fail race
> `useUnreadCount.test.ts > calls notificationApi.listNotifications…`; cả hai **pass khi chạy riêng**
> (13/13 và 4/4) và nằm ngoài các file đã sửa. Không sửa/nới test ngoài phạm vi; lần chạy xanh dùng
> `--maxWorkers=2` để giảm tranh CPU.

**DoD:**
- [x] API tạo workspace + quyền Owner/Admin + 400/401/403 đúng.
- [x] Ghi activity `WorkspaceCreated`, không đổi payload list cũ, không sinh workspace mặc định lần hai.
- [x] UI tạo workspace trên `/`, hiển thị lỗi tiếng Việt, modal ở lại khi lỗi, điều hướng tới boards.
- [x] Không migration; build/lint/typecheck/test/build xanh; CI baseline nâng theo số đo thật.
