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
