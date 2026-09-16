# Giai đoạn 13 — Trực quan hóa & Thông báo Chủ động (Kế hoạch chia task)

> **Trạng thái thi hành:** ✅ **ĐÃ HOÀN THÀNH & VERIFY ĐẦY ĐỦ (Backend + Frontend + CI + Tài liệu).**
> **Nguồn:** `Project-Documents/03-roadmap.md` → *Giai đoạn 13: Trực quan hóa & Thông báo Chủ động* (3 ô A/B/C).
> **Tiền đề đã merge:** Giai đoạn 7 · 8 · 9 · 10 · 11 · **12 (Dashboard & Tìm kiếm)**.
> **📤 Note bàn giao Frontend** (đã hiện thực xong; giữ làm hồ sơ hợp đồng API + bẫy đã biết):
> `tasks/phase-13-remaining-frontend-handover.md`.
> **Báo cáo nghiệm thu:** `report/phase-13-visualization-proactive-notifications-test-report.md`.
>
> **Kết quả ĐO THẬT (số đo thắng tài liệu):**
> - `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` → ✅ **0 Warning / 0 Error**
> - `dotnet test` (PostgreSQL thật) → ✅ **Failed 0 / Passed 480 / Skipped 0 / Total 480** (baseline **423** ⇒ **+57**)
> - `dotnet ef migrations list` → ✅ **10** · `has-pending-model-changes` → ✅ **sạch**
> - `frontend/ npm test` → ✅ **Failed 0 / Passed 508 / Total 508 (86 file)** (baseline **407 / 77 file** ⇒ **+101 / +9**)
> - `frontend/ npm run lint` → ✅ **0 warning / 0 error** (212 file) · `npx tsc -b` → ✅ **exit 0** · `npm run build` → ✅ **thành công**
> - CI: `ci-backend.yml` `423` → **`480`** · `ci-web.yml` `406` → **`508`**
>
> **⬆️ Lệch so với ước tính trong tài liệu này:** kế hoạch ước **+39 backend / +53 frontend**; đo thật **+57 / +101**.
> Nguyên nhân: ước tính đếm theo *test method*, thực tế nhiều test có nhiều `[Theory]`/`it(...)`. **Số đo thắng tài liệu** (đúng như **R9** đã dự liệu).
>
> **Schema:** ⚠️ **KHÁC roadmap — có ĐÚNG 1 migration additive.** Thêm **1 cột mới** `users.digest_enabled`
> (`bool NOT NULL DEFAULT true`) ⇒ tổng **10** migration. **Không** bảng mới, **không** cột mới ở bảng nào khác,
> **không** sửa/xoá cột đang tồn tại. Lý do bắt buộc ở **D2** — roadmap ghi *"không cần migration mới"*, nhưng
> yêu cầu *"người dùng có thể bật/tắt trong Profile Settings"* **không thể** thoả mãn bằng một cờ ở client
> (`localStorage`) vì `BackgroundService` chạy phía server phải **đọc** được lựa chọn đó.
> Sau migration: `dotnet ef migrations has-pending-model-changes` phải trả
> *"No changes have been made to the model since the last migration."*

> **Baseline ĐO THẬT tại phiên lập kế hoạch (số đo thắng tài liệu):**
> - `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` → ✅ **0 Warning(s) / 0 Error(s)** (22.3 s)
> - `dotnet test` **có PostgreSQL thật** → ✅ **Failed 0 / Passed 423 / Skipped 0 / Total 423** (3 m 13 s)
> - `dotnet ef migrations list --no-build` → ✅ **9** (mới nhất `20260914105025_Phase11MemberProfile`)
> - `frontend/ npm test -- --reporter=dot` → ✅ **77 file / 407 test PASS / 0 fail** (211.9 s)
> - `frontend/ npx tsc -b` → ✅ **exit 0** · file test **77**
>
> **⬆️ Lệch nhỏ so với tài liệu Giai đoạn 12 (đã ghi nhận, không phải lỗi):** `ci-web.yml` ghi `≥ 406` và
> `report/phase-12-*` ghi **406**; máy dev đo **407** (1 test được thêm sau khi chốt báo cáo). **Số đo thắng
> tài liệu** ⇒ baseline frontend của Giai đoạn 13 là **407**. Cổng CI §7.2 sau khi hiện thực đã nâng thẳng
> lên **`508`** (tổng đo thật), không phải mức dự kiến ban đầu.

---

## 0. Mục tiêu & 3 ô hoàn thiện

Xem task theo chiều thời gian (Calendar), biểu đồ tiến độ trực quan trong app (Burndown/Velocity), và
email digest tóm tắt hằng ngày cho từng thành viên — tất cả trên dữ liệu đã có
(`due_date`, `completed_at`, `activity_logs`, Resend API).

| # | Yêu cầu roadmap (nguyên văn) | Trạng thái đầu kỳ (đã khảo sát code) | Việc phải làm |
|---|---|---|---|
| **A** | **Calendar View**: tab "Lịch" trong `BoardView` hiển thị task theo ngày bằng antd `Calendar`; click ngày mở `/search?dueFrom=&dueTo=` — *"không cần API mới"* | **Chưa có gì.** `BoardView.tsx` **không** có cơ chế chuyển view (chỉ 1 khối Kanban + `DndContext`, dòng 596–678). `TaskResponse` đã có `dueDate: string \| null` (`board.types.ts` 30). antd **6.6.3 đã export `Calendar`** (`node_modules/antd/es/calendar/index.d.ts`) và `dayjs` đã có sẵn. `TaskSearchPage` **đã** đọc `dueFrom`/`dueTo` từ URL → `searchApi` → backend | **§1** (frontend, **thuần client — không API mới**, đúng roadmap) |
| **B** | **Burndown Chart & Velocity**: biểu đồ open/done **theo tuần** trong `ReportsPage` (tính từ `completed_at` + `activity_logs`); *"không thêm thư viện npm ngoài (antd `Statistic`/`Progress` đủ dùng)"* | **Chưa có gì.** `GET .../reports/summary` (`ReportSummary`) trả **số tổng hợp tại một thời điểm** + `performance.throughputPerWeek` — **không có chuỗi thời gian** nào. `ReportPerformance` chỉ có `completedInRange`/`throughputPerWeek` (vô hướng). UI báo cáo (`ReportSummaryPanel`) **không** vẽ biểu đồ | **§2** (backend **+1 endpoint**, xem **D5**) + **§5** (frontend) |
| **C** | **Email Digest hàng ngày**: `BackgroundService` mới chạy mỗi sáng, gọi `DashboardService` lấy "task của tôi", gửi qua `EmailGateway` (Resend) — tận dụng template Phase 11; **người dùng có thể bật/tắt trong Profile Settings** | **Chưa có gì.** `IDashboardService.GetAsync` + `DashboardResponse` **đã xong** (`DashboardService.cs`) và dùng lại được. `IEmailGateway` + `EmailTemplates` + `IEmailDispatcher` + `email_messages` **đã xong** (Phase 11), gồm cả `NullEmailSender` fallback. `HostedService` **đã có 2 tiền lệ** (`ObserverBackgroundService`, `AgentRunReaper`). **Thiếu:** cột lưu lựa chọn opt-out + service điều phối + template digest + UI toggle | **§3** (migration + backend) + **§6** (frontend) |
| **D** | *"Toàn bộ test vẫn xanh; **không migration mới**"* | — | **§7** (DoD/CI/tài liệu) + ghi nhận **lệch DoD có chủ ý ở D2** |

**Ngoài 3 ô, 5 hạng mục kỹ thuật bắt buộc phát sinh (phát hiện khi khảo sát code):**

- **E** — **`BoardView` không có cơ chế chuyển view.** Thêm tab "Lịch" **không được** đổi mặc định render
  (Kanban) vì `BoardView.test.tsx` có **11 test** assert text ngay khi mount (`Deploy to Staging`, `Đã kết nối`, …) — **đã đếm thật** trong file test.
  ⇒ dùng `Segmented` + render có điều kiện, **Kanban là mặc định**; **không** dùng `Tabs` (antd `Tabs` render
  children theo `destroyOnHidden`, dễ làm đỏ test mount). Xem **D10**.
- **F** — **Burndown cần chuỗi thời gian, API hiện tại không có.** `ReportSummary` là ảnh chụp *một thời điểm*;
  `activity_logs` **không** lưu trạng thái cột nên không thể tái dựng lịch sử open/closed ⇒ **không thể** vẽ
  burndown chỉ bằng client. Phát sinh **ĐÚNG 1 endpoint read-only mới** `GET /api/workspaces/{id}/reports/progress-series`
  (Manager+, cùng quyền với `/summary`) — xem **D5**.
- **G** — **Idempotency của digest phải ở tầng dữ liệu, không ở bộ nhớ tiến trình.** Trên Render free-tier tiến
  trình ngủ/thức lại nhiều lần trong ngày ⇒ bộ đếm in-memory sẽ gửi trùng. Dùng chính `email_messages`
  (đã có) làm nguồn chống trùng: `kind = 'DailyDigest'` + `to_email = <email>` + ngày UTC của người nhận ⇒
  **không** thêm cột `last_sent_at` (xem **D3**).
- **H** — **`email_messages` không lưu cột người nhận dạng Guid.** Bảng chỉ có `to_email`. Nguồn chống trùng
  ở **G** vì vậy phải khoá theo **email**, không theo `user_id`. Chấp nhận: email là UQ trên `users`.
- **I** — **Digest gửi cho mọi user có `digest_enabled` ⇒ không được dùng chung hạn mức
  `WorkspaceEmailOptions.MaxEmailsPerHourPerWorkspace`** (100/giờ). Một workspace 100 thành viên + gửi hàng
  loạt sẽ tự khoá. Digest có hạn mức **riêng**, cấu hình ở section `Digest` (xem **D8**).

---

## 1. Baseline & bối cảnh đã khảo sát

### 1.1 Đã có sẵn — **KHÔNG làm lại** (bằng chứng repo, đã đọc file)

| Hạng mục | Bằng chứng (file · dòng) |
|---|---|
| `tasks` có `due_date` / `completed_at` / `created_at` / `assignee_id` / `priority` + query filter ẩn task của board soft-deleted | `Persistence/Data/Entities/BoardTask.cs` · `Configurations/BoardTaskConfiguration.cs` 57–58 |
| `board_columns.is_done` (nguồn duy nhất để biết task đã xong) | `Migrations/20260909122823_Phase2BoardColumnIsDone.cs` · `ReportAggregator.IsDone` 327 |
| `activity_logs` + IX `(workspace_id, created_at)`, không soft-delete | `Entities/ActivityLog.cs` · `Migrations/20260910105154_Phase5AiObserverSchema.cs` |
| **`IDashboardService.GetAsync(workspaceId, userId, dueSoonDays, take, ct)`** trả "task của tôi" 3 bucket + `summary` + `boards` | `Board/Services/DashboardService.cs` 23–152 |
| Bất biến `IsDone` = cột `is_done` **HOẶC** `completed_at != null`; `IsOverdue` = `!IsDone && dueDate < now` (`dueDate == now` **không** quá hạn) — đã khớp `ReportAggregator` | `DashboardService.cs` 336–360 · `ReportAggregator.cs` 327–337 |
| `TimeProvider` đã đăng ký (`TryAddSingleton(TimeProvider.System)`) + `FixedTimeProvider` cho test | `Board/BoardModule.cs` 40 · `tests/…/Infrastructure/FixedTimeProvider.cs` |
| `IReportService.GetSummaryAsync` (**Manager+**, workspace lạ ⇒ 404) + `ReportAggregator.Build` (hàm **thuần**) | `Reporting/Services/IReportService.cs` 18–58 · `ReportAggregator.cs` 20–64 |
| `ReportsOptions.MaxRangeDays` / `DefaultRangeDays` / `EffectiveRange(from,to,now)` | `Reporting/Options/ReportsOptions.cs` |
| Endpoint báo cáo: `MapGroup("/api/workspaces/{workspaceId:guid}/reports")` + `/summary` `/export` `/boards`, `RequireAuthorization` + `DomainExceptionFilter` | `Reporting/Endpoints/ReportingEndpoints.cs` 28–34 |
| `IEmailGateway` (port **của Board**) + `NullEmailGateway`; Ai ghi đè bằng `EmailGateway` | `Board/Services/IEmailGateway.cs` · `Ai/Services/EmailGateway.cs` · `BoardModule.cs` 59 · `AiModule.cs` 132 |
| `IEmailDispatcher.SendAsync(workspaceId, sentBy, kind, toEmail, subject, text, html, preview, ct)` — **cổng duy nhất**, ghi `email_messages`, **không bao giờ ném** | `Ai/Services/Email/EmailDispatcher.cs` 12–36 |
| `EmailTemplates`: hàm **THUẦN**, `HtmlEncoder.Create(UnicodeRanges.All)` (P5 giữ dấu tiếng Việt), `EncodeMultiline` (P6 ra `<br />`), `Truncate(subject, 200)` | `Ai/Services/Email/EmailTemplates.cs` 32–193 |
| `EmailOptions` (`ApiKey` rỗng ⇒ `NullEmailSender`) + `EmailKinds` (`WorkspaceInvitation`, `QuickEmail`) | `Ai/Options/EmailOptions.cs` · `EmailTemplates.cs` 10–17 |
| `HostedService` **2 tiền lệ**: `ObserverBackgroundService` (PeriodicTimer + bọc try/catch mọi tick) và `AgentRunReaper` (one-shot + `StartupDelay` + swallow) | `Ai/Services/ObserverBackgroundService.cs` · `Ai/Services/Agent/AgentRunReaper.cs` |
| `email_messages` append-only, **không** soft-delete/query filter, `Kind` varchar(40), `Subject` 200, `BodyPreview` 500 | `Entities/EmailMessage.cs` · `Configurations/EmailMessageConfiguration.cs` |
| `users` **không** có query filter; thêm cột **không** phát sinh warning | `Configurations/ApplicationUserConfiguration.cs` |
| `TestScenario.ResetDatabaseAsync` **đã** TRUNCATE `"users"` ⇒ cột mới **không** cần sửa harness | `tests/…/Infrastructure/TestScenario.cs` 108–132 |
| `TestScenario.CreateScenarioAsync(scriptedAi, clock)` / `CreateClockScenarioAsync(clock)` + `TeamNexusApiFactory.Clock` | `tests/…/Infrastructure/DatabaseFixture.cs` 182–238 |
| FE: `TaskSearchPage` đọc `dueFrom`/`dueTo` từ URL → `searchApi.searchTasks` → `?dueFrom=&dueTo=` | `features/search/pages/TaskSearchPage.tsx` 24–50, 77–78 · `services/searchApi.ts` 33–38 |
| FE: key `dueFrom`/`dueTo` trong `TaskSearchFilters` + đã validate `dueFrom > dueTo ⇒ 400` ở backend | `features/search/types/search.types.ts` · `TaskSearchEndpoints` |
| FE: `ReportsPage` đồng bộ filter lên **URL** qua `useSearchParams` (`replace: true`) | `features/reporting/pages/ReportsPage.tsx` 31–59 |
| FE: `AppHeader` (`title`·`children`·`bell`·`dropdown`) + `useWorkspaceRole` + `WorkspaceDashboardPage` là mẫu trang workspace | `shared/components/AppHeader.tsx` · `features/dashboard/pages/WorkspaceDashboardPage.tsx` |
| FE: `ProfilePage` dùng `Tabs` + `Form` + `Tabs` key `profile`/`workspaces` | `features/profile/pages/ProfilePage.tsx` 125–347 |
| FE: `taskDueDate.ts` — `isOverdue` (so theo **ngày**) · `overdueDays` · `dueDateLabel`; map màu priority của `TaskCard` | `features/board/utils/taskDueDate.ts` · `features/board/components/TaskCard.tsx` |
| FE: pattern hook = **useState + useEffect + reload**, **không** react-query | `features/reporting/hooks/useReportSummary.ts` · `useDashboard.ts` |
| FE: antd **6.6.3** có `Calendar` (`cellRender`/`fullCellRender`, `CellRenderInfo`) + `Segmented`; `dayjs` 1.11.23 | `frontend/node_modules/antd/es/calendar/generateCalendar.d.ts` 44–60 · `package.json` |

### 1.2 Baseline **đo tại máy dev** (phiên lập kế hoạch)

```
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental  → 0 Warning(s) / 0 Error(s)   (22.3 s)
dotnet test (PostgreSQL thật)                               → Failed 0 / Passed 423 / Skipped 0 / Total 423  (3 m 13 s)
dotnet ef migrations list --no-build                        → 9 (mới nhất 20260914105025_Phase11MemberProfile)
frontend/ npm test -- --reporter=dot                        → 77 file / 407 passed / 0 failed  (211.9 s)
frontend/ npx tsc -b                                        → exit 0
frontend/ số file *.test.ts(x)                              → 77
```

> ✅ **PostgreSQL thật ĐANG chạy trên máy dev (cổng 5432) — KHÁC Giai đoạn 12 (khi đó mật khẩu `postgres`
> không đúng nên 237 test bị SKIP).** Lần này mật khẩu thật lấy từ biến môi trường có sẵn trong máy
> (`ConnectionStrings__DigitalOps`), dùng được cho `TeamNexus_Test` ⇒ **`Skipped: 0` đã đạt**.
> **Docker daemon KHÔNG chạy** trên máy này (khác Giai đoạn 12) ⇒ cổng 5433 không có; **không** dựng container.
>
> **Cách chạy đủ bộ 100% test backend (bắt buộc trước khi push):**
> ```powershell
> $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=Thinh@12345"
> cd E:\TeamNexus
> dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
> ```
> **Điều kiện chốt: `Skipped: 0`.** Nếu `total` lệch **423** ⇒ **số đo thắng tài liệu**: cập nhật §1.2 + §7.1
> **và** cổng CI §7.2.

---

## 2. Bảng quyết định kiến trúc (D1–D14) — chốt sẵn, không chọn lại

| # | Quyết định | Lý do / ràng buộc |
|---|---|---|
| **D1** | **Tuân thủ tuyệt đối với "không thêm thư viện npm / NuGet"**: Calendar dùng `antd` `Calendar`; biểu đồ burndown/velocity vẽ bằng **`div` + `Progress`/`Statistic`/`Tooltip` sẵn có** (không `recharts`/`echarts`/`@ant-design/charts`) | Hợp đồng "⛔ KHÔNG thêm package" của Phase 10 §10 / 11 D13 / 12 §9, và roadmap ghi rõ *"không thêm thư viện npm ngoài (antd `Statistic`/`Progress` đủ dùng)"*. Biểu đồ cột bằng CSS là đủ cho 2 chỉ số đơn giản |
| **D2** | ⚠️ **CÓ đúng 1 migration additive**: `Phase13DailyDigest` → `users.digest_enabled` (**bool NOT NULL DEFAULT true**). **Ghi rõ đây là lệch roadmap** (*"không migration mới"*) và lệch là **bắt buộc**, không phải lựa chọn | Yêu cầu C nói *"người dùng có thể bật/tắt trong Profile Settings"*. Digest do `BackgroundService` chạy server-side ⇒ lựa chọn **phải** đọc được từ DB. `localStorage`/Zustand chỉ nằm ở browser ⇒ **không** thoả mãn. **Không** có cột nào tái dùng được (`users` chỉ có `display_name`/`avatar_url`/`created_at`). Default `true` ⇒ user hiện tại **không** đổi hành vi (row cũ nhận `true`), và **không** cần backfill. Cột **append-only** ở bảng `users` — **không** sửa/xoá cột nào, **không** bảng mới ⇒ rủi ro thấp nhất trong các lựa chọn còn lại |
| **D3** | **KHÔNG** thêm cột `last_digest_sent_at`. Idempotency chống trùng dùng `email_messages`: `kind = EmailKinds.DailyDigest` + `to_email = <email>` + `CreatedAt >= <mốc nửa đêm UTC của ngày hôm nay>`. Nếu **một** lần gửi đã tồn tại cho `(to_email, hôm nay)` ⇒ **bỏ qua user đó** | Đúng nguyên tắc "chỉ thêm cột khi không còn đường nào khác" (Phase 10 §0 / 12 D1). `email_messages` **đã** là append-only + evidence + đã có IX `(workspace_id, created_at)`; thêm cột thứ hai vào `users` chỉ để lưu 1 timestamp là lãng phí schema cho một tính năng *best-effort*. `to_email` là UQ trên `users` nên khoá theo email **không** gây trùng người |
| **D4** | **Digest là 1 email / user / ngày, gộp MỌI workspace** người đó tham gia (không 1 email/workspace). `email_messages.workspace_id` ghi **workspace đầu tiên** trong danh sách | Tránh N email/ngày cho người tham gia nhiều workspace; `email_messages.workspace_id` là **NOT NULL** nên phải chọn một giá trị — chọn cái đầu theo `JoinedAt ASC` (tất định) và ghi rõ trong doc comment. Danh sách workspace lấy qua `workspace_members` (không dùng `UserProfileService.ListMyWorkspacesAsync` để **không** phụ thuộc vòng) |
| **D5** | **+1 endpoint read-only**: `GET /api/workspaces/{workspaceId:guid}/reports/progress-series?from=&to=&boardId=&tzOffsetMinutes=` — **Manager+**, cùng quyền + cùng `DomainExceptionFilter` như `/summary` | Burndown **không thể** tính ở client (F). Đặt trong module **Reporting** vì: (a) cùng module quyền Manager+, (b) `ReportService` đã là *nơi duy nhất Reporting chạm DB* (IReportService §3), (c) **không** phụ thuộc module Ai (giữ chiều `Ai → Board`, `Reporting → Board`). Response trả **chuỗi bucket theo ngày**, tính bằng **hàm thuần** `ReportAggregator.BuildProgressSeries` để test không cần DB |
| **D6** | **Bucket = NGÀY theo múi giờ của người xem**, qua tham số `tzOffsetMinutes` (mặc định **0 = UTC**); clamp `[-840, 840]`. **`create`/`complete` gán vào ngày theo giờ địa phương**; `openTasks` là **số nguyên không đổi trong ngày** (không trừ `completions`), để `openTasks - completions` = *đường lý tưởng* và **không** phát sinh số âm | "Tuần/tháng" trong UI người Việt là theo lịch địa phương; nếu khoá theo UTC thì task tạo 07:00 sáng VN (= 00:00 UTC) sẽ rơi vào ngày **hôm trước**. `tzOffsetMinutes` là **knob UI** (không 400 khi lệch — clamp), đúng tiền lệ `ClampTake`/`Clamp(days)`. Ghi rõ: task xong **trước** `from` vẫn nằm trong `openTasks` của ngày đầu (nhất quán với cách `ReportProgress.Done` giữ task xong trước cửa sổ) |
| **D7** | `tzOffsetMinutes` lấy từ `new Date().getTimezoneOffset()` **đổi dấu** ở client; mọi so sánh ngày dùng **`dayjs` với offset** (không `toISOString()` cho day-key) | `getTimezoneOffset()` trả **UTC − local** (VN = `-420`) ⇒ server cần `+420`. Đảo dấu ở **một** helper thuần (`timeZoneOffsetMinutes()`) + có test ⇒ không rải `-` khắp code |
| **D8** | Section cấu hình **mới `Digest`** (`DigestOptions`): `Enabled` (mặc định **false** để CI/dev không gửi mail), `SendAtLocalHour` (8) / `SendAtLocalMinute` (0), `DefaultTimeZoneOffsetMinutes` (420 = UTC+7), `StartupDelaySeconds` (120), `MaxUsersPerRun` (200), `MaxWorkspacesPerUser` (10), `MaxItemsPerBucket` (5), `MaxDailyEmails` (500) | Đúng tiền lệ `ObserverOptions`/`AgentOptions` (POCO + clamp, không `ValidateOnStart`). Mặc định `Enabled = false`: giống triết lý `Agent:Enabled`, và CI **không bao giờ** gửi mail thật (thêm lớp bảo vệ ngoài `NullEmailSender`). `MaxDailyEmails` là **hạn mức riêng** (hạng mục **I**) — digest **không** đi qua rate-limit QuickEmail |
| **D9** | `DigestOptions` **cố ý KHÔNG thêm cột lưu timezone cho user** ⇒ dùng `DefaultTimeZoneOffsetMinutes` cho mọi user | Roadmap không yêu cầu cấu hình timezone riêng; thêm cột đó là **cột thứ hai** vào `users` không có yêu cầu. App phục vụ nhóm dùng UTC+7 ⇒ một giá trị toàn cục là đủ. Ghi thành **nợ kỹ thuật §11 R4** |
| **D10** | Chuyển view ở `BoardView` bằng **`Segmented`** (`data-testid="board-view-switch"`, giá trị `kanban` \| `calendar`), **render có điều kiện**, mặc định **`kanban`**; **KHÔNG** dùng `Tabs` | `BoardView.test.tsx` có **11 test** assert text khi mount + 1 test giả lập tab-like flow. antd `Tabs` không render children của tab inactive (thay đổi mount behavior) ⇒ rủi ro đỏ test. `Segmented` + `{view === 'kanban' ? <KanbanCanvas/> : <TaskCalendar/>}` giữ **nguyên** cây render mặc định. Đúng tinh thần R3 Phase 12 ("chạy `npm test` trước và sau; nếu buộc sửa ⇒ ghi báo cáo") |
| **D11** | **Calendar dùng dữ liệu board đã có, KHÔNG gọi API mới.** Nguồn: `tasksByColumn` của `useBoard`. **Tôn trọng** `searchQuery` + `priorityFilter` hiện có của `BoardView` (dùng lại `filteredTasksByColumn`) | Roadmap ghi rõ *"không cần API mới"*. Task **không có** `dueDate` ⇒ hiện ở khối phụ *"Không có hạn chót"* (không làm rối lưới tháng). `TaskResponse` đã đủ field (`title`/`dueDate`/`priority`/`isDone`/`assigneeName`) |
| **D12** | Mọi tính toán lịch/chuỗi thời gian của FE nằm trong **hàm THUẦN** ở `utils/` (không I/O): `boardCalendar.ts` (`groupTasksByDueDate`, `buildDayTasks`), `burndown.ts` (`toWeeklyBuckets`, `scaleBar`), `timeZoneOffsetMinutes.ts` | Đúng tiền lệ `taskDueDate.ts` / `mentionUtils.ts` / `reportFormat.ts`, và đúng lý do Phase 12 P2/P13: giá trị **tất định** mới test được biên mà không flaky theo đồng hồ |
| **D13** | **KHÔNG** sửa `UserProfileResponse`/`UpdateProfileRequest` bằng cách **thêm field giữa**: append `DigestEnabled` ở **CUỐI** record; mọi caller/JSON cũ vẫn compile và vẫn deserialize | `ProfileApiTests` hiện có gọi `GET/PUT /api/users/me` bằng JSON **không** có field mới ⇒ nếu đặt field ở giữa và **bắt buộc**, model binder trả `false` cho field thiếu ⇒ **vô tình tắt digest** của user. Vì vậy: `PUT` thiếu `DigestEnabled` (`null`) ⇒ **giữ nguyên** giá trị hiện tại (không phải `false`), và có test chứng minh (**PE-3**). Response **append cuối** ⇒ additive |
| **D14** | Vai trò port/adaptor cho digest: **Board** sở hữu dữ liệu + port `IDailyDigestService` + `NullDailyDigestService`; **Ai** sở hữu lịch gửi + template + `DigestGateway` gọi `IEmailDispatcher`. **Port cũ `IEmailGateway` được giữ nguyên** (không thêm method vào nó) | Bảo toàn hợp đồng Phase 11 đã verify: `IEmailGateway` có **3** điểm cài đặt/caller (`EmailGateway`, `NullEmailGateway`, `InvitationService`, `QuickEmailService`) — thêm method buộc sửa cả 3 và phình interface. Tạo **port mới** `IDailyDigestService` chỉ **1** người gọi (digest service) ⇒ bề mặt nhỏ nhất, và Board vẫn tự đứng được nếu thiếu Ai (đúng tiền lệ `IActivityLogWriter`/`IAiAgentResolver`/`INotificationWriter`) |

### 2.1 Phát sinh **bắt buộc** khi hiện thực (ghi lại để không bị coi là sai lệch kế hoạch)

| # | Phát sinh | Vì sao |
|---|---|---|
| **P1** | `EmailTemplates` += `DailyDigest(...)` (hàm **thuần**) + `EmailKinds.DailyDigest` | Giữ đúng bất biến Phase 11 §2.4: mọi template là hàm thuần ⇒ `Pure/DigestTemplateTests` chạy **không** DB/mail. Tái dùng `HtmlTemplate`, `Encoder`, `EncodeMultiline`, `Truncate` |
| **P2** | `ReportAggregator` += `BuildProgressSeries(snapshot, thresholds, tzOffsetMinutes)` (hàm **thuần**) | Cùng lý do `Build` hiện tại: test biên thời gian/`tzOffset` chạy không DB/HTTP. `ReportService` chỉ chiếu dữ liệu rồi gọi hàm này |
| **P3** | `DigestOptions` đăng ký ở `AiModule` + **1 dòng log** cấu hình (không chứa secret); `AddHostedService<DailyDigestBackgroundService>()` | Đúng pattern 3 lần đã có (`Observer`, `Agent`, `Email`). Log 1 dòng để biết ngay digest có bật hay không |
| **P4** | `TeamNexusApiFactory` cần `Digest:Enabled` = `false` (tường minh) trong `UseSetting` | Cùng lý do `Email:ApiKey` bị ép rỗng ở Phase 11 §2.2: key thật trong User Secrets **không** được khiến test gửi mail thật |
| **P5** | `DailyDigestBackgroundService` **không** ném: bọc `try/catch` từng user + từng tick; ghi `ILogger` và tiếp tục | Bất biến bắt buộc của `BackgroundService` trong repo (`ObserverBackgroundService` 12–16: exception thoát ra ⇒ `StopHost` giết cả host) |
| **P6** | FE: `search` **nhận** `dueFrom`/`dueTo` dạng `YYYY-MM-DD` từ Calendar | `TaskSearchPage` parse URL → `useTaskSearch` → `searchApi` chỉ dùng string; backend đã `ToUniversalTime()` an toàn. **Không** đổi shape nào ⇒ **không** đụng `search.types.ts` |
| **P7** | FE: `ProfilePage` thêm **tab thứ 3** `notifications` (nhãn "Thông báo") | `ProfilePage` đã có `Tabs` key `profile`/`workspaces`; thêm item là thay đổi nhỏ nhất, không phá 2 tab cũ. `ProfilePage.test.tsx` chạy lại trước/sau (đúng R3 Phase 12) |

---

## 3. §1 — Frontend: Calendar View (ô **A**) — **thuần client, không API mới**

### 3.1 File **mới** — `frontend/src/features/board/`

| File | Nội dung |
|---|---|
| `utils/boardCalendar.ts` | **Hàm thuần**: `groupTasksByDueDate(tasks: TaskResponse[]) => Map<string /*YYYY-MM-DD*/, TaskResponse[]>` (bỏ task `dueDate === null`; khoá theo **ngày địa phương** của `dayjs(dueDate)`, giới hạn `[0,3]` item + `overflow`); `buildDayTasks(...)` trả `CalendarDaySummary { key, date, total, items, overflowCount, hasOverdue }` |
| `utils/calendarNavigation.ts` | **Hàm thuần**: `buildDaySearchUrl(workspaceId, boardId, dayKey)` ⇒ `/workspaces/{ws}/search?boardId={board}&dueFrom={dayKey}&dueTo={dayKey}`; dùng **cùng** `encodeURIComponent` như nút "Tìm trong workspace" hiện có |
| `components/TaskCalendar.tsx` | `Calendar` (antd, `fullscreen={false}` ở màn nhỏ / `true` ở ≥ `lg` bằng `Grid.useBreakpoint`), `cellRender` cho `info.type === 'date'`: `Badge`/`Tag` màu theo `priority` (**dùng lại map màu `TaskCard`**) + dấu đỏ khi `isOverdue` + `+N` khi tràn; `onSelect` ⇒ gọi `onDaySelect(dayKey)`; `onChange`/`onPanelChange` chuyển tháng. Nút **"Lọc ngày này"** ở header mỗi cell khi có task; khối dưới lịch: *"N thẻ không có hạn chót"* + link sang `/search` |
| `components/__tests__/TaskCalendar.test.tsx` | §3.4 |
| `utils/__tests__/boardCalendar.test.ts` · `utils/__tests__/calendarNavigation.test.ts` | §3.4 |

### 3.2 File **sửa**

| File | Thay đổi |
|---|---|
| `features/board/components/BoardView.tsx` | +`Segmented` (`value` = `view`, options "Bảng Kanban" / "Lịch", `data-testid="board-view-switch"`) đặt ở **Tier 1**; `{view === 'kanban' ? <khối Kanban hiện có> : <TaskCalendar …/>}`; truyền `filteredTasksByColumn` đã phẳng hoá + `columns` (để biết `isDone`) + `onTaskClick={setActiveTask}` + `onDaySelect` (điều hướng). **GIỮ NGUYÊN** mặc định `kanban`, toàn bộ DndContext/Modal/Drawer, và **không** đổi nhãn/`data-testid` nào đang có |
| `features/board/components/__tests__/BoardView.test.tsx` | Chỉ **thêm** 1 `describe` cho toggle view (§3.4). **KHÔNG** sửa 11 test hiện có |

### 3.3 Luồng & hợp đồng chốt

- **Mặc định** = `kanban` ⇒ hành vi/test hiện tại **không đổi**.
- Chọn "Lịch" ⇒ render `TaskCalendar`; **không** unmount `useBoard` (SignalR + realtime giữ nguyên).
- **Nguồn task = `filteredTasksByColumn`** ⇒ Calendar **tôn trọng** ô tìm kiếm + filter priority đang có (một nguồn sự thật duy nhất, không có 2 bộ lọc mâu thuẫn).
- Click **một ngày** ⇒ `navigate(buildDaySearchUrl(...))` ⇒ `TaskSearchPage` **đọc sẵn** `boardId`+`dueFrom`+`dueTo` từ URL (đã hoạt động từ Phase 12) — **không** sửa `TaskSearchPage`/`searchApi`/`search.types.ts`.
- Click **một thẻ** ⇒ `setActiveTask(task)` ⇒ mở `TaskDetailModal` hiện có (không modal mới).
- Task **không có** `dueDate` ⇒ **không** lên lưới; hiện ở khối phụ *"N thẻ không có hạn chót"* + nút mở `/search?boardId={boardId}` (tìm trong board, không lọc hạn — search **không** có tham số "không có hạn chót").

### 3.4 Test frontend mới (~**3 file / ~26 test**)

| File test | Nội dung |
|---|---|
| `utils/__tests__/boardCalendar.test.ts` | 1 ngày nhiều task ⇒ đúng nhóm; `dueDate = null` ⇒ **loại**; 2 task cùng ngày **khác giờ** (07:00 và 23:00 **cùng ngày địa phương**) ⇒ **cùng** một ô; task có `dueDate` lệch ngày do UTC (23:30 local ngày 20 ⇒ ô **20**, không phải 21); ngày > 3 task ⇒ `overflowCount` đúng; `isOverdue` ⇒ `hasOverdue = true`; mảng rỗng ⇒ map rỗng; **thứ tự** trong ô theo `priority` giảm rồi `title` tăng (tất định) |
| `utils/__tests__/calendarNavigation.test.ts` | URL đúng cho ngày thường; có `boardId` ⇒ kèm `boardId`; **khoá** là `YYYY-MM-DD` (không ISO có `T00:00:00Z`) — chứng minh Calendar → Search khớp nhau |
| `components/__tests__/TaskCalendar.test.tsx` | render task lên đúng ô; task `isDone` ⇒ có nhãn "Đã xong"; tràn ⇒ `+N`; click ô **trống** **vẫn** điều hướng (empty-state ở trang search, không im lặng); click ô có task ⇒ URL đúng `dueFrom`/`dueTo`; click thẻ ⇒ `onTaskClick` đúng task; chuyển tháng ⇒ không crash, không gọi API thêm; 0 task ⇒ `Empty`/thông báo tiếng Việt, **không** màn hình trắng |
| `components/__tests__/BoardView.test.tsx` (**chỉ thêm**) | mặc định là `kanban` (11 test cũ vẫn xanh); chọn `calendar` ⇒ `TaskCalendar` hiện, `KanbanColumn` **không** hiện; chọn lại `kanban` ⇒ Kanban trở lại với **đủ** task; `Segmented` có `data-testid="board-view-switch"` |

---

## 4. §2 — Backend: chuỗi thời gian cho Burndown/Velocity (ô **B**)

### 4.1 File **mới**

| File | Nội dung |
|---|---|
| `Reporting/Contracts/ReportProgressSeries.cs` | `ReportDailyProgress(DateOnly Date, int OpenTasks, int Completions, int Creations)`, `ReportProgressSeries(ReportRange Range, string Mode, IReadOnlyList<ReportDailyProgress> Days, IReadOnlyList<ReportWeeklyProgress> Weeks, int BucketDays, bool BucketCapReached, int MaxBuckets, int TzOffsetMinutes)` + `ReportWeeklyProgress(DateOnly WeekStart, int Completions, int Creations, int OpenAtEnd)` + `ReportVelocity(double AvgCompletionsPerWeek, int CompletedInRange, int OpenAtEnd)` |
| `Reporting/DTOs/ReportProgressSeriesDtos.cs` | `ReportProgressSeriesResponse(...)` + `.From(domain)` (một chiều, cùng tiền lệ `ReportSummaryResponse.From`) |
| `tests/…/Pure/ReportProgressSeriesTests.cs` | **PS-1 … PS-10** — gọi thẳng `ReportAggregator.BuildProgressSeries` với snapshot dựng tay (không DB) |
| `tests/…/Integration/ReportProgressSeriesApiTests.cs` | **PSA-1 … PSA-8** — qua HTTP thật |

### 4.2 File **sửa**

| File | Thay đổi |
|---|---|
| `Reporting/Services/ReportAggregator.cs` | +`BuildProgressSeries(snapshot, thresholds, tzOffsetMinutes)` (**hàm thuần**). `openTasks` của ngày *d* = số task thoả `createdAt_localDay <= d` **và** (`completedAt == null` **hoặc** `completedAt_localDay > d`); `completions`/`creations` = số task có `completedAt`/`createdAt` rơi vào `d`. **Chỉ** nhận task trong `snapshot.Boards` (dùng lại `FilterToKnownBoards`) |
| `Reporting/Services/IReportService.cs` | +`GetProgressSeriesAsync(workspaceId, boardId, from, to, tzOffsetMinutes, userId, ct)` — doc rõ: **read-only**, **Manager+** (404/403 như `GetSummaryAsync`) |
| `Reporting/Services/ReportService.cs` | +`GetProgressSeriesAsync`: `EnsureEnabled()` → `RequireManagerAsync` → `ValidateRange` → `ResolveBoardScopeAsync` → nạp **cùng** các truy vấn board/column/task **đã lọc theo `range`** rồi gọi `BuildProgressSeries`. **KHÔNG** nạp `activity_logs`/Observer (không dùng). Vẫn là **nơi duy nhất** Reporting chạm DB ⇒ không thêm chỗ thứ hai |
| `Reporting/Endpoints/ReportingEndpoints.cs` | +`group.MapGet("/progress-series", GetProgressSeriesAsync).RequireAuthorization();` + handler bind `from`/`to`/`boardId`/`tzOffsetMinutes` (lỗi parse ⇒ **400** ở tầng bind, đúng tiền lệ `/summary`) |
| `Reporting/Options/ReportsOptions.cs` | +`MaxSeriesBuckets` (mặc định **90**) + `SeriesWeeklyThresholdDays` (mặc định **60**) |

### 4.3 Hợp đồng API backend **chốt** (frontend dùng đúng, không đoán)

```http
GET /api/workspaces/{workspaceId}/reports/progress-series
    ?from=<ISO>&to=<ISO>&boardId=<guid?>&tzOffsetMinutes=<int?>          (Manager+ · 404 người ngoài)

type: date if range.Days <= SeriesWeeklyThresholdDays (60) else week
mode = 'date'  -> bucketDays = 1, days.length = range.Days (đã cap MaxSeriesBuckets = 90)
mode = 'week'  -> bucketDays = 7, số bucket = ceil(range.Days / 7)
```

```ts
interface ReportDailyProgressPoint {
  date: string            // "2026-01-20" (ISO date, ĐÃ áp tzOffsetMinutes)
  openTasks: number       // số task mở vào cuối ngày (>= 0)
  completions: number     // số task completed_at rơi vào ngày này
  creations: number       // số task created_at rơi vào ngày này
}
interface ReportWeeklyProgressPoint {
  weekStart: string       // ISO date của Thứ Hai đầu tuần
  completions: number
  creations: number
  openAtEnd: number       // số task mở ở cuối tuần
}
interface ReportProgressSeriesResponse {
  workspaceId: string
  workspaceName: string
  scope: { type: 'workspace' | 'board'; boardId: string | null; boardName: string | null }
  period: { from: string; to: string; days: number; clamped: boolean }
  mode: 'date' | 'week'
  bucketDays: number                    // 1 hoặc 7
  days: ReportDailyProgressPoint[]       // RỖNG khi mode = 'week'
  weeks: ReportWeeklyProgressPoint[]     // RỖNG khi mode = 'date'
  velocity: { avgCompletionsPerWeek: number; completedInRange: number; openAtEnd: number }
  metricDefinitions: Record<string, string>   // key mới: openTasks/completions/creations/avgCompletionsPerWeek
  truncated: { bucketCapReached: boolean; maxBuckets: number }
  tzOffsetMinutes: number               // giá trị THỰC dùng (đã clamp) — UI hiển thị để không gây hiểu nhầm
}
```

**Luật chốt:**
- `tzOffsetMinutes` vắng ⇒ `0`; giá trị ngoài `[-840, 840]` ⇒ **clamp** (không 400) — đây là knob UI.
- `from`/`to` lỗi parse ⇒ **400**; `from > to` ⇒ **400** (dùng lại `InvalidReportRangeException`).
- `boardId` không thuộc workspace ⇒ **404** (dùng lại `ResolveBoardScopeAsync`).
- **KHÔNG** trả `total`/keyset — đây là chuỗi thời gian có biên xác định.
- `openTasks` **không** trừ `completions` trong cùng ngày (D6) ⇒ đường *lý tưởng* = `openTasks − completions` **luôn ≥ 0**.
- `days`/`weeks` luôn đủ bucket (kể cả bucket 0) ⇒ UI vẽ được biểu đồ liên tục, **không** nhảy cột.
- `truncated.bucketCapReached` = `true` khi `range.Days > MaxSeriesBuckets` ở `mode = 'date'` ⇒ UI hiện cảnh báo "khoảng quá dài, đang hiển thị N ngày cuối".

### 4.4 Bằng chứng cần đo (§2)

```
dotnet build TeamNexus.sln -m:1 -nr:false            → 0 Warning(s) / 0 Error(s)
dotnet ef migrations list --no-build                  → 9   (CHƯA đổi ở bước này)
dotnet test --filter "FullyQualifiedName~ReportProgressSeries" → PASS toàn bộ
```

### 4.5 Test backend mới (§2) — **PS-1 … PS-10** (thuần) + **PSA-1 … PSA-8** (HTTP)

| # | Nội dung |
|---|---|
| **PS-1** | 1 task tạo ngày 1, xong ngày 3 ⇒ `openTasks` = 1 ở ngày 1–2, = 0 từ ngày 3; `completions[3] = 1` |
| **PS-2** | Task tạo **trước** `from` và **vẫn mở** ⇒ `openTasks` **> 0** ở ngày đầu (không bị mất baseline) |
| **PS-3** | Task tạo trước `from` và xong **sau** `from` ⇒ vào `openTasks` các ngày trước, `completions` đúng ngày |
| **PS-4** | Biên `tzOffsetMinutes`: `completedAt = 2026-01-20T18:00Z` với tz **+420** ⇒ `completions` rơi ngày **21**; với tz **0** ⇒ ngày **20** |
| **PS-5** | `tzOffsetMinutes = 99999` / `-99999` ⇒ **clamp** về `±840`, **không** 400; response echo giá trị đã clamp |
| **PS-6** | `openTasks` **không âm** và **không** bị trừ bởi `completions` cùng ngày (D6) |
| **PS-7** | Task ở **cột `is_done` nhưng `completedAt = null`** ⇒ **không** tính `completions` (khớp `ReportAggregator.IsDone` không có `completedAt`) nhưng **có** được coi là **đóng** ⇒ `openTasks` giảm từ ngày **sau** ngày kết thúc? ⇒ **chốt:** dùng `isDoneColumn` để loại khỏi `openTasks` **từ ngày tạo** (vì không biết ngày đóng) và **ghi rõ** trong `metricDefinitions` |
| **PS-8** | Board soft-deleted / task soft-deleted ⇒ **không** xuất hiện (query filter) |
| **PS-9** | `range.Days > 60` ⇒ `mode = 'week'`, `days = []`, số tuần = `ceil(days/7)`; `range.Days = 60` ⇒ `mode = 'date'`; `range.Days = 365` + cap 90 ⇒ `truncated.bucketCapReached = true` |
| **PS-10** | `metricDefinitions` có **4** key mới; mảng rỗng ⇒ mọi số = 0, **không** `NaN`/`Infinity`; thứ tự bucket **tăng dần** theo ngày/tuần (tất định) |
| **PSA-1** | Workspace 3 board, seed task nhiều ngày ⇒ `mode='date'`, `days.length = 7` với `from`/`to` 7 ngày; `completions` khớp seed |
| **PSA-2** | `boardId=<board khác workspace>` ⇒ **404**; `boardId` hợp lệ ⇒ `scope.type = 'board'` và số liệu chỉ của board đó |
| **PSA-3** | **Member** (không phải Manager) ⇒ **403**; người **ngoài** workspace ⇒ **404** (khớp `/summary`) |
| **PSA-4** | `from > to` ⇒ **400**; `from=abc` ⇒ **400**; `tzOffsetMinutes=abc` ⇒ **400**; `tzOffsetMinutes=9999` ⇒ **200** + echo clamp |
| **PSA-5** | Không truyền `from`/`to` ⇒ dùng mặc định `Reports:DefaultRangeDays`, `clamped` theo `MaxRangeDays` |
| **PSA-6** | Reporting `Enabled = false` ⇒ **503** (dùng lại `ReportingDisabledException`) |
| **PSA-7** | 0 task / 0 board ⇒ **200**, `days` đủ bucket với toàn số 0, **không** NaN |
| **PSA-8** | **Hồi quy**: `GET /api/workspaces/{id}/reports/summary` **không đổi** shape (assert lại đủ 12 field cấp 1) |

---

## 5. §3 — Backend: Email Digest hàng ngày (ô **C**)

### 5.1 §3.1 — Migration (D2) — **DUY NHẤT 1 migration**

| File | Nội dung |
|---|---|
| `Persistence/Data/Entities/ApplicationUser.cs` | +`public bool DigestEnabled { get; set; } = true;` (doc comment nêu rõ: **không** phải IAuditableEntity ⇒ không có `updated_at` cho `users`) |
| `Persistence/Data/Configurations/ApplicationUserConfiguration.cs` | +`.Property(u => u.DigestEnabled).HasDefaultValue(true).IsRequired();` ⇒ migration sinh `DEFAULT true` cho row cũ (**không** cần backfill) |
| `Persistence/Migrations/2026xxxx_Phase13DailyDigest.cs` | Sinh bằng `dotnet ef migrations add Phase13DailyDigest --project src/TeamNexus.Persistence/TeamNexus.Persistence.csproj --startup-project src/TeamNexus.Api/TeamNexus.Api.csproj --no-build` (build trước; `dotnet ef` **không** nhận `-m:1`/`-nr:false`) |
| `Project-Documents/04-database-design.md` | §3.1 `users`: +dòng `digest_enabled`; §6: +dòng migration thứ 10 |

> **Điều kiện kiểm tra bắt buộc:** `Up()` chỉ được có **1** `AddColumn` + **1** `AlterColumn/DEFAULT` —
> **không** `DropColumn`, **không** `CreateTable`, **không** `CreateIndex` nào khác. `Down()` chỉ `DropColumn`.

### 5.2 §3.2 — Backend Board (port + service)

| File | Nội dung |
|---|---|
| `Board/DTOs/DigestDtos.cs` | `DigestTaskItem(TaskId, Title, BoardId, BoardName, DueDate, Priority, IsDone, OverdueByDays)`; `DigestBucket(string Kind /* overdue|dueSoon|recentlyAssigned */, int OpenTaskCount, IReadOnlyList<DigestTaskItem> Items, bool Truncated)`; `DigestWorkspaceSection(WorkspaceId, WorkspaceName, Role, IReadOnlyList<DigestBucket> Buckets)`; `DailyDigestContent(string RecipientEmail, string RecipientDisplayName, DateOnly SendDateLocal, IReadOnlyList<DigestWorkspaceSection> Workspaces, string DashboardUrl)` |
| `Board/Services/IDailyDigestService.cs` | `IDailyDigestService.BuildAsync(Guid userId, DateOnly todayLocal, int maxWorkspaces, int maxItemsPerBucket, ct) → DailyDigestContent?`; +`NullDailyDigestService` (trả `null`) + doc **"cài đặt KHÔNG BAO GIỜ ném"** + bất biến *"KHÔNG tạo notification in-app, KHÔNG ghi `activity_logs`, KHÔNG đi qua Accountability Layer"* |
| `Board/Services/DailyDigestService.cs` | (1) nạp user theo `userId` + `DigestEnabled`; (2) nạp tối đa `maxWorkspaces` workspace qua `workspace_members` `ORDER BY joined_at`; (3) mỗi workspace gọi **`IDashboardService.GetAsync`** (tái dùng, **không** viết truy vấn mới) với `take = maxItemsPerBucket`; (4) chuyển 3 bucket thành `DigestBucket` + tên board; (5) **bỏ workspace không có task nào mở**; (6) **rỗng toàn bộ ⇒ trả `null`** (không gửi mail rỗng); (7) `DashboardUrl` = `{Frontend:BaseUrl}/workspaces/{firstWorkspaceId}/dashboard` (dùng **`WorkspaceEmailOptions`/`InvitationSettings`-style** — dùng lại `IConfiguration` optional như `BoardModule` 61–71) |
| `tests/…/Integration/DailyDigestApiTests.cs` | §5.4 (`DA-1 … DA-6`) — gọi **trực tiếp** service qua DI, **không** cần timer |

### 5.3 §3.3 — Backend Ai (lịch gửi + template + gateway)

| File | Nội dung |
|---|---|
| `Ai/Options/DigestOptions.cs` | D8 (POCO + clamp: `SendAtLocalHour` clamp `[0,23]`, `DefaultTimeZoneOffsetMinutes` clamp `[-840,840]`, `MaxUsersPerRun`/`MaxDailyEmails`/`MaxItemsPerBucket` ≥ 1, `StartupDelaySeconds` ≥ 0) + `SectionName = "Digest"` + `Enabled = false` mặc định |
| `Ai/Services/Email/EmailTemplates.cs` (**sửa**) | `EmailKinds.DailyDigest = "DailyDigest"`; +`DailyDigest(content) → (Subject, TextBody, HtmlBody)` — **hàm THUẦN**, **không** tham số URL rời (link đã nằm trong `content.DashboardUrl`); HTML-escape **mọi** giá trị nội suy (tên workspace/board/task là dữ liệu người dùng ⇒ `<script>` phải bị escape); `TextBody` có bản text thuần (đọc được ở terminal); footer **"Tắt nhận email tóm tắt: {tắtUrl}"** với `tắtUrl = {Frontend:BaseUrl}/profile#notifications`; subject = `"TeamNexus — việc của bạn hôm {dd/MM/yyyy}"` (`Truncate(subject, 200)`) |
| `Ai/Services/DigestGateway.cs` | `IDigestGateway.SendDailyDigestAsync(content, ct) → bool`: render qua `EmailTemplates.DailyDigest` → `IEmailDispatcher.SendAsync(kind: EmailKinds.DailyDigest, toEmail: content.RecipientEmail, sentByUserId: null, ...)` → `row.Status == Sent`; **không bao giờ ném** (đúng hợp đồng `EmailGateway`) |
| `Ai/Services/DailyDigestRunner.cs` | `IDailyDigestRunner.RunOnceAsync(ct) → DigestRunResult(int Considered, int SkippedAlreadySent, int Sent, int Failed, int SkippedEmpty)`. Bước: (0) `Enabled == false` ⇒ `Skipped`; (1) tính `now = _clock.GetUtcNow()`; (2) `todayLocal` từ `now + DefaultTimeZoneOffsetMinutes`; (3) **guard chống trùng ở tầng dữ liệu** (D3): tập `to_email` đã có `email_messages` `kind = DailyDigest` với `CreatedAt >= mốc nửa đêm UTC của ngày hôm nay`; (4) `users` có `DigestEnabled && Email != null` `OrderBy(Id)` `Take(MaxUsersPerRun)`; (5) với mỗi user **chưa** gửi: gọi `IDailyDigestService.BuildAsync`; `null` ⇒ `SkippedEmpty`; ngược lại gọi `IDigestGateway.SendDailyDigestAsync`; (6) `MaxDailyEmails` cap **tổng** số gửi trong lượt; (7) `ILogger` **1 dòng tổng kết** (không chứa email đầy đủ — che dạng `a***@x.com`) |
| `Ai/Services/DailyDigestBackgroundService.cs` | `BackgroundService`: `StartupDelaySeconds` → `PeriodicTimer(1h)` → `RunOnceAsync`; **mọi** `Exception` bị bắt + ghi log (**P5**, đúng `ObserverBackgroundService` 77–81). Doc comment nêu rõ *"chống trùng do DB bảo đảm ⇒ tick thừa vô hại"* |
| `Ai/AiModule.cs` (**sửa**) | `services.AddOptions<DigestOptions>().Bind(configuration.GetSection(DigestOptions.SectionName));` · `services.AddScoped<IDigestGateway, DigestGateway>();` · `services.AddScoped<IDailyDigestRunner, DailyDigestRunner>();` · `services.AddHostedService<DailyDigestBackgroundService>();` · +1 dòng log cấu hình (`P3`) |
| `src/TeamNexus.Api/appsettings.json` (**sửa**) | +section `"Digest"` với `Enabled: false` + comment mô tả |

### 5.4 §3.4 — Backend: API lựa chọn opt-out (D13)

| File | Thay đổi |
|---|---|
| `Board/DTOs/UserProfileDtos.cs` | `UserProfileResponse` += **field cuối** `bool DigestEnabled`; `UpdateProfileRequest` += **field cuối** `bool? DigestEnabled = null` |
| `Board/Services/UserProfileService.cs` | `ToResponse` điền `user.DigestEnabled`; `UpdateAsync`: `if (request.DigestEnabled is { } enabled) user.DigestEnabled = enabled;` ⇒ **vắng ⇒ giữ nguyên** (D13). **KHÔNG** ghi `activity_logs` (đây là cài đặt cá nhân, không phải hành động workspace — khớp quyết định "profile **không** ghi activity" của Phase 11; cần ghi rõ trong doc comment) |
| `tests/…/Integration/ProfileApiTests.cs` (**mở rộng**) | +3 test `PE-1 … PE-3` |

### 5.5 §3.5 — Test backend mới (§3)

#### `Pure/DigestTemplateTests` — **DT-1 … DT-8** (không DB, không mail)

| # | Nội dung |
|---|---|
| **DT-1** | `EmailKinds.DailyDigest == "DailyDigest"`; subject ≤ 200 ký tự, **có** ngày `dd/MM/yyyy` |
| **DT-2** | `TextBody` liệt kê tên workspace + tối đa 5 task/bucket; **có** `dashboardUrl` |
| **DT-3** | `HtmlBody` **escape** `<script>alert(1)</script>` trong `title` ⇒ **không** chứa `<script>` trần |
| **DT-4** | **Giữ dấu tiếng Việt** trong HTML (`Nguyễn Văn A`, `Báo cáo`) — chứng minh `Encoder` (P5 Phase 11) vẫn đúng |
| **DT-5** | `EmailTemplates.DailyDigest` **không** ném khi `Workspaces = []`, khi `Items = []`, khi `DisplayName` rỗng |
| **DT-6** | Bản `TextBody` **không** chứa thẻ HTML nào (`<`) |
| **DT-7** | Có **link tắt nhận** trong cả `TextBody` và `HtmlBody` |
| **DT-8** | `OverdueByDays = 3` ⇒ hiện "Quá hạn 3 ngày"; `DueDate = null` ⇒ **không** hiện nhãn hạn (không in "null") |

#### `Integration/DailyDigestApiTests` (`IDailyDigestService` qua DI thật) — **DA-1 … DA-6**

| # | Nội dung |
|---|---|
| **DA-1** | User có task quá hạn ở 1 workspace ⇒ `BuildAsync` trả **1** section, bucket `overdue` đúng `OpenTaskCount` + `Items` ≤ `maxItemsPerBucket` |
| **DA-2** | `OpenTaskCount` là **tổng thật** khi `take = 2` và có 7 task quá hạn (`Items.Length = 2`, `Truncated = true`) |
| **DA-3** | `DigestEnabled = false` ⇒ `BuildAsync` trả **`null`** |
| **DA-4** | User **không có** task nào mở ở mọi workspace ⇒ **`null`** (không gửi mail rỗng) |
| **DA-5** | Task của **workspace khác** / **board soft-deleted** ⇒ **không** lọt vào digest; task của người **khác** trong cùng workspace ⇒ **không** lọt (chỉ `assignee_id == userId`) |
| **DA-6** | User ở **3** workspace ⇒ **1** `DailyDigestContent` có **3** section, thứ tự theo `joined_at ASC`; `Workspaces.Count ≤ maxWorkspaces` |

#### `Integration/DailyDigestRunnerTests` — **DR-1 … DR-4**

| # | Nội dung |
|---|---|
| **DR-1** | `Digest:Enabled = false` ⇒ `RunOnceAsync` trả `Skipped`, **0** row `email_messages` mới |
| **DR-2** | Bật digest + 2 user có task ⇒ **2** row `email_messages` `kind = 'DailyDigest'`. **Chốt được:** `NullEmailSender` (key rỗng) trả `EmailSendResult.Sent(null)` (`IEmailSender.cs` 65) ⇒ `row.Status == Sent` và `ProviderMessageId == null` — assert **cả hai** để chứng minh luồng chạy thật mà không cần Resend |
| **DR-3** | Chạy `RunOnceAsync` **2 lần** liên tiếp trong cùng ngày ⇒ lần 2 **không** thêm row nào (`SkippedAlreadySent = 2`) — **đây là test chống trùng quan trọng nhất** |
| **DR-4** | User có `DigestEnabled = false` ⇒ **không** có row; user **không có email** (`Email = null`, AI Agent) ⇒ **không** có row, **không** ném |

#### `Integration/ProfileApiTests` (mở rộng) — **PE-1 … PE-3**

| # | Nội dung |
|---|---|
| **PE-1** | `GET /api/users/me` có `digestEnabled` (mặc định `true` cho user mới) |
| **PE-2** | `PUT {"displayName":"X","digestEnabled":false}` ⇒ response `digestEnabled = false`; `GET` lại ⇒ **vẫn** `false` |
| **PE-3** | **Hồi quy (D13):** `PUT {"displayName":"Y"}` (**KHÔNG** có `digestEnabled`) ⇒ giá trị **giữ nguyên** (không bị reset về `false`); assert `displayName` đã đổi |

### 5.6 Bằng chứng cần đo (§3)

```
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental → 0 Warning(s) / 0 Error(s)
dotnet ef migrations list --no-build                        → 10 (mới nhất Phase13DailyDigest)
dotnet ef migrations has-pending-model-changes              → "No changes have been made to the model since the last migration."
Đọc lại Up(): CHỈ 1 AddColumn (users.digest_enabled) + DEFAULT
dotnet test (TEAMNEXUS_TEST_DB)                             → Failed 0 / Skipped 0 / Passed = 423 + số test mới
```

---

## 6. §4 — Frontend: Burndown/Velocity + Digest toggle

### 6.1 §4.1 — Burndown/Velocity (ô **B**)

| File | Nội dung |
|---|---|
| `features/reporting/types/reporting.types.ts` (**sửa** — **append** alias, không sửa type cũ) | +`ReportDailyProgressPoint` · `ReportWeeklyProgressPoint` · `ReportProgressSeriesResponse` · `ReportProgressSeriesParams` (**khớp 1-1** §4.3) |
| `features/reporting/services/reportingApi.ts` (**sửa**) | +`getProgressSeries(workspaceId, params)` — build `URLSearchParams` **giống** `workspaceApi.getActivity` (bỏ tham số rỗng) |
| `features/reporting/hooks/useReportProgressSeries.ts` (**mới**) | Pattern `useReportSummary` (`useState + useEffect + reload`); `tzOffsetMinutes` = `timeZoneOffsetMinutes()`; `status: 'idle' \| 'loading' \| 'success' \| 'error'` |
| `features/reporting/utils/burndown.ts` (**mới**) | **Hàm thuần**: `toWeeklyBuckets(days, weeks, mode)` (nhóm ngày → tuần, `weekStart` = **Thứ Hai**); `idealOpenSeries(openAtStart, completionsPerBucket)` = `openAtStart − Σcompletions` (**không** dưới 0); `barHeightPercent(value, max)` (**guard `max = 0` ⇒ 0%**, không chia 0/NaN); `velocityStats(series)` |
| `features/reporting/utils/timeZoneOffsetMinutes.ts` (**mới**) | **Hàm thuần**: `new Date().getTimezoneOffset() * -1` (D7) + doc comment nêu rõ dấu |
| `features/reporting/components/BurndownChart.tsx` (**mới**) | Biểu đồ cột CSS: mỗi bucket = cột `completions` (xanh) + `openTasks` (xám) cạnh nhau, `Tooltip` antd hiển thị số, trục nhãn `DD/MM` (hoặc `Tuần DD/MM`), đường "lý tưởng" vẽ bằng cột mờ. `Empty` khi 0 bucket hoặc board 0 task; cảnh báo khi `truncated.bucketCapReached`. Mọi nhãn **tiếng Việt có dấu** |
| `features/reporting/components/VelocityPanel.tsx` (**mới**) | 3 `Statistic`: **Năng suất trung bình/ tuần** (`velocity.avgCompletionsPerWeek`), **Task còn mở** (`velocity.openAtEnd`), **Hoàn thành trong kỳ** (`velocity.completedInRange`) |
| `features/reporting/pages/ReportsPage.tsx` (**sửa**) | Render `<BurndownChart/>` + `<VelocityPanel/>` **dưới** `ReportSummaryPanel`, truyền `workspaceId` + `selectedBoardId` + `from`/`to` đã có. Nếu request lỗi ⇒ **`Alert`** tiếng Việt, **không** làm hỏng phần `ReportSummaryPanel` đang hiển thị |
| `features/reporting/components/__tests__/BurndownChart.test.tsx` · `VelocityPanel.test.tsx` · `utils/__tests__/burndown.test.ts` · `hooks/__tests__/useReportProgressSeries.test.ts` · `services/__tests__/reportingApi.progressSeries.test.ts` (**mới**) | §6.3 |
| `features/reporting/pages/__tests__/ReportsPage.test.tsx` (**mở rộng**) | +2 test (chart hiện khi có dữ liệu; lỗi 403 ⇒ **không** crash) |

### 6.2 §4.2 — Digest toggle trong Profile (ô **C**)

| File | Nội dung |
|---|---|
| `features/profile/types/profile.types.ts` (**sửa**) | `UserProfileResponse` += `digestEnabled: boolean` · `UpdateProfileRequest` += `digestEnabled?: boolean \| null` |
| `features/profile/pages/ProfilePage.tsx` (**sửa**) | +tab thứ 3 `key: 'notifications'` nhãn **"Thông báo"** (`BellOutlined`), **đọc `window.location.hash === '#notifications'` lúc mount để mở đúng tab** (đích của link "Tắt nhận" trong email digest); `Switch` **"Email tóm tắt công việc hằng ngày"** (`data-testid="digest-toggle"`), mô tả *"Gửi mỗi sáng: task quá hạn, sắp đến hạn và vừa được giao cho bạn. Bạn có thể tắt bất cứ lúc nào."*; `onChange` gọi `profileApi.updateProfile({ displayName, avatarUrl, digestEnabled })` (gửi **cả** 3 field để không mất giá trị), `message.success('Đã bật/tắt email tóm tắt')` / `message.error` tiếng Việt; trạng thái lấy từ `GET /api/users/me` (không tin `useAuthStore` vì `/auth/me` **không** trả field này) |
| `features/profile/pages/__tests__/ProfilePage.test.tsx` (**mở rộng**) | +4 test (`PF-1 … PF-4`) |

### 6.3 Test frontend mới (~**6 file mới + 3 file mở rộng / ~37 test**)

| File test | Nội dung |
|---|---|
| `reporting/utils/__tests__/burndown.test.ts` (**mới**) | nhóm ngày → tuần đúng (`weekStart` = Thứ Hai); `mode='week'` ⇒ dùng `weeks`, bỏ qua `days`; `idealOpenSeries` **không** âm; `barHeightPercent(value, 0)` ⇒ **0** (không `NaN`/`Infinity`); mảng rỗng ⇒ output rỗng, không throw |
| `reporting/utils/__tests__/timeZoneOffset.test.ts` (**mới**) | dấu: VN (`getTimezoneOffset() = -420`) ⇒ `+420`; mock UTC (`0`) ⇒ `0` |
| `reporting/services/__tests__/reportingApi.progressSeries.test.ts` (**mới**) | URL đúng + **chỉ** tham số có giá trị; `tzOffsetMinutes=0` **vẫn** được gửi (không bị coi là rỗng); lỗi 403 ⇒ reject có `response.status` |
| `reporting/hooks/__tests__/useReportProgressSeries.test.ts` (**mới**) | loading → success; 403 ⇒ `status='error'`; `reload` gọi lại; `boardId` đổi ⇒ gọi lại |
| `reporting/components/__tests__/BurndownChart.test.tsx` (**mới**) | 7 bucket ⇒ 7 cột; `truncated` ⇒ cảnh báo tiếng Việt; 0 bucket ⇒ `Empty`; giá trị 0 ⇒ cột cao **0%** (không vỡ layout); mode `week` ⇒ nhãn "Tuần …" |
| `reporting/components/__tests__/VelocityPanel.test.tsx` (**mới**) | 3 `Statistic` đúng số; `avgCompletionsPerWeek = 0` ⇒ `0` (không "—" khó hiểu) |
| `reporting/pages/__tests__/ReportsPage.test.tsx` (**mở rộng**) | biểu đồ hiện khi series thành công; series lỗi ⇒ `Alert` **nhưng** `ReportSummaryPanel` **vẫn** hiển thị |
| `profile/pages/__tests__/ProfilePage.test.tsx` (**mở rộng**) — `PF-1 … PF-4` | `PF-1` tab "Thông báo" hiện, `Switch` phản ánh `digestEnabled` từ API; `PF-2` bật/tắt ⇒ gọi `PUT` với `digestEnabled` đúng + `displayName`/`avatarUrl` giữ nguyên; `PF-3` API lỗi ⇒ `message.error` tiếng Việt, `Switch` **không** đổi trạng thái (rollback); `PF-4` `window.location.hash = '#notifications'` ⇒ tab **"Thông báo"** là tab đang mở khi mount (đích của link "Tắt nhận" trong email) |
| `board/utils/__tests__/*` · `board/components/__tests__/*` | §3.4 |

### 6.4 Route & điều hướng chốt

- **KHÔNG** thêm route mới. `/workspaces/:id/search` và `/reports` và `/profile` **đã có** ⇒ Calendar và Burndown chỉ **điền thêm** nội dung.
- `/workspaces/:id/boards/:boardId` giữ nguyên (chỉ thêm `Segmented` trong view).

---

## 7. §5 — DoD, CI & tài liệu

### 7.1 Con số mục tiêu

| Chỉ số | Baseline **đo được** (§1.2) | Kết quả **đo thật** (điền sau khi xong) |
|---|---|---|
| Backend `dotnet test` (**DB thật**, `Skipped: 0`) | **423** | ⬜ **423 + Δ** (ước tính **+39**) |
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 / 0 | ⬜ 0 / 0 |
| `dotnet ef migrations list` | **9** | ⬜ **10** (`Phase13DailyDigest`) |
| `has-pending-model-changes` | sạch | ⬜ sạch |
| Frontend `npm test` | **407** / 77 file | ⬜ **407 + Δ** (ước tính **+52**) |
| `npm run lint` · `npx tsc -b` · `npm run build` | 0/0 · exit 0 · OK | ⬜ 0/0 · exit 0 · OK |

**Phân bổ backend mới (ước tính):** `DigestTemplateTests` **8** · `ReportProgressSeriesTests` **10** ·
`ReportProgressSeriesApiTests` **8** · `DailyDigestApiTests` **6** · `DailyDigestRunnerTests` **4** ·
`ProfileApiTests` **+3**. ⇒ **+39**.

**Phân bổ frontend mới (ước tính):** `boardCalendar` **8** · `calendarNavigation` **4** ·
`TaskCalendar` **8** · `BoardView` **+3** · `burndown` **6** · `timeZoneOffset` **2** ·
`reportingApi.progressSeries` **3** · `useReportProgressSeries` **4** · `BurndownChart` **5** ·
`VelocityPanel` **3** · `ReportsPage` **+2** · `ProfilePage` **+4**. ⇒ **+52**.

### 7.2 ⚙️ CI phải nâng (làm **SAU CÙNG**, khi số thật đã đo)

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | `if ($total -ne 423)` ⇒ `-ne <số thật>`; cập nhật comment chuỗi `… 371 (Giai đoạn 11) → 423 (Giai đoạn 12) → <N> (Giai đoạn 13)` |
| 2 | `.github/workflows/ci-web.yml` | `if ($total -lt 406)` ⇒ `-lt <số thật>`; cập nhật comment baseline (nhớ ghi chú **406 trong tài liệu vs 407 đo thật** — §1.2) |
| 3 | `.github/workflows/ci-backend.yml` | Nếu cổng "no skip" vẫn dùng `$skipped -gt 0` ⇒ **KHÔNG** cần sửa. **Kiểm tra** thêm: `Digest:Enabled` mặc định `false` ⇒ CI **không** gửi mail (không cần thêm `env`) |

### 7.3 Tài liệu phải cập nhật

| # | File | Việc |
|---|---|---|
| 1 | `Project-Documents/tasks/phase-13-visualization-proactive-notifications.md` | **Tài liệu này** — điền **số thật** vào §1.2 + §7.1 sau khi đo; ghi rõ **lệch DoD** ở **D2** |
| 2 | `Project-Documents/03-roadmap.md` | Giai đoạn 13: tick `[x]` 3 ô + link tài liệu này + baseline thật; sửa dòng *"không cần migration mới"* ⇒ *"có 1 migration additive `users.digest_enabled` (10 tổng) — lý do: opt-out phải đọc được ở server (D2)"*; sửa dòng *"Không cần API mới ngoài … và EmailGateway"* ⇒ thêm `GET .../reports/progress-series` |
| 3 | `Project-Documents/01-system-specification.md` | +§: ngữ nghĩa chuỗi thời gian (`openTasks`/`completions`/`creations` + tz), route mới, cơ chế digest (1 email/user/ngày, chống trùng bằng `email_messages`, hạn mức riêng), quyết định `digestEnabled` **vắng ⇒ giữ nguyên** (D13) |
| 4 | `Project-Documents/04-database-design.md` | §3.1 `users`: +`digest_enabled`; §6: +dòng migration 10 `Phase13DailyDigest`; §7: +3 gạch đầu dòng (digest **không** ghi notification/activity; `email_messages` dùng làm dedupe key theo `(to_email, ngày UTC)`; **không** thêm index mới ở giai đoạn này và **vì sao**) |
| 5 | `README.md` | +`## Trạng thái (Giai đoạn 13)`; xác nhận mục Migration là **10** |
| 6 | `Project-Documents/report/phase-13-visualization-proactive-notifications-test-report.md` | **Tạo khi kết thúc** (theo mẫu `report/phase-12-dashboard-search-test-report.md`) |

### 7.4 Điều kiện "xong"

Cả **3 ô** (A Calendar · B Burndown/Velocity · C Digest) + **5 hạng mục phát sinh** (E segmented view ·
F endpoint progress-series · G dedupe ở DB · H khoá theo email · I hạn mức riêng) đóng bằng bằng chứng §7.5;
**đúng 10 migration** và `has-pending-model-changes` sạch; **`Skipped: 0`** ở backend;
2 cổng CI đã nâng baseline và **chạy xanh trên GitHub Actions**;
`03-roadmap.md` + `README.md` + `01-system-specification.md` + `04-database-design.md` ghi **số thật**;
báo cáo tại `Project-Documents/report/phase-13-visualization-proactive-notifications-test-report.md`.

### 7.5 Bảng bằng chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 warning / 0 error | ⬜ |
| 2 | `dotnet ef migrations list` + `has-pending-model-changes` | **10** / sạch | ⬜ |
| 3 | Đọc lại `Up()` của `Phase13DailyDigest` | **chỉ** `AddColumn(users.digest_enabled)` + DEFAULT; không DROP/CreateTable | ⬜ |
| 4 | `dotnet test` với `TEAMNEXUS_TEST_DB` (§1.2 — PostgreSQL local cổng 5432) | `Skipped: 0`, `Failed: 0`, `Total = 423 + Δ` | ⬜ |
| 5 | `npm run lint` / `npx tsc -b` / `npm test` / `npm run build` | 0-0 / exit 0 / `407 + Δ` pass / OK | ⬜ |
| 6 | **1 lượt kiểm thử tay**: (a) bật tab "Lịch" ⇒ task hiện đúng ngày, click ngày ⇒ `/search` đã lọc đúng `dueFrom`/`dueTo`; (b) bật board khác / filter priority ⇒ Calendar đổi theo; (c) `/reports` ⇒ burndown 7–30 cột + velocity đúng số so với `/summary`; (d) `/profile` → "Thông báo" ⇒ tắt digest, chạy `IDailyDigestRunner` ⇒ **0** email; bật lại ⇒ **1** email; chạy lần 2 ⇒ **0** email | — | ⬜ |
| 7 | 2 workflow CI nâng ngưỡng | `ci-backend` (`total = <N>`, skipped 0) + `ci-web` (`≥ <M>`) | ⬜ |

---

## 8. Ca biên & chế độ lỗi (bắt buộc xử lý)

| Ca | Hành vi chốt |
|---|---|
| Calendar: task `dueDate = null` | **Không** lên lưới; hiện ở khối *"N thẻ không có hạn chót"* + link `/search` |
| Calendar: ngày có **> 3** task | Lưới hiện 3 đầu + `+N`; click ngày ⇒ trang search hiện **đủ** |
| Calendar: `dueDate` gần nửa đêm (23:30 local) | Rơi vào ô **ngày local**, không lệch sang ngày kế (test ở §3.4) |
| Calendar: click ô **trống** | **Vẫn** điều hướng (`dueFrom = dueTo = ngày`) ⇒ trang search hiện `Empty` "Không tìm thấy kết quả" — **không** im lặng |
| Calendar: 0 task / 0 cột | `Empty` tiếng Việt, **không** màn hình trắng |
| Calendar: board chỉ có task không hạn | Lưới rỗng + khối "N thẻ không có hạn chót" ⇒ **không** trông như board trống |
| Calendar: task `isDone` | Có trên lịch + nhãn "Đã xong" (**không** ẩn — hạn chót vẫn là thông tin) |
| Burndown: `range.Days > 60` | `mode = 'week'` (cột tuần) — không vẽ 365 cột |
| Burndown: `range.Days > MaxSeriesBuckets (90)` | `truncated.bucketCapReached = true` + chỉ 90 bucket **cuối**; UI hiện cảnh báo tiếng Việt |
| Burndown: task tạo **trước** `from` | Vẫn nằm trong `openTasks` (baseline không bị mất); task xong trước `from` ⇒ **không** tính lại |
| Burndown: `tzOffsetMinutes` = 0 / bỏ trống | Bucket theo **UTC** (mặc định) |
| Burndown: `tzOffsetMinutes = 99999` | **Clamp** về `840`, **200** (không 400); response echo giá trị đã clamp |
| Burndown: `tzOffsetMinutes = abc` | **400** |
| Burndown: `from > to` · `from = abc` | **400** (dùng lại `InvalidReportRangeException`) |
| Burndown: `boardId` của workspace khác | **404** |
| Burndown: caller là **Member** | **403**; người **ngoài** workspace ⇒ **404** |
| Burndown: Reporting `Enabled = false` | **503** |
| Burndown: 0 task / 0 board | **200**, mọi bucket = 0, `avgCompletionsPerWeek = 0`, **không** `NaN`/`Infinity`, **không** chia 0 |
| Burndown: `idealOpenSeries` khi `completions > openAtStart` | Kẹp tại **0** (không âm) |
| Burndown: API lỗi (403/500) | `Alert` tiếng Việt; **`ReportSummaryPanel` cũ vẫn hiển thị** (lỗi cục bộ) |
| Digest: `Digest:Enabled = false` | Không gửi gì, `email_messages` **không** tăng, **không** log lỗi |
| Digest: đã gửi hôm nay (cùng `to_email`) | **Bỏ qua**; chạy lại N lần trong ngày ⇒ **tối đa 1 email/người/ngày** |
| Digest: user `DigestEnabled = false` | **Không** có row `email_messages` |
| Digest: user có `Email = null` (AI Agent) | **Bỏ qua**, **không** ném |
| Digest: user không có task mở nào | **Không** gửi mail rỗng (`BuildAsync → null`) |
| Digest: user ở 15 workspace | Chỉ **10** đầu (`MaxWorkspacesPerUser`), theo `joined_at ASC` (tất định) |
| Digest: 1 workspace lỗi khi lấy dashboard | Bắt riêng ⇒ **bỏ** workspace đó, **vẫn** gửi các workspace còn lại |
| Digest: `email_messages` quá `MaxDailyEmails` | Dừng lượt, ghi log; **không** gửi phần còn lại |
| Digest: Resend lỗi/timeout | `row.Status = Failed`; lượt chạy **không** ném; user **vẫn** bị coi là "đã xử lý hôm nay"? ⇒ **chốt:** dedupe tính cả row `Failed` (**cố ý** — tránh vòng lặp thử lại gây spam provider; ghi rõ trong doc comment + test `DR-3` chạy lại phải **không** gửi lại) |
| Digest: chạy trên host free-tier ngủ/thức | An toàn vì dedupe ở **DB** (không in-memory) |
| Digest: `Frontend:BaseUrl` rỗng | `DashboardUrl` = `http://localhost:5173/...` (đúng mặc định `BoardModule` 61–71 và `AiModule` 154–158) |
| Digest: **KHÔNG** tạo notification in-app, **KHÔNG** ghi `activity_logs`, **KHÔNG** đi Accountability Layer | Chứng minh bằng test `DA-*`/`DR-*` (đếm bảng liên quan) |
| Profile: `PUT` **không** có `digestEnabled` | **Giữ nguyên** giá trị (không reset về `false`) — test `PE-3` |
| Profile: tắt digest rồi **không** bấm Lưu | `Switch` là local state tới khi gọi API; lỗi API ⇒ rollback + `message.error` (**PF-3**) |
| 401/403/404/409/503 ở mọi endpoint/trang mới | `message.error` / `<Result>` **tiếng Việt** — **không** màn hình trắng |
| `BoardView` mặc định | Luôn là **`kanban`** ⇒ 11 test hiện có **không** đổi |

---

## 9. ⛔ KHÔNG được làm

- ❌ **Không** thêm migration thứ hai. Đúng **1** migration `Phase13DailyDigest` với **1** cột `users.digest_enabled`. `dotnet ef migrations list` phải là **10**.
- ❌ **Không** thêm bảng mới, **không** thêm cột vào bảng nào khác (`email_messages`, `notifications`, `tasks`, `activity_logs` giữ nguyên).
- ❌ **Không** thêm `last_digest_sent_at`/`digest_timezone` hay cột cấu hình nào khác vào `users` (D3, D9).
- ❌ **Không** đổi shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `WorkspaceMemberResponse`, `WorkspaceActivity*`, `ReportSummaryResponse`, payload/tên event SignalR.
- ❌ **Không** sửa `GET /api/boards/{boardId}/tasks` (shape **mảng**), **không** sửa `boardStore`/`useBoard`.
- ❌ **Không** sửa `IEmailGateway` (thêm method vào nó) — dùng **port mới** `IDailyDigestService` (D14).
- ❌ **Không** thêm method vào `IReportService` làm `ReportSummaryResponse` đổi shape; **không** đổi `IReportService.GetSummaryAsync`/`BuildExportAsync`/`ListBoardsAsync`.
- ❌ **Không** thêm `type` vào `NotificationTypes.All` (whitelist chống hallucination của Observer). Digest **không** sinh notification.
- ❌ **Không** dùng `DateTime.Now`/`DateTimeOffset.UtcNow` trong `ReportAggregator.BuildProgressSeries` (phải dùng `snapshot.Now`) hoặc trong `EmailTemplates.DailyDigest` (phải nhận `SendDateLocal` từ tham số).
- ❌ **Không** thêm package NuGet hay thư viện npm (`antd` `Calendar`/`Segmented`/`Statistic`/`Progress`/`Tooltip` + `dayjs` đủ dùng). **Không** `recharts`/`echarts`/`@ant-design/charts`/`chart.js`.
- ❌ **Không** dùng `dangerouslySetInnerHTML` cho email/digest hay cho Calendar tooltip.
- ❌ **Không** quên `HtmlEncoder`/`EncodeMultiline` trong template digest (mọi giá trị nội suy là dữ liệu người dùng).
- ❌ **Không** dùng `Enum.TryParse` trần cho priority (bẫy BUG-1 Phase 10) và **không** quên `ToUtc(...)` cho mọi `DateTimeOffset` (bẫy Npgsql Phase 10).
- ❌ **Không** để module Board tham chiếu module Ai / Reporting (chiều phụ thuộc `Ai → Board`, `Reporting → Board` **một chiều**).
- ❌ **Không** để `Reporting` tham chiếu `Ai` (không đọc `ai_observer_runs` trong progress-series — không cần).
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**); nếu buộc sửa ⇒ ghi rõ lý do + tên test vào báo cáo. **Điều kiện đặc biệt:** `BoardView.test.tsx` (11 test) và `ProfilePage.test.tsx` phải xanh **nguyên trạng**.
- ❌ **Không** mở rộng phạm vi sang: mobile/Flutter (Giai đoạn 14 → theo roadmap mới là 16), AI Task Chat / Observer dự báo (§Giai đoạn 14), Slack/Discord/GitHub (Giai đoạn 15), digest theo tuần, digest tuỳ biến template, timezone riêng từng user, kéo–thả task trên Calendar, iCal export, hard delete.

---

## 10. Thứ tự thi hành đề xuất

1. **§1.2** — xác nhận lại baseline `Skipped: 0` với **423** test hiện có (`TEAMNEXUS_TEST_DB` trỏ PostgreSQL local cổng 5432) **trước** khi sửa gì.
2. **§3.1 (D2)** — `digest_enabled` + migration `Phase13DailyDigest` ⇒ `dotnet build` 0/0, `migrations list` = **10**, `has-pending-model-changes` sạch, **`dotnet test` vẫn 423** (thêm cột không đổi hành vi).
3. **§4 (D5/D6)** — `ReportAggregator.BuildProgressSeries` + `PS-1…PS-10` (thuần, nhanh) ⇒ `dotnet test` **423 + 10**.
4. **§4** — `IReportService.GetProgressSeriesAsync` + endpoint + `PSA-1…PSA-8` ⇒ `dotnet test` **423 + 18**.
5. **§5.2** — `IDailyDigestService` (+ Null) + `DailyDigestService` ⇒ `DA-1…DA-6` ⇒ **423 + 24**.
6. **§5.3** — `DigestOptions` + `EmailTemplates.DailyDigest` + `EmailKinds.DailyDigest` ⇒ `DT-1…DT-8` ⇒ **423 + 32**.
7. **§5.3** — `DigestGateway` + `DailyDigestRunner` + `DailyDigestBackgroundService` + `AiModule` + `appsettings` + `TeamNexusApiFactory` (**P4**) ⇒ `DR-1…DR-4` ⇒ **423 + 36**.
8. **§5.4** — `DigestEnabled` trên profile API + `PE-1…PE-3` ⇒ **423 + 39** (số thật chốt ở đây).
9. **§6.2** — FE Digest toggle (`ProfilePage` tab "Thông báo").
10. **§3** — FE Calendar (`boardCalendar`/`calendarNavigation`/`TaskCalendar`/`BoardView Segmented`) ⇒ `npm test` **trước và sau** từng file.
11. **§6.1** — FE Burndown/Velocity (types → api → hook → utils → components → `ReportsPage`).
12. **§7.2** — nâng 2 cổng CI bằng **số thật**.
13. **§7.3** — cập nhật tài liệu + viết `report/phase-13-visualization-proactive-notifications-test-report.md` + chụp bằng chứng §7.5 (#6).

---

## 11. Rủi ro & giả định

| # | Mục | Xử lý |
|---|---|---|
| **R1** | **Lệch DoD roadmap: có migration** (roadmap §Giai đoạn 13 ghi *"không cần migration mới"*) | Đây là **lệch có chủ ý và bắt buộc** (D2) — không có cột nào tái dùng được cho opt-out server-side. Giảm thiểu: **đúng 1** cột additive, `DEFAULT true` ⇒ **không** backfill, **không** bảng/cột khác. **Bắt buộc** ghi rõ ở `03-roadmap.md` §7.3 (#2) + báo cáo + §9 của tài liệu này |
| **R2** | **Lệch DoD roadmap: có endpoint mới** (*"Không cần API mới ngoài `GET .../tasks/search` và `EmailGateway`"*) | Burndown **không thể** tính ở client (F). Giảm thiểu: **1** endpoint read-only, **Manager+** (đúng quyền `/summary`), dùng lại `ReportService`/`ReportAggregator`/`InvalidReportRangeException`/`ResolveBoardScopeAsync` ⇒ **không** tăng bề mặt quyền, **không** bảng mới. Ghi rõ ở `03-roadmap.md` §7.3 (#2) |
| **R3** | Sửa `BoardView.tsx` (775 dòng) có thể làm đỏ **11 test** đang xanh | D10: `Segmented` + render **có điều kiện**, mặc định `kanban`, **giữ nguyên** cây render, nhãn và `data-testid` cũ. Chạy `npm test -- BoardView` **trước và sau**; nếu buộc sửa ⇒ ghi tên test + lý do vào báo cáo |
| **R4** | Digest chỉ có **một** timezone toàn cục (`DefaultTimeZoneOffsetMinutes = 420`), user ở múi khác nhận mail lệch giờ | Chấp nhận ở quy mô đồ án; **nợ kỹ thuật có chủ ý** (D9) — giải pháp đúng là cột `users.digest_time_zone`, là **cột thứ hai** không có yêu cầu ⇒ để giai đoạn sau. Ghi ở §7.3 (#4) |
| **R5** | `openTasks` là số **tính lại từ dữ liệu hiện tại**, không phải ảnh chụp lịch sử ⇒ task xong ở cột `is_done` nhưng `completed_at = null` (dữ liệu cũ) **không** có ngày đóng | PS-7 chốt: dùng `isDoneColumn` để **loại khỏi `openTasks` từ ngày tạo**, và ghi rõ trong `metricDefinitions` để UI nói thật với người đọc. **Không** bịa ngày đóng |
| **R6** | `email_messages` **không** có cột `recipient_user_id` ⇒ dedupe khoá theo `to_email` | Hợp lệ vì `users.normalized_email` là **UQ** (DB design §3.1) ⇒ 1 email = 1 user. Ghi ở **H** + doc comment `DailyDigestRunner` |
| **R7** | Digest coi row `Failed` là "đã xử lý hôm nay" ⇒ user có thể **mất** digest một ngày khi Resend lỗi | **Cố ý**: thử lại trong cùng ngày có thể lặp vô hạn khi provider outage và ăn vào hạn mức free-tier. Ghi rõ ở §8 + doc comment + test `DR-3`. Phương án tốt hơn (retry có backoff + đếm riêng) là **nợ kỹ thuật** |
| **R8** | `Digest:Enabled` mặc định `false` ⇒ tính năng "im lặng" khi chưa cấu hình | **Cố ý** (D8): tránh CI/gửi mail ngoài ý muốn. **Bắt buộc** có 1 dòng log lúc khởi động (P3) + ghi trong `README`/`appsettings.json` + báo cáo |
| **R9** | Số test thật lệch kỳ vọng (~+39 backend / ~+52 frontend) | **Số đo thắng tài liệu**: cập nhật §7.1 + cổng CI §7.2 + báo cáo |
| **R10** | Tài liệu Giai đoạn 12 ghi **406** test frontend nhưng đo thật **407** tại phiên này | Baseline của giai đoạn này là **407** (số đo thắng tài liệu). Ghi rõ ở §1.2 và ở comment `ci-web.yml` khi nâng cổng (§7.2) |

**Giả định:**
- Baseline **423 backend / 407 frontend (77 file) / 9 migration** — **số đo thắng tài liệu**.
- Có PostgreSQL thật dùng được cho `dotnet test` tại `localhost:5432` (máy dev hiện tại) ⇒ **`Skipped: 0`**; `Docker daemon` **không** chạy nên **không** dựng `postgres:18` cổng 5433.
- Giai đoạn 7–12 đã merge; `IDashboardService`, `TimeProvider`, `TimeProvider` test override, `EmailDispatcher`, `EmailTemplates`, `NullEmailSender`, `Persistence/…/FixedTimeProvider` đều dùng lại được.
- `Program.cs` vẫn gọi `AddBoardModule()` (**36**) → `AddAiModule()` (**39**) → `AddReportingModule()` (**43**) đúng thứ tự này (điều kiện để port/adaptor ghi đè đúng — `AddAiModule` ghi đè `IEmailGateway`/`IActivityLogWriter`/`IAiAgentResolver`/`INotificationWriter`; `AddReportingModule` gọi **sau** để không đổi thứ tự resolve của 2 module cũ).
- `Program.cs` vẫn gọi `AddReportingModule()`; `Reports:Enabled` mặc định `true`.
- Frontend: React 19 + TypeScript 6 + **antd 6.6.3 (`Calendar`/`Segmented`/`Statistic`/`Progress`/`Tooltip` đã có)** + Vite 8 + Vitest 5 + `dayjs` 1.11; toàn bộ nhãn UI **tiếng Việt có dấu**.
- Endpoint mới **append-only**; Flutter (Giai đoạn 16) dùng lại cùng API `.../reports/progress-series` và `.../dashboard`.
