# Giai đoạn 14.6 — Hoàn thiện Trải nghiệm & Nâng cấp Năng lực AI Agent

> **Trạng thái:** 🟡 **TRACK A ĐÃ THI HÀNH XONG (backend + frontend + test).** Track B vẫn là 📋 kế hoạch.
> **Nguồn gốc:** Đúc kết từ kiểm thử thực chiến trên live `app.teamnexus.cloud` sau Giai đoạn 14.5.
> **Mục tiêu:** Giải quyết dứt điểm các điểm ma sát (UX friction) của hệ thống AI hiện tại, đồng thời thiết kế kiến trúc nâng cấp TeamNexus thành một nền tảng **Autonomous AI Agent** chuyên nghiệp trước khi bước vào Giai đoạn 15 (Tích hợp bên ngoài) và 16 (Mobile Flutter).
> **Nguyên tắc bất biến:**
> - **Accountability Layer giữ nguyên 100%:** AI không bao giờ tự ý ghi đè dữ liệu nghiệp vụ nếu chưa có xác nhận/duyệt của con người (`Pending` → `CAS Approve` → `Undo`).
> - **Không thêm migration DB ở Track A:** Tận dụng tối đa schema hiện có (`agent_chat_messages` đã có sẵn cột `task_id` từ Migration 11).
> - **Giữ vững trần ngân sách & token guardrail:** Ngăn chặn bùng nổ chi phí LLM.
>
> **Báo cáo nghiệm thu Track A:** `project-docs/report/phase-14-6-track-a-test-report.md`.


---

## 0. Tổng quan 2 Track của Giai đoạn 14.6

| Track | Phạm vi | Mục tiêu trọng tâm | Tác động Schema / Migration |
|---|---|---|---|
| **Track A — Hoàn thiện Trải nghiệm (Quick-Wins & Hardening)** | Frontend + Backend Prompt & Chat Service | Khắc phục các lỗi UX và ma sát phát hiện qua kiểm thử thực tế: bộ chọn đính kèm thẻ, chặn rò rỉ GUID, hợp nhất transcript task chat, board digest ban đầu. | **0 migration mới** (dùng lại cột `task_id` sẵn có ở migration 11). |
| **Track B — Nâng cấp Năng lực AI Agent (Autonomous Platform)** | AI Engine + Multi-Agent Workflows | Đưa TeamNexus từ tương tác hỏi-đáp đơn thuần lên hệ thống AI Agent tự động: Đội ngũ Agent chuyên biệt (Personas), AI Observer hành động chủ động (Proactive Recovery Plan), Tool-calling nâng cao và bộ nhớ Workspace RAG. | Độc lập, mở rộng theo module. |

---

## 1. Track A — Khắc phục ma sát & Hoàn thiện trải nghiệm

### A1. Bộ chọn đính kèm thẻ trực quan (Card Attachment Picker)
- **Vấn đề thực tế phát hiện:** Hiện tại `AgentChatPanel` chỉ hỗ trợ kéo-thả (Drag & Drop) thẻ từ cột Kanban vào dropzone. Trên laptop dùng trackpad, màn hình cảm ứng hoặc bảng Kanban có nhiều cột cuộn ngang, thao tác kéo một thẻ qua quãng đường dài vào ô dropzone nhỏ rất dễ bị trượt hoặc rơi nhầm vào cột khác.
- **Giải pháp thiết kế:**
  1. Thêm nút **"+ Đính kèm thẻ"** ngay tại thanh header của vùng đính kèm trong `AgentChatPanel`.
  2. Bấm nút sẽ mở một Popover/Dropdown tìm kiếm nhanh thẻ trong board (hiển thị title, priority, assignee). Click để đính kèm (vẫn tuân thủ trần `MaxAttachedTasks = 8`).
  3. Thêm mục action **"Gửi vào AI Chat"** trong menu context (`...`) của từng `TaskCard` trên Kanban. Khi bấm, tự động mở panel AI chat và đính kèm thẻ đó vào danh sách.
  4. Cơ chế kéo-thả hiện tại (`useDroppable`) vẫn được duy trì song song cho người dùng muốn kéo nhanh.

### A2. Chặn rò rỉ ID kỹ thuật (GUID / UUID) trong System Prompt
- **Vấn đề thực tế phát hiện:** Trong phản hồi stream thực tế, model `deepseek-chat` vô tình render raw UUID của cột ra văn bản người dùng: `Sau khi có thẻ trong ngữ cảnh, mình sẽ đề xuất chuyển sang cột "process" (id: 9724c884-d103-4147-9b68-7532d97f04a9)`.
- **Giải pháp thiết kế:**
  1. Cập nhật System Prompt trong `AgentChatService.cs`:
     - Bổ sung quy tắc: *"Tuyệt đối KHÔNG hiển thị raw ID, GUID hoặc UUID trong văn bản trò chuyện thông thường. Chỉ đề cập tên hiển thị của thẻ và tên hiển thị của cột bằng ngôn ngữ tự nhiên. Các ID kỹ thuật chỉ được xuất hiện bên trong khối JSON proposal cuối cùng."*
  2. Bổ sung bộ lọc sanitize ở `AiChatSseWriter` hoặc frontend parser để ẩn mọi chuỗi dạng UUID xuất hiện trong text nếu model vô tình sinh ra.

### A3. Hợp nhất Transcript Task Chat vào PostgreSQL (`agent_chat_messages`)
- **Vấn đề thực tế phát hiện:** Hiện tại, cuộc trò chuyện tại tab *"Hỏi AI về thẻ này"* trong `TaskDetailModal` lưu transcript ở client memory (F5 mất, người khác mở không thấy). Trong khi đó, Board Chat lại lưu transcript bền vững trong PostgreSQL.
- **Giải pháp thiết kế:**
  - Bảng `agent_chat_messages` đã có sẵn cột `task_id` (nullable, kèm CHECK `num_nonnulls(board_id, task_id) = 1` từ Migration 11).
  - Mở endpoint `GET /api/tasks/{taskId}/agent-chat/messages` và cho phép `POST /api/tasks/{taskId}/ai-chat/stream` ghi transcript vào `agent_chat_messages` với `task_id = taskId` và `board_id = null`.
  - Giúp hội thoại của task được lưu trữ bền vững qua các phiên làm việc của nhóm, xóa bỏ tình trạng phân mảnh hai chuẩn chat.

### A4. Cung cấp "Lightweight Board Digest" khi chưa đính kèm thẻ
- **Vấn đề thực tế phát hiện:** Khi người dùng mở chat board và hỏi *"Tiến độ bảng Sprint 1 thế nào?"*, AI trả lời rỗng vì chưa có thẻ nào được kéo vào ngữ cảnh (hệ thống chỉ gửi danh sách tên cột).
- **Giải pháp thiết kế:**
  - Khi `attachedTaskIds` rỗng, server tự động đính kèm một bản tóm tắt siêu nhẹ (**Lightweight Board Digest**):
    - Thống kê số lượng thẻ ở từng cột.
    - Danh sách top thẻ quan trọng (tiêu đề, độ ưu tiên, hạn chót, người phụ trách) không vượt quá 2.000 ký tự.
  - Cho phép AI trả lời tổng quan tiến độ của cả board ngay lập tức mà không vi phạm giới hạn token hoặc đòi hỏi người dùng phải đính kèm thủ công từng thẻ.

---

## 2. Track B — Nâng cấp Năng lực AI Agent (Autonomous Platform)

### B1. Đội ngũ AI Chuyên Gia Đa Vai Trò (Multi-Agent Personas)
Thay vì chỉ có một bot duy nhất ("AI Agent"), xây dựng hệ thống Personas chuyên môn hóa:
1. **`AI Scrum Master / Product Owner`:**
   - Đánh giá chất lượng User Story & Acceptance Criteria.
   - Phân tích rủi ro trễ hạn Sprint, kiểm tra khối lượng công việc giữa các thành viên.
2. **`AI Tech Lead`:**
   - Phân rã kỹ thuật sâu, gợi ý kiến trúc DB, RESTful API endpoint, bảo mật.
3. **`AI QA & Security Engineer`:**
   - Sinh danh sách test cases chi tiết, edge cases và checklist kiểm thử bảo mật cho từng tính năng.
- **Cơ chế tương tác:** Người dùng có thể chọn Persona trong header của `AgentChatPanel` hoặc `@mention` trực tiếp trong trao đổi (ví dụ: `@qa hãy đề xuất kịch bản test`).

### B2. Kịch bản Phục hồi Chủ động (Proactive Auto-Healing từ AI Observer)
- **Hiện trạng:** AI Observer chỉ phát hiện và tạo thông báo thụ động (ví dụ: *"Task X quá hạn 25 ngày"*). Quản lý đọc xong vẫn phải tự thao tác thủ công.
- **Cơ chế mới:** Khi phát hiện tín hiệu `CRITICAL` hoặc `HIGH`:
  1. AI Observer tự động sinh một **Kịch bản Khắc phục Đề xuất (Recovery Plan Proposal)** đính kèm trong thông báo:
     - Đề xuất 1: Điều phối task sang thành viên có độ tải thấp nhất (`UpdateTaskFields`).
     - Đề xuất 2: Phân rã phần còn tồn đọng thành subtasks cho sprint kế tiếp (`CreateSubtasks`).
     - Đề xuất 3: Điều chỉnh deadline khả thi (`UpdateTaskFields`).
  2. Quản lý có thể bấm **"Phê duyệt giải pháp"** ngay trong Notification Center / Lịch sử AI để áp dụng toàn bộ kịch bản chỉ với 1 click.

### B3. Mở rộng Tool-Calling (ReAct Pattern) cho AI Agent Executor
- Nâng cấp vòng lặp của Phase 7 từ bộ công cụ cơ bản sang cơ chế ReAct linh hoạt:
  - Tool `SearchProjectContext`: Tìm kiếm toàn bộ task, comment, quyết định trong board.
  - Tool `InspectMemberWorkload`: Đọc khối lượng công việc thực tế của từng thành viên để gán task tối ưu.
  - Tool `ValidateAcceptanceCriteria`: Kiểm tra xem sản phẩm đầu ra đã thỏa mãn mô tả ban đầu chưa.
- Kết quả thực thi tiếp tục đi qua Accountability Layer để con người nghiệm thu.

### B4. Bộ nhớ Dự án Ngữ nghĩa (Workspace Knowledge Base & RAG với pgvector)
- Tận dụng PostgreSQL sẵn có để kích hoạt extension `pgvector`.
- Cho phép Workspace tải lên tài liệu dự án (PRD, coding conventions, API guidelines, kiến trúc hệ thống).
- Khi Agent thực hiện Smart Setup hoặc Chat, hệ thống tự động vector search để lấy context chính xác của dự án, giúp subtasks và giải pháp phân rã khớp 100% với chuẩn kỹ thuật nội bộ của team.

---

## 3. Ma trận Kiểm thử & Định nghĩa Hoàn thành (DoD)

### 3.1. Tiêu chuẩn Kiểm thử (Test Gates) — ✅ TRACK A ĐÃ ĐẠT
- **Backend Tests:** ~~643~~ → **665 PASS** (`Skipped: 0`, PostgreSQL 18 thật); **0** test cũ bị viết lại, **1** assertion được sửa (xem §4, ngoại lệ duy nhất). Đã thêm đúng ba nhóm được yêu cầu:
  - Board Chat Prompt + bộ lọc: `Pure/AgentChatDigestTests` (rule §A2/§A4, template không chứa GUID thật) + `Pure/AiChatAnswerSanitizerTests` + `Integration/Phase146TrackATests` (GUID model rò ra ⇒ transcript lưu DB đã lọc).
  - Task Chat Transcript: `Integration/Phase146TrackATests` (ghi/nạp qua `task_id`, isolation với board, phân quyền, keyset).
  - Lightweight Board Digest: `Pure/AgentChatDigestTests` (dựng khối + trần ký tự) + `Integration/Phase146TrackATests` (`PrepareAsync` **có** digest khi không gắn thẻ, **không** có khi đã gắn).
- **Frontend Tests:** ~~633~~ → **657 PASS / 110 file**. Đã thêm đúng hai nhóm được yêu cầu:
  - Card Picker trong `AgentChatPanel` (`AgentChatPanel.picker.test.tsx`, 5 test).
  - Menu `...` của `TaskCard` với "Gửi vào AI Chat" (`TaskCard.aiChat.test.tsx`, 4 test) **và** đường nối thật ở `BoardView.agentChat.test.tsx` (+2 test).
- **Lint & Build:** ✅ `npm run lint` **0 warning / 0 error** · `npx tsc -b` **exit 0** · `npm run build` **OK**.

### 3.2. Definition of Done (DoD) — ✅ đạt cả 5
1. Người dùng có thể đính kèm thẻ vào AI Chat bằng cả kéo-thả và nút chọn thẻ trực tiếp. ✅ (popover tìm kiếm + menu `...`; **cùng** một luật từ chối, **cùng** một cách hiển thị pill)
2. AI Chat không bao giờ để lộ UUID/GUID của database ra giao diện văn bản người dùng. ✅ (rule prompt **+** lọc trước khi ghi DB **+** lọc khi hiển thị **và** khi nạp lại; khối JSON đề xuất **không** bị lọc vì ID trong đó là hợp lệ — xem §4)
3. Người dùng hỏi tiến độ board mà không đính kèm thẻ vẫn nhận được câu trả lời tổng quan chính xác. ✅ (Lightweight Board Digest, trần 2.000 ký tự)
4. Lịch sử chat của từng task được lưu bền vững vào database và nạp lại đầy đủ khi mở lại modal. ✅ (`task_id` + `board_id = null`; **0 migration**)
5. Bất biến Accountability Layer: Mọi thay đổi dữ liệu vẫn phải thông qua xác nhận `Pending` → `Approve` → `Undo`. ✅ (**không** file nào của Accountability Layer bị sửa; `SubmitProposalAsync`/`SaveAnswerAsync` không đổi)

---

## 4. Track A — Nhật ký thi hành (phát sinh & lệch kế hoạch, ghi để không bị coi là sai)

> **Số ĐO THẬT:** backend **665 / 0 / 0** (baseline 643 ⇒ **+22**) · FE **657 / 110 file** (baseline 633 / 107 ⇒ **+24 / +3**) · `migrations list` = **11** (không đổi) · `has-pending-model-changes` = **sạch** · `dotnet build` **0/0** · lint **0/0** · `tsc -b` exit **0** · `npm run build` **OK**.
> Báo cáo đầy đủ: `report/phase-14-6-track-a-test-report.md`.

### 4.1 Phát sinh **bắt buộc** (không phải lệch kế hoạch — kế hoạch không thể biết trước)

| # | Phát sinh | Cách xử lý |
|---|---|---|
| **P1** | ❗ **BUG THẬT có sẵn từ Phase 14.5:** hai row của một lượt hỏi–đáp được ghi trong **một** `SaveChangesAsync`, mà `StampAuditableTimestamps` lấy **một** `DateTimeOffset.UtcNow` cho mỗi lần lưu ⇒ `created_at` **giống hệt nhau**. Truy vấn cũ (`created_at`, rồi `id`) để **Guid ngẫu nhiên** phân xử ⇒ khoảng **một nửa** số lần mở lại, câu trả lời hiện **trước** câu hỏi. Suite Phase 14.5 (`AGENTCHAT3`) chỉ **may** nên không bắt được. | Luật đọc **dùng chung** `AgentChatTranscriptQuery.NewestPageAsync`: `created_at` → **`role`** (hỏi trước đáp) → `id` (chỉ để ổn định). Vá **cả hai** transcript. **Không** sửa `StampAuditableTimestamps` (dùng chung mọi bảng; thứ tự `ChangeTracker.Entries` không được bảo đảm) và **không** thêm cột thứ tự (cần migration). |
| **P2** | React bubble sự kiện synthetic **xuyên portal**, nên item của `Dropdown` (render qua portal) vẫn là "click trong thẻ" ⇒ chọn "Gửi vào AI Chat" **cũng mở modal chi tiết thẻ**. | `info.domEvent.stopPropagation()` trong `onClick` của menu item (không phải chỉ trên nút trigger). |
| **P3** | Rule 7 của prompt **nhắc tên** tiêu đề digest (`"TỔNG QUAN BẢNG"`), nên test "không có digest" bằng chính tiêu đề đó **luôn đỏ** (xanh/đỏ giả). | Tách `AgentChatPrompts.DigestBlockMarker` = dòng **mở đầu** khối (`"- TỔNG QUAN BẢNG:"`) và dùng nó ở cả hai chiều khẳng định. |
| **P4** | `tasks.priority` là **text** ⇒ `ORDER BY priority` sắp theo **chữ cái** (`High, Low, Medium, Urgent`) — **ngược** mức độ gấp. | Xếp hạng top thẻ **trong bộ nhớ** bằng enum thật, trên một trang ứng viên bị chặn (`DigestCandidateLimit = 200`); số thẻ mỗi cột vẫn là query gộp riêng nên **chính xác** kể cả board > 200 thẻ. |
| **P5** | `useAiChat` trước đây **không** gọi `httpClient.get` ⇒ mock của `useAiChat.test.ts` thiếu hàm đó. | Thêm `get: vi.fn()` + `mockResolvedValue({ data: [] })` trong `beforeEach`. **Không** assertion nào bị đổi — chỉ bổ sung mock cho endpoint mới. |

### 4.2 **Lệch kế hoạch có chủ ý — ngoại lệ DUY NHẤT**

**`AgentChatApiTests.AGENTCHAT22` bị sửa 1 assertion** (và đổi tên cho đúng nội dung mới): từ
*"task chat vẫn ghi 0 row"* thành *"2 row, `task_id` set, `board_id` null — và transcript của board **rỗng**"*.

- **Vì sao:** Phase 14 **D2** ("server không giữ bảng chat nào") bị **§A3 thay thế có chủ ý** — đúng thứ mà
  tài liệu Phase 14.5 §8 đã ghi là **nợ kỹ thuật có chủ ý** với "chỗ chừa để hợp nhất sau **không cần
  migration**". Giữ nguyên assertion cũ nghĩa là **giữ nguyên chính defect mà A3 sinh ra để sửa**.
- **Phần KHÔNG đổi:** hợp đồng SSE, thứ tự khung, và `meta` vẫn mang **tên TASK** (không bị chat board chiếm).
- **Test còn được TĂNG CƯỜNG:** thêm khẳng định isolation hai chiều (transcript của board **rỗng** sau khi
  hỏi ở tab thẻ, và ngược lại được kiểm ở `Phase146TrackATests.PHASE146A_A2`).
- Ngoài dòng này, **không** test cũ nào bị sửa; `Pure/ObserverVocabularyTests` (Phase 14) **không** bị đụng.

### 4.3 Ghi chú thiết kế phải đọc trước khi "sửa lại cho đúng ý"

1. **§A2 KHÔNG viết lại luồng SSE.** `done.answer == Σ delta` là bất biến client dựa vào để khôi phục khung
   bị mất, mà một UUID **vắt qua hai khung** ⇒ lọc từng khung vừa phá bất biến, vừa cắt nát khối JSON đề
   xuất **thật** (`taskId`/`columnId` hợp lệ, **phải sống** để Manager duyệt được). Vì vậy: prompt (yêu cầu)
   + server lọc **phần lưu DB** + client lọc **phần hiển thị** trên **chuỗi tích luỹ**.
   → **Hợp đồng API:** client Phase 16 **phải** lọc prose trước khi render, **đừng** render thẳng `answer`.
2. **§A3 giữ nguyên hình dạng request/response của chat thẻ.** Stream và `/message` của Phase 14 **không**
   đổi shape (Flutter dùng lại nguyên); chỉ **thêm** một route đọc `GET /api/tasks/{taskId}/agent-chat/messages`.
3. **Retention không có chủ sở hữu thứ hai.** `AgentChatRetentionRunner` quét theo `created_at` **toàn bảng**
   ⇒ lượt của thẻ tự động được dọn cùng; **đừng** viết runner thứ hai.

### 4.4 Kiểm thử tay 1 lượt
Xem `report/phase-14-6-track-a-test-report.md` §6 (6 bước, gồm cả bước kiểm tra bất biến Accountability).

