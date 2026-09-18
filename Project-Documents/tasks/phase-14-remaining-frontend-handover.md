# Giai đoạn 14 — Bàn giao Frontend cho antigravity (AI Task Chat · Sức khỏe dự án · AI Board Template)

> **Trạng thái:** 📤 **BÀN GIAO — backend đã xong & verify, frontend CHƯA bắt đầu.**
> **Nguồn:** `Project-Documents/tasks/phase-14-ai-advanced.md` (kế hoạch đã chốt) — tài liệu này **không** thay thế kế hoạch.
> Tài liệu này là **hồ sơ hợp đồng API + danh sách các bẫy đã biết** cho phần frontend Giai đoạn 14
> (và vì các giai đoạn sau — đặc biệt **Flutter / Giai đoạn 16** — dùng lại đúng các hợp đồng API mô tả ở §1.2, §2.2, §3.2).
>
> **⛔ Không được làm:** xem §3 — mọi ràng buộc của kế hoạch vẫn hiệu lực (không package mới, không `dangerouslySetInnerHTML`,
> không sửa shape hợp đồng đã verify, không viết lại test cũ đang xanh).

---

## 0. Việc của bạn trong một câu

Ba tính năng **frontend thuần**, gọi **4 endpoint backend đã xong**:

1. **§1** — **"Hỏi AI"** trong `TaskDetailModal`: chat streaming SSE trong ngữ cảnh task + nút lưu câu trả lời thành bình luận (đi qua Accountability Layer).
2. **§2** — **Gauge "Sức khỏe dự án"** trên `WorkspaceDashboardPage`, đọc field `health` **mới append** của `GET .../dashboard`.
3. **§3** — **Modal "Tạo board bằng AI"**: preview cấu trúc board + task khởi đầu, confirm ⇒ Pending ⇒ Manager duyệt.

**Không** làm: lưu transcript chat ở DB, chat nhiều task cùng lúc, chat có tool/function-calling, health score **lịch sử**,
board template tạo **nhiều** board, template tự thêm member, Slack/Discord/GitHub, mobile/Flutter.

---

## 1. Baseline ĐO THẬT — backend đã xong (bạn dựa vào số này)

| Hạng mục | Kết quả đo thật |
|---|---|
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | ✅ **0 Warning / 0 Error** |
| `dotnet test` (PostgreSQL thật, `Skipped: 0`) | `<ĐIỀN SỐ THẬT SAU KHI THI HÀNH>` (baseline đầu kỳ **480**) |
| `dotnet ef migrations list --no-build` | ✅ **10** — **KHÔNG đổi so với Giai đoạn 13** |
| `has-pending-model-changes` | ✅ *"No changes have been made to the model since the last migration."* |
| Frontend baseline **trước khi bạn bắt đầu** (đo lại) | `npm test` = **86 file / 508 test** · `npm run lint` = 0/0 · `npx tsc -b` = exit 0 · `npm run build` = OK |

> ⚠️ `frontend/test-results.json` là **file cũ/stale** (ghi 406). **Số file `*.test.ts(x)` (86) và baseline README (508) mới là số đúng.**
> ⇒ **Baseline frontend của Giai đoạn 14 là 508 / 86 file.**

**Giai đoạn 14 KHÔNG có migration.** Khác Giai đoạn 13 (có 1 cột `users.digest_enabled`) — lần này **cả 3 tính năng
đều không cần schema mới**: chat dùng `ai_action_logs` (bảng đã có) + transcript ở client; health **tính lại** mỗi request;
board template dùng `boards`/`board_columns`/`tasks` sẵn có. Nếu bạn thấy `migrations list ≠ 10` ⇒ **dừng lại và báo ngay**.

---

## 2. Đã có sẵn — **KHÔNG viết lại** (bằng chứng trong repo)

| Hạng mục | Bằng chứng |
|---|---|
| Pattern hook = **useState + useEffect + reload**, **không** react-query | `features/reporting/hooks/useReportSummary.ts` · `features/dashboard/hooks/useDashboard.ts` |
| `httpClient` (axios, baseURL `/api`, tự refresh 401, tự gắn `X-XSRF-TOKEN`) **+ `getXsrfToken()` export sẵn** | `shared/api/httpClient.ts` 16–22, 57–70 |
| `TaskDetailModal` **đã có** `Tabs` (`comments`/`activity`) + `AgentRunPanel` + `AttachmentList` + `useAgentRuns` | `features/board/components/TaskDetailModal.tsx` 36, 57, 501–502, 680 |
| `WorkspaceDashboardPage` **đã có** dải 5 `Statistic` KPI + `useDashboard` | `features/dashboard/pages/WorkspaceDashboardPage.tsx` 148–198 |
| `useDashboard` trả `dashboard` **nguyên** `DashboardResponse` ⇒ field `health` mới **tự động** có (không sửa hook) | `features/dashboard/hooks/useDashboard.ts` |
| `BoardListPage` **đã có** `isManagerOrAdmin` + nút tạo board + `BoardModal` | `features/board/pages/BoardListPage.tsx` 45, 195–223, 349–353 |
| `SmartSetupModal` là **mẫu** cho flow generate → preview → confirm | `features/ai/components/SmartSetupModal.tsx` · `hooks/useSmartSetup.ts` |
| `AiActionLogItem` **đã** phân nhánh theo `log.action` | `features/ai/components/AiActionLogItem.tsx` 143 |
| `useAiActions` (approve/reject/undo + `pendingCount`) dùng lại được **nguyên vẹn** cho action mới | `features/ai/hooks/useAiActions.ts` |
| antd **6.6.3** đã có `Progress` · `Statistic` · `Tooltip` · `Empty` · `Alert` · `Modal` · `Radio` · `Table` · `List` · `Switch` · `Tabs` | `frontend/node_modules/antd` |
| `fetch` / `ReadableStream` / `TextDecoder` / `AbortController` = API trình duyệt, **không** cần package | — |

---

## 3. ⛔ KHÔNG được làm (vẫn hiệu lực — copy từ kế hoạch)

- ❌ **Không** thêm thư viện npm. Gauge dùng `Progress`/`Statistic`/`Tooltip` có sẵn; chat dùng `fetch` có sẵn.
- ❌ **Không** `dangerouslySetInnerHTML` — câu trả lời AI là **text thuần** (giữ `\n` bằng `white-space: pre-wrap`).
- ❌ **Không** sửa shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `ReportSummaryResponse`, payload/tên event SignalR. `DashboardResponse` chỉ được **append cuối** (`health`).
- ❌ **Không** sửa `httpClient.ts` (interceptor). Chat dùng `fetch` **riêng**, chỉ **đọc** `getXsrfToken()`.
- ❌ **Không** sửa `GET /api/boards/{boardId}/tasks` (shape **mảng**), **không** sửa `boardStore`/`useBoard`.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**). **Điều kiện đặc biệt** — 4 file này phải xanh **nguyên trạng** phần cũ:
  `features/board/components/__tests__/TaskDetailModal.test.tsx` ·
  `features/dashboard/pages/__tests__/WorkspaceDashboardPage.test.tsx` ·
  `features/board/pages/__tests__/BoardListPage.test.tsx` (nếu tồn tại) ·
  `features/ai/components/__tests__/AiActionLogItem.test.tsx`.
- ❌ **Không** thêm route mới (mọi đường dẫn cần thiết đã tồn tại).
- ❌ **Không** nâng `ci-web.yml` trước khi xong hết test — nâng **sau cùng**, bằng **số thật** (`-lt <số thật>`, hiện là `-lt 508`).

---

## 4. §1 — Chat streaming "Hỏi AI" (ô **A**)

### 4.1 Quyết định đã chốt (không chọn lại)

| # | Quyết định | Lý do |
|---|---|---|
| **D1** | **Dùng `fetch` + `ReadableStream`, KHÔNG dùng `EventSource`** | `EventSource` **không** gửi được header `X-XSRF-TOKEN` và **không** POST được; backend yêu cầu CSRF trên mọi POST. `fetch` giữ nguyên hợp đồng cookie HttpOnly (`credentials: 'include'`) + header CSRF |
| **D2** | **Transcript ở client** (state của hook), gửi lại **toàn bộ** lịch sử mỗi lượt | Backend **cố ý không** lưu transcript ở DB (không có bảng mới ⇒ vẫn 10 migration). Server **không** có bộ nhớ ⇒ **client là nguồn sự thật** của hội thoại |
| **D3** | **Giữ transcript khi đóng/mở lại tab** (state nằm ở component cha hoặc store, **không** unmount mất dữ liệu) | Người dùng đóng tab rồi mở lại mà mất hội thoại sẽ tưởng tính năng hỏng. Muốn giữ **lâu dài** ⇒ bấm "Lưu thành bình luận" (đó chính là Accountability Layer) |
| **D4** | Tab **thứ 4** `key: 'ai'` nhãn **"Hỏi AI"** trong `Tabs` **hiện có** của `TaskDetailModal` — **không** modal mới | Modal mới sẽ trùng trách nhiệm với `TaskDetailModal` và làm loãng ngữ cảnh task |
| **D5** | "Lưu thành bình luận" chỉ gọi API khi người dùng **bấm**; **không** tự động cho mỗi lượt chat | Backend **cố ý** không tạo Pending cho mỗi lượt (D11 của kế hoạch): mỗi câu hỏi vu vơ thành 1 log sẽ nhấn chìm `AiActionHistoryDrawer` |
| **D6** | Nút "Lưu thành bình luận" **disable** khi `answer.length > 2000` (kèm `Tooltip` giải thích) | `task_comments.content` giới hạn 2000; backend **cũng** trả **400** (không tin client) — disable chỉ để UX tốt hơn |
| **D7** | Gom `delta` **vào câu trả lời đang chạy**; chỉ chốt vào transcript khi nhận `done` | Nếu ghi vào transcript ngay mỗi `delta` sẽ sinh N lần re-render và dễ sinh bong bóng rác khi lỗi giữa luồng |
| **D8** | Lỗi **trước** khi stream mở (400/401/403/404/409/503) ⇒ đọc **JSON** `{error}` và ném lỗi có `status`; lỗi **giữa** luồng ⇒ nhận `event: error` ⇒ `Alert` tiếng Việt + **giữ** phần đã stream | Đúng hợp đồng backend (`DomainExceptionFilter` map status **trước** khi mở stream) |

### 4.2 Hợp đồng API **chốt** (dùng đúng, không đoán)

```http
POST /api/tasks/{taskId}/ai-chat/stream      (Member+ · người ngoài workspace ⇒ 404 · AiChat:Enabled=false ⇒ 503)
  Header: Content-Type: application/json
          X-XSRF-TOKEN: <getXsrfToken()>      ← BẮT BUỘC, thiếu ⇒ 403
  credentials: 'include'
  Body:   { "messages": [ { "role": "user" | "assistant", "content": "..." } ] }
  200 OK → Content-Type: text/event-stream

  event: meta
  data: {"taskId":"…","boardId":"…","workspaceId":"…","taskTitle":"…","historyMessages":3,"model":"deepseek-chat"}
  event: delta
  data: {"text":"Xin "}
  event: delta
  data: {"text":"chào…"}
  event: done
  data: {"answer":"…câu trả lời ĐẦY ĐỦ…","promptTokens":812,"completionTokens":240}
  event: error                       ← chỉ khi lỗi SAU khi stream đã mở
  data: {"error":"DeepSeek không phản hồi trong 60s (timeout)."}

  Lỗi TRƯỚC khi mở stream → JSON + status thật (KHÔNG phải SSE):
    400 { "error": "…" }   401   403 (CSRF)   404 (task/ngoài workspace)   503 (tắt tính năng)
```

```http
POST /api/tasks/{taskId}/ai-chat/message      (Member+ · antiforgery)
  Body: { "content": "… 1..2000 ký tự …" }
  201  → AiActionLogResponse {
           "id":"…","action":"PostComment","entityType":"Task","entityId":"<taskId>",
           "status":"Pending","requestedByUserId":"…","taskCount":0,"createdAt":"…","updatedAt":"…"
         }
  400  → { "error": "…" }
  404  → người ngoài workspace / task soft-deleted
```

**Luật chốt:**
- `messages` **vượt 12 phần tử** hoặc **tổng ký tự > 8000** ⇒ **400** (backend **không** gọi AI ⇒ **0 token**).
  ⇒ Client **nên** tự cắt lịch sử (giữ ~10 lượt gần nhất) để không bao giờ chạm 400.
- `messages` rỗng ⇒ **400**. `role` ngoài `{user, assistant}` ⇒ **400**.
- `done.answer` **==** nối toàn bộ `delta.text` (backend có test khẳng định). Dùng `done.answer` làm sự thật cuối cùng, đề phòng khung `delta` bị mất.
- **KHÔNG** có dữ liệu nghiệp vụ nào bị ghi cho tới khi Manager **Approve** cái `Pending` ở trên.
- Response của `/message` là **`AiActionLogResponse` y hệt** flow Smart Setup ⇒ `useAiActions` + `AiActionLogItem` **dùng lại được ngay**.

### 4.3 File **mới** — `frontend/src/features/ai/`

| File | Nội dung |
|---|---|
| `utils/sse.ts` | **Hàm THUẦN**: `parseSseFrames(buffer: string) => { events: { event: string; data: string }[]; carry: string }`. **Bắt buộc** xử lý: khung bị **cắt giữa 2 chunk** (`carry`), `\r\n` **và** `\n`, dòng bắt đầu bằng `:` (comment ⇒ **bỏ qua**), `data:` **nhiều dòng** (nối bằng `\n`), khung thiếu `event:` ⇒ mặc định `"message"`, `data` JSON hỏng ⇒ **bỏ khung đó, không ném** |
| `utils/aiChatStream.ts` | `streamAiChat({ taskId, messages, signal, onEvent })`: `fetch` POST + header + `credentials:'include'`; **kiểm tra `res.ok` TRƯỚC khi đọc stream** — nếu không ok ⇒ `await res.json()` ⇒ **ném** `Object.assign(new Error(error), { status })`; nếu ok ⇒ `res.body.getReader()` + `new TextDecoder()` + `parseSseFrames` (giữ `carry` giữa các vòng) ⇒ gọi `onEvent({ event, data })`. Hàm **không** tự ghi state |
| `utils/aiChatHistory.ts` | **Hàm THUẦN**: `trimHistory(messages, maxMessages = 10)`, `toRequestMessages(messages)`, `canSaveAnswer(text, max = 2000)` |
| `types/aiChat.types.ts` | `AiChatRole = 'user' \| 'assistant'`; `AiChatMessage { id, role, content, createdAt }`; `AiChatMeta`; `AiChatDone`; `AiChatStreamEvent` (union `meta`/`delta`/`done`/`error`); `SaveAiChatMessageRequest` |
| `hooks/useAiChat.ts` | Pattern `useState + useEffect` (**không** react-query). Trả `{ messages, streamingAnswer, status: 'idle'\|'streaming'\|'saving'\|'error', error, httpStatus, ask(content), cancel(), saveAnswer(), clearError() }`. `AbortController` cho `cancel()`; `ask()` **append** message của user + reset `streamingAnswer` + bắt đầu stream; **khi `done`** ⇒ chốt bong bóng assistant + xoá `streamingAnswer`; **khi `error`** ⇒ `status='error'`, **giữ** `streamingAnswer` để người dùng thấy phần đã nhận |
| `components/AiChatPanel.tsx` | Danh sách bong bóng user/assistant (**plain text**, `white-space: pre-wrap`), vùng đang stream có `aria-live="polite"`, ô nhập `Input.TextArea` + Enter gửi (`Shift+Enter` xuống dòng), nút **Dừng** khi đang stream, nút **"Lưu thành bình luận"** (`data-testid="ai-chat-save-btn"`). Thành công ⇒ `message.success('Đã tạo đề xuất — chờ Manager duyệt trong Lịch sử AI')`. Lỗi ⇒ `message.error`/`Alert` **tiếng Việt** |

### 4.4 File **sửa**

| File | Thay đổi |
|---|---|
| `features/board/components/TaskDetailModal.tsx` | + tab thứ 4 `key: 'ai'`, nhãn **"Hỏi AI"**, icon `CommentOutlined`/`RobotOutlined`, `data-testid="ai-chat-tab"`, render `<AiChatPanel taskId={task.id} />`. **GIỮ NGUYÊN** tab `comments`/`activity`, `AgentRunPanel`, `AttachmentList`, mọi `data-testid` đang có, và **không** đổi tab mặc định |
| `features/ai/components/__tests__/TaskDetailModal` (**chỉ thêm** `describe`) | §4.5 |

### 4.5 Test bắt buộc (~22 test)

| File test (mới) | Nội dung |
|---|---|
| `ai/utils/__tests__/sse.test.ts` | khung trọn vẹn; 2 khung trong 1 chunk; **khung bị cắt qua 2 rồi 3 chunk** (`carry` đúng); `\r\n`; dòng `:` comment bị bỏ; `data:` nhiều dòng; chuỗi rỗng ⇒ `events=[]`; `data` JSON hỏng ⇒ bỏ khung **không** ném; khung thiếu `event:` ⇒ `"message"` |
| `ai/utils/__tests__/aiChatStream.test.ts` | URL/method/`X-XSRF-TOKEN`/`credentials` đúng; **403 ⇒ ném có `status=403` và KHÔNG đọc stream**; chuỗi `meta` → 2 `delta` → `done` ⇒ `onEvent` gọi **4** lần đúng thứ tự; `event: error` giữa luồng ⇒ ném/reject lỗi tiếng Việt; `AbortController.abort()` ⇒ dừng, **không** unhandled rejection |
| `ai/utils/__tests__/aiChatHistory.test.ts` | `trimHistory` giữ 10 lượt **gần nhất**; `toRequestMessages` map đúng `role`/`content`; `canSaveAnswer('')` ⇒ `false`; 2000 ký tự ⇒ `true`; 2001 ⇒ `false` |
| `ai/hooks/__tests__/useAiChat.test.ts` | `ask()` ⇒ `streaming` → `done` ⇒ có bong bóng assistant; `done.answer` dùng làm nội dung cuối; `cancel()` ⇒ dừng, transcript **không** có bong bóng rác; lỗi 400 ⇒ `status='error'` + message tiếng Việt; lượt 2 **gửi kèm** lịch sử (assert body); `saveAnswer()` gọi API đúng body + `status='saving'` rồi `idle`; `saveAnswer` lỗi ⇒ message tiếng Việt |
| `ai/components/__tests__/AiChatPanel.test.tsx` | ô nhập + gửi; Enter gửi / `Shift+Enter` xuống dòng; câu trả lời hiện **dần** rồi chốt; nút Dừng hiện khi đang stream; **"Lưu thành bình luận"** gọi API đúng body; câu trả lời > 2000 ⇒ nút **disable** + `Tooltip` lý do; lỗi ⇒ `message.error` tiếng Việt; 0 hội thoại ⇒ gợi ý mẫu, **không** màn hình trắng |
| `board/components/__tests__/TaskDetailModal.test.tsx` (**+2, KHÔNG sửa test cũ**) | tab **"Hỏi AI"** hiện với Member; chuyển sang tab đó ⇒ `AiChatPanel` mount, tab `comments`/`activity` **vẫn** hoạt động nguyên trạng |

---

## 5. §2 — Gauge "Sức khỏe dự án" (ô **B**)

### 5.1 Quyết định đã chốt

| # | Quyết định | Lý do |
|---|---|---|
| **D9** | Đọc **`health`** từ `GET /api/workspaces/{id}/dashboard` — **KHÔNG** gọi endpoint mới | Backend **append cuối** `health` vào `DashboardResponse` (D15 của kế hoạch) ⇒ `useDashboard` **không cần sửa**, chỉ cập nhật type |
| **D10** | `health = null` ⇒ hiện `Empty` **"Chưa đủ dữ liệu để tính sức khỏe dự án"**, **KHÔNG** hiện 0 điểm | 0 điểm trông như thảm họa; `null` nghĩa là "chưa tính được", hai thứ khác nhau |
| **D11** | Gauge dùng `Progress type="dashboard"` + `Statistic` + `Tooltip` của **antd** | **Không** thêm thư viện npm (ràng buộc Giai đoạn 10/11/12/13) |
| **D12** | Thêm **1 `Col`** cạnh dải KPI, **không** sắp xếp lại 5 `Statistic` cũ | 5 KPI cũ đã có test; đổi thứ tự/nhãn là rủi ro đỏ test vô ích |
| **D13** | Màu theo `band`: `'Tốt'` #10b981 · `'Cần chú ý'` #f59e0b · `'Rủi ro'` #f97316 · `'Nghiêm trọng'` #ef4444. **Không** hard-code theo điểm số — dùng **đúng** `band` server trả | Một nguồn sự thật cho ngưỡng: server quyết định `band`, client chỉ tô màu theo `band` ⇒ không bao giờ lệch |

### 5.2 Hợp đồng API **chốt**

```http
GET /api/workspaces/{workspaceId}/dashboard?days=&take=      (Member+ · 404 người ngoài workspace)

200 → DashboardResponse {
  workspaceId, workspaceName, utcNow, dueSoonDays,
  myTasks, boards, boardsTruncated, recentActivities, summary,   ← 9 field CŨ, KHÔNG đổi tên/kiểu
  health: {                                                      ← FIELD MỚI, APPEND CUỐI
    "score": 72,                        // 0..100, số nguyên
    "band": "Cần chú ý",                // "Tốt" | "Cần chú ý" | "Rủi ro" | "Nghiêm trọng"
    "components": {                      // 5 key, đơn vị = ĐIỂM TRỪ (không phải %)
      "overdue": 20, "atRisk": 6, "stalled": 0, "aging": 2, "load": 0
    },
    "reasons": ["2 thẻ quá hạn", "1 thẻ sắp hết hạn mà không ai cập nhật"]   // tiếng Việt, có thể rỗng
  }
  | null                                 // null ⇒ chưa có dữ liệu để tính
}
```

**Luật chốt (`score` → `band`):** `≥ 80` = `Tốt` · `60–79` = `Cần chú ý` · `40–59` = `Rủi ro` · `< 40` = `Nghiêm trọng`.
`components` = các khoản **điểm trừ** (tổng các khoản + `score` ≈ 100) — hiển thị trong `Tooltip` để giải thích *vì sao* điểm thấp.
`reasons` là **lý do bằng tiếng Việt**; `[]` là bình thường (**không** hiện khối lý do khi rỗng).

### 5.3 File **mới/sửa**

| File | Nội dung |
|---|---|
| `features/dashboard/types/dashboard.types.ts` (**sửa** — **append** alias, không sửa type cũ) | +`DashboardProjectHealth { score: number; band: string; components: Record<string, number>; reasons: string[] }`; `DashboardResponse` += `health: DashboardProjectHealth \| null` |
| `features/dashboard/components/ProjectHealthGauge.tsx` (**mới**) | `Card` + `Progress type="dashboard"` (percent = `score`, `strokeColor` theo `band`) + `Statistic` hiện `band` + `Tooltip` liệt kê **5 khoản trừ** (nhãn tiếng Việt: *Quá hạn · Sắp hết hạn · Đứng yên · Tồn lâu · Lệch tải*) và `reasons`. `health == null` ⇒ `Empty` mô tả *"Chưa đủ dữ liệu để tính sức khỏe dự án"* + `Tooltip` gợi ý. `data-testid="project-health-gauge"` |
| `features/dashboard/pages/WorkspaceDashboardPage.tsx` (**sửa**) | Thêm **1 `Col`** render `<ProjectHealthGauge health={dashboard.health} />`. **GIỮ NGUYÊN** 5 `Statistic` cũ, `AppHeader`, các panel, `data-testid` cũ |
| `features/dashboard/components/__tests__/ProjectHealthGauge.test.tsx` (**mới**) | §5.4 |
| `features/dashboard/pages/__tests__/WorkspaceDashboardPage.test.tsx` (**mở rộng — chỉ thêm**) | §5.4 |

### 5.4 Test bắt buộc (~10 test)

| File test | Nội dung |
|---|---|
| `ProjectHealthGauge.test.tsx` | `score=85` ⇒ `band` `Tốt` + stroke **#10b981**; `65` ⇒ `Cần chú ý` + #f59e0b; `45` ⇒ `Rủi ro` + #f97316; `20` ⇒ `Nghiêm trọng` + #ef4444; `health=null` ⇒ `Empty` **và không** có `Progress`; `components` có **đủ 5** key ⇒ `Tooltip` hiện 5 nhãn; `components={}` ⇒ không vỡ layout; `reasons=[]` ⇒ không hiện khối lý do; `reasons` có 2 mục ⇒ hiện đủ; `score=0` ⇒ vẫn render (không crash, không `NaN`) |
| `WorkspaceDashboardPage.test.tsx` (**+3, KHÔNG sửa test cũ**) | có `health` ⇒ gauge hiện **và** 5 `Statistic` cũ vẫn hiện đủ số; `health=null` ⇒ `Empty`; API lỗi 404 ⇒ vẫn là `Result` 404 như trước (**không** màn hình trắng) |

---

## 6. §3 — Modal "Tạo board bằng AI" (ô **C**)

### 6.1 Quyết định đã chốt

| # | Quyết định | Lý do |
|---|---|---|
| **D14** | Nút **"Tạo board bằng AI"** đặt ở `BoardListPage`, **chỉ** hiện khi `isManagerOrAdmin`, **cạnh** nút tạo board hiện có (`data-testid="board-template-btn"`) | Backend yêu cầu **Manager+** (403 với Member). Đặt cạnh nút tạo board để người dùng không phải đi tìm |
| **D15** | Modal **2 bước**: (1) mô tả dự án, (2) **preview sửa được** (tên board, chọn **đúng 1** cột `isDone`, bỏ task) | Roadmap yêu cầu *"UI preview trước khi confirm"*; và **mọi** thứ AI sinh ra đều **không** đáng tin ⇒ cho sửa trước khi ghi |
| **D16** | **KHÔNG** generate lại khi đóng modal; đóng rồi mở lại ⇒ bắt đầu lại từ bước 1 | Tránh đốt token ngoài ý muốn |
| **D17** | **KHÔNG** tự gọi confirm; người dùng phải bấm **"Xác nhận tạo"** | Confirm chỉ tạo `Pending`, **chưa** tạo board. Phải **nói rõ** cho người dùng: *"Đã tạo đề xuất, chờ duyệt trong Lịch sử AI"* |
| **D18** | Nút **Undo** của action này (trong `AiActionHistoryDrawer`/`AiActionLogItem`) phải có **`Popconfirm`** cảnh báo | Undo = **soft-delete** board vừa tạo. Nếu người dùng đã thêm việc vào board đó, Undo sẽ **ẩn** cả đống việc đó ⇒ phải hỏi trước |
| **D19** | `BoardTemplateProposal.columns` từ server **luôn** có **đúng 1** cột `isDone` ⇒ UI dùng `Radio` (không phải `Checkbox`) khi cho sửa | Backend **pin** đúng 1 cột (D13 của kế hoạch). Dùng `Checkbox` sẽ cho phép trạng thái server không bao giờ chấp nhận ⇒ 400 |
| **D20** | `ColumnName` của task **chọn từ danh sách** cột trong preview, **không** free-text | Backend map theo **tên cột**; free-text sai tên ⇒ task rơi vào cột đầu tiên, người dùng tưởng sai |

### 6.2 Hợp đồng API **chốt**

```http
POST /api/workspaces/{workspaceId}/smart-setup/template        (Manager+ · antiforgery · KHÔNG ghi gì)
  Body: { "description": "… 1..4000 ký tự …" }
  200 → {
    "summary": "…" | null,
    "boardName": "…",
    "boardDescription": "…" | null,
    "columns": [ { "name": "Cần làm", "isDone": false }, … 2..6 mục … ],
    "tasks": [ { "title": "…", "description": "…"|null, "priority": "Low"|"Medium"|"High"|"Urgent"|null,
                 "columnName": "…", "labels": [ { "labelId": "…"|null, "name": "…", "exists": true|false } ],
                 "assignee": { "userId": "…"|null, "displayName": "…"|null, "matched": true|false } | null }, … 5..10 mục … ]
  }
  400 → { "error": "…" }   (description rỗng / > 4000)
  403 → Member không phải Manager/Admin
  404 → workspace không tồn tại hoặc không thấy
  502 → AI lỗi (kể cả JSON hỏng sau 2 lần thử)

POST /api/workspaces/{workspaceId}/smart-setup/template/confirm   (Manager+ · antiforgery)
  Body: { "proposal": <y hệt object 200 ở trên, sau khi người dùng sửa> }
  201 → AiActionLogResponse { "action":"CreateBoardFromTemplate", "entityType":"Workspace",
                              "entityId":"<workspaceId>", "status":"Pending", … }
  400 → proposal không hợp lệ (0 cột / > 6 cột / 0 task / task > trần / title quá dài / priority sai)
  409 → log không còn Pending (chỉ xảy ra ở bước Approve)
```

**Luật chốt:**
- `columns` **2..6**; **đúng 1** `isDone = true`. `tasks` **5..10**; `title` 1..200.
- `priority ∈ {Low, Medium, High, Urgent}` hoặc `null` (backend normalize, **không** nhận giá trị khác).
- `labels[].exists = true` ⇒ `labelId` phải là nhãn **của workspace này**; `exists = false` ⇒ nhãn sẽ được **tạo mới** khi Approve.
- `assignee.matched = false` ⇒ `userId = null` ⇒ **không** gán ai (UI nên cho người dùng chọn lại thành viên).
- Generate **không** ghi gì. Confirm **chỉ** tạo `Pending`. **Approve** (qua `POST /api/ai-actions/{id}/approve` **đã có**) mới tạo board.
- **Approve** tạo: **đúng 1** board + N cột + M task (task gắn đúng cột theo `columnName`).
- **Undo** (`POST /api/ai-actions/{id}/undo` **đã có**) ⇒ board bị **soft-delete** ⇒ biến mất khỏi danh sách board + dashboard + tìm kiếm.

### 6.3 File **mới/sửa**

| File | Nội dung |
|---|---|
| `features/ai/types/boardTemplate.types.ts` (**mới**) | `BoardTemplateColumnProposal` · `BoardTemplateTaskProposal` · `BoardTemplateProposal` · `BoardTemplateRequest` · `ConfirmBoardTemplateRequest` (**khớp 1-1** §6.2) |
| `features/ai/services/boardTemplateApi.ts` (**mới**) | `generateTemplate(workspaceId, description)` → `httpClient.post('/workspaces/'+workspaceId+'/smart-setup/template', { description })`; `confirmTemplate(workspaceId, proposal)` → `.../smart-setup/template/confirm` với body `{ proposal }` |
| `features/ai/hooks/useBoardTemplate.ts` (**mới**) | Pattern `useState + useEffect`. `status: 'idle'\|'generating'\|'preview'\|'confirming'\|'created'\|'error'`; `generate(description)`, `updateProposal(patch)`, `confirm()`, `reset()`, `error`, `httpStatus` |
| `features/ai/components/BoardTemplateModal.tsx` (**mới**) | 2 bước theo D15; `Input.TextArea` với nút generate **disable** khi `description.trim().length < 20`; preview dùng `Table`/`List` cột + task, `Input` cho tên board, `Radio.Group` cho cột `isDone` (D19), `Select` cho `columnName` của task (D20), nút **xóa task**; footer "Xác nhận tạo" ⇒ `confirm()` ⇒ `message.success('Đã tạo đề xuất — chờ Manager duyệt trong Lịch sử AI')`. Mọi nhãn **tiếng Việt có dấu** |
| `features/board/pages/BoardListPage.tsx` (**sửa**) | +`<Button data-testid="board-template-btn">Tạo board bằng AI</Button>` **chỉ** khi `isManagerOrAdmin`, cạnh nút tạo board; +state mở modal. **GIỮ NGUYÊN** nút/modal/handler cũ |
| `features/ai/types/aiAction.types.ts` (**sửa — P8**) | `AiActionType` += `'CreateBoardFromTemplate'` (giữ `'CreateSubtasks'`); `AiActionAppliedSnapshot` += `createdColumnIds?: string[]` **và** `createdBoardId?: string` (backend ghi **cả hai** khi approve board template; Undo đọc `createdBoardId` để soft-delete). ⚠️ Hai field này **vắng mặt** trong snapshot của Phase 4/7 — vì backend nay bỏ khoá `null` khỏi jsonb ⇒ khai báo là **optional**, đừng coi `undefined` là lỗi |
| `features/ai/components/AiActionLogItem.tsx` (**sửa — P8**) | + nhãn/icon riêng cho `'CreateBoardFromTemplate'` (**giữ** nhánh `'CreateSubtasks'`); nút **Undo** của action này bọc `Popconfirm` (**D18**) với nội dung cảnh báo *"Board và mọi thẻ trong đó sẽ bị ẩn…"* |
| `features/ai/components/__tests__/BoardTemplateModal.test.tsx` · `services/__tests__/boardTemplateApi.test.ts` · `hooks/__tests__/useBoardTemplate.test.ts` (**mới**) | §6.4 |
| `features/board/pages/__tests__/BoardListPage.test.tsx` · `features/ai/components/__tests__/AiActionLogItem.test.tsx` (**mở rộng — chỉ thêm**) | §6.4 |

### 6.4 Test bắt buộc (~16 test)

| File test | Nội dung |
|---|---|
| `BoardTemplateModal.test.tsx` | mô tả < 20 ký tự ⇒ nút generate **disable**; mô tả hợp lệ ⇒ gọi API đúng body; preview hiện đúng số cột/task; sửa **tên board** ⇒ body confirm dùng **tên mới**; xoá 1 task ⇒ body confirm **không** có task đó; `Radio` `isDone` **chỉ chọn được 1**; đổi `columnName` của task ⇒ body đúng; `generate` lỗi **403** ⇒ `Alert` tiếng Việt, **không** màn hình trắng; `confirm` lỗi **400** ⇒ giữ preview để thử lại; đóng modal ⇒ không gọi API nào thêm |
| `boardTemplateApi.test.ts` | URL + method + body đúng cho **cả 2** endpoint; body confirm là `{ proposal }` (không phải proposal trần); 400/403/404 ⇒ reject có `response.status` |
| `useBoardTemplate.test.ts` | `generate` ⇒ `generating` → `preview`; `updateProposal` không mất các field khác; `confirm` ⇒ `created`; `confirm` lỗi ⇒ `error` + **giữ** `proposal`; `reset()` về `idle` |
| `BoardListPage.test.tsx` (**+2, KHÔNG sửa test cũ**) | Manager thấy `board-template-btn`; Member **không** thấy nó — **và** nút tạo board cũ vẫn hoạt động |
| `AiActionLogItem.test.tsx` (**+2, KHÔNG sửa test cũ**) | `action='CreateBoardFromTemplate'` ⇒ có nhãn riêng + nút Undo có `Popconfirm`; `action='CreateSubtasks'` ⇒ hành vi **không đổi** |

---

## 7. Route & điều hướng chốt

- **KHÔNG** thêm route mới. `/workspaces/:id/boards` (modal AI) · `/workspaces/:id/dashboard` (gauge) và `TaskDetailModal` (chat) **đã có** ⇒ chỉ **điền thêm** nội dung.
- SignalR **không** đổi; AI Board Template dùng chính endpoint `ai-actions` **đã có** ⇒ drawer "Lịch sử AI" tự thấy action mới.

---

## 8. DoD của phần frontend

| # | Điều kiện | Ngưỡng |
|---|---|---|
| 1 | `npm test` | **0 failed**, tổng = **508 + số test mới** (dự kiến **+4 file / ~48 test**) |
| 2 | `npm run lint` | **0 warning / 0 error** |
| 3 | `npx tsc -b` | **exit 0** |
| 4 | `npm run build` | **thành công** |
| 5 | 4 file test "điều kiện đặc biệt" (§3) | xanh **nguyên trạng** phần cũ |
| 6 | `.github/workflows/ci-web.yml` | nâng `-lt 508` ⇒ `-lt <số THẬT>` (**làm SAU CÙNG**, bằng số đo được) |
| 7 | Kiểm thử tay 1 lượt | (a) mở task ⇒ tab "Hỏi AI" ⇒ hỏi 2 lượt ⇒ câu trả lời hiện **dần**; bấm "Lưu thành bình luận" ⇒ `Pending` trong drawer ⇒ Approve ⇒ bình luận xuất hiện; (b) `/dashboard` ⇒ gauge đúng `band` + `Tooltip` giải thích; (c) `/boards` ⇒ "Tạo board bằng AI" ⇒ generate ⇒ preview ⇒ sửa tên board ⇒ confirm ⇒ Approve ⇒ board mới có **đúng** cột/task; Undo (có `Popconfirm`) ⇒ board biến mất |

---

## 9. Bẫy đã biết (đọc trước khi code)

| # | Bẫy | Cách xử lý |
|---|---|---|
| **B1** | **`EventSource` không gửi được `X-XSRF-TOKEN`** ⇒ 403 | Dùng `fetch` + `getXsrfToken()` (D1). **Đừng** thử `EventSource` rồi kết luận "backend hỏng" |
| **B2** | Khung SSE **bị cắt giữa 2 chunk** TCP ⇒ mất chữ hoặc crash parser | **Bắt buộc** giữ `carry` giữa các vòng đọc (`parseSseFrames` trả `carry`) — có test riêng cho ca này (§4.5) |
| **B3** | `data:` **nhiều dòng** trong 1 khung | Nối bằng `\n` trước khi `JSON.parse` (chuẩn SSE). Test §4.5 |
| **B4** | Lỗi **trước** khi stream mở trả **JSON**, không phải SSE | Kiểm tra `res.ok` **trước** khi `getReader()`; đọc `res.json()` để lấy `{error}` (D8) |
| **B5** | Lịch sử chat vượt **12 phần tử / 8000 ký tự** ⇒ **400** | Client tự `trimHistory` (~10 lượt). Đừng gửi **toàn bộ** hội thoại không giới hạn |
| **B6** | `health = null` — dễ bị `dashboard.health.score` ⇒ **crash** | Type là `DashboardProjectHealth \| null`; **bắt buộc** guard trước khi đọc `.score` (§5.4 có test) |
| **B7** | `components` là **điểm trừ**, không phải % | Đừng nhân 100 hay vẽ thành % — hiển thị thẳng + `Tooltip` ghi rõ *"điểm trừ"* |
| **B8** | `isDone` dùng `Checkbox` ⇒ cho phép **0 hoặc 2** cột done ⇒ server **400** | Dùng `Radio.Group` (D19) |
| **B9** | `columnName` free-text ⇒ task rơi vào cột đầu tiên | Dùng `Select` từ **chính** danh sách cột trong preview (D20) |
| **B10** | Nhãn mới của action `CreateBoardFromTemplate` (P8) — quên ⇒ log hiện nhãn chung | Sửa **cả** `aiAction.types.ts` **và** `AiActionLogItem.tsx`; test §6.4 |
| **B11** | Sửa `Tabs` của `TaskDetailModal` có thể làm đỏ test mount | Chỉ **thêm** item, **giữ** tab mặc định; chạy `npm test -- TaskDetailModal` **trước và sau** |
| **B12** | `frontend/test-results.json` ghi 406 (file cũ) | Đừng lấy số đó làm baseline. Baseline thật = **số file test / số test chạy được**, hiện là **86 / 508** |
| **B13** | Nâng `ci-web.yml` quá sớm ⇒ CI đỏ | Nâng **sau cùng**, bằng **số đo thật** (§8 #6) |

---

## 10. Thứ tự thi hành đề xuất

1. **§8** — đo lại baseline FE (`npm test` / `lint` / `tsc -b` / `build`) **trước khi sửa gì**; ghi lại số.
2. **§5** — gauge (type → component → page → test). Nhỏ nhất, tự chứa, có ngay giá trị.
3. **§4** — `utils/sse.ts` + test **trước** (hàm thuần, dễ sai nhất) → `utils/aiChatStream.ts` + test → `useAiChat` + test → `AiChatPanel` + test → nối vào `TaskDetailModal` (+2 test).
4. **§6** — `types` → `boardTemplateApi` + test → `useBoardTemplate` + test → `BoardTemplateModal` + test → nối vào `BoardListPage` (+2 test) → nhãn/nút Undo trong `AiActionLogItem` (+2 test).
5. **§9** — rà lại **13 bẫy** B1–B13.
6. **§8** — chạy đủ `npm run lint && npx tsc -b && npm test && npm run build`; **nâng `ci-web.yml`** bằng số thật; cập nhật `README.md` mục "Trạng thái (Giai đoạn 14)" + `phase-14-ai-advanced.md` §6.1 với **số thật của FE**.
