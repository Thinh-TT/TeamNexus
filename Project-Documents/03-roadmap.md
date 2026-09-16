# TeamNexus – Roadmap Phát triển

> Roadmap ở mức giai đoạn lớn (chưa chia task chi tiết). Mỗi giai đoạn kèm các yêu cầu hoàn thiện để coi là "xong" trước khi chuyển sang giai đoạn kế tiếp.

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (✅)** → **8 (✅)** → **9 (✅)** → **10 (✅)** → **11 (✅)** → **12 (✅)** → **13 (✅)** → 14 → 15 → 16.
>
> Số giai đoạn đã được **đánh lại** (lần 2 — sau Giai đoạn 12): **7 = AI Agent Executor**, **8 = Test & Deploy**, **9 = Đơn giản hóa Role**, **10 = Nâng cao Task & Workspace UX**, **11 = Quản lý Member & Profile**, **12 = Dashboard & Tìm kiếm**, **13 = Trực quan hóa & Thông báo**, **14 = Nâng cao AI**, **15 = Tích hợp bên ngoài**, **16 = Mobile (Flutter)**. `01-system-specification.md` và `02-tech-stack-decisions.md` sẽ cập nhật khi mỗi giai đoạn bắt đầu.
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
- [ ] Backend deploy thành công lên Render, Database trên Neon, Frontend trên Vercel
- [ ] Kiểm tra và xử lý ổn thỏa hiện tượng cold-start ảnh hưởng SignalR (phần frontend **đã xong**)
- [ ] UI/UX rà soát lại tổng thể, sẵn sàng để demo/đưa vào CV

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

## Giai đoạn 14: Nâng cao AI

> **Chưa bắt đầu — dự kiến sau Giai đoạn 13.**
> **Độ phức tạp kỹ thuật: 🟡 Trung bình** — mở rộng `IAiProvider`, `ObserverService`, `SmartSetupService` đã có; cần endpoint mới và có thể thêm 1 migration nhỏ.

Nâng AI từ vai trò quan sát/đề xuất lên tương tác trực tiếp trong ngữ cảnh task, đồng thời cải thiện Observer thành công cụ dự báo rủi ro chủ động thay vì chỉ phát hiện sau khi xảy ra.

**Yêu cầu hoàn thiện:**
- [ ] **AI Task Chat**: nút "Hỏi AI" trong `TaskDetailModal` — người dùng chat với AI trong ngữ cảnh task (title, description, comment); backend dùng `IAiProvider` + streaming SSE; kết quả đi qua Accountability Layer (Pending → Approve/Reject)
- [ ] **Observer Dự báo Rủi ro**: thêm signal `AtRiskDeadline` (task chưa done, còn < 20% thời gian nhưng 0 update trong 48h) và `ProjectHealthScore` (0–100 tổng hợp) vào `ObserverSignalDetector`; hiển thị gauge "Sức khỏe dự án" trên `WorkspaceDashboardPage`
- [ ] **AI Board Template**: mở rộng Smart Setup (Phase 3) — AI đề xuất luôn cấu trúc board (column) và 5–10 task khởi đầu dựa trên mô tả dự án; UI preview trước khi confirm qua Accountability Layer
- [ ] Guardrail và giới hạn token áp dụng cho mọi tính năng mới; test coverage ≥ baseline

---

## Giai đoạn 15: Tích hợp bên ngoài

> **Chưa bắt đầu — dự kiến sau Giai đoạn 14.**
> **Độ phức tạp kỹ thuật: 🔴 Cao** — webhook, OAuth của bên thứ ba, lưu trữ token bảo mật, xử lý eventual consistency.

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
