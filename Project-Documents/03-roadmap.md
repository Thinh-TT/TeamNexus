# TeamNexus – Roadmap Phát triển

> Roadmap ở mức giai đoạn lớn (chưa chia task chi tiết). Mỗi giai đoạn kèm các yêu cầu hoàn thiện để coi là "xong" trước khi chuyển sang giai đoạn kế tiếp.

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (đang làm)** → 8 → 9.
>
> Số giai đoạn đã được **đánh lại cho khớp thứ tự thi hành**: **7 = AI Agent Executor**, **8 = Test & Deploy**, **9 = Mobile**. `01-system-specification.md` §7 và `02-tech-stack-decisions.md` §2.8 đã đổi theo.
>
> **Lưu ý khi đọc tài liệu cũ:** các tài liệu đã đóng băng của giai đoạn 4/5/6 (`tasks/phase-4..6-*.md`, `report/phase-5-*`, `report/phase-6-*`) vẫn dùng **cách đánh số cũ** khi nói "để Giai đoạn 7" ý là test xUnit — theo cách đánh số mới đó là **Giai đoạn 8**.
>
> **Vì sao làm 7 trước 8 và 9:** giai đoạn 1–6 đã hoàn tất & verify; giai đoạn 8 (test/CI/deploy) và 9 (Flutter mobile) **không chặn** giai đoạn 7, trong khi AI Agent Executor mới là
> điểm khác biệt hoá thật của sản phẩm (đúng mục tiêu portfolio/CV ở `02` mục bối cảnh) và đang là mạch phát triển AI còn "nóng". Giai đoạn 7 xong sẽ quay lại 8 (test/CI/deploy trên bản đã đóng băng
> API) rồi 9 (Flutter). Kế hoạch chi tiết từng bước: `tasks/phase-7-ai-agent-executor.md`.

## Giai đoạn 1: Nền tảng & Auth

Khởi tạo project ASP.NET Core (Modular Monolith) + React, thiết kế schema PostgreSQL cơ bản, dựng ASP.NET Core Identity với OAuth Google/GitHub và JWT/Cookie, phân quyền Admin/Manager/Member.

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
> (`notifications`), email ghi chú cho tương lai. **Frontend (§6 + §7.2)** — drawer cảnh báo + badge unread
> và màn hình lịch sử lần quét — đã bàn giao antigravity với contract chốt ở
> `tasks/phase-5-ai-observer.md` (mục "🔻 BÀN GIAO"). Các ô trên được tick theo tiêu chí roadmap đã
> verify ở backend; UI sẽ dùng lại đúng 6 endpoint đó.

## Giai đoạn 6: Báo cáo & Xuất dữ liệu

Xây tính năng tổng hợp tiến độ/hiệu suất, xuất PDF (QuestPDF) và Excel (ClosedXML) theo yêu cầu, generate on-demand.

**Yêu cầu hoàn thiện:**
- [x] Tổng hợp được dữ liệu tiến độ/hiệu suất board (vd: số task hoàn thành, thời gian trung bình mỗi task)
- [x] Xuất báo cáo ra PDF (QuestPDF) đúng định dạng, đọc được
- [x] Xuất báo cáo ra Excel (ClosedXML) đúng định dạng, mở được
- [x] File generate on-demand, không lưu trữ vĩnh viễn trên server

> **Trạng thái: ✅ ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ.** Backend verify 74/74 check PASS (§7.1) gồm aggregation thuần,
> render PDF/Excel on-demand qua byte stream, 3 REST endpoint phân quyền Manager/Admin. Frontend hoàn tất tại
> `src/features/reporting/` (types, API, utils, hooks, components, page) + tích hợp routing `/workspaces/:id/reports`
> và nút điều hướng có phân quyền tại BoardListPage/BoardView. Toàn bộ 30 test files / 155 tests PASS 100%
> (`oxlint` 0/0, `tsc -b` sạch, `npm run build` thành công). Chi tiết: `tasks/phase-6-reporting-export.md` và `report/phase-6-reporting-test-report.md`.

## Giai đoạn 7: AI Agent Executor (đang làm — bước tiếp theo)

Nâng AI từ vai trò đề xuất/quan sát lên vai trò thực thi thật: gán task cho AI Agent như một thành viên, xây vòng lặp tool-calling (soạn thảo, tóm tắt, web search qua Tavily), gắn kết quả vào Accountability Layer sẵn có, xử lý trạng thái "Chờ làm rõ" khi Agent thiếu thông tin.

**Yêu cầu hoàn thiện:**
- [ ] Thêm pseudo-member AI Agent vào `workspace_members`, gán được task qua đúng UI assignee hiện có
- [ ] Vòng lặp tool-calling hoạt động qua DeepSeek function-calling với bộ tool: `SearchSystemData`, `WebSearch` (Tavily), `DraftOutput`, `RequestClarification`
- [ ] Trạng thái task mới "Chờ làm rõ" hoạt động: Agent tạm dừng đúng lúc, hiển thị câu hỏi trên Kanban, nút "Chạy lại" hoạt động sau khi trưởng nhóm trả lời
- [ ] Kết quả AI (comment ngắn hoặc file đính kèm dài) đi qua đúng luồng Accountability Layer (Pending → Approve/Reject/Undo)
- [ ] Guardrail hoạt động đúng: dừng khi vượt 15 tool-call / 5 phút / ~50.000 token, có log & thông báo khi vượt ngưỡng
- [ ] Bảng `agent_runs` ghi đầy đủ trạng thái/tool trace, broadcast real-time qua SignalR để thấy tiến trình Agent trên Kanban

> **Trạng thái: 🔄 ĐANG LÀM — giai đoạn tiếp theo.** Chưa có mục nào được tick. Kế hoạch chi tiết + bảng quyết định kiến trúc (D1–D…) ở
> `tasks/phase-7-ai-agent-executor.md`; phần schema đã chốt và ghi vào `04-database-design.md` §3.8.
>
> **Hạng mục bắt buộc phát sinh** (không có trong bản roadmap đầu, phát hiện khi khảo sát code — thiếu thì 2 ô dưới không thể hoàn thành):
>
> - [ ] Bổ sung dropdown chọn người thực hiện vào UI Kanban (task detail + thêm thẻ nhanh) — nguồn `GET /api/workspaces/{id}/members`.
>       Hiện `TaskDetailModal` **chỉ hiển thị** `assigneeName` và luôn gửi lại `assigneeId` cũ ⇒ chưa có đường nào để gán/đổi người thực hiện,
>       nên ô "gán task cho AI Agent qua đúng UI assignee" không thể làm được nếu thiếu việc này.
> - [ ] API gán/đổi người thực hiện phải kiểm **membership** của workspace chứa task (`TaskService.CreateTask/UpdateTask` hiện chỉ kiểm
>       `users.AnyAsync` theo bảng `users`) — nếu không, task có thể gán cho user ngoài workspace và AI Agent sẽ trở thành lỗ hổng mới.
>
> **Liên quan tới các giai đoạn sau:** giai đoạn 8 và 9 nằm sau giai đoạn 7 (xem "Thứ tự thi hành" ở đầu tài liệu) — chúng
> **không phải** bị bỏ. Việc lùi là có chủ ý: feature 1–5 (điểm khác biệt hoá của sản phẩm) đã xong, còn deploy/CI nên chạy trên một API
> đã đóng băng (tức sau khi giai đoạn 7 chốt contract), và Flutter thì "sau khi bản Web ổn định" — đúng như mô tả của chính giai đoạn 9.

## Giai đoạn 8: Hoàn thiện, Test & Deploy

> **Chưa bắt đầu — dự kiến sau Giai đoạn 9.** Nội dung giai đoạn không đổi; chỉ đổi thứ tự thi hành.

Viết test (xUnit, Vitest), dọn UI/UX, cấu hình CI/CD bằng GitHub Actions, deploy backend/frontend/DB lên hạ tầng free-tier đã chọn, xử lý các vấn đề cold-start/SignalR reconnect.

**Yêu cầu hoàn thiện:**
- [ ] Test cơ bản cho các luồng chính (Auth, Kanban CRUD, AI Smart Setup) bằng xUnit/Vitest
- [ ] CI/CD pipeline qua GitHub Actions chạy build/test tự động
- [ ] Backend deploy thành công lên Railway/Render, Database trên Neon/Supabase, Frontend trên Vercel/Netlify
- [ ] Kiểm tra và xử lý ổn thỏa hiện tượng cold-start ảnh hưởng SignalR (reconnect UX chấp nhận được)
- [ ] UI/UX rà soát lại tổng thể, sẵn sàng để demo/đưa vào CV

## Giai đoạn 9: Mobile (Flutter)

> **Chưa bắt đầu — dự kiến sau Giai đoạn 7.** Nội dung giai đoạn không đổi; chỉ đổi thứ tự thi hành.

Sau khi bản Web ổn định, xây app Flutter dùng chung API, tích hợp signalr_netcore cho real-time và kiểm thử trên thiết bị thật.

**Yêu cầu hoàn thiện:**
- [ ] App Flutter kết nối và xác thực được với API đã có (dùng chung OAuth/JWT flow)
- [ ] Các màn hình chính (Board, Task, thông báo AI Observer) hoạt động trên Flutter
- [ ] Real-time qua `signalr_netcore` hoạt động tương đương bản Web
- [ ] Kiểm thử thành công trên thiết bị thật (không chỉ emulator)

> **Kết thúc roadmap.** Giai đoạn 7 nằm ở trên (ngay sau Giai đoạn 6) theo "Thứ tự thi hành" ở đầu tài liệu.
