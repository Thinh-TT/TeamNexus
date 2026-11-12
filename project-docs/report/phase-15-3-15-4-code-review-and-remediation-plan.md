# Đánh giá Nhánh `backup/main-before-reset-26382b6` (Phase 15.3 + 15.4) & Kế hoạch Chỉnh sửa

> **Ngày review:** 2026-11-11
> **Nhánh review:** `backup/main-before-reset-26382b6` @ `26382b6`
> — `95ca673` *feat(ai): implement inbound GitHub webhooks and PR automation (15.3)*
> — `26382b6` *feat: ... GitHub webhook integration for task triage (15.4)*
> **Base:** `develop` @ `0120100` (Sub-phase 15.2). `develop` là **ancestor** của nhánh ⇒ merge `--ff-only` được, không conflict.
> **Phạm vi thay đổi:** 26 files, **+3.704 / −25** dòng.
> **Tài liệu đối chiếu:** [`phase-15-0`](../tasks/phase-15-0-external-integrations-master-plan.md), [`phase-15-3`](../tasks/phase-15-3-inbound-github-webhooks-and-pr-automation.md), [`phase-15-4`](../tasks/phase-15-4-github-issues-jev-triage-and-comments.md), [`07-external-integrations-design.md`](../07-external-integrations-design.md), [`AGENTS.md`](../../AGENTS.md).

---

## 1. Kết luận Điều hành

**Chưa nên merge vào `develop`.** Phần lõi kỹ thuật (kiến trúc module, Minimal API, HMAC-SHA256, secret một-lần, PR State Machine, Accountability + Undo) đạt chuẩn và build/test gần như xanh — **nhưng cổng `Failed = 0` đang bị vi phạm** (1 test đỏ, CI sẽ fail) và có **2 lỗi cấu hình triển khai** khiến tính năng nhiều khả năng không chạy được trên production dù code đúng (Payload URL trỏ nhầm host SPA; key ring DataProtection không được lưu bền ⇒ secret tự "chết" sau mỗi lần deploy).

Ngoài ra bản sửa `Degraded` nằm trong master plan §1.3 mới làm **một nửa** (`SuggestAssigneesAsync`), còn `TriageAsync` vẫn đánh dấu suy giảm cho một kết quả Jev hợp lệ có độ tự tin < 50 %.

| Mức | Số lượng | Ý nghĩa |
| :--- | :---: | :--- |
| **P0** | 1 | Chặn merge (test đỏ / CI gate) |
| **P1** | 2 | Phải xác nhận & sửa trước khi bật tính năng trên production |
| **P2** | 6 | Sai lệch chức năng / dữ liệu / tài liệu, nên sửa trước 15.5 |
| **P3** | 7 | Gia cố (hardening), có thể vào 15.5/16 |

Ước lượng công sức: **~1,5 – 2,5 ngày công** cho toàn bộ P0 + P1 + P2 (không tính hạ tầng key ring nếu chọn phương án DB).

---

## 2. Kết quả Đo lường Thực tế

Môi trường: Windows, .NET SDK 10, EF Core 10, Node 24.15 / npm 11.12; PostgreSQL 18 + `pgvector` (container `pgvector/pgvector:pg18` ở `localhost:5433`).

| Cổng | Lệnh | Kết quả | Đánh giá |
| :--- | :--- | :--- | :---: |
| Backend build | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | **0 Warning / 0 Error** (22,1 s) | ✅ |
| EF Core drift | `dotnet ef migrations has-pending-model-changes ...` | `No changes have been made to the model since the last migration.` | ✅ |
| Pure tests | `dotnet test ... --filter "FullyQualifiedName~Pure"` | **645 / 645 pass**, 0 skip (4 s) | ✅ |
| Toàn bộ backend | `dotnet test ...` (DB pgvector `:5433`) | **1.299 pass / 1 failed / 0 skip / total 1.300** (6 m 27 s) | ❌ |
| Frontend lint | `npm run lint` | **0 warning / 0 error**, 288 files | ✅ |
| Frontend typecheck | `npx tsc -b` | exit **0** | ✅ |
| Frontend tests | `npm test` | **123 files / 744 tests pass**, 0 fail | ✅ |

**Đối chiếu số test (khớp tài liệu):** 1.236 (hết 15.2) **+38** Pure (15.3) **+14** Integration (`GitHubWebhookPrTests`) **+12** Integration (`GitHubWebhookIssueAndCommentTests`) = **1.300**. Riêng `GitHubWebhookPrTests` **14/14 pass**; `GitHubWebhookIssueAndCommentTests` **11/12 pass** (1 đỏ, xem **F1**).

> **Lưu ý môi trường:** PostgreSQL bản cài sẵn ở `localhost:5432` của máy review khiến 26 test GitHub bị **skip** (fixture không đạt điều kiện DB — nhiều khả năng thiếu extension `vector`, đúng cảnh báo trong `DatabaseFixture`). Toàn bộ số liệu integration ở trên chạy với `TEAMNEXUS_TEST_DB="Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"` trỏ vào container `pgvector/pgvector:pg18`. CI dùng đúng image này nên kết quả sẽ lặp lại.

---

## 3. Tuân thủ Bất biến Kiến trúc (`AGENTS.md`)

| Bất biến | Kết quả | Ghi chú |
| :--- | :---: | :--- |
| Giữ đúng 4 module, không thêm `.csproj` thứ 5 | ✅ | Toàn bộ mã mới nằm trong `TeamNexus.Modules.Ai`; không có file `Board`/`Auth`/`Reporting` nào bị sửa. |
| 100 % Minimal API (không `*Controller.cs`) | ✅ | 2 file `*Endpoints.cs`, đăng ký qua `MapAiModuleEndpoints`. |
| `Ai → Board` một chiều | ✅ | `Board` không tham chiếu `TeamNexus.Modules.Ai`; chỉ dùng port hẹp `ITaskAutomation`/`ITaskService`. |
| Append-Only DTO | ✅ | Chỉ **thêm** DTO trong `DTOs/Integrations`; không đổi record cũ nào. |
| CSRF trên endpoint cookie-auth | ✅ | `PUT`/`POST /rotate-secret`/`DELETE` có `AntiforgeryValidationEndpointFilter`. |
| Webhook server-to-server: `AllowAnonymous`, bỏ CSRF, HMAC-SHA256 + `FixedTimeEquals`, body limit | ⚠️ | Đúng về cơ chế; **giới hạn body chưa được cưỡng chế trong lúc đọc** (**F10**). |
| Zero secret leakage (trả plaintext đúng 1 lần) | ✅ | `GET` chỉ trả `SecretHint` 4 ký tự; `rotate-secret` trả 1 lần; test `GetIntegration_..._DoesNotExposePlaintextSecret` xác nhận. |
| Một migration duy nhất của Phase 15, không drift | ✅ | Không thêm migration; schema dùng lại từ 15.1. |
| Bẫy **P2** (applier không mở nested transaction) | ✅ | Cả 2 applier không gọi `BeginTransactionAsync`; Undo đi qua `AiActionService.UndoAsync`. |
| Bẫy **T1** (`SaveChangesAsync` trong applier dùng DbContext trực tiếp) | ✅ | `GitHubIssueCreateTaskApplier.UndoAsync` gọi `SaveChangesAsync` sau khi xoá `task_external_links`. |
| Không làm giả số đo / không tự hạ DoD | ⚠️ | 15.4 ghi "12 tests" nhưng **1 test đỏ** và master plan **chưa cập nhật trạng thái 15.3/15.4** (**F16**). |

---

## 4. Danh sách Lỗi & Vấn đề

### F1 — **[P0] Test đỏ: `IssuesOpened_CreatesApprovedAiActionLog_AndUndo_SoftDeletesCreatedTask`**

- **Bằng chứng:** [`GitHubWebhookIssueAndCommentTests.cs:412-414`](../../tests/TeamNexus.Api.Tests/Integration/GitHubWebhookIssueAndCommentTests.cs#L412-L414)
  ```
  Assert.NotNull() Failure: Value of type 'Nullable<DateTimeOffset>' does not have a value
  ```
  Tái hiện **deterministic** (chạy riêng 1 test vẫn đỏ).
- **Nguyên nhân:** test dùng lại `db` (`TeamNexusDbContext`) đã mở **trước** lời gọi API Undo. `db.Tasks.FirstOrDefaultAsync(...)` (dòng 409) trả `null` đúng (query filter `deleted_at IS NULL` chạy ở SQL), nhưng truy vấn `IgnoreQueryFilters()` (dòng 412) gặp entity **đã bị track từ trước** nên EF Core trả về instance cũ với `DeletedAt = null` (identity map không ghi đè giá trị đã track).
- **Xác minh sản phẩm không lỗi:** đổi sang `s.NewDbContext()` cho phần assert ⇒ test **PASS**. `TaskService.DeleteTaskAsync` thực hiện soft-delete đúng (`task.DeletedAt = DateTimeOffset.UtcNow`, [`TaskService.cs:339-352`](../../src/Modules/Board/TeamNexus.Modules.Board/Services/TaskService.cs#L339-L352)).
- **Tác động:** cổng CI `Failed = 0` bị vi phạm; DoD 15.4 không đạt; nếu merge, pipeline đỏ ngay.
- **Hướng sửa:** dùng `await using var freshDb = s.NewDbContext();` (hoặc `db.ChangeTracker.Clear()`) cho 3 assert cuối. Tuỳ chọn: thay `Assert.Equal(0, commentCount)` bằng truy vấn có scope (`where TaskId == ...` / `where BoardId == ...`) để không phụ thuộc TRUNCATE.

---

### F2 — **[P1] `WebhookPayloadUrl` trỏ nhầm host trên production (sai key cấu hình so với thiết kế)**

- **Bằng chứng:**
  - [`WorkspaceGitHubIntegrationService.cs:215-219`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WorkspaceGitHubIntegrationService.cs#L215-L219) đọc `Integrations:ApiBaseUrl` → fallback `Frontend:BaseUrl` → fallback `https://api.teamnexus.cloud`.
  - `Integrations:ApiBaseUrl` **không tồn tại** trong bất kỳ `appsettings*.json`; chỉ có `Frontend:BaseUrl = http://localhost:5173`.
  - Thiết kế yêu cầu key **`Integrations:PublicApiBaseUrl`** (`https://api.teamnexus.cloud`) để dựng Payload URL — [`07-external-integrations-design.md:478-486`](../07-external-integrations-design.md#L478-L486).
  - [`frontend/vercel.json`](../../frontend/vercel.json) rewrite **mọi** đường dẫn về `/index.html`.
- **Tác động:** nếu production chỉ set `Frontend__BaseUrl=https://app.teamnexus.cloud` (không set `Integrations__ApiBaseUrl`), URL hiển thị cho người dùng là `https://app.teamnexus.cloud/api/webhooks/github/{token}` → Vercel trả `index.html` (200 HTML) ⇒ **GitHub không bao giờ chạm tới API**, tích hợp "chết im lặng" dù mọi thứ khác đúng.
- **Hướng sửa:** thêm key `Integrations:PublicApiBaseUrl` vào `appsettings.json` (+ env override), thứ tự resolve `PublicApiBaseUrl` → `ApiBaseUrl` (tương thích ngược) → `Frontend:BaseUrl`; thêm unit test dựng URL; xác nhận biến môi trường trên Render/VPS sau deploy (`GET /integrations/github` trả đúng host `api.`). Ghi rõ vào mục cấu hình của tài liệu 15.3/15.5.

---

### F3 — **[P1] Key ring `IDataProtectionProvider` không được lưu bền ⇒ secret GitHub không giải mã được sau redeploy**

- **Bằng chứng:** không có `AddDataProtection` / `PersistKeysTo*` / `SimplifyKeys` trong `Program.cs` hay `Auth` (chỉ `AddIdentityCore`). `GitHubSecretProtector` dùng provider mặc định; key ring nằm ở profile người dùng (`%APPDATA%\ASP.NET\DataProtection-Keys` — đã kiểm tra thấy 4 file key XML), tức **ephemeral trong container**.
- **Tác động:** sau mỗi lần deploy/scale, `Unprotect` ném lỗi → [`GitHubWebhookEndpoints.cs:78-92`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/GitHubWebhookEndpoints.cs#L78-L92) trả **401** cho mọi delivery; chỉ phục hồi được bằng `POST /rotate-secret` + cập nhật secret bên GitHub. Thiết kế đã yêu cầu rõ: *"Key ring: Bắt buộc lưu DB"* (Render) và `Integrations:DataProtection.CertPath` (VPS) — [`07-external-integrations-design.md:485,495`](../07-external-integrations-design.md#L485).
- **Hướng sửa (chọn 1, cần quyết định của chủ dự án):**
  1. **Nhanh, không đổi schema (khuyến nghị cho 15.x):** `AddDataProtection().SetApplicationName("TeamNexus").PersistKeysToFileSystem(new DirectoryInfo(path))` với `path` lấy từ env + volume bền (hoặc mount secret), ghi log cảnh báo nếu path không tồn tại.
  2. **Bền vững đúng thiết kế:** `PersistKeysToDbContext` (bảng `data_protection_keys`) ⇒ **cần migration #16**, tức phá vỡ "đúng 1 migration của Phase 15" ⇒ phải được duyệt tường minh và ghi vào nhật ký phase, hoặc hoãn sang Phase 16.
- **Bổ sung:** thêm health check / log `Error` khi `Unprotect` thất bại kèm hướng dẫn `rotate-secret`, để sự cố không im lặng.
- **Cập nhật sau nghiệm thu 14/15 (TASK-DP-01):** chủ dự án đã duyệt **phương án 2** — key ring lưu PostgreSQL qua `PersistKeysToDbContext<TeamNexusDbContext>()`, migration #16 `Phase15DataProtectionKeyRing`, bỏ hẳn `KeysPath`; `CertPath` còn là tuỳ chọn at-rest cho VPS. Vì đổi nguồn key ring, ciphertext cũ không giải mã được ⇒ runbook bắt buộc **rotate secret 1 lần** sau deploy (xem `tasks/phase-15-remaining-frontend-handover.md`).

---

### F4 — **[P2] `TriageAsync` vẫn đánh cờ `Degraded` cho kết quả Jev hợp lệ (< 50 % tự tin)**

- **Bằng chứng:** [`TaskTriageService.cs:138`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/TaskTriageService.cs#L138) — `Degraded: result.RoutingAdvice == Jev.ConfidenceRouting.ManualRequired`, trong khi 2 nhánh còn lại đã đổi sang `result.Usage is null` ([`:196`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/TaskTriageService.cs#L196), [`:278`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/TaskTriageService.cs#L278)).
- **Mâu thuẫn:** master plan §1.3 chỉ đích danh **cả hai** dòng 131 & 197 phải sửa; `AGENTS.md` §3.5 quy định "*`ManualRequired` với `LatencyMs > 0` là kết quả hợp lệ — KHÔNG đặt `Degraded = true`*".
- **Tác động:** [`JevTriagePanel.tsx:76`](../../frontend/src/features/ai/components/JevTriagePanel.tsx#L76) hiển thị *"Gợi ý AI tạm không khả dụng"* cho một phán đoán hợp lệ "không đủ tự tin" ⇒ sai thông điệp người dùng, ngược tinh thần "đo thắng tài liệu" (Jev chạy đúng nhưng bị báo lỗi).
- **Hướng sửa:** tách helper `private static bool IsDegraded(JevResult result, bool hadCandidates)` dùng chung cho cả 3 chỗ (`Usage is null` khi có input); thêm test `FAKE:JEV_LOW` cho triage tương tác + system, khẳng định `Degraded == false` và `RoutingAdvice == ManualRequired`. Cân nhắc tách luôn phần dựng `TaskTriageResponse` đang lặp ~70 dòng giữa `TriageAsync` và `TriageForSystemAsync`.

---

### F5 — **[P2] `#NNN` được tự động liên kết & đồng bộ ⇒ nguy cơ gán nhầm thẻ do trùng số với GitHub Issue/PR**

- **Bằng chứng:** regex nhận cả hậu tố `#` — [`TaskKeyParser.cs:14`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/TaskKeyParser.cs#L14); dùng để quét PR (`GitHubWebhookService.cs:179`) và bình luận ([`:835`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs#L835)).
- **Kịch bản lỗi thật:** PR body ghi *"Fixes #42"* (số Issue của GitHub) trong khi workspace có thẻ `TN-42` không liên quan ⇒ thẻ bị **tự động kéo sang In Review/Done**; bình luận *"xem #42"* bị **đồng bộ vào sai thẻ**. Không gian số hiệu GitHub (theo repo) và `task_number` (theo workspace) không cùng hệ quy chiếu.
- **Hướng sửa:** thêm tham số `requireTnPrefix` (hoặc regex thứ hai `TN-\d+`) cho đường **tự động** (PR state machine, comment source 2); vẫn giữ `#n` cho tìm kiếm thủ công/`TaskKeyParser` công khai. Bổ sung test: PR chỉ có `#42` **không** move thẻ; comment chỉ có `#42` **không** tạo comment; `TN-42` vẫn hoạt động.

---

### F6 — **[P2] Thiếu phát SignalR `TaskUpdated` khi Issue `closed`/`reopened`**

- **Bằng chứng:** tài liệu 15.4 §T15.4.2 bước 4 yêu cầu *"cập nhật `TaskExternalLink.State` ... và phát SignalR `TaskUpdated`"*; code chỉ cập nhật link + `SaveChangesAsync` — [`GitHubWebhookService.cs:456-491`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs#L456-L491), không gọi `IBoardEventPublisher`.
- **Tác động:** chip GitHub trên `TaskCard`/`TaskDetailModal` chỉ đổi trạng thái sau khi người dùng F5, trái cam kết "xuất hiện tức thì".
- **Hướng sửa:** thêm method vào port `ITaskAutomation` (ví dụ `NotifyExternalLinkStateChangedAsync(taskId, ct)`) trong `Board`, đọc lại task + links và gọi `IBoardEventPublisher.TaskUpdated` — giữ đúng chiều phụ thuộc `Ai → Board`, không cho `Board` biết `GitHubWebhookService`.

---

### F7 — **[P2] Luồng tạo thẻ từ Issue không transactional; lỗi bước Jev/tag có thể làm mất `AiActionLog` (mất khả năng Undo)**

- **Bằng chứng:** [`GitHubWebhookService.cs:598-705`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs#L598-L705): tạo task → lưu link → gọi `ApplySystemTriageSuggestionsAsync` → mới ghi `AiActionLog` và delivery.
- **Rủi ro:** nếu `ApplySystemTriageSuggestionsAsync` ném lỗi (label trùng/độ dài, DB), webhook trả 500 **sau khi** thẻ đã tạo nhưng **trước khi** có log/delivery; GitHub redeliver → dedupe theo link ⇒ coi là "Ignored" ⇒ **vĩnh viễn không có bản ghi kiểm toán và không Undo được** thẻ đó. Ngoài ra 3 bước ghi DB rời nhau, không có transaction bao ngoài.
- **Hướng sửa:** ghi `AiActionLog` **ngay sau** khi có `createdTask.Id` + link; bọc phần Jev/tag trong `try/catch` (fail-soft, log warning, vẫn ghi delivery `Processed` kèm ghi chú); cân nhắc `BeginTransactionAsync` cho (task + link + log) — không đụng bẫy P2 vì đây không phải applier. Thêm test: Jev ném lỗi (`FAKE:JEV_ERROR`) ⇒ thẻ vẫn được tạo, **có** `AiActionLog`, delivery `Processed`.

---

### F8 — **[P2] Undo chuyển thẻ PR không khôi phục `position` (và không khôi phục `completedAt` một cách tường minh)**

- **Bằng chứng:** snapshot được ghi đầy đủ (`previousPosition`, `previousCompletedAt` — [`GitHubWebhookService.cs:392-399`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs#L392-L399)), nhưng [`GitHubPrMoveTaskApplier.cs:105-113`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Appliers/GitHubPrMoveTaskApplier.cs#L105-L113) tính `position = Count(...)` (nối vào **cuối** cột) và không đọc `previousCompletedAt`.
- **Tác động:** sau Undo, thẻ về đúng cột nhưng **sai thứ tự** — trái với yêu cầu "trả về đúng `fromColumnId`, `position` và `completedAt`" tại [`phase-15-3` §T15.3.3 bước 4](../tasks/phase-15-3-inbound-github-webhooks-and-pr-automation.md). Riêng `completedAt` hiện đúng *theo cấu trúc* trong luồng merge-PR (thẻ chuyển Done ⇒ `MoveTaskAsync` xoá `CompletedAt` khi rời Done), nhưng đó là hệ quả gián tiếp chứ không phải khôi phục từ snapshot.
- **Hướng sửa:** thêm `AiActionService.ReadInt(...)`; dùng `position = Math.Clamp(previousPosition, 0, count)`; nếu `previousCompletedAt` khác null thì ghi lại tường minh (hoặc mở rộng `MoveTaskRequest` bằng cờ khôi phục). Test: 3 thẻ cùng cột, Undo thẻ giữa phải về đúng vị trí giữa.

---

### F9 — **[P2] Lịch sử Hành động AI hiển thị thô 2 action mới + chuỗi "Mô tả 0 ký tự"**

- **Bằng chứng:** [`AiActionLogItem.tsx:143-154`](../../frontend/src/features/ai/components/AiActionLogItem.tsx#L143-L154) fallback `log.action` (hiện `GitHubPrMoveTask` / `GitHubIssueCreateTask` bằng tiếng Anh); phần basis giả định shape Smart Setup nên GitHub basis (repo/PR/issue) render thành *"Mô tả 0 ký tự"* ([`:241-257`](../../frontend/src/features/ai/components/AiActionLogItem.tsx#L241-L257)); `taskCount = 0` ⇒ tag "0 tasks".
- **Tác động:** vi phạm chuẩn copy tiếng Việt (`AGENTS.md` §4); người dùng mở chi tiết log thấy thông tin vô nghĩa dù backend ghi đầy đủ.
- **Hướng sửa (đưa vào 15.5):** bổ sung nhãn `GitHubPrMoveTask` → *"Tự động chuyển thẻ (GitHub PR)"*, `GitHubIssueCreateTask` → *"Tạo thẻ từ GitHub Issue"*; render basis theo `repo`/`prNumber`/`issueNumber` + link `html_url`; ẩn tag task-count khi `0`; thêm test component.

---

### F10 — **[P3] Giới hạn body 256 KB được kiểm tra *sau* khi đã buffer toàn bộ vào RAM**

- **Bằng chứng:** đọc trọn `request.Body` vào `MemoryStream` ([`GitHubWebhookEndpoints.cs:60-68`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/GitHubWebhookEndpoints.cs#L60-L68)) rồi mới so `Length`; `ContentLength` có thể `null` (chunked) nên nhánh chặn sớm không áp dụng.
- **Tác động:** request ký hợp lệ (hoặc endpoint bị lạm dụng bởi bên giữ secret) có thể tiêu tốn bộ nhớ trước khi bị 413; Kestrel mặc định 30 MB nên vẫn có trần, nhưng không phải trần của tính năng.
- **Hướng sửa:** dùng `context.Features.Get<IHttpMaxRequestBodySizeFeature>()` để đặt `MaxRequestBodySize = MaxPayloadBytes` (và trả 413 qua `BadHttpRequestException`), hoặc đọc qua `LimitedStream`/đếm byte và dừng ngay khi vượt ngưỡng. Thêm integration test body 300 KB → 413.

### F11 — **[P3] Delivery `Failed` vẫn trả HTTP 200 ⇒ GitHub không retry/redeliver tự động**

- **Bằng chứng:** [`GitHubWebhookEndpoints.cs:94-101`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/GitHubWebhookEndpoints.cs#L94-L101) luôn `Results.Ok(processResult)` kể cả `Status == "Failed"` (ví dụ `DefaultBoardId` không còn tồn tại — `GitHubWebhookService.cs:527-538`).
- **Tác động:** sự kiện bị mất mà không có tín hiệu retry; phải redeliver thủ công từ GitHub.
- **Hướng sửa:** map `Failed` → `502 Bad Gateway` (vẫn giữ bản ghi delivery để điều tra), `Ignored` → 200; test tương ứng.

### F12 — **[P3] `IssueLabelFilter` không được validate độ dài (DB `varchar(500)`) ⇒ 500 thay vì 400**

- **Bằng chứng:** [`WorkspaceGitHubIntegrationService.cs:96,121`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/WorkspaceGitHubIntegrationService.cs#L96) trim rồi gán; cấu hình entity `HasMaxLength(500)` ([`WorkspaceGitHubIntegrationConfiguration.cs`](../../src/TeamNexus.Persistence/Data/Configurations/WorkspaceGitHubIntegrationConfiguration.cs)).
- **Hướng sửa:** kiểm tra `Length <= 500` + số nhãn hợp lý (ví dụ ≤ 20) và ném `BadRequestException`; thêm test.

### F13 — **[P3] Payload thiếu `sender` có thể gây 500 do `TryGetProperty` trên `JsonElement` Undefined**

- **Bằng chứng:** `var senderType = root.TryGetProperty("sender", out var sender) ...` rồi dùng `sender.TryGetProperty("login", ...)` ở [`:807`](../../src/Modules/Ai/TeamNexus.Modules.Ai/Services/Integrations/GitHubWebhookService.cs#L807) mà không kiểm tra `sender.ValueKind == Object`.
- **Hướng sửa:** guard `sender.ValueKind == JsonValueKind.Object` cho cả `sender` và từng phần tử `labels`; hoặc dùng `TryGetProperty` an toàn qua helper. Kèm test payload tối thiểu.

### F14 — **[P3] Thiếu allow-list repo & dedupe không an toàn khi 2 delivery đồng thời**

- **Bằng chứng:** `workspace_github_integrations` là **1-1 theo workspace** (không có bảng repo/PAT như thiết kế §R3/§5); duy nhất index `uq_task_external_links_task_external (task_id, provider, link_type, external_id)` — **hai task khác nhau vẫn có thể cùng `external_id`** (hai delivery `issues.opened`/`labeled` chạy song song đều không thấy link ⇒ tạo 2 thẻ).
- **Tác động:** rủi ro thấp nhưng có thật; đồng thời bất kỳ repo nào có token+secret đều điều khiển được thẻ trong workspace (mô hình capability — chấp nhận được nếu ghi rõ).
- **Hướng sửa:** ghi nhận là **L-deviation** trong nhật ký 15.3/15.4 (schema 15.1 đã chốt 1-1); nếu muốn chặn triệt để: unique index `(workspace_id, provider, link_type, external_id)` + xử lý `23505` bằng "đọc lại link rồi bỏ qua" — cần migration mới nên để Phase 16.

### F15 — **[P3] File rác `sqlite_mcp_server.db` (0 byte) bị commit**

- **Bằng chứng:** `sqlite_mcp_server.db` được thêm trong commit `26382b6` (0 byte, nằm ở repo root); `.gitignore` không chặn `*.db` của MCP.
- **Hướng sửa:** `git rm --cached sqlite_mcp_server.db` + thêm mục `.gitignore` cho artifact MCP (`sqlite_mcp_server.db`, `*.sqlite_mcp*`).

### F16 — **[P2] Tài liệu chưa phản ánh đúng trạng thái / sai lệch so với code**

| Vị trí | Vấn đề |
| :--- | :--- |
| [`phase-15-0` §2](../tasks/phase-15-0-external-integrations-master-plan.md) | Bảng tổng hợp vẫn để 15.3/15.4 **không có ✅** dù roadmap đã ghi xong. |
| [`phase-15-4` §4.4](../tasks/phase-15-4-github-issues-jev-triage-and-comments.md) | Ghi "*12/12 tests ... bao phủ toàn bộ kịch bản*" nhưng **không nêu 1 test đỏ** (F1); cần nêu trung thực hoặc sửa xong rồi mới ghi. |
| [`phase-15-4` §T15.4.2](../tasks/phase-15-4-github-issues-jev-triage-and-comments.md) | Nói có phát SignalR `TaskUpdated` khi `closed`/`reopened` — code không có (F6). |
| [`phase-15-3` §T15.3.3](../tasks/phase-15-3-inbound-github-webhooks-and-pr-automation.md) | Nói `AppliedSnapshot` chứa `position`/`completedAt` — code ghi `previousPosition`/`previousCompletedAt` (F8). |
| [`phase-15-3` §T15.3.4](../tasks/phase-15-3-inbound-github-webhooks-and-pr-automation.md) | Nói "body limit 256KB" — chưa cưỡng chế khi đọc (F10). |
| `03-roadmap.md` | Chưa nâng CI baseline (backend ≥ 1.300, web ≥ 744) theo quy định "chỉ nâng ở cuối phase" — hợp lệ, nhưng nên ghi chú vào 15.5. |

---

## 5. Kế hoạch Chỉnh sửa

### Bước 0 — Vệ sinh nhánh (≈15 phút, làm ngay)
1. `git rm --cached sqlite_mcp_server.db`; thêm `.gitignore` cho artifact MCP.
2. Cập nhật master plan §2: đánh dấu ✅ 15.3/15.4 **sau khi** các bước dưới xong.
3. Giữ nguyên nhánh `backup/...` cho tới khi hết review; mọi sửa đổi làm trên nhánh mới `fix/phase15-review` (hoặc `feat/phase15-3-4-fixes`) tách từ `develop`.

### Bước 1 — Chặn merge (P0 + P1, ≈0,5 ngày)
| Task | Nội dung | File | Nghiệm thu |
| :--- | :--- | :--- | :--- |
| **R1.1** (F1) | Đổi sang `s.NewDbContext()`/`ChangeTracker.Clear()` cho assert sau Undo; scope lại assert comment count | `tests/.../GitHubWebhookIssueAndCommentTests.cs` | `dotnet test` **0 failed**, full suite 1.300 pass |
| **R1.2** (F2) | Thêm `Integrations:PublicApiBaseUrl` vào `appsettings.json`; sửa thứ tự resolve; test dựng Payload URL; ghi env var vào tài liệu 15.3 §T15.3.2 | `WorkspaceGitHubIntegrationService.cs`, `appsettings.json`, test mới `Pure/GitHubPayloadUrlTests.cs` | Unit test xác nhận host `api.`; không còn fallback im lặng sang Frontend |
| **R1.3** (F3) | Quyết định phương án key ring; tối thiểu `PersistKeysToFileSystem` + env path + log cảnh báo; ghi rõ phương án DB/cert vào 15.5/16 | `Program.cs`/`AiModule` hoặc `Auth/DependencyInjection.cs` | Restart tiến trình ⇒ secret cũ vẫn `Unprotect` được (test thủ công 2 lần chạy) |

### Bước 2 — Sửa lỗi chức năng (P2, ≈1 ngày)
| Task | Nội dung | File chính | Test bổ sung |
| :--- | :--- | :--- | :--- |
| **R2.1** (F4) | Helper `IsDegraded` dùng chung; áp cho `TriageAsync`; gộp phần dựng response | `TaskTriageService.cs` | Pure/Integration với `FAKE:JEV_LOW` ⇒ `degraded=false`, `routingAdvice=ManualRequired` |
| **R2.2** (F5) | Chỉ nhận `TN-` cho auto-link/comment-sync; giữ `#n` cho search | `TaskKeyParser.cs`, `GitHubWebhookService.cs` | PR `#42` không move; `TN-42` move; comment `#42` không sync |
| **R2.3** (F6) | Port `NotifyExternalLinkStateChangedAsync` + phát `TaskUpdated` | `ITaskAutomation`, `TaskAutomationService`, `GitHubWebhookService` | Integration test với `IBoardEventPublisher` giả lập/fake hub |
| **R2.4** (F7) | Ghi `AiActionLog` ngay sau tạo thẻ; bọc Jev/tag fail-soft | `GitHubWebhookService.cs` | `FAKE:JEV_ERROR` ⇒ có task, có log, delivery `Processed` |
| **R2.5** (F8) | Khôi phục `previousPosition`/`previousCompletedAt` | `GitHubPrMoveTaskApplier.cs` | Undo thẻ giữa 3 thẻ ⇒ đúng vị trí giữa |
| **R2.6** (F16) | Cập nhật tài liệu 15.3/15.4/master plan/roadmap đúng thực tế | `project-docs/tasks/phase-15-3*`, `phase-15-4*`, `phase-15-0*`, `03-roadmap.md` | Không còn câu khẳng định vượt quá code |

### Bước 3 — Gia cố (P3, ≈0,5 ngày)
**R3.1** (F10) body limit thật sự + test 413; **R3.2** (F11) `Failed` → 502; **R3.3** (F12) validate `IssueLabelFilter`; **R3.4** (F13) guard `JsonElement`; **R3.5** ghi L-deviation về mô hình 1 repo/workspace + dedupe (F14).

### Bước 4 — Bàn giao 15.5 (≈0,5 ngày)
**R4.1** (F9) nhãn + basis + ẩn "0 tasks" trong `AiActionLogItem`/`aiAction.types.ts`, test component; **R4.2** chọn thời điểm nâng CI baseline `.github/workflows/ci-backend.yml` (≥ **1.300**) và `ci-web.yml` (≥ **744**) theo số đo thật ở cuối Phase 15; **R4.3** thêm badge `TN-123`/chip PR (đã có trong 15.5).

### Cổng kiểm thử cho mọi bước
```powershell
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental   # 0 warning / 0 error
dotnet ef migrations has-pending-model-changes --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build   # Failed = 0, Skipped = 0, Total >= 1300
cd frontend; npm run lint; npx tsc -b; npm test                              # 0/0; exit 0; 744 pass
```
> DB test phải là `pgvector/pgvector:pg18` và trỏ `TEAMNEXUS_TEST_DB` (xem Phụ lục) — nếu không, 26 test GitHub **skip** và cổng "Skipped = 0" bị vi phạm.

---

## 6. Rủi ro được Chấp nhận (cần ghi vào nhật ký)

1. **Mô hình 1 integration/workspace, không có bảng repo + PAT** (khác thiết kế §R3/§5): không hỗ trợ đa repo, không liên kết PR/Issue bằng URL thủ công, không gọi GitHub API để làm mới state. Đây là hệ quả của schema 15.1 đã chốt — cần ghi **L-deviation** thay vì im lặng.
2. **Không allow-list repo:** token+secret là capability; bất kỳ repo nào gắn webhook đúng token đều tạo/sửa được thẻ trong workspace.
3. **Mất sự kiện khi service ngủ** (Render free) — GitHub không retry; đã ghi trong thiết kế §13.2.
4. **Trùng số `#n` giữa GitHub và `TN-n`** — chấp nhận sau khi siết F5 cho auto-link.
5. **Delivery `Failed` không retry** — chấp nhận tạm nếu F11 chưa làm; UI delivery log + nút redeliver của GitHub là mitigation.

---

## 7. Phụ lục — Lệnh & Môi trường

```powershell
# Nhánh
git fetch --all --prune
git checkout backup/main-before-reset-26382b6
git merge --ff-only origin/backup/main-before-reset-26382b6   # đã up to date tại 26382b6

# Backend
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental
dotnet ef migrations has-pending-model-changes --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build --filter "FullyQualifiedName~Pure"

# Integration (PostgreSQL 18 + pgvector)
docker run --name teamnexus-pg-review -e POSTGRES_PASSWORD=postgres -p 5433:5432 -d pgvector/pgvector:pg18
$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build

# Frontend
cd frontend
npm run lint
npx tsc -b
npm test
```

**Ghi chú tái hiện F1:** chạy riêng test đỏ vẫn fail; thay `db.Tasks.IgnoreQueryFilters()` bằng `freshDb.Tasks.IgnoreQueryFilters()` (context mới) ⇒ PASS → chứng minh đây là lỗi test (stale tracking), không phải lỗi `DeleteTaskAsync`.

---

## 8. Kết luận

- **15.3:** đạt yêu cầu kỹ thuật; 14/14 integration test xanh. Cần sửa F2/F3 (cấu hình) + F8 (Undo vị trí) + F10/F11 (hardening) trước khi coi là "production-ready".
- **15.4:** logic đúng nhưng **1 test đỏ** (F1) và thiếu SignalR theo chính tài liệu (F6); bản sửa `Degraded` còn dang dở (F4). Sau khi xử lý Bước 1 + Bước 2, có thể merge `--ff-only` vào `develop` và chuyển sang 15.5.

---

## 9. Trạng thái Thi hành (2026-11-11)

> Kế hoạch trên đã được **thi hành đầy đủ** trên nhánh `fix/phase15-review` (tách từ `develop`, merge `backup/main-before-reset-26382b6`, rồi sửa tuần tự theo Bước 0 → Bước 4). Không phát sinh migration mới.

| Bước | Commit | Nội dung | Kết quả |
| :--- | :--- | :--- | :--- |
| 0 | `2087e24` | Merge nhánh review, xoá `sqlite_mcp_server.db`, thêm rule `.gitignore` cho artifact MCP, commit báo cáo review | ✅ |
| 1 | `3fa1bc9` | F1 (test Undo dùng context mới), F2 (`Integrations:PublicApiBaseUrl` + resolver + 6 test), F3 (key ring `KeysPath`/`CertPath` + 3 test + cảnh báo production) — **F3 đã được TASK-DP-01 thay bằng key ring PostgreSQL** | ✅ |
| 2 | `a32b35f` | F4 (helper `IsProviderDegraded` dùng chung), F5 (`requireTnPrefix`), F6 (`ITaskAutomation.NotifyTaskUpdatedAsync` + recorder test), F7 (audit log trước Jev + fail-soft), F8 (Undo khôi phục vị trí/CompletedAt) | ✅ |
| 3 | `6595eb1` | F10 (body limit khi đọc), F11 (`Failed` → 502), F12 (validate `IssueLabelFilter`), F13 (guard `sender`/`labels`), F14 ghi L2 trong tài liệu 15.3 | ✅ |
| 4 | (commit kế tiếp) | F9 (nhãn tiếng Việt + basis GitHub + ẩn "0 tasks" + 3 Vitest test), cập nhật 15.0/15.3/15.4/roadmap, nâng CI baseline **1.329**/**747** | ✅ |

**Cổng kiểm thử cuối cùng (đo thật):**

| Cổng | Kết quả |
| :--- | :--- |
| `dotnet build --no-incremental` | **0 warning / 0 error** |
| `dotnet ef migrations has-pending-model-changes` | `No changes have been made to the model since the last migration.` |
| Pure tests | **665 / 665 pass**, 0 skip |
| Toàn bộ backend (`pgvector` `:5433`) | **1.329 pass / 0 fail / 0 skip** (6 m 39 s) |
| `npm run lint` | **0 warning / 0 error** (288 files) |
| `npx tsc -b` | exit **0** |
| `npm test` | **123 files / 747 tests pass** (lần chạy lại sau khi sửa; lần đầu có 1 test flake, không tái hiện) |

**Khác biệt so với kế hoạch ban đầu (chấp nhận được):**
- F3 chọn `PersistKeysToFileSystem`/`ProtectKeysWithCertificate` qua env thay vì `PersistKeysToDbContext`: tránh migration #16 (bất biến Phase 15 "đúng 1 migration"). Phương án DB/cert để Phase 16 quyết định.
- F1 chỉ sửa test (đúng bản chất: sản phẩm soft-delete chính xác; lỗi nằm ở `DbContext` tái sử dụng).
- F9 làm luôn trong đợt này (dù kế hoạch xếp vào 15.5) vì rủi ro hiển thị sai ngay khi bật tính năng; phần badge `TN-123`/chip PR/Issue và tab Tích hợp vẫn thuộc 15.5.
