# Bàn giao §5 — Frontend "AI Agent Executor" (Giai đoạn 7)

> **Người nhận:** agent/đội thực thi frontend (Antigravity).
> **Người giao:** phiên làm backend §2–§4 + verify §7 (đã xong, **270 → 291 check PASS**).
> **Phạm vi:** chỉ **frontend** (`frontend/`). Backend §2–§4 **đã xong và đã verify trên API thật + PostgreSQL 18 thật**;
> **không** cần sửa backend, **không** sinh migration, **không** thêm route.
> **Nguồn chuẩn:** `Project-Documents/tasks/phase-7-ai-agent-executor.md` §5.1 (**hợp đồng đã đóng băng**) + §5.2 (checklist),
> `Project-Documents/report/phase-7-ai-agent-executor-test-report.md` §2.3 (bằng chứng verify).
>
> **Việc phải làm, tóm tắt:** (A) dropdown đổi người thực hiện — hạng mục **bắt buộc**, là nửa còn lại của ô roadmap;
> (B) trạng thái "Chờ làm rõ" + nút **"Chạy lại"** trên Kanban; (C) panel chạy Agent + real-time + duyệt kết quả + tệp đính kèm;
> (D) chất lượng (lint/tsc/build/test, số test **phải tăng** so với baseline 155).

---

## 1. Trạng thái bàn giao & điều kiện tiên quyết

| Mục | Trạng thái |
|---|---|
| Backend §2 (schema), §3 (Board), §4 (Module Ai), §6 (config) | ✅ **XONG** — build 0/0, `migrations list` = 6 |
| Verify §7 nhóm A–I + gọi thật DeepSeek & Tavily | ✅ **XONG** — **291 check PASS** (chi tiết §7 dưới đây) |
| **§5 frontend (nhóm J)** | ⬜ **CHÍNH LÀ VIỆC CỦA BẠN** — chưa có dòng code frontend nào cho giai đoạn 7 |
| Baseline frontend hiện tại (**bắt buộc chạy lại trước khi bắt đầu**) | `npm run lint` → **0/0** · `npx tsc -b` → **exit 0** · `npm run build` → **OK** · `npm test` → **30 files / 155 tests PASS** |

**Không được làm:** không sửa backend, không đổi hợp đồng DTO (nếu buộc phải đổi thì **sửa cả hai phía** và nói rõ trong PR),
không thêm thư viện UI mới, không viết lại `httpClient`/`reportDownload`, không tự set header CSRF (đã tự động), không dùng
`window.open` để tải file.

---

## 2. Hợp đồng API đã verify (dùng đúng như dưới đây)

### 2.1 Bảy route của Agent (đã verify: 202/401/403/404/405/409/503 đúng như ghi)

| # | Method + path | Quyền | Thành công | Ghi chú đã verify |
|---|---|---|---|---|
| 1 | `POST /api/tasks/{taskId}/agent-runs` | Manager/Admin | **202** `AgentRunResponse` | 202 **không** phải 200; body là row `status="Running"`; `X-XSRF-TOKEN` do `httpClient` tự gắn |
| 2 | `POST /api/tasks/{taskId}/agent-runs/{runId}/rerun` | Manager/Admin | **202** `AgentRunResponse` | nút **"Chạy lại"**; run mới có `id` khác, `previousRunId` = run cũ |
| 3 | `GET /api/tasks/{taskId}/agent-runs?take=` | Member+ | 200 `AgentRunResponse[]` | `take` clamp **1–50**, mặc định **20**; sort `startedAt DESC`; `take=abc` cũng OK (fallback mặc định) |
| 4 | `GET /api/agent-runs/{runId}` | Member+ | 200 `AgentRunDetailResponse` | kèm `toolCallTrace`, `previousQuestion`, `resolutionCommentContent` |
| 5 | `POST /api/agent-runs/{runId}/cancel` | Manager/Admin | 200 `AgentRunDetailResponse` | response **đã** là `status="Failed"`, `stopReason="Cancelled"` (không cần poll); huỷ lần 2 ⇒ 409 |
| 6 | `GET /api/tasks/{taskId}/attachments` | Member+ | 200 `AttachmentResponse[]` | sort `createdAt DESC` |
| 7 | `GET /api/tasks/{taskId}/attachments/{attachmentId}/download` | Member+ | 200 bytes | **GET** (không CSRF) ⇒ dùng đúng pattern `responseType:'blob'` của `utils/reportDownload.ts` |

### 2.2 Route có sẵn được **tái dùng** (không viết mới)

| Route | Dùng cho |
|---|---|
| `GET /api/workspaces/{workspaceId}/members` | nguồn **duy nhất** cho dropdown assignee (đã có `memberType`) |
| `GET /api/workspaces/{workspaceId}/boards/{boardId}` | tải board (task đã kèm `assigneeIsAiAgent`/`activeAgentRunId`) |
| `GET /api/boards/{boardId}/tasks`, `GET /api/boards/{boardId}/tasks/{taskId}` | 2 đường còn lại trả task (đã kèm 2 field mới) |
| `GET /api/ai-actions/{logId}`, `POST /api/ai-actions/{logId}/approve\|reject\|undo` | **drawer duyệt** — tái dùng nguyên `features/ai` (`AiActionLogItem`, `useAiActions`) |
| `GET /api/notifications`, `POST /api/notifications/{id}/read` | badge/notification center có sẵn |

### 2.3 Kiểu TS phải mirror (đã khoá — chép đúng §5.1a của task doc)

```ts
export type MemberType = 'human' | 'ai_agent'
export type AgentRunStatus = 'Running' | 'AwaitingClarification' | 'AwaitingApproval' | 'Completed' | 'Failed'
export type AgentStopReason =
  | 'DraftProduced' | 'QuestionAsked' | 'ToolLimit' | 'TimeLimit' | 'TokenBudget'
  | 'ProviderError' | 'Cancelled' | 'TaskChanged' | 'InternalError'

// thêm vào TaskResponse (2 field ở CUỐI, backend đã trả):
assigneeIsAiAgent: boolean
activeAgentRunId: string | null

// thêm vào ColumnResponse:
isClarification: boolean

// thành viên workspace (đã có ở backend):
memberType: MemberType

export interface AgentRunResponse {
  id: string; taskId: string; boardId: string
  agentUserId: string; agentDisplayName: string
  triggeredByUserId: string; triggeredByName: string
  status: AgentRunStatus; stopReason: AgentStopReason | null
  clarificationQuestion: string | null
  aiActionLogId: string | null; outputKind: 'Comment' | 'Attachment' | null
  error: string | null
  toolCallCount: number; llmCallCount: number
  promptTokens: number; completionTokens: number; totalTokens: number
  startedAt: string; finishedAt: string | null; traceTruncated: boolean
}
export interface AgentToolTraceEntryResponse {
  name: string; arguments: string | null; resultSummary: string | null
  isError: boolean; at: string; durationMs: number
}
export interface AgentRunDetailResponse {
  run: AgentRunResponse
  toolCallTrace: AgentToolTraceEntryResponse[]
  previousRunId: string | null
  previousQuestion: string | null
  resolutionCommentContent: string | null
}
export interface AttachmentResponse {
  id: string; taskId: string; fileName: string; contentType: string; sizeBytes: number
  createdByUserId: string; createdByName: string; sourceRunId: string | null; createdAt: string
}
```

### 2.4 Sự kiện SignalR (nhóm `board-{boardId}`, tên event **`AgentRunProgress`**)

```ts
export interface AgentRunProgressEvent {
  runId: string; taskId: string; boardId: string
  status: AgentRunStatus; stopReason: AgentStopReason | null
  toolCallCount: number; totalTokens: number
  clarificationQuestion: string | null
}
```

> ⚠️ **Event chỉ là tăng tốc, KHÔNG phải nguồn sự thật.** Backend chạy in-process và app free-tier có thể ngủ ⇒ mất event.
> Mọi màn hình Kanban **phải** `GET /api/tasks/{taskId}/agent-runs?take=1` khi mount **và** khi SignalR reconnect.
> Backend broadcast **2 lần** cho mỗi run (lúc `Running` và lúc kết thúc) — không stream token.

### 2.5 Mã lỗi → UX (message thật, đã verify)

| Status | Khi nào | `error` thật | UX |
|---|---|---|---|
| 400 | Task chưa gán agent | `"Task is not assigned to the AI Agent."` | `message.warning` + mở dropdown assignee |
| 400 | Rerun mà chưa có câu trả lời | `"No answer found for the clarification request."` | nhắc trưởng nhóm trả lời bằng comment trước |
| 403 | Member gọi route ghi **hoặc** thiếu CSRF | `"Requires Manager or Admin role in this workspace."` / `"CSRF token missing or invalid…"` | **ẩn** nút "Chạy Agent"/"Huỷ"/"Chạy lại" với Member |
| 404 | task/run/attachment không thấy | `"Task not found."` / `"Agent run not found."` / `"Attachment not found."` | `Alert` + reload board |
| 409 | Đang có run cho task này | `"An agent run is already in progress for this task."` | disable nút + hiện "Đang chạy" |
| 409 | Huỷ run đã kết thúc | `"Only a Running agent run can be cancelled."` | đọc lại run rồi ẩn nút "Huỷ" |
| 503 | `Agent:Enabled=false` | `"AI Agent Executor is disabled."` | `Alert` "Tính năng AI Agent đang tạm tắt" (ẩn nút ghi) |

---

## 3. Mười một sự thật đã verify mà UI **phải** tôn trọng (nếu không sẽ là bug im lặng)

1. **`activeAgentRunId` là "run đang sống", không phải "đang chạy":** nó được điền cho `Running` **và**
   `AwaitingClarification` **và** `AwaitingApproval`. Task có run `Failed` ⇒ `null`. Dùng nó để hiện badge, **không** dùng để
   suy ra `status` — phải GET run để biết trạng thái thật.
2. **`status = "Completed"` không bao giờ được backend đặt ở giai đoạn này.** Sau khi duyệt, run **vẫn** `AwaitingApproval`
   và phán quyết nằm ở `ai_action_logs` (đọc qua `agentRun.aiActionLogId` → `GET /api/ai-actions/{logId}` →
   `status ∈ Pending|Approved|Rejected|Undone`). ⇒ Panel phải hiển thị **cả hai**: trạng thái run và trạng thái quyết định.
3. **Kết quả do AGENT tạo:** `task_comments.author_id` / `task_attachments.created_by_user_id` = **agent user**;
   `authorName`/`createdByName` = `AgentDisplayName` (`"TeamNexus Agent"` mặc định). Không được ghi công cho người duyệt.
4. **Hai field mới đã có ở CẢ 3 đường trả task** (`GET /boards/{id}`, `GET /boards/{id}/tasks`, `GET /boards/{id}/tasks/{id}`)
   ⇒ card Kanban **không** cần gọi thêm API để biết assignee là agent.
5. **Agent là một thành viên thật trong `GET /members`**, `memberType='ai_agent'`, **luôn nằm cuối** danh sách, và **được tạo
   ở lần gọi `/members` đầu tiên**. Vì vậy: sau khi user đổi assignee sang agent (hoặc sau lần đầu mở dropdown), **refetch
   members** để danh sách có agent; đây là lý do dropdown phải có nút/mục "AI Agent" ngay từ đầu nếu members chưa có.
6. **Cột "Chờ làm rõ":** `isClarification=true`, **tối đa 1 cột/board**, tạo **lazy** ở cuối board (position = max+1) khi agent
   hỏi lần đầu; **không xoá được** (409) và **không** được bật cùng `isDone` (400). UI phải xử lý trường hợp cột này **xuất hiện
   sau** khi board đã render (nguồn: event `ColumnCreated` hoặc refetch board sau khi run kết thúc `AwaitingClarification`).
7. **Câu hỏi làm rõ là một comment của agent**, và task **đã được move** sang cột clarification. `clarificationQuestion` trong
   run là **bản sao** để hiển thị nhanh; nguồn sự thật là comment (`clarificationCommentId` không trả ra API — dùng
   `GET /api/tasks/{taskId}/comments`).
8. **`GET /api/ai-actions/{logId}` trả về nguyên `afterSnapshot`**, kể cả `contentBase64` của tệp đính kèm (tối đa ~700 KB
   jsonb). ⇒ **Không** render chuỗi base64; chỉ hiển thị metadata (tên file/kích thước) và dùng route 7 để tải bytes.
9. **Duyệt/Từ chối/Hoàn tác đã có sẵn** ở `features/ai` (`AiActionLogItem` + `useAiActions`). Panel Agent chỉ cần truyền
   `aiActionLogId` vào đúng component đó — **không** dựng lại UI accountability, **không** tự gọi `approve` bằng tay.
10. **Thông báo mới** (1 row / 1 Manager): `AgentRunFailed`, `AgentAwaitingClarification`, `AgentOutputPending`
    ⇒ thêm nhãn tiếng Việt trong `NotificationItem.tsx`, nhớ **giữ mã gốc trong ngoặc** để truy vết.
11. **`take` của lịch sử run**: truyền `1` khi chỉ cần run mới nhất (nhanh, 1 row). Backend **không** trả `previousRunId`
    trong `AgentRunResponse` — chỉ `AgentRunDetailResponse` có.

---

## 4. Việc phải làm (theo §5.2 của task doc) — kèm đường dẫn file chính xác

### A. Dropdown chọn người thực hiện — **hạng mục bắt buộc** (S1)

| File | Việc |
|---|---|
| `frontend/src/features/board/types/board.types.ts` | thêm `MemberType`; 2 field cuối của `TaskResponse`; `isClarification` của `ColumnResponse`; `isClarification?` cho `CreateColumnRequest`/`UpdateColumnRequest` |
| `frontend/src/features/board/services/boardApi.ts` | thêm `getMembers(workspaceId)`, `getTaskAttachments(taskId)`, `downloadAttachment(taskId, attachmentId)` (blob) |
| `frontend/src/features/board/hooks/useWorkspaceMembers.ts` **(mới)** | cache theo `workspaceId`, gọi **1 lần cho cả board** (không gọi theo task) |
| `frontend/src/features/board/components/TaskDetailModal.tsx` | **thay khối read-only ở dòng ~615–621** (`task.assigneeName`) bằng `Select`: avatar + `Tag color="purple"` "AI Agent" khi `memberType === 'ai_agent'`, `allowClear`, ghi thẳng qua `onUpdateTask` (`assigneeId`) hiện có ở dòng ~153; `data-testid="assignee-select"` |
| `frontend/src/features/board/components/KanbanColumn.tsx` | quick-add thêm `Select` người thực hiện (**optional**, cùng nguồn) ⇒ `createTask({ columnId, title, assigneeId })` |
| sau khi đổi assignee thành/khỏi agent | **refresh board** (agent row được tạo ⇒ dropdown có thêm lựa chọn mới) |

### B. Trạng thái "Chờ làm rõ" trên Kanban

| File | Việc |
|---|---|
| `frontend/src/features/board/components/KanbanColumn.tsx` | icon `QuestionCircleOutlined` (`#f59e0b`) + tooltip khi `column.isClarification` (đối xứng `isDone`) |
| `frontend/src/features/board/components/TaskCard.tsx` | badge agent theo `activeAgentRunId`; câu hỏi làm rõ nổi bật (icon + 2 dòng `line-clamp`) |
| `frontend/src/features/board/components/TaskDetailModal.tsx` | panel `AgentRunPanel` + câu hỏi làm rõ + nút **"Chạy lại"** (chỉ Manager/Admin) |

### C. Panel Agent + real-time + duyệt + tệp đính kèm

| File | Việc |
|---|---|
| `frontend/src/features/ai/services/agentApi.ts` **(mới)** | 7 route ở §2.1 |
| `frontend/src/features/ai/types/agentRun.types.ts` **(mới)** | các kiểu ở §2.3 |
| `frontend/src/features/ai/hooks/useAgentRuns.ts` **(mới)** | GET run (mount + reconnect), start/rerun/cancel, trạng thái loading/lỗi |
| `frontend/src/features/board/hooks/useBoardHub.ts` | thêm handler `connection.on('AgentRunProgress', …)` (file đã có sẵn map handler ở dòng ~58–100) |
| `frontend/src/features/ai/components/AgentRunPanel.tsx` **(mới)** | nút **"Chạy Agent"**/**"Chạy lại"**/**"Huỷ"**, `Tag` trạng thái (nhãn tiếng Việt), số tool-call/token, `Timeline` cho `toolCallTrace`, cảnh báo khi `traceTruncated`, `Alert` lỗi từ `stopReason`/`error` |
| `frontend/src/features/ai/components/AgentDraftApproval.tsx` **(mới)** | hiện `aiActionLogId` ⇒ **tái dùng** `AiActionLogItem` + `useAiActions` để Duyệt/Từ chối/Hoàn tác |
| `frontend/src/features/ai/components/AttachmentList.tsx` **(mới)** | tên file, kích thước (`formatBytes`), "do AI Agent tạo", nút tải (blob qua `utils/reportDownload.ts`) |
| `frontend/src/features/ai/components/NotificationItem.tsx` | nhãn tiếng Việt cho 3 `notification.type` mới |
| `frontend/src/features/ai/index.ts` | export các thành phần mới (theo pattern sẵn có) |

### D. Chất lượng

- `npm run lint` **0/0** · `npx tsc -b` **exit 0** · `npm run build` **OK** · `npm test` — số test **> 155**.
- Test mới (theo pattern `__tests__` sẵn có): dropdown đổi được assignee (kể cả agent); badge theo từng `status`;
  nút "Chạy lại" **chỉ** hiện khi `AwaitingClarification`; `AgentRunPanel` disable nút "Chạy Agent" khi `Running`;
  `AttachmentList` tải blob; mapping nhãn notification mới.
- **Không** viết lại `httpClient`/`reportDownload`; **không** thêm thư viện UI mới.

---

## 5. Cách tự kiểm trước khi báo xong (Definition of Done của §5)

1. `npm run lint` 0/0, `npx tsc -b` exit 0, `npm run build` OK, `npm test` **> 155** (ghi số cụ thể vào báo cáo).
2. Bật backend thật (`dotnet run --project src/TeamNexus.Api`, cần `ConnectionStrings` + `Jwt:SigningKey` trong User Secrets,
   `Observer__Enabled=false`) và kiểm tay 4 luồng: (a) đổi assignee sang **TeamNexus Agent**; (b) bấm **Chạy Agent** ⇒ thấy
   "Đang chạy" → "Chờ duyệt"; (c) duyệt ⇒ comment/tệp xuất hiện; (d) task `FAKE:CLARIFY` (đặt tiêu đề chứa chuỗi đó để
   `FakeAiProvider` hỏi lại) ⇒ task sang cột "Chờ làm rõ" + nút **"Chạy lại"** hoạt động sau khi trả lời bằng comment.
   > Mẹo: để test offline **không tốn token**, đặt env `DeepSeek__ApiKey` = **một khoảng trắng** `' '` (env rỗng bị .NET coi là
   > "unset" và User Secrets sẽ thắng trở lại ⇒ gọi DeepSeek thật).
3. Cập nhật: `Project-Documents/tasks/phase-7-ai-agent-executor.md` §5 (tick checklist + banner kết quả),
   `README.md` (dòng §5), `Project-Documents/report/phase-7-ai-agent-executor-test-report.md` (thêm §2.5 nhóm **J** với số
   test thật). Ghi rõ nhóm **J** trong §7 là **hạng mục cuối cùng** còn lại của Giai đoạn 7 ⇒ sau đó §8 chỉ còn đối chiếu.
4. **Không** sửa backend. Nếu phát hiện lệch hợp đồng, dừng lại và báo (kèm request/response thật) thay vì tự sửa DTO.

---

## 6. Bằng chứng backend đã có (để bạn tin hợp đồng, không cần chạy lại)

| Nhóm §7 | Nội dung | Kết quả |
|---|---|---|
| A | Schema (`agent_runs`/`task_attachments`/`member_type`/`is_clarification`, CHECK, partial UQ, FK RESTRICT, `jsonb`/`bytea`) | **36/36** (§2.1) + **11/11** (re-check §7) |
| B/C | Hàm thuần + hợp đồng DeepSeek/Tavily với stub `HttpMessageHandler` + registry + 3 nhánh fake | **89/89** |
| C-real | **Tavily thật** qua tool `WebSearch` (dữ liệu thật, key không vào log) | **5/5** |
| D | Loop end-to-end: 202 → `AwaitingApproval`; approve ⇒ comment/tệp thật; undo ⇒ revert; tool lỗi không làm chết run | **43/43** |
| E | "Chờ làm rõ" + "Chạy lại" (append-only: run cũ byte-identical) | **19/19** |
| F | Guardrail 4 ngưỡng (`ToolLimit`/`TokenBudget`/`TimeLimit`/`InternalError`) + **đúng 1** notification | **44/44** |
| G | Provider lỗi, reaper, 2 request đồng thời (202+409), cancel, task đổi giữa run | **21/21** |
| H | 7 route: 401/403/404/405/409, `take` clamp, `Agent:Enabled=false` ⇒ **503**, tải file đúng header | **22/22** |
| I | Bất biến + **không hồi quy Phase 4** (`CreateSubtasks`) + 3 đường trả task | **44/44** + **29/29** |
| Thật | 1 lượt **DeepSeek thật** (+ Tavily thật trong cùng boot) | **8/8** |

Tổng: **291 check PASS** cho §7; tất cả trên API Kestrel thật + PostgreSQL 18 thật. Harness nằm ngoài workspace và **đã xoá**
(quyết định D17), DB về baseline (`users=1`, `agent_runs=0`, `task_attachments=0`).
