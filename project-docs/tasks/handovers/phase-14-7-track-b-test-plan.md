# Kế Hoạch Kiểm Thử Thực Chiến (Live E2E Test Plan) — Track B Giai Đoạn 14.7

> **Mục tiêu:** Kiểm thử thực chiến trực quan và chức năng (Live Browser E2E) trên môi trường production `https://app.teamnexus.cloud` cho toàn bộ các tính năng mới của **Track B (Giai đoạn 14.7: Autonomous Agent & Workspace Knowledge Platform)**.  
> **Tài liệu căn cứ:** `project-docs/03-roadmap.md` (§14.7 Track B), `project-docs/tasks/phase-14-7-autonomous-agent-jev.md` (§12, §B1–§B3).  
> **Thời điểm lập:** 15/10/2026.  
> **URL mục tiêu:** `https://app.teamnexus.cloud`

---

## 1. Phạm Vi Kiểm Thử (Scope)

Kiểm thử tập trung vào 3 trụ cột kỹ thuật của Track B:

### §B1. Multi-Agent Personas (Đội ngũ AI Chuyên Gia)
1. **Catalog Personas:**
   - 4 persona chính thức: `general` (TeamNexus Agent / Mặc định), `scrum_master` (AI Scrum Master), `tech_lead` (AI Tech Lead), `qa_security` (AI QA & Security).
2. **Bộ chọn Vai trò (Persona Picker UI):**
   - Vị trí: Header của `AgentChatPanel` (`data-testid="agent-chat-persona-row"`).
   - Trạng thái hiển thị: Chỉ hiện khi danh mục persona có từ 2 lựa chọn trở lên; hiển thị nhãn thân thiện (DisplayName) thay vì raw key.
   - Hành vi: Cho phép chọn vai trò chuyên biệt hoặc xóa trắng (clear) để về mặc định của hệ thống. Bị disabled khi đang stream câu trả lời (`isBusy`).
3. **Luồng Chat Stream theo Persona:**
   - Khi chọn persona, request stream gửi kèm query param `?persona=<key>`.
   - Phản hồi từ Agent mang đúng ngữ cảnh, góc nhìn chuyên môn của vai trò được chọn:
     - `scrum_master`: Tập trung vào sprint, velocity, blockers, user stories, DoD.
     - `tech_lead`: Tập trung vào kiến trúc, clean code, hiệu năng, bảo mật API, thiết kế hệ thống.
     - `qa_security`: Tập trung vào ca kiểm thử, biên dữ liệu, lỗ hổng OWASP, edge cases.
   - Nhãn persona hiển thị rõ trên từng tin nhắn trợ lý tương ứng.
4. **Hồ sơ Thành viên & Phân bổ Task:**
   - Bảng thành viên Workspace (`/workspaces/:id/members`) hiển thị các Agent persona với đúng `personaKey` và cờ AI Agent.

### §B2. Mở Rộng Tool-Calling (ReAct Loop 3 Tool Mới)
1. **`InspectMemberWorkload`:**
   - Truy vấn khối lượng việc của thành viên trong workspace (số thẻ mở, quá hạn, in-progress, `maxWipLimit`, `capacityWeight`).
   - Kiểm tra Agent trả lời chính xác số liệu phân bổ công việc.
2. **`MatchSkills`:**
   - So khớp kỹ năng của các thành viên trong workspace theo taxonomy/skills.
   - Thử nghiệm hỏi Agent tìm người phù hợp nhất cho một tác vụ cụ thể.
3. **`ValidateAcceptanceCriteria`:**
   - Agent gọi Jev để đánh giá chất lượng hoàn thành của task so với tiêu chí nghiệm thu (Acceptance Criteria).
   - Kiểm tra hành vi khi Jev hoạt động hoặc xử lý an toàn (graceful fallback) khi Jev tắt (`verified: null`).

### §B3. Kho Tri Thức Dự Án (Workspace RAG & Knowledge Base)
1. **Điều hướng & Nút truy cập:**
   - Nút **"Kho tri thức"** (`data-testid="nav-knowledge-toolbar-btn"`) trên thanh công cụ Board List (`/workspaces/:workspaceId/boards`).
   - URL: `/workspaces/:workspaceId/knowledge`.
2. **Trạng thái Tính năng & Giao diện Kho Tri Thức:**
   - Kiểm tra trạng thái bật/tắt của RAG (`Jev:RagEnabled`): nếu tắt, hiển thị Alert thông báo rõ ràng; nếu bật, hiển thị danh sách tài liệu và nút "Thêm tài liệu".
   - Bảng tài liệu: Tiêu đề, Trạng thái (`pending` / `ready` / `failed`), Số đoạn (chunks), Nguồn (Source URL), Ngày tạo, Lỗi (nếu có), Nút thao tác xóa.
3. **Luồng Phê Duyệt Accountability Layer (Bất biến tối cao):**
   - **Tạo tài liệu mới:** Nhập Tiêu đề, Nội dung, Source URL tuỳ chọn.
   - **Xác thực trạng thái Pending:** Bấm gửi KHÔNG ĐƯỢC ghi trực tiếp vào DB, mà phải tạo ra Action Log ở trạng thái `Pending` (`CreateKnowledgeDocument`).
   - **Thông báo UI:** Thông báo *"Đã gửi yêu cầu thêm tài liệu — chờ Manager duyệt trong Lịch sử AI"*.
   - **Phê duyệt:** Manager truy cập Lịch sử AI (`/workspaces/:id/activity` hoặc drawer/modal Lịch sử AI) để bấm **Phê duyệt (Approve)**.
   - **Xác nhận ghi dữ liệu & Embedding:** Sau khi duyệt, tài liệu xuất hiện trong danh sách và chuyển trạng thái `pending` → `ready` khi runner nhúng vector thành công.
   - **Xóa tài liệu:** Thao tác xóa cũng phải sinh Action Log `Pending` và chỉ soft-delete khi được phê duyệt; hỗ trợ **Hoàn tác (Undo)**.
4. **Hỏi đáp Tri thức qua Agent Chat (RAG Integration):**
   - Đặt câu hỏi đặc thù chỉ có trong tài liệu vừa nạp.
   - Kiểm tra Agent có trích xuất khối `KIẾN THỨC DỰ ÁN` để trả lời chính xác thông tin nội bộ mà không bịa đặt (hallucinate) hay không.

---

## 2. Kịch Bản Kiểm Thử Chi Tiết (Test Scenarios)

| Mã | Tên kịch bản | Các bước thực hiện | Kết quả kỳ vọng |
|---|---|---|---|
| **TC-B01** | Kiểm tra Điều hướng & Giao diện Kho Tri Thức | 1. Đăng nhập vào `app.teamnexus.cloud`<br>2. Chọn một Workspace<br>3. Tìm nút "Kho tri thức" trên thanh toolbar Board List<br>4. Bấm vào nút | 1. Nút "Kho tri thức" hiển thị icon database, style chuẩn Ant Design.<br>2. Điều hướng mượt mà đến `/workspaces/{id}/knowledge`.<br>3. Trang hiển thị tiêu đề "Kho tri thức dự án", breadcrumbs, bảng tài liệu hoặc Alert trạng thái nếu RAG tắt. |
| **TC-B02** | Thêm Tài liệu Tri Thức & Kiểm tra Accountability Layer | 1. Trên trang Kho tri thức, bấm "Thêm tài liệu"<br>2. Điền Tiêu đề và Nội dung tài liệu dự án<br>3. Bấm "Gửi yêu cầu" | 1. Modal mở chuẩn Ant Design, validate độ dài hợp lệ.<br>2. Thông báo thành công: *"Đã gửi yêu cầu thêm tài liệu — chờ Manager duyệt trong Lịch sử AI"*.<br>3. Tạo bản ghi Action Log `CreateKnowledgeDocument` với status `Pending`. Chưa xuất hiện ngay như tài liệu `ready`. |
| **TC-B03** | Phê Duyệt Tài liệu trong Lịch sử AI | 1. Điều hướng vào trang Lịch sử AI / Hoạt động của Workspace<br>2. Tìm đề xuất `CreateKnowledgeDocument`<br>3. Bấm "Phê duyệt" (Approve) | 1. Action chuyển sang `Approved`.<br>2. Quay lại Kho tri thức: Tài liệu xuất hiện trong danh sách, trạng thái chuyển sang `pending` hoặc `ready`.<br>3. Hiển thị số đoạn chunks chính xác. |
| **TC-B04** | Kiểm tra Bộ chọn Persona trong AI Agent Chat | 1. Mở một Board bất kỳ<br>2. Bấm mở panel "AI Agent Chat"<br>3. Quan sát hàng chọn Persona dưới Header | 1. Panel mở bên phải màn hình.<br>2. Xuất hiện hàng "Vai trò" với Select dropdown chứa các persona: Mặc định, AI Scrum Master, AI Tech Lead, AI QA & Security.<br>3. Style Ant Design chuẩn, không vỡ layout. |
| **TC-B05** | Chat với Persona "AI Tech Lead" | 1. Chọn vai trò "AI Tech Lead"<br>2. Gửi câu hỏi về kiến trúc microservices hoặc tối ưu database<br>3. Quan sát quá trình streaming | 1. Request stream đính kèm `?persona=tech_lead`.<br>2. Phản hồi tập trung vào khía cạnh kỹ thuật, architecture, clean code, bảo mật.<br>3. Tin nhắn có nhãn hiển thị "AI Tech Lead". |
| **TC-B06** | Chat với Persona "AI Scrum Master" | 1. Đổi vai trò sang "AI Scrum Master"<br>2. Gửi câu hỏi về quản lý sprint, xử lý task bị block<br>3. Quan sát phản hồi | 1. Request stream đính kèm `?persona=scrum_master`.<br>2. Phản hồi mang phong cách Scrum Master (hỗ trợ nhóm tháo gỡ khó khăn, sprint goal, DoD).<br>3. Tin nhắn có nhãn "AI Scrum Master". |
| **TC-B07** | Thử nghiệm ReAct Tools & RAG Tri thức | 1. Đặt câu hỏi về tải việc của thành viên hoặc kỹ năng của team (`InspectMemberWorkload`, `MatchSkills`)<br>2. Đặt câu hỏi dựa trên nội dung tài liệu đã nạp vào Kho tri thức | 1. Agent trả lời đúng thông tin thực tế từ database/tài liệu tri thức.<br>2. Không rò rỉ ID kỹ thuật (GUID/UUID) ra văn bản trả lời. |
| **TC-B08** | Xóa Tài liệu Tri Thức & Kiểm tra Hoàn tác (Undo) | 1. Bấm nút xóa một tài liệu trong Kho tri thức<br>2. Xác nhận yêu cầu xóa<br>3. Phê duyệt yêu cầu xóa trong Lịch sử AI | 1. Sinh Action Log `DeleteKnowledgeDocument` ở trạng thái `Pending`.<br>2. Sau khi duyệt, tài liệu bị ẩn (soft-delete).<br>3. Nút Hoàn tác (Undo) có thể khôi phục tài liệu về trạng thái khả dụng. |

---

## 3. Tiêu Chí Nghiệm Thu (Acceptance Criteria) & Bất Biến

1. **Giao diện & Trải nghiệm (UI/UX):**
   - 100% tuân thủ Ant Design v6, không dùng Tailwind CSS gây vỡ layout (rút kinh nghiệm sâu sắc từ sự cố Track A).
   - Responsive tốt, font chữ, khoảng cách, tag màu sắc hài hòa và sắc nét.
2. **Tính toàn vẹn dữ liệu (Accountability Layer):**
   - Mọi thao tác ghi dữ liệu (thêm/xóa tài liệu tri thức) tuyệt đối không được ghi đè trực tiếp mà phải qua quy trình `Pending` → `Approve` → `Undo`.
3. **Bảo mật & Ổn định:**
   - Không để lộ thông tin nhạy cảm, API key hay raw GUID/UUID ra giao diện chat.
   - Khi các dịch vụ AI bên ngoài (OpenRouter/DeepSeek/Jev) gặp độ trễ mạng hoặc timeout, hệ thống phải xử lý suy giảm an toàn (graceful degradation) mà không làm sập ứng dụng.
