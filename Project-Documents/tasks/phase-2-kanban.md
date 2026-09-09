# Giai đoạn 2 – Kanban Core (CRUD + Real-time)

> **Mục tiêu:** Xây dựng CRUD cho Board/Column/Task, giao diện kéo-thả bằng `@dnd-kit`, tích hợp SignalR để đồng bộ trạng thái real-time giữa các client.
>
> **Công nghệ:** ASP.NET Core · EF Core (PostgreSQL) · SignalR · React + TypeScript · `@dnd-kit/core` · Ant Design

---

## 1. Database Migration (Giai đoạn 2 Schema)

### 1.1 Định nghĩa Entity (EF Core Code-First)

- [x] Định nghĩa entity `BoardColumn`:
  - Fields: `Id (Guid)`, `BoardId (FK → Board)`, `Name`, `Position (int)`, `CreatedAt`, `UpdatedAt`
  - Constraint: UQ `(BoardId, Position)` — thứ tự cột không trùng trong một board
- [x] Định nghĩa entity `BoardTask` (dùng tên `BoardTask` tránh xung đột `System.Threading.Tasks.Task`):
  - Fields: `Id (Guid)`, `BoardId (FK → Board)`, `ColumnId (FK → BoardColumn)`, `Title`, `Description`, `Position (int)`, `AssigneeId (FK → ApplicationUser, nullable)`, `DueDate (timestamptz, nullable)`, `Priority (enum: Low/Medium/High/Urgent, nullable)`, `CreatedBy (FK → ApplicationUser)`, `CreatedAt`, `UpdatedAt`, `CompletedAt (nullable)`, `DeletedAt (nullable)`
  - Index: `(BoardId, ColumnId, Position)` — phục vụ sắp xếp kéo-thả
- [x] Định nghĩa entity `Label`:
  - Fields: `Id (Guid)`, `WorkspaceId (FK → Workspace)`, `Name`, `Color (hex string)`, `CreatedAt`
  - Constraint: UQ `(WorkspaceId, Name)` — tên nhãn duy nhất trong workspace
- [x] Định nghĩa entity `TaskLabel` (junction):
  - Fields: `TaskId (FK → BoardTask)`, `LabelId (FK → Label)`
  - PK: composite `(TaskId, LabelId)`
- [x] Định nghĩa entity `TaskComment`:
  - Fields: `Id (Guid)`, `TaskId (FK → BoardTask)`, `AuthorId (FK → ApplicationUser)`, `Content`, `CreatedAt`, `UpdatedAt`, `DeletedAt (nullable)`
- [x] Thêm soft delete global query filter cho `Board`, `BoardColumn`, `BoardTask`, `TaskComment` trong `TeamNexusDbContext.OnModelCreating`
- [x] Cập nhật entity `Board` thêm `DeletedAt` + `UpdatedAt` nếu chưa có (Phase 1 đã có skeleton — không cần sửa)
- [x] Cấu hình Fluent API đầy đủ cho các entity mới (index, FK, constraints, `snake_case` naming)
- [x] Tạo migration: `dotnet ef migrations add Phase2KanbanSchema`
- [x] Chạy migrate: `dotnet ef database update`
- [x] Verify: kiểm tra schema sinh ra đúng cấu trúc (`board_columns`, `tasks`, `labels`, `task_labels`, `task_comments`)

---

## 2. Backend – Module Board

> Tạo module mới `src/Modules/Board/TeamNexus.Modules.Board`.

### 2.1 Cấu trúc Module

- [x] Tạo cấu trúc thư mục module:
  ```
  src/Modules/Board/TeamNexus.Modules.Board/
  ├── Endpoints/      ← Minimal API mappers (tương đương Controllers; nhất quán module Auth)
  ├── Services/       ← BoardService/ColumnService/TaskService/LabelService/CommentService + WorkspaceAccess
  ├── DTOs/
  └── BoardModule.cs  ← AddBoardModule() (đăng ký DI); hub (Hubs/) sẽ thêm ở §3
  ```
- [x] Đăng ký module vào `Program.cs` (`services.AddBoardModule()`, `app.MapBoardModuleEndpoints()`)

### 2.2 Board CRUD

- [x] **DTOs:** `CreateBoardRequest`, `UpdateBoardRequest`, `BoardResponse` (bao gồm danh sách columns)
- [x] **Service `IBoardService` / `BoardService`:**
  - `GetBoardsAsync(workspaceId, userId)` — lọc theo membership
  - `GetBoardByIdAsync(boardId, userId)` — trả full board với columns + tasks
  - `CreateBoardAsync(workspaceId, request, createdByUserId)`
  - `UpdateBoardAsync(boardId, request, userId)` — kiểm tra quyền
  - `DeleteBoardAsync(boardId, userId)` — soft delete
- [x] **Controller `BoardsController`** (`/api/workspaces/{workspaceId}/boards`):
  - `GET /` → `GetBoardsAsync` (yêu cầu Member+ của workspace)
  - `GET /{boardId}` → `GetBoardByIdAsync` (Member+; verify board thuộc workspace trong URL)
  - `POST /` → `CreateBoardAsync` (Manager/Admin)
  - `PUT /{boardId}` → `UpdateBoardAsync` (Manager/Admin)
  - `DELETE /{boardId}` → `DeleteBoardAsync` (Manager/Admin)
- [x] Verify Board CRUD hoạt động qua Scalar/Swagger + script API (32 check: 401/403/404/409, CSRF, CRUD)

### 2.3 Column CRUD

- [x] **DTOs:** `CreateColumnRequest`, `UpdateColumnRequest`, `ReorderColumnsRequest` (danh sách `{id, position}`), `ColumnResponse`
- [x] **Service `IColumnService` / `ColumnService`:**
  - `GetColumnsAsync(boardId)`
  - `CreateColumnAsync(boardId, request, userId)` — gán `position` tự động (cuối cùng)
  - `UpdateColumnAsync(columnId, request, userId)`
  - `ReorderColumnsAsync(boardId, reorderRequest, userId)` — cập nhật `position` hàng loạt trong transaction (2 pha chống vi phạm UQ)
  - `DeleteColumnAsync(columnId, userId)` — 409 nếu column còn task (kể cả soft-deleted, FK Restrict)
- [x] **Controller `ColumnsController`** (`/api/boards/{boardId}/columns`):
  - `GET /` → `GetColumnsAsync`
  - `POST /` → `CreateColumnAsync`
  - `PUT /{columnId}` → `UpdateColumnAsync`
  - `PUT /reorder` → `ReorderColumnsAsync`
  - `DELETE /{columnId}` → `DeleteColumnAsync`
- [x] Verify Column CRUD hoạt động

### 2.4 Task CRUD

- [x] **DTOs:** `CreateTaskRequest`, `UpdateTaskRequest`, `MoveTaskRequest` (`{columnId, position}`), `TaskResponse` (kèm `AssigneeName`, `Labels`, `CommentCount`)
- [x] **Service `ITaskService` / `TaskService`:**
  - `GetTasksAsync(boardId, columnId?)` — lọc theo board hoặc cột
  - `GetTaskByIdAsync(taskId)`
  - `CreateTaskAsync(boardId, columnId, request, createdByUserId)` — gán `position` cuối trong cột; validate `column.BoardId == boardId`
  - `UpdateTaskAsync(taskId, request, userId)` — update metadata (title, desc, assignee, due date, priority)
  - `MoveTaskAsync(taskId, request, userId)` — cập nhật `column_id` + `position`; validate column cùng board; set/clear `completed_at`
  - `DeleteTaskAsync(taskId, userId)` — soft delete
- [x] **Controller `TasksController`** (`/api/boards/{boardId}/tasks`):
  - `GET /` → `GetTasksAsync`
  - `GET /{taskId}` → `GetTaskByIdAsync`
  - `POST /` → `CreateTaskAsync`
  - `PUT /{taskId}` → `UpdateTaskAsync`
  - `PUT /{taskId}/move` → `MoveTaskAsync`
  - `DELETE /{taskId}` → `DeleteTaskAsync`
- [x] Verify Task CRUD và move task hoạt động

### 2.5 Label & Comment (MVP)

- [x] **Label:** `GET /api/workspaces/{workspaceId}/labels`, `POST`, `DELETE`; `POST /api/tasks/{taskId}/labels` (gán), `DELETE /api/tasks/{taskId}/labels/{labelId}` (gỡ)
- [x] **Comment:** `GET /api/tasks/{taskId}/comments`, `POST`, `PUT /{commentId}`, `DELETE /{commentId}` (soft delete; sửa/xoá = tác giả hoặc Manager+)
- [x] Verify label gán/gỡ, comment CRUD hoạt động

---

## 3. Backend – SignalR Real-time

> Tham chiếu: `02-tech-stack-decisions.md` §2.2 — group theo `boardId`, optimistic update, last-write-wins.

### 3.1 Thiết lập Hub

- [ ] Tạo `BoardHub : Hub` tại `Hubs/BoardHub.cs`:
  - Method `JoinBoard(string boardId)` → `Groups.AddToGroupAsync(connectionId, $"board-{boardId}")`
  - Method `LeaveBoard(string boardId)` → `Groups.RemoveFromGroupAsync(connectionId, $"board-{boardId}")`
- [ ] Map hub endpoint: `app.MapHub<BoardHub>("/hubs/board")` với Authorization (yêu cầu đăng nhập)
- [ ] Cấu hình CORS cho SignalR (cho phép credential từ frontend origin)
- [ ] Inject `IHubContext<BoardHub>` vào các service cần broadcast

### 3.2 Broadcast sự kiện từ Service

Sau mỗi thao tác thay đổi thành công, service gọi `IHubContext` broadcast đến group `board-{boardId}`:

- [ ] **Task events:**
  - `TaskCreated` → payload: `TaskResponse`
  - `TaskUpdated` → payload: `TaskResponse`
  - `TaskMoved` → payload: `{ taskId, fromColumnId, toColumnId, position }`
  - `TaskDeleted` → payload: `{ taskId }`
- [ ] **Column events:**
  - `ColumnCreated` → payload: `ColumnResponse`
  - `ColumnUpdated` → payload: `ColumnResponse`
  - `ColumnsReordered` → payload: danh sách `{ id, position }`
  - `ColumnDeleted` → payload: `{ columnId }`
- [ ] **Comment events:**
  - `CommentAdded` → payload: `CommentResponse`
  - `CommentDeleted` → payload: `{ commentId, taskId }`

### 3.3 Verify Real-time

- [ ] Mở 2 tab trình duyệt cùng board → thao tác ở tab A → tab B cập nhật ngay không cần reload
- [ ] Test group isolation: thao tác ở board A không ảnh hưởng client đang xem board B

---

## 4. Frontend – Kanban UI

### 4.1 Cài đặt & Cấu trúc

- [ ] Cài packages: `@dnd-kit/core`, `@dnd-kit/sortable`, `@dnd-kit/utilities`, `@microsoft/signalr`
- [ ] Tạo cấu trúc thư mục feature:
  ```
  src/features/board/
  ├── components/
  │   ├── BoardView.tsx
  │   ├── KanbanColumn.tsx
  │   ├── TaskCard.tsx
  │   ├── TaskDetailModal.tsx
  │   └── CreateColumnModal.tsx
  ├── hooks/
  │   ├── useBoard.ts
  │   └── useBoardHub.ts
  ├── services/
  │   └── boardApi.ts
  └── stores/
      └── boardStore.ts
  ```

### 4.2 API Service & State

- [ ] `boardApi.ts`: các hàm gọi API (getBoard, createTask, updateTask, moveTask, createColumn, reorderColumns, ...)
- [ ] `boardStore.ts` (Zustand): lưu `{ columns, tasks }`, action `applyEvent(event)` để cập nhật từ SignalR
- [ ] `useBoard` hook: fetch board data khi mount, expose actions

### 4.3 Drag & Drop (dnd-kit)

- [ ] Setup `DndContext` bao bọc `BoardView` với `sensors` (PointerSensor, KeyboardSensor)
- [ ] `KanbanColumn` dùng `useDroppable`; `TaskCard` dùng `useSortable`
- [ ] `onDragEnd` handler:
  - Xác định task và column đích + position mới
  - **Optimistic update:** cập nhật state local ngay lập tức
  - Gọi `PUT /api/boards/{boardId}/tasks/{taskId}/move` lên backend
  - Nếu request thất bại → **revert** state + hiển thị thông báo lỗi
- [ ] Hỗ trợ reorder trong cùng column và move sang column khác
- [ ] `DragOverlay` hiển thị ghost card khi đang kéo

### 4.4 SignalR Client (useBoardHub)

- [ ] Khởi tạo `HubConnection` khi mount `BoardView`:
  ```ts
  const connection = new HubConnectionBuilder()
    .withUrl("/hubs/board", { withCredentials: true })
    .withAutomaticReconnect()
    .build();
  ```
- [ ] Sau kết nối: `connection.invoke("JoinBoard", boardId)`
- [ ] Cleanup khi unmount: `connection.invoke("LeaveBoard", boardId)` → `connection.stop()`
- [ ] Đăng ký handler cho tất cả events → cập nhật `boardStore` qua `applyEvent(event)`
- [ ] **Tránh double-update:** bỏ qua event do chính client này tạo ra (dùng flag hoặc so sánh `connectionId`)
- [ ] Hiển thị trạng thái kết nối: badge nhỏ `Connected` / `Reconnecting` / `Disconnected`

### 4.5 UI Components

- [ ] **`BoardView`**: layout flex-row các `KanbanColumn` + nút "+ Thêm cột"
- [ ] **`KanbanColumn`**: header (tên + số task + menu đổi tên/xóa), danh sách `TaskCard` cuộn dọc
- [ ] **`TaskCard`**: title, priority badge (màu theo mức), assignee avatar, due date, label chips; click → mở modal
- [ ] **`TaskDetailModal`**: form đầy đủ (title, desc, assignee, due date, priority, labels) + tab Comments
- [ ] **`CreateColumnModal`**: input tên cột, submit
- [ ] Loading state & empty state (board không có cột, cột không có task)

### 4.6 Routing & Navigation

- [ ] Route `/workspaces/:workspaceId/boards` → trang danh sách board
- [ ] Route `/workspaces/:workspaceId/boards/:boardId` → `BoardView`
- [ ] Sidebar/breadcrumb điều hướng workspace → board

---

## 5. Kiểm thử & Hoàn thiện

### 5.1 Verify theo Checklist Roadmap

- [ ] CRUD đầy đủ: tạo/sửa/xóa Board, Column, Task qua UI
- [ ] Kéo-thả mượt: move task giữa columns, reorder trong cùng column; optimistic update + revert khi lỗi
- [ ] Real-time: 2 client cùng board → thay đổi ở A hiển thị ngay ở B không cần reload
- [ ] Group isolation: thao tác board A không broadcast sang client đang xem board B
- [ ] Auto-reconnect: mất mạng tạm thời → kết nối tự phục hồi, UI hiển thị trạng thái đúng

### 5.2 Edge Cases

- [ ] Move task sang column không cùng board → backend trả lỗi 400
- [ ] Xoá column còn task → xử lý rõ ràng (chặn + thông báo, hoặc cascade soft delete task)
- [ ] Nhiều user kéo task cùng lúc → last-write-wins, không crash
- [ ] JWT hết hạn khi SignalR đang kết nối → auto-reconnect lấy token mới qua Axios interceptor + `withCredentials`
- [ ] Board không có column → empty state có hướng dẫn tạo cột đầu tiên

### 5.3 Bổ sung (nếu còn thời gian)

- [ ] Ghi `activity_logs` cho các hành động task (TaskCreated, TaskMoved, TaskCompleted) — chuẩn bị dữ liệu cho Giai đoạn 5 AI Observer
- [ ] Filter task theo assignee / priority / label trên UI board
- [ ] Search task trong board

---

## 6. Checklist Hoàn thiện Giai đoạn 2

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 3.

- [ ] CRUD đầy đủ cho Board, Column, Task (tạo/sửa/xóa qua UI và API)
- [ ] Giao diện kéo-thả task giữa các column hoạt động mượt (optimistic update + revert khi lỗi)
- [ ] SignalR đồng bộ real-time: thay đổi ở client A phản ánh ngay ở client B (không cần reload)
- [ ] SignalR group theo `boardId`, chỉ broadcast đến đúng board
- [ ] Auto-reconnect khi mất kết nối hoạt động, UI hiển thị trạng thái kết nối
