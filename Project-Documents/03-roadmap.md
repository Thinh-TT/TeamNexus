# TeamNexus – Roadmap Phát triển

> Roadmap ở mức giai đoạn lớn (chưa chia task chi tiết). Mỗi giai đoạn kèm các yêu cầu hoàn thiện để coi là "xong" trước khi chuyển sang giai đoạn kế tiếp.

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (đã hoàn thành)** → **8 (đang làm)** → **9 (làm ngay)** → 10 → 11 → 12 → 13.
>
> Số giai đoạn đã được **đánh lại**: **7 = AI Agent Executor**, **8 = Test & Deploy**, **9 = Đơn giản hóa Role**, **10 = Nâng cao Task & Workspace UX**, **11 = Quản lý Member & Profile**, **12 = Dashboard & Tìm kiếm**, **13 = Mobile**. `01-system-specification.md` và `02-tech-stack-decisions.md` đã cập nhật theo.
>
> **Lưu ý khi đọc tài liệu cũ:** các tài liệu đã đóng băng của giai đoạn 4/5/6 (`tasks/phase-4..6-*.md`, `report/phase-5-*`, `report/phase-6-*`) vẫn dùng cách đánh số cũ — Giai đoạn 9 (cũ) = Mobile, nay là **Giai đoạn 13**.

## Giai đoạn 1: Nền tảng & Auth

Khởi tạo project ASP.NET Core (Modular Monolith) + React, thiết kế schema PostgreSQL cơ bản, dựng ASP.NET Core Identity với OAuth Google/GitHub và JWT/Cookie, phân quyền Admin/Manager/Member theo workspace.

**Yêu cầu hoàn thiện:**
- [x] Project backend (Modular Monolith) và frontend (React/Vite) khởi tạo, build/run được
- [x] Schema PostgreSQL cơ bản (User, Role, Workspace/Board) đã migrate
- [x] Đăng nhập được qua Google và GitHub OAuth
- [x] JWT + refresh token qua HttpOnly Cookie hoạt động, có rotate refresh token
- [x] Phân quyền Policy-based cho 3 role (Admin/Manager/Member) áp dụng được trên ít nhất 1 endpoint mẫu

## Giai đoạn 2: Kanban Core (CRUD + Real-time)

Xây dựng CRUD cho Board/Task/Column, giao diện kéo-thả bằng @dnd-kit, tích hợp SignalR để đồng bộ trạng thái real-time giữa các client.

**Yêu cầu hoàn thiện:**
- [x] CRUD đầy đủ cho Board, Column, Task
- [x] Giao diện kéo-thả task giữa các column hoạt động mượt
- [x] SignalR đồng bộ real-time: thay đổi ở client A phản ánh ngay ở client B (không cần reload)
- [x] SignalR group theo boardId, có xử lý auto-reconnect khi mất kết nối

## Giai đoạn 3: AI Smart Setup

Tích hợp DeepSeek API qua interface IAiProvider, xây luồng: trưởng nhóm nhập mô tả → AI phân rã sub-tasks/nhãn/đề xuất người phụ trách → hiển thị để con người xác nhận.

**Yêu cầu hoàn thiện:**
- [x] Interface `IAiProvider` triển khai xong cho DeepSeek
- [x] Nhập mô tả dự án/tính năng → AI trả về danh sách sub-tasks có nhãn và đề xuất người phụ trách (JSON đúng schema, có validate)
- [x] Giao diện hiển thị kết quả AI đề xuất, cho phép chỉnh sửa trước khi xác nhận
- [x] Chưa ghi thẳng vào DB — chờ xác nhận (liên kết Accountability Layer ở giai đoạn 4)

## Giai đoạn 4: Accountability Layer

Xây bảng AiActionLog và service AiActionService làm cổng trung gian cho mọi hành động AI ghi dữ liệu (Pending/Approved/Rejected/Undone), gắn vào luồng Smart Setup ở bước trên.

**Yêu cầu hoàn thiện:**
- [x] Bảng `AiActionLog` lưu action, basis (tóm tắt input), timestamp, actor, trạng thái
- [x] Service `AiActionService` là điểm duy nhất mọi hành động AI đi qua trước khi ghi dữ liệu thật
- [x] Smart Setup (giai đoạn 3) đã chuyển sang dùng luồng Pending → Approve/Reject qua service này
- [x] Chức năng Undo hoạt động cho ít nhất 1 loại hành động AI (vd: tạo hàng loạt sub-tasks)

## Giai đoạn 5: AI Observer

Xây BackgroundService quét log định kỳ, phát hiện bottleneck/quá tải/xung đột tiềm ẩn, gửi báo cáo riêng cho Manager qua kênh phù hợp (thông báo trong app).

**Yêu cầu hoàn thiện:**
- [x] `BackgroundService` chạy nền, quét log/hoạt động board theo chu kỳ cố định
- [x] Log được tóm tắt trước khi gửi cho DeepSeek API (kiểm soát token/chi phí)
- [x] Phát hiện được ít nhất 1 loại tín hiệu (vd: task quá hạn dồn ứ ở 1 người, task đứng yên lâu ngày)
- [x] Kết quả cảnh báo chỉ hiển thị cho Manager, không public toàn team

> **Trạng thái:** Backend (§1–§5 + §7.1 verify **178 check PASS**) đã xong; kênh gửi hiện là **in-app**
> (`notifications`), email ghi chú cho tương lai. **Frontend (§6 + §7.2)** đã bàn giao antigravity với contract chốt ở
> `tasks/phase-5-ai-observer.md` (mục "🔻 BÀN GIAO").

## Giai đoạn 6: Báo cáo & Xuất dữ liệu

Xây tính năng tổng hợp tiến độ/hiệu suất, xuất PDF (QuestPDF) và Excel (ClosedXML) theo yêu cầu, generate on-demand.

**Yêu cầu hoàn thiện:**
- [x] Tổng hợp được dữ liệu tiến độ/hiệu suất board (vd: số task hoàn thành, thời gian trung bình mỗi task)
- [x] Xuất báo cáo ra PDF (QuestPDF) đúng định dạng, đọc được
- [x] Xuất báo cáo ra Excel (ClosedXML) đúng định dạng, mở được
- [x] File generate on-demand, không lưu trữ vĩnh viễn trên server

> **Trạng thái: ✅ ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ.** Backend verify 74/74 check PASS. Frontend hoàn tất tại `src/features/reporting/`. Chi tiết: `tasks/phase-6-reporting-export.md` và `report/phase-6-reporting-test-report.md`.

## Giai đoạn 7: AI Agent Executor (✅ HOÀN THÀNH — cả Backend & Frontend)

Nâng AI từ vai trò đề xuất/quan sát lên vai trò thực thi thật: gán task cho AI Agent như một thành viên, xây vòng lặp tool-calling (soạn thảo, tóm tắt, web search qua Tavily), gắn kết quả vào Accountability Layer sẵn có, xử lý trạng thái "Chờ làm rõ" khi Agent thiếu thông tin.

**Yêu cầu hoàn thiện:**
- [x] Thêm pseudo-member AI Agent vào `workspace_members`, **gán được task qua đúng UI assignee hiện có**
- [x] Vòng lặp tool-calling hoạt động qua DeepSeek function-calling với bộ tool: `SearchSystemData`, `WebSearch` (Tavily), `DraftOutput`, `RequestClarification`
- [x] Trạng thái task mới "Chờ làm rõ" hoạt động, hiển thị nổi bật trên Kanban
- [x] Kết quả AI đi qua đúng luồng Accountability Layer (Pending → Approve/Reject/Undo)
- [x] Guardrail hoạt động đúng (15 tool-call / 5 phút / ~50.000 token)
- [x] Bảng `agent_runs` ghi đầy đủ trạng thái, broadcast real-time qua SignalR

> **Trạng thái: ✅ ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ.** Backend **291/291 check PASS** + Frontend **187/187 tests PASS**. Chi tiết: `tasks/phase-7-ai-agent-executor.md` và `report/phase-7-ai-agent-executor-test-report.md`.

## Giai đoạn 8: Hoàn thiện, Test & Deploy — 🔄 ĐANG LÀM

> **Đang làm — tiếp ngay sau Giai đoạn 7.** Kế hoạch chi tiết: `tasks/phase-8-completion-test-deploy.md`.
>
> **Schema đã đóng băng ở Giai đoạn 7 (6 migration)** — giai đoạn này không thêm migration. Migration tiếp theo thuộc Giai đoạn 9.

Viết test (xUnit, Vitest), dọn UI/UX, cấu hình CI/CD bằng GitHub Actions, deploy backend/frontend/DB lên hạ tầng free-tier đã chọn.

**Yêu cầu hoàn thiện:**
- [x] Test cơ bản cho các luồng chính bằng xUnit/Vitest — backend **172 test**, frontend **206 test**
- [x] CI/CD pipeline qua GitHub Actions chạy build/test tự động — cả hai workflow **xanh** trên run thật
- [ ] Backend deploy thành công lên Render, Database trên Neon, Frontend trên Vercel
- [ ] Kiểm tra và xử lý ổn thỏa hiện tượng cold-start ảnh hưởng SignalR (phần frontend **đã xong**)
- [ ] UI/UX rà soát lại tổng thể, sẵn sàng để demo/đưa vào CV

## Giai đoạn 9: Đơn giản hóa Kiến trúc Role — ⚡ LÀM NGAY

> **Prerequisite của mọi giai đoạn sau.** Thay đổi nhỏ ở tầng Auth, không ảnh hưởng workspace role hay schema board/task.
> Kế hoạch chi tiết: `tasks/phase-9-role-simplification.md`.

Đơn giản hóa Identity Role toàn cục từ 3 (`Admin`/`Manager`/`Member`) xuống 2 (**`Admin`** / **`User`**). Workspace Role (`workspace_members.role`) **giữ nguyên** (Admin/Manager/Member) vì đây là lớp phân quyền thực sự của mọi tính năng.

**Lý do:** Identity Role cũ chỉ được dùng ở 2 endpoint mẫu — mọi kiểm tra quyền thực sự đã đọc `workspace_members.role`. Giữ 3 Identity Role gây nhầm lẫn giữa "global role" và "workspace role". Sau thay đổi: `Admin` = system administrator (platform management); `User` = mọi người dùng thường đã đăng nhập.

**Yêu cầu hoàn thiện:**
- [ ] `roles` table chỉ còn 2 row: `Admin` (system admin) và `User` (default cho mọi user thường)
- [ ] User mới đăng ký OAuth tự động nhận Identity role `User` (thay vì `Member` cũ)
- [ ] Policy definitions đơn giản: chỉ còn `SystemAdminPolicy` (Identity = Admin); mọi endpoint workspace dùng `.RequireAuthorization()` thuần + workspace role check trong service layer
- [ ] Migration `Phase9RoleSimplification` chạy sạch: rename "Member" → "User" trong `roles` + `user_roles`, xóa row "Manager"
- [ ] Toàn bộ test PASS (≥ 172 backend / ≥ 206 frontend)

## Giai đoạn 10: Nâng cao Task & Workspace UX

> **Không cần migration mới cho task fields** — `tasks.due_date`, `tasks.priority`, `tasks.description` đã có sẵn trong schema Phase 2.
> Chỉ cần xây UI và kết nối API. Riêng Workspace Settings và Activity Log cũng dùng bảng/endpoint đã có.

Hoàn thiện UI cho các field task đã có schema nhưng chưa có giao diện, thêm quản lý workspace cơ bản và hiển thị lịch sử hoạt động.

**Yêu cầu hoàn thiện:**
- [ ] Task có thể set/hiển thị **Due Date** (hạn chót); card Kanban hiển thị badge đỏ khi quá hạn — kết nối với tín hiệu `OverdueTask` của AI Observer
- [ ] Task có thể set/hiển thị **Priority** (Low/Medium/High/Urgent), badge màu trên card
- [ ] Task có **Description** với markdown cơ bản (in đậm, code inline, link) trong modal chi tiết
- [ ] **Workspace Settings** (Admin): sửa tên/mô tả workspace, chuyển ownership cho Admin khác, xóa workspace (với xác nhận)
- [ ] **Activity Log UI** (Manager/Admin): trang lịch sử hoạt động workspace — tận dụng `activity_logs` table đã có từ Phase 5

## Giai đoạn 11: Quản lý Member & Profile

> Cần schema mới: bảng `workspace_invitations`. Email gửi qua **Resend API** (free 3.000 email/tháng, không cần thẻ tín dụng).
> Kế hoạch chi tiết sẽ chia task khi đến giai đoạn này.

Xây luồng mời thành viên vào workspace qua email, quản lý danh sách member, profile cá nhân và notification center.

**Yêu cầu hoàn thiện:**
- [ ] **Mời member qua email**: Manager/Admin nhập email → hệ thống gửi link mời qua Resend → người nhận click link, chọn role (Member mặc định), tham gia workspace
- [ ] **Gửi email nhanh**: Manager soạn tiêu đề + nội dung ngắn trong app → gửi đến inbox thật của một hoặc nhiều member qua Resend (dùng cho thông báo họp, việc cần gấp...)
- [ ] **Quản lý member**: xem danh sách thành viên + role, hủy invitation đang chờ (Manager/Admin), đổi role member (chỉ Admin), kick member (chỉ Admin)
- [ ] **Profile cá nhân**: xem/sửa display_name, avatar URL; xem danh sách workspace đang tham gia + role của mình; tự rời workspace (không phải owner)
- [ ] **Notification Center**: badge unread + dropdown/drawer tại header — thông báo khi được assign task, có comment mới trên task của mình, được mời vào workspace — tận dụng `notifications` table từ Phase 5

## Giai đoạn 12: Dashboard & Tìm kiếm

> Xây trên dữ liệu từ Phase 10+11. Dashboard có giá trị cao khi workspace có nhiều member và task có due date.

Trang tổng quan workspace sau đăng nhập và khả năng tìm kiếm/lọc task nâng cao.

**Yêu cầu hoàn thiện:**
- [ ] **Dashboard tổng quan**: trang chủ workspace hiển thị "Task của tôi" (sắp đến hạn, quá hạn, mới giao), hoạt động gần đây (recent activity feed), tóm tắt board (số task theo trạng thái), cảnh báo AI Observer chưa đọc
- [ ] **Tìm kiếm & Lọc task**: tìm task theo tên, assignee, label, priority, trạng thái, due date — trong phạm vi workspace hoặc board đang xem
- [ ] **@mention trong comment**: tag thành viên bằng `@tên` (autocomplete), kích hoạt thông báo cho người được tag — mở rộng `notifications` table

## Giai đoạn 13: Mobile (Flutter)

> **Chưa bắt đầu — dự kiến sau Giai đoạn 12.** Nội dung giữ nguyên từ roadmap cũ (trước đây là Giai đoạn 9).

Sau khi bản Web ổn định, xây app Flutter dùng chung API, tích hợp signalr_netcore cho real-time và kiểm thử trên thiết bị thật.

**Yêu cầu hoàn thiện:**
- [ ] App Flutter kết nối và xác thực được với API đã có (dùng chung OAuth/JWT flow)
- [ ] Các màn hình chính (Board, Task, thông báo AI Observer) hoạt động trên Flutter
- [ ] Real-time qua `signalr_netcore` hoạt động tương đương bản Web
- [ ] Kiểm thử thành công trên thiết bị thật (không chỉ emulator)
