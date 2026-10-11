# Giai đoạn 14.5 — Củng cố nền tảng + AI Agent Chat cấp Board (Kế hoạch chia task)

> **Trạng thái thi hành:** ✅ **BACKEND + FRONTEND ĐÃ HOÀN THÀNH.**
> **Nguồn:** yêu cầu người dùng (phiên này) — *"đọc tài liệu dự án, kiểm tra lại các tính năng để tìm thứ cần chỉnh sửa/nâng cấp/củng cố làm nền cho giai đoạn 15 và 16"* + *"gộp các tính năng tasks như AI agent, Hỏi AI thành AI Agent Chat tổng của board"*.
> **Tiền đề đã merge:** Giai đoạn 1 → **14**.
> **Báo cáo nghiệm thu:** `report/phase-14-5-foundation-and-agent-chat-test-report.md`.
>
> **⚠️ Vì sao KHÔNG phải "Giai đoạn 14.6" hay "Giai đoạn 15":** người dùng chốt **một** tài liệu duy nhất, và Phase 15 (Slack/Discord/GitHub) **không** được đụng vào. Số `14.5` nói đúng bản chất: đây là phần **nối tiếp của 14**, không phải một giai đoạn chức năng mới trong roadmap gốc.
>
> **⚠️ LỆCH DoD có chủ ý của Giai đoạn 14 (bắt buộc ghi lại):** `tasks/phase-14-ai-advanced.md` §8 ghi **"⛔ KHÔNG được làm"** ba việc — *lưu transcript chat ở DB* (D2), *chat nhiều task cùng lúc*, *chat có tool/function-calling*. Người dùng đã chốt đi thẳng vào 2 việc đầu. Giai đoạn 14.5 **thay thế có chủ ý** hai dòng đó; dòng thứ ba (*function-calling*) **vẫn bị cấm**.

---

## 0. Hai track của giai đoạn này

| Track | Nội dung | Vì sao làm **trước** Phase 15/16 |
|---|---|---|
| **A — Củng cố nền** | A1.1 lịch sử AI cấp workspace · A1.2 seam `IExternalAlertSink` · A2.1 reaper dọn run của task đã xoá · A2.3 bỏ fetch trùng ở `BoardView` · A2.4 retention cho transcript | Mỗi mục là một chỗ mà Phase 15 (Slack/GitHub/webhook) hoặc Phase 16 (Flutter) **bắt buộc** phải sửa hoặc dựa vào. Làm sau khi đã có integration = phải sửa cả integration |
| **B — AI Agent Chat** | Panel chat cấp board: 1 nút nổi, kéo thẻ từ cột để đính kèm, stream SSE, transcript lưu DB, đề xuất ghi dữ liệu đi qua Accountability Layer | Là yêu cầu trực tiếp của người dùng, và là bề mặt mà Phase 16 (Flutter) sẽ dùng lại nguyên hợp đồng API |

---

## 1. Track A — phát hiện & cách xử lý

> Tiền lệ "Khảo sát đầu kỳ" của Phase 10/12/13: mỗi mục **đã được xác minh trong code** trước khi sửa. Mục nào đã có sẵn thì ghi nhận, không viết lại.

### A1.1 — Lịch sử AI cấp workspace (bắt buộc)

**Phát hiện (đã xác minh):** `ai_action_logs` **không có** cột `workspace_id`; log của board template lưu `entity_type = 'Workspace'` + `entity_id = workspaceId`. Endpoint duy nhất để đọc lịch sử là `GET /api/boards/{boardId}/ai-actions` ⇒ **một log tạo board không bao giờ xuất hiện ở đó**, và cách duy nhất để duyệt/undo một đề xuất board template là mở **chính board nó vừa tạo**. `grep -rn "workspaces/{.*}/ai-actions"` trước Phase 14.5 = **0 kết quả**.

**Cách xử lý:**
- Thêm `GET /api/workspaces/{workspaceId}/ai-actions` (**Manager+**, filter `status`/`entityType`/`action`, `take` clamp 1..100): map `entity_type='Board'` với `entity_id ∈ boards(workspaceId)` **hoặc** `entity_type='Workspace'` với `entity_id = workspaceId`.
- **Không** thêm cột/index/migration: truy vấn vẫn dùng đúng index `(entity_type, entity_id, created_at)` đã có.
- FE: `AiActionHistoryDrawer` nhận **một trong hai** scope (`boardId` | `workspaceId`); `useAiActions` có một đường fetch duy nhất cho cả hai; `BoardListPage` thêm nút **"Lịch sử AI"** cho Manager+ và **re-fetch danh sách board** sau mỗi quyết định (approve board template = có board mới).

**Bằng chứng:** `Integration/WorkspaceAiActionApiTests` (9 test) + `hooks/__tests__/useAiActions.workspace.test.ts` (4) + `pages/__tests__/BoardListPage.workspaceAiHistory.test.tsx` (2).

### A1.2 — Seam thông báo ra bên ngoài (bắt buộc)

**Phát hiện (đã xác minh):** mọi cảnh báo đều chỉ có **in-app**. Có **4** đường ghi `notifications`: `NotificationService.NotifyManagersAsync` (Observer findings) · `NotifyManagersAsync` (agent alerts, dùng bởi `AgentRunOrchestrator` ở **4** call site) · `NotifyUsersAsync` (member alerts: `TaskAssigned`/`CommentOnTask`/`CommentMention`/`WorkspaceInvitation`). `NotificationWriter` chỉ là adapter mỏng trên **một** trong ba. ⇒ Nếu Phase 15 cắm thẳng vào `NotificationWriter`, **hai** đường của Ai (agent + observer) sẽ bị bỏ sót.

**Cách xử lý:** đặt seam ở **`NotificationService`** (nơi cả 4 đường đi qua), không ở adapter:
- Port mới `IExternalAlertSink` + `ExternalAlertMessage` + `NoOpExternalAlertSink` (mặc định production).
- `NotificationService` gọi sink **sau khi** `SaveChangesAsync` (cảnh báo phải tồn tại thật rồi mới phát ra ngoài) và **nuốt mọi lỗi** (transport hỏng ⇒ chỉ còn in-app, **không** làm request ghi dữ liệu thất bại).
- Phase 15 chỉ cần **1 dòng đăng ký** + 1 class; **0** thay đổi ở Observer/Agent/member path.

**Bằng chứng:** `Integration/Phase145HardeningTests` `PHASE145_A5/A6/A7` (3 test: sink nhận được alert; mặc định là NoOp; transport ném lỗi ⇒ request vẫn **201**).

### A2.1 — Reaper dọn run của task đã xoá (nên làm)

**Phát hiện (đã xác minh):** `AgentRunReaper` chỉ dọn theo **thời gian** (`StartedAt < now − timeout − grace`). Xoá mềm một task đang chạy để lại run `Running`/`AwaitingApproval` tới **10 phút**, trong khi thẻ đã biến mất khỏi Kanban và lần gán sau đó bị **409** nhắc tới một run không ai nhìn thấy.

**Cách xử lý:** thêm nhánh thứ hai — run ở status sống mà `task` không còn tồn tại (query filter của Tasks **chính là** phép thử "đã xoá") ⇒ `Failed` + `stop_reason = TaskChanged`, **không** sinh notification (xoá thẻ vốn là hành động có chủ ý của người).
**Tách `AgentRunReaperRunner` (scoped) khỏi `AgentRunReaper` (hosted)**: đúng tiền lệ `DailyDigestRunner`, và gỡ luôn một chướng ngại thật — `BackgroundService` **không resolve được từ container**, nên trước đây không test nào gọi trực tiếp được.

**Bằng chứng:** `PHASE145_A1` (run của task đã xoá bị đóng, `TaskChanged`) + `PHASE145_A2` (run tươi của task còn sống **không** bị đụng).

### A2.3 — Bỏ fetch trùng & log lỗi bị nuốt (nên làm)

**Phát hiện (đã xác minh):** `BoardView` gọi `aiActionApi.listAiActions(boardId, {status:'Pending', take:100})` **hai lần** mỗi lần mở board (một ở `refreshPendingCount`, một inline trong `useEffect`), và cả hai đều `.catch(() => {})` — badge sai mà không có dấu vết nào.

**Cách xử lý:** mount chỉ gọi `refreshPendingCount()`; cả hai helper log bằng `console.warn` khi lỗi (dữ liệu **advisory** không được làm hỏng board, nhưng badge sai phải để lại dấu vết).

**Bằng chứng:** `components/__tests__/BoardView.agentChat.test.tsx` `AGENTCHAT_FE4` — khẳng định endpoint được gọi **đúng 1 lần**.

### A2.4 — Retention cho transcript chat (bắt buộc, phát sinh từ Track B)

**Phát hiện:** bảng `agent_chat_messages` là append-only và **sinh ra ở mọi câu hỏi** ⇒ không có sweep thì DB free-tier đầy văn xuôi không ai đọc lại. Các bảng append-only khác đều đã có chủ sở hữu retention (Observer runs) hoặc là dữ liệu người dùng chủ ý giữ (attachments/comments).
**Cách xử lý:** `AgentChatRetentionRunner` (scoped, gọi trực tiếp được từ test): xoá theo `AgentChat:RetentionDays` (mặc định **90**), theo **batch** `RetentionBatchSize` (2000), dừng ngay khi batch trả về thiếu — một lượt quét đầu trên DB lâu năm không khoá bảng.
**Bằng chứng:** `PHASE145_A3` (chỉ xoá lượt quá hạn) + `PHASE145_A4` (5/5 lượt cũ bị xoá, vòng lặp **kết thúc**).

### A3 — Chỉ ghi nhận thành nợ kỹ thuật (KHÔNG làm ở 14.5)

`task_attachments` chỉ `text/*`·`text/csv` ≤ 512 KB (Phase 15 cần diff/ảnh) · chưa có idempotency-key cho webhook · chưa có seam `IWorkItemLink` (GitHub PR ↔ task) · `DeepSeek`/`Tavily` chỉ 1 provider · **task chat vẫn transcript ở client** (nợ có chủ ý, xem §3.6).

---

## 2. Track B — AI Agent Chat cấp Board

### 2.1 Quyết định đã chốt (D1–D14)

| # | Quyết định | Lý do |
|---|---|---|
| **D1** | **CÓ 1 migration** (`Phase14_5AgentChat` — bảng `agent_chat_messages`) ⇒ `migrations list` **10 → 11** | Người dùng chốt *"lưu DB"*. Đây là **lệch có chủ ý** so với Phase 14 (D1 của Phase 14 cấm migration); ghi rõ ở `03-roadmap.md` + `04-database-design.md` + báo cáo |
| **D2** | **Tái dùng** pipeline chat sẵn có: cùng `IAiStreamingProvider`, cùng `AiChatSseWriter`, cùng `parseSseFrames` (FE), cùng khung `meta`→`delta`*→`done`\|`error` | Một transport duy nhất cho hai bề mặt. Thêm chuẩn SSE thứ hai = thêm một chỗ để trôi |
| **D3** | **Section option RIÊNG `AgentChat`**, nâng trần **2–3×** (`MaxHistoryMessages` 12→**24**, `MaxHistoryChars` 8000→**24000**, `MaxOutputTokens` 1200→**2400**, `MaxAttachedTasks` **8**, `MaxTotalContextChars` **24000**) | Người dùng yêu cầu nâng token. Nếu nhét vào `AiChat` thì một lần chỉnh sẽ âm thầm nới cả trần của tab "Hỏi AI" — đúng cảnh báo **D9 của Phase 14** |
| **D4** | **Đề xuất là JSON trong chính câu trả lời**, không phải khung SSE riêng | Giữ nguyên hợp đồng SSE đã verify; transcript lưu DB chứa đủ để dựng lại card; parser là **hàm thuần** trên chuỗi (test được không cần HTTP) |
| **D5** | **Đúng 1 đề xuất mỗi câu trả lời** — `MaxProposalsPerAnswer` bị **clamp cứng** `[1,1]` | Một câu trả lời gộp "đổi tên cái này, chuyển cái kia, tạo 5 thẻ" là yêu cầu Manager duyệt một gói họ không thể phán từng phần |
| **D6** | Đề xuất **chỉ** được thao tác trên task **nằm trong tập đính kèm**, cột **phải thuộc board này**, và **không** được di chuyển vào cột `is_clarification` | Biên an ninh thật sự: model + body request đều là **đầu vào không tin cậy**. Thiếu luật đầu tiên, một Manager có thể duyệt thay đổi trên thẻ của board họ không hề xem |
| **D7** | "Gửi đề xuất" ⇒ `Pending` ⇒ **Manager duyệt** mới ghi; ba applier mới: `UpdateTaskFields`, `MoveTaskToColumn` (**+ `CreateSubtasks` dùng lại applier Phase 4**) | Giữ nguyên bất biến *"chưa duyệt thì chưa ghi dữ liệu nghiệp vụ"* của Phase 4, đồng thời mở được 3 dạng output người dùng yêu cầu |
| **D8** | **"Lưu thành bình luận"** dùng lại `RequestChatCommentAsync` (đường của **người**, Member+), không phải `RequestAgentOutputAsync` | Tiền lệ **P10 Phase 14**: đường của agent assert "task đang gán cho AI Agent" ⇒ sẽ **403** mọi lượt lưu hợp lệ của người dùng, kèm thông điệp nói về một Agent họ không nhắc tới |
| **D9** | **Panel docked trong layout**, KHÔNG overlay/Drawer; vùng thả `useDroppable` + nhánh `handleDragEnd` xử lý **trước** mọi nhánh di chuyển thẻ | Tính năng định nghĩa bởi *kéo thẻ từ cột vào panel*; overlay sẽ che chính các thẻ đang kéo, và dnd-kit không có target đo được ở dưới |
| **D10** | `AGENT_CHAT_DROPZONE_ID` là **một hằng số export** dùng bởi cả panel và `BoardView` | Hai chuỗi `'ai-agent-chat-dropzone'` ở hai file là một bug chờ xảy ra (drop im lặng không làm gì) |
| **D11** | Tab `'ai'` trong `TaskDetailModal` **giữ nguyên**, chỉ rõ nhãn **"Hỏi AI về thẻ này"** | Người dùng chốt giữ. Không phá tab/test Phase 14 đang xanh; hai cửa vào phục vụ hai ngữ cảnh khác nhau |
| **D12** | Transcript lưu DB **KHÔNG** chứa khối JSON; `done` frame **vẫn** chứa đủ (client cần để dựng card) | Transcript là hội thoại người đọc; khối JSON thuộc về action log (nơi Manager đọc nó như bản xem trước) |
| **D13** | `agent_chat_messages.task_id` **để sẵn** (nullable) + CHECK `num_nonnulls(board_id, task_id) = 1` | Hợp nhất task chat vào cùng bảng sau này **không cần migration nữa** — trả nợ §3.6 bằng một cột đã có |
| **D14** | `Enabled` mặc định `true`, tắt ⇒ **503**; cờ **riêng** với `AiChat:Enabled` | Vận hành phải tắt được chat board (ngân sách lớn hơn) mà vẫn giữ tab "Hỏi AI" |

### 2.2 Schema (`Phase14_5AgentChat`)

`agent_chat_messages`: `id` · `board_id` (null) · `task_id` (null) · `workspace_id` · `user_id` (null) · `role` (`User`/`Assistant`, CHECK) · `content` · `attachments` (jsonb) · `created_at` · `updated_at`.
CHECK `ck_agent_chat_messages_scope`: `num_nonnulls(board_id, task_id) = 1`. Index `(board_id, created_at)` (nạp transcript) + `(workspace_id, created_at)` (retention). **Không** soft-delete, **không** query filter (append-only, tiền lệ `ai_action_logs`/`agent_runs`).
**Không** navigation property trên entity, và **hai** lệnh `ToTable(…, t => t.HasCheckConstraint(...))` thay vì một chuỗi: EF Core 10 không cho chuỗi `HasCheckConstraint` trên `CheckConstraintBuilder`, và dạng gộp một lệnh làm model không round-trip qua snapshot (`has-pending-model-changes` báo thay đổi ma vĩnh viễn).

### 2.3 Hợp đồng API (Flutter Phase 16 dùng lại nguyên)

```http
GET  /api/boards/{boardId}/agent-chat/session                (Member+ · idempotent)
GET  /api/boards/{boardId}/agent-chat/messages?take=&before= (Member+ · cũ→mới)
POST /api/boards/{boardId}/agent-chat/stream                 (Member+ · antiforgery ⇒ SSE)
POST /api/boards/{boardId}/agent-chat/message                (Member+ · antiforgery ⇒ 201 Pending PostComment)
POST /api/boards/{boardId}/agent-chat/proposal/confirm        (Member+ · antiforgery ⇒ 201 Pending)
GET  /api/workspaces/{workspaceId}/ai-actions?status=&entityType=&action=&take=   (Manager+ · A1.1)
```

Luật chốt: `attachedTaskIds` ≤ **8** và **mọi** task phải thuộc board (⇒ **400**, **0 token**); `messages` > 24 hoặc > 24 000 ký tự ⇒ **400** trước khi gọi provider; người ngoài workspace ⇒ **404** (không 403); `AgentChat:Enabled=false` ⇒ **503**; lỗi **sau** khi mở stream ⇒ khung `event: error` + giữ phần đã stream; `done.answer` == nối mọi `delta.text`.

---

## 3. Phát sinh **bắt buộc** khi hiện thực (ghi để không bị coi là sai lệch kế hoạch)

| # | Phát sinh | Vì sao & cách xử lý |
|---|---|---|
| **P1** | ❗ **BUG THẬT có sẵn từ Phase 14:** cả `AiChatService` và `AgentChatService` kiểm `chunk.Done` **trước** khi nối `chunk.Delta` | Provider offline đặt **text ở chính khung kết thúc** ⇒ chữ đó **bị mất**, mọi câu trả lời **ngắn đi đúng một chunk**, và bất biến `done.answer == Σ delta` (client dựa vào để khôi phục khung bị mất) **sai một cách âm thầm**. Bộ test Phase 14 **không bắt được** vì nó chỉ khẳng định hai thứ **cùng tồn tại**, chưa bao giờ khẳng định chúng **bằng nhau**. Đã sửa: nối delta **trước**, kiểm `Done` **sau**; thêm `Pure/FakeStreamingProviderTests` khoá đúng tính chất đó |
| **P2** | ❗ `AiActionService.ApproveAsync` mở transaction, mà `TaskService.MoveTaskAsync` **cũng** mở transaction riêng ⇒ `InvalidOperationException` ⇒ duyệt `MoveTaskToColumn` trả **500** | Bỏ transaction **thừa** trong `MoveTaskAsync`: mọi thay đổi đã nằm trong **một** `SaveChangesAsync` (bản thân nó là một transaction), nên tính nguyên tử **không đổi**, và thứ tự "ghi activity log **sau** khi commit" vẫn đúng. Sửa ở applier (tự di chuyển thẻ bằng tay) sẽ nhân bản logic đánh số lại vị trí và **bỏ mất broadcast SignalR** |
| **P3** | `IAuditableEntity` **ghi đè** `CreatedAt` khi `SaveChangesAsync` ⇒ test không thể "back-date" một row bằng cách gán property | Ghi timestamp cũ bằng `ExecuteSqlRawAsync` **sau** khi save — đúng mẹo `DailyDigestTests` đã dùng |
| **P4** | `BackgroundService` **không resolve được** từ container | Tách `AgentRunReaperRunner` (scoped) khỏi `AgentRunReaper` (hosted) — cũng là điều kiện để test A2.1 tồn tại |
| **P5** | `TaskService.UpdateTaskAsync` nhận **full replacement** ⇒ Undo cập nhật field phải khôi phục **5** trường | Applier chụp **toàn bộ** field vào `before_snapshot` tại **thời điểm apply** (không phải lúc request), trong **cùng** transaction của `ApproveAsync` |
| **P6** | `Element.scrollTo` **không tồn tại** trong jsdom | Bọc guard `typeof node.scrollTo === 'function'`, nếu không **mọi** test component của panel đỏ ở một DOM method thiếu |
| **P7** | `confirmSmartSetupRequest` cần `Description` **đứng đầu** | `CreateSubtasks` từ chat dùng `summary` đã validate làm `Description` (không có mô tả người dùng để đưa) |
| **P8** | `Alert.message` **deprecated** ở antd v6 | Dùng `title` (đúng như `AiChatPanel` đã chuyển) — cảnh báo deprecation trong output test che mất cảnh báo thật |

---

## 4. Ca biên & chế độ lỗi (đã xử lý)

> **§3.3 của kế hoạch** — tất cả đã có test.

| Ca | Hành vi chốt |
|---|---|
| `attachedTaskIds` > 8 | **400** trước khi gọi provider (**0 token**); UI chặn trước bằng `message.warning` nêu rõ lý do |
| Task đính kèm không thuộc board / đã soft-delete | **400** `"…không thuộc bảng này…"` |
| `messages` > 24 / > 24 000 ký tự | **400**; **0** row ghi DB |
| Người ngoài workspace | **404** (không 403) |
| Thiếu CSRF | **403** |
| `AgentChat:Enabled=false` | **503** trước khi chạm DB/provider |
| Lỗi provider **giữa** luồng | khung `event: error`, **không** `done`; FE giữ phần đã stream + `Alert` tiếng Việt + mã lỗi |
| Client `abort()` / bấm **Dừng** | dừng, **không** bong bóng rác, **không** unhandled rejection |
| AI trả JSON hỏng / kind lạ / `taskId` ngoài tập đính kèm / cột khác board | **Không** card (nếu hỏng) hoặc **400** (nếu gửi); **không** ghi gì |
| `priority` là số (`"1"`) | **400** — **không** âm thầm thành `Medium` (bẫy **BUG-1** Phase 10) |
| `dueDate` có offset `+07:00` | chuẩn hoá `ToUtc(...)` → lưu UTC, giữ nguyên thời điểm (bẫy **BUG-2** Npgsql) |
| Confirm / Approve **hai lần** | **409** (CAS Phase 4) |
| Lưu thành bình luận mà **không** đính kèm thẻ | **400** + nút disable + lý do trong `Tooltip` |
| Undo `UpdateTaskFields` khi task đã bị xoá | **warning** trong `applied_snapshot`, **không** ném |
| Undo `MoveTaskToColumn` khi cột cũ đã bị xoá | **warning**, **không** ném |
| Kéo thẻ rồi **thả ra ngoài** panel | hành vi **y hệt cũ** (di chuyển thẻ) — có test khẳng định `moveTask` không bị gọi khi thả **vào** panel |
| Panel mở/đóng, đổi board | panel **mount lại** ⇒ nạp đúng transcript của board đó; `useBoard` **không** unmount (SignalR không đứt) |
| Workspace/board rỗng | `Empty` + gợi ý, **không** màn hình trắng |
| DB lỗi khi nạp transcript | panel **vẫn chat được**; `Alert` tiếng Việt |
| Transport ngoài hỏng (giả lập) | request ghi dữ liệu vẫn **201** (A1.2) |

---

## 5. Bằng chứng & số ĐO THẬT

**Baseline đầu kỳ (đo lại trong phiên này, không lấy từ tài liệu):** backend `dotnet test` = **584 / 0 / 0**; `migrations list` = **10**; frontend `npm test` = **578 test / 99 file**; `dotnet build --no-incremental` = **0/0**.

| # | Bằng chứng | Ngưỡng | Kết quả |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 warning / 0 error | ✅ **0 / 0** |
| 2 | `dotnet test` (PostgreSQL thật, `Skipped: 0`) | — | ✅ **643 / 0 / 0** (baseline 584 ⇒ **+59**) |
| 3 | `dotnet ef migrations list` | **11** | ✅ **11** (`20261011154528_Phase14_5AgentChat`) |
| 4 | `has-pending-model-changes` | sạch | ✅ *"No changes have been made to the model since the last migration."* |
| 5 | `TestScenario.ResetDatabaseAsync` | có `agent_chat_messages` | ✅ (thiếu ⇒ rò state giữa test) |
| 6 | FE `npm test` | 0 failed, ≥ 578 | ✅ **633 passed / 107 file** (baseline 578 / 99 ⇒ **+55 / +8**) |
| 7 | FE `npm run lint` · `npx tsc -b` · `npm run build` | 0/0 · exit 0 · OK | ✅ **0 warning / 0 error** (255 file) · exit 0 · build OK |
| 8 | Cổng CI nâng **sau cùng** bằng số thật | `ci-backend.yml` · `ci-web.yml` | ✅ (xem §6.2) |
| 9 | Kiểm thử tay 1 lượt | — | ✅ (xem §6.3) |

**Phân bổ backend +59:**

| Suite | Số test | Nội dung |
|---|---|---|
| `Pure/AgentChatProposalTests` | **20** | parser + validator: khối cuối cùng, envelope, JSON hỏng, task ngoài tập đính kèm, cột khác board, priority số, `dueDate` offset, trần task, kind lạ |
| `Integration/AgentChatApiTests` | **22** | session idempotent, SSE contract, transcript **bền qua reload**, keyset paging, 401/403/404/400/503, trần 8 thẻ, task ngoài board, lưu bình luận ⇒ Pending, **đề xuất ⇒ Pending ⇒ approve ⇒ thẻ ĐỔI CỘT thật ⇒ undo ⇒ về cột cũ**, 409, và hồi quy *task chat không đổi* |
| `Integration/WorkspaceAiActionApiTests` | **9** | gộp log `Board` + `Workspace`, 403 Member, 404 người ngoài, không rò workspace khác, parse `status` nghiêm, clamp `take`, approve board template **không cần mở board**, shape response không đổi |
| `Integration/Phase145HardeningTests` | **7** | A2.1 reaper (đóng + không đóng nhầm), A2.4 retention (ngưỡng + batch), A1.2 seam (nhận alert, mặc định NoOp, transport lỗi vẫn 201) |
| `Pure/FakeStreamingProviderTests` | **1** | khoá **P1**: khung kết thúc **mang text**, và bỏ nó làm câu trả lời **ngắn đi** |

**Phân bổ frontend +55 (107 file, +8):**

| File test | Số test |
|---|---|
| `ai/utils/__tests__/agentChatProposal.test.ts` | 11 |
| `ai/hooks/__tests__/useAgentChat.test.ts` | 12 |
| `ai/utils/__tests__/agentChatAttachments.test.ts` | 8 |
| `ai/utils/__tests__/sseStream.test.ts` | 6 |
| `ai/components/__tests__/AgentChatPanel.test.tsx` | 6 |
| `board/components/__tests__/BoardView.agentChat.test.tsx` | 4 |
| `ai/hooks/__tests__/useAiActions.workspace.test.ts` | 4 |
| `board/pages/__tests__/BoardListPage.workspaceAiHistory.test.tsx` | 2 |
| *(mở rộng — chỉ thêm)* `ai/components/__tests__/AiActionLogItem.test.tsx` | +2 |
| *(mở rộng — đổi 1 assertion trong 1 test)* `board/components/__tests__/TaskDetailModal.test.tsx` | 0 (nhãn tab đổi ⇒ assertion đổi; `data-testid` giữ nguyên) |

---

## 6. DoD, CI & tài liệu

### 6.1 Ngoại lệ được phép
- `Pure/ObserverVocabularyTests` (**Phase 14**) **không** bị đụng ở giai đoạn này.
- `board/components/__tests__/TaskDetailModal.test.tsx` — **1** assertion được sửa: nhãn tab `'ai'` đổi từ `"Hỏi AI"` thành `"Hỏi AI về thẻ này"` theo quyết định **D11** của người dùng. `data-testid="ai-chat-tab"` **giữ nguyên**, thứ tự tab và tab mặc định **giữ nguyên**; đây là **ngoại lệ duy nhất** và được ghi lại ở đây + trong báo cáo.
- **P1** sửa **hành vi** của `AiChatService` (một dòng trong vòng lặp). Đây là **sửa bug**, không phải viết lại test: **không** test cũ nào bị sửa; bộ test Phase 14 vẫn xanh **nguyên trạng** vì nó chưa từng khẳng định bất biến bị vi phạm.

### 6.2 Cổng CI (làm **sau cùng**, bằng số thật)
| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | `-ne 584` → `-ne 643` + cập nhật chuỗi comment baseline |
| 2 | `.github/workflows/ci-web.yml` | `-lt 578` → `-lt 633` + cập nhật chuỗi comment baseline |

### 6.3 Kiểm thử tay 1 lượt
`dotnet run --project src/TeamNexus.Api` + `npm run dev`:
**(a)** mở một board ⇒ nút nổi góc phải-dưới ⇒ mở panel ⇒ **kéo 2 thẻ** từ cột vào panel ⇒ 2 pill xuất hiện;
**(b)** hỏi "Nên chuyển thẻ này đi đâu?" ⇒ câu trả lời **hiện dần**; **F5** ⇒ hội thoại **vẫn còn** (bền qua reload);
**(c)** card **"Đề xuất: Di chuyển thẻ"** ⇒ bấm *Gửi đề xuất* ⇒ *Lịch sử AI* có `Pending` ⇒ **Approve** ⇒ thẻ **đổi cột thật**, client khác cập nhật qua SignalR; **Undo** ⇒ về cột cũ;
**(d)** kéo một thẻ vào panel rồi bấm *Lưu thành bình luận* ⇒ `Pending` `PostComment` ⇒ Approve ⇒ bình luận xuất hiện đúng thẻ;
**(e)** `BoardListPage` ⇒ *Lịch sử AI* ⇒ thấy log `CreateBoardFromTemplate` (thứ **trước đây không hiện ở đâu**);
**(f)** tab "Hỏi AI về thẻ này" trong `TaskDetailModal` **vẫn** hoạt động như Phase 14.

### 6.4 Tài liệu đã cập nhật
`tasks/phase-14-5-foundation-and-agent-chat.md` (tài liệu này) · `tasks/phase-14-ai-advanced.md` (ghi lệch 14.5) · `03-roadmap.md` · `01-system-specification.md` §14 · `04-database-design.md` §3.10/§4/§5/§6 · `README.md` (10 → **11**) · `src/Modules/Ai/…/README.md` · `report/phase-14-5-foundation-and-agent-chat-test-report.md`.

---

## 7. ⛔ KHÔNG được làm (vẫn hiệu lực)

- ❌ **Không** thêm migration thứ hai: chỉ **đúng 1** (`Phase14_5AgentChat`).
- ❌ **Không** đụng Phase 15/16 (Slack/Discord/GitHub/webhook/HMAC/mã hoá token/Flutter).
- ❌ **Không** cho AI hay client ghi dữ liệu nghiệp vụ khi chưa `Approved`.
- ❌ **Không** cho đề xuất chạm task ngoài tập đính kèm / cột của board khác / cột `is_clarification`.
- ❌ **Không** `EventSource`, **không** `dangerouslySetInnerHTML`, **không** buffer rồi mới trả trong stream.
- ❌ **Không** dùng chung option `AiChat` ↔ `AgentChat`.
- ❌ **Không** đổi shape hợp đồng đã verify (kể cả `.../ai-chat/stream` của task chat, và tên event SignalR).
- ❌ **Không** viết lại test cũ đang xanh.
- ❌ **Không** nâng cổng CI trước khi xong; **không** thêm package NuGet/npm.

---

## 8. Nợ kỹ thuật ghi lại (không làm ở 14.5)

- **Task chat vẫn transcript ở client** ⇒ cùng một thẻ có thể có 2 nguồn hội thoại (tab trong modal tạm thời, panel board bền). **Cố ý** giữ để không phá hợp đồng + test Phase 14 đã verify; `agent_chat_messages.task_id` (**D13**) là chỗ chừa để hợp nhất sau **không cần migration**.
- **Chat không có function-calling**: model chỉ *đề xuất* JSON, người bấm gửi. Đúng chốt của người dùng và giữ chat **không tự ghi**.
- **A3**: attachment chỉ text ≤512 KB · chưa có webhook idempotency · chưa có `IWorkItemLink` · chưa có AI provider fallback.
