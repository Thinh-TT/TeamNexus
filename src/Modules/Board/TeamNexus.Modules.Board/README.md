# TeamNexus.Modules.Board — Backend Kanban (Phase 2 §2–§3)

Module cung cấp CRUD Board/Column/Task + Label/Comment theo Minimal API
(nhất quán module Auth). SignalR `BoardHub` sẽ bổ sung ở §3.

## Quyền (workspace-scoped)

Quyền kiểm tra theo **workspace_members.role** (không dùng role Identity toàn cục):

| Hành động | Yêu cầu |
|---|---|
| Đọc board/column/task/label/comment; tạo/sửa/di chuyển/xoá task; bình luận; gắn/gỡ label | Member+ trong workspace của board |
| Tạo/sửa/xoá board, column, reorder; tạo/xoá label workspace | Manager/Admin |
| Sửa/xoá comment | Tác giả, hoặc Manager/Admin của workspace |

Mọi endpoint yêu cầu đăng nhập (`RequireAuthorization`); mutating endpoint bắt buộc
header `X-XSRF-TOKEN` (lấy ở `GET /api/auth/antiforgery`).

## Endpoints

| Nhóm | Endpoint |
|---|---|
| Boards | `GET|POST /api/workspaces/{workspaceId}/boards`, `GET|PUT|DELETE .../boards/{boardId}` |
| Columns | `GET|POST /api/boards/{boardId}/columns`, `PUT /columns/reorder`, `PUT|DELETE /columns/{columnId}` |
| Tasks | `GET|POST /api/boards/{boardId}/tasks`, `GET|PUT|DELETE /tasks/{taskId}`, `PUT /tasks/{taskId}/move` |
| Labels | `GET|POST /api/workspaces/{workspaceId}/labels`, `DELETE /labels/{labelId}`, `POST|DELETE /api/tasks/{taskId}/labels[/{labelId}]` |
| Comments | `GET|POST /api/tasks/{taskId}/comments`, `PUT|DELETE /comments/{commentId}` |

## Ghi chú nghiệp vụ

- `completed_at` set khi task vào cột `is_done = true`, clear khi rời đi (cột `is_done`
  thêm qua migration `Phase2BoardColumnIsDone`).
- Column chỉ xoá vật lý khi **trống tuyệt đối** (task soft-deleted vẫn giữ FK Restrict) —
  nếu còn task → 409.
- Reorder column dùng 2 pha trong transaction (tránh vi phạm UQ `(board_id, position)`).
- Move task: dịch chuyển + đánh số lại cột đích trong transaction; chấp nhận last-write-wins.
- Errors: service ném `BoardModuleException` (404/403/400/409), group filter map sang `{ error }`.

## Test nhanh (dev)

1. Seed workspace + membership (xem README root, mục Auth quick test — gán `Admin`/`Manager`
   cho user GitHub trong `workspace_members`).
2. Chạy API (`dotnet run --project src/TeamNexus.Api` — port 5000) → Scalar tại `/scalar`.
3. Login GitHub → gọi các endpoint nhóm Boards/Columns/Tasks/Labels/Comments.
