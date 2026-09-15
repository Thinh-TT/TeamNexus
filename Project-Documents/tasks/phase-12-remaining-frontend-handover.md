# Bàn giao phần còn lại — Giai đoạn 12 (Dashboard & Tìm kiếm)

> **Người nhận:** Antigravity (agent/đội thực thi frontend).
> **Người giao:** phiên làm **backend §1–§3** (đã xong; xem bảng trạng thái dưới).
>
> **Tham chiếu bắt buộc — đọc trước khi code:**
> - `Project-Documents/tasks/phase-12-dashboard-search.md` — **kế hoạch gốc**: bảng quyết định **D1–D13**, phát sinh **P1–P6**, ca biên, bảng bằng chứng. Đây là hợp đồng cứng.
> - `Project-Documents/03-roadmap.md` → *Giai đoạn 12* (ô A–C) · `01-system-specification.md` §11.
> - `Project-Documents/tasks/phase-11-remaining-frontend-handover.md` — note bàn giao mẫu trước đó (cùng văn phong, cùng mức chi tiết).
>
> **Phạm vi note này:** **§4 (Dashboard UI)** + **§5 (Search UI)** + **§6 (@mention UI)** + **§7.2 (CI)** + **§7.3 (tài liệu)**.
> **Backend §1–§3 ĐÃ XONG** — **không** làm lại, **không** sửa file backend (xem §8).

---

## 1. Trạng thái bàn giao

| Mục | Trạng thái |
|---|---|
| **§1 Dashboard backend** (`GET /api/workspaces/{id}/dashboard`) | ✅ **XONG** — `DashboardApiTests` **14/14 PASS** |
| **§2 Search backend** (`GET /api/workspaces/{id}/tasks/search`) | ✅ **XONG** — `TaskSearchApiTests` **24/24 PASS** |
| **§3 Mention backend** (`mentionUserIds` + `CommentMention`) | ✅ **XONG** — `CommentMentionApiTests` **10/10 PASS** |
| **§3.4 `kind` filter** trên `GET /api/notifications` | ✅ **XONG** — 4 test mới trong `NotificationTriggerApiTests` |
| **P1/P2 refactor** (`TaskReadHelpers`, `TimeProvider`) | ✅ **XONG** — hồi quy `KanbanApiTests`/`TaskFieldsApiTests`/`WorkspaceApiTests` **76/76 PASS** |
| **§4 Dashboard UI** | ⬜ **Chờ bạn** |
| **§5 Search UI** | ⬜ **Chờ bạn** |
| **§6 @mention UI** | ⬜ **Chờ bạn** |
| **§7.2 CI + §7.3 tài liệu** | ⬜ **Chờ bạn** |

### Bằng chứng backend **đo thật** (PostgreSQL 18 trong Docker, cổng 5433)

```
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental
    → 0 Warning(s) / 0 Error(s)

dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
    → Failed: 0 / Passed: 423 / Skipped: 0 / Total: 423      ⏱ ~60 s

dotnet ef migrations list --no-build
    → 9 migration (mới nhất 20260914105025_Phase11MemberProfile)

dotnet ef migrations has-pending-model-changes --no-build
    → "No changes have been made to the model since the last migration."
```

> **Baseline trước giai đoạn này là 371 test.** `423 = 371 + 52` test mới
> (**14** dashboard + **24** search + **10** mention + **4** `kind`).

### ⛔ Ba việc **bắt buộc** làm TRƯỚC khi viết dòng code frontend đầu tiên

1. **Chạy `dotnet test` với PostgreSQL thật và xác nhận `Skipped: 0`.**
   ```powershell
   docker run -d --name teamnexus-test-pg -e POSTGRES_PASSWORD=postgres -p 5433:5432 postgres:18
   $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"
   dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
   ```
   **Kỳ vọng: `Passed: 423 / Failed: 0 / Skipped: 0`.**
   - Nếu **`total` lệch 423** ⇒ **số đo thắng tài liệu**: cập nhật §7.1 của task doc **và** cổng CI §7.
   - Nếu **có test đỏ** ⇒ sửa cho xanh (test là hợp đồng; nếu bạn tin code sai thì báo lại kèm tên test).
2. **Đo baseline frontend** trước khi sửa:
   ```
   cd frontend
   npm run lint   → 0 warning / 0 error
   npx tsc -b     → exit 0
   npm test       → Test Files 60 passed (60) · Tests 360 passed (360)
   npm run build  → OK
   ```
   > ⛔ **Điều kiện "xong" của §4–§6 là `npm test` phải ra ≥ 453 test** (360 + ~93 mới), `tsc -b` exit 0, `lint` 0/0, `build` OK. Đây là DoD cứng.
   > Nếu baseline lệch **360** ⇒ **dừng lại và báo**, đừng tự đoán.
3. **Chạy** `npm run dev` **+ backend** để có dữ liệu thật khi làm UI (dashboard/search cần task có `dueDate` + ít nhất 2 member để thấy hiệu quả).

---

## 2. Hợp đồng API backend đã có (dùng đúng, đừng đoán)

> `httpClient` chung **tự** gắn `X-XSRF-TOKEN` cho POST/PUT/DELETE và **tự** refresh 401 — **không** tự đặt header CSRF. Tất cả endpoint mới đều là **GET** nên không cần CSRF.

### 2.1 Dashboard — `GET /api/workspaces/{workspaceId}/dashboard?days=3&take=10`

| | |
|---|---|
| Quyền | **Member+** (mọi thành viên). **404** nếu không phải thành viên (không phải 403) |
| `days` | Khung "sắp đến hạn", mặc định **3**, clamp `[1,30]`; **`days=abc` ⇒ 400** |
| `take` | Số item mỗi bucket, mặc định **10**, clamp `[1,50]` |

```ts
interface DashboardTaskItem {
  id: string
  boardId: string
  columnId: string
  title: string
  boardName: string            // để card ngoài board vẫn có ngữ cảnh
  columnName: string
  dueDate: string | null       // ISO
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  createdAt: string
  assigneeId: string | null
  isDone: boolean              // cột is_done HOẶC có completed_at
  overdueByDays: number | null // >= 1 khi quá hạn; null khi không  ← DÙNG SỐ NÀY, đừng tự tính lại
}

interface DashboardTaskBucket {
  count: number                // TỔNG THẬT (không bị `take` cắt) ⇒ hiện ở tiêu đề tab
  items: DashboardTaskItem[]   // tối đa `take`
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
  payload: null                // LUÔN null — đừng trông đợi dữ liệu trong đây
}

interface DashboardResponse {
  workspaceId: string
  workspaceName: string
  utcNow: string               // mốc server dùng để tính bucket
  dueSoonDays: number
  myTasks: {
    overdue: DashboardTaskBucket
    dueSoon: DashboardTaskBucket
    recentlyAssigned: DashboardTaskBucket
  }
  boards: DashboardBoardSummary[]        // tối đa 20 board
  boardsTruncated: boolean               // true khi workspace có > 20 board
  recentActivities: DashboardActivityItem[]   // tối đa 10, MỚI NHẤT TRƯỚC
  summary: {
    totalTasks: number; doneTasks: number; openTasks: number
    overdueTasks: number; myOpenTasks: number
  }
}
```

**Ngữ nghĩa chốt (server đã tính, UI chỉ hiển thị):**
- **Quá hạn** = chưa xong **và** `dueDate < now`. Hạn **đúng lúc `now`** ⇒ **KHÔNG** quá hạn.
- **Sắp đến hạn** = chưa xong **và** `now ≤ dueDate ≤ now + dueSoonDays` **và LOẠI TRỪ task đã quá hạn** (3 tab là một **phân hoạch theo mức khẩn cấp**).
- **Mới giao** = chưa xong **và** `createdAt ≥ now − 7 ngày`.
- `isDone` = ở cột `is_done` **hoặc** có `completed_at` (khớp `ReportAggregator` của Giai đoạn 6 — Dashboard và Báo cáo luôn nói cùng một số).
- `isDone` / `overdueByDays` trong response **là mốc so sánh duy nhất**: frontend **không** gọi `taskDueDate.isOverdue` cho các item dashboard (hàm đó so theo **ngày**, server so theo **thời điểm**).

### 2.2 Search — `GET /api/workspaces/{workspaceId}/tasks/search`

| Tham số | Kiểu | Luật |
|---|---|---|
| `q` | string | trim; rỗng ⇒ bỏ lọc; **> 200 ký tự ⇒ 400**; khớp `title` **hoặc** `description`, không phân biệt hoa/thường, **khớp dấu tiếng Việt** |
| `boardId` | guid | board của workspace khác ⇒ **404** |
| `assigneeId` | guid | không phải thành viên ⇒ **400** |
| `unassigned` | `true` | chỉ task chưa gán; **thắng** `assigneeId` |
| `labelIds` | CSV guid | task phải có **MỌI** nhãn (AND); id lạ ⇒ **400**; **> 10 ⇒ 400** |
| `priority` | string | `Low/Medium/High/Urgent` (ignore-case); **`"1"`/`"99"`/`"Boss"` ⇒ 400** |
| `dueFrom` / `dueTo` | ISO-8601 | bao gồm hai đầu; `dueFrom > dueTo` ⇒ **400**; offset `+07:00` **được chấp nhận** |
| `overdue` | `true` | chỉ task chưa xong và `dueDate < now` |
| `includeDone` | bool | **mặc định `true`**; `false` ⇒ ẩn task đã xong |
| `take` | int | mặc định **25**, clamp `[1,100]` (không 400) |
| `cursor` | string | con trỏ opaque của trang trước; hỏng ⇒ **400** |

```ts
interface TaskSearchItem {
  task: TaskResponse          // NGUYÊN shape TaskResponse đã có ở board.types.ts — dùng lại được
  boardName: string
  columnName: string
  isDoneColumn: boolean       // true ⇒ hiện nhãn "Đã xong"
}
interface TaskSearchResponse {
  items: TaskSearchItem[]
  nextCursor: string | null   // null ⇒ hết trang
  hasMore: boolean
  hasQuery: boolean           // false ⇒ "chưa nhập từ khoá" (KHÁC "không tìm thấy kết quả")
}
```

> **Thứ tự:** khi có `q` ⇒ task khớp **title** đứng trước task chỉ khớp description, sau đó `updatedAt DESC`. Khi không có `q` ⇒ thuần `updatedAt DESC`.
> **`GET /api/boards/{boardId}/tasks` KHÔNG đổi** (vẫn trả **mảng** `TaskResponse[]`) — vì `useBoard`, `boardStore` và kéo-thả đang parse nó.

### 2.3 Notifications — thêm tham số `kind` (append)

```http
GET /api/notifications?isRead=false&kind=observer&take=20
```

- `kind ∈ { observer, agent, member }`; **vắng ⇒ tất cả** (hành vi y hệt trước Giai đoạn 12); giá trị khác ⇒ **400**.
- `observer` ⇒ `OverdueTask`, `StalledTask`, `Overload`, `Bottleneck`
- `agent` ⇒ `AgentRunFailed`, `AgentAwaitingClarification`, `AgentOutputPending`
- `member` ⇒ `TaskAssigned`, `CommentOnTask`, **`CommentMention`**, `WorkspaceInvitation`
- **`unreadCount` trong response đếm theo CÙNG bộ lọc** ⇒ badge luôn khớp list.
- `POST /api/notifications/{id}/read` và `/read-all` **KHÔNG đổi**.

### 2.4 @mention — route comment **KHÔNG đổi**

```http
POST /api/boards/{boardId}/tasks/{taskId}/comments
body: { "content": "Nhờ @Trần An xem lại phần auth", "mentionUserIds": ["<guid>", "<guid>"] }
```

| Luật | Hành vi |
|---|---|
| `content` | 1–4000 ký tự (như cũ) |
| `mentionUserIds` | **tuỳ chọn**, **field CUỐI**, mặc định `null`/`[]`; vắng ⇒ hành vi y hệt trước Giai đoạn 12 |
| ai được mention | **chỉ thành viên `human` của workspace**; AI Agent ⇒ **400**; người ngoài workspace ⇒ **400** |
| cap | **> 20 id ⇒ 400** |
| trùng id | tự gộp ⇒ **1 thông báo/người** |
| tự mention / mention assignee | **không** sinh row `CommentMention` (assignee đã có row `CommentOnTask`) |
| **sửa** bình luận (`PUT`) | **KHÔNG** gửi lại thông báo |
| thất bại | **400 TRƯỚC khi ghi** ⇒ không có bình luận "mồ côi" |

> **`CommentResponse` KHÔNG đổi** (vẫn 7 field) ⇒ danh sách bình luận trong `TaskDetailModal` không phải sửa logic.
> **Server KHÔNG parse `@tên`** — nó chỉ tin `mentionUserIds`. Vì vậy client **phải** gửi id (xem §6).

---

## 3. §4 — Dashboard UI

### 3.1 Thư mục **mới**: `frontend/src/features/dashboard/`

| File | Nội dung |
|---|---|
| `types/dashboard.types.ts` | Interface §2.1 (**khớp 1-1**, camelCase) |
| `services/dashboardApi.ts` | `dashboardApi.get(workspaceId, { days?, take? })` → `httpClient.get` |
| `hooks/useDashboard.ts` | Pattern `useReportSummary` (**useState + useEffect + `reload`**), **KHÔNG** react-query; lỗi ⇒ `message.error` + `status='error'`, **không** màn hình trắng |
| `components/MyTasksPanel.tsx` | `Tabs` 3 tab: **Quá hạn (N) · Sắp đến hạn (N) · Mới giao (N)** — `N = bucket.count`; tab rỗng ⇒ `Empty` |
| `components/DashboardTaskRow.tsx` | `List.Item`: title · `Tag` board + cột · `Tag` priority · nhãn hạn; item bấm ⇒ `/workspaces/{wsId}/boards/{boardId}` |
| `components/BoardSummaryPanel.tsx` | Mỗi board 1 dòng: tên · `Progress` `done/(done+open)` · `total/done/open/overdue` · chip số lượng **từng cột**; cảnh báo khi `boardsTruncated`; **không chia 0** |
| `components/RecentActivityPanel.tsx` | 10 item: `Avatar` + `authorName` + nhãn tiếng Việt qua `activityLabels.ts` **đã có** + `dayjs(...).fromNow()`; "Xem tất cả" ⇒ `/workspaces/{id}/activity` |
| `components/ObserverAlertsPanel.tsx` | Gọi `notificationApi.listNotifications({ isRead:false, kind:'observer', take:10 })`; > 0 ⇒ `Alert`; nút ⇒ mở `NotificationDrawer` |
| `pages/WorkspaceDashboardPage.tsx` | `AppHeader workspaceId` + `Row/Col` 2 cột + `Spin`/`Result` khi loading/lỗi + nút **Làm mới** |
| `__tests__/…` | §3.3 |

### 3.2 File **sửa**

| File | Thay đổi |
|---|---|
| `app/router.tsx` | +route `/workspaces/:workspaceId/dashboard` (**ProtectedRoute**) |
| `features/board/pages/BoardListPage.tsx` | +nút **"Tổng quan"** (`DashboardOutlined`) đầu nhóm toolbar, `data-testid="nav-dashboard-btn"` — chỉ **thêm**, không đổi nhãn nút đang có |
| `features/ai/services/notificationApi.ts` | `listNotifications` nhận thêm `kind?: 'observer' \| 'agent' \| 'member'` |
| `features/auth/pages/DashboardPage.tsx` | **GIỮ** `workspaceApi.list()` (side effect "tự tạo workspace mặc định" mà trang `/` phụ thuộc) nhưng thay 2 card demo Phase 1 (test RBAC `/manager/ping`, `/admin/ping`) bằng khối **"Workspace của bạn"** + nút vào `/workspaces/{id}/dashboard`. Hiện **không** có `features/auth/pages/__tests__` ⇒ bỏ an toàn (ghi vào báo cáo) |
| 4 trang khác (`WorkspaceMembersPage`, `ReportsPage`, `WorkspaceActivityPage`, `WorkspaceSettingsPage`) | **Không bắt buộc**: thêm cùng nút "Tổng quan" vào `AppHeader` `children` **chỉ nếu** `npm test` vẫn xanh sau từng trang |

### 3.3 Route & điều hướng chốt

- `/` ⇒ **vẫn** `DashboardPage` (entry cũ: danh sách workspace). **KHÔNG** đổi hành vi.
- `/workspaces/:workspaceId/dashboard` ⇒ **MỚI** `WorkspaceDashboardPage`.
- Điểm vào: nút **"Tổng quan"** ở `BoardListPage`.

### 3.4 Test cần viết (~**8 file mới / ~34 test**)

| File test | Nội dung |
|---|---|
| `services/__tests__/dashboardApi.test.ts` | URL + query `days`/`take`; lỗi 404 ⇒ reject có `response.status` |
| `hooks/__tests__/useDashboard.test.ts` | loading → success; 404 ⇒ `status='error'` + `message.error`; `reload` gọi lại |
| `components/__tests__/MyTasksPanel.test.tsx` | 3 tab hiện **đúng `count`** khi `items` bị cắt; tab rỗng ⇒ `Empty`; nhãn `Quá hạn 3 ngày` / `Hôm nay` / `DD/MM`; không `dueDate` ⇒ không nhãn |
| `components/__tests__/BoardSummaryPanel.test.tsx` | % đúng; board 0 task ⇒ `0%` (**không** chia 0); `boardsTruncated` ⇒ cảnh báo |
| `components/__tests__/RecentActivityPanel.test.tsx` | nhãn tiếng Việt theo `action`; `action` lạ ⇒ hiện chuỗi trần, **không** crash; `authorName = null` ⇒ "Hệ thống" |
| `components/__tests__/ObserverAlertsPanel.test.tsx` | **chỉ** gọi với `kind:'observer'` + `isRead:false`; 0 alert ⇒ `Empty`; nhiều ⇒ `Alert` |
| `pages/__tests__/WorkspaceDashboardPage.test.tsx` | render 4 panel; 404 ⇒ `Result` tiếng Việt (không trắng); thiếu `workspaceId` ⇒ `Result` lỗi |
| `features/auth/pages/__tests__/DashboardPage.test.tsx` | giữ `workspaceApi.list()`; nút vào dashboard đúng URL; **không** còn nút `/manager/ping` |
| `app/__tests__/router.dashboard.test.tsx` | route mới render trong `ProtectedRoute` |

---

## 4. §5 — Search UI

### 4.1 Thư mục **mới**: `frontend/src/features/search/`

| File | Nội dung |
|---|---|
| `types/search.types.ts` | `TaskSearchResponse`, `TaskSearchItem`, `TaskSearchFilters` |
| `services/searchApi.ts` | `searchApi.searchTasks(workspaceId, filters)` — build `URLSearchParams` **giống** `workspaceApi.getActivity` (bỏ tham số rỗng/undefined) |
| `hooks/useTaskSearch.ts` | `filters`, `items`, `nextCursor`, `hasMore`, `loading`, `loadMore`, `reset`; gọi lại khi `filters` đổi (**debounce 300 ms cho `q`**); `loadMore` nối trang + giữ cursor |
| `components/TaskSearchBar.tsx` | `Input` prefix `SearchOutlined` + nút xoá; `data-testid="search-input"` |
| `components/TaskSearchFilters.tsx` | Select **Board** (`boardApi.getBoards`) · Select **Assignee** (`useWorkspaceMembers` + mục "Chưa gán") · Select **Priority** (4 + "Tất cả") · Select **Label** (`boardApi.getLabels`, **chọn nhiều = AND**) · `DatePicker.RangePicker` · `Switch` **"Chỉ task quá hạn"** + **"Ẩn task đã xong"**; nút **"Xoá lọc"** |
| `components/TaskSearchResultList.tsx` | `List` item: title · board + cột (**nhãn "Đã xong"** khi `isDoneColumn`) · `Tag` priority · assignee · hạn · số bình luận; bấm ⇒ `/workspaces/{wsId}/boards/{boardId}`; nút **"Tải thêm"** khi `nextCursor != null` |
| `pages/TaskSearchPage.tsx` | `AppHeader` + `useSearchParams` **đồng bộ filter lên URL** (F5/chia sẻ link giữ nguyên lọc — tiền lệ `ReportsPage` 31–56); phân biệt rỗng bằng `hasQuery` |
| `__tests__/…` | §4.3 |

### 4.2 File **sửa**

| File | Thay đổi |
|---|---|
| `app/router.tsx` | +`/workspaces/:workspaceId/search` |
| `features/board/pages/BoardListPage.tsx` | +nút **"Tìm kiếm"** (`data-testid="nav-search-btn"`) |
| `features/board/components/BoardView.tsx` | **Chỉ thêm 1 nút**: "Tìm trong workspace →" cạnh ô tìm kiếm hiện có (`data-testid="board-search-workspace-btn"`) ⇒ mở `/workspaces/{wsId}/search?boardId={boardId}&q={searchQuery}`. **GIỮ NGUYÊN** toàn bộ lọc client hiện tại (đang có 3 assertion test) |

### 4.3 Test cần viết (~**6 file / ~38 test**)

| File test | Nội dung |
|---|---|
| `services/__tests__/searchApi.test.ts` | URL + **chỉ** tham số có giá trị; CSV `labelIds`; `overdue`/`unassigned` là `"true"` |
| `hooks/__tests__/useTaskSearch.test.ts` | đổi filter ⇒ gọi lại; **debounce**: gõ 3 ký tự nhanh ⇒ **1** request; `loadMore` nối trang; `reset` xoá hết |
| `components/__tests__/TaskSearchFilters.test.tsx` | chọn nhiều label ⇒ mảng id; "Chưa gán" ⇒ `unassigned=true` và **bỏ** `assigneeId`; "Xoá lọc" ⇒ reset |
| `components/__tests__/TaskSearchResultList.test.tsx` | nhãn "Đã xong" khi `isDoneColumn`; không kết quả ⇒ `Empty`; `hasMore=false` ⇒ **ẩn** "Tải thêm" |
| `pages/__tests__/TaskSearchPage.test.tsx` | filter đọc từ URL khi mount; đổi filter ⇒ `searchParams` cập nhật (`replace: true`); `q` rỗng ⇒ "Nhập từ khoá"; **400 từ API ⇒ `message.error` tiếng Việt** |
| `features/board/components/__tests__/BoardView.workspaceSearch.test.tsx` | nút mới build **đúng URL** kèm `boardId` + `q`; **không** đổi hành vi lọc hiện có |

> **Chú ý:** `BoardView` **giữ nguyên** lọc client (tức thời trong 1 board). Trang `/search` là bộ lọc **server-side xuyên board**. **Không** hợp nhất 2 cơ chế trong giai đoạn này.

---

## 5. §6 — @mention UI

| File | Việc |
|---|---|
| `features/board/utils/mentionUtils.ts` (**mới**) | `extractMentionUserIds(text, members)`: quét **tên thành viên theo độ dài GIẢM DẦN**, khớp `"@" + displayName` (biên: ký tự ngay trước `@` **không** phải chữ/số); gom `userId` **không trùng**; bỏ chính mình. **Hàm thuần, không I/O** |
| `features/board/components/TaskDetailModal.tsx` | Ô **soạn** bình luận: `Input.TextArea` → **`Mentions`** của antd v6 (`prefix="@"`, `options.value = displayName`, chỉ member `human`), **GIỮ** `placeholder`/`data-testid`/nút gửi hiện có; submit: `boardApi.createComment(taskId, { content, mentionUserIds: extractMentionUserIds(content, members) })`. Ô **sửa** bình luận: **GIỮ** `Input.TextArea` (sửa mention không gửi lại thông báo ⇒ không cần autocomplete) |
| `features/board/types/board.types.ts` | `CreateCommentRequest` += `mentionUserIds?: string[]` (**append**) |
| `features/ai/utils/notificationLabels.ts` | +`case 'CommentMention': return 'Được nhắc đến'` |
| `features/ai/types/notification.types.ts` | +`'CommentMention'` vào union `NotificationType`; +`mentionedCount` / `mentionUserIds` vào `NotificationPayload` |
| `features/ai/components/NotificationItem.tsx` | **Không đổi logic** — `payload.taskId` đã dùng để điều hướng; chỉ thêm test cho type mới |

> ✅ **`antd@6.6.3` ĐÃ CÓ `Mentions`** (export tên là `Mentions`, không phải `Mention`) — xác nhận tại
> `frontend/node_modules/antd/es/index.js:42` và `es/mentions/index.d.ts`. **KHÔNG** thêm thư viện npm.
> **KHÔNG** dùng `dangerouslySetInnerHTML` — `@tên` chỉ là **văn bản**, hiển thị bình luận giữ nguyên dạng text.

### 5.1 Test cần viết (~**2 file mới + 1 file mở rộng / ~21 test**)

| File test | Nội dung |
|---|---|
| `features/board/utils/__tests__/mentionUtils.test.ts` (**mới**) | tên **1 từ**; tên **có dấu cách**; 2 tên lồng nhau (`An` vs `An Bình`) ⇒ chọn tên **dài**; `@` trong email (`a@b.c`) ⇒ **không** tính; tên xuất hiện 2 lần ⇒ **1** id; **tự** mention ⇒ loại; không mention ⇒ `[]`; **tên trùng** giữa 2 member ⇒ chọn id **đầu tiên** (ghi chú trong doc comment) |
| `features/board/components/__tests__/TaskDetailModal.mention.test.tsx` (**mới**) | gõ `@` ⇒ hiện option thành viên (**không** có AI Agent); chọn ⇒ chèn tên; gửi ⇒ body có `mentionUserIds` đúng; **`TaskDetailModal.test.tsx` cũ vẫn xanh** |
| `features/ai/components/__tests__/NotificationItem.test.tsx` (mở rộng) | type `CommentMention` ⇒ nhãn "Được nhắc đến" + chip `default` (không đỏ) + không crash khi `payload = null` |

---

## 6. Ca biên & chế độ lỗi (frontend phải xử lý đúng)

| Ca | Hành vi chốt |
|---|---|
| Dashboard: người ngoài workspace | **404** ⇒ `<Result>` tiếng Việt, **không** màn hình trắng |
| Dashboard: `days`/`take` ngoài khoảng | server tự clamp — UI **không** cần chặn |
| Dashboard: workspace 0 board / 0 task | `200` với bucket rỗng, `summary` toàn `0` ⇒ `Empty` (không NaN/`Infinity`) |
| Dashboard: 21+ board | `boardsTruncated = true` ⇒ hiện cảnh báo "chỉ hiển thị 20 board đầu" |
| Dashboard: `payload = null` trong feed | **Bình thường** — đừng render gì từ `payload` |
| Dashboard: `authorName = null` | Hiện "Hệ thống" |
| Dashboard: task vừa quá hạn vừa mới giao | **Có mặt ở cả 2 bucket** — **bình thường** (2 lát cắt khác nhau) |
| Dashboard: task quá hạn | **Không** xuất hiện ở tab "Sắp đến hạn" (server đã loại) |
| Search: `q` > 200 ký tự | **400** ⇒ `message.error` tiếng Việt |
| Search: `q` chỉ khoảng trắng | Bỏ lọc, `hasQuery = false` ⇒ hiện "Nhập từ khoá để tìm" |
| Search: 0 kết quả + `hasQuery = true` | `Empty` "Không tìm thấy kết quả" |
| Search: `priority` giá trị lạ | **400** ⇒ `message.error` |
| Search: `labelIds` > 10 / id lạ | **400** ⇒ `message.error` (UI giới hạn chọn ≤ 10) |
| Search: `dueFrom > dueTo` | **400** (UI nên chặn trước bằng `RangePicker`) |
| Search: `cursor` hỏng | **400** ⇒ `message.error` + `reset()` về trang đầu |
| Search: task ở cột done | `isDoneColumn = true` ⇒ hiện nhãn "Đã xong" |
| Search: `labelIds` nhiều nhãn | **AND** — UI ghi rõ "có tất cả nhãn" |
| Mention: `mentionUserIds` chứa AI Agent / người ngoài | **400**, **không** ghi bình luận ⇒ UI **không** đưa AI Agent vào danh sách option |
| Mention: > 20 người | **400** ⇒ `message.error` |
| Mention: tự gõ `@Tên` nhưng không chọn từ autocomplete | Server **không** parse ⇒ **không** có thông báo. Đây là **hành vi mong muốn** (chống mạo danh): autocomplete phải là đường duy nhất |
| Mention: tên hiển thị trùng nhau | Chọn **người đầu tiên** — ghi rõ trong doc comment + có test |
| Notifications: `kind` lạ | **400** |
| Notifications: không truyền `kind` | Hành vi **y hệt** trước Giai đoạn 12 (hồi quy) |
| 401/403/404/400/429 ở mọi trang mới | `message.error` / `<Result>` **tiếng Việt** — **không** màn hình trắng |

---

## 7. §7.2 + §7.3 — CI & tài liệu

### 7.1 ⚙️ CI phải nâng (làm **SAU CÙNG**, khi số thật đã đo)

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | `if ($total -ne 371)` ⇒ **`-ne 423`**; cập nhật comment chuỗi `… → 371 (Giai đoạn 11) → 423 (Giai đoạn 12)` |
| 2 | `.github/workflows/ci-web.yml` | `if ($total -le 279)` ⇒ **`-le <baseline mới>`** (≥ 453) + comment baseline |

> Backend cần đủ **423** test, `Skipped: 0`, `Failed: 0`. Web cần ≥ số test thật sau khi bạn viết xong.

### 7.2 Tài liệu phải cập nhật

| # | File | Việc |
|---|---|---|
| 1 | `Project-Documents/tasks/phase-12-dashboard-search.md` | Điền **số thật** frontend vào §7.1 (backend đã ghi **423**); ghi lại 2 quyết định hiện thực mới (§8 dưới) |
| 2 | `Project-Documents/03-roadmap.md` | Giai đoạn 12: tick `[x]` **3 ô** + baseline thật + ghi **"không migration (9 tổng)"** |
| 3 | `Project-Documents/01-system-specification.md` | §11 **đã cập nhật** (backend phiên này đã chốt ngữ nghĩa bucket + route + mention) — chỉ cần kiểm lại |
| 4 | `Project-Documents/04-database-design.md` | §4 bảng `NotificationType`: +`CommentMention`; §7: +3 gạch đầu dòng (mention tường minh · dashboard read-only không bảng mới · search keyset `(updated_at, id)`); §5: ghi rõ **không** thêm index và **vì sao** |
| 5 | `README.md` | +`## Trạng thái (Giai đoạn 12)`; xác nhận mục Migration vẫn **9** |
| 6 | `Project-Documents/report/phase-12-dashboard-search-test-report.md` | **Tạo khi kết thúc** (theo mẫu `report/phase-11-member-profile-management-test-report.md`) |

### 7.3 Bảng bằng chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 warning / 0 error | ✅ **đạt** |
| 2 | `dotnet ef migrations list` + `has-pending-model-changes` | **9** / sạch | ✅ **đạt** |
| 3 | `dotnet test` với `TEAMNEXUS_TEST_DB` | `Failed 0 / Skipped 0 / Total 423` | ✅ **đạt** |
| 4 | `npm run lint` / `npx tsc -b` / `npm test` / `npm run build` | 0-0 / exit 0 / ≥ 453 / OK | ⬜ **chờ bạn** |
| 5 | **1 lượt thao tác thật, có ảnh**: dashboard 3 tab + tóm tắt board + feed; `/search` lọc nhãn + khoảng hạn rồi "Tải thêm"; gõ `@` ⇒ chọn người ⇒ **chuông nổi số** ⇒ drawer "Được nhắc đến" | — | ⬜ **chờ bạn** |
| 6 | 2 workflow CI xanh | `ci-backend` total = 423, skipped 0 + `ci-web` ≥ baseline mới | ⬜ **chờ bạn** |

---

## 8. ⛔ KHÔNG được làm

- ❌ **Không** thêm migration/bảng/cột/index. `dotnet ef migrations list` phải vẫn **9**.
- ❌ **Không** sửa file backend đã xong (`DashboardService`, `TaskSearchService`, `CommentService`, `NotificationService`, `TaskReadHelpers`, `DashboardEndpoints`, `TaskSearchEndpoints`, `NotificationVocabulary`, `TaskSearchDtos`, `DashboardDtos`, `CommentDtos`).
- ❌ **Không** đổi shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `NotificationListResponse`, `WorkspaceMemberResponse`, `WorkspaceActivity*`, payload/tên event SignalR.
- ❌ **Không** sửa `GET /api/boards/{boardId}/tasks` (shape **mảng**) và **không** sửa `boardStore`/`useBoard`.
- ❌ **Không** sửa `shared/api/httpClient.ts`.
- ❌ **Không** thêm **bất kỳ** thư viện npm nào (dùng `antd` `Mentions`, `dayjs`, `axios` sẵn có).
- ❌ **Không** dùng `dangerouslySetInnerHTML`; `@tên` chỉ là văn bản.
- ❌ **Không** để FE tự tính lại `isOverdue`/`overdueByDays` từ `dueDate` — **dùng số của server** (2 định nghĩa trên cùng một màn hình sẽ tự mâu thuẫn).
- ❌ **Không** đưa AI Agent vào danh sách option mention (server trả **400**).
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**); nếu buộc sửa ⇒ ghi rõ lý do + tên test vào báo cáo.
- ❌ **Không** mở rộng phạm vi sang: Mobile/Flutter (Giai đoạn 13), full-text search/`unaccent`/`pg_trgm`, real-time cho dashboard, đa assignee, hard delete, SSO/SCIM.

---

## 9. Ghi chú kỹ thuật phát sinh khi làm backend (đọc để không bị coi là sai lệch)

1. **`dueSoon` LOẠI TRỪ `overdue`.** Kế hoạch gốc nói ngược lại; khi hiện thực đã chốt 3 tab là một **phân hoạch theo mức khẩn cấp**. Task quá hạn **không** hiện lại ở tab "Sắp đến hạn". Task **vẫn có thể** ở cả "Quá hạn" và "Mới giao".
2. **`days=0`** ⇒ clamp về **1** (không quay về mặc định 3). `days=99` ⇒ **30**.
3. **Search endpoint validate `assigneeId` phải là thành viên workspace ⇒ 400** (kế hoạch gốc chưa nêu; nếu trả trang rỗng thì filter sai sẽ trông y hệt "không có kết quả").
4. **`kind=member` gồm cả `CommentMention`** (4 type), không phải 3.
5. **Sửa test Phase 11 `TheAlertsArePrivateAndReadableOnlyByTheirRecipient`** — lần đầu chạy đủ bộ với DB thật phát hiện **lỗi tiềm ẩn của harness**: sau `AsUserAsync(manager)`, mọi client tạo trước đó (kể cả `memberClient`) đã xác thực **là manager** vì `TestScenario` dùng **chung một cookie jar**. Test cũ chỉ đăng nhập lại cho phía Manager mà quên phía member ⇒ POST cuối nhận **404** thay vì **200**. Đã sửa bằng cách đăng nhập lại **cả hai** phía + viết comment "Trap 3". **Đây là test cũ duy nhất phải sửa** (ghi trong báo cáo theo yêu cầu).
6. **`TestScenario.CreateScenarioAsync(scriptedAi, clock)`** giờ nhận thêm tham số `clock` (optional). Khi có `clock` **hoặc** `scriptedAi`, scenario dựng **host riêng**; không có ⇒ dùng host chung như cũ. `CreateClockScenarioAsync(clock)` là lối vào cho suite dashboard.
7. **`TeamNexusApiFactory.Clock`** (property mới, optional) thay `TimeProvider` qua `ConfigureTestServices` ⇒ thắng `TimeProvider.System` mà `AddBoardModule` đăng ký.
8. **`DatabaseFixture.CreateScriptedAiScenarioAsync`** vẫn giữ nguyên (không đụng) — đường dẫn cũ cho scripted AI.
