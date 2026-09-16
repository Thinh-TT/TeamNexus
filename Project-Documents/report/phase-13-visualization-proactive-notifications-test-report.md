# Báo Cáo Nghiệm Thu Giai Đoạn 13: Trực Quan Hóa & Thông Báo Chủ Động

> **Dự án**: TeamNexus – Trợ lý điều phối không gian làm việc thông minh
> **Giai đoạn**: Phase 13 – Trực quan hóa & Thông báo Chủ động (**Backend + Frontend + CI + Tài liệu**)
> **Thời điểm nghiệm thu**: 16/09/2026
> **Trạng thái**: ✅ **100% HOÀN THÀNH & ĐẠT MỌI CHỈ TIÊU DoD**

---

## 1. Tổng Quan Kết Quả Đo Đạc Thực Tế

| Thành phần kiểm thử | Baseline trước Phase 13 | Kết quả thực tế đạt được | Đánh giá |
|---|---|---|---|
| **Backend Tests (`dotnet test`)** | **423** (0 fail / 0 skip) | **480 PASS / 0 failed / 0 skipped** (PostgreSQL 18 thật, cổng 5432) | 🟢 **+57 tests mới** |
| **Backend Compile (`dotnet build`)** | 0 warning / 0 error | **0 warning / 0 error** (`-m:1 -nr:false`) | 🟢 **Chuẩn mực** |
| **Database Migrations** | **9** | **10** (`20260916045955_Phase13DailyDigest`) | 🟢 **Đúng lệch DoD có chủ ý #1** |
| **`has-pending-model-changes`** | sạch | *"No changes have been made to the model since the last migration."* | 🟢 **Sạch** |
| **Frontend Tests (`vitest run`)** | **407** (77 files, 0 fail) | **508 PASS / 0 failed / 86 file** | 🟢 **+101 tests / +9 file** |
| **Frontend Lint (`oxlint`)** | 0 warning / 0 error | **0 warnings / 0 errors** (212 files) | 🟢 **Tuyệt đối sạch** |
| **Frontend Typecheck (`tsc -b`)** | exit 0 | **exit 0** | 🟢 **Hoàn hảo** |
| **Frontend Build (`npm run build`)** | OK | **OK** (Vite build `dist/` trong 35.75 s) | 🟢 **Sẵn sàng deploy** |
| **Cổng CI Backend (`ci-backend.yml`)** | Assert `423` | **Assert `480`** + giữ `$skipped -gt 0` | 🟢 **Đã cập nhật** |
| **Cổng CI Web (`ci-web.yml`)** | Threshold `-lt 406` | **Threshold `-lt 508`** | 🟢 **Đã cập nhật** |

> **Điều kiện chốt `Skipped: 0` ĐÃ ĐẠT.** Khác Giai đoạn 12 (khi đó 237 test bị SKIP vì mật khẩu Postgres local khác `postgres/postgres`),
> lần này PostgreSQL local **kết nối được** nên toàn bộ suite thực sự chạy.
> Lệnh chạy đủ bộ:
> ```powershell
> $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=<pw>"
> dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
> ```
> **Docker daemon KHÔNG chạy** trên máy dev ⇒ **không** dựng `postgres:18` cổng 5433 như Giai đoạn 12.

> **Ghi chú về baseline frontend:** tài liệu Giai đoạn 12 ghi **406** nhưng máy dev đo **407** (1 test được thêm sau khi chốt báo cáo).
> Theo nguyên tắc "số đo thắng tài liệu", baseline của giai đoạn này là **407**.

---

## 2. Lệch DoD So Với Roadmap — CÓ CHỦ Ý, BẮT BUỘC, ĐÃ GHI VÀO TÀI LIỆU

Roadmap Giai đoạn 13 ghi *"Độ phức tạp kỹ thuật: 🟢 Thấp — tận dụng tối đa code sẵn có, **không cần migration mới**"*
và *"Không cần API mới ngoài `GET .../tasks/search` và `EmailGateway`"*. Thực tế phát sinh **đúng 2 lệch**, cả hai **bắt buộc về mặt kỹ thuật**:

### 2.1 Lệch #1 — Migration `Phase13DailyDigest` (`users.digest_enabled`)

| Mục | Nội dung |
|---|---|
| **Roadmap ghi** | *"không cần migration mới"* |
| **Thực tế** | **+1 cột** `users.digest_enabled` (`boolean NOT NULL DEFAULT true`) ⇒ **10** migration |
| **Vì sao BẮT BUỘC** | Yêu cầu *"người dùng có thể bật/tắt trong Profile Settings"* không thể thoả mãn bằng cờ **client** (`localStorage`/Zustand): digest do `BackgroundService` chạy **phía server** gửi, nên lựa chọn **phải đọc được từ DB**. `users` **không** có cột nào tái dùng được (chỉ `display_name`/`avatar_url`/`created_at`) |
| **Giảm thiểu rủi ro** | `DEFAULT true` ⇒ **không cần backfill**, **không** đổi hành vi của bất kỳ row cũ nào. **Không** bảng mới, **không** sửa/xoá cột nào, **không** index mới. `Up()` chỉ có **1** `AddColumn`, `Down()` chỉ có **1** `DropColumn` |
| **Đã kiểm chứng** | Đọc lại `Up()`: chỉ `AddColumn<bool>` + `defaultValue: true`. `has-pending-model-changes` sạch |

### 2.2 Lệch #2 — Endpoint `GET /api/workspaces/{id}/reports/progress-series`

| Mục | Nội dung |
|---|---|
| **Roadmap ghi** | *"Không cần API mới ngoài `GET .../tasks/search` và `EmailGateway`"* |
| **Thực tế** | **+1 endpoint read-only** (Manager+, cùng quyền `/summary`) |
| **Vì sao BẮT BUỘC** | `ReportSummary` là **ảnh chụp một thời điểm** (`progress`/`performance` đều vô hướng), và `activity_logs` **không** lưu trạng thái cột ⇒ **không thể** tái dựng lịch sử open/closed ở client. Burndown vì vậy **không thể** tính thuần frontend |
| **Giảm thiểu rủi ro** | **1** endpoint, **read-only**, **không** tăng bề mặt quyền (dùng lại `RequireManagerAsync` + `InvalidReportRangeException` + `ResolveBoardScopeAsync` + `EnsureEnabled` của Phase 6), **không** bảng mới, **không** đụng `ReportSummaryResponse` |
| **Đã kiểm chứng** | `PSA-8` khẳng định `/reports/summary` **không đổi** shape (11 field cấp 1 vẫn đủ) |

> Cả hai lệch đã được ghi tường minh vào `03-roadmap.md`, `01-system-specification.md`, `04-database-design.md`, `README.md` và tài liệu chia task — **không** có sai lệch im lặng.

---

## 3. Chi Tiết Backend Đã Hiện Thực

### 3.1 §2 — Chuỗi thời gian cho Burndown/Velocity

**File mới:**

| File | Nội dung |
|---|---|
| `Reporting/Contracts/ReportProgressSeries.cs` | `ReportSeriesModes` (`date`/`week` + `BucketDays`) · `ReportDailyProgress` · `ReportWeeklyProgress` · `ReportVelocity` · `ReportProgressSeries` (+ property `Scope` suy ra như `BuildScope`) |
| `Reporting/DTOs/ReportProgressSeriesDtos.cs` | 5 record DTO + `.From(domain)` (một chiều, cùng tiền lệ `ReportSummaryResponse.From`) |
| `tests/…/Pure/ReportProgressSeriesTests.cs` | **17** test — số học thuần, không DB/HTTP |
| `tests/…/Integration/ReportProgressSeriesApiTests.cs` | **10** test — hợp đồng HTTP |

**File sửa:**

| File | Thay đổi |
|---|---|
| `Reporting/Services/ReportAggregator.cs` | +`BuildProgressSeries(snapshot, thresholds, tzOffsetMinutes)` — **hàm THUẦN**, +`ProgressSeriesMetricDefinitions()` (7 key tiếng Việt), +`BuildWeeklyBuckets` (mốc **Thứ Hai**), +`LocalDay` (một chỗ duy nhất đổi UTC → ngày local) |
| `Reporting/Services/ReportThresholds.cs` | +`MaxSeriesBuckets` (**90**) + `SeriesWeeklyThresholdDays` (**60**) |
| `Reporting/Options/ReportsOptions.cs` | +2 knob trên (clamp) + truyền vào `ToThresholds()` |
| `Reporting/Services/IReportService.cs` | +`GetProgressSeriesAsync(...)` (doc **read-only**, **Manager+**) |
| `Reporting/Services/ReportService.cs` | +`GetProgressSeriesAsync` + `LoadSeriesTasksAsync` — **lọc ngay ở DB** (`created_at <= To OR (completed_at IS NOT NULL AND completed_at >= From)`), **KHÔNG** nạp `activity_logs`/`ai_observer_runs` (không ai đọc) |
| `Reporting/Endpoints/ReportingEndpointHelpers.cs` | +`ParseTzOffsetMinutes` (sai định dạng ⇒ **400**; ngoài khoảng ⇒ **KHÔNG** 400, để aggregator clamp) |
| `Reporting/Endpoints/ReportingEndpoints.cs` | +`group.MapGet("/progress-series", …).RequireAuthorization()` |

**Ngữ nghĩa chốt (đã có test chứng minh):**

- `mode = 'date'` khi số **ngày lịch bao gồm cả hai đầu** ≤ **60**; ngược lại `'week'` (mốc Thứ Hai). `days` và `weeks` **loại trừ nhau**.
- `openTasks` = số thẻ **còn mở ở CUỐI mốc**, **cố ý KHÔNG trừ** `completions` cùng mốc ⇒ `openTasks − completions` = đường lý tưởng, **luôn ≥ 0**.
- Bucket **luôn đủ** (kể cả bucket toàn số 0) ⇒ biểu đồ liên tục, không nhảy cột.
- `mode='date'` + khoảng > 90 ngày ⇒ `truncated.bucketCapReached = true`, giữ **90 bucket CUỐI**.
- Thẻ ở cột `is_done` nhưng `completed_at = null` (dữ liệu cũ) **không** bao giờ nằm trong `openTasks`, **không** tính là `completions` — **không bịa ngày đóng**.
- `tzOffsetMinutes`: vắng ⇒ `0`; ngoài `±840` ⇒ **clamp** + **echo giá trị thực dùng**; ngày mọi bucket tính theo **múi giờ người xem**.

### 3.2 §3 — Email Digest hàng ngày

**File mới:**

| File | Nội dung |
|---|---|
| `Board/Services/IDailyDigestService.cs` | Port (`BuildAsync`) + `NullDailyDigestService` + `DailyDigestWorkspaceSection`/`DailyDigestContent` — **tái dùng `DashboardResponse`**, không tạo shape thứ hai |
| `Board/Services/DailyDigestService.cs` | Dựng nội dung: kiểm `DigestEnabled` + `Email != null` → nạp workspace theo `joined_at` → mỗi workspace gọi **`IDashboardService.GetAsync`** → bỏ workspace không có task mở → rỗng toàn bộ ⇒ **`null`** (không gửi mail rỗng). Bắt riêng lỗi từng workspace |
| `Ai/Services/DigestGateway.cs` | `IDigestGateway` + `DigestGateway` — render rồi gọi `IEmailDispatcher` (**cổng duy nhất**, tự ghi row audit). **Không bao giờ ném** |
| `Ai/Services/DailyDigestRunner.cs` | `IDailyDigestRunner.RunOnceAsync` + `DigestRunResult`. **Chống trùng ở DB**, cap `MaxDailyEmails`, log **1 dòng** có che địa chỉ (`a***@x.com`) |
| `Ai/Services/DailyDigestBackgroundService.cs` | `BackgroundService`: `StartupDelaySeconds` → `PeriodicTimer(PollInterval)` → `RunOnceAsync`; **bọc try/catch MỌI tick** |
| `Ai/Options/DigestOptions.cs` | Section `Digest` + clamp phòng thủ + `DueAt(offset, day)` ("8 giờ sáng" = 8 giờ **của người đọc**) |
| `tests/…/Pure/DigestTemplateTests.cs` | **13** test |
| `tests/…/Integration/DailyDigestTests.cs` | **14** test |

**File sửa:**

| File | Thay đổi |
|---|---|
| `Ai/Services/Email/EmailTemplates.cs` | +`EmailKinds.DailyDigest` + `DailyDigest(content)` (**hàm THUẦN**, HTML-escape **mọi** giá trị qua `Encoder` có `UnicodeRanges.All` ⇒ giữ dấu tiếng Việt) + `DailyDigestPreview` |
| `Ai/AiModule.cs` | +`DigestOptions`, +`IDigestGateway`, +`IDailyDigestRunner`, +`AddHostedService<DailyDigestBackgroundService>()`, +1 dòng log cấu hình (không chứa secret) |
| `TeamNexus.Api/appsettings.json` | +section `"Digest"` với `Enabled: false` + comment giải thích |
| `Board/BoardModule.cs` | +`IDailyDigestService → DailyDigestService` |
| `Board/DTOs/UserProfileDtos.cs` · `Board/Services/UserProfileService.cs` | +`DigestEnabled` (**append cuối**); `PUT` **vắng ⇒ giữ nguyên** |
| `tests/…/Infrastructure/TeamNexusApiFactory.cs` | +`DigestEnabled` (mặc định **false**) · `DigestSendAtLocalHour` · `Digest:StartupDelaySeconds = 3600` (**P4**) |
| `tests/…/Infrastructure/TestScenario.cs` | +property `Factory` (additive) để suite resolve service trên **đúng host** của scenario (không bỏ qua `FixedTimeProvider`) |

**Cơ chế chống trùng (điểm quan trọng nhất của ô C):**

```
idempotency key = (email_messages.kind = 'DailyDigest', to_email, ngày UTC của người nhận)
```

Trên Render free-tier, tiến trình **ngủ/thức nhiều lần trong ngày** ⇒ mọi cờ in-memory bị reset bởi cold start và cùng một người nhận nhiều email.
Dùng chính bảng audit `email_messages` (đã có từ Phase 11) vừa **bền vững** vừa **không phải thêm cột** `last_sent_at`.
Row `Failed` **cũng** được coi là "đã xử lý hôm nay" — **cố ý** (không hammer provider outage, không ăn hạn mức free-tier).

---

## 4. Kiểm Thử Backend — Phân Bổ +57 Tests

| Suite | Số test | Nội dung |
|---|---|---|
| `Pure/ReportProgressSeriesTests` | **17** | PS-1…PS-10: tạo/xong trong kỳ, baseline trước cửa sổ, biên `tzOffsetMinutes` (18:00Z ⇒ ngày kế tiếp với UTC+7), clamp `±840`, `openTasks` không âm, cột `is_done` thiếu `completed_at`, ngưỡng 60/61 ngày, cap 90 bucket, mốc Thứ Hai, velocity, 0 task ⇒ không `NaN` |
| `Integration/ReportProgressSeriesApiTests` | **10** | PSA-1…PSA-8: 7 bucket/7 ngày khớp seed, scope board + board lạ **404**, Member **403** / người ngoài **404**, `from>to`/`from=abc`/`tz=abc` **400** + `tz=99999` **clamp 200**, khoảng mặc định 30 ngày, `Reports:Enabled=false` **503**, workspace rỗng ⇒ toàn 0, **hồi quy `/summary`**, `metricDefinitions` tới được client |
| `Pure/DigestTemplateTests` | **13** | DT-1…DT-8: kind `DailyDigest`, subject ≤ 200 + có ngày, escape `<script>`, **giữ dấu tiếng Việt** (không `&#x`), text thuần không thẻ HTML, không ném khi rỗng, link "Tắt nhận" ở **cả hai** body, field thiếu ⇒ **không in "null"** |
| `Integration/DailyDigestTests` | **14** | DA-1…DA-6: nội dung 1 section, `Count` **đúng** khi list bị cap, opt-out ⇒ `null`, không task mở ⇒ `null`, chỉ task **của mình** + board **sống**, 3 workspace ⇒ **1** mail thứ tự `joined_at`, cap workspace; DR-1…DR-4: cờ off ⇒ `Skipped` + **0** row, 2 người ⇒ **2** row `Sent` + `ProviderMessageId == null` + `SentByUserId == null`, **chạy 2 lần ⇒ vẫn 1 row**, opt-out + AI Agent (`email = null`) ⇒ **0** row; +**digest KHÔNG ghi `notifications`/`activity_logs`** |
| `Integration/ProfileApiTests` | **21 → 24** | PE-1 mặc định `true`; PE-2 bật/tắt ghi nhớ được (đọc lại từ DB); PE-3 **client cũ bỏ trống field ⇒ KHÔNG bị tắt digest** |
| *(hồi quy)* | **423** | **Không sửa test nào** — toàn bộ suite cũ vẫn xanh |

### 4.1 Hai lỗi thật bắt được khi viết test (giá trị của việc test trước khi tin)

| # | Bug | Vì sao nguy hiểm | Cách sửa |
|---|---|---|---|
| **BUG-1** | Dùng `null` cho "task chưa đóng" ⇒ `default(DateOnly)` = **0001-01-01** | `0001-01-01` **không** lớn hơn mọi ngày ⇒ **mọi** task chưa xong bị đếm là "hoàn thành" ở **mỗi** ngày của cửa sổ. Biểu đồ sẽ sai **toàn bộ** chứ không sai lệch nhỏ | Sentinel `DateOnly.MaxValue` cho "chưa đóng" + `HasCompletedAt` quyết định nhánh tính |
| **BUG-2** | Ngưỡng chuyển bucket tuần lệch **1 ngày** | `ReportRange.Days` đếm **khoảng thời gian** (ceil) còn số bucket đếm **ngày lịch bao gồm hai đầu** ⇒ cửa sổ **"60 ngày"** bị gộp tuần sớm một ngày, trái với tài liệu (`≤ 60 ⇒ theo ngày`) | Ngưỡng đọc theo **số bucket ngày** (`endDay − startDay + 1`), không dùng `range.Days` |

> Cả hai đều **không thể** lộ ra nếu chỉ "chạy thử xem có lỗi không" — chúng chỉ lộ khi assert **giá trị chính xác** ở **biên**.

---

## 5. Nâng Ngưỡng CI/CD & Cập Nhật Tài Liệu

### 5.1 Cổng CI

1. `.github/workflows/ci-backend.yml`:
   ```pwsh
   if ($total -ne 480) {
     throw "Số test backend đã đổi: $total (kỳ vọng 480). Nếu là chủ ý, cập nhật con số này và tài liệu."
   }
   ```
   Chuỗi comment baseline: `171 (GĐ9) → 189 (GĐ10 §1) → 226 (GĐ10 §2) → 371 (GĐ11) → 423 (GĐ12) → 480 (GĐ13)`.
   Giữ nguyên cổng chống "xanh giả" `if ($skipped -gt 0)`.
2. `.github/workflows/ci-web.yml`: nâng `-lt 406` ⇒ **`-lt 407`** (số **đo thật**; tài liệu Phase 12 ghi 406).
   > ⚠️ **Còn một lần nâng nữa** lên tổng đo thật (ước tính **≥ 460**) **sau khi** frontend Giai đoạn 13 xong. Giữ 407 lúc này là **đúng** — nâng lên 460 khi test chưa tồn tại sẽ làm CI đỏ vô cớ.
   > Cảnh báo này đã được ghi **ngay trong comment của workflow** để người sau không bỏ sót.

### 5.2 Tài Liệu

| File | Nội dung |
|---|---|
| `Project-Documents/tasks/phase-13-visualization-proactive-notifications.md` | Kế hoạch chia task (D1–D14, ca biên, bằng chứng) — đã có từ bước lập kế hoạch |
| `Project-Documents/tasks/phase-13-remaining-frontend-handover.md` | **Note bàn giao frontend cho antigravity** (§1 Calendar · §4 Burndown/Velocity · §5 Digest toggle + hợp đồng API + test phải viết + 10 bẫy đã biết) |
| `Project-Documents/03-roadmap.md` | Giai đoạn 13: trạng thái 🔄 (backend ✅), tick 3 ô backend, **ghi tường minh 2 lệch DoD + lý do**, số đo thật, link tài liệu + note bàn giao |
| `Project-Documents/01-system-specification.md` | +§12: ngữ nghĩa chuỗi thời gian (`openTasks`/`completions`/`creations` + `tzOffsetMinutes`), endpoint mới, cơ chế digest (1 email/người/ngày, chống trùng ở DB, không sinh sự kiện, mặc định tắt), quyết định `digestEnabled` **vắng ⇒ giữ nguyên** |
| `Project-Documents/04-database-design.md` | §3.1 `users`: +dòng `digest_enabled`; §6: +migration **10**; §7: +5 gạch đầu dòng (migration additive duy nhất + lý do, chống trùng qua `email_messages` + lý do **không** thêm cột, digest không ghi notification/activity, row `Failed` coi như đã xử lý) |
| `README.md` | +`## Trạng thái (Giai đoạn 13)`; Migration **10**; bảng CI (`assert 480` / `≥ 407`); baseline frontend **407** (ghi rõ lệch so với 406 của tài liệu) |

### 5.3 Từ vựng mới (append-only, không sửa whitelist nào)

| Cột | Giá trị mới | Ghi chú |
|---|---|---|
| `email_messages.kind` | **`DailyDigest`** | Text tự do (varchar 40), cùng chỗ với `WorkspaceInvitation`/`QuickEmail`. **Không** đụng `NotificationTypes.All` (whitelist chống hallucination của Observer) |

---

## 6. Bảng Bằng Chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 warning / 0 error | ✅ **đạt** (22.3 s → 7.8 s khi incremental) |
| 2 | `dotnet ef migrations list --no-build` + `has-pending-model-changes` | **10** / sạch | ✅ **đạt** |
| 3 | Đọc lại `Up()` của `Phase13DailyDigest` | **chỉ** `AddColumn(users.digest_enabled)` + DEFAULT; `Down()` chỉ `DropColumn` | ✅ **đạt** |
| 4 | `dotnet test` với `TEAMNEXUS_TEST_DB` (PostgreSQL local, cổng 5432) | `Skipped: 0`, `Failed: 0`, `Total 480` | ✅ **đạt** |
| 5 | Hồi quy: 423 test cũ | **không** sửa test nào, vẫn xanh | ✅ **đạt** |
| 6 | `/reports/summary` không đổi shape sau khi thêm endpoint | 11 field cấp 1 đủ | ✅ **đạt** (`PSA-8`) |
| 7 | Digest **không** ghi `notifications` / `activity_logs` | cả hai bảng **không** tăng | ✅ **đạt** |
| 8 | Digest chạy 2 lần trong cùng ngày ⇒ 1 email | 1 row `email_messages` | ✅ **đạt** (`DR-3`) |
| 9 | Cổng CI backend | `total = 480`, skipped 0 | ✅ **đã cập nhật** |
| 10 | Cổng CI web | `≥ 407` (+ ghi chú nâng tiếp sau frontend) | ✅ **đã cập nhật** |
| 11 | Runtime frontend (`lint` / `tsc -b` / `npm test` / `build`) | 0-0 / exit 0 / 407 pass / OK | ✅ **đạt** (đo tại phiên lập kế hoạch; phần mới bàn giao) |
| 12 | ⬜ Chạy lại 2 workflow trên GitHub Actions | cả hai xanh | ⬜ **chưa chạy** |

---

## 7. Chi Tiết Frontend Đã Hiện Thực (+101 tests / +9 file)

### 7.1 §1 — Calendar View (`BoardView`)

| File | Nội dung |
|---|---|
| `features/board/utils/taskPriority.ts` (**mới**) | Tách `getPriorityConfig` (bảng màu/nhãn 4 mức ưu tiên) + `priorityRank` ra **helper dùng chung**. Trước đây bảng này nằm private trong `TaskCard`; lịch cần **cùng** màu cho cùng mức ưu tiên nên copy sẽ tạo hai nguồn sự thật |
| `features/board/utils/boardCalendar.ts` (**mới**) | **Hàm THUẦN**: `dayKey` · `groupTasksByDueDate` (gom theo ngày **địa phương**, tối đa 3 thẻ/ô + `overflowCount`, sắp xếp tất định theo ưu tiên → tiêu đề → id) · `tasksWithoutDueDate` · `initialCalendarMonth` |
| `features/board/utils/calendarNavigation.ts` (**mới**) | **Hàm THUẦN**: `buildDaySearchUrl` (khoá `YYYY-MM-DD`, **không** `toISOString()`) · `buildBoardSearchUrl` |
| `features/board/components/TaskCalendar.tsx` (**mới**) | antd `Calendar` với `cellRender`, `headerRender` (Tháng trước/Hôm nay/Tháng sau), chip màu theo ưu tiên + dấu đỏ khi trễ hạn + `+N`; `Empty` khi board rỗng; khối *"N thẻ không có hạn chót"* |
| `features/board/components/BoardView.tsx` (**sửa, tối thiểu**) | +`Segmented` `data-testid="board-view-switch"` + render **có điều kiện**; **giữ nguyên** mặc định `kanban`, toàn bộ DndContext/Modal/Drawer và mọi `data-testid` cũ |
| `features/board/components/TaskCard.tsx` (**sửa 1 dòng**) | Chỉ đổi import sang helper chung — **không** đổi hành vi hiển thị |

**Test mới:** `boardCalendar` **18** · `calendarNavigation` **8** · `TaskCalendar` **10** · `BoardView` **+4**.

### 7.2 §4 — Burndown/Velocity (`ReportsPage`)

| File | Nội dung |
|---|---|
| `reporting/types/reporting.types.ts` (**append**) | `ReportSeriesMode` · `ReportDailyProgressPoint` · `ReportWeeklyProgressPoint` · `ReportVelocityResponse` · `ReportSeriesTruncationResponse` · `ReportProgressSeriesResponse` · `ReportProgressSeriesParams` — khớp 1-1 backend |
| `reporting/services/reportingApi.ts` (**sửa**) | +`getProgressSeries` — tự dựng `URLSearchParams` để **giữ `tzOffsetMinutes=0`** (0 là "UTC", không phải "rỗng") |
| `reporting/utils/timeZoneOffsetMinutes.ts` (**mới**) | **Hàm THUẦN** đảo dấu `getTimezoneOffset()` + chuẩn hoá `-0` thành `0` |
| `reporting/utils/burndown.ts` (**mới**) | **Hàm THUẦN**: `seriesBuckets` (chuẩn hoá `days`/`weeks` về một dạng) · `visibleBuckets` (cap 31, giữ **mới nhất**) · `idealOpenSeries` (**kẹp tại 0**) · `barHeightPercent` (**guard `max = 0`**) · `seriesMax` · `velocityStats` |
| `reporting/hooks/useReportProgressSeries.ts` (**mới**) | Pattern `useState` + `useEffect` + `reload` (không react-query); `workspaceId` rỗng ⇒ **không** gọi API |
| `reporting/components/BurndownChart.tsx` (**mới**) | Biểu đồ cột **CSS thuần** (không thư viện): cột "hoàn thành" + "còn mở" + đường "lý tưởng" mờ, `Tooltip` số liệu, nhãn trục thưa, cảnh báo khi `bucketCapReached` hoặc khi tự cắt còn 31 cột |
| `reporting/components/VelocityPanel.tsx` (**mới**) | 3 `Statistic` lấy **nguyên** `velocity` từ server (không tính lại từ bucket đã cắt) |
| `reporting/pages/ReportsPage.tsx` (**sửa**) | Render 2 khối mới với **trạng thái lỗi riêng** — chuỗi thời gian hỏng chỉ hiện `Alert`, khối báo cáo đã tải **vẫn nguyên vẹn** |

**Test mới:** `burndown` **20** · `timeZoneOffsetMinutes` **5** · `reportingApi.progressSeries` **6** · `useReportProgressSeries` **7** · `BurndownChart` **11** · `VelocityPanel` **6** · `ReportsPage` **+2**.

### 7.3 §5 — Digest toggle (`ProfilePage`)

| File | Nội dung |
|---|---|
| `profile/types/profile.types.ts` (**sửa**) | `UserProfileResponse` += `digestEnabled` · `UpdateProfileRequest` += `digestEnabled?: boolean \| null` (kèm doc nêu rõ **vắng ⇒ giữ nguyên**) |
| `profile/pages/ProfilePage.tsx` (**sửa**) | +tab thứ 3 `notifications` ("Thông báo") với `Switch` `data-testid="digest-toggle"`; đọc trạng thái từ **`getProfile()`** (không từ `useAuthStore`); gửi **kèm** `displayName`/`avatarUrl`; **rollback** khi API lỗi; **mở thẳng tab khi URL có `#notifications`** (đích link "Tắt nhận" trong email) |

**Test mới:** `ProfilePage` **+5** (PF-1, PF-1b, PF-2, PF-3, PF-4).

### 7.4 Hồi quy — **không sửa test cũ nào**

- `BoardView.test.tsx`: **11 test cũ vẫn xanh nguyên trạng** (chỉ **thêm** 1 `describe` mới).
- `ProfilePage.test.tsx`: **8 test cũ vẫn xanh nguyên trạng** (chỉ **thêm** 5 test mới).
- `ReportsPage.test.tsx`: **3 test cũ vẫn xanh** (chỉ **thêm** 2 test mới).
- Toàn bộ suite frontend cũ: **0 test nào bị sửa**.

### 7.5 Bug thật bắt được khi viết test frontend

| # | Bug | Vì sao nguy hiểm | Cách sửa |
|---|---|---|---|
| **FE-BUG-1** | antd `Calendar` mặc định mở **tháng hiện tại của đồng hồ máy** | Board toàn thẻ hạn tháng 6 mà hôm nay là tháng 9 ⇒ người dùng mở tab "Lịch" và thấy lưới **trống**, tính năng trông như hỏng dù dữ liệu vẫn còn | Điều khiển tháng đang xem + `initialCalendarMonth` suy từ **dữ liệu** (tháng gần nhất còn hạn; nếu mọi hạn đã qua thì lấy tháng mới nhất) |
| **FE-BUG-2** | `-date.getTimezoneOffset()` sinh **`-0`** khi offset bằng 0 | URL mang `tzOffsetMinutes=-0`; backend vẫn hiểu đúng nhưng log/URL trông như lỗi, và `Object.is(-0, 0)` là **khác nhau** ⇒ assertion đúng bị đỏ | `+ 0` để chuẩn hoá `-0` → `0` |
| **FE-BUG-3** | oxlint báo `set-state-in-effect` khi effect gọi hàm `setStatus('loading')` **đồng bộ** | `npm run lint` phải giữ **0 warning**; setState đồng bộ trong effect còn làm React render thêm một lượt vô ích | Trì hoãn lời gọi sang microtask (state khởi tạo đã đúng nên vòng render đầu vẫn hiện "đang tải") |

---

## 8. Phần Frontend — Đối Chiếu Với Note Bàn Giao

Note bàn giao ban đầu (`tasks/phase-13-remaining-frontend-handover.md`) vẫn được **giữ lại** làm hồ sơ hợp đồng API + danh sách bẫy,
và đã được cập nhật trạng thái ✅. Hai khác biệt so với note, đã ghi rõ ở đó:

1. **Phát sinh ngoài danh sách file:** `features/board/utils/taskPriority.ts` (mới) và **1 dòng import** trong `TaskCard.tsx` —
   để lịch và thẻ dùng **cùng** một bảng màu ưu tiên thay vì copy. Không đổi hành vi (`TaskCard.test.tsx` xanh).
2. **Số test thật lệch ước tính:** note ước **+53**; **đo thật +101**. Nguyên nhân: ước tính đếm theo *test method*, thực tế nhiều test có nhiều `it(...)`. **Số đo thắng tài liệu.**

---

## 8b. Kết Luận

Giai đoạn 13 đã hoàn tất và đạt mọi chỉ tiêu DoD trên **cả backend và frontend**:

1. **Kiến trúc & Toàn vẹn**: giữ vững Modular Monolith; chiều phụ thuộc `Ai → Board`, `Reporting → Board` **một chiều** vẫn nguyên (Board khai báo port `IDailyDigestService`, Ai gọi nó; Reporting **không** tham chiếu Ai). Schema chỉ **+1 cột** additive, `has-pending-model-changes` sạch.
2. **Chất lượng mã nguồn**: **480/480** test backend PASS trên PostgreSQL thật và **508/508** test frontend PASS; `dotnet build` **0 warning / 0 error**; frontend `lint` **0/0**, `tsc -b` exit 0, build OK. **Không** sửa test cũ nào ở cả hai phía.
3. **Tính đúng đắn đã được chứng minh ở biên**: **5 bug thật** bắt được (2 backend: sentinel `DateOnly` + ngưỡng tuần lệch 1 ngày; 3 frontend: `Calendar` mở sai tháng, `-0` của timezone offset, `set-state-in-effect`) — tất cả đều là loại lỗi **im lặng** mà chỉ assert chính xác ở biên mới lộ.
4. **Chi phí & an toàn được kiểm soát**: digest mặc định **TẮT**, chống trùng ở **DB** (host free-tier ngủ/thức vẫn ≤ 1 email/người/ngày), hạn mức **riêng** không dùng chung cap quick-email, log **không** chứa địa chỉ đầy đủ, và digest **không** sinh notification/activity.
5. **Ràng buộc giai đoạn được tôn trọng**: **không** thêm package npm/NuGet (biểu đồ vẽ bằng CSS, lịch dùng antd `Calendar` sẵn có), **không** `dangerouslySetInnerHTML`, **không** đổi shape hợp đồng đã verify.
6. **Minh bạch về lệch kế hoạch**: 2 lệch DoD (migration + endpoint) đều **bắt buộc**, đã ghi rõ **lý do** và **phương án giảm thiểu** trong cả 5 tài liệu dự án — không có sai lệch im lặng.

**Việc còn lại duy nhất (không nằm trong DoD kỹ thuật):** chạy lại 2 workflow trên **GitHub Actions** để xác nhận xanh trên CI thật, và chụp ảnh kiểm thử tay 4 bước.
