# Giai đoạn 13 — Bàn giao Frontend cho antigravity (Calendar · Burndown/Velocity · Digest toggle)

> **Trạng thái:** ✅ **ĐÃ HIỆN THỰC XONG & VERIFY.**
> Tài liệu này được giữ lại làm **hồ sơ hợp đồng API + danh sách các bẫy đã biết** cho phần frontend Giai đoạn 13
> (và vì các giai đoạn sau — đặc biệt **Flutter / Giai đoạn 16** — dùng lại đúng hai hợp đồng API mô tả ở §5.1 và §6.1).
>
> **Kết quả ĐO THẬT của phần frontend** (xem `report/phase-13-visualization-proactive-notifications-test-report.md`):
> `npm test` = **Failed 0 / Passed 508 / Total 508 (86 file)** (baseline **407 / 77 file** ⇒ **+101 test / +9 file**) ·
> `npm run lint` = **0 warning / 0 error** (212 file) · `npx tsc -b` = **exit 0** · `npm run build` = **thành công** ·
> cổng `ci-web.yml` đã nâng `406` → **`508`**.
>
> **Đối chiếu với các con số ước tính trong tài liệu này:** kế hoạch ước **+53** ⇒ đo thật **+101**. Chênh lệch vì
> ước tính đếm theo *test method* còn thực tế nhiều test có nhiều `it(...)`; **số đo thắng tài liệu**.
> Các mục ✅/⬜ dưới đây đã được đánh dấu theo tình trạng thật.
>
> **Nguồn:** `Project-Documents/tasks/phase-13-visualization-proactive-notifications.md` (kế hoạch đã chốt) — tài liệu này **không** thay thế kế hoạch.
> **⛔ Không được làm:** xem §3 — mọi ràng buộc của kế hoạch vẫn hiệu lực (không package mới, không `dangerouslySetInnerHTML`, không sửa shape hợp đồng đã verify, không viết lại test cũ đang xanh).

---

## 0. Việc của bạn trong một câu

> ✅ **Mục này đã hoàn thành.** Giữ lại để mô tả phạm vi đã giao.

Ba tính năng **frontend thuần**, **không cần API mới nào ngoài 1 endpoint backend đã có sẵn**:

1. **§1** — Tab **"Lịch"** trong `BoardView` (antd `Calendar`) → click ngày mở `/search?dueFrom=&dueTo=`.
2. **§4** — **Biểu đồ Burndown + Velocity** trong `ReportsPage`, dùng endpoint **`GET .../reports/progress-series`** (mới, đã xong ở backend).
3. **§5** — **Toggle email digest** trong `ProfilePage` (tab "Thông báo").

**Không** làm: Calendar kéo–thả, iCal export, digest theo tuần, timezone riêng từng user, mobile/Flutter.

---

## 1. Baseline ĐO THẬT — backend đã xong (bạn dựa vào số này)

| Hạng mục | Kết quả đo thật |
|---|---|
| `dotnet build TeamNexus.sln -m:1 -nr:false` | ✅ **0 Warning / 0 Error** |
| `dotnet test` (PostgreSQL thật, `Skipped: 0`) | ✅ **480** test PASS (baseline đầu kỳ **423** ⇒ **+57**) |
| `dotnet ef migrations list --no-build` | ✅ **10** (mới nhất `20260916045955_Phase13DailyDigest`) |
| `has-pending-model-changes` | ✅ *"No changes have been made to the model since the last migration."* |
| Frontend baseline **đo tại phiên lập kế hoạch** | ✅ **77 file / 407 test PASS** · `npx tsc -b` exit 0 |

> ⚠️ **Lệch nhỏ đã ghi nhận:** `ci-web.yml` và báo cáo Giai đoạn 12 ghi **406**; máy dev đo **407** (1 test thêm sau khi chốt báo cáo). **Số đo thắng tài liệu** ⇒ baseline frontend của giai đoạn này là **407**.

**Phân bổ +57 test backend mới (đo thật từng suite — để bạn tin rằng backend đã được phủ, không phải "chạy được là xong"):**

| Suite | Số test | Nội dung |
|---|---|---|
| `Pure/ReportProgressSeriesTests` | **17** | số học chuỗi thời gian: ngày, tz, clamp, mode ngày/tuần, cap bucket, 0 task |
| `Integration/ReportProgressSeriesApiTests` | **10** | hợp đồng HTTP: 403/404/400/503, scope board, khoảng mặc định |
| `Pure/DigestTemplateTests` | **13** | template digest: escape HTML, giữ dấu tiếng Việt, field thiếu ⇒ không in "null" |
| `Integration/DailyDigestTests` | **14** | dựng nội dung + chống trùng + **không** ghi notification/activity |
| `Integration/ProfileApiTests` | **21 → 24** | **+3**: `digestEnabled` bật/tắt/giữ nguyên |
| *(hồi quy)* | **423** | **không** sửa test nào |

> ℹ️ Con số kế hoạch ước tính **+39**; **đo thật là +57**. **Số đo thắng tài liệu** — chênh lệch đến từ các `[Theory]` (nhiều `InlineData` = nhiều test) mà bản ước tính đếm theo *method*.

**Hai lỗi thật backend bắt được khi viết test (ghi lại để bạn biết suite có giá trị thật):**
1. **`default(DateOnly)` = 0001-01-01** ⟹ nếu dùng `null` cho task chưa đóng, **mọi** task chưa xong bị đếm là "hoàn thành" ở **mỗi** ngày. Sửa bằng sentinel `DateOnly.MaxValue`.
2. **Ngưỡng đổi sang bucket tuần lệch 1 ngày**: `range.Days` đếm *khoảng thời gian* (ceil) còn `totalDays` đếm *ngày lịch bao gồm hai đầu* ⇒ cửa sổ "60 ngày" bị gộp tuần sớm một ngày. Sửa để ngưỡng đọc theo số bucket ngày.

---

## 2. Đã có sẵn — **KHÔNG viết lại** (bằng chứng trong repo)

| Hạng mục | Bằng chứng |
|---|---|
| antd **6.6.3** đã export `Calendar` (`cellRender`, `fullCellRender`, `CellRenderInfo`, `mode` = `'year' \| 'month'`) | `frontend/node_modules/antd/es/calendar/generateCalendar.d.ts` |
| `Segmented`, `Statistic`, `Progress`, `Tooltip`, `Empty`, `Alert`, `Switch`, `Tabs` đã có | `antd` 6.6.3 |
| `dayjs` **1.11.23** đã có | `frontend/package.json` |
| `TaskResponse` đã có `dueDate: string \| null` · `priority` · `isDone` · `completedAt` | `features/board/types/board.types.ts` 21–46 |
| `taskDueDate.ts`: `isOverdue` (so theo **ngày**) · `overdueDays` · `dueDateLabel` | `features/board/utils/taskDueDate.ts` 4–55 |
| Map màu priority của `TaskCard` | `features/board/components/TaskCard.tsx` |
| `TaskSearchPage` **đã** đọc `dueFrom`/`dueTo`/`boardId` từ URL → `searchApi` → `GET /api/workspaces/{id}/tasks/search` | `features/search/pages/TaskSearchPage.tsx` 24–50, 64–96 · `services/searchApi.ts` 33–38 |
| `ReportsPage` **đã** đồng bộ filter lên URL qua `useSearchParams` (`replace: true`) | `features/reporting/pages/ReportsPage.tsx` 31–59 |
| `ProfilePage` **đã** có `Tabs` key `profile` / `workspaces` + `Form` + `profileApi.updateProfile` | `features/profile/pages/ProfilePage.tsx` 125–347 · `services/profileApi.ts` 20–25 |
| Pattern hook = **useState + useEffect + reload**, **không** react-query | `features/reporting/hooks/useReportSummary.ts` · `features/dashboard/hooks/useDashboard.ts` |
| `httpClient` (axios, baseURL `/api`, tự refresh 401, tự gắn `X-XSRF-TOKEN`) | `shared/api/httpClient.ts` |
| `AppHeader` (`title` · `children` · `bell` · `dropdown`) | `shared/components/AppHeader.tsx` |
| `ReportsPage` **đã** gọi `/reports/summary` **chỉ khi** `isManagerOrAdmin` | `ReportsPage.tsx` 39–45 |

---

## 3. ⛔ KHÔNG được làm (vẫn hiệu lực — copy từ kế hoạch)

- ❌ **Không** thêm thư viện npm. **Không** `recharts` / `echarts` / `@ant-design/charts` / `chart.js`. Biểu đồ vẽ bằng `div` + CSS + `Tooltip`/`Statistic` có sẵn.
- ❌ **Không** `dangerouslySetInnerHTML` (không cho Calendar tooltip, không cho digest, không cho bất cứ đâu).
- ❌ **Không** sửa shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `ReportSummaryResponse`, payload/tên event SignalR.
- ❌ **Không** sửa `GET /api/boards/{boardId}/tasks` (shape **mảng**), **không** sửa `boardStore`/`useBoard`.
- ❌ **Không** sửa `TaskSearchPage` / `searchApi` / `search.types.ts` — Calendar chỉ **dùng lại** `dueFrom`/`dueTo`.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**). **Điều kiện đặc biệt:**
  `features/board/components/__tests__/BoardView.test.tsx` (**11 test**) và
  `features/profile/pages/__tests__/ProfilePage.test.tsx` phải xanh **nguyên trạng**.
- ❌ **Không** đổi **mặc định** render của `BoardView` (phải là Kanban) và **không** dùng `Tabs` cho switch view (xem §4 D2).
- ❌ **Không** thêm route mới (mọi đường dẫn cần thiết đã tồn tại).

---

## 4. §1 — Calendar View (ô **A**) — **thuần client, KHÔNG API mới**

### 4.1 Quyết định đã chốt (không chọn lại)

| # | Quyết định | Lý do |
|---|---|---|
| **D1** | Nguồn task = **`filteredTasksByColumn`** của `BoardView` (đã áp `searchQuery` + `priorityFilter`) | Một nguồn sự thật duy nhất ⇒ Calendar **tôn trọng** ô tìm kiếm/filter đang có, không sinh ra bộ lọc thứ hai mâu thuẫn |
| **D2** | Chuyển view bằng **`Segmented`** (`data-testid="board-view-switch"`, giá trị `kanban` \| `calendar`) + **render có điều kiện**, **mặc định `kanban`** | antd `Tabs` **không** render children của tab inactive ⇒ đổi hành vi mount ⇒ rủi ro đỏ **11 test** `BoardView.test.tsx`. `Segmented` là thay đổi nhỏ nhất |
| **D3** | **Không** unmount `useBoard` khi chuyển view | SignalR realtime phải giữ nguyên |
| **D4** | Task **không có** `dueDate` ⇒ **không** lên lưới, hiện ở khối phụ *"N thẻ không có hạn chót"* | Hạn chót là thứ lịch biểu diễn; task không hạn không có chỗ trên lưới. `TaskSearchPage` **không** có filter "không hạn chót" ⇒ nút mở `/search?boardId={boardId}` (không lọc hạn) |
| **D5** | Click **một ngày** ⇒ `/workspaces/{ws}/search?boardId={boardId}&dueFrom={YYYY-MM-DD}&dueTo={YYYY-MM-DD}` | **Đúng** roadmap. Dùng **khoá ngày `YYYY-MM-DD`** (không `toISOString()` có `T00:00:00Z`) vì `TaskSearchPage` parse thẳng chuỗi đó và backend đã `ToUniversalTime()` an toàn |
| **D6** | Click **một thẻ** ⇒ `setActiveTask(task)` ⇒ mở `TaskDetailModal` hiện có | Không modal mới |
| **D7** | Khoá ngày theo **ngày địa phương** của `dayjs(dueDate)` | Nếu khoá theo UTC thì task hạn 23:30 giờ VN sẽ rơi sang ô ngày hôm sau |

### 4.2 File **mới** — `frontend/src/features/board/`

| File | Nội dung |
|---|---|
| `utils/boardCalendar.ts` | **Hàm THUẦN**: `groupTasksByDueDate(tasks: TaskResponse[]) => Map<string /*YYYY-MM-DD*/, TaskResponse[]>` (bỏ `dueDate === null`; khoá = `dayjs(dueDate).format('YYYY-MM-DD')`; **giới hạn 3 item** + `overflowCount`; **thứ tự tất định**: `priority` giảm dần → `title` tăng dần (so sánh `String.prototype.localeCompare` với `'vi'`) → `id`); `buildDaySummary(...)` trả `CalendarDaySummary { key, date, total, items, overflowCount, hasOverdue }` |
| `utils/calendarNavigation.ts` | **Hàm THUẦN**: `buildDaySearchUrl(workspaceId, boardId, dayKey)` ⇒ `/workspaces/{ws}/search?boardId={board}&dueFrom={dayKey}&dueTo={dayKey}` (dùng `encodeURIComponent` như nút "Tìm trong workspace" hiện có) |
| `components/TaskCalendar.tsx` | antd `Calendar`; `cellRender` cho `info.type === 'date'`: tối đa 3 chip (**màu priority dùng lại map của `TaskCard`**) + dấu đỏ khi `hasOverdue` + chip `+N` khi tràn; `onSelect` ⇒ `onDaySelect(dayKey)`; `Empty` tiếng Việt khi board 0 task; khối dưới lịch *"N thẻ không có hạn chót"* + nút mở `/search?boardId={boardId}` |
| `utils/__tests__/boardCalendar.test.ts` | §4.4 |
| `utils/__tests__/calendarNavigation.test.ts` | §4.4 |
| `components/__tests__/TaskCalendar.test.tsx` | §4.4 |

### 4.3 File **sửa**

| File | Thay đổi (**tối thiểu**) |
|---|---|
| `features/board/components/BoardView.tsx` | +state `view: 'kanban' \| 'calendar'` (mặc định `'kanban'`); +`Segmented` ở **Tier 1** (`data-testid="board-view-switch"`, options "Bảng Kanban" / "Lịch"); bọc khối Kanban hiện có (`DndContext` … `DragOverlay`, dòng ~596–678) trong `{view === 'kanban' ? (…) : (<TaskCalendar …/>)}`. **GIỮ NGUYÊN**: toàn bộ Tier 1/Tier 2 hiện có, mọi `data-testid`, mọi nhãn, mọi Modal/Drawer. Truyền vào `TaskCalendar`: `workspaceId`, `boardId`, `tasks` (đã phẳng hoá `filteredTasksByColumn`), `isDoneColumnIds` (từ `columns`), `onTaskClick={setActiveTask}`, `onDaySelect`, `onOpenBoardSearch` |
| `features/board/components/__tests__/BoardView.test.tsx` | **Chỉ THÊM** 1 `describe` (4 test) — **KHÔNG** sửa 11 test hiện có |

### 4.4 Test phải viết (~**3 file mới + 1 file mở rộng / ~26 test**)

| File test | Nội dung bắt buộc |
|---|---|
| `board/utils/__tests__/boardCalendar.test.ts` | 1 ngày nhiều task ⇒ đúng nhóm; `dueDate = null` ⇒ **loại**; 2 task cùng ngày khác giờ ⇒ **cùng** ô; **23:30 giờ địa phương ⇒ ô của ngày đó, KHÔNG sang ngày kế**; > 3 task ⇒ `overflowCount` đúng; `overdue` ⇒ `hasOverdue = true`; `[]` ⇒ `Map` rỗng; **thứ tự** trong ô tất định |
| `board/utils/__tests__/calendarNavigation.test.ts` | URL đúng ngày thường; có `boardId` ⇒ kèm `boardId`; khoá là `YYYY-MM-DD` (**không** `T00:00:00Z`) |
| `board/components/__tests__/TaskCalendar.test.tsx` | task lên đúng ô; task `isDone` ⇒ nhãn "Đã xong"; tràn ⇒ `+N`; click ô **trống** **vẫn** gọi `onDaySelect` (trang search hiện `Empty`, không im lặng); click ô có task ⇒ `dayKey` đúng; click thẻ ⇒ `onTaskClick` đúng task; chuyển tháng ⇒ không crash, **không** gọi API thêm; 0 task ⇒ `Empty` tiếng Việt, **không** màn hình trắng; board chỉ có task không hạn ⇒ khối *"N thẻ không có hạn chót"* |
| `board/components/__tests__/BoardView.test.tsx` (**chỉ thêm**) | mặc định `kanban` (11 test cũ vẫn xanh); chọn `calendar` ⇒ `TaskCalendar` hiện, `KanbanColumn` **không** hiện; quay lại `kanban` ⇒ Kanban trở lại với **đủ** task; `Segmented` có `data-testid="board-view-switch"` |

### 4.5 Ca biên

| Ca | Hành vi |
|---|---|
| `dueDate = null` | Không lên lưới; ở khối phụ |
| Ngày > 3 task | 3 đầu + `+N`; click ngày ⇒ trang search hiện **đủ** |
| Click ô **trống** | **Vẫn** điều hướng ⇒ search hiện "Không tìm thấy kết quả" |
| Task `isDone` | Vẫn có trên lịch + nhãn "Đã xong" (hạn chót vẫn là thông tin) |
| 0 task / 0 cột | `Empty` tiếng Việt |
| Đang gõ ô tìm kiếm | Calendar cập nhật theo (vì nguồn là `filteredTasksByColumn`) |

---

## 5. §4 — Burndown/Velocity trong `ReportsPage` (ô **B**)

### 5.1 Hợp đồng backend **CHỐT** (endpoint đã có, đã test — dùng đúng, không đoán)

```http
GET /api/workspaces/{workspaceId}/reports/progress-series
    ?from=<ISO-8601>&to=<ISO-8601>&boardId=<guid?>&tzOffsetMinutes=<int?>
```
**Quyền: Manager/Admin** (Member ⇒ **403**; ngoài workspace ⇒ **404**; `Reports:Enabled=false` ⇒ **503**).

**Luật tham số:**

| Tham số | Kiểu | Luật chốt |
|---|---|---|
| `from` / `to` | ISO-8601 | vắng ⇒ mặc định `Reports:DefaultRangeDays` (**30 ngày**, `to` = now); `from > to` ⇒ **400**; sai định dạng ⇒ **400** |
| `boardId` | guid? | board **không** thuộc workspace ⇒ **404**; hợp lệ ⇒ `scope.type = "board"` |
| `tzOffsetMinutes` | int? | vắng ⇒ **0** (UTC); **ngoài `[-840, 840]` ⇒ CLAMP (200, không 400)**; không parse được ⇒ **400** |

**Response (camelCase):**

```ts
export type ReportSeriesMode = 'date' | 'week'

export interface ReportDailyProgressPoint {
  date: string          // "2026-01-20" — ngày THEO MÚI GIỜ ĐÃ CHỌN, không phải mốc UTC nửa đêm
  openTasks: number     // số thẻ còn mở ở CUỐI ngày (>= 0)
  completions: number   // số thẻ completed_at RƠI VÀO ngày này
  creations: number     // số thẻ created_at RƠI VÀO ngày này
}

export interface ReportWeeklyProgressPoint {
  weekStart: string     // "2026-01-19" — luôn là THỨ HAI
  completions: number
  creations: number
  openAtEnd: number
}

export interface ReportVelocityResponse {
  avgCompletionsPerWeek: number   // 1 chữ số thập phân
  completedInRange: number
  openAtEnd: number
}

export interface ReportProgressSeriesResponse {
  workspaceId: string
  scope: { type: 'workspace' | 'board'; boardId: string | null; boardName: string | null }
  period: { from: string; to: string; days: number; clamped: boolean }
  mode: ReportSeriesMode
  bucketDays: 1 | 7
  days: ReportDailyProgressPoint[]       // RỖNG khi mode === 'week'
  weeks: ReportWeeklyProgressPoint[]     // RỖNG khi mode === 'date'
  velocity: ReportVelocityResponse
  metricDefinitions: Record<string, string>
  truncated: { bucketCapReached: boolean; maxBuckets: number }
  tzOffsetMinutes: number                // giá trị THỰC DÙNG (đã clamp) — hiển thị nó nếu có cảnh báo
}
```

**Bất biến quan trọng (đã có test backend chứng minh — đừng "sửa" ở FE):**
- `days` và `weeks` **loại trừ nhau**: đúng một trong hai có dữ liệu.
- `mode = 'date'` khi `số ngày lịch <= 60`, `'week'` khi **> 60**.
- `mode = 'date'` + khoảng > **90** ngày ⇒ `truncated.bucketCapReached = true` và **chỉ 90 bucket CUỐI**.
- **`openTasks` KHÔNG trừ `completions` của cùng ngày** ⇒ `openTasks − completions` = *đường lý tưởng* và **luôn ≥ 0**. **Đừng** tự trừ ở client.
- `days`/`weeks` **luôn đủ bucket** (kể cả bucket toàn 0) ⇒ vẽ được biểu đồ liên tục, không nhảy cột.
- Task ở cột `is_done` nhưng `completed_at = null` (dữ liệu cũ) **không** bao giờ nằm trong `openTasks`, và **không** tính là `completions` ở bất kỳ ngày nào. Điều này **đúng** — đừng "sửa" bằng cách tự cộng `done`.

### 5.2 `tzOffsetMinutes` — **bắt buộc** dùng helper, đừng tự đảo dấu

```ts
// features/reporting/utils/timeZoneOffsetMinutes.ts  — HÀM THUẦN, có test
/** `Date.getTimezoneOffset()` trả về UTC − local (VN = -420) ⇒ server cần +420. */
export const timeZoneOffsetMinutes = (date: Date = new Date()): number =>
  -date.getTimezoneOffset()
```

### 5.3 File **mới**

| File | Nội dung |
|---|---|
| `features/reporting/types/reporting.types.ts` (**sửa — CHỈ APPEND**) | +`ReportSeriesMode` · `ReportDailyProgressPoint` · `ReportWeeklyProgressPoint` · `ReportVelocityResponse` · `ReportSeriesTruncationResponse` · `ReportProgressSeriesResponse` · `ReportProgressSeriesParams` (**khớp 1-1** §5.1). **KHÔNG** sửa type cũ nào |
| `features/reporting/services/reportingApi.ts` (**sửa**) | +`getProgressSeries(workspaceId, params)` — build `URLSearchParams` **giống** `workspaceApi.getActivity` (bỏ tham số rỗng) nhưng **`tzOffsetMinutes = 0` VẪN phải gửi** (0 là giá trị hợp lệ, không phải "rỗng" — đây là bẫy dễ mắc) |
| `features/reporting/utils/timeZoneOffsetMinutes.ts` (**mới**) | §5.2 |
| `features/reporting/utils/burndown.ts` (**mới**) | **Hàm THUẦN**: `idealOpenSeries(startOpen, completionsPerBucket)` = `startOpen − Σcompletions`, **kẹp tại 0**; `barHeightPercent(value, max)` — **`max <= 0` ⇒ `0`** (không chia 0/`NaN`/`Infinity`); `seriesBuckets(series)` chuẩn hoá `mode='date'|'week'` thành 1 mảng `{ key, label, completions, openTasks, creations }` (`label`: `DD/MM` cho ngày, `Tuần DD/MM` cho tuần); `velocityStats(series)` |
| `features/reporting/hooks/useReportProgressSeries.ts` (**mới**) | Pattern `useReportSummary` (**useState + useEffect + reload**); tự truyền `tzOffsetMinutes = timeZoneOffsetMinutes()`; `status: 'idle' \| 'loading' \| 'success' \| 'error'`; `workspaceId` rỗng ⇒ **không** gọi API (giữ nguyên cách `ReportsPage` chặn Member) |
| `features/reporting/components/BurndownChart.tsx` (**mới**) | Biểu đồ **cột CSS** (không thư viện): mỗi bucket 1 nhóm cột — `completions` (xanh) + `openTasks` (xám) — `Tooltip` antd hiện số, nhãn trục dưới cùng; đường "lý tưởng" vẽ bằng cột mờ dùng `idealOpenSeries`; `Empty` khi 0 bucket; `Alert` cảnh báo khi `truncated.bucketCapReached`. Nhãn **tiếng Việt có dấu** |
| `features/reporting/components/VelocityPanel.tsx` (**mới**) | 3 `Statistic`: **Năng suất trung bình/tuần** (`velocity.avgCompletionsPerWeek`) · **Thẻ còn mở** (`velocity.openAtEnd`) · **Hoàn thành trong kỳ** (`velocity.completedInRange`) |
| `features/reporting/utils/__tests__/burndown.test.ts` · `timeZoneOffsetMinutes.test.ts` · `hooks/__tests__/useReportProgressSeries.test.ts` · `services/__tests__/reportingApi.progressSeries.test.ts` · `components/__tests__/BurndownChart.test.tsx` · `VelocityPanel.test.tsx` (**6 file mới**) | §5.5 |

### 5.4 File **sửa**

| File | Thay đổi |
|---|---|
| `features/reporting/pages/ReportsPage.tsx` | Render `<BurndownChart/>` + `<VelocityPanel/>` **dưới** `ReportSummaryPanel` (dòng ~164–166), truyền `workspaceId` + `selectedBoardId` + `from`/`to` **đang có**. **Quan trọng:** lỗi của series ⇒ **`Alert` cục bộ**, **KHÔNG** được làm hỏng `ReportSummaryPanel` đang hiển thị (2 request độc lập) |
| `features/reporting/pages/__tests__/ReportsPage.test.tsx` | **Chỉ thêm** 2 test |

### 5.5 Test phải viết (~**6 file mới + 1 file mở rộng / ~23 test**)

| File test | Nội dung bắt buộc |
|---|---|
| `utils/__tests__/burndown.test.ts` | `idealOpenSeries` **không âm**; `barHeightPercent(v, 0) === 0` (không `NaN`/`Infinity`); `seriesBuckets` xử lý `mode='week'` (dùng `weeks`, bỏ `days`) và ngược lại; `[]` ⇒ `[]`, không throw; nhãn `DD/MM` và `Tuần DD/MM` |
| `utils/__tests__/timeZoneOffsetMinutes.test.ts` | mock `getTimezoneOffset() = -420` ⇒ **`+420`**; `0` ⇒ `0` |
| `services/__tests__/reportingApi.progressSeries.test.ts` | URL + query đúng; **`tzOffsetMinutes=0` VẪN được gửi**; `boardId` rỗng ⇒ bỏ; 403 ⇒ reject có `response.status` |
| `hooks/__tests__/useReportProgressSeries.test.ts` | loading → success; 403 ⇒ `status='error'`; `reload` gọi lại; `boardId` đổi ⇒ gọi lại; `workspaceId` rỗng ⇒ **không** gọi |
| `components/__tests__/BurndownChart.test.tsx` | 7 bucket ⇒ 7 nhóm cột; `truncated.bucketCapReached` ⇒ cảnh báo tiếng Việt; 0 bucket ⇒ `Empty`; giá trị 0 ⇒ cột cao **0%** (không vỡ layout); `mode='week'` ⇒ nhãn "Tuần …" |
| `components/__tests__/VelocityPanel.test.tsx` | 3 `Statistic` đúng số; `avgCompletionsPerWeek = 0` ⇒ hiện `0` (không "—" khó hiểu) |
| `pages/__tests__/ReportsPage.test.tsx` (**chỉ thêm**) | series thành công ⇒ biểu đồ hiện; series **lỗi** ⇒ `Alert` **nhưng** `ReportSummaryPanel` **vẫn** hiển thị |

### 5.6 Ca biên

| Ca | Hành vi |
|---|---|
| `range.Days > 60` | `mode='week'` ⇒ cột tuần (đừng cố vẽ 365 cột) |
| `bucketCapReached = true` | Cảnh báo tiếng Việt; chỉ 90 bucket **cuối** |
| `tzOffsetMinutes` bị clamp | response echo giá trị thật ⇒ nếu khác giá trị client gửi, hiện ghi chú nhỏ cho người dùng |
| 0 task / 0 board | `200`, mọi bucket = 0, `avgCompletionsPerWeek = 0`, **không** `NaN`/`Infinity` ⇒ hiện `Empty` hoặc biểu đồ phẳng, **không** màn hình trắng |
| Member | `ReportsPage` **đã** chặn bằng `isManagerOrAdmin` trước khi gọi API ⇒ giữ nguyên; nếu vẫn gọi ⇒ **403** phải thành `Alert` tiếng Việt |
| API lỗi (403/500) | `Alert` tiếng Việt + `ReportSummaryPanel` **vẫn** hiển thị |

---

## 6. §5 — Toggle email digest trong `ProfilePage` (ô **C**)

### 6.1 Hợp đồng backend **CHỐT** (đã có, đã test)

`GET /api/users/me` — **append** field cuối:

```ts
interface UserProfileResponse {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  createdAt: string
  digestEnabled: boolean      // MỚI (append cuối) — mặc định true
}
```

`PUT /api/users/me` — **append** field cuối:

```ts
interface UpdateProfileRequest {
  displayName: string
  avatarUrl?: string | null
  digestEnabled?: boolean | null   // MỚI — VẮNG (undefined) ⇒ GIỮ NGUYÊN giá trị đang lưu
}
```

> ⚠️ **Bẫy quan trọng nhất của ô C:** `digestEnabled` **vắng** ⇒ backend **giữ nguyên**. `false` **chỉ** khi client thật sự muốn tắt. Đây là lý do field này là `boolean | null` chứ không phải `boolean` — nếu bạn gửi `false` ở chỗ đáng lẽ phải bỏ trống, bạn sẽ **âm thầm tắt digest** của người dùng.
> **Khi submit**: gửi **cả 3** field (`displayName`, `avatarUrl`, `digestEnabled`) để không mất giá trị nào.

### 6.2 File **sửa**

| File | Thay đổi |
|---|---|
| `features/profile/types/profile.types.ts` | `UserProfileResponse` += `digestEnabled: boolean`; `UpdateProfileRequest` += `digestEnabled?: boolean \| null` |
| `features/profile/pages/ProfilePage.tsx` | +item thứ **3** trong `Tabs.items`: `key: 'notifications'`, nhãn **"Thông báo"** (`BellOutlined`). Nội dung: `Switch` nhãn **"Email tóm tắt công việc hằng ngày"** (`data-testid="digest-toggle"`) + mô tả *"Gửi mỗi sáng: task quá hạn, sắp đến hạn và vừa được giao cho bạn. Bạn có thể tắt bất cứ lúc nào."*; `onChange` ⇒ `profileApi.updateProfile({ displayName, avatarUrl, digestEnabled })` ⇒ `message.success('Đã bật/tắt email tóm tắt')` / lỗi ⇒ `message.error` **tiếng Việt** + **rollback** `Switch`. Trạng thái lấy từ **`profileApi.getProfile()`** (⚠️ **KHÔNG** tin `useAuthStore`: `/api/auth/me` **không** trả `digestEnabled`) |
| **Bonus bắt buộc để link "Tắt nhận" trong email hoạt động:** `ProfilePage` đọc `window.location.hash === '#notifications'` lúc mount ⇒ mở đúng tab `notifications` | Link trong email digest trỏ tới `{Frontend:BaseUrl}/profile#notifications` |
| `features/profile/pages/__tests__/ProfilePage.test.tsx` | **Chỉ thêm** 4 test |

### 6.3 Test phải viết (~**1 file mở rộng / ~4 test**)

| # | Nội dung |
|---|---|
| **PF-1** | Tab "Thông báo" hiện; `Switch` phản ánh `digestEnabled` từ `GET /api/users/me` |
| **PF-2** | Bật/tắt ⇒ `PUT` có `digestEnabled` đúng **và** `displayName`/`avatarUrl` **giữ nguyên** |
| **PF-3** | API lỗi ⇒ `message.error` tiếng Việt, `Switch` **không** đổi trạng thái (rollback) |
| **PF-4** | `window.location.hash = '#notifications'` ⇒ tab **"Thông báo"** là tab đang mở khi mount (đích của link "Tắt nhận" trong email) |

### 6.4 Ca biên

| Ca | Hành vi |
|---|---|
| `PUT` **không** có `digestEnabled` | **Giữ nguyên** — đã có test backend `PE-3` chứng minh; FE **phải** gửi tường minh khi người dùng bấm `Switch` |
| Lỗi API khi bật/tắt | `message.error` tiếng Việt + rollback `Switch` |
| User chưa từng vào tab | `getProfile()` vẫn phải được gọi (hoặc gọi lazy khi mở tab) — trạng thái ban đầu phải **đúng**, không mặc định `false` |

---

## 7. Điều kiện "xong" của phần frontend (Definition of Done)

| # | Bằng chứng | Ngưỡng | Kết quả **đo thật** |
|---|---|---|---|
| 1 | `npm test` (**trước và sau từng nhóm file**) | `Failed 0`, tổng **≥ 407 + test mới** | ✅ **508 passed / 0 failed / 86 file** (ước tính +53; **đo thật +101**) |
| 2 | `BoardView.test.tsx` **11 test cũ** + `ProfilePage.test.tsx` test cũ | **xanh nguyên trạng** | ✅ **đạt** — 11 test cũ của `BoardView` và 8 test cũ của `ProfilePage` **không sửa dòng nào**; chỉ **thêm** `describe`/`it` mới |
| 3 | `npm run lint` (oxlint) | **0 warning / 0 error** | ✅ **0/0** (212 file) — có 1 warning `set-state-in-effect` ở `useReportProgressSeries` đã được xử lý bằng cách trì hoãn lời gọi sang microtask |
| 4 | `npx tsc -b` | **exit 0** | ✅ **exit 0** |
| 5 | `npm run build` | **thành công** | ✅ **OK** (35.75 s) |
| 6 | **Kiểm thử tay 4 bước** | — | ⬜ **chưa chụp ảnh** — cần chạy `npm run dev` + backend để chụp bằng chứng trực quan |
| 7 | Không có file nào bị sửa ngoài danh sách §4.3 / §5.4 / §6.2 | — | ⚠️ **có 2 file ngoài danh sách** (đã ghi rõ lý do): `utils/taskPriority.ts` (mới — tách bảng màu priority dùng chung, thay vì copy) và `TaskCard.tsx` (sửa **1 dòng import** để dùng helper chung; không đổi hành vi, `TaskCard.test.tsx` vẫn xanh) |

**Số test frontend mới — đo thật:** **+101 test / +9 file**, phân bổ theo file:
`boardCalendar` **18** · `calendarNavigation` **8** · `TaskCalendar` **10** · `BoardView` **+4** ·
`burndown` **20** · `timeZoneOffsetMinutes` **5** · `reportingApi.progressSeries` **6** ·
`useReportProgressSeries` **7** · `BurndownChart` **11** · `VelocityPanel` **6** ·
`ReportsPage` **+2** · `ProfilePage` **+5**.

**File test thay đổi:** 9 file mới + 3 file mở rộng (`BoardView.test.tsx`, `ReportsPage.test.tsx`, `ProfilePage.test.tsx`).

---

## 8. Bàn giao kèm gì

1. **Backend đã xong & verify** — không cần bạn chạm vào `src/**` hay `tests/**`.
2. **2 endpoint đã có, đã test:**
   - `GET /api/workspaces/{id}/reports/progress-series` (Manager+) — §5.1.
   - `GET|PUT /api/users/me` với `digestEnabled` — §6.1.
3. **Không cần API mới** cho Calendar — dùng `GET /api/workspaces/{id}/tasks/search` (**đã có từ Giai đoạn 12**).
4. **Việc còn lại sau frontend — ĐÃ XONG:**
   - ✅ Nâng cổng CI: `.github/workflows/ci-backend.yml` (`-ne 423` ⇒ **`-ne 480`**) và `.github/workflows/ci-web.yml` (`-lt 406` ⇒ **`-lt 508`**).
   - ✅ Cập nhật `Project-Documents/03-roadmap.md` (**tick cả 3 ô + ghi rõ 2 lệch DoD**), `01-system-specification.md` (§12 + hành vi Calendar/digest toggle), `04-database-design.md` (+`users.digest_enabled`, migration thứ 10), `README.md` (Migration = **10**, CI mới).
   - ✅ Viết `Project-Documents/report/phase-13-visualization-proactive-notifications-test-report.md`.
   - ⬜ **Còn lại duy nhất:** chạy lại 2 workflow trên **GitHub Actions** để xác nhận xanh trên CI thật, và chụp ảnh kiểm thử tay 4 bước (§7 #6).

---

## 9. Thứ tự thi hành đề xuất

1. `npm test` **một lần** để xác nhận baseline **407 / 0 fail** trên máy bạn.
2. **§1** Calendar: `boardCalendar.ts` (+test) → `calendarNavigation.ts` (+test) → `TaskCalendar.tsx` (+test) → `BoardView.tsx` `Segmented` (+4 test).
   **Chạy `npx vitest run src/features/board/components/__tests__/BoardView.test.tsx` TRƯỚC và SAU khi sửa `BoardView.tsx`.**
3. **§4** Burndown: `reporting.types.ts` (append) → `timeZoneOffsetMinutes.ts` (+test) → `reportingApi.getProgressSeries` (+test) → `useReportProgressSeries` (+test) → `burndown.ts` (+test) → `BurndownChart.tsx` + `VelocityPanel.tsx` (+test) → `ReportsPage.tsx` (+2 test).
4. **§5** Digest toggle: `profile.types.ts` → `ProfilePage.tsx` (+4 test) → `ReportsPage`/`BoardView` **không** liên quan.
5. `npm run lint` → `npx tsc -b` → `npm test` → `npm run build`.
6. Kiểm thử tay 4 bước (§7 #6) + chụp ảnh.

---

## 10. Câu hỏi thường gặp / bẫy đã biết

| # | Bẫy | Cách đúng |
|---|---|---|
| 1 | Dùng `Tabs` cho switch view ⇒ đỏ 11 test `BoardView` | Dùng **`Segmented`** + render **có điều kiện**, mặc định `kanban` |
| 2 | Gửi `dueFrom`/`dueTo` dạng `toISOString()` (`2026-01-20T00:00:00.000Z`) | Gửi **khoá ngày** `"2026-01-20"` — `TaskSearchPage` parse thẳng chuỗi đó |
| 3 | Tự trừ `openTasks` bằng `completions` để vẽ "đường lý tưởng" | Backend **cố ý không** trừ ⇒ đường lý tưởng = `openTasks − completions`, **kẹp tại 0** |
| 4 | Bỏ `tzOffsetMinutes` khi nó bằng `0` | `0` là giá trị **hợp lệ** — vẫn phải gửi (nếu không, backend hiểu là UTC, nhưng điều đó chỉ đúng **tình cờ**) |
| 5 | Gửi `digestEnabled: false` khi người dùng chỉ sửa tên hiển thị | Chỉ gửi `digestEnabled` khi người dùng thật sự bấm `Switch`; còn lại **bỏ trống** ⇒ backend giữ nguyên |
| 6 | Lấy `digestEnabled` từ `useAuthStore` | `/api/auth/me` **không** trả field này ⇒ phải `profileApi.getProfile()` |
| 7 | Lỗi request series làm trắng cả `ReportsPage` | 2 request **độc lập**: series lỗi ⇒ `Alert` cục bộ, `ReportSummaryPanel` vẫn hiển thị |
| 8 | Cố vẽ 365 cột khi người dùng chọn khoảng 1 năm | `mode='week'` (backend tự chuyển) ⇒ vẽ cột tuần |
| 9 | `barHeightPercent(v, 0)` khi board 0 task | Guard: `max <= 0` ⇒ `0%` (không `NaN`, không vỡ layout) |
| 10 | Thêm `recharts` vì "vẽ cột bằng CSS trông thủ công" | **⛔ Không được** — ràng buộc cứng của giai đoạn. `div` + CSS + `Tooltip` là đủ cho 2 chỉ số |

---

## 11. Liên hệ với kế hoạch đầy đủ

Tài liệu này là **bản rút gọn dành cho người làm frontend**. Nguồn chuẩn (đầy đủ quyết định D1–D14, ca biên, rủi ro, bằng chứng) là:

`Project-Documents/tasks/phase-13-visualization-proactive-notifications.md`
→ các mục **§3** (Calendar) · **§4.3** (hợp đồng API series) · **§6** (Burndown + Digest toggle) · **§9** (⛔ không được làm).
