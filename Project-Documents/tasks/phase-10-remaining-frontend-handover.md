# Bàn giao phần còn lại — Giai đoạn 10 (Nâng cao Task & Workspace UX)

> **Người nhận:** Antigravity (agent/đội thực thi frontend).
> **Người giao:** phiên làm **backend §1 + §2** (đã xong và verify: **226/226 test PASS, 0 fail, 0 skip** trên PostgreSQL 18 thật).
>
> **Tham chiếu bắt buộc:**
> - `Project-Documents/tasks/phase-10-advanced-task-workspace-ux.md` — **kế hoạch gốc, đọc trước khi code.** Có bảng quyết định **D1–D12**, checklist theo từng file, ca biên, bảng bằng chứng.
> - `Project-Documents/03-roadmap.md` → *Giai đoạn 10* (5 ô hoàn thiện A–E).
> - `Project-Documents/tasks/phase-8-2b-frontend-handover.md` — note bàn giao mẫu trước đó (cùng văn phong, cùng mức chi tiết).
>
> **Phạm vi note này:** **§3 + §4 + §5** — tức **toàn bộ frontend** của Giai đoạn 10, **cộng** 2 việc nhỏ về CI và tài liệu.
> **Backend §1 và §2 ĐÃ XONG** — bạn **không** cần làm lại, và **không được sửa** các file backend (xem §7).

---

## 1. Trạng thái bàn giao

| Mục | Trạng thái |
|---|---|
| **§1 Backend Task UX** | ✅ **XONG** — `TaskFieldsApiTests` (13 method / 18 case). **2 bug thật đã sửa** (`Enum.TryParse` nhận chuỗi số; `dueDate` offset làm 500) |
| **§2 Backend Workspace & Activity** | ✅ **XONG** — `WorkspaceApiTests` (30 method / 37 case). 6 endpoint `/api/workspaces`, activity feed keyset, 3 action cấp workspace |
| **§3 Frontend Task UX** | ⬜ **Chờ bạn** — badge quá hạn · markdown Description · nghi vấn bug ở `TaskDetailModal` |
| **§4 Frontend Workspace Settings & Activity** | ⬜ **Chờ bạn** — 2 trang mới + `useWorkspaceRole` + nút điều hướng |
| **§5 CI (1 dòng) + tài liệu** | ⬜ **Chờ bạn** — nâng cổng `ci-web.yml` + đánh dấu roadmap/README |

### Baseline frontend — **ĐO LẠI NGAY trước khi viết dòng code đầu tiên**

```
cd frontend
npm run lint   → 0 warning / 0 error   (107 file, 116 rule)
npx tsc -b     → exit 0
npm run build  → OK
npm test       → Test Files 37 passed (37) · Tests 206 passed (206)
```

> ⛔ **Điều kiện "xong" của §3+§4 là `npm test` phải ra ≥ 279 test** (206 + 38 ở §3 + 35 ở §4).
> Kèm `lint` = 0/0, `tsc -b` = exit 0, `build` = OK. Đây là DoD cứng, không phải "cố gắng".
> Nếu số đo baseline lệch 206 ⇒ **dừng lại và báo**, đừng tự đoán.

**Backend đã ở 226 test** — nếu bạn vô tình chạm backend, `dotnet test` phải vẫn ra **226**.

---

## 2. §3 — Frontend: Task UX (ô **A**, **C**, **H**)

> Chi tiết đầy đủ: task doc §5. Dưới đây là bản rút gọn để thi hành.

### 2.1 Tạo `frontend/src/features/board/utils/taskDueDate.ts` (hàm **THUẦN**)

```ts
export function isTaskCompleted(task: Pick<TaskResponse,'completedAt'>, isDoneColumn?: boolean): boolean
export function isOverdue(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): boolean
export function overdueDays(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): number
export function dueDateLabel(task: Pick<TaskResponse,'dueDate'|'completedAt'>, now: Date, isDoneColumn?: boolean): string | null
```

- `now` là **tham số** truyền vào — **không** đọc `Date.now()` bên trong ⇒ test tất định, **không** cần `vi.useFakeTimers()`.
- So sánh theo **mốc NGÀY** (`startOf('day')` qua `dayjs`, **đã có sẵn**) ⇒ hạn **hôm nay KHÔNG** phải quá hạn lúc 08:00.
- **Không** import React, không I/O.

### 2.2 Tạo `frontend/src/features/board/utils/markdown.tsx` (thuần + render)

```ts
export type MarkdownNode =
  | { kind: 'text'; value: string } | { kind: 'strong'; value: string }
  | { kind: 'em'; value: string }   | { kind: 'code'; value: string }
  | { kind: 'link'; text: string; href: string }

export function parseInlineMarkdown(text: string): MarkdownNode[]
export function isSafeHref(href: string): boolean
export function renderMarkdown(text: string | null | undefined): React.ReactNode
```

| Cú pháp | Kết quả |
|---|---|
| `**đậm**` | `<strong>` |
| `*nghiêng*` | `<em>` |
| `` `code` `` | `<code>` |
| `[nhãn](https://x)` | `<a href="https://x" target="_blank" rel="noopener noreferrer">nhãn</a>` |
| `\n` | `<br />` |
| `~~gạch~~`, `# H1`, bảng, ảnh | **ngoài phạm vi** ⇒ render nguyên văn, **không ném** |

**Quy tắc an toàn — bắt buộc, sẽ bị review:**
- `isSafeHref` **chỉ** nhận scheme `http:` / `https:` (so sau `trim().toLowerCase()`); `javascript:`, `data:`, `file:`, `vbscript:` ⇒ render **text thường**, **không** tạo `<a>`.
- **Tuyệt đối không** `dangerouslySetInnerHTML`. HTML thô trong mô tả (`<img onerror=…>`) ⇒ hiện **nguyên văn**.
- Cặp dấu chưa đóng (`**x`, `` `x ``, `[x](`) ⇒ giữ nguyên văn, không ném, không cắt cụt.
- `null`/`undefined`/`''` ⇒ trả `null` để UI hiện placeholder "Chưa có mô tả".

### 2.3 Sửa `components/TaskCard.tsx` (ô **A**)

- Thay tính toán `isOverdue` inline (dòng **79–80**) bằng 3 hàm ở §2.1.
- Khi quá hạn **thêm** (không thay thế phần đang có):
  - `borderLeft: '3px solid #ef4444'` trên card.
  - `Tag` đỏ **`data-testid="task-card-overdue-badge"`**, nội dung `Quá hạn N ngày`.
- **Giữ nguyên**: `getPriorityConfig` + badge priority, labels, badge AI Agent, clamp 2 dòng câu hỏi làm rõ, avatar agent, số bình luận, tooltip hạn chót.
- **Giữ nguyên chữ ký props** `{ task, isDoneColumn, onClick, isDragOverlay }` — có 2 chỗ gọi (`KanbanColumn`, `BoardView` drag overlay).
- Task ở cột `is_done` hoặc có `completedAt` ⇒ **KHÔNG** badge đỏ.

### 2.4 Sửa `components/TaskDetailModal.tsx` (ô **C** + ô **H**)

**(a) Ô C — markdown**: thêm `Segmented` **"Soạn" / "Xem trước"** phía trên textarea Mô tả (dòng 438–445); tab xem trước dùng `renderMarkdown(Form.useWatch('description', form))`; rỗng ⇒ "Chưa có mô tả". **Giữ nguyên** `Form.Item name="description"` và `onBlur={handleSaveMetadata}`.

**(b) Ô H — NGHI VẤN BUG, đọc kỹ trước khi sửa:**
Hiện `onChange={handleSaveMetadata}` trên Select priority (dòng 683), DatePicker (703), Select cột (659). AntD đọc giá trị từ `form.getFieldsValue()` **bên trong** `handleSaveMetadata`, mà `onChange` của control chạy **cùng nhịp** ⇒ **payload có thể vẫn là giá trị CŨ**; thao tác chỉ được "cứu" nhờ lần `onBlur` sau đó của Title/Description.

> **Quy trình BẮT BUỘC — không được nhảy bước:**
> 1. Viết test "đổi Priority ⇒ `onUpdateTask` nhận `priority` **MỚI**" **TRƯỚC**.
> 2. Test **đỏ** ⇒ sửa theo hướng `onChange={(v) => { form.setFieldValue('priority', v ?? null); handleSaveMetadata() }}` (làm tương tự cho DatePicker & Select cột). **Không** đổi `onUpdateTask`/`UpdateTaskRequest`.
> 3. Test **xanh** ⇒ **KHÔNG sửa gì**, ghi lại vào báo cáo là *"không tái hiện được với AntD v6"* **kèm tên test**. Việc sửa vô cớ sẽ làm hỏng hành vi đang đúng.

### 2.5 KHÔNG đổi `KanbanColumn.tsx`
Quick-add giữ nguyên `title` + `assignee`. Lý do: ô A/B đã có đường set đầy đủ ở modal; nhồi priority/due date vào ô thêm nhanh làm UI chật mà roadmap **không** yêu cầu.

### 2.6 Test §3 — **+38 test**, 4 file

| File | Số test | Nội dung |
|---|---|---|
| `utils/__tests__/taskDueDate.test.ts` *(mới)* | ~9 | `dueDate = null` ⇒ `isOverdue=false`, label `null`; hạn **hôm nay** ⇒ không quá hạn; **1 ngày trước** ⇒ 1; **3 ngày trước** ⇒ 3; có `completedAt` ⇒ không quá hạn; trong cột `is_done` ⇒ không quá hạn; hạn tương lai ⇒ `DD/MM`; mốc nửa đêm; `isTaskCompleted` |
| `utils/__tests__/markdown.test.ts` *(mới)* | ~11 | 4 cú pháp; trộn cả 4 trong 1 câu; text thuần; `**` chưa đóng ⇒ nguyên văn; `[x](javascript:alert(1))` ⇒ `isSafeHref=false` **và không sinh node link**; `data:text/html,…` ⇒ unsafe; xuống dòng; `''` ⇒ `[]` |
| `utils/__tests__/renderMarkdown.test.tsx` *(mới)* | ~4 | render `<strong>`/`<code>`/`<a href>`; link có `rel="noopener noreferrer"`; HTML thô **không** thành thẻ; `null` ⇒ `null` |
| `components/__tests__/TaskCard.test.tsx` *(**SỬA** — giữ 4 test cũ xanh)* | +5 | quá hạn ⇒ có `task-card-overdue-badge` + chữ `Quá hạn`; task xong ⇒ **không** badge đỏ; hạn tương lai ⇒ `DD/MM`; không hạn ⇒ không render phần hạn; **giữ nguyên** test priority tag & labels |
| `components/__tests__/TaskDetailModal.test.tsx` *(mới)* | ~9 | preview render `<strong>`; toggle Soạn ⇄ Xem trước; rỗng ⇒ placeholder; **đổi Priority ⇒ `onUpdateTask` nhận giá trị MỚI** (test bắt ô H); đổi ngày ⇒ ISO đúng; xoá priority ⇒ `null`; blur description ⇒ gọi update; đổi cột ⇒ gọi `onMoveTask`; vẫn có `data-testid="assignee-select"` |

---

## 3. §4 — Frontend: Workspace Settings & Activity (ô **D**, **E**, **F**, **G**)

> Chi tiết đầy đủ: task doc §6.

### 3.1 Tạo `src/shared/hooks/useWorkspaceRole.ts` (đóng ô **G** — xoá logic copy 2 lần)

```ts
export type WorkspaceRole = 'Admin' | 'Manager' | 'Member'

/** HÀM THUẦN — test không cần render. */
export function resolveWorkspaceRole(
  rows: Array<{ id: string; role: string; ownerId?: string }>,
  workspaceId: string, userId?: string | null
): { role: WorkspaceRole | null; isMember: boolean; isManagerOrAdmin: boolean; isAdmin: boolean; isOwner: boolean }

export function useWorkspaceRole(workspaceId?: string): WorkspaceRoleState & { loading: boolean; reload: () => void }
```

- Gọi `GET /workspaces` (một lần; `useState` + `useEffect` + cờ `ignore` — **cùng pattern hiện có**, **không** thêm React Query).
- `role` so **không phân biệt hoa thường** (backend trả `Enum.ToString()`).
- `isOwner` = `isMember && userId != null && row.ownerId === userId`; **chịu được** response cũ thiếu `ownerId` (⇒ `false`, không ném).

### 3.2 Sửa 3 file để dùng hook trên (giữ **nguyên hành vi**)

| File | Thay gì |
|---|---|
| `features/board/components/BoardView.tsx` **(209–227)** | bỏ `httpClient.get('/workspaces')` + so role; dùng `useWorkspaceRole(workspaceId)` |
| `features/reporting/pages/ReportsPage.tsx` **(46–74)** | như trên; **giữ** `checkingRole` (dùng `loading`) và nhánh `<Result 403>` |
| `features/auth/pages/DashboardPage.tsx` **(29–38)** | dùng hook lấy workspace đầu tiên |

> ⚠️ `BoardView.test.tsx` đang có **phải còn xanh**. Nếu test cũ mock `httpClient.get('/workspaces')` thì **giữ mock tương đương**, **không** viết lại test cũ.
> 🔎 Ô **G** đáng làm vì: cùng một đoạn `get('/workspaces')` + so `role` đang bị **copy nguyên xi ở 2 chỗ**, và §3.3 sắp cần nó ở **4 chỗ** nữa.

### 3.3 Tạo thư mục `src/features/workspace/`

```
types/workspace.types.ts        WorkspaceSummary, WorkspaceDetail, UpdateWorkspaceRequest,
                                TransferOwnershipRequest, WorkspaceActivityItem,
                                WorkspaceActivityPage, WorkspaceRole
services/workspaceApi.ts        list() · get() · update() · transferOwnership() ·
                                deleteWorkspace() · getActivity()
hooks/useWorkspaceDetail.ts     { detail, loading, error, reload, save, transfer, remove }
hooks/useWorkspaceActivity.ts   { items, hasMore, nextCursor, loading, loadMore(), reload() }
components/WorkspaceSettingsModal.tsx
components/ActivityFeedItem.tsx
utils/activityLabels.ts         (hàm THUẦN)
pages/WorkspaceSettingsPage.tsx
pages/WorkspaceActivityPage.tsx
```

**Hợp đồng API thật đã verify — dùng đúng, đừng đoán:**

| Method | Route | Quyền | Trả về |
|---|---|---|---|
| `GET` | `/workspaces` | đăng nhập | `[{ id, name, description, role, ownerId, isOwner }]` |
| `GET` | `/workspaces/{id}` | Member+ | `{ id, name, description, createdAt, updatedAt, ownerId, ownerDisplayName, memberCount, boardCount, currentUserRole }` |
| `PUT` | `/workspaces/{id}` | Manager+ | **204** — body `{ name, description }` (name 1–120 ký tự) |
| `PUT` | `/workspaces/{id}/owner` | owner/Admin | **204** — body `{ newOwnerId }` |
| `DELETE` | `/workspaces/{id}` | owner/Admin | **204** (soft delete) |
| `GET` | `/workspaces/{id}/activity` | Manager+ | `{ items: [...], nextCursor: string \| null, hasMore: boolean }` |

**Chi tiết `GET …/activity`** — query: `boardId?`, `entityType?`, `action?`, `take?` (mặc định 50, trần 200), `before?` (cursor).
Mỗi item: `{ id, boardId, userId, userDisplayName, entityType, entityId, action, payload, createdAt }`.
- Mặc định **sắp mới nhất trước** (`createdAt DESC`).
- Cursor là **chuỗi mờ** — chỉ việc truyền lại qua `before`; **cursor hỏng ⇒ 400** `{ error }` (không 500).
- `userId = null` ⇒ sự kiện do hệ thống ⇒ UI hiển thị **"Hệ thống"**.
- `payload` có thể `null` ⇒ phải chịu được, **không** crash.
- `action` cấp workspace: `WorkspaceUpdated`, `WorkspaceOwnerTransferred`, `WorkspaceDeleted` (**`boardId = null`** ⇒ khi lọc theo board thì các row này bị loại, đúng như thiết kế).

**Quy tắc UI bắt buộc:**
- `workspaceApi` dùng `httpClient` chung ⇒ tự gắn `X-XSRF-TOKEN` cho `PUT`/`DELETE` và tự refresh 401. **KHÔNG tự đặt header CSRF.**
- Đặt tên hàm xoá là **`deleteWorkspace`** (không đặt `delete` — che từ khoá).
- `getActivity` phải **bỏ** mọi param `undefined` khỏi query string.
- **Chuyển ownership**: `Select` member **lọc `memberType === 'human'`** (loại AI Agent); disable chính owner hiện tại; `Popconfirm` xác nhận.
- **Xoá workspace**: `Popconfirm` yêu cầu **gõ đúng tên workspace** mới cho bấm nút xoá. Mọi nhãn **tiếng Việt có dấu**.
- Nhãn action tiếng Việt: `TaskCreated` "đã tạo thẻ" · `TaskUpdated` "đã cập nhật thẻ" · `TaskMoved` "đã chuyển thẻ" · `TaskCompleted` "đã hoàn thành thẻ" · `TaskDeleted` "đã xoá thẻ" · `CommentAdded` "đã bình luận" · `WorkspaceUpdated` "đã cập nhật workspace" · `WorkspaceOwnerTransferred` "đã chuyển quyền sở hữu" · `WorkspaceDeleted` "đã xoá workspace". Action **lạ** ⇒ fallback trả chính chuỗi đó, **không ném**.
- Trang Activity: bộ lọc `Segmented` (Tất cả / Thẻ / Bình luận / Workspace) + `Select` board (lấy từ `boardApi.getBoards`); nút **"Tải thêm"** khi `hasMore` (**không** infinite scroll — D11); `Empty` khi rỗng; `Spin` khi loading.
- Trang Settings: hiển thị owner, số member, số board, ngày tạo.
- **Không phải Manager+** ⇒ `<Result status="403">` + nút quay lại (theo `ReportsPage` 103–120). **Không** để màn hình trắng.

### 3.4 Sửa routing & điều hướng (đóng ô **F**)

| File | Thay gì |
|---|---|
| `app/router.tsx` | thêm `/workspaces/:workspaceId/settings` và `/workspaces/:workspaceId/activity` (đều bọc `ProtectedRoute`) |
| `features/board/pages/BoardListPage.tsx` | thêm nút **"Cài đặt"** (`SettingOutlined`) + **"Hoạt động"** (`HistoryOutlined`) cạnh nút **"Báo cáo"** (172–194); chỉ render khi `isManagerOrAdmin` |
| `features/board/components/BoardView.tsx` | thêm 2 nút tương tự trong nhóm action ở header |

### 3.5 Test §4 — **+35 test**, 6 file

| File | Số test | Nội dung |
|---|---|---|
| `shared/hooks/__tests__/useWorkspaceRole.test.ts` *(mới)* | ~6 | `resolveWorkspaceRole` thuần: Admin/Manager/Member/không-thành-viên/role chữ thường/`ownerId` khớp ⇒ `isOwner`; thiếu `ownerId` ⇒ không ném |
| `features/workspace/services/__tests__/workspaceApi.test.ts` *(mới)* | ~7 | 6 hàm đúng method + URL + body; `getActivity` truyền query đúng và **bỏ** param `undefined` |
| `features/workspace/utils/__tests__/activityLabels.test.ts` *(mới)* | ~5 | mỗi action ⇒ nhãn tiếng Việt; action lạ ⇒ fallback an toàn; payload rỗng ⇒ nhãn trần |
| `features/workspace/components/__tests__/ActivityFeedItem.test.tsx` *(mới)* | ~5 | nhãn + tên actor; actor `null` ⇒ "Hệ thống"; payload `null` không crash; enrich tên cột; icon theo `entityType` |
| `features/workspace/components/__tests__/WorkspaceSettingsModal.test.tsx` *(mới)* | ~8 | validate tên rỗng; Lưu gọi `onSave`; Member ⇒ disable/ẩn tab Thông tin; **danh sách chuyển owner KHÔNG chứa AI Agent**; gõ sai tên ⇒ nút xoá disabled; gõ đúng ⇒ gọi `onDelete` |
| `features/workspace/pages/__tests__/WorkspaceActivityPage.test.tsx` *(mới)* | ~4 | hiển thị items; "Tải thêm" ⇒ gọi `loadMore`; đổi filter ⇒ gọi lại API; không phải Manager ⇒ 403 |

---

## 4. §5 — CI (1 dòng) + tài liệu

### 4.1 `.github/workflows/ci-web.yml` — **bắt buộc, và làm SỚM**

```pwsh
# DÒNG CẦN SỬA (trong step "Assert test count did not regress"):
if ($total -le 187) { throw "Số test đã tụt: $total (baseline cuối Giai đoạn 7 là 187)" }
```

⛔ **Vấn đề:** cổng đang chốt **187**, nhưng baseline thật cuối Giai đoạn 8 đã là **206** — nghĩa là **xoá 19 test vẫn lọt CI**.

✅ **Sửa thành `-le 206`** (và cập nhật comment cho khớp: 187 → Giai đoạn 8: 206 → Giai đoạn 10: **279**).
Nếu làm §3/§4 xong trước rồi mới sửa thì cũng được, nhưng **phải** sửa — nếu không, cổng bảo vệ baseline vẫn vô dụng.

> ℹ️ `.github/workflows/ci-backend.yml` **đã được nâng** ở phiên backend: `if ($total -ne 226)`. **Bạn không cần làm gì** với file đó; chỉ cần biết để không sửa nhầm.

### 4.2 Tài liệu

| # | File | Việc |
|---|---|---|
| 1 | `Project-Documents/tasks/phase-10-advanced-task-workspace-ux.md` | Đánh dấu **§3 / §4 / §5 = XONG**; điền **số test frontend thật** vào §9.1 và §9.2; dòng đầu ghi trạng thái thi hành |
| 2 | `Project-Documents/03-roadmap.md` | Giai đoạn 10: tick các ô **A (Due Date)**, **C (Description)**, **D (Workspace Settings)**, **E (Activity Log UI)** — ô **B** đã tick từ §1 |
| 3 | `README.md` | Mục `## Trạng thái (Giai đoạn 10 …)`: thêm gạch đầu dòng **§3** và **§4/§5**, cập nhật số test frontend thật |
| 4 | `Project-Documents/report/phase-10-advanced-task-workspace-ux-test-report.md` | **Tạo mới** — số test thật (trước/sau), bug thật bắt được (2 bug backend ở §1 + bug frontend nếu có), bằng chứng |

> ⚠️ **Số mục tiêu hiện tại trong tài liệu là `frontend ≥ 279`, `backend ≥ 281`.** Backend thật đang là **226** và sẽ **không đổi** ở §3–§5 ⇒ khi chốt tài liệu, sửa mục tiêu backend thành **226** (đúng, không phải "≥ 281") hoặc ghi rõ "226 là con số cuối của giai đoạn".

---

## 5. Thứ tự thi hành bắt buộc

1. **Đo baseline** (`lint`, `tsc -b`, `test`, `build`) và ghi lại 4 con số.
2. **§3 trước §4** — vì §4.2 refactor `BoardView`/`ReportsPage`, mà §3.3 sửa `TaskCard`; làm §3 trước để cô lập lỗi.
3. Trong §3: **viết test ô H TRƯỚC** khi chạm `TaskDetailModal` (xem §2.4b).
4. **§4**: `useWorkspaceRole` (§3.1) → refactor 3 file (§3.2, chạy test cũ) → `workspaceApi` → hooks → components → pages → router/nút.
5. **§5.1** sửa cổng `ci-web.yml`; **§5.2** cập nhật tài liệu + tạo báo cáo.
6. Chạy **toàn bộ DoD** ở §6 và dán số thật.

---

## 6. Bằng chứng phải nộp

| # | Bằng chứng | Cách đo | Ngưỡng |
|---|---|---|---|
| 1 | `npm run lint` | `frontend/` | 0 warning / 0 error |
| 2 | `npx tsc -b` | `frontend/` | exit 0 |
| 3 | `npm test` | `frontend/` | **≥ 279**, 0 fail — **dán số thật (trước/sau)** |
| 4 | `npm run build` | `frontend/` | OK |
| 5 | `dotnet test tests/TeamNexus.Api.Tests/…csproj` | PostgreSQL 18 thật, đã đặt `TEAMNEXUS_TEST_DB` | vẫn **226 passed / 0 failed / 0 skipped** (chứng minh §3/§4 **không** hồi quy backend) |
| 6 | `dotnet ef migrations list` + `has-pending-model-changes` | `--no-build` | vẫn **8** / không pending |
| 7 | `git diff` trên `ci-web.yml` | — | cổng đã thành `206` |
| 8 | Ảnh/ghi chú 1 lượt thao tác thật | trình duyệt local | badge đỏ quá hạn · preview markdown · đổi tên workspace · chuyển owner · xoá workspace có xác nhận gõ tên · trang Hoạt động + "Tải thêm" |
| 9 | 2 workflow CI xanh | GitHub Actions | `ci-backend` (không skip, `total = 226`) + `ci-web` (`total ≥ 279`) |

**Phải ghi vào báo cáo khi xong:** số test frontend trước/sau, **kết luận về ô H** (tái hiện được ⇒ đã sửa; không tái hiện ⇒ nêu tên test chứng minh), và **bất kỳ phát hiện nào trái với note này** (ví dụ AntD v6 cần chữ ký `onChange` khác, hoặc `BoardView` không truyền được hook như mô tả) — ghi thẳng vào báo cáo, **đừng** tự ý đổi hợp đồng REST/DTO.

---

## 7. ⛔ KHÔNG được làm

- ❌ **Không sửa file backend** (đã verify 226 test): `WorkspaceService.cs`, `WorkspaceActivityService.cs`, `WorkspacesEndpoints.cs`, `WorkspaceDtos.cs`, `TaskService.cs`, `Program.cs`, `BoardModule.cs`, `IActivityLogWriter.cs`, 2 file test backend.
- ❌ **Không sinh migration**, không sửa `04-database-design.md`. `activity_logs.action` là **free text** ⇒ action mới **không** cần constraint.
- ❌ **Không thêm thư viện npm** — đặc biệt **không** cài `react-markdown`/`marked`/editor. `dayjs` · `antd` · `zustand` · `axios` · `@microsoft/signalr` là đủ.
- ❌ **Không dùng `dangerouslySetInnerHTML`** ở bất kỳ đâu.
- ❌ **Không đổi**: `shared/api/httpClient.ts` · `features/reporting/utils/reportDownload.ts` · shape `HubConnectionStatus` · tên event SignalR · kiến trúc 1-hub-1-group · shape `TaskResponse` (17 field, đúng thứ tự).
- ❌ **Không** tự đặt header CSRF trong `workspaceApi`.
- ❌ **Không** viết lại test đang xanh (chỉ **thêm**); nếu buộc phải sửa thì ghi rõ lý do vào báo cáo.
- ❌ **Không** mở rộng phạm vi sang: mời member qua email / đổi role member / kick member (**Giai đoạn 11**); Dashboard / tìm kiếm nâng cao / `@mention` (**Giai đoạn 12**); hard delete workspace; gửi email.
- ❌ **Không** đổi `KanbanColumn.tsx` (xem §2.5).

---

## 8. 🪤 4 cái bẫy backend để lại — đọc để không lặp lại ở frontend

Ghi ở task doc §2.7; tóm tắt phần **áp dụng được cho frontend**:

1. **Seed dữ liệu không đi qua đường thật thì test chứng minh được rất ít.** Ở backend, `CreateTaskAsync` của fixture ghi thẳng bằng EF ⇒ bỏ qua `IActivityLogWriter` ⇒ activity rỗng, test đỏ mà lỗi là ở test. Frontend tương tự: **mock đúng cái `httpClient` mà code thật gọi**, chứ đừng mock tầng khác rồi tưởng đã phủ.
2. **Có những hành vi "không thấy được" là CỐ Ý.** Ở backend, membership của workspace đã xoá bị query filter ẩn đi — đó là hành vi đang kiểm, không phải bug. Frontend: workspace bị xoá ⇒ `GET /workspaces` **không** còn nó; đừng "sửa" bằng cách cache lại.
3. **Wrapper của test framework có thể âm thầm làm test vô nghĩa.** `TestHttpClient` tự gắn lại CSRF header khiến `RemoveAntiforgeryHeader()` vô hiệu ⇒ phải đi vòng qua `client.Http`. Frontend: nếu helper/hook tự set state, assert trên **giá trị cuối cùng** chứ không phải trên mock call.
4. **Thứ tự gọi có thể quan trọng hơn bạn tưởng.** Ở backend `onChange` chạy trước khi Form cập nhật store (ô H). Frontend cũng vậy: khi một callback đọc state **ngay trong** `onChange`, giá trị có thể là **cũ** — đúng thứ mà test ô H sẽ phơi ra.

---

## 9. Sau khi §3+§4+§5 xong

1. Điền số test thật vào task doc §9 + `03-roadmap.md` + `README.md`.
2. Tạo `Project-Documents/report/phase-10-advanced-task-workspace-ux-test-report.md`.
3. Chạy lại toàn bộ bảng bằng chứng §6, dán số thật, rồi mới coi Giai đoạn 10 là **hoàn thành**.
4. Chuyển sang **Giai đoạn 11 (Quản lý Member & Profile)** — cần migration mới (`workspace_invitations`).
