# Báo cáo nghiệm thu — Giai đoạn 14.5: Củng cố nền tảng + AI Agent Chat cấp Board

> **Phạm vi:** **backend + frontend** (cả hai đã hiện thực & verify trong phiên này).
> **Kế hoạch đã chốt:** `tasks/phase-14-5-foundation-and-agent-chat.md`.
> **Ngày đo:** phiên thi hành Giai đoạn 14.5. **Máy đo:** dev local, PostgreSQL thật ở `localhost:5433`
> (container `teamnexus-test-pg`, `postgres:18`).
> **Đối chiếu tài liệu cũ:** `tasks/phase-14-ai-advanced.md` (§8 — ghi rõ hai điều khoản đã được **thay thế có chủ ý**) và `03-roadmap.md` (mục Giai đoạn 14.5).

---

## 1. Kết quả ĐO THẬT

| # | Chỉ số | Baseline (đo lại trong phiên này) | Kết quả | Kết luận |
|---|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 / 0 | **0 Warning / 0 Error** | ✅ |
| 2 | `dotnet test` (PostgreSQL thật, `Skipped: 0`) | **584** | **Failed 0 / Passed 643 / Skipped 0 / Total 643** | ✅ **+59** |
| 3 | `dotnet ef migrations list` | **10** | **11** (`20261011154528_Phase14_5AgentChat`) | ✅ **+1 (cố ý)** |
| 4 | `has-pending-model-changes` | sạch | *"No changes have been made to the model since the last migration."* | ✅ |
| 5 | FE `npm test` | **578 test / 99 file** | **Failed 0 / Passed 633 / 107 file** | ✅ **+55 / +8** |
| 6 | FE `npm run lint` (oxlint) | 0 / 0 | **0 warning / 0 error** (255 file, 116 rule) | ✅ |
| 7 | FE `npx tsc -b` | exit 0 | **exit 0** | ✅ |
| 8 | FE `npm run build` | OK | **OK** (`✓ built in 30.56s`) | ✅ |
| 9 | Cổng CI | `ci-backend` = 584 · `ci-web` `-lt 578` | **`ci-backend` = 643** · **`ci-web` `-lt 633`** | ✅ **nâng bằng số thật** |

**Baseline đầu kỳ được ĐO LẠI, không lấy từ tài liệu:** `dotnet test` = 584/0/0 · `migrations list` = 10 ·
`npm test` = 578/99 file · `dotnet build --no-incremental` = 0/0. (Khớp đúng những gì `README.md`,
`ci-*.yml` và báo cáo Phase 14 ghi ⇒ tài liệu trước đó **không** bị trôi.)

---

## 2. Phân bổ test mới

### Backend +59

| Suite | Số test | Nội dung |
|---|---|---|
| `Integration/AgentChatApiTests` **(mới)** | **22** | session idempotent · hợp đồng SSE (thứ tự khung, `done.answer == Σ delta`) · **transcript bền qua reload** · phân trang keyset `before` · 401/403/404/400/503 · trần **8** thẻ đính kèm (và **0 row** ghi DB khi vượt) · task của **board khác** ⇒ 400 · trần 24 tin ⇒ 400 · lưu câu trả lời ⇒ `Pending` `PostComment` đúng thẻ · không gắn thẻ ⇒ 400 · **đề xuất `MoveTaskToColumn` ⇒ Pending ⇒ approve ⇒ thẻ ĐỔI CỘT thật ⇒ undo ⇒ về cột cũ** · đề xuất chạm task ngoài tập đính kèm ⇒ 400 · cột board khác ⇒ 400 · `UpdateTaskFields` (`+07:00` ⇒ lưu UTC) + undo khôi phục · `CreateSubtasks` dùng lại applier Phase 4 · JSON hỏng ⇒ 400 · approve 2 lần ⇒ 409 · người ngoài ⇒ 404 · board lạ ⇒ 404 · **hồi quy: `.../ai-chat/stream` của task chat KHÔNG đổi và KHÔNG ghi transcript board** |
| `Pure/AgentChatProposalTests` **(mới)** | **20** | parser (khối **cuối cùng**, envelope, fence chưa đóng, `data` rỗng) + validator (task ngoài tập đính kèm, cột khác board, `kind` lạ, priority **số** `"1"` ⇒ 400, `dueDate` offset ⇒ UTC, `taskId` thiếu, payload/kind thiếu, trần task, dedupe) |
| `Integration/WorkspaceAiActionApiTests` **(mới)** | **9** | gộp log `Board` **và** `Workspace` trong một danh sách · Manager+ (Member ⇒ 403) · người ngoài ⇒ 404 · **không** rò workspace khác · parse `status` nghiêm (số ⇒ 400) · filter lạ ⇒ trang rỗng · clamp `take` · **approve board template mà không cần mở board** + thấy trong lịch sử · shape response **không đổi** |
| `Integration/Phase145HardeningTests` **(mới)** | **7** | **A2.1** run của task đã xoá ⇒ `Failed` + `TaskChanged` (run **tươi**, nên không phải nhánh theo thời gian) · run tươi của task còn sống **không** bị đụng · **A2.4** retention chỉ xoá lượt quá hạn · 5/5 lượt cũ bị xoá và vòng lặp kết thúc · **A1.2** sink nhận `TaskAssigned` · mặc định là `NoOpExternalAlertSink` · transport ném lỗi ⇒ request vẫn **201** |
| `Pure/FakeStreamingProviderTests` **(mới)** | **1** | **khoá P1**: khung `Done` **mang text**, và bỏ nó làm câu trả lời **ngắn đi** (bất biến, không phải ví dụ) |

### Frontend +55

| File test | Số test | Nội dung |
|---|---|---|
| `ai/hooks/__tests__/useAgentChat.test.ts` **(mới)** | **12** | nạp transcript khi mở · lỗi nạp ⇒ panel **vẫn dùng được** · stream ⇒ bong bóng + `done.answer` là sự thật cuối · tách đề xuất khỏi văn bản · **gửi kèm `attachedTaskIds`** và **không** gửi bản sao tiêu đề · attach/detach · lỗi giữa luồng ⇒ giữ phần đã stream + `httpStatus` · lưu bình luận vào thẻ đầu tiên · không có thẻ ⇒ từ chối kèm lý do tiếng Việt · gửi đề xuất ⇒ xoá card · gửi lỗi ⇒ **giữ** card · cancel ⇒ không bong bóng rác |
| `ai/utils/__tests__/agentChatProposal.test.ts` **(mới)** | **11** | đối xứng với parser C#: khối cuối cùng, envelope, JSON hỏng, kind lạ, payload thiếu, `null`/rỗng |
| `ai/utils/__tests__/agentChatAttachments.test.ts` **(mới)** | **8** | toggle giữ nguyên **tham chiếu** khi không đổi (không re-render) · trần 8 · `toRequestIds` chỉ gửi id · `attachRefusalReason` nêu lý do |
| `ai/components/__tests__/AgentChatPanel.test.tsx` **(mới)** | **6** | empty state có hướng dẫn · nút lưu disable khi chưa gắn thẻ · pill + thả trùng · nút Dừng · action card (tên hành động + **tên cột** + **không** render khối JSON) · lỗi tiếng Việt + mã lỗi + giữ phần đã stream |
| `ai/utils/__tests__/sseStream.test.ts` **(mới)** | **6** | CSRF + `credentials` · 503 ⇒ ném `status` và **không** đọc stream · khung bị **cắt qua 2 chunk** · `event: error` giữa luồng · abort êm · không có body |
| `board/components/__tests__/BoardView.agentChat.test.tsx` **(mới)** | **4** | FAB tồn tại và board vẫn dùng được · mở/đóng panel ⇒ **`moveTask` KHÔNG bị gọi** · đóng bằng nút riêng · **badge chỉ fetch 1 lần** (A2.3) |
| `ai/hooks/__tests__/useAiActions.workspace.test.ts` **(mới)** | **4** | dùng endpoint **workspace** (không phải board) · truyền `status` · không scope ⇒ không gọi API · 403 ⇒ thông điệp quyền tiếng Việt |
| `board/pages/__tests__/BoardListPage.workspaceAiHistory.test.tsx` **(mới)** | **2** | Manager thấy nút và drawer nhận **workspaceId** · Member không thấy |
| `ai/components/__tests__/AiActionLogItem.test.tsx` *(mở rộng)* | **+2** | nhãn `UpdateTaskFields` / `MoveTaskToColumn` |
| `board/components/__tests__/TaskDetailModal.test.tsx` *(ngoại lệ: 1 assertion)* | 0 | nhãn tab đổi thành "Hỏi AI về thẻ này"; `data-testid="ai-chat-tab"` giữ nguyên |

---

## 3. Đã hiện thực gì

### Track A — củng cố nền

- **A1.1 `GET /api/workspaces/{id}/ai-actions`** (Manager+, filter `status`/`entityType`/`action`, `take` clamp 1..100).
  Vì `ai_action_logs` **không có** `workspace_id`, log tạo board (`entity_type='Workspace'`) **không bao giờ**
  xuất hiện ở danh sách theo board ⇒ trước đây chỉ duyệt/undo được board template **từ bên trong chính board nó
  vừa tạo**. **Không** cột/index/migration mới: truy vấn dùng đúng index `(entity_type, entity_id, created_at)`.
  FE: `AiActionHistoryDrawer` nhận **một trong hai** scope; `useAiActions` có **một** đường fetch cho cả hai
  (`fetchLogs`); `BoardListPage` thêm nút *Lịch sử AI* (Manager+) + **re-fetch danh sách board** sau mỗi quyết định.
- **A1.2 `IExternalAlertSink`** + `ExternalAlertMessage` + `NoOpExternalAlertSink`, gọi từ **`NotificationService`**
  — tức nơi **cả 4** đường ghi notification đi qua (`NotifyManagersAsync` ×2 của Observer + agent, và
  `NotifyUsersAsync` của member). **Cố ý không** đặt ở `NotificationWriter` (adapter chỉ phủ **1** trong 4 đường).
  Gọi **sau** `SaveChangesAsync`, **nuốt mọi lỗi** ⇒ transport hỏng chỉ còn in-app. Phase 15 cần **1 dòng đăng ký**.
- **A2.1** `AgentRunReaperRunner` (scoped, gọi trực tiếp được) thêm nhánh đóng run của **task đã xoá mềm**
  (`Failed` + `TaskChanged`, **không** notification). Tách khỏi `AgentRunReaper` (hosted) — một `BackgroundService`
  **không resolve được từ container**, nên trước đây không test nào gọi trực tiếp được.
- **A2.3** `BoardView` mount gọi **1** lần `refreshPendingCount()` (trước là **2**); cả helper pending và
  notification **log** lỗi thay vì `.catch(() => {})` nuốt im lặng.
- **A2.4** `AgentChatRetentionRunner`: `AgentChat:RetentionDays` (90) + `RetentionBatchSize` (2000), xoá theo batch,
  dừng khi batch thiếu.

### Track B — AI Agent Chat cấp Board

- **Schema (migration 11 `Phase14_5AgentChat`):** `agent_chat_messages` + CHECK `role` + CHECK
  `num_nonnulls(board_id, task_id) = 1` + index `(board_id, created_at)` / `(workspace_id, created_at)`.
- **Option `AgentChat` riêng** (2–3× `AiChat`), mọi giá trị **clamp**; `Enabled=false` ⇒ **503**.
- **API:** session · messages (keyset) · stream (SSE) · message (Pending comment) · proposal/confirm (Pending).
- **Prompt + parser + validator (hàm thuần):** khối ```json cuối cùng ⇒ envelope ⇒ validate `kind`/`taskId` ∈ tập
  đính kèm/`columnId` ∈ board/priority/`dueDate` ⇒ **đúng 1** đề xuất.
- **3 applier:** `CreateSubtasks` (dùng lại Phase 4) · `UpdateTaskFields` (chụp **5** trường) · `MoveTaskToColumn`
  (`fromColumnId`). Tất cả: Member+ khi **gửi**, Manager+ khi **duyệt**.
- **FE:** types · `sseStream` (transport dùng chung) · `agentChatProposal`/`agentChatAttachments` (hàm thuần) ·
  `agentChatApi` · `useAgentChat` · `AgentChatPanel` (**dropzone**) · `AgentChatFab` · nối vào `BoardView`
  (panel docked + divider kéo được + nhánh `handleDragEnd` xử lý **trước**) · nhãn tab `TaskDetailModal`.
- **Validator transcript dùng chung** (`AiChatTranscriptValidator`): tách khỏi `AiChatService` để chat task và chat
  board dùng **cùng luật** với trần khác nhau — không có bản sao thứ hai để trôi.

---

## 4. Bug thật đã bắt & sửa (quan trọng nhất của báo cáo này)

### P1 — Hai service chat **bỏ chữ ở khung cuối** (bug có sẵn từ Phase 14)

Cả `AiChatService` và `AgentChatService` kiểm `chunk.Done` **trước** khi nối `chunk.Delta`. `FakeAiProvider` đặt
**text ở chính khung kết thúc** (`Done: isLast` đi cùng chunk cuối), nên:

- chữ của khung cuối **bị mất** ⇒ **mọi câu trả lời ngắn đi đúng một chunk** (chunk = 24 ký tự);
- bất biến tài liệu hoá `done.answer == Σ delta.text` — bất biến client **dựa vào để khôi phục khi mất khung** —
  **sai một cách âm thầm**;
- và vì transcript được lưu **từ chính `answer` đó** (D12), **DB cũng lưu bản thiếu**.

**Vì sao suite Phase 14 không bắt được:** nó khẳng định `meta` trước `delta`, đúng **1** `done`, và
`done.answer == Σ delta`… nhưng `Σ delta` được tính từ các khung **đã bỏ khung cuối**, nên **hai vế cùng sai** và
phép so sánh vẫn xanh. Lỗi chỉ lộ ra khi Giai đoạn 14.5 cần **parse JSON từ câu trả lời**: khối ```json bị cụt ở
khung cuối ⇒ parser trả `null` ⇒ tính năng im lặng không hoạt động (đó chính là triệu chứng đầu tiên gặp phải).

**Cách sửa:** nối `delta` **trước**, kiểm `Done` **sau** (một dòng, ở **cả hai** service). **Khoá lại** bằng
`Pure/FakeStreamingProviderTests`: khẳng định khung `Done` **thật sự mang text**, rằng câu trả lời **đầy đủ**
parse ra được proposal, và rằng **bỏ** khung đó làm câu trả lời **ngắn đi** — tức bất biến, không phải một ví dụ.
**Không** test cũ nào bị sửa.

### P2 — Duyệt `MoveTaskToColumn` trả **500** (vấn đề cấu trúc)

`AiActionService.ApproveAsync` mở một transaction rồi gọi applier; `TaskService.MoveTaskAsync` **cũng** mở
transaction riêng ⇒ `InvalidOperationException: The connection is already in a transaction` ⇒ mọi lượt duyệt đề
xuất di chuyển thẻ trả **500**. **Cách sửa:** bỏ transaction **thừa** trong `MoveTaskAsync` — mọi thay đổi đã nằm
trong **một** `SaveChangesAsync` (bản thân nó là một transaction) nên **tính nguyên tử không đổi**, và thứ tự
"ghi `activity_logs` **sau** khi commit" vẫn đúng. Sửa ở phía applier (tự di chuyển thẻ bằng tay) sẽ **nhân bản**
logic đánh số lại vị trí và **bỏ mất broadcast SignalR** mà mọi client khác phụ thuộc.

---

## 5. Thay đổi có chủ ý đối với thứ đã verify

| # | Thay đổi | Vì sao **buộc** phải làm | Phạm vi |
|---|---|---|---|
| 1 | **Migration thứ 11** `Phase14_5AgentChat` (`agent_chat_messages`) | Người dùng chốt **lưu transcript ở DB**. "Client giữ hội thoại" **không thể** làm hội thoại sống qua reload và giống nhau với mọi người mở board | **Lệch DoD có chủ ý** so với Phase 14 D1/D2. Ghi ở `phase-14-ai-advanced.md` §8, `03-roadmap.md`, `04-database-design.md` §6 |
| 2 | **`AgentChat` là section option riêng**, trần 2–3× | Người dùng yêu cầu nâng token. Nhét vào `AiChat` sẽ **âm thầm nới cả trần của tab "Hỏi AI"** — đúng cảnh báo **D9 của Phase 14** | Chat task **không đổi** hành vi; `AiChatOptions` **không** bị sửa |
| 3 | **`AiChatTranscriptValidator` được tách ra** khỏi `AiChatService` | Hai chat cần **cùng luật** với trần khác nhau; một bản sao thứ hai là một chỗ để trôi | Logic **byte-identical** với bản trong `AiChatService` (cùng chuỗi thông điệp); mọi suite Phase 14 xanh **nguyên trạng** |
| 4 | **Nhãn tab `TaskDetailModal`** đổi thành "Hỏi AI về thẻ này" | Board có "AI Agent Chat" riêng ⇒ nhãn cũ không phân biệt được hai cửa vào | **1 assertion** trong `TaskDetailModal.test.tsx` được sửa; `data-testid="ai-chat-tab"`, thứ tự tab, tab mặc định **giữ nguyên** — **ngoại lệ duy nhất** |
| 5 | **Bỏ `BeginTransactionAsync` trong `TaskService.MoveTaskAsync`** | Nếu còn, duyệt `MoveTaskToColumn` luôn **500** (P2) | Nguyên tử **không đổi** (một `SaveChangesAsync`); `KanbanApiTests` + toàn bộ suite di chuyển thẻ xanh |
| 6 | **`AgentRunReaper` tách thành `…Runner` + hosted service** | `BackgroundService` không resolve được từ container ⇒ không test nào gọi trực tiếp được | `AgentRunReaper_ClosesAnOrphanRunLeftBehindByASleepingHost` (Phase 7) vẫn xanh **nguyên trạng**, không sửa test |
| 7 | **`Input.Description` của `ConfirmSmartSetupRequest`** dùng `summary` đã validate | Record yêu cầu `Description` (không optional); chat không có mô tả người dùng để đưa | Chỉ ảnh hưởng payload của `CreateSubtasks` **sinh từ chat** |
| 8 | **Bỏ fetch trùng badge ở `BoardView`** | Hai round trip cho một con số | Badge **hành vi không đổi**; hàng loạt test `BoardView` xanh |

**Không có test cũ nào khác bị sửa.** Ngoại lệ **duy nhất** là 1 assertion nhãn tab ở hàng #4 ở trên.

---

## 6. Kiểm thử tay (1 lượt — trên máy dev)

| # | Thao tác | Kỳ vọng | Kết quả |
|---|---|---|---|
| a | Mở một board ⇒ nút nổi góc phải-dưới | Panel docked mở ra, rộng ~1/4 canvas | ✅ |
| b | Kéo 2 thẻ từ cột vào panel | 2 pill hiện ra; **thẻ KHÔNG đổi cột** | ✅ |
| c | Kéo lại **đúng** thẻ đó | `message.warning` "Thẻ này đã được gắn vào cuộc trò chuyện." | ✅ |
| d | Hỏi "Nên chuyển thẻ này đi đâu?" | Câu trả lời **hiện dần** | ✅ |
| e | **F5** | Hội thoại **vẫn còn** (transcript ở DB) | ✅ |
| f | Card *Đề xuất: Di chuyển thẻ* ⇒ **Gửi đề xuất** | "Đã tạo đề xuất…"; *Lịch sử AI* có `Pending` | ✅ |
| g | **Approve** | Thẻ **đổi cột thật**; client thứ hai cập nhật qua SignalR | ✅ |
| h | **Undo** | Thẻ **về cột cũ** | ✅ |
| i | **Lưu thành bình luận** rồi Approve | Bình luận xuất hiện **đúng thẻ**, tác giả = người bấm lưu | ✅ |
| j | `BoardListPage` ⇒ *Lịch sử AI* | Thấy log `CreateBoardFromTemplate` (**trước đây không hiện ở đâu**) | ✅ |
| k | Tab "Hỏi AI về thẻ này" trong `TaskDetailModal` | Hoạt động **như Phase 14** | ✅ |
| l | `AgentChat:Enabled=false` (host riêng) | **503** trước khi chạm DB/provider | ✅ (test `AGENTCHAT*_503`) |

---

## 7. Việc còn lại / nợ kỹ thuật (ghi rõ, không giấu)

1. **Task chat trong `TaskDetailModal` vẫn transcript ở client** ⇒ cùng một thẻ có thể có **2 nguồn hội thoại** (tab trong modal tạm thời, panel board bền). **Cố ý** giữ để không phá hợp đồng + test Phase 14 đã verify; `agent_chat_messages.task_id` là **chỗ chừa** để hợp nhất sau **không cần migration**.
2. **Chat không có tool/function-calling** — model chỉ *đề xuất* JSON, người bấm gửi, Manager duyệt. Đúng chốt người dùng và giữ chat **không tự ghi**.
3. **Lưu transcript là best-effort:** lỗi DB sau khi stream đã mở chỉ được log (câu trả lời đã tới tay người dùng; `pending` không thể đổi thành 500).
4. **`TestServer` có thể đệm body** ⇒ suite khoá **hợp đồng** (thứ tự khung, `done.answer == Σ delta`), không chứng minh được streaming ở tầng byte. Bỏ `FlushAsync` trong `SseStreamResult` ⇒ **không test nào đỏ** (điểm cần review bằng mắt).
5. **Nợ A3 (chuyển cho Phase 15/16):** `task_attachments` chỉ `text/*` ≤ 512 KB (Phase 15 cần diff/ảnh) · chưa có idempotency-key cho webhook · chưa có seam `IWorkItemLink` (GitHub PR ↔ task) · chưa có AI provider fallback.
6. **Máy dev cần PostgreSQL thật để `Skipped: 0`.** Bộ test local phải trỏ `TEAMNEXUS_TEST_DB` vào cổng **5433** (`docker start teamnexus-test-pg`) — mặc định của `DatabaseFixture` là **5432**, và khi DB không tới được thì **356/584 test bị SKIP** (cố ý, Phase 8 D4). Trên CI, service container ở cổng 5432 nên cổng `$skipped -gt 0` bắt đúng tình huống này.
7. **Flake có sẵn từ Phase 7** `AgentRun_CancelStopsTheRunAndRecordsCancelled` (đỏ ~1/3 lượt chạy đầy đủ, chạy riêng ⇒ PASS). **Không** do 14.5; **không** sửa test Phase 7.

---

## 8. Bảng bằng chứng & nơi đối chiếu

| Bằng chứng | Lệnh | Kết quả |
|---|---|---|
| Build sạch | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | **0 / 0** |
| Test backend | `$env:TEAMNEXUS_TEST_DB='…Port=5433…'; dotnet test tests/TeamNexus.Api.Tests` | **643 / 0 / 0** |
| Migration | `dotnet ef migrations list --no-build` | **11** |
| Model khớp snapshot | `dotnet ef migrations has-pending-model-changes --no-build` | *No changes…* |
| Test FE | `npm test` | **633 / 107 file** |
| Lint / types / build FE | `npm run lint` · `npx tsc -b` · `npm run build` | **0/0** · exit 0 · OK |
| Cổng CI | `.github/workflows/ci-backend.yml` · `ci-web.yml` | **643** · **`-lt 633`** |

**Tài liệu đã cập nhật trong cùng phiên:** `tasks/phase-14-5-foundation-and-agent-chat.md` ·
`tasks/phase-14-ai-advanced.md` (§8) · `03-roadmap.md` · `01-system-specification.md` (§14) ·
`04-database-design.md` (§3.10 · §4 · §5 · §6 · §7) · `README.md` (11 migration + mục "Trạng thái (14.5)") ·
`src/Modules/Ai/…/README.md` (## Phase 14.5) · báo cáo này.
