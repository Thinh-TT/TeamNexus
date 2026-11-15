# TeamNexus.Modules.Board — Phân hệ Quản trị Không gian Làm việc & Kanban Thời gian thực

Phân hệ cốt lõi quản lý vòng đời **Workspaces**, **Boards**, **Columns** (`is_done`, `is_clarification`, `is_in_review`), **Tasks** (kèm mã định danh tuần tự `TN-123`), **Labels**, **Comments & `@mention`**, **Members & Invitations**, **Tìm kiếm đa tiêu chí (Keyset Pagination)**, **Dashboard & Sức khỏe Dự án (`ProjectHealth`)** và đồng bộ thời gian thực qua **SignalR `BoardHub`**.

---

## 1. Quy tắc Phân quyền theo Workspace (`IWorkspaceAccess`)

Mọi điểm cuối trong phân hệ `Board` yêu cầu xác thực (`RequireAuthorization`) và kiểm tra quyền hạn theo bảng `workspace_members.role` (`Admin` | `Manager` | `Member`):

| Cấp độ quyền | Các thao tác được phép |
|---|---|
| **Non-Member (Người ngoài Workspace)** | Luôn nhận `404 Not Found` (bất biến bảo mật chống dò sự tồn tại của Workspace). |
| **`Member` trở lên** | Xem Workspace/Board/Column/Task/Label/Comment/Dashboard; tạo/sửa/di chuyển/xóa thẻ; bình luận & `@mention`; tìm kiếm xuyên bảng; rời Workspace. |
| **`Manager` hoặc `Admin`** | Tạo/sửa/xóa Board & Column; sắp xếp lại cột; tạo/xóa nhãn Workspace; mời thành viên mới, hủy lời mời; xóa bình luận của người khác. |
| **`Admin` (Workspace Admin)** | Đổi tên Workspace; xóa mềm Workspace; thay đổi vai trò thành viên; xóa thành viên; chuyển nhượng quyền sở hữu Workspace (`OwnerId`). |

---

## 2. Tổng hợp Điểm cuối HTTP (Minimal APIs)

| Nhóm nghiệp vụ | Điểm cuối chính |
|---|---|
| **Workspaces** | `GET|POST /api/workspaces` · `GET|PUT|DELETE /api/workspaces/{workspaceId}` · `POST /api/workspaces/{workspaceId}/transfer-ownership` · `GET /api/workspaces/{workspaceId}/activity` |
| **Members & Invitations** | `GET /api/workspaces/{workspaceId}/members` · `PUT|DELETE .../members/{userId}` · `POST .../members/leave` · `GET|POST .../invitations` · `DELETE .../invitations/{id}` · `GET|POST /api/invitations/{token}[/accept\|/decline]` |
| **Profile** | `GET|PUT /api/me/profile` (`displayName`, `avatarUrl`, `bio`, `jobTitle`, `dailyDigestEnabled`) |
| **Boards & Columns** | `GET|POST /api/workspaces/{workspaceId}/boards` · `GET|PUT|DELETE .../boards/{boardId}` · `GET|POST /api/boards/{boardId}/columns` · `PUT /api/boards/{boardId}/columns/reorder` · `PUT|DELETE /api/boards/{boardId}/columns/{columnId}` |
| **Tasks (`TN-123`)** | `GET|POST /api/boards/{boardId}/tasks` · `GET|PUT|DELETE /api/boards/{boardId}/tasks/{taskId}` · `PUT /api/boards/{boardId}/tasks/{taskId}/move` |
| **Labels & Comments** | `GET|POST /api/workspaces/{workspaceId}/labels` · `DELETE .../labels/{labelId}` · `POST|DELETE /api/tasks/{taskId}/labels[/{labelId}]` · `GET|POST /api/tasks/{taskId}/comments` · `PUT|DELETE /api/comments/{commentId}` |
| **Dashboard & Search** | `GET /api/workspaces/{workspaceId}/dashboard` (kèm `ProjectHealth.Compute`) · `GET /api/workspaces/{workspaceId}/tasks/search` (Keyset Pagination trên `(updated_at, id)`) |
| **SignalR Hub** | `WS /hubs/board` (`JoinBoard(boardId)`, `LeaveBoard(boardId)`) |

---

## 3. Đồng bộ Thời gian thực qua SignalR (`BoardHub`)

- Client kết nối `/hubs/board` và gọi `JoinBoard(boardId)` để tham gia nhóm `board-{boardId}` (máy chủ kiểm tra tư cách thành viên Workspace ở mỗi lần tham gia).
- Các sự kiện phát sóng sau khi giao dịch CSDL đã commit (`IBoardEventPublisher`):
  - `TaskCreated` / `TaskUpdated` (`TaskResponse` kèm `taskNumber`, `assigneeIsAiAgent`, `activeAgentRunId`, `externalLinks`)
  - `TaskMoved` (`{ taskId, fromColumnId, toColumnId, position }`)
  - `TaskDeleted` (`{ taskId }`)
  - `ColumnCreated` / `ColumnUpdated` (`ColumnResponse`) / `ColumnsReordered` / `ColumnDeleted`
  - `CommentAdded` (`CommentResponse`) / `CommentDeleted` (`{ commentId, taskId }`)
  - `AgentRunProgress` (tiến trình chạy của AI Agent Executor theo thời gian thực)

---

## 4. Cổng Giao tiếp Liên Phân hệ (Ports & Invariants)

Theo bất biến kiến trúc **Chiều phụ thuộc Một chiều (`Ai → Board`)**, phân hệ `Board` **tuyệt đối không** tham chiếu `TeamNexus.Modules.Ai`. Thay vào đó:

1. **Các Cổng do `Board` khai báo và `Ai` ghi đè trong DI (Port + Null-Default Override):**
   - `IActivityLogWriter` (`NullActivityLogWriter` → `ActivityLogWriter`): Ghi nhận các sự kiện `TaskCreated`, `TaskUpdated`, `TaskMoved`, `TaskCompleted`, `TaskDeleted`, `CommentAdded`, `WorkspaceCreated`, `MemberInvited`,...
   - `INotificationWriter` (`NullNotificationWriter` → `NotificationService`): Tạo thông báo `TaskAssigned`, `CommentMention`, `CommentOnTask`, `WorkspaceInvitation`, `MemberJoined`.
   - `IEmailGateway` (`NullEmailGateway` → `EmailGateway`): Gửi email lời mời tham gia Workspace.
   - `IAiAgentResolver` (`NullAiAgentResolver` → `WorkspaceAiAgentResolver`): Khởi tạo thành viên ảo `ai_agent` (bao gồm 4 nhân cách `general`, `scrum_master`, `tech_lead`, `qa_security`) và cột `is_clarification`.
2. **Các Cổng do `Board` cung cấp cho `Ai` sử dụng:**
   - `ITaskService` / `IColumnService` / `IBoardService`: Được các bộ `IAiActionApplier` gọi khi quản trị viên phê duyệt hoặc hoàn tác đề xuất AI.
   - `ITaskAutomation` (`TaskAutomationService`): Cổng hẹp chuyên dụng cho tự động hóa hệ thống (như GitHub Webhooks) thực hiện tạo thẻ công việc (`CreateTaskAsSystemAsync`), chuyển cột (`MoveTaskAsSystemAsync`), xóa mềm hoàn tác (`SoftDeleteTaskAsSystemAsync`) và gắn nhãn taxonomy.
3. **Ba Cờ Vai trò Đặc biệt trên `board_columns`:**
   - Mỗi bảng có tối đa 1 cột `is_done`, 1 cột `is_clarification` và 1 cột `is_in_review`.
   - Ba cờ này loại trừ tương hỗ (`(is_done::int + is_clarification::int + is_in_review::int) <= 1`). Khi thẻ chuyển vào cột `is_done`, `completed_at` tự động được gán mốc UTC hiện tại; khi rời cột `is_done`, `completed_at` được xóa về `null`.
