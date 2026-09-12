# TeamNexus – Roadmap Phát triển

> Roadmap ở mức giai đoạn lớn (chưa chia task chi tiết). Mỗi giai đoạn kèm các yêu cầu hoàn thiện để coi là "xong" trước khi chuyển sang giai đoạn kế tiếp.

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (đã hoàn thành)** → **8 (đang làm)** → 9.
>
> Số giai đoạn đã được **đánh lại cho khớp thứ tự thi hành**: **7 = AI Agent Executor**, **8 = Test & Deploy**, **9 = Mobile**. `01-system-specification.md` §7 và `02-tech-stack-decisions.md` §2.8 đã đổi theo.
>
> **Lưu ý khi đọc tài liệu cũ:** các tài liệu đã đóng băng của giai đoạn 4/5/6 (`tasks/phase-4..6-*.md`, `report/phase-5-*`, `report/phase-6-*`) vẫn dùng **cách đánh số cũ** khi nói "để Giai đoạn 7" ý là test xUnit — theo cách đánh số mới đó là **Giai đoạn 8**.
>
> **Vì sao làm 7 trước 8 và 9:** giai đoạn 1–6 đã hoàn tất & verify; giai đoạn 8 (test/CI/deploy) và 9 (Flutter mobile) **không chặn** giai đoạn 7, trong khi AI Agent Executor mới là
> điểm khác biệt hoá thật của sản phẩm (đúng mục tiêu portfolio/CV ở `02` mục bối cảnh) và đang là mạch phát triển AI còn "nóng". Giai đoạn 7 đã xong ⇒ **đang quay lại 8** (test/CI/deploy
> trên bản đã đóng băng API, xong sẽ tới 9 Flutter). Kế hoạch chi tiết từng bước: `tasks/phase-7-ai-agent-executor.md` và `tasks/phase-8-completion-test-deploy.md`.

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

## Giai đoạn 7: AI Agent Executor (✅ HOÀN THÀNH — cả Backend & Frontend)

Nâng AI từ vai trò đề xuất/quan sát lên vai trò thực thi thật: gán task cho AI Agent như một thành viên, xây vòng lặp tool-calling (soạn thảo, tóm tắt, web search qua Tavily), gắn kết quả vào Accountability Layer sẵn có, xử lý trạng thái "Chờ làm rõ" khi Agent thiếu thông tin.

**Yêu cầu hoàn thiện:**
- [x] Thêm pseudo-member AI Agent vào `workspace_members`, **gán được task qua đúng UI assignee hiện có** (`TaskDetailModal` Select và `KanbanColumn` quick-add, API `PUT /api/boards/{boardId}/tasks/{taskId}` — đã verify 200 + `assigneeIsAiAgent=true`)
- [x] Vòng lặp tool-calling hoạt động qua DeepSeek function-calling với bộ tool: `SearchSystemData`, `WebSearch` (Tavily), `DraftOutput`, `RequestClarification` — verify nhóm **B/C/D** + **1 lượt DeepSeek thật** (2–3 tool call, 4 356→5 114 token) + **Tavily thật** qua tool `WebSearch`
- [x] Trạng thái task mới "Chờ làm rõ" hoạt động: Agent tạm dừng đúng lúc (`AwaitingClarification` + comment câu hỏi + `board_columns.is_clarification` tạo lazy), hiển thị nổi bật trên Kanban (icon, card clamp, modal câu hỏi), nút "Chạy lại" hoạt động sau khi trưởng nhóm trả lời (verify nhóm **E** & **J**; append-only: run cũ byte-identical)
- [x] Kết quả AI (comment ngắn hoặc file đính kèm dài) đi qua đúng luồng Accountability Layer (Pending → Approve/Reject/Undo) — verify nhóm **D** (comment soft-delete khi undo, tệp **hard delete**), đúng CAS 409 của Phase 4, UI tích hợp `AgentDraftApproval` tái dùng `AiActionLogItem`
- [x] Guardrail hoạt động đúng: dừng khi vượt 15 tool-call / 5 phút / ~50.000 token (+ 20 lượt gọi model), `error` ghi rõ ngưỡng bị vượt và số đã dùng, **đúng một** notification `AgentRunFailed` (`notification_sent=true` chống spam) — verify nhóm **F** (4 ngưỡng, mỗi ngưỡng một lần boot)
- [x] Bảng `agent_runs` ghi đầy đủ trạng thái/tool trace (`tool_call_trace` jsonb có cap + cờ `trace_truncated`, counters cập nhật **dần sau mỗi vòng**), broadcast real-time qua SignalR (`AgentRunProgress`, 2 lần/run), hiển thị trực quan trên Kanban với `AgentRunPanel`, counters, timeline trace và download attachment blob
- [x] Bổ sung dropdown chọn người thực hiện vào UI Kanban (task detail `TaskDetailModal` + thêm thẻ nhanh `KanbanColumn`) — nguồn `GET /api/workspaces/{id}/members` qua hook `useWorkspaceMembers` (cache theo workspaceId)

> **Trạng thái: ✅ ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ (Cả Backend & Frontend).**
> - **Backend (§2–§4, §6):** Verify **§7 = 291/291 check PASS** trên **API Kestrel thật + PostgreSQL 18 thật**; `dotnet build` 0 warning/0 error; `migrations list` = **6**.
> - **Frontend (§5, Nhóm J):** Đầy đủ components `AgentRunPanel`, `AgentDraftApproval`, `AttachmentList`, assignee select dropdown, clarification badge/indicator, SignalR real-time event hook, Vitest test suite đạt **35 test files / 187 tests PASS 100%** (vượt baseline 155), `npm run lint` **0/0**, `npx tsc -b` **exit 0**, `npm run build` thành công.
> Chi tiết: `tasks/phase-7-ai-agent-executor.md` và `report/phase-7-ai-agent-executor-test-report.md`.
>
> **Hạng mục bắt buộc phát sinh:**
>
> - [x] API gán/đổi người thực hiện phải kiểm **membership** của workspace chứa task — **đã siết** ở §3.2 (`RequireAssigneeInWorkspaceAsync`, user ngoài workspace ⇒ **400**; agent đi qua đúng đường này) và verify ở nhóm I.
> - [x] Bổ sung dropdown chọn người thực hiện vào UI Kanban (task detail + thêm thẻ nhanh) — nguồn `GET /api/workspaces/{id}/members` đã hoàn tất tại `TaskDetailModal.tsx` và `KanbanColumn.tsx`.
>
> **Liên quan tới các giai đoạn sau:** giai đoạn 8 và 9 nằm sau giai đoạn 7 (xem "Thứ tự thi hành" ở đầu tài liệu) — chúng
> **không phải** bị bỏ. Việc lùi là có chủ ý: feature 1–5 (điểm khác biệt hoá của sản phẩm) đã xong, còn deploy/CI nên chạy trên một API
> đã đóng băng (tức sau khi giai đoạn 7 chốt contract), và Flutter thì "sau khi bản Web ổn định" — đúng như mô tả của chính giai đoạn 9.

## Giai đoạn 8: Hoàn thiện, Test & Deploy

> **Đang làm — tiếp ngay sau Giai đoạn 7.** Nội dung giai đoạn không đổi. Kế hoạch chi tiết đã chia task:
> `tasks/phase-8-completion-test-deploy.md` (gồm cả hướng dẫn deploy từng bước cho người chưa từng dùng Render/Neon/Vercel/Railway/Supabase/Netlify/GitHub Actions).

Viết test (xUnit, Vitest), dọn UI/UX, cấu hình CI/CD bằng GitHub Actions, deploy backend/frontend/DB lên hạ tầng free-tier đã chọn, xử lý các vấn đề cold-start/SignalR reconnect.

**Yêu cầu hoàn thiện:**
- [x] Test cơ bản cho các luồng chính (Auth, Kanban CRUD, AI Smart Setup) bằng xUnit/Vitest
      — backend **172 test** (xUnit v3 + `WebApplicationFactory<Program>` + PostgreSQL 18 thật), frontend **206 test** (Vitest + Testing Library)
- [x] CI/CD pipeline qua GitHub Actions chạy build/test tự động
      — `ci-backend` (.NET 10 + `postgres:18`) và `ci-web` (Node 24: lint → tsc → test → build); **không** giữ secret nào;
      cả hai workflow **xanh** trên run thật; có cổng chặn "test bị skip" và "số test tụt"
- [ ] Backend deploy thành công lên Railway/Render, Database trên Neon/Supabase, Frontend trên Vercel/Netlify
- [ ] Kiểm tra và xử lý ổn thỏa hiện tượng cold-start ảnh hưởng SignalR (reconnect UX chấp nhận được)
      — phần frontend **đã xong** (retry vô hạn có trần + nút "Kết nối lại" + refetch khi reconnect); còn đo cold-start thật sau khi deploy
- [ ] UI/UX rà soát lại tổng thể, sẵn sàng để demo/đưa vào CV

## Giai đoạn 9: Mobile (Flutter)

> **Chưa bắt đầu — dự kiến sau Giai đoạn 8.** Nội dung giai đoạn không đổi; chỉ đổi thứ tự thi hành.

Sau khi bản Web ổn định, xây app Flutter dùng chung API, tích hợp signalr_netcore cho real-time và kiểm thử trên thiết bị thật.

**Yêu cầu hoàn thiện:**
- [ ] App Flutter kết nối và xác thực được với API đã có (dùng chung OAuth/JWT flow)
- [ ] Các màn hình chính (Board, Task, thông báo AI Observer) hoạt động trên Flutter
- [ ] Real-time qua `signalr_netcore` hoạt động tương đương bản Web
- [ ] Kiểm thử thành công trên thiết bị thật (không chỉ emulator)

