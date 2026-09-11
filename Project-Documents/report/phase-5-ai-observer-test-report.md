# BÁO CÁO KIỂM THỬ GIAI ĐOẠN 5: AI OBSERVER (BACKEND)

**Dự án:** TeamNexus – AI-Powered Collaborative Workspace
**Giai đoạn:** Phase 5 – AI Observer (§1 Schema, §2 Activity log, §3 Detector, §4 Service/AI/Notification, §5 DTO/Endpoints/DI, §7.1 Verify backend)
**Đối tượng kiểm thử:** `activity_logs` / `notifications` / `ai_observer_runs`, `ObserverSignalDetector`, `ObserverSummarizer`, `ObserverFindingValidator`, `NotificationService`, `ObserverService`, `ObserverBackgroundService`, 6 endpoint mới
**Ngày thực hiện:** 10/09/2026
**Người thực hiện:** DSH coding agent (backend §1–§5 + §7.1); frontend §6/§7.2 bàn giao antigravity
**Môi trường thực thi:**
- Backend: ASP.NET Core (.NET 10), `ASPNETCORE_ENVIRONMENT=Development`
- AI provider: `FakeAiProvider` (`DeepSeek__ApiKey` để trống) hoặc stub điều khiển được ⇒ **không gọi DeepSeek thật, không tốn token**
- CSDL: PostgreSQL **18.4** local (`localhost:5432`, DB `TeamNexus`), migration `Phase5AiObserverSchema` (`20260910105154`)
- Xác thực: JWT tự ký bằng `Jwt:SigningKey` trong User Secrets, đặt trong cookie `access_token`; CSRF lấy từ cookie `XSRF-TOKEN` **sau khi gắn JWT**
- Cổng harness: 5198 (§2), 5210 (§5), 5200–5202 (boot app thật)

---

## 1. Mục Tiêu & Tiêu Chí Nghiệm Thu

1. **Schema đúng thiết kế** (`04-database-design.md` §3.6): 3 bảng, 3 cột `jsonb`, CHECK `status`, FK `RESTRICT`, index theo workspace/người nhận.
2. **Thu thập hoạt động**: mọi mutation task/comment phát 1 sự kiện vào `activity_logs`, **không** làm hỏng request CRUD.
3. **Phát hiện bất thường**: 3 tín hiệu bắt buộc + 1 optional, ngưỡng cấu hình được, hàm **thuần & tất định**.
4. **Tóm tắt trước khi gửi AI**: prompt ≤ `MaxPromptCharacters`, **0 lần gọi AI khi không có tín hiệu**, token ghi vào run summary.
5. **Chỉ Manager/Admin thấy cảnh báo**; notification 1 row/người nhận, dedupe 24h, mark-read idempotent.
6. **Observer là lớp đọc/cảnh báo**: không ghi `tasks`/`labels`/`task_labels`, không đi qua `AiActionService`.
7. **Hợp đồng HTTP**: 200/400/401/403/404/405/502, body lỗi `{ error }`, camelCase.

## 2. Phương Pháp Kiểm Thử

| Lớp | Công cụ | Phạm vi | Kết quả |
|---|---|---|---|
| A. Schema | Harness Npgsql (`information_schema`/`pg_constraint`/`pg_indexes`) + round-trip EF | Cấu trúc, jsonb, CHECK, FK RESTRICT | **28/28 PASS** |
| B. Hàm thuần | Console harness (không DB/HTTP/AI) | Detector, severity, thresholds, window, prompt | **44/44 PASS** |
| B2. Hàm thuần §4 | Console harness | Validator, summarizer, `FakeAiProvider` | **15/15 (+7/7 chạy lại sau fix)** |
| C. Background service | `IHost` thật + `AddAiModule` | Vòng đời host, `Enabled` on/off | **3/3 PASS** |
| D. Scan end-to-end | DI thật + PostgreSQL thật + stub provider | 9 bước scan, ghi run + notification | **6/6 PASS** |
| E. Chi phí | Stub đếm số lần gọi | 0 tín hiệu, cap prompt, token | **3/3 PASS** |
| F. HTTP API §5 | API thật (cổng 5210) + JWT + CSRF + stub | 6 endpoint, quyền, mã lỗi | **32/32 PASS** |
| G. Activity log | API thật (cổng 5198) + PostgreSQL thật | 6 loại sự kiện, mutation thất bại | **31/31 PASS** |
| H/I/J | API thật + DI thật | Dedupe, concurrency, failure modes | **2/2, 3/3, 4/4 PASS** |
| **Tổng** | | | **178 check PASS** |

> Harness nằm **ngoài workspace** (`%TEMP%`), **đã xoá** sau khi chạy; DB dev được trả về baseline; API test **đã stop** (các cổng đều đóng).

## 3. Kết Quả Chi Tiết Theo Nhóm

### 3.1 A — Schema & migration (28/28)

| Mã | Nội dung | Kết quả |
|---|---|---|
| A1 | 3 bảng đúng cột/thứ tự/kiểu (`activity_logs` 10, `notifications` 11, `ai_observer_runs` 8) | ✅ |
| A2 | Đúng 3 cột `jsonb` (`payload` ×2, `summary`) | ✅ |
| A3 | `ck_ai_observer_runs_status` đủ 4 giá trị; `status` = `varchar(16)` | ✅ |
| A4 | 6 index (3 khai báo + 3 PK/FK); **6 FK đều `ON DELETE RESTRICT`** | ✅ |
| A5 | Round-trip insert→read: timestamp stamp, `IsRead=false`, `ReadAt=null`, `Skipped` đọc đúng enum; jsonb hỏng → **`22P02`**; xoá cha còn tham chiếu → **`23001`**; `status='Bogus'` → **`23514`** | ✅ |
| A6 | `dotnet ef migrations list` → 5 migration applied; 8 file migration cũ **zero-diff**; snapshot **+227/−0**; DB về baseline | ✅ |

### 3.2 B / B2 — Hàm thuần (44 + 15 + 7)

- **B1–B5**: `NotificationSeverities.Rank/IsKnown/AtLeast` (case-insensitive, lạ ⇒ −1); `ToThresholds` clamp ≥ 1; `Interval`/`LookbackWindow` clamp.
- **B6–B9**: `OverdueTask` — `due == now` **không** tính; cột `is_done` bị loại; `overdueDays == 2×` ⇒ High, `+1` ⇒ Critical; weight = worst.
- **B10–B12**: `StalledTask` **strict** `<` — `== StalledDays` **không** tín hiệu; comment muộn hơn `UpdatedAt` ⇒ "sống lại".
- **B13–B16**: `Overload` — 5 mở ⇒ có, 4 mở + 2 quá hạn ⇒ có, 4 mở + 0 quá hạn ⇒ **không**; 10 mở ⇒ Critical.
- **B17–B18**: `Bottleneck` — đúng 2 điều kiện, cột done bị loại, tắt flag ⇒ không sinh.
- **B19–B20**: 30 assignee quá tải ⇒ 20 tín hiệu + `truncatedSignals=10`; **30 task quá hạn của 1 workspace gom thành 1 signal**.
- **B21–B23**: sort severity desc → weight desc; **tất định** (JSON 2 lần byte-identical); rỗng ⇒ `([], 0)`.
- **B24–B25**: ngưỡng 0/âm không throw; 1000 task chạy **2 ms**.
- **B2a–B2o**: validator (ID bịa bị giao bỏ, severity/cap/cắt độ dài), summarizer (không chứa `description`, cắt tín hiệu + JSON vẫn hợp lệ, `ComputeWindow` 3 nhánh), `FakeAiProvider` (marker vs proposal).
- **B2m chạy lại (7/7)**: sau fix offset marker/payload, findings của `FakeAiProvider` **không còn rỗng**, dùng đúng `taskId`/`userId` từ `evidence` và **qua được validator**.

### 3.3 C — Background service (3/3)

| Mã | Nội dung | Kết quả |
|---|---|---|
| C1 | `Enabled=false`: host start/stop ~1.2s, **0** row `ai_observer_runs`, log `enabled=False` | ✅ |
| C2 | `Enabled=true` + `StartupDelaySeconds=30`: start/stop ~0.8s, không exception (đang trong delay) | ✅ |

### 3.4 D — Scan end-to-end (6/6)

Fixture (raw SQL, vì DbContext auto-stamp timestamp): 1 workspace + board 3 cột (1 `is_done`) + 6 task (quá hạn 20 ngày, đứng yên 12 ngày, 5 task mở).

| Mã | Nội dung | Kết quả |
|---|---|---|
| D1 | `ScanAsync(workspaceId)` ⇒ `Completed`, **`signalsDetected = 4`**, `aiCalled=true` | ✅ |
| D2 | `ai_observer_runs` = 1 row `Completed` + `FinishedAt` + `signalsByType` **đủ 4 loại** | ✅ |
| D3 | `notifications = findings × 1 manager`, `is_read=false`, recipient = manager | ✅ |
| D4 | `payload` có `runId`/`severity`/`model` | ✅ |
| D5 | **`tasks` nguyên vẹn 6 row, `ai_action_logs` không tăng** | ✅ |
| D6 | Task ở cột `is_done` dù quá hạn ⇒ **không** sinh tín hiệu quá hạn | ✅ |

### 3.5 E — Kiểm soát token/chi phí (3/3)

| Mã | Nội dung | Kết quả |
|---|---|---|
| E1 | Workspace **0 tín hiệu** ⇒ `Completed` + `aiCalled=false` + **stub chưa được gọi (`stubCalls=0`)** | ✅ |
| E2 | 0 tín hiệu ⇒ `promptTokens`/`completionTokens` = `null`, 0 notification | ✅ |
| E3 | Prompt thực gửi **4133 ≤ 12000** ký tự, **không** chứa `description`, `JsonMode=true` | ✅ |

### 3.6 F — HTTP API §5 (32/32)

| Mã | Kịch bản | Kết quả |
|---|---|---|
| F0 | Dựng Manager + Member (raw SQL) + ẩn danh; CSRF lấy **sau** khi gắn JWT (198 ký tự) | ✅ |
| F1 | `POST .../observer/scan` (Manager) ⇒ **200** `Completed`, `signalsDetected=2`, tạo notification thật | ✅ |
| F2 | `GET /api/notifications` ⇒ 200, có alert **mới** (`isRead=false`, `unreadCount=2`) | ✅ |
| F3/F4 | `?isRead=true` / `?isRead=FALSE` lọc đúng; casing chấp nhận | ✅ |
| F5 | `?isRead=bogus` ⇒ **400** `{ error: "Unknown value for isRead 'bogus'. Expected true or false." }` | ✅ |
| F6/F7 | `?take=0` ⇒ 200 (default); `?take=1000` ⇒ 200 (clamp) | ✅ |
| F8 | `?take=abc` ⇒ **400 ProblemDetails** (framework — đúng như cảnh báo §5.5) | ✅ |
| F9/F10 | `POST {id}/read` ⇒ 200 + `readAt`; gọi lần 2 ⇒ **không đổi** (idempotent, so ở độ chính xác µs) | ✅ |
| F11 | Member đọc alert của Manager ⇒ **404** `"Notification not found."` | ✅ |
| F12 | `read-all` ⇒ `updated=n` rồi `updated=0`; `unreadCount=0` | ✅ |
| F13 | Member `GET /notifications` ⇒ 200 `unreadCount=0`, `items=[]` | ✅ |
| F14/F15 | `GET runs` ⇒ 200 sort `startedAt DESC`; `GET run detail` ⇒ 200 + `summary` + `findings[]` | ✅ |
| F16–F18 | **Member ⇒ 403 cả 3 route Observer** (`Requires Manager or Admin role in this workspace.`) | ✅ |
| F19/F19b | Workspace lạ ⇒ **404** cho cả `runs` và `scan` (không tạo run) | ✅ |
| F20 | Run lạ ⇒ **404** `"Observer run not found."` | ✅ |
| F21 | 5 route không auth ⇒ **401** | ✅ |
| F22 | POST thiếu CSRF ⇒ **403** `{ error: CSRF… }` | ✅ |
| F23 | POST vào route GET ⇒ **405** | ✅ |
| F24a–d | App tối giản + stub: JSON hỏng ⇒ **502**; run `Failed` vẫn được ghi + `summary.error`; provider ném ⇒ **502**; `findings: []` ⇒ 200 + `notificationsCreated=0` | ✅ |
| Z1 | Cleanup: workspace/member/notifications/runs của fixture = **0** | ✅ |

### 3.7 G — Activity log (31/31)

`TaskCreated`/`TaskUpdated`/`TaskMoved`/`TaskCompleted`/`TaskDeleted`/`CommentAdded` — mỗi mutation 1 row đúng `entity_type`/`entity_id`/`workspace_id`/`board_id`/`user_id`/`payload`; `payload` `TaskUpdated` **không** chứa nội dung title/description; move vào cột `is_done` phát thêm `TaskCompleted` và `tasks.completed_at` vẫn set; **4 mutation thất bại (400/404×3) ⇒ `activity_logs` 8 → 8**; backdoor probe xác nhận DI resolve `ActivityLogWriter` (không phải no-op); regression frontend `oxlint` 0/0 + `vitest` **88/88**.

### 3.8 H/I/J — Dedupe, concurrency, failure modes (2/3/4)

| Mã | Nội dung | Kết quả |
|---|---|---|
| H1 | Quét lần 2 trong window 24h ⇒ `notifications` **2 → 2** | ✅ |
| H2 | `activity_logs` cũ 90 ngày bị prune, row mới còn nguyên | ✅ |
| I1 | 2 `ScanAsync` song song ⇒ **1 `Completed` + 1 `Skipped(AlreadyRunning)`** | ✅ |
| I2 | Đua scan không nhân bội notification (≤ 2× một vòng gửi) | ✅ |
| I3 | Advisory lock được giải phóng ⇒ scan kế tiếp `Completed` | ✅ |
| J1–J4 | JSON hỏng ⇒ `Failed` + `summary.error` (102 ký tự) + 0 notification; provider ném ⇒ `Failed`; `findings: []` ⇒ `Completed` + 0 notification; **ID bịa ⇒ evidence rỗng** (không lộ ID không tồn tại) | ✅ |

## 4. Hai Bug Thật Đã Bắt Được Và Sửa

| # | Bug | Phát hiện bởi | Tác động | Cách sửa |
|---|---|---|---|---|
| **1** | `POST /api/workspaces/{id}/observer/scan` **không** kiểm tra quyền — Member quét được (200) vì `LoadTargetsAsync` chỉ kiểm tra board tồn tại | F18 | **Lỗ hổng phân quyền**: thành viên thường kích hoạt được quét (tốn token + tạo cảnh báo) | Thêm `actingUserId?` vào `IObserverService.ScanAsync`; có actor ⇒ `RequireManagerAsync` (Member **403**, workspace lạ **404**); timer truyền `null` |
| **2** | `FakeAiProvider.BuildObserverFindings` cắt chuỗi ngay sau marker ⇒ payload còn `\n` đầu dòng ⇒ `JsonDocument.Parse` ném `JsonException` ⇒ `catch` trả `findings: []` | F2 (đường HTTP) | Nhánh offline **im lặng không tạo cảnh báo nào**; test §4 đã bỏ lọt vì chỉ kiểm `findings.Count >= 1` mà không đi qua HTTP | `.TrimStart()` phần payload **và** thêm `LogWarning` trong `catch`; verify lại B2m **7/7** và F1 tạo 2 notification thật |

Ngoài ra phát hiện 2 bug công thức đã sửa ở §3/§4 (ghi trong file task):
`ComputeWindow` để `LookbackHours` chặn `MaxLookbackDays`; retention prune không lọc workspace.

## 5. Known Gaps / Hạn Chế

1. **Dedupe khi 2 tiến trình quét thật sự đồng thời**: cả hai có thể ghi notification trước khi bên nào commit dedupe state ⇒ dedupe chỉ *giảm* (không triệt tiêu 100%) trùng lặp. Nhịp 30 phút khiến thực tế không xảy ra; H1 chứng minh dedupe đúng cho các lần quét tuần tự.
2. **Nhóm G không chạy lại** sau §5 — §5 không chạm `TaskService`/`CommentService`/`IActivityLogWriter` (đã xác nhận bằng `git status`); giá trị 31/31 là từ lần chạy §2.
3. **502 được verify qua app tối giản + stub** (F24) thay vì tạo lỗi DeepSeek thật — tránh tốn token; J1/J2 đã chứng minh run `Failed` đúng ở tầng service.
4. **Tick nhóm C không chờ đủ 70s** để thấy timer chạy thật; "exception không giết host" được bao phủ bằng `try/catch` toàn bộ `ExecuteAsync` + start/stop host thật.
5. **Chưa có test xUnit** (để Giai đoạn 8): các hàm `public static` (`Analyze`, `Validate`, `BuildPayload`, `ComputeWindow`) là điểm tựa chuyển đổi.
6. **DB dev chứa rác lịch sử Phase 1–4** (board `deleted_at != null`, task soft-deleted) ⇒ assert dùng "mutation delta" thay vì tổng tuyệt đối của bảng.
7. **Frontend §6/§7.2 chưa có** — đã bàn giao antigravity kèm contract chốt bằng JSON thật.

## 6. Kết Luận

Backend Giai đoạn 5 **hoàn tất và verify end-to-end 178 check PASS** trên API thật + PostgreSQL thật,
không tốn token DeepSeek. Hai bug thật (1 lỗ hổng phân quyền, 1 bug im lặng của nhánh offline) đã được
bắt và sửa trước khi bàn giao frontend. Bốn yêu cầu roadmap của Giai đoạn 5 đều có bằng chứng định lượng;
phần còn lại là UI (§6/§7.2) do antigravity thực hiện theo contract đã chốt.
