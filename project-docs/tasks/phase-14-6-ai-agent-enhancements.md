# Giai đoạn 14.6 — Hoàn thiện Trải nghiệm & Nâng cấp Năng lực AI Agent

> **Trạng thái:** 📋 **KẾ HOẠCH & ĐẶC TẢ CHI TIẾT.**
> **Nguồn gốc:** Đúc kết từ kiểm thử thực chiến trên live `app.teamnexus.cloud` sau Giai đoạn 14.5.
> **Mục tiêu:** Giải quyết dứt điểm các điểm ma sát (UX friction) của hệ thống AI hiện tại, đồng thời thiết kế kiến trúc nâng cấp TeamNexus thành một nền tảng **Autonomous AI Agent** chuyên nghiệp trước khi bước vào Giai đoạn 15 (Tích hợp bên ngoài) và 16 (Mobile Flutter).
> **Nguyên tắc bất biến:**
> - **Accountability Layer giữ nguyên 100%:** AI không bao giờ tự ý ghi đè dữ liệu nghiệp vụ nếu chưa có xác nhận/duyệt của con người (`Pending` → `CAS Approve` → `Undo`).
> - **Không thêm migration DB ở Track A:** Tận dụng tối đa schema hiện có (`agent_chat_messages` đã có sẵn cột `task_id` từ Migration 11).
> - **Giữ vững trần ngân sách & token guardrail:** Ngăn chặn bùng nổ chi phí LLM.

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

### 3.1. Tiêu chuẩn Kiểm thử (Test Gates)
- **Backend Tests:** Tất cả 643 tests hiện tại giữ nguyên trạng thái PASS. Thêm unit/integration tests cho:
  - Board Chat Prompt: Khẳng định không có GUID nào xuất hiện trong câu trả lời văn bản.
  - Task Chat Transcript: Kiểm tra lưu và nạp transcript từ bảng `agent_chat_messages` qua khóa `task_id`.
  - Lightweight Board Digest: Kiểm tra payload context khi không có thẻ đính kèm.
- **Frontend Tests:** Tất cả 633 tests hiện tại giữ nguyên trạng thái PASS. Thêm tests cho:
  - Card Picker dropdown trong `AgentChatPanel`.
  - Context menu action "Gửi vào AI Chat" từ `TaskCard`.
- **Lint & Build:** `npm run lint` (0 warning), `npx tsc -b` (exit 0), `npm run build` (OK).

### 3.2. Definition of Done (DoD)
1. Người dùng có thể đính kèm thẻ vào AI Chat bằng cả kéo-thả và nút chọn thẻ trực tiếp.
2. AI Chat không bao giờ để lộ UUID/GUID của database ra giao diện văn bản người dùng.
3. Người dùng hỏi tiến độ board mà không đính kèm thẻ vẫn nhận được câu trả lời tổng quan chính xác.
4. Lịch sử chat của từng task được lưu bền vững vào database và nạp lại đầy đủ khi mở lại modal.
5. Bất biến Accountability Layer: Mọi thay đổi dữ liệu vẫn phải thông qua xác nhận `Pending` → `Approve` → `Undo`.
