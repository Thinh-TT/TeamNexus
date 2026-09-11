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
| Members | `GET /api/workspaces/{workspaceId}/members` → `[{ userId, displayName, role, avatarUrl, memberType }]` (Phase 3 §3.1, `memberType` từ Phase 7) |
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
| `AgentRunProgress` | `{ runId, taskId, boardId, status, stopReason, toolCallCount, totalTokens, clarificationQuestion }` (Phase 7 D8 — **Board chỉ khai báo** tên event + method trên `IBoardEventPublisher`; orchestrator của module Ai mới là nơi broadcast, ở 2 thời điểm Running và terminal) |

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

## AI Agent Executor (Phase 7 §3)

Ba thay đổi nhỏ nhưng bắt buộc để AI Agent trở thành một assignee thật:

**1. Gán người thực hiện phải là thành viên của workspace.** `TaskService.CreateTaskAsync`/`UpdateTaskAsync` (qua helper
`RequireAssigneeInWorkspaceAsync`) kiểm `workspace_members`, **không** chỉ bảng `users` như trước. Assignee ngoài workspace ⇒
**400** `"Assignee is not a member of this workspace."`; assignee `null` luôn hợp lệ (không tốn truy vấn). Agent là một row
`workspace_members` bình thường (`member_type = 'ai_agent'`, `role = Member`) nên **đi qua đúng đường kiểm tra này** — không có
nhánh đặc biệt nào cho agent.

**2. `TaskResponse` thêm 2 field cuối** (giữ thứ tự cũ để client cũ không vỡ):

| Field | Ý nghĩa | Nguồn |
|---|---|---|
| `assigneeIsAiAgent` | assignee là agent của workspace | `IAiAgentResolver.IsAiAgentAsync` — resolve **1 lần cho cả trang** |
| `activeAgentRunId` | run "đang sống" mới nhất (`Running` > đang chờ), `null` nếu không có | `AgentRunLookup.LoadActiveRunIdsAsync` — **1 truy vấn gộp cho cả trang** |

Điền ở **cả 3 đường trả task**: `GET /api/workspaces/{id}/boards/{boardId}`, `GET /api/boards/{id}/tasks`,
`GET /api/boards/{id}/tasks/{taskId}` (cùng `PUT`/`move` vì trả qua cùng service). Bỏ sót đường board là **bug im lặng**
(Kanban card không hiện badge agent) nên hành vi này được verify riêng (check I-2a).

> ⚠️ `activeAgentRunId` **không** phải nguồn sự thật duy nhất: event SignalR có thể rớt khi app sleep, nên UI vẫn phải GET run
> khi mount/reconnect (§5C).

**3. Cột "Chờ làm rõ" (`is_clarification`)** — đối xứng với `is_done`, luật nằm ở `ColumnService`:

| Hành vi | Kết quả |
|---|---|
| Tạo/sửa cột với **cả** `isDone` và `isClarification` = true (hoặc bật cờ này lên cột đã có cờ kia) | **400** `"A column cannot be both Done and Awaiting clarification."` |
| Tạo cột clarification **thứ hai** trong một board | bị DB chặn (`uq_board_columns_clarification`) |
| Xoá cột clarification — **kể cả khi rỗng** | **409** `"The 'Awaiting clarification' column is managed by the AI Agent and cannot be deleted."` |
| Cột thường rỗng | xoá được (204) như trước |
| Cột clarification trong Reporting/Observer | vẫn tính là "đang mở" (`is_done = false`) — đúng mong muốn |

Cột **không** auto-seed cho board cũ: `IAiAgentResolver.EnsureClarificationColumnAsync` tạo **lazy** ở `position = max + 1`
khi agent cần lần đầu; partial UQ là chốt chống race.

**4. Port `IAiAgentResolver` (khai báo ở Board, cài đặt ở Ai — D7).** Cùng mẹo `IActivityLogWriter`: `BoardModule` đăng ký
`NullAiAgentResolver`, module Ai đăng ký đè `WorkspaceAiAgentResolver` (Program.cs gọi `AddBoardModule` trước `AddAiModule`).
`IsAiAgentAsync` của null impl trả `false` để Board vẫn chạy độc lập; hai method còn lại ném `NotSupportedException`.

> **Agent xuất hiện trong dropdown assignee như thế nào:** `WorkspaceMemberService.GetMembersAsync` (nguồn duy nhất của
> dropdown) gọi `EnsureAgentAsync` **trước khi đọc** — agent là "pseudo-member" tạo lazy, và nếu không bảo đảm row tồn tại thì
> không workspace nào gán được việc cho agent. Đây là điều chỉnh **có chủ ý** so với câu chữ §3.4 ban đầu; xem banner §3 trong
> `tasks/phase-7-ai-agent-executor.md`. Cuộc gọi nằm trong `try/catch` best-effort: lỗi tạo agent chỉ `LogWarning` và endpoint
> vẫn trả danh sách. `OrderBy(JoinedAt)` giữ agent ở **cuối** dropdown. `MemberType` được trả về dạng snake_case
> (`"human"` | `"ai_agent"`) để khớp DB design §4 và union type của frontend.

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
