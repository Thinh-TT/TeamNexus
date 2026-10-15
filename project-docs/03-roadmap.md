# TeamNexus – Roadmap Phát triển

> Roadmap ở mức giai đoạn lớn (chưa chia task chi tiết). Mỗi giai đoạn kèm các yêu cầu hoàn thiện để coi là "xong" trước khi chuyển sang giai đoạn kế tiếp.

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (✅)** → **8 (✅)** → **9 (✅)** → **10 (✅)** → **11 (✅)** → **12 (✅)** → **13 (✅)** → **14 (✅)** → **14.5 (✅)** → **14.6 (✅ Track A)** → **14.7 (🟡 Track A ✅ · Track B 📋)** → 15 → 16.
>
> Số giai đoạn đã được **đánh lại** (lần 2 — sau Giai đoạn 12): **7 = AI Agent Executor**, **8 = Test & Deploy**, **9 = Đơn giản hóa Role**, **10 = Nâng cao Task & Workspace UX**, **11 = Quản lý Member & Profile**, **12 = Dashboard & Tìm kiếm**, **13 = Trực quan hóa & Thông báo**, **14 = Nâng cao AI**, **14.5 = Củng cố nền + AI Agent Chat cấp Board**, **14.6 = Hoàn thiện Trải nghiệm (Track A)**, **14.7 = Autonomous Agent & Tối ưu hóa Ra Quyết định với Jev (Hybrid AI)**, **15 = Tích hợp bên ngoài**, **16 = Mobile (Flutter)**. `01-system-specification.md` và `02-tech-stack-decisions.md` sẽ cập nhật khi mỗi giai đoạn bắt đầu.
>
> **Giai đoạn 14.5, 14.6 và 14.7 là các giai đoạn NỐI TIẾP của 14:** Giai đoạn 14.5 hoàn thiện chat board; Giai đoạn 14.6 giải quyết triệt để ma sát thực chiến UX (Track A); Giai đoạn 14.7 kế thừa và mở rộng Track B, hợp nhất mô hình Jev (Non-Generative Decision AI) để hoàn thiện nền tảng Autonomous AI Agent trước khi mở rộng ra Phase 15 (Tích hợp ngoài) và 16 (Mobile). Chi tiết kiến trúc Jev lưu tại `project-docs/06-adjust-and-upgrade-with-jev.md`.
>
> **Lưu ý khi đọc tài liệu cũ:** các tài liệu đã đóng băng của giai đoạn 4/5/6 (`tasks/phase-4..6-*.md`, `report/phase-5-*`, `report/phase-6-*`) vẫn dùng cách đánh số cũ — Giai đoạn 9 (cũ) = Mobile, nay là **Giai đoạn 16**.

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
- [x] Backend deploy thành công lên Render, Database trên Neon, Frontend trên Vercel
- [x] Kiểm tra và xử lý ổn thỏa hiện tượng cold-start ảnh hưởng SignalR (phần frontend **đã xong**)
- [x] UI/UX rà soát lại tổng thể, sẵn sàng để demo/đưa vào CV

## Giai đoạn 9: Đơn giản hóa Kiến trúc Role — ✅ HOÀN THÀNH

> **Prerequisite của mọi giai đoạn sau.** Thay đổi nhỏ ở tầng Auth, không ảnh hưởng workspace role hay schema board/task.
> Kế hoạch chi tiết: `tasks/phase-9-role-simplification.md`.

Đơn giản hóa Identity Role toàn cục từ 3 (`Admin`/`Manager`/`Member`) xuống 2 (**`Admin`** / **`User`**). Workspace Role (`workspace_members.role`) **giữ nguyên** (Admin/Manager/Member) vì đây là lớp phân quyền thực sự của mọi tính năng.

**Lý do:** Identity Role cũ chỉ được dùng ở 2 endpoint mẫu — mọi kiểm tra quyền thực sự đã đọc `workspace_members.role`. Giữ 3 Identity Role gây nhầm lẫn giữa "global role" và "workspace role". Sau thay đổi: `Admin` = system administrator (platform management); `User` = mọi người dùng thường đã đăng nhập.

**Yêu cầu hoàn thiện:**
- [x] `roles` table chỉ còn 2 row: `Admin` (system admin) và `User` (default cho mọi user thường)
- [x] User mới đăng ký OAuth tự động nhận Identity role `User` (thay vì `Member` cũ)
- [x] Policy definitions đơn giản: chỉ còn `SystemAdminPolicy` (Identity = Admin); mọi endpoint workspace dùng `.RequireAuthorization()` thuần + workspace role check trong service layer
- [x] Migration `Phase9RoleSimplification` chạy sạch: rename "Member" → "User" trong `roles` + `user_roles`, xóa row "Manager" (kèm migration `Phase9ModelSync` ⇒ tổng **8** migration)
- [x] Toàn bộ test PASS — backend **171 test / 0 fail / 0 skip**, frontend **37 file / 206 test PASS**, `oxlint` 0/0, `tsc -b` exit 0, `npm run build` OK

## Giai đoạn 10: Nâng cao Task & Workspace UX — ✅ HOÀN THÀNH

> **Không cần migration mới cho task fields** — `tasks.due_date`, `tasks.priority`, `tasks.description` đã có sẵn trong schema Phase 2.
> Chỉ cần xây UI và kết nối API. Riêng Workspace Settings và Activity Log cũng dùng bảng/endpoint đã có.
>
> **Kế hoạch chi tiết đã chia task (D1–D12, checklist theo file, ca biên, bằng chứng):**
> `tasks/phase-10-advanced-task-workspace-ux.md`.
>
> 📤 **Bàn giao §3 + §4 + §5 (frontend, CI, tài liệu):** `tasks/phase-10-remaining-frontend-handover.md`.
>
> **⛔ Schema đóng băng ở Giai đoạn 9 (8 migration)** — giai đoạn này **KHÔNG** thêm migration;
> `dotnet ef migrations has-pending-model-changes` phải tiếp tục trả "No changes have been made to the model since the last migration."
> **Đã kiểm: vẫn 8 migration, không có pending change.**
>
> **Kết quả đo thật hoàn thành:**
> backend **226 test PASS / 0 fail / 0 skip**, `dotnet build TeamNexus.sln -m:1 -nr:false` = **0 warning / 0 error**;
> frontend **47 file / 279 test PASS**, `oxlint` **0/0**, `tsc -b` exit 0, `npm run build` OK;
> `dotnet ef migrations list` = **8**. Cổng CI `ci-web.yml` đã được nâng và chạy xanh.

Hoàn thiện UI cho các field task đã có schema nhưng chưa có giao diện, thêm quản lý workspace cơ bản và hiển thị lịch sử hoạt động.

**Yêu cầu hoàn thiện:**
- [x] Task có thể set/hiển thị **Due Date** (hạn chót); card Kanban hiển thị badge đỏ khi quá hạn — kết nối với tín hiệu `OverdueTask` của AI Observer
- [x] Task có thể set/hiển thị **Priority** (Low/Medium/High/Urgent), badge màu trên card — ✅ **§1 XONG** (UI đã có sẵn từ trước; §1 bịt lỗ hổng test + sửa 2 bug validate — xem dưới)
- [x] Task có **Description** với markdown cơ bản (in đậm, code inline, link) trong modal chi tiết
- [x] **Workspace Settings** (Admin): sửa tên/mô tả workspace, chuyển ownership cho Admin khác, xóa workspace (với xác nhận) — ✅ **HOÀN THÀNH** (Backend §2 + Frontend §4)
- [x] **Activity Log UI** (Manager/Admin): trang lịch sử hoạt động workspace — tận dụng `activity_logs` table đã có từ Phase 5 — ✅ **HOÀN THÀNH** (Backend §2 + Frontend §4)

> **🐞 2 bug thật đã bắt & sửa ở §1** (nhờ viết test cho 3 field task — trước đó **không** test nào phủ):
> **(1)** `priority: "1"` bị `Enum.TryParse` **âm thầm** map thành `Medium` (và `"99"` lọt qua ⇒ vi phạm `ck_tasks_priority` ⇒ **500** thay vì 400)
> ⇒ sửa bằng `Enum.IsDefined` + helper `IsNumericString`; mọi giá trị số giờ trả **400**, còn `"urgent"` vẫn ⇒ `Urgent`.
> **(2)** `dueDate` có offset (ví dụ `+07:00`) làm Npgsql ném `only offset 0 (UTC) is supported` **từ `SaveChangesAsync`** ⇒ **mọi request 500**
> ⇒ sửa bằng helper `ToUtc(...)` ở cả create và update (payload activity ghi giá trị **đã chuẩn hoá**). UI hiện gửi `.toISOString()` nên bug
> **chưa từng lộ**, nhưng mọi client gửi offset theo múi giờ (Flutter Giai đoạn 13, mobile, Postman) đều dính.
> Kết quả §1: backend **189 test PASS / 0 fail / 0 skip**, `dotnet build` **0 warning / 0 error**, **không** migration, **không** đổi DTO/endpoint.

> **§2 — Backend Workspace & Activity (ĐÃ XONG, 37 test case mới):**
> - **Rút `GET /api/workspaces` khỏi `Program.cs`** (code inline từ Giai đoạn 1) thành `WorkspaceService` + `WorkspacesEndpoints` trong module Board;
>   payload **giữ nguyên 4 field cũ** (`id`/`name`/`description`/`role`) và **append** `ownerId`/`isOwner`; giữ nguyên side effect "tự tạo workspace mặc định" mà `DashboardPage` phụ thuộc.
> - **6 endpoint mới**: `GET /api/workspaces/{id}` · `PUT /api/workspaces/{id}` (Manager+) · `PUT /api/workspaces/{id}/owner` (owner/Admin) ·
>   `DELETE /api/workspaces/{id}` (owner/Admin, **soft delete**) · `GET /api/workspaces/{id}/activity` (Manager+).
> - **Activity feed** phân trang **keyset** `(created_at, id)` trần 200, filter `boardId`/`entityType`/`action`, lấy tên actor bằng LEFT JOIN `users`,
>   cursor hỏng ⇒ **400** (không 500). Ghi thêm **3 action cấp workspace** (`WorkspaceUpdated` / `WorkspaceOwnerTransferred` / `WorkspaceDeleted`, `board_id = NULL`).
> - **Quyết định đáng nhớ:** chuyển ownership **nâng** owner mới lên `Admin` nhưng **giữ nguyên** role owner cũ; **từ chối** chuyển cho AI Agent;
>   Manager (không phải owner) **không** chuyển owner/xoá được (**403**).

> **Khảo sát đầu kỳ (đã phản ánh vào tài liệu chia task):**
> **Đã có sẵn, không viết lại** — backend nhận & trả đủ `description`/`dueDate`/`priority`
> (`DTOs/TaskDtos.cs`, `TaskService.ParsePriority`, `DtoMapping.MapTask` điền ở **cả 3** đường trả task);
> card Kanban đã có badge priority màu và badge hạn chót (`TaskCard.tsx`); modal đã có Select priority + DatePicker + textarea mô tả.
> **Khoảng trống thật** — (1) **markdown** cho Description: repo **chưa có** thư viện markdown ⇒ tự viết renderer thuần, **không**
> `dangerouslySetInnerHTML`; (2) **Workspace Settings**: `GET /api/workspaces` đang là **code inline trong `Program.cs`**, chưa có
> PUT/DELETE/chuyển owner ⇒ tách ra `WorkspaceService` + `WorkspacesEndpoints`; (3) **Activity Log**: bảng `activity_logs` có dữ liệu
> từ Phase 5 nhưng **không** endpoint nào đọc cho UI ⇒ thêm `GET /api/workspaces/{id}/activity` (phân trang keyset).
> **Phát sinh bắt buộc** — nút vào 2 trang mới ở `BoardListPage`/`BoardView`; tách `useWorkspaceRole` dùng chung (logic phân quyền đang
> **copy 2 lần** ở `BoardView` và `ReportsPage`); và 1 **nghi vấn bug** ở `TaskDetailModal` (Select priority/ngày có thể gửi **giá trị cũ**
> do `onChange` chạy trước khi AntD Form cập nhật store) ⇒ **phải viết test chứng minh trước, chỉ sửa nếu test đỏ**.

## Giai đoạn 11: Quản lý Member & Profile (✅ HOÀN THÀNH — cả Backend & Frontend)

> Schema mới: bảng `workspace_invitations` và `email_messages` (Migration `Phase11MemberProfile`). Dịch vụ email tích hợp qua Resend API kết nối với tên miền riêng `teamnexus.cloud` (SPF/DKIM/DMARC qua Cloudflare DNS) kèm `NullEmailSender` fallback cho môi trường test/dev/CI.

Xây luồng mời thành viên vào workspace qua email, quản lý danh sách member, profile cá nhân và notification center.

**Yêu cầu hoàn thiện:**
- [x] **Mời member qua email**: Manager/Admin nhập email → hệ thống lưu token SHA-256 an toàn và gửi link mời qua Resend (`noreply@teamnexus.cloud`) → người nhận click link, chọn role (Member mặc định), tham gia workspace
- [x] **Gửi email nhanh**: Manager/Admin soạn tiêu đề + nội dung ngắn trong app → gửi đến inbox thật của một hoặc nhiều member (dùng cho thông báo họp, việc cần gấp...) kèm ghi nhận vào `email_messages`
- [x] **Quản lý member**: xem danh sách thành viên + role, hủy invitation đang chờ (Manager/Admin), đổi role member (chỉ Admin), kick member (chỉ Admin)
- [x] **Profile cá nhân**: xem/sửa display_name, avatar URL (validate an toàn); xem danh sách workspace đang tham gia + role của mình; tự rời workspace (chặn chủ sở hữu)
- [x] **Notification Center**: badge unread + Drawer tại AppHeader dùng chung — hỗ trợ cả thông báo AI Observer và thông báo nghiệp vụ (được assign task, comment mới, được mời vào workspace)

> **Trạng thái: ✅ ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ.**
> - **Backend:** **371 tests** (0 failed; CI PostgreSQL 18 assert 371/371 pass). Migration thứ 9 `Phase11MemberProfile` áp dụng thành công.
> - **Frontend:** **360/360 tests passed (60 test files)**; `npm run lint` đạt 0 warning / 0 error; `npx tsc -b` exit 0; `npm run build` thành công.
> - **Live Deployment:** Đã cấu hình và kết nối thành công tên miền chính thức `https://app.teamnexus.cloud` (Vercel) và `https://api.teamnexus.cloud` (Render).
> - Chi tiết: `tasks/phase-11-member-profile-management.md` và `report/phase-11-member-profile-management-test-report.md`.

## Giai đoạn 12: Dashboard & Tìm kiếm — ✅ **ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ**

> Xây trên dữ liệu từ Phase 10+11. Dashboard có giá trị cao khi workspace có nhiều member và task có due date.
>
> **Kế hoạch chi tiết đã chia task (D1–D13, P1–P13, ca biên, bằng chứng):**
> `tasks/phase-12-dashboard-search.md`.
> **Báo cáo nghiệm thu chi tiết:** `report/phase-12-dashboard-search-test-report.md`.
>
> **⛔ Schema đóng băng ở Giai đoạn 11 (9 migration)** — giai đoạn này **KHÔNG** thêm migration, **KHÔNG** bảng/cột/index mới.
> `@mention` dùng cột `notifications.type` (text tự do) + `notifications.payload` (jsonb) sẵn có —
> `dotnet ef migrations has-pending-model-changes` vẫn trả "No changes have been made to the model since the last migration."
>
> **Kết quả kiểm thử toàn diện đo thật:**
> - **Backend:** **Failed 0 / Passed 423 / Skipped 0 / Total 423** (PostgreSQL 18 Docker, cổng 5433).
>   `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` = **0 warning / 0 error**; `dotnet ef migrations list` = **9**.
>   Phân bổ: `DashboardApiTests` 14 · `TaskSearchApiTests` 24 · `CommentMentionApiTests` 10 · `NotificationTriggerApiTests` +4.
> - **Frontend:** **Failed 0 / Passed 406 / Total 406 (77 test files)**; `npm run lint` đạt **0 warning / 0 error** (194 files); `npx tsc -b` exit 0; `npm run build` thành công.
> - **CI Workflows:** Đã nâng ngưỡng kiểm soát `ci-backend.yml` (total = 423) và `ci-web.yml` (total ≥ 406).
>
> Trang tổng quan workspace sau đăng nhập và khả năng tìm kiếm/lọc task nâng cao.
>
> **Yêu cầu hoàn thiện:**
> - [x] **Dashboard tổng quan**: trang chủ workspace hiển thị "Task của tôi" (sắp đến hạn, quá hạn, mới giao), hoạt động gần đây (recent activity feed), tóm tắt board (số task theo trạng thái), cảnh báo AI Observer chưa đọc
>   - ✅ **Backend XONG** — `GET /api/workspaces/{id}/dashboard` (Member+, 404 người ngoài) + tham số `kind` trên `GET /api/notifications` để tách riêng "cảnh báo AI Observer chưa đọc"
>   - ✅ **Frontend XONG** — `WorkspaceDashboardPage` tại `/workspaces/:workspaceId/dashboard` với 3 panel thống kê, bộ lọc tab, tóm tắt board, cảnh báo observer và hoạt động gần đây
> - [x] **Tìm kiếm & Lọc task**: tìm task theo tên, assignee, label, priority, trạng thái, due date — trong phạm vi workspace hoặc board đang xem
>   - ✅ **Backend XONG** — `GET /api/workspaces/{id}/tasks/search` (11 tham số, keyset `(updated_at, id)`); `GET /api/boards/{id}/tasks` **giữ nguyên** shape mảng
>   - ✅ **Frontend XONG** — trang `/workspaces/:workspaceId/search` với bộ lọc đa điều kiện, phân trang tải thêm keyset, sync 2 chiều URL params + nút chuyển hướng nhanh "Tìm trong workspace →" tại `BoardView`
> - [x] **@mention trong comment**: tag thành viên bằng `@tên` (autocomplete), kích hoạt thông báo cho người được tag — mở rộng `notifications` table
>   - ✅ **Backend XONG** — `CreateCommentRequest.MentionUserIds` (**id tường minh**, server **không** parse `@tên`) + type `CommentMention` (jsonb, **không** cần migration)
>   - ✅ **Frontend XONG** — `Mentions` của antd v6 trong `TaskDetailModal` + lọc thành viên human + trích xuất `mentionUserIds` an toàn + nhãn "Được nhắc đến" trong Notification Center

> **Khảo sát đầu kỳ (đã phản ánh vào tài liệu chia task):**
> **Đã có sẵn, không viết lại** — `BoardView` **đã có** lọc client theo title/description/assignee/label + priority (nhưng thiếu trạng thái/due date và không tìm xuyên board);
> `CommentService` đã có 1 row `CommentOnTask` cho assignee; hạ tầng notification + `AppHeader`/`NotificationBell`/drawer
> đã đủ (Phase 11); 3 vocabulary notification đã tách sẵn; `activityLabels` UI, `taskDueDate.ts`, `useWorkspaceMembers`, `useWorkspaceRole` dùng lại được.
> **Khoảng trống thật** — (1) **Dashboard**: `DashboardPage` vẫn là trang demo Phase 1 (ô nhập Workspace ID + 3 nút test RBAC), **không** có API/service/hook nào;
> (2) **Search**: chưa có endpoint tìm xuyên board (chỉ có `GET /api/boards/{id}/tasks`), chưa lọc theo trạng thái/due date;
> (3) **Mention**: chưa có gì (`grep -i mention` toàn repo = 1 dòng comment).
> **Phát sinh bắt buộc** — `GET /api/notifications` **không tách được** "cảnh báo AI Observer chưa đọc" khỏi thông báo nghiệp vụ (badge trả tổng mọi loại)
> ⇒ phải **append** tham số `kind` (observer/agent/member) — additive, tham số vắng ⇒ hành vi y hệt;
> và trang chủ workspace phải là **route riêng** `/workspaces/:workspaceId/dashboard` (giữ nguyên `/` = danh sách workspace,
> vì `DashboardPage` phụ thuộc side effect "tự tạo workspace mặc định" của `GET /api/workspaces`).

## Giai đoạn 13: Trực quan hóa & Thông báo Chủ động — ✅ **ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ**

> **Kế hoạch chi tiết đã chia task (D1–D14, ca biên, bằng chứng):** `tasks/phase-13-visualization-proactive-notifications.md`.
> **📤 Note bàn giao Frontend** (giữ lại làm hồ sơ hợp đồng API + các bẫy đã biết): `tasks/phase-13-remaining-frontend-handover.md`.
> **Báo cáo nghiệm thu chi tiết:** `report/phase-13-visualization-proactive-notifications-test-report.md`.
>
> **⚠️ Khác bản roadmap đầu — HAI lệch có chủ ý & bắt buộc (chi tiết ở §D2 và §D5 của tài liệu chia task):**
> 1. **CÓ 1 migration additive:** thêm **1 cột** `users.digest_enabled` (`bool NOT NULL DEFAULT true`) ⇒ tổng **10**
>    migration (mới nhất `20260916045955_Phase13DailyDigest`). Lý do: yêu cầu *"người dùng có thể bật/tắt trong
>    Profile Settings"* **không thể** thoả mãn bằng cờ ở client — `BackgroundService` chạy phía **server** phải
>    **đọc** được lựa chọn đó, và `users` không có cột nào tái dùng được. `DEFAULT true` ⇒ không cần backfill.
> 2. **CÓ 1 endpoint read-only mới:** `GET /api/workspaces/{id}/reports/progress-series?from=&to=&boardId=&tzOffsetMinutes=`
>    (**Manager+**, cùng quyền `/summary`). Lý do: `ReportSummary` chỉ là ảnh chụp **một thời điểm**, và
>    `activity_logs` **không** lưu trạng thái cột ⇒ **không thể** tái dựng lịch sử open/closed ở client.
>    Burndown vì vậy **không thể** tính thuần frontend.
>
> **Kết quả ĐO THẬT (số đo thắng tài liệu):**
> - **Backend:** `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` = **0 warning / 0 error** ·
>   `dotnet test` (PostgreSQL thật) = **Failed 0 / Passed 480 / Skipped 0** (baseline 423 ⇒ **+57**) ·
>   `dotnet ef migrations list` = **10** · `has-pending-model-changes` = **sạch**.
>   Phân bổ 57 test mới: `ReportProgressSeriesTests` **17** · `ReportProgressSeriesApiTests` **10** ·
>   `DigestTemplateTests` **13** · `DailyDigestTests` **14** · `ProfileApiTests` **+3**.
> - **Frontend:** `npm test` = **Failed 0 / Passed 508 / Total 508 (86 file)** (baseline 407/77 file ⇒ **+101 / +9 file**) ·
>   `npm run lint` = **0 warning / 0 error** (212 file) · `npx tsc -b` = **exit 0** · `npm run build` = **thành công**.
> - **CI:** `ci-backend.yml` nâng `423` → **`480`**; `ci-web.yml` nâng `406` → **`508`**.

Bổ sung khả năng xem task theo chiều thời gian (Calendar), biểu đồ tiến độ trực quan trong app, và email digest tóm tắt hàng ngày/tuần cho từng thành viên — tất cả tận dụng dữ liệu đã có (`due_date`, `completed_at`, `activity_logs`, Resend API).

**Yêu cầu hoàn thiện:**
- [x] **Calendar View**: tab "Lịch" trong `BoardView` (antd `Calendar`) — thẻ xếp theo **hạn chót địa phương**, tràn gộp `+N`, ô có thẻ trễ hạn báo đỏ; click ngày mở `/search?boardId=&dueFrom=&dueTo=`; click thẻ mở `TaskDetailModal` hiện có; `Segmented` chuyển view với **Kanban là mặc định**. **Thuần client, không API mới** — tôn trọng ô tìm kiếm/bộ lọc ưu tiên đang bật.
- [x] **Burndown Chart & Velocity**: `GET /api/workspaces/{id}/reports/progress-series` (Manager+) trả chuỗi `openTasks`/`completions`/`creations` theo **ngày** (≤ 60 ngày) hoặc theo **tuần** (> 60, mốc Thứ Hai), `tzOffsetMinutes` clamp `±840` + echo giá trị thực dùng, cap 90 bucket + cờ `truncated`; tính bằng **hàm thuần** `ReportAggregator.BuildProgressSeries`. UI vẽ biểu đồ cột bằng **CSS** + `Statistic`/`Tooltip` (**không** thêm thư viện npm); lỗi chuỗi thời gian chỉ hiện `Alert` cục bộ, **không** làm hỏng khối báo cáo.
- [x] **Email Digest hàng ngày**: `DigestOptions` (mặc định **`Enabled = false`**) + `DailyDigestRunner` (scoped ⇒ gọi trực tiếp được từ test) + `DailyDigestBackgroundService` (bọc try/catch **mọi** tick) + `EmailTemplates.DailyDigest` (hàm thuần, HTML-escape mọi giá trị, giữ dấu tiếng Việt) + port `IDailyDigestService`/`NullDailyDigestService` ở Board (tái dùng `IDashboardService`). **Chống trùng ở tầng DB** ⇒ host free-tier ngủ/thức vẫn **tối đa 1 email/người/ngày**; digest **không** tạo notification, **không** ghi `activity_logs`. UI: tab "Thông báo" trong `ProfilePage`, mở thẳng tab khi URL có `#notifications`, **rollback** công tắc khi API lỗi.
- [x] Toàn bộ test xanh: **backend 480/0/0** · **frontend 508/0/0**; migration **9 → 10** (lệch #1 ở trên) và `has-pending-model-changes` sạch.

---

## Giai đoạn 14: Nâng cao AI — ✅ **HOÀN THÀNH (BACKEND & FRONTEND)**

> **Kế hoạch chi tiết đã chia task:** `tasks/phase-14-ai-advanced.md`.
> **Frontend hoàn thiện bởi:** antigravity (570 test frontend / 96 test files / 0 warning 0 error / CI gate -lt 570).
> **Báo cáo nghiệm thu chi tiết:** `report/phase-14-ai-advanced-test-report.md`.
>
> **⚠️ LỆCH DoD có chủ ý & bắt buộc (chi tiết ở §1 D6 và §10 R1 của tài liệu chia task):**
> **`ProjectHealthScore` KHÔNG đặt trong `ObserverSignalDetector`** như dòng yêu cầu dưới đây viết, mà ở
> `Board/Services/ProjectHealth.cs` (**hàm thuần**) và được **append cuối** `DashboardResponse`. Lý do:
> gauge nằm trên `WorkspaceDashboardPage` vốn là **Member+**, còn `ObserverSignalDetector` là nội bộ của
> pipeline **Manager+**; thêm nữa `Board → Ai` **bị cấm** (bất biến `Ai → Board` một chiều, Giai đoạn 13 §9)
> nên Board **không thể** nhúng một giá trị do Ai sở hữu. Tín hiệu `AtRiskDeadline` **vẫn** nằm trong
> `ObserverSignalDetector` (đúng yêu cầu). Quy tắc "at risk" thì **dùng chung** một định nghĩa duy nhất ở
> `TeamNexus.Shared.Risk.DeadlineRiskRules` ⇒ gauge và cảnh báo **không thể** nói hai số khác nhau.
>
> **Schedule/schema:** ⛔ **KHÔNG migration nào — `dotnet ef migrations list` vẫn là 10** (khác Giai đoạn 13).
> Cả 3 tính năng đều không cần schema mới: chat dùng `ai_action_logs` (**bảng đã có**, cột `action`/
> `entity_type` là free text) + transcript ở **client**; health **tính lại** mỗi request; board template dùng
> `boards`/`board_columns`/`tasks` sẵn có. `entity_type = 'Workspace'` **dùng lại** index
> `(entity_type, entity_id, created_at)` ⇒ không index mới.

Bổ sung khả năng tương tác trực tiếp với AI trong ngữ cảnh task, biến Observer thành công cụ **dự báo** rủi ro,
và mở rộng Smart Setup thành **AI Board Template**.

**Yêu cầu hoàn thiện:**
- [x] **AI Task Chat**: `POST /api/tasks/{id}/ai-chat/stream` (**SSE**, Member+) — `IAiStreamingProvider` (port **thứ ba**, không sửa `IAiProvider`), khung `meta` → `delta`* → `done` | `error` dựng bằng **hàm thuần** `AiChatSseWriter`, guard `AiChat:MaxHistoryMessages(12)`/`MaxHistoryChars(8000)` ⇒ **400 trước khi gọi provider**; + `POST .../ai-chat/message` lưu câu trả lời thành bình luận ⇒ `Pending` `PostComment` ⇒ Manager duyệt. **Transcript ở client** (không bảng mới); lỗi giữa luồng trả khung `error` thay vì 500.
- [x] **Observer Dự báo Rủi ro**: tín hiệu **`AtRiskDeadline`** (task chưa done, còn < 20% cửa sổ, 0 update trong 48 h, **sàn cửa sổ ≥ 2 ngày**) + severity `Critical ≤24h` / `High ≤72h` / `Medium`; `NotificationTypes.All` **4 → 5**; prompt liệt kê 5 type; `ObserverSummarizer` **pin thứ tự ưu tiên khi cắt** + đếm `signalsPreserved`/`signalsDroppedBeforePrompt`. **Gauge "Sức khỏe dự án"** = `ProjectHealth.Compute` (0–100, 4 band, 5 thành phần điểm trừ cộng lại **đúng 100**) **append cuối** `DashboardResponse`.
- [x] **AI Board Template**: `POST /api/workspaces/{id}/smart-setup/template` (Manager+, **không ghi gì**) + `/confirm` ⇒ `Pending` `CreateBoardFromTemplate` (`entity_type = 'Workspace'`) ⇒ Approve tạo **đúng 1 board + 2–6 cột (pin đúng 1 cột `is_done`) + 5–10 task**; Undo = **soft-delete board**.
- [x] Guardrail và giới hạn token áp dụng cho mọi tính năng mới (`AiChatOptions` + `ObserverOptions.AtRiskDeadline*` + `BoardTemplatePrompts` bounds, tất cả **clamp**); test coverage **≥ baseline** (xem số thật trong `phase-14-ai-advanced.md` §6.1 + báo cáo).

---

## Giai đoạn 14.5: Củng cố nền tảng + AI Agent Chat cấp Board — ✅ **HOÀN THÀNH (BACKEND & FRONTEND)**

> **Vì sao có giai đoạn này:** theo yêu cầu *"đọc lại tài liệu, rà các tính năng cần chỉnh sửa/nâng cấp/củng cố để làm nền chắc chắn cho giai đoạn 15 và 16"* + *"gộp AI Agent và Hỏi AI thành **AI Agent Chat** tổng của board"*.
> **Kế hoạch chi tiết:** `tasks/phase-14-5-foundation-and-agent-chat.md`.
> **Báo cáo nghiệm thu:** `report/phase-14-5-foundation-and-agent-chat-test-report.md`.
>
> **⚠️ LỆCH DoD có chủ ý so với Giai đoạn 14 (bắt buộc ghi rõ):** `tasks/phase-14-ai-advanced.md` §8 ghi
> **"⛔ KHÔNG được làm"** — *lưu transcript chat ở DB* (D2) và *chat nhiều task cùng lúc*. Giai đoạn 14.5
> **thay thế có chủ ý** hai dòng đó (người dùng chốt). Dòng thứ ba — *chat có tool/function-calling* —
> **vẫn bị cấm**. Cơ chế không đổi: **chưa duyệt thì chưa ghi dữ liệu nghiệp vụ**.
>
> **Schedule/schema:** ⚠️ **CÓ 1 migration** — bảng `agent_chat_messages` (`Phase14_5AgentChat`) ⇒
> `dotnet ef migrations list` **10 → 11**. Lệch có chủ ý so với Giai đoạn 14 (giai đoạn đó không migration).
> Cột `task_id` (nullable, kèm CHECK `num_nonnulls(board_id, task_id) = 1`) là **chỗ chừa**: hợp nhất task
> chat vào cùng bảng sau này sẽ **không cần migration nữa**.
>
> **Kết quả ĐO THẬT:** backend `dotnet build --no-incremental` **0 warning / 0 error** ·
> `dotnet test` **Failed 0 / Passed 643 / Skipped 0** (baseline 584 ⇒ **+59**) ·
> `migrations list` = **11** · `has-pending-model-changes` = **sạch** ·
> frontend **Failed 0 / Passed 633 / 107 file** (baseline 578 / 99 ⇒ **+55 / +8**) ·
> `oxlint` **0 warning / 0 error** (255 file) · `npx tsc -b` exit 0 · `npm run build` OK ·
> cổng CI: `ci-backend.yml` `584 → 643`, `ci-web.yml` `578 → 633`.

### Track A — củng cố nền (làm trước Phase 15/16)
- **A1.1** — **`GET /api/workspaces/{id}/ai-actions`** (Manager+). Vì `ai_action_logs` **không có**
  `workspace_id`, log tạo board (`entity_type='Workspace'`) **không bao giờ** hiện ở danh sách theo board:
  trước đây chỉ duyệt/undo được board-template **từ bên trong chính board nó vừa tạo**. FE: nút *Lịch sử AI*
  ở `BoardListPage` (+ re-fetch danh sách board sau khi duyệt). **Không** cột/index mới.
- **A1.2** — seam **`IExternalAlertSink`** đặt ở `NotificationService` (nơi **cả 4** đường ghi notification
  đi qua, không phải adapter `NotificationWriter` chỉ phủ 1 đường). Mặc định `NoOpExternalAlertSink`;
  transport hỏng **không** làm request ghi dữ liệu thất bại. ⇒ Phase 15 thêm **1 dòng đăng ký**, sửa **0**
  producer.
- **A2.1** — `AgentRunReaper` đóng luôn run mà **task đã bị xoá mềm** (`Failed` + `TaskChanged`, không
  notification). Tách `AgentRunReaperRunner` (scoped, test gọi trực tiếp được) khỏi hosted service.
- **A2.3** — bỏ **fetch trùng** số hành động AI đang chờ ở `BoardView`; hai helper log lỗi thay vì
  `.catch(() => {})` nuốt im lặng.
- **A2.4** — `AgentChatRetentionRunner`: retention **90 ngày**, theo batch, cho transcript chat.
- **A3** — nợ kỹ thuật ghi lại, **không** làm: attachment chỉ text ≤512 KB, chưa có webhook idempotency,
  chưa có seam GitHub PR ↔ task, chưa có AI provider fallback.

### Track B — AI Agent Chat cấp Board
- **Bề mặt:** nút nổi góc phải-dưới board ⇒ **panel docked ~1/4 bề rộng** (kéo được) + **kéo thẻ từ cột vào
  panel để đính kèm** (tối đa **8** thẻ/lượt) + action card cho đề xuất + stream câu trả lời.
- **Transcript lưu DB** nên hội thoại **bền qua reload**; đọc lại bằng
  `GET /api/boards/{id}/agent-chat/messages` (keyset).
- **Ngân sách riêng `AgentChat`** (2–3× `AiChat`): `MaxHistoryMessages 24`, `MaxHistoryChars 24000`,
  `MaxOutputTokens 2400`, `MaxAttachedTasks 8`, `MaxTotalContextChars 24000` — mọi giá trị **clamp**;
  vượt trần ⇒ **400 trước khi gọi provider** (0 token). `Enabled=false` ⇒ **503**.
- **3 dạng đề xuất**, tất cả đi **Chat → "Gửi đề xuất" → Pending → Manager duyệt → mới ghi**:
  `CreateSubtasks` (dùng lại applier Phase 4) · `UpdateTaskFields` · `MoveTaskToColumn`; **Undo** khôi phục
  đúng trạng thái trước (5 trường / cột cũ). Đề xuất **chỉ** chạm thẻ trong tập đính kèm và cột của board
  này; **không** vào cột `is_clarification`.
- **❗ 2 bug thật bắt được khi hiện thực:**
  **(P1)** cả hai service chat kiểm `chunk.Done` **trước** khi nối `chunk.Delta` ⇒ **mất chữ ở khung cuối**,
  mọi câu trả lời **ngắn đi đúng một chunk**, và bất biến `done.answer == Σ delta` (client dựa vào để khôi
  phục khung bị mất) **sai âm thầm** — bộ test Phase 14 không bắt được vì chỉ khẳng định hai thứ *cùng tồn
  tại*. **(P2)** `MoveTaskAsync` mở transaction riêng trong khi `ApproveAsync` đã mở ⇒ duyệt di chuyển thẻ
  trả **500**; bỏ transaction thừa (mọi thay đổi đã nằm trong **một** `SaveChangesAsync` nên nguyên tử
  không đổi, thứ tự "activity log sau commit" vẫn đúng).
> **Nợ kỹ thuật có chủ ý:** task chat trong `TaskDetailModal` **vẫn** transcript ở client; chỉ nhãn đổi thành
> **"Hỏi AI về thẻ này"** (1 assertion test được sửa — ngoại lệ duy nhất) và `data-testid` giữ nguyên.
> `agent_chat_messages.task_id` là chỗ chừa để hợp nhất sau **không cần migration**.

---

## Giai đoạn 14.6: Hoàn thiện Trải nghiệm (Track A) — ✅ ĐÃ HOÀN THÀNH

> **Vì sao có giai đoạn này:** Sau khi đưa Phase 14.5 lên production (`app.teamnexus.cloud`), kiểm thử thực chiến phát hiện các điểm ma sát UX thực tế (kéo thẻ vào dropzone khó khăn trên laptop/touch, model để lộ raw UUID ra chat, transcript task chat vẫn ở client, hỏi tiến độ rỗng khi chưa gắn thẻ).
> **Kế hoạch chi tiết & DoD:** `tasks/phase-14-6-ai-agent-enhancements.md`.
> **Báo cáo nghiệm thu Track A:** `report/phase-14-6-track-a-test-report.md` (665 backend tests / 657 frontend tests PASS).
> **Ghi chú kiến trúc:** Track B ban đầu của 14.6 được **hợp nhất và nâng tầm vào Giai đoạn 14.7** cùng với mô hình **Jev** để tạo nên nền tảng Autonomous AI Agent toàn diện.

### Các hạng mục đã hoàn thành (Track A — Quick-Wins & Hardening):
- **A1. Bộ chọn đính kèm thẻ trực quan:** ✅ Nút **"Đính kèm thẻ"** (Popover tìm kiếm theo tiêu đề/người phụ trách, hiển thị title + priority + assignee) trên header vùng đính kèm, song song với kéo-thả dnd; action **"Gửi vào AI Chat"** trong menu `...` của `TaskCard` (chỉ hiện khi có handler, tự mở panel + gắn thẻ).
- **A2. Chặn rò rỉ ID kỹ thuật (GUID / UUID):** ✅ Rule nghiêm ngặt trong **cả hai** system prompt (`AgentChatPrompts` + `AiChatPrompts`) **và** bộ lọc cưỡng chế: `AiChatAnswerSanitizer` (C#, lọc transcript trước khi ghi DB) + `redactIdentifiers.ts` (FE, lọc trên chuỗi tích luỹ khi hiển thị). Khối JSON đề xuất **không** bị lọc (ID thật phải sống để duyệt được).
- **A3. Hợp nhất Transcript Task Chat:** ✅ Task chat ghi/nạp `agent_chat_messages` qua `task_id` (`board_id = null`, CHECK của Migration 11 giữ isolation) + route `GET /api/tasks/{taskId}/agent-chat/messages`. **0 migration mới.**
- **A4. Lightweight Board Digest:** ✅ Khi `attachedTaskIds` rỗng, server tự dựng digest (số thẻ mỗi cột + top thẻ theo priority/hạn chót, trần `MaxDigestChars = 2000`); có thẻ đính kèm thì **không** thêm digest.

---

## Giai đoạn 14.7: Autonomous Agent & Tối ưu hóa Ra Quyết định với Jev (Hybrid AI Platform) — 🟡 TRACK A XONG (§A1–§A5), TRACK B 📋 KẾ HOẠCH

> **Mục tiêu:** Hợp nhất năng lực Autonomous AI Agent (kế thừa Track B của 14.6) với mô hình **Jev (Non-Generative Decision AI)** theo kiến trúc **Hybrid AI: System 1 (Jev - Ra quyết định có cấu trúc siêu tốc) + System 2 (DeepSeek - Tư duy ngôn ngữ & Giải thích sâu)**.
> **Tài liệu kiến trúc chi tiết:** `project-docs/06-adjust-and-upgrade-with-jev.md`.
> **Kế hoạch chia task + NHẬT KÝ THI HÀNH (5 bug thật + 6 lệch kế hoạch):** `tasks/phase-14-7-autonomous-agent-jev.md` §11.
> **Nguyên tắc bất biến:** Accountability Layer giữ nguyên 100% (mọi tác động ghi dữ liệu phải qua `Pending` → `Approve` → `Undo`); tuân thủ trần token & guardrail.
>
> **📊 SỐ ĐO THẬT (Track A hoàn thiện):** backend `dotnet build --no-incremental` **0 warning / 0 error** ·
> `dotnet test` **Failed 0 / Passed 913 / Skipped 0** (baseline 665 ⇒ **+248**) ·
> `dotnet ef migrations list` **11 → 12** (`20261014163751_Phase147AgentPlatform`) · `has-pending-model-changes` **sạch** ·
> frontend **680 / 117 file** (thêm 23 tests cho Track A) · `oxlint` **0/0** · `tsc -b` exit 0 · `npm run build` OK ·
> cổng CI: `ci-backend.yml` `665 → 913`; `ci-web.yml` `657 → 680`.
> **⚡ KẾT QUẢ ĐO LIVE JEV (OpenRouter key thật, 20 lượt):** 100% thành công (0 degraded), server latency P50 **773 ms**, client round-trip P50 **782 ms**.
>
> **⚠️ 4 LỆCH có chủ ý so với bản roadmap bên dưới (chi tiết ở §11.3 của tài liệu chia task):**
> 1. **1 migration** (`Phase147AgentPlatform`) — không phải 2 như dự định: phần pgvector thuộc **Track B §B3** và chưa thi hành.
> 2. **KHÔNG đổi image CI** sang `pgvector/pgvector:pg18` — chỉ cần khi có cột `vector`; để lại cho §B3.
> 3. **KHÔNG nới index `uq_workspace_members_ai_agent`** — nhiều agent row theo persona là **Track B §B1**; Track A giữ nguyên "đúng 1 agent/workspace" và **toàn bộ test Phase 7 xanh không sửa assertion nào**.
> 4. **Sửa ĐÚNG 1 assertion của test cũ** (`WorkspaceMemberApiTests`): §A2 **append** 4 field vào `WorkspaceMemberResponse` theo luật append-only từ Phase 10 §4.2. Là ngoại lệ **duy nhất** của Track A.

### 1. Nâng cấp Tiền đề Bắt buộc (Foundational Prerequisites)
- **1.1. Quản lý Nhãn Cố định & Taxonomy Chuẩn hóa:** ✅ **XONG** — bảng `workspace_taxonomies` (`domain`/`work_type`, UNIQUE `(workspace_id, kind, key)`), seed mặc định **lazy + idempotent bằng `INSERT … ON CONFLICT DO NOTHING`**, `GET/PUT /api/workspaces/{id}/taxonomies` (Member+ / Manager+). Frontend `TaxonomySettings` trong Workspace Settings.
- **1.2. Hồ sơ Kỹ năng (Skills) & Vai trò (Roles) Thành viên:** ✅ **XONG** — `workspace_members` thêm `skills` (jsonb), `primary_role`, `capacity_weight`, `max_wip_limit`; `PUT /api/workspaces/{id}/members/{userId}/profile` (**Admin**, từ chối AI Agent, field vắng ⇒ giữ nguyên). Frontend `MemberSkillsForm` trong Members Table.
- **1.3. Cấu trúc Logic Rẽ nhánh theo "Xác suất Tin cậy" (Confidence Threshold Branching):** ✅ **XONG** — `ConfidenceRouting` + `JevDecisionValidator` (hàm thuần); ngưỡng **cưỡng chế trong code, không tin provider**; test ở **đúng biên** 0.85/0.50 + `NaN`/`∞`. Frontend `confidenceHelpers`.

### 2. Tích hợp Mô hình Jev & Tối ưu Hóa Ra Quyết Định (System 1)
- **2.1. Phân loại Task & Đánh giá Mức độ Gấp (Auto-Triage & Smart Priority):** ✅ **XONG toàn diện (BE + FE)** — `POST /api/boards/{id}/tasks/triage` (Member+, **không ghi gì**, `Jev:Enabled=false` ⇒ 503, provider lỗi ⇒ **200 + `degraded:true`** thay vì 502). Trả `latencyMs`. Frontend: `useJevTriage` debounce 400ms + `JevTriagePanel` 3 nhánh, degraded warning, security badge.
- **2.2. Gợi ý Người Thực hiện Phù hợp (Smart Assignee Dispatching):** ✅ **XONG toàn diện (BE + FE)** — `POST /api/boards/{id}/tasks/{taskId}/assignee-suggestions`; Jev chỉ thấy nhãn `cand-N`. Frontend: `AssigneeSuggestion` hiển thị độ phù hợp %, lý do và nút gán nhanh.
- **2.3. AI Observer Toàn diện (Bộ đôi A + B):**
  - **Phần A** ✅ **XONG** — Jev chấm `severity` + cờ khẩn + rủi ro domino **trước**; verdict tự tin dưới ngưỡng ⇒ **chặn luôn lời gọi LLM** (đây là phần tiết kiệm token); verdict được truyền xuống **system prompt** để LLM giải thích đúng mức. `ObserverFindingValidator` **vẫn** là thứ quyết định finding nào được ghi. Cờ rollback: `Jev:ObserverSeverityRouting=false` ⇒ về đúng đường Phase 5/14 bằng 1 dòng config.
  - **Phần B** ✅ **XONG toàn diện (BE + FE)** — `RecoveryPlan` (`Pending`, scope `entity_type='Workspace'`) + `RecoveryPlanApplier` cho 4 kind; `EscalateManager` **không ghi gì** (có test riêng); `AutoRecoveryPlanEnabled = false` **mặc định**. Frontend: `RecoveryPlanCard` trong `AiActionLogItem` với cảnh báo họp khẩn cấp và nút Duyệt/Từ chối/Undo.

### 3. Nâng cấp Autonomous Agent Platform (Kế thừa Track B 14.6) — 📋 CHƯA THI HÀNH
- **3.1. Đội ngũ AI Chuyên Gia (Multi-Agent Personas):** 📋 Track B §B1 — cần nới `uq_workspace_members_ai_agent` thành `(workspace_id, persona_key)`.
- **3.2. Mở rộng Tool-Calling (ReAct Loop):** 📋 Track B §B2 — `InspectMemberWorkload`, `MatchSkills`, `ValidateAcceptanceCriteria`.
- **3.3. Bộ nhớ Dự án Ngữ nghĩa (Workspace RAG với pgvector):** 📋 Track B §B3 — cần `CREATE EXTENSION vector` + cột `vector(1536)` + index HNSW + đổi image CI.

**Yêu cầu hoàn thiện (DoD):**
- [x] Interface `IJevDecisionService` và mock `FakeJevDecisionProvider` hoạt động ổn định với kiểm thử đơn vị độc lập.
- [x] Bổ sung schema `workspace_taxonomies` và các cột `skills`/`primary_role` trên `workspace_members` (kèm migration EF Core sạch).
- [x] Tự động gợi ý phân loại và độ khẩn cấp của task (`latencyMs` trả về trong response — **đã đo thật 20 runs, P50 773 ms**).
- [x] Gợi ý assignee tuân thủ nghiêm ngặt logic rẽ nhánh theo ngưỡng confidence.
- [x] AI Observer phát hiện rủi ro sinh Recovery Plan Proposal ở trạng thái `Pending`, Quản lý có thể Approve/Reject/Undo thành công.
- [x] Toàn bộ test suite backend và frontend tiếp tục pass 100%, không vi phạm bất biến của Accountability Layer (**913/0/0** + **680/0/0**).
- [x] **Frontend của §A2–§A5** (triage panel, nút gợi ý người làm, màn hình taxonomy + hồ sơ kỹ năng, action card Recovery Plan) — ✅ **HOÀN THÀNH 100%**.

---

## Giai đoạn 15: Tích hợp bên ngoài

> **Chưa bắt đầu — dự kiến sau Giai đoạn 14.5.**
> **Độ phức tạp kỹ thuật: 🔴 Cao** — webhook, OAuth của bên thứ ba, lưu trữ token bảo mật, xử lý eventual consistency.
> **Đã có sẵn seam:** `IExternalAlertSink` (Giai đoạn 14.5 A1.2) là điểm cắm thông báo ra ngoài — Phase 15 đăng ký
> implementation thật và **không** phải sửa Observer/Agent/member path.

Kết nối TeamNexus với hệ sinh thái công cụ phát triển phổ biến: thông báo sang Slack/Discord, liên kết task với GitHub PR/Issue.

**Yêu cầu hoàn thiện:**
- [ ] **Slack / Discord Notification**: Workspace Settings → nhập Webhook URL; `SlackNotificationGateway : IExternalNotificationGateway` (adapter pattern, đúng tiền lệ `EmailGateway`); gửi khi AI Observer phát hiện tín hiệu, task overdue, member mới join
- [ ] **GitHub Integration**: Workspace Settings → nhập repo + Personal Access Token (mã hóa); task có thể gắn GitHub Issue/PR URL; webhook `POST /api/webhooks/github` → khi PR merge tự động move task sang cột `is_done`; chip "PR #123" trong `TaskCard` với link ngoài
- [ ] Webhook endpoint xác thực chữ ký HMAC-SHA256 (GitHub secret); token bên thứ ba được mã hóa khi lưu DB
- [ ] Hoạt động khi workspace không cấu hình tích hợp (graceful degradation)

---

## Giai đoạn 16: Mobile (Flutter)

> **Chưa bắt đầu — dự kiến sau khi bản Web đã có đủ tích hợp bên ngoài.**
> **Độ phức tạp kỹ thuật: 🔴 Cao** — nền tảng mới, cần xử lý OAuth flow trên mobile, real-time SignalR trên Flutter.
> Nội dung giữ nguyên từ roadmap cũ (trước đây là Giai đoạn 13 → 9 cũ).

Sau khi bản Web ổn định, xây app Flutter dùng chung toàn bộ API đã có, tích hợp `signalr_netcore` cho real-time và kiểm thử trên thiết bị thật. `dueDate` offset và priority enum đã được xử lý đúng ở backend (Phase 10 BUG-1/BUG-2) — Flutter sẽ hưởng lợi trực tiếp.

**Yêu cầu hoàn thiện:**
- [ ] App Flutter kết nối và xác thực được với API đã có (dùng chung OAuth/JWT flow)
- [ ] Các màn hình chính (Board Kanban, Task Detail, Dashboard, thông báo AI Observer) hoạt động trên Flutter
- [ ] Real-time qua `signalr_netcore` hoạt động tương đương bản Web
- [ ] Kiểm thử thành công trên thiết bị thật (không chỉ emulator)
