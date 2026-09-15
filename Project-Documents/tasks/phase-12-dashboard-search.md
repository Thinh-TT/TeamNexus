# Giai đoạn 12 — Dashboard & Tìm kiếm (Kế hoạch chia task)

> **Trạng thái thi hành:** 🔄 **Backend XONG (§1, §2, §3 + P1/P2); frontend + CI bàn giao antigravity** (`tasks/phase-12-remaining-frontend-handover.md`).
> ✅ **P1/P2 Refactor** (`TaskReadHelpers`, `TimeProvider`) — hồi quy **76/76 PASS** ·
> ✅ **§1 Dashboard backend** — `DashboardApiTests` **14/14 PASS** ·
> ✅ **§2 Search backend** — `TaskSearchApiTests` **24/24 PASS** ·
> ✅ **§3 Mention backend** — `CommentMentionApiTests` **10/10 PASS** ·
> ✅ **§3.4 `kind` filter** — 4 test mới trong `NotificationTriggerApiTests` ·
> ⬜ **§4/§5/§6 Frontend** và **§7.2 CI** — đã viết note bàn giao.
>
> **Đo thật trên PostgreSQL 18 (Docker, cổng 5433 — xem §1.2):**
> `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` = **0 warning / 0 error** ·
> `dotnet test` = **Failed 0 / Passed 423 / Skipped 0 / Total 423** (⏱ 68 s) ·
> `dotnet ef migrations list` = **9** · `has-pending-model-changes` = **không có**.
>
> **Baseline trước giai đoạn này là 371** ⇒ `423 = 371 + 52` test mới
> (**14** dashboard + **24** search + **10** mention + **4** `kind`).
>
> **Nguồn:** `Project-Documents/03-roadmap.md` → *Giai đoạn 12: Dashboard & Tìm kiếm* (3 ô A/B/C).
> **Tiền đề đã merge:** Giai đoạn 7 (AI Agent Executor) · 8 (Test/CI/Deploy) · 9 (Đơn giản hoá Role) · 10 (Nâng cao Task & Workspace UX) · **11 (Quản lý Member & Profile)**.
> **Nhánh đề xuất:** `feat/phase12-dashboard-search`
> **Schema:** ⛔ **KHÔNG migration, KHÔNG bảng/cột/index mới.** Vẫn **9** migration (mới nhất `20260914105025_Phase11MemberProfile`). `has-pending-model-changes` phải tiếp tục trả *"No changes have been made to the model since the last migration."*
>
> **Baseline đo thật tại phiên lập kế hoạch (không suy đoán):**
> - `frontend/ npm test` → ✅ **360 passed / 0 failed** (60 file) · 110 s
> - `backend/ dotnet test` → `Failed 0 / Passed 134 / Skipped 237 / Total 371` ⚠️ *237 test bị SKIP vì chưa nối được DB thật — xem §1.2*
> - `src/TeamNexus.Persistence/Migrations` → **9**
> - `dotnet build TeamNexus.sln -m:1 -nr:false` → **0 warning / 0 error**

---

## 0. Mục tiêu & 3 ô hoàn thiện

Trang tổng quan workspace sau đăng nhập và khả năng tìm kiếm/lọc task nâng cao, cộng thêm `@mention` trong bình luận.

| # | Yêu cầu roadmap | Trạng thái đầu kỳ (đã khảo sát code) | Việc phải làm |
|---|---|---|---|
| **A** | **Dashboard tổng quan**: "Task của tôi" (sắp đến hạn, quá hạn, mới giao) · hoạt động gần đây · tóm tắt board (số task theo trạng thái) · cảnh báo AI Observer chưa đọc | **Chưa có gì.** `features/auth/pages/DashboardPage.tsx` (171 dòng) vẫn là **trang demo Phase 1**: xem `user`, ô nhập Workspace ID thủ công, 3 nút test RBAC `/auth/me` · `/manager/ping` · `/admin/ping`. **Không** có service / endpoint / hook / component thống kê nào | **§1** (backend) + **§4** (frontend) |
| **B** | **Tìm kiếm & Lọc task**: theo tên · assignee · label · priority · trạng thái · due date — phạm vi **workspace hoặc board đang xem** | **Board đang xem: ĐÃ CÓ — nhưng chỉ ở client.** `features/board/components/BoardView.tsx` 176–264: `searchQuery` khớp title/description/assigneeName/label + `priorityFilter`. Khoảng trống: (1) **không** lọc theo trạng thái/due date; (2) **không** lọc phía server; (3) **không** tìm xuyên board trong workspace (`GET /api/boards/{id}/tasks` chỉ trả 1 board) | **§2** (backend) + **§5** (frontend) |
| **C** | **@mention trong comment**: tag `@tên` (autocomplete) → kích hoạt thông báo cho người được tag — *"mở rộng `notifications` table"* | **Chưa có gì.** `CommentService.CreateCommentAsync` chỉ phát **1** row `CommentOnTask` cho **assignee** (`CommentService.cs` 94–97), kèm comment ghi rõ *"@mention … is Giai đoạn 12"*. Ô soạn bình luận là `Input.TextArea` thuần. `grep -i mention` toàn repo = **1 dòng comment** | **§3** (backend) + **§6** (frontend) |

**Ngoài 3 ô, 3 hạng mục kỹ thuật bắt buộc phát sinh (phát hiện khi khảo sát code):**

- **D** — **"Cảnh báo AI Observer chưa đọc" không tách được bằng API hiện có.** `GET /api/notifications` chỉ lọc `isRead` + `take`; `UnreadCount` trả về là **tổng của mọi loại** thông báo (Observer + agent + nghiệp vụ). Ô A ghi rõ *"cảnh báo AI Observer chưa đọc"* ⇒ phải **append** tham số `kind` (additive) — xem **D4** và **§3.4**.
- **E** — **`DashboardPage` đang là route gốc `/`** và phụ thuộc side effect *"`GET /api/workspaces` tự tạo workspace mặc định"*. "Trang chủ workspace" mới phải là **route riêng** `/workspaces/:workspaceId/dashboard` (đúng pattern `/reports`, `/members`, `/activity`) để **không** phá hành vi đang được test.
- **F** — **Tái sử dụng, không viết lại.** Đã có sẵn và **phải** dùng: 3 helper tải labels / comment-count / agent-run của `TaskService` (485–519 + `AgentRunLookup`), `IWorkspaceAccess.RequireMemberAsync`, `DtoMapping.MapLabel`, `features/workspace/utils/activityLabels.ts`, `features/board/utils/taskDueDate.ts`, `shared/hooks/useWorkspaceMembers`, `shared/components/AppHeader`, `features/ai/services/notificationApi` + `NotificationDrawer`, `shared/hooks/useWorkspaceRole`. Bằng chứng file·dòng đầy đủ ở **§1.1 "Đã có sẵn — KHÔNG làm lại"**.

---

## 1. Baseline & bối cảnh đã khảo sát

### 1.1 Đã có sẵn — **KHÔNG làm lại** (bằng chứng trong repo)

| Hạng mục | Bằng chứng (file · dòng) |
|---|---|
| `tasks` có `due_date` / `priority` / `completed_at` / `deleted_at` / `assignee_id` + **query filter** ẩn task của **board soft-deleted** | `Persistence/Data/Entities/BoardTask.cs` 20–53 · `Configurations/BoardTaskConfiguration.cs` 55–58 |
| Index sẵn có: `ix_tasks_assignee_id`, `ix_tasks_board_id_column_id_position`, `ix_tasks_column_id`, `ix_tasks_created_by` | `Migrations/20260909120507_Phase2KanbanSchema.cs` 187–203 · `04-database-design.md` §5 |
| `board_columns.is_done` (cột "Done" để suy ra task đã xong) | `Migrations/20260909122823_Phase2BoardColumnIsDone.cs` · `ReportAggregator.IsDone` 327 |
| `activity_logs` + IX `(workspace_id, created_at)`; **không** soft-delete, **không** query filter | `Entities/ActivityLog.cs` 5–45 · `Migrations/20260910105154_Phase5AiObserverSchema.cs` 121 |
| `WorkspaceActivityService` — mẫu **keyset cursor** + `LeftJoin` lấy tên actor + `Take(pageSize + 1)` | `Board/Services/WorkspaceActivityService.cs` 69–142 (đặc biệt `ParseCursor` 166–195) |
| `IWorkspaceAccess.RequireMemberAsync` (**404** người ngoài) / `RequireManagerAsync` (**403**) / `IsAtLeast` | `Board/Services/WorkspaceAccess.cs` 12–27, 38–64 |
| `DtoMapping.MapTask` / `MapLabel` | `Board/Services/DtoMapping.cs` 20–47 |
| `TaskService` 3 khối tải theo lô: `LoadLabelsByTaskAsync` · `LoadCommentCountsAsync` · `ResolvePageAssigneeIsAiAgentAsync` (**không N+1**) | `Board/Services/TaskService.cs` 397–412, 485–519 |
| `TaskService.ParsePriority` — `IsNumericString` + `Enum.IsDefined` (đã sửa bẫy BUG-1 Phase 10) + `ToUtc(...)` | `TaskService.cs` 521–577, 591–592 |
| `ef.Functions.ILike` + `%query%` (tiền lệ tìm kiếm phía server, không unaccent) | `Ai/Services/Agent/Agents/SearchSystemDataTool.cs` 86–90 |
| `INotificationWriter` + `MemberNotificationTypes` + `MemberNotificationLimits` (**port của Board, adapter ở Ai**) | `Board/Services/INotificationWriter.cs` 15–105 |
| `NotificationService.NotifyUsersAsync` (1 row/người nhận, loại trùng, cap 100, **không bao giờ ném**) | `Ai/Services/NotificationService.cs` 302–344 |
| `NotificationService.ListAsync` đã scoped `RecipientUserId`; `CountUnreadAsync` | `Ai/Services/NotificationService.cs` 236–290 |
| 3 vocabulary notification **đã tách sẵn** (Observer whitelist · agent · member) | `ObserverSeverity.cs` 83–99 · `Agent/AgentConstants.cs` 61–71 · `Board/Services/INotificationWriter.cs` 67–81 |
| `CommentService.CreateCommentAsync` + `NotifyAssigneeAsync` (1 row `CommentOnTask`) | `Board/Services/CommentService.cs` 61–142 |
| `DomainExceptionFilter` (`{ error }` + status) + `BoardModuleException` (`BadRequest`/`NotFound`/`Forbidden`/`Conflict`/`Gone`/`TooManyRequests`) | `Board/Endpoints/DomainExceptionFilter.cs` 11–24 · `Board/Services/DomainExceptions.cs` 9–88 |
| FE: `AppHeader` (title · children · bell · dropdown) + `NotificationBell` + `useUnreadCount` (poll 60 s, fail-soft) | `shared/components/AppHeader.tsx` · `NotificationBell.tsx` · `shared/hooks/useUnreadCount.ts` |
| FE: `activityLabels.ts` (nhãn tiếng Việt cho `activity_logs.action`) | `features/workspace/utils/activityLabels.ts` |
| FE: `taskDueDate.ts` — `isOverdue` (so theo **ngày**) · `overdueDays` · `dueDateLabel` (`'Quá hạn N ngày' \| 'Hôm nay' \| 'DD/MM'`) | `features/board/utils/taskDueDate.ts` 4–55 |
| FE: `useWorkspaceMembers` / `useWorkspaceRole` / `workspaceApi.getActivity` (build `URLSearchParams` bỏ tham số rỗng) | `features/board/hooks/…` · `shared/hooks/useWorkspaceRole.ts` · `features/workspace/services/workspaceApi.ts` 45–76 |
| FE: `ReportsPage` đồng bộ filter lên **URL** qua `useSearchParams` (`replace: true`) | `features/reporting/pages/ReportsPage.tsx` 31–56 |
| antd **6.6.3 đã có `Mentions`** (export tên `Mentions`) + `Mentions.getMentions` | `frontend/node_modules/antd/es/index.js:42` · `es/mentions/index.d.ts` 47–66 |
| Hook pattern của repo: **useState + useEffect + reload**, **không** react-query | `useWorkspaceActivity.ts` 22–51 · `useReportSummary.ts` 29–91 |

### 1.2 Baseline **đo tại máy dev** (phiên lập kế hoạch)

```
frontend/  npm test -- --reporter=dot   → 360 passed / 0 failed (60 file) · 110.18 s
backend/   dotnet test (KHÔNG có DB)    → Failed 0 / Passed 134 / Skipped 237 / Total 371
backend/   dotnet build TeamNexus.sln -m:1 -nr:false → 0 warning / 0 error
src/TeamNexus.Persistence/Migrations/*.cs → 9 (mới nhất 20260914105025_Phase11MemberProfile)
```

> ⚠️ **Phát hiện mới về môi trường (khác ghi chú Phase 10/11):**
> 1. **PostgreSQL 18 ĐANG chạy** — service `postgresql-x64-18` Running, cổng **5432 mở**.
> 2. Nhưng **mật khẩu `postgres` KHÔNG đúng**:
>    `psql -h localhost -U postgres` ⇒ `FATAL: password authentication failed for user "postgres"`.
>    `DatabaseFixture.DefaultConnectionString` dùng đúng cặp `postgres/postgres`
>    (`Infrastructure/DatabaseFixture.cs` 39–40) ⇒ **237 test phụ thuộc DB bị SKIP**.
> 3. **Docker daemon ĐANG chạy** (`docker info` OK, chưa có container nào). Cổng **5433** và **55432** đều trống.
>
> **⇒ Cách chạy đủ bộ 100% test backend (bắt buộc trước khi push):**
> ```powershell
> docker run -d --name teamnexus-test-pg -e POSTGRES_PASSWORD=postgres -p 5433:5432 postgres:18
> $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"
> dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
> ```
> Nếu biết mật khẩu Postgres local thật thì đặt `TEAMNEXUS_TEST_DB` trỏ thẳng vào nó — **không** cần Docker.
> **Điều kiện chốt: `Skipped: 0`.** Nếu `total` lệch 371 ⇒ **số đo thắng tài liệu**: cập nhật §1.2 + §7.1 **và** cổng CI §7.2.

---

## 2. Bảng quyết định kiến trúc (D1–D13) — chốt sẵn, không chọn lại

| # | Quyết định | Lý do / ràng buộc |
|---|---|---|
| **D1** | **KHÔNG migration, KHÔNG bảng/cột/index mới.** Vẫn **9** migration | Cả 3 ô đều đọc dữ liệu đã có. `@mention` dùng cột `notifications.type` (**text tự do**, varchar(32) — `"CommentMention"` = 14 ký tự) + `notifications.payload` (jsonb) ⇒ **đúng tiền lệ Phase 10** (Activity Log dùng bảng có sẵn, "không cần migration"). Index `(assignee_id, due_date)` là tối ưu **chưa có bằng chứng** ⇒ **không** làm (ghi nợ kỹ thuật — §6 R5) |
| **D2** | Thêm **2 service mới vào module Board**, **KHÔNG** tạo module mới: `DashboardService`, `TaskSearchService` | Board đã sở hữu `tasks` / `board_columns` / `boards` / `activity_logs` + `IWorkspaceAccess`. Tạo `Modules/Dashboard` kéo theo cạnh phụ thuộc mới và phải di chuyển code ⇒ **đúng lập luận D11 của Phase 11** (tách module vì tên gọi, rủi ro không tương xứng) |
| **D3** | **KHÔNG thêm port mới.** `DashboardService` **không** đọc `ai_observer_runs`; "cảnh báo AI Observer chưa đọc" do **frontend** lấy qua `GET /api/notifications?kind=observer&isRead=false` | Bảng `notifications` + endpoint **đã tồn tại** và **đã scoped theo người gọi** (`ListAsync` lọc `RecipientUserId`). Thêm port chỉ để đếm là thừa. Giữ nguyên hướng phụ thuộc **Board ↛ Ai** |
| **D4** | **Append** tham số query `kind` (`observer` \| `agent` \| `member`; vắng = tất cả) vào `GET /api/notifications`; **`UnreadCount` đếm theo cùng bộ lọc** | Không thể trả số badge đúng nếu lọc ở client. Ba vocabulary **đã tách sẵn** (`NotificationTypes.All` · `AgentNotificationTypes` · `MemberNotificationTypes`) ⇒ ánh xạ 1-1, **không** đụng `NotificationTypes.All` (whitelist chống hallucination của Observer — Phase 11 D10). Additive: tham số vắng ⇒ hành vi **y hệt** hôm nay |
| **D5** | Endpoint dashboard **theo workspace**: `GET /api/workspaces/{workspaceId}/dashboard` — **Member+** (404 người ngoài) | Không có endpoint `/api/dashboard` toàn cục: mọi số liệu phải scope workspace (tiền lệ `WorkspaceActivityService`) |
| **D6** | **"Task của tôi" = task có `assignee_id = người gọi`**, chỉ trong **board sống** | Query filter của `tasks` đã ẩn task của chính nó **và** của board soft-deleted (`BoardTaskConfiguration` 55–58) ⇒ không cần join thêm, nhưng **phải** giới hạn bằng `boardIds` của workspace. `user_id` lấy từ JWT (`http.RequireUserId()`) — **không** có `{userId}` trên path ⇒ **không thể** xem dashboard người khác |
| **D7** | **Ngữ nghĩa thời gian chốt (một nguồn duy nhất, qua `TimeProvider`):** `now = TimeProvider.GetUtcNow()`. **Quá hạn** = `!isDone && dueDate < now`. **Sắp đến hạn** = `!isDone && now <= dueDate <= now + dueSoonDays` (mặc định **3**, tham số `days` clamp `[1,30]`). **Mới giao** = `!isDone && createdAt >= now - 7 ngày`. `isDone` = *ở cột `is_done`* **HOẶC** `completed_at != null` | Khớp **chính xác** `ReportAggregator.IsDone`/`IsOverdue` (Phase 6 §1.3) — nếu lệch, Dashboard và Báo cáo sẽ nói 2 số khác nhau cho **cùng một workspace**. **Lưu ý:** FE `taskDueDate.isOverdue` so theo **ngày** (hạn hôm nay = chưa quá hạn) ⇒ dashboard trả `overdueByDays` từ **server**, FE **không** tự tính lại |
| **D8** | **KHÔNG** sửa `GET /api/boards/{boardId}/tasks` (giữ **nguyên** shape `TaskResponse[]` + `?columnId=`) | `useBoard`, `boardStore`, `BoardView` + 4 file test đang parse **mảng** đó. Tìm kiếm xuyên board dùng route **mới**: `GET /api/workspaces/{workspaceId}/tasks/search` |
| **D9** | Search: **cursor keyset trên `(updated_at DESC, id DESC)`**, `take` mặc định **25**, trần **100**, `hasMore = take + 1` | Thứ tự `(updated_at DESC, id DESC)` — `updated_at` **non-null** ⇒ **không** có bài toán NULLS LAST như sắp theo `due_date`. Cursor = `"{updatedAt:O}\|{id:D}"`, hỏng ⇒ **400** (đúng tiền lệ `WorkspaceActivityService.ParseCursor`) |
| **D10** | `q` khớp **`title` HOẶC `description`** bằng `EF.Functions.ILike` (case-insensitive); rỗng/whitespace ⇒ **bỏ** điều kiện; > 200 ký tự ⇒ **400** | Đúng tiền lệ `SearchSystemDataTool` (`ILike` + `%…%`). **Không** dùng `unaccent`/full-text: 0 dependency mới (hợp đồng "⛔ không thêm package" của Phase 10 §10) |
| **D11** | **@mention nhận `mentionUserIds` TƯỜNG MINH trong body**; server **KHÔNG** parse `@tên` từ text | Parse tên ở server = **mạo danh được** (`xem @An` với `An` bất kỳ) + tên hiển thị có dấu cách ⇒ nhập nhằng. Server vẫn kiểm: id phải là **member `human`** của workspace (lệch ⇒ **400**), loại trùng, **loại tác giả**, **loại AI Agent** ⇒ **không** thêm port, chỉ dùng `INotificationWriter` sẵn có |
| **D12** | UI mention: thay `Input.TextArea` ở ô **soạn** bình luận bằng **`Mentions` của antd v6**, `prefix="@"`, `options.value` = **displayName**; khi submit, `extractMentionUserIds(text, members)` quy đổi tên → `userId` phía **client** | `antd@6.6.3` **đã có sẵn** (đã kiểm `es/index.js:42` + `es/mentions/index.d.ts`) ⇒ **không** thêm thư viện npm (D13 Phase 11). Text lưu trữ **vẫn là** `@Tên Hiển Thị` thuần (không markdown link, **không** đổi shape `CommentResponse`). Hiển thị bình luận **giữ nguyên** dạng text — **không** `dangerouslySetInnerHTML` |
| **D13** | `CreateCommentRequest` → **`(string Content, IReadOnlyList<Guid>? MentionUserIds = null)`** (field **cuối**, default null) | Mọi caller cũ (test, `PostCommentApplier` của AI) vẫn compile. Logic cũ (1 row `CommentOnTask` cho assignee) **giữ nguyên hoàn toàn**; mention là row **bổ sung**, tự loại trùng với assignee |

### 2.1 Phát sinh **bắt buộc** khi hiện thực (ghi lại để không bị coi là sai lệch kế hoạch)

| # | Phát sinh | Vì sao |
|---|---|---|
| **P1** | Trích **`TaskReadHelpers`** (static, `internal`, Board/Services) cho 3 khối tải labels / comment-count / `assigneeIsAiAgent` đang nằm **private** trong `TaskService` | `TaskSearchService` cần **đúng 3 khối đó**; nếu không ⇒ N+1 hoặc copy 40 dòng. Đây là **extract method**: hành vi và SQL của `TaskService` **không đổi** (điều kiện: `dotnet test` vẫn 371 trước khi thêm test mới) |
| **P2** | `TimeProvider` resolve trong `DashboardService`; `AddBoardModule` đăng ký `services.AddSingleton(TimeProvider.System)` | Để test xác định được **biên** "quá hạn / sắp đến hạn" mà **không** dùng `DateTime.Now` — đúng lập luận "hàm thuần, tất định" của `ReportAggregator` |
| **P3** | `DashboardService` cần **tên board** + **tên cột** cho từng task ⇒ dùng `LeftJoin` sang `boards`/`board_columns` (không navigation property) | Đúng tiền lệ `WorkspaceActivityService` (`LeftJoin` + actor name). Trả ở `DashboardTaskItem.{BoardName, ColumnName}` để card đọc được |
| **P4** | DTO **riêng** `TaskSearchItem { Task, BoardName, ColumnName, IsDoneColumn }` **thay vì** sửa `TaskResponse` | Thêm field vào `TaskResponse` buộc sửa `DtoMapping.MapTask` + 7 call site trong `TaskService`, **và** là "đổi shape hợp đồng đã verify" (⛔ §6). DTO riêng bảo toàn hợp đồng cũ, đồng thời cho search UI biết cột nào `is_done` (cần cho nhãn "Đã xong") |
| **P5** | `RecentActivities` của dashboard **luôn trả `payload = null`** | `payload` là jsonb tùy ý (`assigneeId`, `columnId`, `titleChanged`…). Trả nguyên payload cho **mọi thành viên** là mở rộng bề mặt dữ liệu ngoài yêu cầu. FE chỉ cần `action` + `authorName` + `entityType` để hiện nhãn tiếng Việt qua `activityLabels.ts` |
| **P6** | Frontend **GIỮ** `GET /api/workspaces` trong `DashboardPage` gốc | Side effect "tự tạo workspace mặc định" mà `DashboardPage` phụ thuộc (Phase 11 §7 ghi rõ `UserProfileService.ListMyWorkspacesAsync` **cố tình không** tái dùng `WorkspaceService.ListForUserAsync` vì lý do này). Route mới `/workspaces/:id/dashboard` **không** gọi lại endpoint tạo hộ đó |

### 2.2 Quyết định **chốt khi hiện thực** (khác/ bổ sung bản kế hoạch đầu — ghi lại để không bị coi là sai lệch)

| # | Quyết định | Vì sao |
|---|---|---|
| **P7** | **`dueSoon` LOẠI TRỪ `overdue`** (bản kế hoạch đầu nói ngược lại) | 3 tab là một **phân hoạch theo mức khẩn cấp** ("đã trễ" / "sắp tới hạn" / "vừa được giao"); task vừa bị server gọi là quá hạn mà hiện tiếp ở "sắp đến hạn" thì dashboard tự mâu thuẫn. Task **vẫn có thể** nằm ở cả "Quá hạn" và "Mới giao" (2 lát cắt độc lập) |
| **P8** | **`days=0` clamp về 1** (không quay về mặc định 3) | `Clamp(value, fallback, min, max)` coi "ngoài khoảng" là clamp; chỉ `null` mới dùng mặc định — đúng tiền lệ `WorkspaceActivityService.ClampTake` |
| **P9** | Search **validate `assigneeId` phải là thành viên workspace ⇒ 400** | Không task nào có thể khớp một người ngoài workspace ⇒ trả trang rỗng sẽ trông y hệt "không có kết quả", không phân biệt được với lỗi gõ. Cùng lý do với `labelIds` |
| **P10** | `kind=member` gồm **4** type (có `CommentMention`), không phải 3 | Mention là thông báo nghiệp vụ hướng thành viên; nếu để ngoài `member` thì ô "Trung tâm thông báo" không lọc được nó |
| **P11** | Test Phase 11 `TheAlertsArePrivateAndReadableOnlyByTheirRecipient` **phải sửa** | Lần chạy đầu tiên với DB thật phát hiện **lỗi harness tiềm ẩn**: sau `AsUserAsync(manager)`, mọi client tạo trước đó (**kể cả `memberClient`**) đã xác thực **là manager** vì `TestScenario` dùng **chung một cookie jar**. Test cũ chỉ đăng nhập lại phía Manager mà quên phía member ⇒ POST cuối nhận **404** thay vì **200**. Sửa bằng đăng nhập lại **cả hai** phía. **Đây là test cũ duy nhất phải sửa** — ghi vào báo cáo theo yêu cầu §9 |
| **P12** | `TestScenario.CreateScenarioAsync(scriptedAi, clock)` nhận thêm `clock`; có `clock`/`scriptedAi` ⇒ **host riêng**, không có ⇒ host chung. Thêm `CreateClockScenarioAsync(clock)` + `TeamNexusApiFactory.Clock` | Bucket của dashboard định nghĩa bằng **biên thời gian**; assert biên với đồng hồ thật là flaky theo cấu trúc (request chạy sau khi test tính `UtcNow` vài ms). Đúng tinh thần "hàm tất định" của `ReportAggregator` |
| **P13** | Trong test, `created_at`/`updated_at` phải `UPDATE` **sau** `SaveChanges` | `TeamNexusDbContext.StampAuditableTimestamps` đặt cả hai cột bằng **đồng hồ thật** cho mọi `Added` `IAuditableEntity` (chỉ ghi khi `Added`/`Modified`, **không** khôi phục giá trị caller đặt) ⇒ mọi test cửa sổ thời gian sẽ xanh vì **lý do sai**. Đã ghi rõ trong doc comment của helper seed |

---

## 3. §1 + §2 + §3.4 — Backend — ✅ **XONG**

### 3.1 File **mới** (đã tạo)

| File | Nội dung |
|---|---|
| `Board/DTOs/DashboardDtos.cs` | `DashboardTaskItem`, `DashboardTaskBucket`, `DashboardBoardSummary`, `DashboardBoardColumnCount`, `DashboardActivityItem`, `DashboardSummary`, `DashboardResponse` (shape chốt ở §3.3) |
| `Board/DTOs/TaskSearchDtos.cs` | `TaskSearchItem(TaskResponse Task, string BoardName, string ColumnName, bool IsDoneColumn)`, `TaskSearchResponse(IReadOnlyList<TaskSearchItem> Items, string? NextCursor, bool HasMore, bool HasQuery)` |
| `Board/Services/DashboardService.cs` | `IDashboardService.GetAsync(workspaceId, userId, days, take, ct)` — `RequireMemberAsync` rồi 6 truy vấn **có bound** (§3.3) |
| `Board/Services/ITaskSearchService.cs` | `ITaskSearchService.SearchAsync(TaskSearchRequest, Guid userId, ct)` + record **public** `TaskSearchRequest` (phải public vì xuất hiện trong signature của interface — **CS0051**) |
| `Board/Services/TaskSearchService.cs` | Cài đặt: scope board/label/assignee → dựng `IQueryable` → keyset `(updated_at DESC, id DESC)` + rank theo vị trí khớp title → nạp labels/comment/agent-run theo lô cho **trang hiện tại** |
| `Board/Services/TaskReadHelpers.cs` (**P1**) | `internal static` — `LoadLabelsByTaskAsync`, `LoadCommentCountsAsync`, `ResolveAssigneeIsAiAgentAsync` (move nguyên SQL từ `TaskService`) |
| `Board/Endpoints/DashboardEndpoints.cs` | `GET /api/workspaces/{workspaceId:guid}/dashboard?days=&take=` — `RequireAuthorization` + `DomainExceptionFilter` |
| `Board/Endpoints/TaskSearchEndpoints.cs` | `GET /api/workspaces/{workspaceId:guid}/tasks/search` + 11 tham số (§3.4) — parse/validate tại handler, `RequireAuthorization` + `DomainExceptionFilter` |
| `Ai/Services/NotificationVocabulary.cs` | Ánh xạ `kind` → 3 tập type (Observer · agent · member) + `Parse` ném **400** cho giá trị lạ. **Không** sửa `NotificationTypes.All` |
| `tests/…/Infrastructure/FixedTimeProvider.cs` | `TimeProvider` đóng băng tại một mốc, cho suite dashboard |
| `tests/…/Integration/DashboardApiTests.cs` | **D-1 … D-14** — ✅ **14/14 PASS** |
| `tests/…/Integration/TaskSearchApiTests.cs` | **S-1 … S-16** (24 method do có `[Theory]`) — ✅ **24/24 PASS** |
| `tests/…/Integration/CommentMentionApiTests.cs` | **M-1 … M-10** — ✅ **10/10 PASS** |

### 3.2 File **sửa** (đã sửa)

| File | Thay đổi |
|---|---|
| `Board/Endpoints/BoardEndpoints.cs` | +`endpoints.MapDashboardEndpoints(); endpoints.MapTaskSearchEndpoints();` |
| `Board/BoardModule.cs` | +`IDashboardService`, `ITaskSearchService`, `services.TryAddSingleton(TimeProvider.System)` (**P2**) |
| `Board/Services/TaskService.cs` | **Chỉ thay 3 khối private bằng gọi `TaskReadHelpers`** (**P1**). Hành vi/SQL **không đổi** |
| `Board/DTOs/CommentDtos.cs` | `CreateCommentRequest(string Content, IReadOnlyList<Guid>? MentionUserIds = null)` (**D13**) |
| `Board/Services/CommentService.cs` | +`ResolveMentionedMembersAsync` (validate **trước khi ghi**) +`NotifyMentionedAsync` (sau `NotifyAssigneeAsync`); **giữ nguyên** `NotifyAssigneeAsync` |
| `Board/Services/INotificationWriter.cs` | +`MemberNotificationTypes.CommentMention = "CommentMention"`; +`MemberNotificationLimits.MaxMentionedUsers = 20` |
| `Ai/Services/MemberNotificationTypes.cs` | +`CommentMention = Board.Services.MemberNotificationTypes.CommentMention` (alias, đúng tiền lệ 3 hằng đang có) |
| `Ai/Services/NotificationService.cs` | `ListAsync(Guid userId, bool? isRead, IReadOnlyList<string>? kind, int take, ct)`; **`UnreadCount` tính theo CÙNG bộ lọc** (**D4**) |
| `Ai/Endpoints/NotificationEndpoints.cs` | +bind `string? kind`; `NotificationVocabulary.Parse(kind)` ⇒ giá trị lạ **400** |
| `Ai/DTOs/NotificationDtos.cs` | **KHÔNG đổi** — `NotificationResponse` / `NotificationListResponse` giữ nguyên shape |
| `tests/…/Infrastructure/TeamNexusApiFactory.cs` | +property `Clock` (`TimeProvider?`, qua `ConfigureTestServices`) (**P12**) |
| `tests/…/Infrastructure/DatabaseFixture.cs` | `CreateScenarioAsync(scriptedAi, clock)`; có DI-override ⇒ **host riêng**, không có ⇒ host chung; +`CreateClockScenarioAsync(clock)` (**P12**) |
| `tests/…/Integration/NotificationTriggerApiTests.cs` | +4 test `kind` (N-13…N-16) + **sửa 1 test Phase 11** (**P11** — ghi rõ trong báo cáo) |

### 3.3 Hợp đồng API backend **chốt** (frontend dùng đúng, không đoán)

#### A. Dashboard

```http
GET /api/workspaces/{workspaceId}/dashboard?days=3&take=10      (Member+ · 404 người ngoài)
```

```ts
interface DashboardTaskItem {
  id: string; boardId: string; columnId: string; title: string
  boardName: string; columnName: string
  dueDate: string | null                 // ISO
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  createdAt: string
  assigneeId: string | null
  isDone: boolean                        // ở cột is_done HOẶC có completed_at
  overdueByDays: number | null           // >= 1 khi quá hạn; null khi không
}
interface DashboardTaskBucket {
  count: number                          // TỔNG THẬT (không bị `take` cắt)
  items: DashboardTaskItem[]             // tối đa `take`
}
interface DashboardBoardColumnCount { columnId: string; name: string; isDone: boolean; count: number }
interface DashboardBoardSummary {
  boardId: string; name: string
  total: number; done: number; open: number; overdue: number
  columns: DashboardBoardColumnCount[]
}
interface DashboardActivityItem {
  id: string; boardId: string | null; entityType: string; action: string
  authorName: string | null; createdAt: string
  payload: null                          // LUÔN null (P5)
}
interface DashboardSummary {
  totalTasks: number; doneTasks: number; openTasks: number; overdueTasks: number; myOpenTasks: number
}
interface DashboardResponse {
  workspaceId: string; workspaceName: string
  utcNow: string                         // mốc thời gian server dùng để tính (audit/UI)
  dueSoonDays: number
  myTasks: {
    overdue: DashboardTaskBucket
    dueSoon: DashboardTaskBucket
    recentlyAssigned: DashboardTaskBucket
  }
  boards: DashboardBoardSummary[]        // tối đa 20 board
  boardsTruncated: boolean                // true khi workspace có > 20 board
  recentActivities: DashboardActivityItem[]   // tối đa 10, mới nhất trước
  summary: DashboardSummary
}
```

**Thứ tự sắp xếp chốt:** `overdue` sắp `dueDate ASC` (quá hạn lâu nhất trước) → `title ASC` → `id ASC`; `dueSoon` sắp `dueDate ASC`; `recentlyAssigned` sắp `createdAt DESC`.
**`count` là tổng thật**, `items` bị `take` cắt ⇒ UI luôn hiển thị **số đúng** ở tiêu đề tab.
**Một task CÓ THỂ xuất hiện ở nhiều bucket** (vừa "quá hạn" vừa "mới giao") — **cố ý**: đó là 3 lát cắt khác nhau trên cùng một tập.
**`dueSoon` LOẠI TRỪ `overdue`** (chốt khi hiện thực §1 — bản kế hoạch đầu nói ngược lại): 3 tab là một **phân hoạch theo mức khẩn cấp** ("đã trễ" / "sắp tới hạn" / "vừa được giao"); nếu task vừa bị server gọi là quá hạn lại hiện tiếp ở tab "sắp đến hạn" thì dashboard tự mâu thuẫn. Vì vậy `summary.overdue` + `dueSoon.count` không cộng lại thành một con số có nghĩa — mỗi tab là một câu hỏi riêng.

#### B. Search

```http
GET /api/workspaces/{workspaceId}/tasks/search
    ?q=&boardId=&assigneeId=&unassigned=&labelIds=&priority=&dueFrom=&dueTo=&overdue=&includeDone=&take=&cursor=
     (Member+ · 404 người ngoài)
```

| Tham số | Kiểu | Luật chốt |
|---|---|---|
| `q` | string? | trim; rỗng ⇒ **bỏ** điều kiện; **> 200 ký tự ⇒ 400**; khớp `title` **hoặc** `description` (`ILike`) |
| `boardId` | guid? | board phải thuộc workspace, nếu không ⇒ **404** |
| `assigneeId` | guid? | user phải là member của workspace ⇒ **400**; lọc đúng `assignee_id` |
| `unassigned` | bool? | `true` ⇒ **chỉ** task `assignee_id IS NULL` (bỏ qua `assigneeId`) |
| `labelIds` | string? (CSV guid) | **AND**: task phải có **mọi** nhãn liệt kê; id lạ ⇒ **400**; **> 10 id ⇒ 400** |
| `priority` | string? | đúng 4 tên `Low/Medium/High/Urgent`; **`Enum.IsDefined` + `IsNumericString`** ⇒ `"1"`/`"99"`/`"Boss"` ⇒ **400** (bẫy BUG-1 Phase 10) |
| `dueFrom` / `dueTo` | DateTimeOffset? | **`.ToUniversalTime()`** (bẫy offset Npgsql Phase 10); `dueFrom > dueTo` ⇒ **400** |
| `overdue` | bool? | `true` ⇒ `!isDone && dueDate < now` |
| `includeDone` | bool? | **mặc định `true`** (task ở cột done vẫn hiện); `false` ⇒ loại task done |
| `take` | int? | mặc định **25**, clamp `[1,100]` (**không** 400 — đây là knob UI) |
| `cursor` | string? | `"{updatedAt:O}\|{id:D}"`; hỏng/rác ⇒ **400** (không 500, không im lặng về trang 1) |

```ts
interface TaskSearchItem { task: TaskResponse; boardName: string; columnName: string; isDoneColumn: boolean }
interface TaskSearchResponse { items: TaskSearchItem[]; nextCursor: string | null; hasMore: boolean; hasQuery: boolean }
```

> `hasQuery = (q có nội dung)` — cho UI phân biệt **"chưa nhập từ khoá"** với **"không tìm thấy kết quả"**.
> **KHÔNG** trả `total` (keyset không đếm) — UI hiện "Hiển thị N kết quả" + nút **"Tải thêm"**.
> `task` dùng **nguyên** `TaskResponse` ⇒ search UI render được bằng chính field mà `TaskCard` đang dùng.

#### C. Notifications — tham số `kind` (append)

```http
GET /api/notifications?isRead=false&kind=observer&take=20
```

- `kind ∈ { observer, agent, member }`; **vắng ⇒ tất cả** (hành vi y hệt hiện tại); giá trị khác ⇒ **400**.
- `observer` ⇒ 4 type của `NotificationTypes.All` (`OverdueTask`, `StalledTask`, `Overload`, `Bottleneck`).
- `agent` ⇒ 3 type của `AgentNotificationTypes` (`AgentRunFailed`, `AgentAwaitingClarification`, `AgentOutputPending`).
- `member` ⇒ `TaskAssigned`, `CommentOnTask`, **`CommentMention`**, `WorkspaceInvitation`.
- **`UnreadCount` trong response đếm theo CÙNG bộ lọc** ⇒ badge luôn đúng.
- `POST /api/notifications/{id}/read` và `POST /api/notifications/read-all` — **KHÔNG đổi**.

### 3.4 @mention — luồng ghi (backend)

```http
POST /api/boards/{boardId}/tasks/{taskId}/comments     (route KHÔNG đổi)
body: { "content": "Nhờ @Trần An xem lại phần auth", "mentionUserIds": ["<guid>", "<guid>"] }
```

1. Validate `Content` 1–4000 (**giữ nguyên**) và `MentionUserIds` **≤ 20** ⇒ vượt ⇒ **400**.
2. Mọi id phải là **member `human`** của workspace (kiểm qua `workspace_members` + `member_type`). Id lạ hoặc **AI Agent** ⇒ **400** — **validate TRƯỚC khi ghi bất kỳ row nào**.
3. Ghi `task_comments` (giữ nguyên) → `_events.CommentAdded` → `activity_logs` `CommentAdded` (giữ nguyên, **không** chứa text bình luận).
4. `NotifyAssigneeAsync` (**GIỮ NGUYÊN**): 1 row `CommentOnTask` nếu assignee ≠ author và ≠ AI Agent.
5. **MỚI** `NotifyMentionedAsync`:
   - `recipients = mentionUserIds − {author} − {assignee đã nhận row ở bước 4}`; rỗng ⇒ **không ghi gì**.
   - Ghi **1 batch** qua `INotificationWriter` với `type = MemberNotificationTypes.CommentMention`,
     `title = "Bạn được nhắc đến trong một bình luận"`,
     `message = "{authorName}: {excerpt ≤ 120 ký tự}"` (dùng `MemberNotificationLimits.CommentExcerpt`),
     `payload = { boardId, taskId, commentId, mentionedCount }`.
6. `PUT` sửa bình luận (**không** đổi): mention **KHÔNG** được gửi lại (tránh bão thông báo) — ghi rõ trong doc comment.

> **Bất biến:** `CommentResponse` **không** đổi (vẫn 7 field) ⇒ danh sách bình luận trong `TaskDetailModal` không phải sửa logic. `@tên` chỉ là **văn bản** trong `content`.
> **Không** parse `@tên` ở server (D11) ⇒ **không thể mạo danh**. **Không** gửi tin cho AI Agent, cho chính tác giả.

### 3.5 Bằng chứng cần đo

```
dotnet build TeamNexus.sln -m:1 -nr:false            → 0 Warning(s) / 0 Error(s)
dotnet ef migrations list --no-build                  → 9  (KHÔNG đổi)
dotnet ef migrations has-pending-model-changes        → "No changes have been made to the model since the last migration."
dotnet test  (TEAMNEXUS_TEST_DB trỏ DB thật)          → Failed 0 / Skipped 0 / Total = baseline + 40
dotnet test --filter "FullyQualifiedName~EmailTemplateTests" → 14/14 PASS (hồi quy Phase 11)
```

### 3.6 Test backend phải viết (≈ **44 test method** mới)

#### `DashboardApiTests` — D-1 … D-14

| # | Nội dung |
|---|---|
| **D-1** | Bucket `overdue`: task `dueDate` **quá khứ** + chưa done ⇒ vào `overdue`, `overdueByDays >= 1` |
| **D-2** | Biên: `dueDate = now - 1s` ⇒ **overdue**; `dueDate = now` ⇒ **KHÔNG** overdue (vào `dueSoon`) |
| **D-3** | Task ở **cột `is_done` nhưng `completed_at = null`** ⇒ **không** vào bucket nào; vẫn tính vào `summary.doneTasks` (khớp `ReportAggregator`) |
| **D-4** | `dueSoon` biên trên: `now` ⇒ có; `now + 3 ngày` ⇒ có; `now + 3 ngày + 1s` ⇒ không |
| **D-5** | `days=10` ⇒ biên đổi theo; `days=0`/`days=99` ⇒ **clamp** (không 400); `days=abc` ⇒ **400** |
| **D-6** | `recentlyAssigned`: `createdAt = now - 6 ngày` ⇒ có; `now - 8 ngày` ⇒ không; task **không** phải của tôi ⇒ không |
| **D-7** | `count` **không** bị `take` cắt: seed 15 task quá hạn, `take=5` ⇒ `count = 15`, `items.Length = 5` |
| **D-8** | Task của **board soft-deleted** ⇒ **không** xuất hiện |
| **D-9** | Task của **workspace khác** ⇒ không xuất hiện (kể cả khi cùng assignee) |
| **D-9b** | Task do người **đã bị kick** sở hữu ⇒ **vẫn** hiện (khớp Phase 11 R7: kick không xoá `assignee_id`) |
| **D-10** | Người **ngoài workspace** ⇒ **404** (không 403 — không tiết lộ workspace tồn tại) |
| **D-11** | **Member** (không phải Manager) gọi được ⇒ **200** + `recentActivities` **có** dữ liệu (khác phạm vi `/api/workspaces/{id}/activity` là Manager+) |
| **D-12** | `summary.totalTasks/doneTasks/openTasks/overdueTasks` khớp seed; `boards[].columns[].count` cộng đúng bằng `boards[].total` |
| **D-13** | `boardsTruncated = false` khi ≤ 20 board; seed **21 board** ⇒ `true` + đúng 20 phần tử |
| **D-14** | `recentActivities`: tối đa 10, **mới nhất trước**, `payload == null`, có `authorName`; hành động do **người khác** trong cùng workspace **vẫn** hiện (feed cấp workspace) |

#### `TaskSearchApiTests` — S-1 … S-16

| # | Nội dung |
|---|---|
| **S-1** | `q` khớp **title**, không phân biệt hoa/thường, **khớp cả tiếng Việt có dấu** (`"Báo cáo"`) |
| **S-2** | `q` khớp **description** |
| **S-3** | `q` rỗng / `"   "` ⇒ trả **tất cả** (không lọc), `hasQuery = false` |
| **S-4** | `q` **201 ký tự ⇒ 400**; đúng **200 ⇒ 200** |
| **S-5** | `boardId` của workspace khác ⇒ **404** |
| **S-6** | `assigneeId` lọc đúng; `assigneeId` **không phải member** ⇒ **400** |
| **S-7** | `unassigned=true` ⇒ chỉ task `assignee_id IS NULL` |
| **S-8** | `labelIds=L1,L2` ⇒ chỉ task có **cả hai**; id lạ ⇒ **400**; **11 id ⇒ 400** |
| **S-9** | `priority="urgent"` ⇒ **200** (ra `Urgent`); `"1"` / `"99"` / `"Boss"` ⇒ **400** |
| **S-10** | `dueFrom`/`dueTo` bao đúng; `dueFrom > dueTo` ⇒ **400**; `dueFrom` có offset `+07:00` ⇒ **không** 500 |
| **S-11** | `overdue=true` ⇒ chỉ task chưa done có `dueDate < now` |
| **S-12** | `includeDone` **mặc định** ⇒ task cột done **có** mặt; `includeDone=false` ⇒ **không** |
| **S-13** | Task **soft-deleted** và task của **board soft-deleted** ⇒ không xuất hiện |
| **S-14** | **Keyset 2 trang:** seed 30 task, `take=25` ⇒ page 1 = 25 + `hasMore=true` + `nextCursor != null`; page 2 ⇒ **5** + `hasMore=false` + `nextCursor=null`; **hợp 2 trang không trùng, không sót**. Lặp lại **sau khi chèn task mới** giữa 2 page ⇒ không nhảy/lặp |
| **S-15** | `cursor` rác (`"abc"`, `"\|"`, uuid sai) ⇒ **400** (không 500) |
| **S-16** | Người ngoài ⇒ **404**; thứ tự `updatedAt DESC`; mỗi item có `boardName`/`columnName` không rỗng; `labels` + `commentCount` điền đúng (chứng minh tái dùng `TaskReadHelpers`) |

#### `CommentMentionApiTests` — M-1 … M-10

| # | Nội dung |
|---|---|
| **M-1** | Mention 1 member khác ⇒ **1** row `CommentMention` cho người đó + **1** row `CommentOnTask` cho assignee (2 row, 2 loại) |
| **M-2** | Mention **chính mình** ⇒ **0** row `CommentMention` |
| **M-3** | Mention **assignee** (người đã nhận `CommentOnTask`) ⇒ **0** row `CommentMention` (không nhân đôi) |
| **M-4** | Body cũ **không** có `mentionUserIds` ⇒ hành vi **y hệt** trước Giai đoạn 12 (hồi quy Phase 11) |
| **M-5** | Mention **AI Agent** ⇒ **400**, **không** ghi comment/notification nào |
| **M-6** | Mention user **ngoài workspace** ⇒ **400**, không ghi gì |
| **M-7** | 2 id **trùng nhau** ⇒ **1** row |
| **M-8** | **> 20 id** ⇒ **400** |
| **M-9** | **11 người** được mention ⇒ 11 row; `message` ≤ `120 + len(authorName)` và **không** chứa toàn văn bình luận |
| **M-10** | `PUT` sửa bình luận ⇒ **không** thêm row mention nào (idempotent) |

#### `NotificationTriggerApiTests` (+4 test)

| # | Nội dung |
|---|---|
| **N-13** | `kind=observer` chỉ trả 4 type Observer; `unreadCount` **khớp** số đó (không tính `TaskAssigned`) |
| **N-14** | `kind=member` **gồm** `CommentMention`; `kind=agent` gồm 3 type agent |
| **N-15** | `kind=blah` ⇒ **400** |
| **N-16** | **Không truyền** `kind` ⇒ hành vi/hình dạng **y hệt** hôm nay (hồi quy) |

---

## 4. §4 — Frontend: Dashboard (ô **A**)

### 4.1 File **mới** — `frontend/src/features/dashboard/`

| File | Nội dung |
|---|---|
| `types/dashboard.types.ts` | Interface trong §3.3 (**khớp 1-1**, camelCase) |
| `services/dashboardApi.ts` | `dashboardApi.get(workspaceId, { days?, take? })` |
| `hooks/useDashboard.ts` | Pattern `useReportSummary` (**useState + useEffect + `reload`**, **không** react-query); lỗi ⇒ `message.error` + `status = 'error'`, **không** màn hình trắng |
| `components/MyTasksPanel.tsx` | `Tabs` 3 tab: **Quá hạn (N) · Sắp đến hạn (N) · Mới giao (N)** — `N = bucket.count`; tab rỗng ⇒ `Empty` tiếng Việt; item bấm ⇒ điều hướng tới board chứa task |
| `components/DashboardTaskRow.tsx` | `List.Item`: title · `Tag` board + cột · `Tag` priority (**dùng lại map màu của `TaskCard`**) · nhãn hạn `Quá hạn N ngày` (đỏ) / `Hôm nay` / `DD/MM` (**dùng `taskDueDate.ts`**, không tự viết lại); task `isDone` ⇒ nhãn "Đã xong" |
| `components/BoardSummaryPanel.tsx` | Mỗi board 1 dòng: tên · `Progress` `done/(done+open)` · số `total/done/open/overdue` · chip số lượng **theo từng cột** (kèm chip xám "Đã xong"); cảnh báo khi `boardsTruncated`; **không** chia 0 khi board 0 task |
| `components/RecentActivityPanel.tsx` | 10 item: `Avatar` + `authorName` + **nhãn hành động tiếng Việt** bằng `activityLabels.ts` **đã có** + `dayjs(...).fromNow()`; nút "Xem tất cả" ⇒ `/workspaces/{id}/activity` |
| `components/ObserverAlertsPanel.tsx` | Gọi `notificationApi.listNotifications({ isRead:false, kind:'observer', take:10 })`; > 0 ⇒ `Alert`; nút ⇒ mở `NotificationDrawer` |
| `pages/WorkspaceDashboardPage.tsx` | `AppHeader workspaceId` + `Row/Col` 2 cột (`MyTasksPanel` + `ObserverAlertsPanel` trên; `BoardSummaryPanel` + `RecentActivityPanel` dưới) + `Spin`/`Result` khi loading/lỗi + nút **Làm mới** |
| `__tests__/…` (~7 file) | §4.4 |

### 4.2 File **sửa**

| File | Thay đổi |
|---|---|
| `app/router.tsx` | +route `/workspaces/:workspaceId/dashboard` (**ProtectedRoute**) |
| `features/board/pages/BoardListPage.tsx` | +nút **"Tổng quan"** (`DashboardOutlined`) đầu nhóm toolbar, `data-testid="nav-dashboard-btn"` — chỉ **thêm**, không đổi nhãn nút đang có |
| `features/ai/services/notificationApi.ts` | `listNotifications` nhận thêm `kind?: 'observer' \| 'agent' \| 'member'` |
| `features/auth/pages/DashboardPage.tsx` | **GIỮ** `workspaceApi.list()` (**P6**) nhưng thay 2 card demo Phase 1 (RBAC `/manager/ping`, `/admin/ping`) bằng khối **"Workspace của bạn"** + nút vào `/workspaces/{id}/dashboard`. Hiện **không** có `features/auth/pages/__tests__` ⇒ có thể bỏ an toàn (ghi vào báo cáo) |
| `features/members/pages/WorkspaceMembersPage.tsx` · `reporting/pages/ReportsPage.tsx` · `workspace/pages/WorkspaceActivityPage.tsx` · `WorkspaceSettingsPage.tsx` | **Không bắt buộc**: thêm cùng nút "Tổng quan" vào `AppHeader` `children` **chỉ nếu** `npm test` vẫn xanh sau từng trang |

### 4.3 Route & điều hướng chốt

- `/` ⇒ **vẫn** `DashboardPage` (entry cũ: danh sách workspace). **KHÔNG** đổi hành vi.
- `/workspaces/:workspaceId/dashboard` ⇒ **MỚI** `WorkspaceDashboardPage` (trang chủ workspace thật).
- Điểm vào: nút **"Tổng quan"** ở `BoardListPage`; sau này Flutter (Giai đoạn 13) dùng cùng API.

### 4.4 Test frontend mới (~**7 file / ~34 test**)

| File test | Nội dung |
|---|---|
| `services/__tests__/dashboardApi.test.ts` | URL + query `days`/`take`; lỗi 404 ⇒ reject có `response.status` |
| `hooks/__tests__/useDashboard.test.ts` | loading → success; 404 ⇒ `status='error'` + `message.error`; `reload` gọi lại |
| `components/__tests__/MyTasksPanel.test.tsx` | 3 tab hiện **đúng số tổng** (`count`) khi `items` bị cắt; tab rỗng ⇒ `Empty`; nhãn `Quá hạn 3 ngày` / `Hôm nay` / `DD/MM`; không `dueDate` ⇒ không nhãn |
| `components/__tests__/BoardSummaryPanel.test.tsx` | % đúng; board 0 task ⇒ hiện `0%` (**không** chia 0); `boardsTruncated` ⇒ cảnh báo |
| `components/__tests__/RecentActivityPanel.test.tsx` | nhãn tiếng Việt theo `action` (dùng `activityLabels`); type lạ ⇒ hiện chuỗi trần, **không** crash; `authorName = null` ⇒ "Hệ thống" |
| `components/__tests__/ObserverAlertsPanel.test.tsx` | **chỉ** gọi với `kind:'observer'` + `isRead:false`; 0 alert ⇒ `Empty`; nhiều ⇒ `Alert` |
| `pages/__tests__/WorkspaceDashboardPage.test.tsx` | render 4 panel; 404 ⇒ `Result` tiếng Việt (không trắng); thiếu `workspaceId` ⇒ `Result` lỗi |
| `app/__tests__/router.dashboard.test.tsx` | route mới render trong `ProtectedRoute` |
| `features/auth/pages/__tests__/DashboardPage.test.tsx` (**mới**) | giữ `workspaceApi.list()`; nút vào dashboard đúng URL; không còn nút `/manager/ping` |

---

## 5. §5 — Frontend: Tìm kiếm & Lọc (ô **B**)

### 5.1 File **mới** — `frontend/src/features/search/`

| File | Nội dung |
|---|---|
| `types/search.types.ts` | `TaskSearchResponse`, `TaskSearchFilters` |
| `services/searchApi.ts` | `searchApi.searchTasks(workspaceId, filters)` — build `URLSearchParams` **giống** `workspaceApi.getActivity` (bỏ tham số rỗng) |
| `hooks/useTaskSearch.ts` | `filters`, `items`, `nextCursor`, `hasMore`, `loading`, `loadMore`, `reset`; gọi lại khi `filters` đổi (**debounce 300 ms cho `q`** — tránh 1 request/ký tự); `loadMore` nối trang |
| `components/TaskSearchBar.tsx` | `Input` (prefix `SearchOutlined`) + nút xoá; `data-testid="search-input"` |
| `components/TaskSearchFilters.tsx` | Select **Board** (`boardApi.getBoards`) · Select **Assignee** (`useWorkspaceMembers`, có mục "Chưa gán") · Select **Priority** (4 + "Tất cả") · Select **Label** (`boardApi.getLabels`, **chọn nhiều** = AND) · `DatePicker.RangePicker` cho due date · `Switch` **"Chỉ task quá hạn"** + **"Ẩn task đã xong"**; nút **"Xoá lọc"** |
| `components/TaskSearchResultList.tsx` | `List` item: title · board + cột (**nhãn "Đã xong"** khi `isDoneColumn`) · `Tag` priority · assignee · hạn (`taskDueDate.ts`) · số bình luận; bấm ⇒ `/workspaces/{wsId}/boards/{boardId}`; nút **"Tải thêm"** khi `nextCursor != null` |
| `pages/TaskSearchPage.tsx` | `AppHeader` + `useSearchParams` **đồng bộ filter lên URL** (F5/chia sẻ link giữ nguyên lọc — tiền lệ `ReportsPage` 31–56); phân biệt trạng thái rỗng bằng `hasQuery` ("Nhập từ khoá để tìm" ≠ "Không tìm thấy kết quả") |
| `__tests__/…` (~6 file) | §5.4 |

### 5.2 File **sửa**

| File | Thay đổi |
|---|---|
| `app/router.tsx` | +`/workspaces/:workspaceId/search` |
| `features/board/pages/BoardListPage.tsx` | +nút **"Tìm kiếm"** (`data-testid="nav-search-btn"`) |
| `features/board/components/BoardView.tsx` | **Chỉ thêm 1 nút**: "Tìm trong workspace →" cạnh ô tìm kiếm hiện có (`data-testid="board-search-workspace-btn"`) ⇒ mở `/workspaces/{wsId}/search?boardId={boardId}&q={searchQuery}`. **GIỮ NGUYÊN** toàn bộ lọc client hiện tại (đang có 3 assertion test) |

### 5.3 Quan hệ với lọc client đang có (chốt rõ để không mâu thuẫn)

- `BoardView` **giữ nguyên** lọc client (title/description/assignee/label + priority) — đó là bộ lọc **tức thời trong 1 board**.
- Ô B yêu cầu lọc **sâu hơn** (trạng thái, due date, xuyên board) ⇒ **trang `/search`** dùng API server-side.
- **KHÔNG** hợp nhất 2 cơ chế trong giai đoạn này (rủi ro phá 3 test `BoardView` + `boardStore`); ghi thành nợ **§6 R6**.

### 5.4 Test frontend mới (~**6 file / ~38 test**)

| File test | Nội dung |
|---|---|
| `services/__tests__/searchApi.test.ts` | URL + **chỉ** tham số có giá trị; CSV `labelIds`; `overdue`/`unassigned` là `"true"` |
| `hooks/__tests__/useTaskSearch.test.ts` | đổi filter ⇒ gọi lại; **debounce**: gõ 3 ký tự nhanh ⇒ **1** request; `loadMore` nối trang + giữ cursor; `reset` xoá hết |
| `components/__tests__/TaskSearchFilters.test.tsx` | chọn nhiều label ⇒ mảng id; "Chưa gán" ⇒ `unassigned=true` và **bỏ** `assigneeId`; "Xoá lọc" ⇒ reset |
| `components/__tests__/TaskSearchResultList.test.tsx` | nhãn "Đã xong" khi `isDoneColumn`; không kết quả ⇒ `Empty`; `hasMore=false` ⇒ **ẩn** nút "Tải thêm" |
| `pages/__tests__/TaskSearchPage.test.tsx` | filter đọc từ URL khi mount; đổi filter ⇒ `searchParams` cập nhật (`replace: true`); `q` rỗng ⇒ hiện "Nhập từ khoá"; **400 từ API ⇒ `message.error` tiếng Việt** |
| `features/board/components/__tests__/BoardView.workspaceSearch.test.tsx` | nút mới build **đúng URL** kèm `boardId` + `q`; **không** đổi hành vi lọc hiện có |

---

## 6. §6 — Frontend: @mention trong bình luận (ô **C**)

| File | Việc |
|---|---|
| `features/board/utils/mentionUtils.ts` (**mới**) | `extractMentionUserIds(text, members)`: quét **tên thành viên theo độ dài GIẢM DẦN**, mỗi lần khớp `"@" + displayName` (biên: ký tự ngay trước `@` **không** phải chữ/số); gom `userId` **không trùng**; bỏ id của chính mình. **Hàm thuần, không I/O** |
| `features/board/components/TaskDetailModal.tsx` | Ô **soạn** bình luận: `Input.TextArea` → **`Mentions`** (`prefix="@"`, `options.value = displayName`, chỉ member `human`), **giữ** `placeholder`/`data-testid`/nút gửi hiện có; submit: `boardApi.createComment(taskId, { content, mentionUserIds: extractMentionUserIds(content, members) })`. Ô **sửa** bình luận: **giữ** `Input.TextArea` (sửa mention không gửi lại thông báo ⇒ không cần autocomplete) |
| `features/board/types/board.types.ts` | `CreateCommentRequest` += `mentionUserIds?: string[]` (**append**) |
| `features/ai/services/notificationApi.ts` | (đã ở §4.2) `kind` |
| `features/ai/utils/notificationLabels.ts` | +`case 'CommentMention': return 'Được nhắc đến'` |
| `features/ai/types/notification.types.ts` | +`'CommentMention'` vào union `NotificationType`; +`mentionedCount` / `mentionUserIds` vào `NotificationPayload` |
| `features/ai/components/NotificationItem.tsx` | **Không đổi logic** — `payload.taskId` đã dùng để điều hướng; chỉ thêm test cho type mới |

### 6.1 Test frontend mới (~**2 file mới + 1 file mở rộng / ~21 test**)

| File test | Nội dung |
|---|---|
| `features/board/utils/__tests__/mentionUtils.test.ts` (**mới**) | tên **1 từ**; tên **có dấu cách**; 2 tên lồng nhau (`An` vs `An Bình`) ⇒ chọn tên **dài**; `@` trong email (`a@b.c`) ⇒ **không** tính là mention; tên xuất hiện 2 lần ⇒ **1** id; **tự** mention ⇒ loại; không mention ⇒ `[]`; **tên trùng** giữa 2 member ⇒ chọn id **đầu tiên** (hành vi có chủ ý, ghi trong doc comment) |
| `features/board/components/__tests__/TaskDetailModal.mention.test.tsx` (**mới**) | gõ `@` ⇒ hiện option thành viên (**không** có AI Agent); chọn ⇒ chèn tên; gửi ⇒ body có `mentionUserIds` đúng; **`TaskDetailModal.test.tsx` cũ vẫn xanh** |
| `features/ai/components/__tests__/NotificationItem.test.tsx` (mở rộng) | type `CommentMention` ⇒ nhãn "Được nhắc đến" + chip `default` (không đỏ) + không crash khi `payload = null` |

> **KHÔNG** đổi cách **hiển thị** bình luận (vẫn `Text`, **không** `dangerouslySetInnerHTML`) ⇒ không có đường XSS mới. Ghi vào §8 R3.

---

## 7. §7 — DoD, CI & tài liệu

### 7.1 Con số mục tiêu

| Chỉ số | Baseline **đo được** | Kết quả **đo thật** |
|---|---|---|
| Backend `dotnet test` (**DB thật**, `Skipped: 0`) | **371** (134 PASS + 237 SKIP vì chưa nối DB — §1.2) | ✅ **423** (Failed **0** / Skipped **0**) = 371 + **52** |
| Backend `dotnet test` (không DB — vẫn phải chạy được) | 134 pass / 237 skip | ✅ không đổi hành vi skip; **không** dùng làm bằng chứng DoD |
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 / 0 | ✅ **0 / 0** |
| Frontend `npm test` | ✅ **360** / 60 file | ⬜ **≈ 453** (~72 file) — chờ antigravity |
| Frontend `npm run lint` · `npx tsc -b` · `npm run build` | 0/0 · exit 0 · OK | ⬜ giữ nguyên |
| `dotnet ef migrations list` | ✅ **9** | ✅ **9** (KHÔNG đổi) |
| `has-pending-model-changes` | sạch | ✅ **"No changes have been made to the model since the last migration."** |

**Phân bổ 52 test backend mới:** `DashboardApiTests` **14** · `TaskSearchApiTests` **24** ·
`CommentMentionApiTests` **10** · `NotificationTriggerApiTests` **+4**.

### 7.2 ⚙️ CI phải nâng (làm **SAU CÙNG**, khi số thật đã đo)

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | `if ($total -ne 371)` ⇒ **`-ne 423`**; cập nhật comment chuỗi `… → 371 (Giai đoạn 11) → 423 (Giai đoạn 12)` |
| 2 | `.github/workflows/ci-web.yml` | `if ($total -le 279)` ⇒ **`-le <baseline mới>`** (≥ số test thật sau khi frontend xong) + comment baseline |

### 7.3 Tài liệu phải cập nhật

| # | File | Việc |
|---|---|---|
| 1 | `Project-Documents/tasks/phase-12-dashboard-search.md` | **Tài liệu này** — điền **số thật** vào §1.2 + §7.1 sau khi đo |
| 2 | `Project-Documents/03-roadmap.md` | Giai đoạn 12: tick `[x]` 3 ô + link tài liệu này + baseline thật + ghi **"không migration (9 tổng)"** |
| 3 | `Project-Documents/01-system-specification.md` | §11: chốt ngữ nghĩa 3 bucket (§2 D7), route `/workspaces/{id}/dashboard` + `/search`, quyết định `mentionUserIds` tường minh (D11) |
| 4 | `Project-Documents/04-database-design.md` | §4 bảng `NotificationType`: +`CommentMention`; §7: +3 gạch đầu dòng (mention tường minh · dashboard read-only không bảng mới · search keyset `(updated_at, id)`); §5: ghi rõ **không** thêm index và **vì sao** |
| 5 | `README.md` | +`## Trạng thái (Giai đoạn 12)`; xác nhận mục Migration vẫn **9** |
| 6 | `Project-Documents/report/phase-12-dashboard-search-test-report.md` | **Tạo khi kết thúc** (theo mẫu `report/phase-11-member-profile-management-test-report.md`) |

### 7.4 Điều kiện "xong"

Cả **3 ô** (A, B, C) + **3 hạng mục phát sinh** (D `kind` · E route · F tái sử dụng) đóng bằng bằng chứng §7.5;
**vẫn đúng 9 migration** và `has-pending-model-changes` sạch;
2 cổng CI đã nâng baseline và **chạy xanh trên GitHub Actions**;
`03-roadmap.md` + `README.md` + `01-system-specification.md` + `04-database-design.md` ghi **số thật**;
báo cáo tại `Project-Documents/report/phase-12-dashboard-search-test-report.md`.

### 7.5 Bảng bằng chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 warning / 0 error | ✅ **đạt** (sau mỗi bước) |
| 2 | `dotnet ef migrations list` + `has-pending-model-changes` | **9** / sạch | ✅ **đạt** (9 · *"No changes have been made…"*) |
| 3 | `dotnet test` với `TEAMNEXUS_TEST_DB` (Docker `postgres:18` cổng 5433 — §1.2) | `Skipped: 0`, `Failed: 0`, `Total 423` | ✅ **đạt** (423/0/0 · 68 s) |
| 4 | `npm run lint` / `npx tsc -b` / `npm test` / `npm run build` | 0-0 / exit 0 / ≥ 453 / OK | ⬜ **chờ antigravity** |
| 5 | **1 lượt thao tác thật, có ảnh**: mở `/workspaces/{id}/dashboard` thấy 3 tab "Task của tôi" + tóm tắt board + hoạt động gần đây; vào `/search` lọc theo nhãn + khoảng hạn rồi "Tải thêm"; gõ `@` trong bình luận ⇒ chọn người ⇒ **chuông header nổi số** ⇒ drawer hiện "Được nhắc đến" | — | ⬜ |
| 6 | 2 workflow CI xanh | `ci-backend` (total = số thật, skipped 0) + `ci-web` (≥ baseline mới) | ⬜ |

---

## 8. Ca biên & chế độ lỗi (bắt buộc xử lý)

| Ca | Hành vi chốt |
|---|---|
| Dashboard: người ngoài workspace | **404** (không tiết lộ workspace tồn tại) |
| Dashboard: **Member** (không phải Manager) | **200** — **khác** `/api/workspaces/{id}/activity` (Manager+). Ghi rõ trong doc |
| Dashboard: `dueDate == now` | **KHÔNG** quá hạn; **CÓ** trong "sắp đến hạn" |
| Dashboard: task ở cột `is_done` + `completedAt = null` | **Không** vào bucket nào; **vẫn** đếm `done` ở `summary` |
| Dashboard: `days` ngoài `[1,30]` | clamp; `days = abc` ⇒ **400** |
| Dashboard: `take` ngoài `[1,50]` | clamp (**không** 400) |
| Dashboard: workspace **0 board / 0 task** | `200`, mọi bucket rỗng, `summary` toàn `0`, **không** NaN/Infinity |
| Dashboard: 21+ board | `boardsTruncated = true`, tối đa 20 board |
| Dashboard: task do người **đã bị kick** sở hữu | **Vẫn** hiện (khớp Phase 11 R7 — kick không xoá `assignee_id`) |
| Search: `q` chỉ whitespace | Điều kiện lọc bị **bỏ**; `hasQuery = false` |
| Search: `q` > 200 ký tự | **400** |
| Search: `priority = "1"` / `"99"` / `"Boss"` | **400** (bẫy BUG-1 Phase 10) |
| Search: `priority = "urgent"` | **200** ⇒ `Urgent` |
| Search: `dueFrom` có offset (`+07:00`) | Chuẩn hoá UTC ⇒ **không** 500 (bẫy Npgsql Phase 10) |
| Search: `dueFrom > dueTo` | **400** |
| Search: `boardId` của workspace khác | **404** |
| Search: `assigneeId` không phải member | **400** |
| Search: `labelIds` chứa id lạ / > 10 id | **400** |
| Search: `cursor` rác | **400** (không 500, không im lặng quay về trang 1) |
| Search: dữ liệu đổi giữa 2 trang | Không nhảy/lặp (keyset trên `(updated_at, id)`) |
| Search: task ở board soft-deleted | **Không** xuất hiện |
| Search: 0 kết quả | `200` `items: []` + `hasMore = false`; UI phân biệt bằng `hasQuery` |
| Mention: `mentionUserIds` chứa AI Agent / người ngoài workspace | **400**, **không** ghi bình luận nào (**validate trước khi ghi**) |
| Mention: > 20 id | **400** |
| Mention: tự mention · trùng id · mention assignee | Loại trùng ⇒ **không** row thừa (tối đa **1 row/người/bình luận**) |
| Mention: `mentionUserIds` vắng hoặc `[]` | Hành vi **y hệt** trước Giai đoạn 12 |
| Mention: sửa bình luận | **KHÔNG** gửi lại thông báo |
| Notifications: `kind` lạ | **400**; không truyền ⇒ y hệt hôm nay |
| Notifications: `CommentMention` | `NotificationItem` chip `default` (không đỏ), nhãn "Được nhắc đến", không crash khi `payload = null` |
| 401/403/404/409/429 ở mọi trang/endpoint mới | `message.error` / `<Result>` **tiếng Việt** — **không** màn hình trắng |
| Tên hiển thị **trùng** trong workspace | `extractMentionUserIds` chọn **id đầu tiên** (theo thứ tự trả của `GET /members`); ghi rõ trong `mentionUtils` + có test |
| Tên hiển thị có **dấu cách** | `@Trần An` khớp được (quét tên dài trước, biên `@` không dính chữ trước đó) |

---

## 9. ⛔ KHÔNG được làm

- ❌ **Không** thêm migration/bảng/cột/index. `dotnet ef migrations list` phải vẫn **9**; `has-pending-model-changes` phải sạch.
- ❌ **Không** đổi shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `NotificationListResponse`, `WorkspaceMemberResponse`, `WorkspaceActivity*`, payload/tên event SignalR.
- ❌ **Không** sửa `GET /api/boards/{boardId}/tasks` (shape **mảng**) và **không** sửa `boardStore`/`useBoard`.
- ❌ **Không** thêm type vào `NotificationTypes.All` (whitelist chống hallucination của Observer) — dùng `MemberNotificationTypes` (đúng Phase 11 D10).
- ❌ **Không** parse `@tên` ở server; **không** nhận `userId` từ text; **không** cho mention vượt qua kiểm tra membership.
- ❌ **Không** gửi thông báo mention cho **AI Agent**, cho **chính tác giả**, hoặc **lặp lại** row của **assignee**.
- ❌ **Không** thêm package NuGet hay thư viện npm (dùng `antd` `Mentions`, `dayjs`, `axios` sẵn có).
- ❌ **Không** dùng `dangerouslySetInnerHTML`; `@tên` chỉ là **văn bản**.
- ❌ **Không** dùng `Enum.TryParse` trần cho priority (bẫy BUG-1 Phase 10) và **không** quên `ToUtc(...)` cho mọi `DateTimeOffset` (bẫy Npgsql Phase 10).
- ❌ **Không** để Dashboard/Search của Board tham chiếu module `Ai` (chiều phụ thuộc **Ai → Board** chỉ một chiều).
- ❌ **Không** di chuyển `NotificationService`/`NotificationEndpoints`/`NotificationDrawer` sang module/thư mục mới.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**); nếu buộc sửa ⇒ ghi rõ lý do + tên test vào báo cáo.
- ❌ **Không** mở rộng phạm vi sang: Mobile/Flutter (Giai đoạn 13), full-text search / `unaccent` / `pg_trgm`, real-time cho dashboard, đa assignee, hard delete, template email tuỳ biến, SSO/SCIM.

---

## 10. Thứ tự thi hành đề xuất

1. **§1.2** — dựng PostgreSQL test (Docker `postgres:18` cổng 5433) ⇒ xác nhận `Skipped: 0` với **371** test **hiện có** (baseline sạch **trước** khi thêm gì).
2. **P1 + P2** — `TaskReadHelpers` + `TimeProvider` ⇒ `dotnet build` 0/0 và `dotnet test` **vẫn 371** (refactor **không** đổi hành vi).
3. **§3.1** — Dashboard backend + `DashboardApiTests` (D-1…D-14).
4. **§3.2** — Search backend + `TaskSearchApiTests` (S-1…S-16).
5. **§3.3** — Mention backend + `CommentMentionApiTests` (M-1…M-10); **§3.4** `kind` filter + 4 test notification.
6. **§4 → §5 → §6** — Frontend theo thứ tự Dashboard ⇒ Search ⇒ Mention (mỗi bước: `npm test` **trước và sau**, thêm file test tương ứng).
7. **§7.2** — nâng 2 cổng CI bằng **số thật**.
8. **§7.3** — cập nhật tài liệu + viết `report/phase-12-dashboard-search-test-report.md` + chụp bằng chứng §7.5 (#5).

---

## 11. Rủi ro & giả định

| # | Mục | Xử lý |
|---|---|---|
| **R1** | **Chưa đo được 100% test backend** vì mật khẩu Postgres local khác `postgres/postgres` (§1.2) | Docker **đang chạy** + cổng 5433 trống ⇒ dựng `postgres:18` tạm và đặt `TEAMNEXUS_TEST_DB`. **Điều kiện chốt là `Skipped: 0`**; nếu vẫn không được ⇒ **không** được coi là đạt DoD, ghi rõ trong báo cáo |
| **R2** | Cổng CI `$total -ne 371` sẽ **đỏ** cho tới khi nâng số | Đây là **cổng cố ý** (chống xanh giả). Nâng ở §7.2 **sau** khi có số thật |
| **R3** | Thêm `Mentions` thay `Input.TextArea` ⇒ dễ làm đỏ `TaskDetailModal.test.tsx` đang xanh | Giữ **nguyên** `placeholder` / `data-testid` / nút gửi; chạy `npm test` **trước và sau** riêng file đó; nếu buộc sửa ⇒ ghi vào báo cáo |
| **R4** | `kind` filter là **thay đổi hợp đồng backend đã verify** (additive) | Tham số vắng ⇒ **hành vi y hệt**; có test hồi quy riêng (**N-16**); `NotificationResponse` **không** đổi ⇒ `features/ai` gần như không phải sửa |
| **R5** | Dashboard gọi 6 truy vấn + search `ILike` trên `tasks` **chưa có index phù hợp** | Chấp nhận ở quy mô đồ án (đúng tinh thần "chỉ tối ưu khi có bằng chứng"); nợ kỹ thuật ghi ở **§9**: index `(assignee_id, due_date)` / `(board_id, due_date)` là **migration của giai đoạn sau** |
| **R6** | 2 cơ chế lọc (client ở `BoardView`, server ở `/search`) có thể gây hiểu nhầm cho người dùng | UI ghi rõ: ô ở board = "lọc trong bảng này", nút "Tìm trong workspace" = tìm **xuyên board**; hợp nhất để giai đoạn sau |
| **R7** | Tên hiển thị **trùng** ⇒ mention sai người | Ghi rõ hành vi (chọn id đầu tiên) + có test; giải pháp đúng (lưu `userId` trong văn bản) sẽ **đổi shape** comment ⇒ để giai đoạn sau |
| **R8** | `_db.Boards.Where(WorkspaceId == …)` + query filter có thể gây cảnh báo EF về filter không nhất quán | Theo dõi **0 warning** khi build; nếu xuất hiện ⇒ lấy board id qua `_db.Boards.Select(b => b.Id)` (đúng tiền lệ `SearchSystemDataTool` 72–78) **sau khi** `RequireMemberAsync` đã chặn |
| **R9** | Số test thật lệch kỳ vọng (~44 backend / ~93 frontend) | **Số đo thắng tài liệu**: cập nhật §7.1 + cổng CI §7.2 + báo cáo |

**Giả định:**
- Baseline **371 backend / 360 frontend (60 file) / 9 migration** — **số đo thắng tài liệu**.
- Có PostgreSQL 18 dùng được cho `dotnet test` (Docker cổng **5433** hoặc mật khẩu local thật) ⇒ `Skipped: 0`.
- Giai đoạn 9–11 đã merge: Identity role chỉ còn `Admin`/`User`; `AppHeader` · `NotificationBell` · `useUnreadCount` · `useWorkspaceMembers` · `useWorkspaceRole` · `activityLabels.ts` · `taskDueDate.ts` dùng lại được.
- `Program.cs` vẫn gọi `AddBoardModule()` **trước** `AddAiModule()`.
- Frontend: React 19 + TypeScript 6 + **antd 6.6.3 (đã có `Mentions`)** + Vite 8 + Vitest 5; toàn bộ nhãn UI **tiếng Việt có dấu**.
- Endpoint mới **không** ảnh hưởng client cũ ngoài việc được **thêm** vào (append-only); Flutter (Giai đoạn 13) dùng lại cùng API.
