# TeamNexus.Modules.Board — Backend Kanban (Phase 2 §2–§3)

Module cung cấp CRUD Board/Column/Task + Label/Comment theo Minimal API
(nhất quán module Auth) và real-time qua SignalR `BoardHub`.

## Quyền (workspace-scoped)

Quyền kiểm tra theo **workspace_members.role** (không dùng role Identity toàn cục):

| Hành động | Yêu cầu |
|---|---|
| Đọc board/column/task/label/comment; tạo/sửa/di chuyển/xoá task; bình luận; gắn/gỡ label | Member+ trong workspace của board |
| Đọc danh sách thành viên workspace (`GET .../members`) | Member+ |
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
| Members | `GET /api/workspaces/{workspaceId}/members` → `[{ userId, displayName, role, avatarUrl }]` (Phase 3 §3.1) |
| Real-time | `WS /hubs/board` (yêu cầu đăng nhập) — `JoinBoard(boardId)`, `LeaveBoard(boardId)` |

## SignalR (Phase 2 §3)

- Client connect `/hubs/board` → gọi `JoinBoard(boardId)` để vào group `board-{boardId}`.
  Join chỉ thành công nếu user là **member** của workspace chứa board (kiểm tra mỗi lần join).
- Server broadcast qua `IBoardEventPublisher` (wrapper `IHubContext<BoardHub>`) **sau khi** ghi
  DB commit. Lỗi broadcast chỉ log, không làm fail request.
- Sự kiện (tên method client đăng ký):

| Sự kiện | Payload |
|---|---|
| `TaskCreated` / `TaskUpdated` | `TaskResponse` |
| `TaskMoved` | `{ taskId, fromColumnId, toColumnId, position }` |
| `TaskDeleted` | `{ taskId }` |
| `ColumnCreated` / `ColumnUpdated` | `ColumnResponse` |
| `ColumnsReordered` | danh sách `{ id, position }` |
| `ColumnDeleted` | `{ columnId }` |
| `CommentAdded` | `CommentResponse` |
| `CommentDeleted` | `{ commentId, taskId }` |

- JSON payload camelCase (mặc định SignalR). Auth: JWT trong cookie `access_token` gửi kèm
  handshake; ngoài ra JwtBearer chấp nhận `access_token` query chỉ cho path `/hubs/*`
  (SignalR client không set được Authorization header).
- Không broadcast cho: Board CRUD, comment update, label attach/detach (theo §3.2 task doc).

## Ghi chú nghiệp vụ

- `completed_at` set khi task vào cột `is_done = true`, clear khi rời đi (cột `is_done`
  thêm qua migration `Phase2BoardColumnIsDone`).
- Column chỉ xoá vật lý khi **trống tuyệt đối** (task soft-deleted vẫn giữ FK Restrict) —
  nếu còn task → 409.
- Reorder column dùng 2 pha trong transaction (tránh vi phạm UQ `(board_id, position)`).
- Move task: dịch chuyển + đánh số lại cột đích trong transaction; chấp nhận last-write-wins.
- Errors: service ném `BoardModuleException` (404/403/400/409), group filter map sang `{ error }`.

## Phát activity log cho AI Observer (Phase 5 §2)

Board **khai báo** port `Services/IActivityLogWriter.cs` (interface + `ObserverActivityActions` +
`ObserverEntityTypes` + `NullActivityLogWriter`) và **không** tham chiếu module `Ai` — implementation
`ActivityLogWriter` nằm ở module Ai và đăng ký đè bản no-op của `AddBoardModule`.

5 mutation phát sự kiện **sau khi** dữ liệu đã ghi (riêng move: sau `CommitAsync`):

| Nơi | `action` | payload (jsonb) |
|---|---|---|
| `TaskService.CreateTaskAsync` | `TaskCreated` | `{columnId, assigneeId, priority, isDone}` |
| `TaskService.UpdateTaskAsync` | `TaskUpdated` | `{titleChanged, descriptionChanged, assigneeId, dueDate, priority}` — **boolean** cho field text, **không** dump nội dung |
| `TaskService.MoveTaskAsync` | `TaskMoved` (+ `TaskCompleted` nếu vào cột `is_done`) | `{fromColumnId, toColumnId, position}` / `{columnId}` |
| `TaskService.DeleteTaskAsync` | `TaskDeleted` | `{columnId}` (lấy `WorkspaceId` **trước** khi soft-delete) |
| `CommentService.CreateCommentAsync` | `CommentAdded` | `{commentId, taskId}` |

Ghi log là **best-effort**: `RecordAsync` không bao giờ throw (lỗi ⇒ `LogWarning`), nên CRUD không bị ảnh
hưởng. API/quyền công khai của Board **không đổi**; verify nhóm G (31/31 PASS) nằm ở README module Ai.

## Test nhanh (dev)

1. Seed workspace + membership (xem README root, mục Auth quick test — gán `Admin`/`Manager`
   cho user GitHub trong `workspace_members`).
2. Chạy API (`dotnet run --project src/TeamNexus.Api` — port 5000) → Scalar tại `/scalar`.
3. Login GitHub → gọi các endpoint nhóm Boards/Columns/Tasks/Labels/Comments.
4. Real-time: mở 2 tab (hoặc 2 client WebSocket) cùng board → thao tác REST ở 1 client,
   client kia nhận event ngay; client ở board khác không nhận (group isolation).
