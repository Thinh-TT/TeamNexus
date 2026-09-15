# TeamNexus – Trợ lý Điều phối Không gian Làm việc Thông minh

## Ý tưởng dự án
TeamNexus là một nền tảng quản lý công việc và giao tiếp nhóm, kết hợp giữa mô hình không gian làm việc truyền thống (như Kanban board) và trí tuệ nhân tạo. Thay vì chỉ là công cụ ghi chép thụ động, hệ thống tích hợp một AI Agent đóng vai trò như một "thành viên ảo". AI này có khả năng tự động hóa khâu thiết lập dự án, theo dõi luồng công việc theo thời gian thực và phân tích hành vi để đề xuất giải pháp, giúp tối ưu hóa hiệu suất và giảm thiểu ma sát trong quá trình cộng tác.

## Tầm nhìn dự án
* **Về mặt Sản phẩm:** Trở thành một nền tảng SaaS tinh gọn, giải quyết triệt để vấn đề "bất đồng ngầm" và thiếu minh bạch trong các nhóm làm việc vừa và nhỏ.
* **Về mặt Kỹ thuật:** Đóng vai trò là một minh chứng thực tế (portfolio) toàn diện về năng lực phát triển phần mềm đa nền tảng. Dự án phô diễn khả năng làm chủ kiến trúc hệ thống, tích hợp mô hình AI (phân luồng các model tối ưu chi phí và hiệu suất), và xử lý luồng dữ liệu thời gian thực phức tạp.

## Các tính năng cốt lõi

### 1. Bảo mật & Phân quyền (Auth & RBAC)
Xác thực người dùng qua OAuth 2.0 (Google/GitHub), quản lý phiên đăng nhập an toàn với JWT/HttpOnly Cookie và phân quyền chặt chẽ (Admin, Manager, Member).

### 2. Bảng điều khiển Kanban Real-time
Giao diện quản lý tác vụ kéo thả trực quan. Trạng thái công việc được đồng bộ hóa lập tức đến toàn bộ các client đang kết nối mà không cần tải lại trang.

### 3. AI Smart Setup (Khởi tạo tự động)
Trưởng nhóm cung cấp mô tả tổng quan, AI sẽ tự động phân rã yêu cầu thành các sub-tasks chi tiết, gán nhãn và đưa ra đề xuất nhân sự phụ trách phù hợp.

### 4. AI Observer (Quan sát & Cảnh báo ngầm)
Thu thập log hệ thống và bối cảnh giao tiếp để nhận diện sớm các điểm nghẽn (bottlenecks), nhân sự quá tải hoặc xung đột tiềm ẩn, từ đó gửi báo cáo riêng cho quản lý.

### 5. Hệ thống Giải trình AI (Accountability Layer)
Đảm bảo tính minh bạch tuyệt đối. Mọi hành động của AI đều lưu lại vết dữ liệu (dựa trên cơ sở nào, vào lúc nào) và cấp quyền cho con người dừng, chỉnh sửa hoặc hoàn tác bất cứ lúc nào.

### 6. Xuất Báo cáo Đa định dạng
Tự động tổng hợp dữ liệu tiến độ và hiệu suất thành các tệp PDF hoặc Excel trực quan.

### 7. AI Agent Executor (Thành viên ảo thực thi công việc) — Giai đoạn 7 ✅
Nâng AI từ vai trò "đề xuất/quan sát" lên vai trò "thực thi". AI Agent được gán task trực tiếp như một thành viên thật (qua đúng thao tác assign/kéo-thả sẵn có) và tự thực hiện các task đặc thù được trưởng nhóm giao.
 
- **Phạm vi công việc:** kết hợp soạn thảo tài liệu/báo cáo nội bộ, tóm tắt & tổng hợp thông tin từ task/comment trong hệ thống, và nghiên cứu qua web khi cần.
- **Nguồn dữ liệu:** vừa truy cập dữ liệu nội bộ (task, comment, file đính kèm) vừa có khả năng tìm kiếm thông tin ngoài qua web search.
- **Khi thiếu thông tin:** Agent tạm dừng, chuyển task sang trạng thái "Chờ làm rõ" và hỏi lại trưởng nhóm thay vì tự suy đoán; sau khi trưởng nhóm trả lời (comment), cần thao tác thủ công "Chạy lại" để Agent tiếp tục.
- **Kết quả đầu ra:** nội dung ngắn thể hiện dưới dạng bình luận trên task, nội dung dài thể hiện dưới dạng tệp đính kèm — cả hai đều phải qua Accountability Layer (mục 5) để trưởng nhóm duyệt trước khi coi là hoàn thành.
- **Giới hạn an toàn:** mỗi lượt thực thi bị giới hạn số bước gọi công cụ, thời gian chạy và ngân sách token để tránh vòng lặp vô hạn và phát sinh chi phí ngoài kiểm soát.

### 8. Đơn giản hóa Kiến trúc Role — Giai đoạn 9
Tách biệt rõ ràng hai lớp phân quyền: **Identity Role toàn cục** (`Admin`/`User`) dành cho quản trị nền tảng; **Workspace Role** (`Admin`/`Manager`/`Member`) dành cho mọi nghiệp vụ trong workspace. Mọi user đăng nhập qua OAuth tự động nhận Identity role `User`; role `Admin` chỉ assign thủ công cho system administrator của nền tảng.

### 9. Nâng cao Task & Workspace UX — Giai đoạn 10
Hoàn thiện UI cho các field task đã có schema: **Due Date** (hiển thị badge đỏ khi quá hạn, kết nối AI Observer), **Priority** (badge màu Low/Medium/High/Urgent), **Description** (markdown cơ bản). Thêm **Workspace Settings** cho Admin (sửa tên/mô tả, chuyển ownership, xóa workspace) và **Activity Log UI** cho Manager/Admin (lịch sử ai làm gì trong workspace).

### 10. Quản lý Member & Profile — Giai đoạn 11 ✅
- **Mời member qua email:** Manager/Admin nhập email → hệ thống lưu token băm SHA-256 an toàn trong DB và gửi link mời qua Postmark / MailKit → người nhận click link, tham gia workspace đúng vai trò chỉ định.
- **Quy trình chấp nhận lời mời:** Truy cập `/invitations/accept?token=...`; nếu chưa đăng nhập, hệ thống lưu tạm token tại `sessionStorage` (`pending_invite_token`) và điều hướng tới `/login`, sau khi đăng nhập/đăng ký thành công sẽ tự động quay lại chấp nhận. Ghi nhận thành viên mới và lưu vết vào Activity Log.
- **Gửi email nhanh:** Manager/Admin soạn tiêu đề + nội dung ngắn trong app, chọn danh sách người nhận → gửi tới inbox thật của thành viên kèm theo dõi lịch sử gửi trong `email_messages`.
- **Quản lý member:** Xem danh sách thành viên (phân biệt Người thật / AI Agent), huỷ lời mời đang chờ (Manager/Admin), đổi vai trò thành viên (chỉ Admin/Owner), khai trừ thành viên (chỉ Admin/Owner).
- **Profile cá nhân:** Xem và cập nhật tên hiển thị, URL avatar (xác thực an toàn scheme `http://` / `https://`); xem danh sách workspace đang tham gia kèm vai trò; tự rời workspace (chặn chủ sở hữu tự rời nếu chưa chuyển giao quyền).
- **Trung tâm thông báo (Notification Center):** Tích hợp chuông báo badge số lượng chưa đọc trên `AppHeader` dùng chung, Drawer thông báo hỗ trợ lọc Chưa đọc / Tất cả cho cả cảnh báo AI Observer và thông báo nghiệp vụ (được assign task, comment mới, được mời vào workspace).

### 11. Dashboard & Tìm kiếm — Giai đoạn 12 (✅ backend xong; frontend bàn giao)
**Dashboard tổng quan workspace:** hiển thị "Task của tôi" (sắp đến hạn, quá hạn, mới giao), hoạt động gần đây, tóm tắt board, cảnh báo AI Observer chưa đọc. **Tìm kiếm & Lọc task:** theo tên, assignee, label, priority, trạng thái, due date trong phạm vi workspace/board. **@mention trong comment:** tag thành viên với `@tên`, kích hoạt thông báo cho người được tag.

- **Trang chủ workspace:** route riêng `/workspaces/:workspaceId/dashboard` (`/` vẫn là danh sách workspace như hiện tại). Số liệu lấy từ `GET /api/workspaces/{id}/dashboard` — quyền **Member+** (404 người ngoài workspace), khác `/activity` (Manager+).
- **Ngữ nghĩa 3 nhóm "Task của tôi" (chốt, một nguồn duy nhất phía server):** `isDone` = *ở cột `is_done`* **hoặc** có `completed_at` (**khớp** `ReportAggregator` của Giai đoạn 6 — nếu lệch thì Dashboard và Báo cáo nói hai số khác nhau). **Quá hạn** = `!isDone && due_date < now` (hạn đúng lúc `now` ⇒ **chưa** quá hạn). **Sắp đến hạn** = `!isDone && now ≤ due_date ≤ now + N ngày` (mặc định **3**, chỉnh được `[1,30]`). **Mới giao** = `!isDone && created_at ≥ now − 7 ngày`. Ba nhóm **độc lập** với nhau (một task có thể nằm ở nhiều nhóm).
- **Tìm kiếm task xuyên board:** endpoint **mới** `GET /api/workspaces/{id}/tasks/search` (tên/mô tả · board · assignee hoặc "chưa gán" · nhiều nhãn theo **AND** · priority · khoảng due date · chỉ task quá hạn · ẩn task đã xong), phân trang **keyset** trên `(updated_at DESC, id DESC)`. `GET /api/boards/{boardId}/tasks` **giữ nguyên** shape mảng cũ.
- **@mention — quyết định chống mạo danh:** client gửi **`mentionUserIds` tường minh** trong body bình luận; **server KHÔNG parse `@tên`** từ text. Server chỉ nhận người **là thành viên `human` của workspace** (AI Agent / người ngoài ⇒ **400** trước khi ghi), loại trùng, loại tác giả, và **không** gửi thêm row cho assignee (người đã nhận `CommentOnTask`). Văn bản bình luận vẫn là text thuần (`@Tên Hiển Thị`) — hiển thị **không** dùng HTML thô.
- **Không schema mới:** loại thông báo `CommentMention` nằm trong `notifications.type` (**text tự do**) + `notifications.payload` (jsonb) ⇒ Giai đoạn 12 **không** thêm migration (vẫn **9**).