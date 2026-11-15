# TeamNexus – Đặc tả Hệ thống & Nghiệp vụ (System Specification)

## Ý tưởng dự án
**TeamNexus** là nền tảng quản lý công việc và điều phối không gian làm việc thông minh, kết hợp giữa mô hình quản lý dự án trực quan (Kanban board thời gian thực) và kiến trúc Trí tuệ Nhân tạo lai (**Hybrid AI: System 1 + System 2**). Thay vì chỉ đóng vai trò công cụ ghi chép thụ động, hệ thống tích hợp đội ngũ AI Agent như các "thành viên ảo" có định danh chính thức. Các tác nhân này có khả năng phân loại công việc tức thời, tự động hóa khâu thiết lập bảng dự án, giám sát luồng công việc để dự báo rủi ro sớm, trực tiếp thực thi tác vụ qua vòng lặp công cụ (ReAct tool-calling) và kết nối khép kín với hệ sinh thái kỹ thuật bên ngoài (Slack, Discord, GitHub) — tất cả đều đặt dưới sự kiểm soát minh bạch của con người thông qua **Lớp Giải trình AI (Accountability Layer)**.

## Tầm nhìn dự án
* **Về mặt Sản phẩm:** Trở thành một nền tảng điều phối công việc tinh gọn, giải quyết triệt để vấn đề "bất đồng ngầm", phân mảnh thông tin và thiếu minh bạch trong các nhóm phát triển phần mềm và làm việc tri thức.
* **Về mặt Kỹ thuật & Học thuật:** Minh chứng toàn diện cho kiến trúc phần mềm hiện đại (Modular Monolith đa nền tảng Web & Mobile), ứng dụng mô hình nhận thức kép (System 1 Jev ra quyết định có cấu trúc siêu tốc kết hợp System 2 DeepSeek tư duy ngôn ngữ sâu), tìm kiếm ngữ nghĩa Vector RAG (`pgvector`), đồng bộ hóa thời gian thực qua WebSocket/SignalR và cơ chế kiểm duyệt Human-in-the-Loop có khả năng hoàn tác 100%.

---

## Các phân hệ và tính năng cốt lõi

### 1. Bảo mật, Xác thực & Phân quyền Hai lớp (Auth & Two-Layer RBAC)
* **Xác thực đa phương thức:** Đăng nhập qua OAuth 2.0 (Google, GitHub), quản lý phiên làm việc an toàn bằng JWT kết hợp Refresh Token lưu trong `HttpOnly Cookie` (cơ chế xoay vòng token chống phát lại) và bảo vệ chống giả mạo yêu cầu (`CSRF` qua header `X-XSRF-TOKEN`).
* **Kiến trúc phân quyền 2 lớp tách biệt:**
  * **Identity Role toàn cục (`Admin` / `User`):** Dùng cho quản trị cấp nền tảng (`SystemAdminPolicy`). Mọi người dùng đăng nhập qua OAuth mặc định nhận vai trò `User`.
  * **Workspace Role (`Admin` / `Manager` / `Member`):** Lưu trữ trên từng thành viên của không gian làm việc (`workspace_members.role`), quyết định toàn bộ quyền hạn nghiệp vụ trong phạm vi workspace đó. Người dùng ngoài workspace nhận phản hồi `404 Not Found` (bảo mật sự tồn tại của không gian làm việc); thành viên không đủ quyền nhận `403 Forbidden`.
* **Tài khoản hệ thống (`is_system = true`) & Thành viên ảo (`ai_agent`):** Bị khóa hoàn toàn khỏi các luồng đăng nhập tương tác, OAuth và làm mới token.

### 2. Bảng điều khiển Kanban Thời gian thực (Real-time Kanban Core)
* **Quản lý trực quan:** Thao tác kéo-thả mượt mà cho thẻ công việc (Task) và cột trạng thái (Column) trong từng bảng (Board).
* **Đồng bộ hóa tức thời:** Mọi thay đổi về thẻ, cột và bình luận được phát sóng trực tiếp theo nhóm bảng (`board-{boardId}`) qua **ASP.NET Core SignalR**, giúp các thành viên đang mở bảng thấy cập nhật ngay lập tức mà không cần tải lại trang, kèm cơ chế tự động kết nối lại khi mạng gián đoạn.
* **Ngữ nghĩa cột hệ thống:** Hỗ trợ 3 loại cột trạng thái đặc biệt loại trừ lẫn nhau trên mỗi bảng: cột **Hoàn thành (`is_done`)** tự động ghi nhận thời điểm `completed_at`; cột **Chờ làm rõ (`is_clarification`)** dành cho AI Agent khi cần hỏi lại; và cột **Đang duyệt PR (`is_in_review`)** phục vụ tự động hóa với GitHub Pull Request.

### 3. AI Smart Setup & AI Board Template (Khởi tạo tự động)
* **Khởi tạo công việc theo bảng (AI Smart Setup):** Quản lý nhập mô tả yêu cầu tổng quan → AI tự động phân rã thành danh sách các thẻ công việc chi tiết kèm mức độ ưu tiên, nhãn phân loại và đề xuất nhân sự phụ trách dựa trên danh sách thành viên hiện có.
* **Khởi tạo bảng dự án từ mẫu AI (AI Board Template):** Từ mô tả mục tiêu dự án ở cấp Workspace → AI đề xuất trọn gói một bảng mới gồm tên bảng, mô tả, cấu trúc `2–6` cột (luôn đảm bảo đúng 1 cột hoàn thành `is_done`) và `5–10` thẻ công việc khởi đầu.
* **Kiểm duyệt trước khi áp dụng:** Cả hai luồng khởi tạo đều chỉ trả về bản xem trước (Proposal) để người dùng chỉnh sửa; dữ liệu chỉ được ghi vào hệ thống sau khi được xác nhận và phê duyệt qua Lớp Giải trình AI.

### 4. Hệ thống Giải trình AI (Accountability Layer)
* **Cổng ghi dữ liệu duy nhất:** Mọi hành động thêm/sửa/xóa dữ liệu nghiệp vụ do AI khởi tạo đều bắt buộc đi qua dịch vụ trung tâm (`AiActionService`) và lưu vết tại bảng `ai_action_logs`.
* **Vòng đời phê duyệt minh bạch (`Pending → Approved / Rejected → Undone`):**
  * Đề xuất của AI được ghi nhận ở trạng thái chờ duyệt (`Pending`) kèm cơ sở lý luận (`basis`) và ảnh chụp dữ liệu đề xuất (`after_snapshot`), tuyệt đối chưa tác động vào dữ liệu thật.
  * Chỉ khi Quản lý (`Manager` / `Admin`) bấm **Phê duyệt (`Approve`)**, thay đổi mới được thực thi nguyên tử và lưu lại kết quả thực tế (`applied_snapshot`) cùng trạng thái trước khi đổi (`before_snapshot`).
  * Quản lý có thể **Từ chối (`Reject`)** kèm ghi chú lý do, hoặc **Hoàn tác (`Undo`)** bất kỳ hành động nào đã duyệt để khôi phục dữ liệu về nguyên trạng.
* **Lịch sử AI đa cấp:** Cho phép tra cứu, lọc và phê duyệt hành động AI ở cả cấp Bảng (Board) lẫn cấp Không gian làm việc (Workspace).

### 5. AI Observer, Dự báo Rủi ro & Điểm Sức khỏe Dự án
* **Giám sát ngầm định kỳ:** Dịch vụ nền quét nhật ký hoạt động và trạng thái thẻ để phát hiện sớm 5 nhóm tín hiệu bất thường:
  1. `OverdueTask`: Thẻ công việc đã quá hạn hoàn thành.
  2. `AtRiskDeadline`: **Dự báo sớm nguy cơ trễ hạn** đối với các thẻ chưa hoàn thành có thờiלượng cửa sổ tối thiểu 2 ngày, chỉ còn dưới `20%` quỹ thời gian nhưng không có bất kỳ cập nhật hay bình luận nào trong `48 giờ` qua (phân mức `Critical ≤ 24h`, `High ≤ 72h`, `Medium`).
  3. `StalledTask`: Thẻ đang làm bị đình trệ lâu ngày không có tương tác.
  4. `Overload`: Nhân sự bị dồn ứ quá nhiều thẻ đang mở hoặc thẻ quá hạn.
  5. `Bottleneck`: Cột trạng thái bị tắc nghẽn nhiều thẻ đứng yên.
* **Cảnh báo riêng tư cho cấp quản lý:** Các phát hiện sau khi thẩm định được gửi thông báo riêng cho `Manager` và `Admin` của workspace, kèm cơ chế khử trùng lặp trong cửa sổ 24 giờ.
* **Chỉ số Sức khỏe Dự án (`Project Health Score`):** Tính toán khách quan, tất định trên thang điểm `0–100` (chia thành 4 ngưỡng: `Tốt ≥ 80`, `Cần chú ý 60–79`, `Rủi ro 40–59`, `Nghiêm trọng < 40`) dựa trên 5 trọng số cộng lại đúng 100: tỷ lệ quá hạn (`40`), tỷ lệ có nguy cơ trễ hạn (`20`), tỷ lệ đình trệ (`10`), tuổi thọ thẻ mở lâu nhất (`15`) và độ lệch tải nhân sự (`15`).

### 6. Báo cáo Tiến độ, Biểu đồ Vận tốc & Xuất dữ liệu Đa định dạng
* **Tổng hợp thống kê trực quan:** Cung cấp báo cáo tiến độ hiện tại, hiệu suất hoàn thành theo người phụ trách/theo bảng, thống kê hoạt động và tóm tắt sức khỏe từ AI Observer trong khoảng thời gian tùy chọn.
* **Chuỗi thời gian Tiến độ & Vận tốc (Burndown & Velocity):** Phục vụ biểu đồ số lượng thẻ mở (`openTasks`), hoàn thành (`completions`) và tạo mới (`creations`) theo từng ngày (khi khoảng xem ≤ 60 ngày) hoặc theo tuần (mốc Thứ Hai, khi khoảng xem > 60 ngày), tự động quy đổi chính xác theo múi giờ địa phương của người xem (`tzOffsetMinutes`).
* **Xuất tệp PDF & Excel theo yêu cầu (On-demand Export):** Kết xuất báo cáo ra tệp **PDF** (QuestPDF) hoặc bảng tính **Excel** 5 trang (ClosedXML) trực tiếp trên bộ nhớ đệm mà không lưu trữ tệp tĩnh lâu dài trên máy chủ.

### 7. AI Agent Executor (Thành viên ảo thực thi công việc)
* **Giao việc trực tiếp trên Kanban:** Thành viên ảo (`member_type = 'ai_agent'`) xuất hiện trong danh sách thành viên workspace và nhận thẻ công việc qua đúng thao tác gán người phụ trách thông thường.
* **Vòng lặp suy luận & hành động (ReAct Tool-Calling Loop):** Khi Quản lý kích hoạt chạy Agent, hệ thống thực thi vòng lặp gọi công cụ trong danh sách cho phép (whitelist) để tra cứu dữ liệu nội bộ, tìm kiếm thông tin trên web (qua Tavily API), kiểm tra tải công việc, đối chiếu kỹ năng và kiểm định tiêu chí nghiệm thu.
* **Cơ chế tạm dừng hỏi lại (`Awaiting Clarification`):** Khi mô tả công việc thiếu thông tin quan trọng, Agent tự động đăng câu hỏi bình luận trên thẻ, chuyển thẻ sang cột *"Chờ làm rõ"* và tạm dừng chờ con người phản hồi trước khi được bấm *"Chạy lại"*.
* **Đầu ra có kiểm duyệt:** Kết quả ngắn được đề xuất dưới dạng bình luận (`PostComment`), kết quả dài (báo cáo, tài liệu kỹ thuật ≤ 512 KB) được đề xuất dưới dạng tệp đính kèm (`PostAttachment`) — tất cả đều tạo bản ghi `Pending` trong Lớp Giải trình để Quản lý duyệt.
* **Giới hạn an toàn cứng (Guardrails):** Mỗi lượt chạy bị giới hạn nghiêm ngặt về số lần gọi công cụ (`≤ 15` tool calls), số lượt gọi mô hình (`≤ 20` LLM calls), thời gian chạy (`≤ 5 phút`) và ngân sách token (`≤ 50.000` tokens).

### 8. Quản lý Không gian làm việc, Thành viên & Hồ sơ Cá nhân
* **Khởi tạo & Quản trị Workspace:** Người dùng đã đăng nhập có thể chủ động tạo không gian làm việc mới (trở thành `Owner` kiêm `Admin`). Quản trị viên có thể cập nhật tên/mô tả, chuyển giao quyền sở hữu (`Transfer Ownership` cho thành viên con người) hoặc xóa mềm workspace.
* **Nhật ký Hoạt động (Activity Log):** Ghi vết toàn bộ sự kiện nghiệp vụ cấp workspace và cấp bảng, hiển thị dòng thời gian phân trang keyset cho cấp Quản lý.
* **Mời thành viên & Gửi email nhanh:**
  * Mời thành viên qua email với liên kết chứa token ngẫu nhiên (chỉ lưu mã băm `SHA-256` trong CSDL, thời hạn 7 ngày); hỗ trợ bảo toàn luồng chấp nhận lời mời qua bước đăng nhập/đăng ký OAuth.
  * Quản lý có thể gửi email thông báo nhanh trực tiếp tới hộp thư của các thành viên trong workspace qua dịch vụ email giao dịch (`Resend`), lưu lịch sử tại bảng `email_messages`.
* **Hồ sơ cá nhân & Trung tâm thông báo (Notification Center):** Người dùng quản lý tên hiển thị, ảnh đại diện, danh sách workspace đang tham gia, quyền tự rời workspace (trừ chủ sở hữu chưa chuyển quyền), và theo dõi toàn bộ thông báo nghiệp vụ lẫn cảnh báo AI qua chuông thông báo trên thanh điều hướng chung.

### 9. Trang Tổng quan (Dashboard), Tìm kiếm Xuyên bảng, Lịch biểu & Email Digest
* **Workspace Dashboard:** Trang tổng quan cho mọi thành viên hiển thị 3 nhóm *"Công việc của tôi"* (**Quá hạn**, **Sắp đến hạn** trong N ngày, **Mới giao** trong 7 ngày), chỉ số Sức khỏe Dự án, tóm tắt tiến độ các bảng, cảnh báo AI Observer chưa đọc và dòng hoạt động gần đây.
* **Tìm kiếm & Lọc đa tiêu chí xuyên bảng:** Tìm thẻ theo từ khóa tiêu đề/mô tả, mã công việc (`TN-123`), bảng, người phụ trách (hoặc chưa gán), kết hợp nhiều nhãn theo phép hội (`AND`), độ ưu tiên, khoảng ngày hết hạn và trạng thái hoàn thành, sử dụng phân trang keyset hiệu năng cao.
* **Nhắc tên `@mention` an toàn trong bình luận:** Cho phép gắn thẻ đồng nghiệp (`@Tên`) trong bình luận với danh sách định danh thành viên xác thực phía máy chủ (chống giả mạo định danh, chỉ gửi thông báo `CommentMention` cho thành viên con người thuộc workspace).
* **Chế độ xem Lịch (Calendar View):** Chuyển đổi trực tiếp giữa giao diện cột Kanban và Lịch tháng ngay trên bảng, gom nhóm thẻ theo ngày hạn tại múi giờ địa phương và làm nổi bật các ngày có thẻ trễ hạn.
* **Bản tin Email Tóm tắt Hàng ngày (Daily Email Digest):** Tiến trình nền tự động tổng hợp các công việc quá hạn, sắp đến hạn và mới giao từ mọi workspace của người dùng thành một email duy nhất mỗi sáng (đảm bảo tính đơn nhất `1 email/người/ngày` ở tầng CSDL); người dùng có thể chủ động bật/tắt trong trang Hồ sơ cá nhân.

### 10. Trợ lý Đàm thoại Ngữ cảnh Thời gian thực (AI Task Chat & Board Agent Chat)
* **Hai bề mặt tương tác trực tiếp qua SSE (`text/event-stream`):**
  * **AI Task Chat (Cấp thẻ):** Hộp thoại *"Hỏi AI về thẻ này"* ngay trong chi tiết thẻ công việc, tự động nạp ngữ cảnh mô tả, nhãn, hạn chót và bình luận của thẻ.
  * **AI Agent Chat (Cấp bảng):** Khung trò chuyện neo bên phải giao diện bảng Kanban, cho phép đính kèm đồng thời tối đa **8 thẻ công việc** bằng thao tác kéo-thả trực tiếp từ cột Kanban, chọn từ bộ tìm kiếm nhanh hoặc bấm từ menu thẻ. Khi không đính kèm thẻ nào, hệ thống tự động cung cấp bản tóm tắt nhẹ của toàn bảng (*Lightweight Board Digest*).
* **Lưu trữ lịch sử hội thoại bền vững:** Toàn bộ lượt hỏi–đáp của cả cấp bảng và cấp thẻ được lưu trữ tại bảng `agent_chat_messages` (tự động dọn dẹp theo chính sách lưu giữ 90 ngày) để đồng bộ xuyên suốt giữa các phiên làm việc.
* **Đề xuất hành động có cấu trúc từ hội thoại:** Trong quá trình trò chuyện cấp bảng, AI có thể trích xuất khối đề xuất hành động (`CreateSubtasks`, `UpdateTaskFields`, `MoveTaskToColumn`) hiển thị thành thẻ hành động trực quan. Khi người dùng bấm *"Gửi đề xuất"* hoặc *"Lưu thành bình luận"*, yêu cầu được đưa vào trạng thái `Pending` của Lớp Giải trình để Quản lý phê duyệt.
* **Bảo vệ định danh hai đầu (Zero GUID Leakage):** Mọi văn bản trả lời của AI đều đi qua bộ lọc làm sạch định danh kỹ thuật ở cả phía máy chủ (trước khi lưu CSDL) và phía giao diện, đảm bảo không bao giờ để lộ mã UUID/GUID thô ra nội dung hội thoại mà vẫn giữ nguyên tính toàn vẹn của các khối JSON đề xuất.

### 11. Kiến trúc Hybrid AI với Mô hình Jev (System 1) & Đội ngũ AI Chuyên gia
* **Phân tách Nhận thức System 1 & System 2:**
  * **System 1 — TypeSafe Jev 1.13 (`Decisions API`):** Đảm nhận các bài toán ra quyết định có cấu trúc tĩnh (`Choice`, `Score`, `Noul`) với độ trễ cực thấp và không sinh ảo giác cú pháp.
  * **System 2 — DeepSeek LLM:** Đảm nhận tư duy ngôn ngữ tự nhiên, giải thích lý do chuyên sâu, trò chuyện SSE và vòng lặp suy luận.
* **Cơ chế Rẽ nhánh theo Ngưỡng Tin cậy (`Confidence Routing`):**
  * **Độ tự tin cao (`≥ 0.85` — `DirectAssign`):** Đề xuất trực tiếp phương án tối ưu duy nhất để người dùng chấp thuận bằng 1 cú nhấp chuột.
  * **Độ tự tin trung bình (`0.50 .. < 0.85` — `OfferChoices`):** Hiển thị tối đa 3 phương án khả dĩ nhất kèm tỷ lệ phần trăm xác suất để con người cân nhắc.
  * **Độ tự tin thấp (`< 0.50` — `ManualRequired`):** Không tự suy đoán giá trị; nhường quyền quyết định thủ công cho con người hoặc chuyển tiếp sang System 2 phân tích sâu.
* **Hệ thống Từ điển Danh mục (`Taxonomy`) & Hồ sơ Năng lực Thành viên:**
  * Mỗi workspace quản lý bộ từ điển chuẩn gồm **Miền kỹ thuật (`domain`)** và **Loại hình công việc (`work_type`)**.
  * Hồ sơ thành viên trong workspace lưu trữ danh sách kỹ năng (`skills`), vai trò chuyên môn chính (`primary_role`), trọng số năng lực (`capacity_weight`) và giới hạn số việc tối đa đang làm (`max_wip_limit`).
* **Phân loại Tự động (`Auto-Triage`) & Điều phối Nhân sự (`Smart Assignee Dispatching`):**
  * Gợi ý tức thời `Domain`, `WorkType`, `Priority` và cờ cần rà soát bảo mật (`NeedsSecurityReview`) ngay khi người dùng nhập tiêu đề/mô tả thẻ.
  * Đánh giá độ khớp giữa yêu cầu công việc với kỹ năng và tải công việc thực tế của từng thành viên để gợi ý người nhận việc phù hợp nhất (ẩn hoàn toàn email và UUID thật khi gửi ngữ cảnh tới mô hình).
* **Nâng cấp AI Observer Chủ động & Tự phục hồi (`Proactive Auto-Healing`):**
  * Jev System 1 thẩm định mức độ nghiêm trọng của tín hiệu rủi ro trước khi quyết định có cần gọi LLM System 2 hay không, giúp tiết kiệm chi phí token cho các đợt quét nền.
  * Khi phát hiện rủi ro mức cao, hệ thống có thể tự động khởi tạo đề xuất **Kịch bản Phục hồi (`RecoveryPlan`)** ở trạng thái `Pending` gồm 4 hình thức: chuyển giao lại thẻ (`ReassignTask`), tách nhỏ thẻ tồn đọng (`SplitSubtasks`), gia hạn thời hạn (`ExtendDeadline`) hoặc yêu cầu họp khẩn cấp (`EscalateManager`).
* **Đội ngũ AI Chuyên gia (`Multi-Agent Personas`) & 7 Công cụ ReAct:**
  * Mở rộng từ một trợ lý chung thành đội ngũ 4 chuyên gia ảo theo vai trò: **Trợ lý Tổng quát (`general`)**, **AI Scrum Master (`scrum_master`)**, **AI Tech Lead (`tech_lead`)**, và **AI QA & Security (`qa_security`)**. Người dùng có thể chọn vai trò chuyên gia khi trò chuyện hoặc giao việc.
  * Mở rộng bộ công cụ ReAct của Agent Executor lên **7 công cụ**: `SearchSystemData`, `WebSearch`, `DraftOutput`, `RequestClarification`, `InspectMemberWorkload` (kiểm tra tải thành viên), `MatchSkills` (so khớp kỹ năng), và `ValidateAcceptanceCriteria` (kiểm định tiêu chí nghiệm thu bằng Jev).
* **Bộ nhớ Dự án Ngữ nghĩa (`Workspace Semantic RAG` với `pgvector`):**
  * Cho phép nạp các tài liệu quy chuẩn, kiến trúc và hướng dẫn nội bộ của dự án vào Kho tri thức workspace (thao tác thêm/xóa tài liệu bắt buộc qua bước phê duyệt `CreateKnowledgeDocument` của Lớp Giải trình).
  * Tài liệu sau khi duyệt được chia đoạn (chunking) và nhúng thành vector `1536 chiều` ở tiến trình nền, đánh chỉ mục `HNSW` khoảng cách cosine. Khi thành viên trò chuyện với AI, hệ thống tự động truy xuất các đoạn tri thức liên quan nhất đưa vào ngữ cảnh trả lời.

### 12. Tích hợp Hệ sinh thái Công cụ Bên ngoài (Slack, Discord & GitHub)
* **Mã định danh công việc tuần tự (`TN-123`):** Mỗi thẻ công việc trong workspace được cấp phát nguyên tử một số thứ tự duy nhất (`task_number`), hiển thị dưới dạng mã `TN-123` trên thẻ Kanban, cửa sổ chi tiết và bộ tìm kiếm để liên kết xuyên suốt với mã nguồn.
* **Thông báo Đa kênh Ra ngoài (Outbound Slack & Discord Webhooks):**
  * Quản lý cấu hình các điểm cuối Incoming Webhook của **Slack** (Block Kit) và **Discord** (Rich Embeds) theo từng workspace, tùy chọn đăng ký theo danh mục sự kiện (`ObserverFinding`, `OverdueTask`, `MemberJoined`, `AgentRunFailed`, v.v.).
  * Đảm bảo độ bền gửi tin bằng mẫu **Transactional Outbox** (`webhook_delivery_outbox`) kèm cơ chế thử lại theo bậc (`5s → 30s → 2m → 10m → 30m`), cầu dao tự ngắt (circuit breaker) khi lỗi liên tiếp, công cụ gửi tin kiểm thử trực tiếp và bộ lọc chống tấn công SSRF nghiêm ngặt.
* **Tự động hóa Quy trình Kỹ thuật với GitHub (Inbound GitHub Webhooks):**
  * Xác thực yêu cầu webhook từ GitHub bằng chữ ký số **`HMAC-SHA256`** trên luồng byte thô (so sánh hằng thời gian), bảo vệ bí mật webhook bằng `ASP.NET Core Data Protection` (chỉ hiển thị khóa bí mật duy nhất 1 lần khi tạo hoặc xoay vòng) và chống phát lại (anti-replay) theo `X-GitHub-Delivery`.
  * **Tự động hóa theo Pull Request (`GitHubPrMoveTask`):** Tự động nhận diện mã `TN-123` trong tiêu đề, nhánh hoặc mô tả PR để gắn liên kết `task_external_links`; tự động kéo thẻ sang cột *Đang duyệt PR (`is_in_review`)* khi PR mở/sẵn sàng duyệt, và tự động chuyển thẻ sang cột *Hoàn thành (`is_done`)* khi toàn bộ PR liên kết đã được hợp nhất (`merged`).
  * **Tự động khởi tạo thẻ từ GitHub Issue (`GitHubIssueCreateTask`) & Đồng bộ Bình luận:** Khi có Issue mới khớp bộ lọc nhãn, tài khoản hệ thống `GitHub Bot` tự động tạo thẻ công việc trên bảng mặc định, gọi Jev System 1 để phân loại và gán nhãn/độ ưu tiên tự động (fail-soft), đồng thời đồng bộ bình luận một chiều từ GitHub vào thẻ.
  * **Hoàn tác 100% các tự động hóa từ GitHub:** Mọi thao tác tự động di chuyển thẻ từ PR hoặc tạo thẻ từ Issue đều được ghi vết vào `ai_action_logs` với đầy đủ ảnh chụp trạng thái, cho phép Quản lý hoàn tác (`Undo`) về đúng cột và trạng thái trước đó bất cứ lúc nào.
