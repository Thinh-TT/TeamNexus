# TeamNexus – Lộ trình Phát triển (Master Roadmap)

> Tài liệu theo dõi tiến độ tổng thể và các tiêu chí hoàn thành (Definition of Done – DoD) qua từng giai đoạn của dự án **TeamNexus**.
>
> **Thứ tự lộ trình:** `1 (✅)` → `2 (✅)` → `3 (✅)` → `4 (✅)` → `5 (✅)` → `6 (✅)` → `7 (✅)` → `8 (✅)` → `9 (✅)` → `10 (✅)` → `11 (✅)` → `12 (✅)` → `13 (✅)` → `14 (✅)` → `14.5 (✅)` → `14.6 (✅)` → `14.7 (✅)` → `15 (✅)` → `16 (⏳)`.

---

## Bảng Tổng hợp Trạng thái & Số đo Nghiệm thu

| Giai đoạn | Tên giai đoạn | Trạng thái | Tổng số Migration | Backend Tests | Frontend Tests |
| :---: | :--- | :---: | :---: | :---: | :---: |
| **1** | Nền tảng & Xác thực (Auth & RBAC) | ✅ Hoàn thành | 1 | — | — |
| **2** | Kanban Core (CRUD & Real-time SignalR) | ✅ Hoàn thành | 3 | — | — |
| **3** | AI Smart Setup (Khởi tạo công việc tự động) | ✅ Hoàn thành | 3 | — | — |
| **4** | Hệ thống Giải trình AI (Accountability Layer) | ✅ Hoàn thành | 4 | — | — |
| **5** | AI Observer (Giám sát & Cảnh báo ngầm) | ✅ Hoàn thành | 5 | — | — |
| **6** | Báo cáo & Xuất dữ liệu (PDF / Excel) | ✅ Hoàn thành | 5 | — | — |
| **7** | AI Agent Executor (Thành viên ảo thực thi) | ✅ Hoàn thành | 6 | 291 checks | 187 |
| **8** | Kiểm thử Tự động, CI/CD & Triển khai Cloud | ✅ Hoàn thành | 6 | 172 | 206 |
| **9** | Đơn giản hóa Kiến trúc Phân quyền Hai lớp | ✅ Hoàn thành | 8 | 171 | 206 |
| **10** | Nâng cao Trải nghiệm Task & Quản trị Workspace | ✅ Hoàn thành | 8 | 226 | 279 |
| **11** | Quản lý Thành viên, Email & Hồ sơ Cá nhân | ✅ Hoàn thành | 9 | 371 | 360 |
| **12** | Trang Tổng quan (Dashboard), Tìm kiếm & `@mention` | ✅ Hoàn thành | 9 | 423 | 406 |
| **13** | Lịch biểu, Biểu đồ Tiến độ & Daily Email Digest | ✅ Hoàn thành | 10 | 480 | 508 |
| **14** | Nâng cao AI (Task Chat SSE, Dự báo Rủi ro, Board Template) | ✅ Hoàn thành | 10 | 584 | 578 |
| **14.5** | Củng cố Nền tảng & AI Agent Chat cấp Bảng | ✅ Hoàn thành | 11 | 643 | 633 |
| **14.6** | Hoàn thiện UX Đính kèm Thẻ & Chống rò rỉ Định danh | ✅ Hoàn thành | 11 | 665 | 657 |
| **14.7** | Kiến trúc Hybrid AI với Jev, Personas, ReAct & RAG (`pgvector`) | ✅ Hoàn thành | 14 | 1.167 | 738 |
| **15** | Tích hợp Hệ sinh thái Bên ngoài (Slack, Discord, GitHub) | ✅ Hoàn thành | 16 | 1.346 | 799 |
| **16** | Ứng dụng Di động Đa nền tảng (Flutter) | ⏳ Chưa bắt đầu | — | — | — |

---

## Giai đoạn 1: Nền tảng & Auth — ✅ HOÀN THÀNH

Khởi tạo kiến trúc Modular Monolith trên ASP.NET Core kết hợp React + TypeScript + Vite, thiết kế schema PostgreSQL nền tảng, tích hợp ASP.NET Core Identity với OAuth 2.0 (Google / GitHub), JWT + Refresh Token qua `HttpOnly Cookie` và phân quyền `Admin` / `Manager` / `Member` theo workspace.

**Yêu cầu hoàn thiện (DoD):**
- [x] Khởi tạo giải pháp backend (Modular Monolith) và frontend (React/Vite) biên dịch và chạy ổn định.
- [x] Thiết lập schema PostgreSQL cơ bản (`users`, `roles`, `workspaces`, `workspace_members`, `refresh_tokens`) qua migration `InitialSchema`.
- [x] Đăng nhập thành công qua Google OAuth và GitHub OAuth.
- [x] Cấp phát JWT + Refresh Token qua `HttpOnly Cookie`, hỗ trợ xoay vòng (rotate) refresh token và chống CSRF.
- [x] Phân quyền theo vai trò cho các thao tác trong phạm vi không gian làm việc.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-1-auth.md`](tasks/phase-1-auth.md).

---

## Giai đoạn 2: Kanban Core (CRUD + Real-time) — ✅ HOÀN THÀNH

Xây dựng đầy đủ nghiệp vụ CRUD cho Bảng (`Board`), Cột (`Column`), Thẻ công việc (`Task`), Nhãn (`Label`) và Bình luận (`Comment`); xây dựng giao diện kéo-thả bằng `@dnd-kit` và tích hợp SignalR `BoardHub` để đồng bộ trạng thái thời gian thực giữa các trình duyệt.

**Yêu cầu hoàn thiện (DoD):**
- [x] Hoàn thiện API CRUD cho Board, Column (kèm cờ `is_done`), Task, Label, Comment.
- [x] Giao diện kéo-thả thẻ giữa các cột và sắp xếp lại thứ tự cột hoạt động mượt mà.
- [x] Đồng bộ thời gian thực qua SignalR: thay đổi ở máy khách A phản ánh tức thời trên máy khách B mà không cần tải lại trang.
- [x] Cô lập sự kiện theo nhóm `board-{boardId}` và tự động kết nối lại khi rớt mạng.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-2-kanban.md`](tasks/phase-2-kanban.md) · Báo cáo nghiệm thu [`report/phase-2-kanban-test-report.md`](report/phase-2-kanban-test-report.md).

---

## Giai đoạn 3: AI Smart Setup — ✅ HOÀN THÀNH

Tích hợp DeepSeek API qua giao diện trừu tượng `IAiProvider` (kèm `FakeAiProvider` ngoại tuyến); xây dựng luồng khởi tạo thông minh: Quản lý nhập mô tả yêu cầu → AI phân rã thành danh sách thẻ công việc kèm độ ưu tiên, nhãn và gợi ý người phụ trách → hiển thị bản xem trước để con người chỉnh sửa.

**Yêu cầu hoàn thiện (DoD):**
- [x] Triển khai `IAiProvider` cho DeepSeek API và `FakeAiProvider` phục vụ kiểm thử tất định.
- [x] Từ mô tả dự án/tính năng, AI trả về danh sách sub-tasks chuẩn JSON schema, tự động đối chiếu nhãn và thành viên sẵn có trong workspace.
- [x] Giao diện hiển thị bản đề xuất của AI, cho phép chỉnh sửa tiêu đề, độ ưu tiên, nhãn và người phụ trách trước khi xác nhận.
- [x] Tuyệt đối không ghi thẳng vào CSDL khi chưa qua bước xác nhận của con người.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-3-ai-smart-setup.md`](tasks/phase-3-ai-smart-setup.md).

---

## Giai đoạn 4: Accountability Layer — ✅ HOÀN THÀNH

Xây dựng bảng `ai_action_logs` và dịch vụ trung tâm `AiActionService` làm cổng kiểm duyệt duy nhất cho mọi hành động ghi dữ liệu của AI (`Pending → Approved / Rejected → Undone`), tích hợp hoàn chỉnh với luồng AI Smart Setup.

**Yêu cầu hoàn thiện (DoD):**
- [x] Thiết lập bảng `ai_action_logs` lưu loại hành động, cơ sở dữ liệu đầu vào (`basis`), ảnh chụp trước/đề xuất/đã áp dụng (`before_snapshot`, `after_snapshot`, `applied_snapshot`), người yêu cầu, người quyết định và trạng thái.
- [x] Đảm bảo `AiActionService` là cổng duy nhất thực thi thay đổi dữ liệu do AI đề xuất (sử dụng cơ chế CAS chống duyệt trùng).
- [x] Chuyển đổi luồng xác nhận Smart Setup sang tạo bản ghi `Pending` → Quản lý `Approve` / `Reject`.
- [x] Hoàn thiện tính năng Hoàn tác (`Undo`) khôi phục chính xác trạng thái trước khi áp dụng (xóa mềm các thẻ đã tạo và dọn nhãn mới sinh).

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-4-accountability-layer.md`](tasks/phase-4-accountability-layer.md) · Báo cáo nghiệm thu [`report/phase-4-accountability-test-report.md`](report/phase-4-accountability-test-report.md).

---

## Giai đoạn 5: AI Observer — ✅ HOÀN THÀNH

Xây dựng tiến trình nền `ObserverBackgroundService` quét nhật ký hoạt động và trạng thái bảng định kỳ để phát hiện sớm điểm nghẽn (`Bottleneck`), quá tải nhân sự (`Overload`), thẻ quá hạn (`OverdueTask`) và thẻ đình trệ (`StalledTask`), gửi cảnh báo riêng cho cấp Quản lý.

**Yêu cầu hoàn thiện (DoD):**
- [x] Bổ sung các bảng `activity_logs`, `notifications` và `ai_observer_runs` (Migration #5).
- [x] Tiến trình nền quét định kỳ an toàn với khóa chống chạy chồng (`pg_try_advisory_lock`) và cơ chế dọn dẹp nhật ký cũ theo thời hạn lưu giữ.
- [x] Tóm tắt tín hiệu trước khi gọi LLM để kiểm soát chi phí token và xác thực chặt chẽ kết quả trả về (`ObserverFindingValidator` chống ảo giác định danh).
- [x] Phát cảnh báo riêng cho `Manager` / `Admin` trong ứng dụng kèm cửa sổ khử trùng lặp 24 giờ.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-5-ai-observer.md`](tasks/phase-5-ai-observer.md) · Báo cáo nghiệm thu [`report/phase-5-ai-observer-test-report.md`](report/phase-5-ai-observer-test-report.md).

---

## Giai đoạn 6: Báo cáo & Xuất dữ liệu — ✅ HOÀN THÀNH

Xây dựng module `TeamNexus.Modules.Reporting` tổng hợp số liệu tiến độ, hiệu suất hoàn thành, hoạt động và sức khỏe dự án; hỗ trợ kết xuất báo cáo theo yêu cầu ra định dạng PDF (QuestPDF) và Excel (ClosedXML) trực tiếp trong bộ nhớ RAM.

**Yêu cầu hoàn thiện (DoD):**
- [x] Tổng hợp chính xác các chỉ số tiến độ và hiệu suất theo khoảng thời gian và theo bảng (`ReportAggregator`).
- [x] Xuất báo cáo PDF (QuestPDF) trình bày rõ ràng, hiển thị đúng tiếng Việt.
- [x] Xuất báo cáo Excel (ClosedXML) gồm 5 trang tính với định dạng thanh dữ liệu và bộ lọc tự động.
- [x] Kết xuất tệp hoàn toàn trong bộ nhớ đệm (`byte[]`), không lưu trữ tệp vật lý trên máy chủ.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-6-reporting-export.md`](tasks/phase-6-reporting-export.md) · Báo cáo nghiệm thu [`report/phase-6-reporting-test-report.md`](report/phase-6-reporting-test-report.md).

---

## Giai đoạn 7: AI Agent Executor — ✅ HOÀN THÀNH

Nâng cấp AI từ vai trò đề xuất/quan sát lên vai trò thực thi công việc: khởi tạo thành viên ảo (`member_type = 'ai_agent'`) nhận thẻ trực tiếp trên Kanban, thực thi vòng lặp gọi công cụ (`SearchSystemData`, `WebSearch` qua Tavily, `DraftOutput`, `RequestClarification`), xử lý cột *"Chờ làm rõ"* (`is_clarification`) và đưa mọi đầu ra bình luận/tệp đính kèm qua Lớp Giải trình.

**Yêu cầu hoàn thiện (DoD):**
- [x] Bổ sung thành viên ảo `ai_agent` vào `workspace_members`, cho phép gán thẻ công việc qua đúng giao diện chọn người phụ trách hiện hữu.
- [x] Triển khai vòng lặp function-calling (`IAiToolCallingProvider`) với 4 công cụ trong danh sách trắng.
- [x] Hỗ trợ trạng thái cột *"Chờ làm rõ"* (`is_clarification`) và thao tác *"Chạy lại"* sau khi Quản lý giải đáp câu hỏi của Agent.
- [x] Đưa kết quả đầu ra ngắn (`PostComment`) và dài (`PostAttachment` lưu `bytea ≤ 512 KB`) qua quy trình `Pending → Approve/Reject → Undo`.
- [x] Cưỡng chế giới hạn ngân sách (`15 tool-call` / `5 phút` / `50.000 token`) và phát sóng tiến độ thời gian thực (`AgentRunProgress`) qua SignalR.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-7-ai-agent-executor.md`](tasks/phase-7-ai-agent-executor.md) · Bàn giao FE [`tasks/handovers/phase-7-frontend-handover.md`](tasks/handovers/phase-7-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-7-ai-agent-executor-test-report.md`](report/phase-7-ai-agent-executor-test-report.md).

---

## Giai đoạn 8: Hoàn thiện, Kiểm thử Tự động & Triển khai Cloud — ✅ HOÀN THÀNH

Xây dựng bộ kiểm thử tự động toàn diện bằng xUnit (Backend) và Vitest (Frontend), thiết lập quy trình CI/CD qua GitHub Actions và triển khai hệ thống thực tế lên hạ tầng Cloud (Vercel, Render Docker, Neon PostgreSQL, Cloudflare).

**Yêu cầu hoàn thiện (DoD):**
- [x] Xây dựng bộ kiểm thử tự động cho toàn bộ các luồng nghiệp vụ chính (**172** backend tests, **206** frontend tests).
- [x] Cấu hình thành công 2 pipeline GitHub Actions (`ci-backend.yml` và `ci-web.yml`) tự động kiểm tra build và chạy test.
- [x] Triển khai hoàn chỉnh Backend lên Render, Database lên Neon Serverless PostgreSQL và Frontend lên Vercel.
- [x] Xử lý mượt mà trạng thái chờ khởi động lại (cold-start) và tự động kết nối lại SignalR trên giao diện Web.

> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-8-completion-test-deploy.md`](tasks/phase-8-completion-test-deploy.md) · Bàn giao FE [`tasks/handovers/phase-8-2b-frontend-handover.md`](tasks/handovers/phase-8-2b-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-8-completion-test-deploy-report.md`](report/phase-8-completion-test-deploy-report.md).

---

## Giai đoạn 9: Đơn giản hóa Kiến trúc Role — ✅ HOÀN THÀNH

Tách biệt minh bạch hai tầng phân quyền: tinh giản **Identity Role toàn cục** xuống 2 vai trò (`Admin` quản trị nền tảng và `User` cho mọi người dùng đăng nhập OAuth), giữ nguyên **Workspace Role** (`Admin` / `Manager` / `Member` trong `workspace_members.role`) làm lớp phân quyền thực tế cho toàn bộ tính năng nghiệp vụ.

**Yêu cầu hoàn thiện (DoD):**
- [x] Bảng `roles` chỉ giữ 2 bản ghi chuẩn: `Admin` và `User`.
- [x] Tài khoản mới đăng nhập qua OAuth tự động nhận Identity role `User`.
- [x] Tinh giản Policy còn `SystemAdminPolicy` cho quản trị hệ thống; mọi endpoint cấp workspace dùng `.RequireAuthorization()` kết hợp kiểm tra quyền tại tầng dịch vụ (`IWorkspaceAccess`).
- [x] Hoàn tất 2 migration `Phase9RoleSimplification` và `Phase9ModelSync` (nâng tổng số lên **8 migrations**), toàn bộ bộ kiểm thử đạt 100% PASS.

---

## Giai đoạn 10: Nâng cao Task & Workspace UX — ✅ HOÀN THÀNH

Hoàn thiện giao diện và kiểm tra hợp lệ cho các trường nâng cao của thẻ công việc (**Due Date**, **Priority**, **Description Markdown**), tách dịch vụ quản trị không gian làm việc (`WorkspaceService`) và bổ sung giao diện **Cài đặt Workspace** cùng **Nhật ký Hoạt động (Activity Log)**.

**Yêu cầu hoàn thiện (DoD):**
- [x] Hiển thị và cập nhật hạn chót (**Due Date** chuẩn hóa UTC, cảnh báo đỏ khi quá hạn) và mức độ ưu tiên (**Priority** `Low` / `Medium` / `High` / `Urgent`).
- [x] Hỗ trợ trình bày **Description** với cú pháp Markdown an toàn (không dùng `dangerouslySetInnerHTML`).
- [x] Hoàn thiện trang **Cài đặt Workspace** (cập nhật tên/mô tả, chuyển quyền sở hữu cho thành viên con người, xóa mềm workspace) và tính năng **Tạo không gian làm việc chủ động** (`POST /api/workspaces`).
- [x] Hoàn thiện trang **Nhật ký Hoạt động (Activity Log)** phân trang keyset cho cấp Quản lý.

> **Kết quả nghiệm thu:** **8 migrations** · Backend **226 tests PASS** · Frontend **279 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-10-advanced-task-workspace-ux.md`](tasks/phase-10-advanced-task-workspace-ux.md) · Bổ sung tạo Workspace [`tasks/handovers/workspace-creation-api-ui.md`](tasks/handovers/workspace-creation-api-ui.md) · Bàn giao FE [`tasks/handovers/phase-10-remaining-frontend-handover.md`](tasks/handovers/phase-10-remaining-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-10-advanced-task-workspace-ux-test-report.md`](report/phase-10-advanced-task-workspace-ux-test-report.md).

---

## Giai đoạn 11: Quản lý Thành viên, Email & Hồ sơ Cá nhân — ✅ HOÀN THÀNH

Xây dựng quy trình mời thành viên tham gia workspace qua email (sử dụng Resend API trên tên miền chính thức `teamnexus.cloud`), gửi email thông báo nhanh, quản lý danh sách thành viên, trang hồ sơ cá nhân và trung tâm thông báo (`Notification Center`) trên thanh điều hướng chung.

**Yêu cầu hoàn thiện (DoD):**
- [x] Bổ sung bảng `workspace_invitations` và `email_messages` (Migration #9 `Phase11MemberProfile`).
- [x] Hoàn thiện luồng mời thành viên qua email (lưu mã băm token `SHA-256`, hạn 7 ngày) và luồng chấp nhận lời mời tự động khôi phục sau bước đăng nhập OAuth.
- [x] Hỗ trợ Quản lý gửi email thông báo nhanh tới các thành viên trong workspace.
- [x] Quản lý danh sách thành viên: phân biệt rõ Người thật và AI Agent, hủy lời mời đang chờ, đổi vai trò hoặc xóa thành viên.
- [x] Trang Hồ sơ cá nhân (cập nhật tên hiển thị, ảnh đại diện, danh sách workspace tham gia, tự rời workspace) và Chuông thông báo hợp nhất tại `AppHeader`.

> **Kết quả nghiệm thu:** **9 migrations** · Backend **371 tests PASS** · Frontend **360 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-11-member-profile-management.md`](tasks/phase-11-member-profile-management.md) · Bàn giao FE [`tasks/handovers/phase-11-remaining-frontend-handover.md`](tasks/handovers/phase-11-remaining-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-11-member-profile-management-test-report.md`](report/phase-11-member-profile-management-test-report.md).

---

## Giai đoạn 12: Trang Tổng quan (Dashboard) & Tìm kiếm — ✅ HOÀN THÀNH

Xây dựng trang chủ tổng quan cho từng không gian làm việc (`WorkspaceDashboardPage`), công cụ tìm kiếm & lọc thẻ đa tiêu chí xuyên bảng phân trang keyset, và tính năng nhắc tên `@mention` an toàn trong bình luận.

**Yêu cầu hoàn thiện (DoD):**
- [x] **Workspace Dashboard** (`/workspaces/:workspaceId/dashboard`): thống kê 3 nhóm *"Công việc của tôi"* (Quá hạn, Sắp đến hạn, Mới giao), tóm tắt tiến độ các bảng, cảnh báo AI Observer chưa đọc và dòng hoạt động gần đây.
- [x] **Tìm kiếm & Lọc xuyên bảng** (`GET /api/workspaces/{id}/tasks/search`): lọc theo từ khóa, bảng, người phụ trách, nhiều nhãn kết hợp (`AND`), độ ưu tiên, khoảng hạn chót và trạng thái hoàn thành với phân trang keyset `(updated_at DESC, id DESC)`.
- [x] **`@mention` trong bình luận**: gửi danh sách định danh `mentionUserIds` tường minh từ giao diện `Mentions`, xác thực thành viên con người thuộc workspace và phát thông báo `CommentMention`.

> **Kết quả nghiệm thu:** **9 migrations** · Backend **423 tests PASS** · Frontend **406 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-12-dashboard-search.md`](tasks/phase-12-dashboard-search.md) · Bàn giao FE [`tasks/handovers/phase-12-remaining-frontend-handover.md`](tasks/handovers/phase-12-remaining-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-12-dashboard-search-test-report.md`](report/phase-12-dashboard-search-test-report.md).

---

## Giai đoạn 13: Trực quan hóa & Thông báo Chủ động — ✅ HOÀN THÀNH

Bổ sung chế độ xem Lịch tháng (**Calendar View**) ngay trên bảng Kanban, biểu đồ chuỗi thời gian tiến độ & vận tốc (**Burndown & Velocity**) trong trang Báo cáo, và dịch vụ gửi bản tin email tóm tắt công việc mỗi sáng (**Daily Email Digest**).

**Yêu cầu hoàn thiện (DoD):**
- [x] **Calendar View**: chuyển đổi mượt mà giữa Kanban và Lịch tháng trên `BoardView`, gom nhóm thẻ theo ngày hạn địa phương, làm nổi bật ô có thẻ trễ hạn và kết nối trực tiếp với `TaskDetailModal` cùng trang Tìm kiếm.
- [x] **Burndown & Velocity**: endpoint `GET /api/workspaces/{id}/reports/progress-series` tính toán chuỗi `openTasks`, `completions`, `creations` theo ngày hoặc theo tuần tôn trọng múi giờ người xem (`tzOffsetMinutes`), hiển thị biểu đồ trực quan trên `ReportsPage`.
- [x] **Daily Email Digest**: bổ sung cột `users.digest_enabled` (Migration #10 `Phase13DailyDigest`), tiến trình `DailyDigestRunner` tổng hợp công việc từ mọi workspace và đảm bảo chống gửi trùng tối đa 1 email/người/ngày ở tầng CSDL; cho phép bật/tắt trong trang Hồ sơ cá nhân.

> **Kết quả nghiệm thu:** **10 migrations** · Backend **480 tests PASS** · Frontend **508 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-13-visualization-proactive-notifications.md`](tasks/phase-13-visualization-proactive-notifications.md) · Bàn giao FE [`tasks/handovers/phase-13-remaining-frontend-handover.md`](tasks/handovers/phase-13-remaining-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-13-visualization-proactive-notifications-test-report.md`](report/phase-13-visualization-proactive-notifications-test-report.md).

---

## Giai đoạn 14: Nâng cao AI — ✅ HOÀN THÀNH

Đưa AI vào tương tác trực tiếp theo luồng thời gian thực trong ngữ cảnh thẻ công việc (**AI Task Chat qua SSE**), nâng cấp AI Observer thành công cụ dự báo sớm nguy cơ trễ hạn (**`AtRiskDeadline`**) kèm chỉ số **Sức khỏe Dự án (`Project Health Score`)**, và mở rộng Smart Setup thành **AI Board Template**.

**Yêu cầu hoàn thiện (DoD):**
- [x] **AI Task Chat (`POST /api/tasks/{id}/ai-chat/stream`):** truyền tải câu trả lời theo thời gian thực qua Server-Sent Events (`meta → delta* → done | error`), kiểm soát ngân sách lịch sử hội thoại và hỗ trợ lưu câu trả lời thành bình luận qua Lớp Giải trình (`Pending` `PostComment`).
- [x] **Dự báo rủi ro `AtRiskDeadline` & Điểm Sức khỏe Dự án:** phát hiện sớm các thẻ còn `< 20%` thời gian nhưng đứng yên `48 giờ`; tính toán tất định chỉ số `ProjectHealth.Compute` (thang `0–100`, 5 trọng số cộng đúng 100) hiển thị trên Workspace Dashboard.
- [x] **AI Board Template (`POST /api/workspaces/{id}/smart-setup/template`):** sinh đề xuất bảng mới (`2–6` cột kèm đúng 1 cột `is_done` và `5–10` thẻ), thực thi tạo bảng khi Quản lý phê duyệt (`CreateBoardFromTemplate`) và xóa mềm bảng khi Hoàn tác (`Undo`).

> **Kết quả nghiệm thu:** **10 migrations** · Backend **584 tests PASS** · Frontend **578 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-14-ai-advanced.md`](tasks/phase-14-ai-advanced.md) · Bàn giao FE [`tasks/handovers/phase-14-remaining-frontend-handover.md`](tasks/handovers/phase-14-remaining-frontend-handover.md) · Báo cáo nghiệm thu [`report/phase-14-ai-advanced-test-report.md`](report/phase-14-ai-advanced-test-report.md).

---

## Giai đoạn 14.5: Củng cố Nền tảng & AI Agent Chat cấp Board — ✅ HOÀN THÀNH

Gia cố nền tảng kiến trúc (lịch sử AI cấp workspace, điểm nối `IExternalAlertSink` cho thông báo ngoại vi, tiến trình dọn dẹp lượt chạy mồ côi) và xây dựng khung **AI Agent Chat cấp Bảng** neo bên phải Kanban, hỗ trợ kéo-thả đính kèm nhiều thẻ công việc, lưu trữ lịch sử hội thoại bền vững vào CSDL và trích xuất đề xuất hành động có cấu trúc.

**Yêu cầu hoàn thiện (DoD):**
- [x] Bổ sung bảng `agent_chat_messages` (Migration #11 `Phase14_5AgentChat`) kèm tiến trình lưu giữ 90 ngày theo lô (`AgentChatRetentionRunner`).
- [x] Bổ sung endpoint `GET /api/workspaces/{id}/ai-actions` (Manager+) và giao diện Lịch sử AI cấp Workspace; thiết lập cổng `IExternalAlertSink` tại `NotificationService`.
- [x] Xây dựng giao diện **AI Agent Chat panel** neo bên phải bảng Kanban, cho phép kéo-thả tối đa 8 thẻ công việc vào làm ngữ cảnh.
- [x] Hỗ trợ trích xuất và xác nhận 3 loại đề xuất hành động từ cuộc trò chuyện (`CreateSubtasks`, `UpdateTaskFields`, `MoveTaskToColumn`), toàn bộ đi qua quy trình `Pending → Approve → Undo` của Lớp Giải trình.

> **Kết quả nghiệm thu:** **11 migrations** · Backend **643 tests PASS** · Frontend **633 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-14-5-foundation-and-agent-chat.md`](tasks/phase-14-5-foundation-and-agent-chat.md) · Báo cáo nghiệm thu [`report/phase-14-5-foundation-and-agent-chat-test-report.md`](report/phase-14-5-foundation-and-agent-chat-test-report.md).

---

## Giai đoạn 14.6: Hoàn thiện Trải nghiệm & Chống rò rỉ Định danh — ✅ HOÀN THÀNH

Giải quyết triệt để các điểm ma sát trải nghiệm thực tế sau khi vận hành trên môi trường production: bổ sung bộ chọn đính kèm thẻ trực quan không cần kéo-thả, hợp nhất lưu trữ lịch sử Task Chat vào CSDL, tự động cung cấp bản tóm tắt bảng (*Lightweight Board Digest*) và thiết lập cơ chế lọc hai đầu chặn rò rỉ mã UUID/GUID ra văn bản hội thoại.

**Yêu cầu hoàn thiện (DoD):**
- [x] **Bộ chọn đính kèm thẻ trực quan:** nút *"Đính kèm thẻ"* (Popover tìm kiếm nhanh theo tiêu đề/người phụ trách) và mục *"Gửi vào AI Chat"* trên menu của từng `TaskCard`.
- [x] **Chặn rò rỉ định danh kỹ thuật (GUID / UUID):** kết hợp quy tắc prompt nghiêm ngặt cùng bộ lọc cưỡng chế ở cả máy chủ (`AiChatAnswerSanitizer`) và giao diện (`redactIdentifiers.ts`) mà không làm ảnh hưởng khối JSON đề xuất.
- [x] **Hợp nhất Transcript Task Chat:** lưu và nạp lịch sử hội thoại cấp thẻ qua cột `agent_chat_messages.task_id` (`GET /api/tasks/{taskId}/agent-chat/messages`).
- [x] **Lightweight Board Digest:** tự động cung cấp bản tóm tắt số lượng thẻ theo cột và các thẻ ưu tiên/sắp đến hạn khi người dùng hỏi tiến độ chung mà chưa đính kèm thẻ cụ thể.

> **Kết quả nghiệm thu:** **11 migrations** · Backend **665 tests PASS** · Frontend **657 tests PASS**.
> **Hồ sơ chi tiết:** Kế hoạch [`tasks/phase-14-6-ai-agent-enhancements.md`](tasks/phase-14-6-ai-agent-enhancements.md) · Báo cáo nghiệm thu [`report/phase-14-6-track-a-test-report.md`](report/phase-14-6-track-a-test-report.md).

---

## Giai đoạn 14.7: Autonomous Agent & Kiến trúc Hybrid AI với Jev — ✅ HOÀN THÀNH

Xây dựng nền tảng **Hybrid AI** kết hợp **System 1 (TypeSafe Jev 1.13 — mô hình quyết định có cấu trúc siêu tốc)** và **System 2 (DeepSeek LLM)**; đồng thời mở rộng toàn diện hệ sinh thái Autonomous Agent với **Đội ngũ Chuyên gia AI (Multi-Agent Personas)**, **7 công cụ ReAct** và **Bộ nhớ Dự án Ngữ nghĩa (Workspace RAG trên `pgvector`)**.

**Yêu cầu hoàn thiện (DoD):**
- [x] **Nền tảng Jev System 1 & Confidence Routing (Track A):** triển khai `IJevDecisionProvider` (`JevDecisionHttpProvider` + `FakeJevDecisionProvider`), bảng từ điển `workspace_taxonomies` (`domain` / `work_type`), hồ sơ kỹ năng thành viên (`skills`, `primary_role`, `capacity_weight`, `max_wip_limit` trên `workspace_members` — Migration #12 `Phase147AgentPlatform`) và bộ định tuyến ngưỡng tin cậy `ConfidenceRouting` (`≥ 0.85` `DirectAssign`, `0.50..0.85` `OfferChoices`, `< 0.50` `ManualRequired`).
- [x] **Auto-Triage & Smart Assignee Dispatching (Track A):** phân loại thẻ tự động (`POST /api/boards/{id}/tasks/triage`) và gợi ý người thực hiện phù hợp (`POST /api/boards/{id}/tasks/{taskId}/assignee-suggestions`) kèm giao diện `JevTriagePanel` và `AssigneeSuggestion`.
- [x] **AI Observer Severity Routing & Recovery Plan (Track A):** thẩm định mức độ nghiêm trọng qua Jev trước khi gọi LLM; tự động sinh đề xuất kịch bản phục hồi `RecoveryPlan` (`ReassignTask`, `SplitSubtasks`, `ExtendDeadline`, `EscalateManager`) ở trạng thái `Pending` để Quản lý duyệt hoặc hoàn tác.
- [x] **Đội ngũ AI Chuyên gia — Multi-Agent Personas (Track B §B1):** bổ sung cột `workspace_members.persona_key` và chỉ mục duy nhất theo từng persona (Migration #13 `Phase147AgentPersonas`), hỗ trợ 4 vai trò (`general`, `scrum_master`, `tech_lead`, `qa_security`) trên cả giao diện thành viên và khung Agent Chat (`?persona=`).
- [x] **Mở rộng 7 công cụ ReAct (Track B §B2):** bổ sung `InspectMemberWorkload`, `MatchSkills` và `ValidateAcceptanceCriteria` vào vòng lặp thực thi của Agent Executor, giữ nguyên giới hạn guardrail an toàn.
- [x] **Bộ nhớ Dự án Ngữ nghĩa — Workspace RAG với `pgvector` (Track B §B3):** kích hoạt extension `vector`, bổ sung bảng `workspace_knowledge_documents` và `workspace_knowledge_chunks` (`vector(1536)` + chỉ mục HNSW `vector_cosine_ops` — Migration #14 `Phase147KnowledgeBase`), kiểm duyệt nạp/xóa tài liệu qua `CreateKnowledgeDocument` của Lớp Giải trình, nhúng vector chạy nền (`KnowledgeEmbeddingRunner`) và tự động chèn ngữ cảnh `KIẾN THỨC DỰ ÁN` vào Agent Chat.

> **Kết quả nghiệm thu:** **14 migrations** · Backend **1.167 tests PASS** · Frontend **738 tests PASS** · Độ trễ thực tế Jev live P50 **773 ms**.
> **Hồ sơ chi tiết:** Kiến trúc [`06-adjust-and-upgrade-with-jev.md`](06-adjust-and-upgrade-with-jev.md) · Kế hoạch [`tasks/phase-14-7-autonomous-agent-jev.md`](tasks/phase-14-7-autonomous-agent-jev.md) · Nhật ký kiểm thử thực chiến [`tasks/handovers/phase-14-7-track-a-issues.md`](tasks/handovers/phase-14-7-track-a-issues.md), [`tasks/handovers/phase-14-7-track-b-issues.md`](tasks/handovers/phase-14-7-track-b-issues.md), [`tasks/handovers/phase-14-7-track-b-test-plan.md`](tasks/handovers/phase-14-7-track-b-test-plan.md) · Báo cáo nghiệm thu [`report/phase-14-7-track-b-test-report.md`](report/phase-14-7-track-b-test-report.md), [`report/phase-14-98-uiux-luminous-enterprise-upgrade-report.md`](report/phase-14-98-uiux-luminous-enterprise-upgrade-report.md), [`report/phase-14-99-uiux-comprehensive-audit-report.md`](report/phase-14-99-uiux-comprehensive-audit-report.md).

---

## Giai đoạn 15: Tích hợp Hệ sinh thái Bên ngoài (Slack, Discord, GitHub) — ✅ HOÀN THÀNH

Kết nối TeamNexus với hệ sinh thái công cụ phát triển phần mềm: đẩy cảnh báo ra ngoài sang **Slack** và **Discord** qua Transactional Outbox, và nhận sự kiện **GitHub Webhook** (Pull Request, Issue, Comment) để tự động hóa vòng đời thẻ công việc kèm khả năng hoàn tác đầy đủ qua Lớp Giải trình.

**Lộ trình triển khai qua 5 phân đoạn con:**
1. **15.1 — Nền tảng Schema & Mã công việc `TN-123`:** Thiết lập 6 bảng tích hợp mới (`task_numbers`, `workspace_webhook_endpoints`, `webhook_delivery_outbox`, `workspace_github_integrations`, `github_webhook_deliveries`, `task_external_links`) và 4 cột mở rộng (`users.is_system`, `workspaces.next_task_number`, `board_columns.is_in_review`, `tasks.task_number` — Migration #15 `Phase15ExternalIntegrations`) cùng bảng lưu vòng khóa `data_protection_keys` (Migration #16 `Phase15DataProtectionKeyRing`); cấp phát mã `TN-123` nguyên tử, hỗ trợ tìm kiếm theo mã và khởi tạo cổng `ITaskAutomation`.
2. **15.2 — Outbound Slack / Discord Webhooks & Transactional Outbox:** Triển khai `WebhookOutboxExternalAlertSink` trên cổng `IExternalAlertSink`, bộ kiểm tra URL chống SSRF (`WebhookUrlValidator`), định dạng bản tin Slack Block Kit & Discord Embed, tiến trình gửi tin nền `WebhookOutboxRunner` (backoff retry 5 bậc + circuit breaker) và 6 endpoint quản trị kênh webhook.
3. **15.3 — Inbound GitHub Webhooks & Tự động hóa Pull Request:** Triển khai quản trị cấu hình GitHub cấp workspace (mã hóa khóa bí mật bằng `IDataProtectionProvider`, chỉ hiển thị plaintext 1 lần), endpoint nhận webhook `POST /api/webhooks/github/{webhookToken:guid}` (xác thực chữ ký `HMAC-SHA256` bằng `FixedTimeEquals`, giới hạn body 256 KB, chống replay theo `delivery_id`), tự động gắn liên kết `TN-123` và chuyển thẻ sang cột *Đang duyệt PR (`is_in_review`)* khi mở PR hoặc sang cột *Hoàn thành (`is_done`)* khi merge PR kèm ghi vết hoàn tác `GitHubPrMoveTask`.
4. **15.4 — Đồng bộ GitHub Issue (+ Jev Auto-Triage) & Bình luận 1 chiều:** Tự động tạo thẻ công việc từ sự kiện `issues` khớp bộ lọc nhãn dưới danh nghĩa `GitHub Bot`, gọi Jev 1.13 phân loại tự động (`TriageForSystemAsync` fail-soft), ghi vết hoàn tác `GitHubIssueCreateTask`, và đồng bộ bình luận một chiều từ `issue_comment.created`.
5. **15.5 — Giao diện Quản trị Tích hợp & Nghiệm thu E2E:** Hiển thị huy hiệu `TN-123` và chip trạng thái PR/Issue trên thẻ Kanban, cửa sổ chi tiết và kết quả tìm kiếm; bổ sung tùy chọn cột *Đang duyệt PR* và xây dựng tab **Tích hợp (Slack / Discord / GitHub)** trong trang Cài đặt Workspace.

**Yêu cầu hoàn thiện (DoD):**
- [x] Cấu hình và gửi thông báo thành công sang Slack / Discord theo danh mục sự kiện với hàng đợi Outbox bền vững, hỗ trợ gửi tin kiểm thử và tra cứu lịch sử gửi.
- [x] Cấu hình tích hợp GitHub, tự động nhận diện mã `TN-123`, tự động chuyển trạng thái thẻ khi mở/merge PR, tự động tạo thẻ từ Issue kèm Jev Triage và đồng bộ bình luận một chiều.
- [x] Xác thực chữ ký `HMAC-SHA256` trên raw body, chống phát lại theo `delivery_id`, chống SSRF và bảo vệ khóa bí mật at-rest bằng Data Protection (key ring lưu trên PostgreSQL).
- [x] Mọi tự động hóa thay đổi dữ liệu từ GitHub đều lưu vết vào `ai_action_logs` và hoàn tác (`Undo`) được 100%.
- [x] Đảm bảo suy giảm an toàn (Graceful Degradation) khi workspace chưa cấu hình tích hợp.

> **Kết quả nghiệm thu:** **16 migrations** (35 bảng vật lý) · Backend **1.346 tests PASS** (0 fail, 0 skip) · Frontend **799 tests PASS** (129 test files).
> **Hồ sơ chi tiết:** Thiết kế [`07-external-integrations-design.md`](07-external-integrations-design.md) · Kế hoạch [`tasks/phase-15-0-external-integrations-master-plan.md`](tasks/phase-15-0-external-integrations-master-plan.md) (`phase-15-1..15-5.md`) · Bàn giao [`tasks/handovers/phase-15-remaining-frontend-handover.md`](tasks/handovers/phase-15-remaining-frontend-handover.md) · Báo cáo review [`report/reviews/phase-15-3-15-4-code-review-and-remediation-plan.md`](report/reviews/phase-15-3-15-4-code-review-and-remediation-plan.md), [`report/reviews/phase-15-5-code-review-and-remediation-plan.md`](report/reviews/phase-15-5-code-review-and-remediation-plan.md) · Báo cáo nghiệm thu [`report/phase-15-external-integrations-test-report.md`](report/phase-15-external-integrations-test-report.md) & [`report/phase-14-15-acceptance-test-and-remediation-report.md`](report/phase-14-15-acceptance-test-and-remediation-report.md).

---

## Giai đoạn 16: Ứng dụng Di động Đa nền tảng (Flutter) — ⏳ CHƯA BẮT ĐẦU

Xây dựng ứng dụng di động đa nền tảng (iOS & Android) bằng **Flutter** tái sử dụng chung toàn bộ hệ thống REST API và hợp đồng DTO append-only hiện có, tích hợp `signalr_netcore` để đồng bộ thời gian thực và kiểm thử trên thiết bị thật.

**Yêu cầu hoàn thiện (DoD):**
- [ ] Ứng dụng Flutter kết nối và xác thực an toàn với Backend API (OAuth/JWT + lưu trữ token mã hóa qua `flutter_secure_storage`).
- [ ] Hoàn thiện các màn hình cốt lõi tối ưu cho thao tác chạm – vuốt: Bảng Kanban, Chi tiết thẻ, Trang tổng quan (Dashboard), Trung tâm thông báo và Màn hình phê duyệt/hoàn tác đề xuất AI.
- [ ] Đồng bộ trạng thái thời gian thực hai chiều giữa Web và Mobile qua `signalr_netcore` (`BoardHub`).
- [ ] Kiểm thử vận hành ổn định trên thiết bị di động thực tế.
