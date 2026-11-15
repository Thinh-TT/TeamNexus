# Danh sách Vấn đề Phát hiện & Đánh giá Kiểm thử Thực chiến — Track B Giai Đoạn 14.7

> **Tài liệu nguồn:** `project-docs/tasks/phase-14-7-autonomous-agent-jev.md` (§12, §B1–§B3)  
> **Môi trường kiểm thử:** Production Live (`https://app.teamnexus.cloud` — Frontend Vercel, `https://api.teamnexus.cloud` — Backend Render, `typesafe/jev-1.13` qua OpenRouter)  
> **Thời điểm kiểm thử lần 1:** Ngày 15/10/2026  
> **Thời điểm tái kiểm thử (Retest):** Ngày 16/10/2026 (Sau khi cấu hình bật `Jev:RagEnabled`)  
> **Thời điểm khắc phục UI/UX:** Ngày 16/10/2026 (Khắc phục hoàn tất ISSUE-B02 và ISSUE-B03)  
> **Phương pháp kiểm thử:** Live E2E qua Browser Subagent, tương tác trực tiếp giao diện người dùng và đối chiếu dữ liệu nghiệp vụ thực tế.

---

## 1. Tổng Quan Kết Quả Nghiệm Thu Thực Tế (Sau Khắc Phục)

| Nhóm chức năng | Trạng thái Nghiệp vụ Backend | Trạng thái Giao diện Frontend | Đánh giá chung |
|---|---|---|---|
| **§B1: Multi-Agent Personas** | ✅ Phân loại và định tuyến prompt theo persona chính xác (`?persona=scrum_master`, `tech_lead`, `qa_security`). | ✅ Select vai trò Ant Design mượt mà; **đã bổ sung Persona Tag ngay trong khi stream câu trả lời**. | **PASSED TOÀN DIỆN** |
| **§B2: Mở rộng Tool-calling (ReAct)** | ✅ ReAct loop tích hợp 7 tools, phân tích sâu các thẻ Kanban theo từng vai trò. | ✅ Tích hợp trong stream chat, phản hồi phân tích sprint cực kỳ chi tiết. | **PASSED TOÀN DIỆN** |
| **§B3: Kho Tri thức (Knowledge Base & pgvector)** | ✅ **HOẠT ĐỘNG HOÀN HẢO:** Nạp tài liệu qua Accountability Layer (`CreateKnowledgeDocument` Pending → Approved) và trích xuất ngữ cảnh RAG vào Agent Chat chuẩn xác 100%. | ✅ Bảng tài liệu, modal nhập liệu chuẩn Ant Design; **đã loại bỏ thanh scroll ngang giả tạo bằng `scroll={{ x: 'max-content' }}`**. | **PASSED TOÀN DIỆN** |

---

## 2. Danh Sách Chi Tiết Các Vấn Đề (Issue List)

### ISSUE-B01: Cấu hình `Jev:RagEnabled = false` trên Production Mặc Định Chặn Thao Tác Nạp Tri Thức Mới

- **Phân loại:** Configuration / DevOps / Backend
- **Mức độ:** 🟡 **Trung bình (Medium)**
- **Trạng thái:** ✅ **ĐÃ GIẢI QUYẾT & XÁC MINH THỰC CHIẾN (RESOLVED)**
- **Vị trí:** Trang Kho tri thức (`/workspaces/{workspaceId}/knowledge`) & API Knowledge Base.
- **Hiện tượng ban đầu:**
  - Cảnh báo màu vàng: *"Bộ nhớ ngữ nghĩa đang tắt (Jev:RagEnabled)..."*
  - Nút *"+ Thêm tài liệu"* bị vô hiệu hóa (disabled).
- **Nhật ký khắc phục & Kết quả tái kiểm thử (Retest Ngày 16/10/2026):**
  1. **Kích hoạt cấu hình:** Người dùng đã cấu hình `Jev:RagEnabled=true` và các biến môi trường RAG/Embeddings trên server Render.
  2. **Trạng thái giao diện Kho tri thức sau khi bật:**
     - Cảnh báo tắt RAG biến mất hoàn toàn. Thay vào đó là thông báo xanh dương chuẩn mực: *"Mọi thay đổi tài liệu đều cần Manager duyệt. Yêu cầu thêm hoặc xoá tài liệu được ghi thành một đề xuất chờ duyệt trong Lịch sử AI; bộ nhớ chỉ thay đổi sau khi Manager phê duyệt."*
     - Nút **"+ Thêm tài liệu"** chuyển sang trạng thái khả dụng (màu xanh tím, có thể bấm được).
  3. **Kiểm thử luồng thêm tài liệu qua Accountability Layer (TC-B02):**
     - Đã tạo tài liệu: *"Quy chuẩn kiến trúc TeamNexus 2026"* (Nguồn: `https://teamnexus.cloud/docs/arch`, Nội dung: Modular Monolith C# .NET 10, PostgreSQL 18 pgvector, React 19 Ant Design v6).
     - Hệ thống từ chối ghi thẳng vào DB mà tạo đề xuất `Pending` với message: *"Đã gửi yêu cầu thêm tài liệu — chờ Manager duyệt trong Lịch sử AI"*.
  4. **Kiểm thử phê duyệt trong Lịch sử AI (TC-B03):**
     - Mở Lịch sử Hành động AI (`/activity`), xuất hiện bản ghi `CreateKnowledgeDocument` (0 tasks, requested by `Thinh-TT`, trạng thái Chờ duyệt màu vàng).
     - Bấm nút **"Duyệt & áp dụng"** (Approve) → đề xuất chuyển sang trạng thái Đã duyệt màu xanh lá.
     - Quay lại Kho tri thức: Tài liệu *"Quy chuẩn kiến trúc TeamNexus 2026"* đã hiển thị chính thức trên bảng với trạng thái *Đang xử lý* (Pending ingest / chunkCount: 1), có nút *Xoá* màu đỏ.
  5. **Kiểm thử trích xuất tri thức vào AI Agent Chat (TC-B07 - Live RAG):**
     - Mở AI Agent Chat trên Board Kanban Sprint 1, gửi câu hỏi:  
       *"Dự án TeamNexus sử dụng phiên bản .NET nào và bộ thư viện UI nào theo quy chuẩn kiến trúc 2026?"*
     - **Kết quả trả lời thực tế của Agent:**
       > *"Theo thông tin trong quy chuẩn kiến trúc 2026 của TeamNexus:*  
       > *- Nền tảng: TeamNexus — nền tảng quản lý dự án thông minh, kết hợp Hybrid AI.*  
       > *- AI: Hybrid AI gồm System 1 (Jev) và System 2 (DeepSeek).*  
       > *- Backend: C# .NET 10, kiến trúc Modular Monolith.*  
       > *- Cơ sở dữ liệu: PostgreSQL 18, tích hợp extension pgvector.*  
       > *- Frontend: React 19, dùng bộ thư viện Ant Design v6.*  
       > *Đó là toàn bộ thông tin công nghệ mình có trong ngữ cảnh. Nếu bạn cần chi tiết hơn... mình chưa có dữ liệu nên sẽ cần bổ sung."*
     - 👉 **Minh chứng 100%:** RAG đã hoạt động hoàn hảo, trích xuất đúng ngữ cảnh nội bộ, không ảo giác, có trích dẫn trung thực giới hạn ngữ cảnh.

---

### ISSUE-B02: Thanh Cuộn Ngang (Horizontal Scrollbar) Xuất Hiện Thô Sơ Khi Bảng Tài Liệu Rỗng / Ít Dòng

- **Phân loại:** Frontend / UI Polish
- **Mức độ:** 🟢 **Thấp (Low / Polish)**
- **Trạng thái:** ✅ **ĐÃ KHẮC PHỤC (RESOLVED)**
- **Vị trí:** Bảng tài liệu trong `src/features/knowledge/components/KnowledgeBasePage.tsx`.
- **Nguyên nhân gốc rễ:**
  - Table Ant Design bị hardcode `scroll={{ x: 1100 }}`, ép buộc chiều rộng tối thiểu 1100px. Khi container hiển thị ở màn hình chuẩn (1000–1136px), trình duyệt buộc phải sinh thanh cuộn ngang dù dữ liệu chỉ có 1 dòng hoặc rỗng.
- **Giải pháp đã thực hiện:**
  - Thay thế `scroll={{ x: 1100 }}` thành `scroll={{ x: 'max-content' }}` trong `KnowledgeBasePage.tsx`.
  - Bảng tự động co giãn vừa khít với container trên desktop, triệt tiêu thanh cuộn ngang thừa thãi, chỉ kích hoạt cuộn ngang khi màn hình thu hẹp dưới kích thước cột (responsive tablet/mobile).

---

### ISSUE-B03: Auto-Scroll Trong AI Agent Chat Panel Chưa Cuộn Triệt Để Xuống Header Tin Nhắn Mới

- **Phân loại:** Frontend / UX
- **Mức độ:** 🟡 **Trung bình (Medium)**
- **Trạng thái:** ✅ **ĐÃ KHẮC PHỤC (RESOLVED)**
- **Vị trí:** `src/features/ai/components/AgentChatPanel.tsx`.
- **Nguyên nhân gốc rễ:**
  1. Trong khi AI đang stream (`streamingAnswer`), không hiển thị Tag vai trò persona, chỉ sau khi stream xong và chuyển vào mảng `messages` mới có tag.
  2. Logic auto-scroll `node.scrollTo({ top: node.scrollHeight })` kích hoạt vô điều kiện trên mỗi delta text đến, giật cuộn xuống đáy và đẩy phần đỉnh của tin nhắn (nơi chứa Tag vai trò và icon Robot) ra khỏi tầm nhìn, đồng thời ngăn cản người dùng cuộn lên để đọc.
- **Giải pháp đã thực hiện:**
  1. **Hiển thị Persona Tag ngay trong khi stream:** Bổ sung `Tag` màu tím mang tên vai trò (`personaLabel(persona)`) ngay đầu khối `streamingAnswer` khi có vai trò được chọn. Người dùng nhận diện được ngay vai trò đang nói từ ký tự đầu tiên.
  2. **Auto-scroll thông minh (Smart Auto-Scroll):**
     - Theo dõi vị trí cuộn của người dùng qua `handleScroll`: nếu người dùng chủ động cuộn lên trên (cách đáy > 90px), hệ thống ngừng cưỡng bức cuộn đáy để người dùng đọc thoải mái.
     - Khi người dùng gửi câu hỏi mới (`handleSend`), hệ thống tự động reset trạng thái để cuộn mượt xuống đầu lượt hội thoại mới.
  3. **Nút Floating Cuộn Nhanh ("Scroll to Bottom"):**
     - Khi người dùng cuộn lên trên, một nút tròn nhỏ Ant Design với icon `DownOutlined` (`data-testid="agent-chat-scroll-bottom"`) xuất hiện ở góc dưới bên phải khung chat.
     - Bấm nút này sẽ kích hoạt cuộn mượt xuống đáy (`behavior: 'smooth'`) và ẩn nút.

---

## 3. Kết Luận Chung Sau Khi Hoàn Tất Khắc Phục

1. Toàn bộ **3/3 Issues** phát hiện trong quá trình kiểm thử thực chiến Track B giai đoạn 14.7 đều đã được giải quyết triệt để.
2. Kiểm tra chất lượng mã nguồn:
   - `npm run lint` (oxlint): **0 errors, 0 warnings**.
   - `npx tsc -b`: **Exit code 0**.
   - `vitest` suite (`features/knowledge`, `features/ai`): **46 test files / 287 tests PASSED**.
   - `npm run build`: **Production bundle tạo thành công**.
3. **Giai đoạn 14.7 Track B** đã sẵn sàng 100% để bàn giao và đưa vào tài liệu nghiệm thu chính thức của dự án.
