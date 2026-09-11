Module AI của TeamNexus (modular monolith):
1. **AI Smart Setup (Phase 3):** biến mô tả công việc của trưởng nhóm thành **đề xuất** sub-tasks + nhãn + người phụ trách qua DeepSeek API để con người chỉnh sửa và xác nhận (không ghi DB).
2. **Accountability Layer (Phase 4):** cổng trung gian kiểm soát mọi hành động AI ghi dữ liệu (`AiActionLog`, vòng đời `Pending → Approved / Rejected → Undone`). Nút "Xác nhận" tạo log `Pending`, chỉ khi con người **Duyệt** mới sinh `tasks`/`labels`/`task_labels` thật, và hỗ trợ **Hoàn tác** (soft-delete + dọn nhãn).
3. **AI Observer (Phase 5):** `BackgroundService` quét hoạt động board theo chu kỳ, phát hiện tín hiệu bất thường và cảnh báo riêng cho Manager/Admin qua `notifications`.
4. **AI Agent Executor (Phase 7 — 🔄 đang làm):** Agent là thành viên ảo của workspace, được gán task như người thật, tự chạy vòng lặp tool-calling, kết quả đi qua Accountability Layer và có guardrail ngân sách.

> Trạng thái: **Giai đoạn 3–5 đã hoàn tất & verify.** **Giai đoạn 7 đang làm** — kế hoạch chi tiết ở
> `Project-Documents/tasks/phase-7-ai-agent-executor.md`, schema đã chốt ở `Project-Documents/04-database-design.md` §3.8.

## §1 — Cấu hình & đăng ký (đã hiện thực)

### Options `DeepSeek` (`Options/DeepSeekOptions.cs`)

| Key | Mặc định | Ghi chú |
|---|---|---|
| `ApiKey` | `""` | Secret — đặt qua User Secrets, không commit, **không bao giờ log** |
| `BaseUrl` | `https://api.deepseek.com` | `NormalizedBaseUrl` tự cắt `/` cuối (rỗng → về mặc định) |
| `Model` | `deepseek-chat` | |
| `TimeoutSeconds` | `60` | Áp vào `HttpClient.Timeout` của named client |
| `MaxTokens` | `4096` | Kiểm soát chi phí token |
| `Temperature` | `0.2` | Thấp để JSON ổn định |
| `MaxTaskCount` | `20` | Trần số sub-task trong 1 proposal |

Đặt key (khuyến nghị, không lưu vào file tracked):

```bash
dotnet user-secrets init --project src/TeamNexus.Api        # chỉ cần một lần
dotnet user-secrets set --project src/TeamNexus.Api "DeepSeek:ApiKey" "sk-..."
```

### Đăng ký DI (`AiModule.AddAiModule`)

| Đăng ký | Chi tiết |
|---|---|
| `IOptions<DeepSeekOptions>` | Bind section `DeepSeek` — **không** `ValidateOnStart` để dev chạy được khi chưa có key |
| Named `HttpClient` `"DeepSeek"` | `AiModule.HttpClientName`; timeout lấy từ options. `DeepSeekAiProvider` tạo client bằng `IHttpClientFactory.CreateClient(AiModule.HttpClientName)` |
| `IAiProvider` (singleton) | Chọn theo cấu hình: `ApiKey` rỗng → `FakeAiProvider`; có key → `DeepSeekAiProvider` |
| `DomainExceptionFilter` (Board) | Map `BoardModuleException` → `{ error }` + status tương ứng (gồm `AiProviderException` 502) |
| `AntiforgeryValidationEndpointFilter` (Shared) | Bắt buộc header `X-XSRF-TOKEN` trên endpoint POST |

`ISmartSetupService` **chưa đăng ký** — thuộc §4 (tránh stub `NotImplementedException` dùng một lần).

### Hành vi khi thiếu key

`ApiKey` rỗng là cấu hình dev hợp lệ: app vẫn boot và `FakeAiProvider` được đăng ký
(proposal mẫu, không tốn chi phí, không cần mạng). Khi khởi động, module log đúng một dòng
để biết đang ở nhánh nào:

```
warn: TeamNexus.Modules.Ai[0]
      Ai module: DeepSeek:ApiKey chưa được cấu hình — đăng ký FakeAiProvider (proposal mẫu, không gọi API). ...
```

Có key → `info: ... provider=DeepSeekAiProvider, model=deepseek-chat, baseUrl=...` (không kèm key).

## §2 — `IAiProvider` & DeepSeek (đã hiện thực)

### Chọn provider

| Điều kiện | Implementation | Hành vi |
|---|---|---|
| `DeepSeek:ApiKey` rỗng | `FakeAiProvider` | Trả proposal mẫu khớp schema §4.1; không gọi mạng; token counts `null` |
| `DeepSeek:ApiKey` có giá trị | `DeepSeekAiProvider` | `POST {BaseUrl}/chat/completions` thật |

> Provider là **singleton**: đổi key cần restart app để nhánh mới có hiệu lực.

### Contract (`Services/AiProvider.cs`)

```csharp
Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);
record AiCompletionRequest(string SystemPrompt, string UserPrompt, double Temperature, int MaxTokens, bool JsonMode);
record AiCompletionResult(string Content, int? PromptTokens, int? CompletionTokens);
```

Interface cố tình mỏng để Giai đoạn 5 (Observer) tái dùng chỉ bằng cách đổi prompt.

### Request gửi DeepSeek (OpenAI-compatible)

| JSON key | Nguồn | Ghi chú |
|---|---|---|
| `model` | `DeepSeekOptions.Model` | |
| `messages` | `[{role:"system",content:SystemPrompt},{role:"user",content:UserPrompt}]` | |
| `temperature` | `AiCompletionRequest.Temperature` | Validate `0–2` trước khi gọi |
| `max_tokens` | `AiCompletionRequest.MaxTokens` | Validate `> 0` |
| `response_format.type` | `"json_object"` khi `JsonMode = true` | Bỏ hẳn field khi `JsonMode = false` |

Header: `Authorization: Bearer {ApiKey}`, `Accept: application/json`.
Nối URL: `$"{NormalizedBaseUrl}/chat/completions"` → `https://api.deepseek.com/chat/completions`.

### Response parse

| Nguồn | Đích |
|---|---|
| `choices[0].message.content` | `AiCompletionResult.Content` (ném 502 nếu null/rỗng) |
| `usage.prompt_tokens` | `AiCompletionResult.PromptTokens` |
| `usage.completion_tokens` | `AiCompletionResult.CompletionTokens` |

Thiếu `usage` **không** phải lỗi — token counts để `null`.

### Failure modes → `AiProviderException` (502)

| Tình huống | Xử lý |
|---|---|
| HTTP ≠ 2xx | 502, message có status + body (cắt ≤ 500 ký tự) |
| Timeout (`TimeoutSeconds`) | 502 "không phản hồi trong Ns" |
| `content` rỗng/chỉ khoảng trắng | 502 "nội dung rỗng" |
| Body không phải JSON | 502, kèm ≤ 200 ký tự đầu để debug |
| Caller `ct` bị cancel | **Không** bọc — rethrow `OperationCanceledException` |
| Prompt rỗng / `MaxTokens ≤ 0` / `Temperature` ngoài `[0,2]` | 502 ngay, **không** gọi mạng (không tốn token) |

Không retry HTTP ở tầng provider (tránh trả tiền 2 lần cho một POST). Việc **retry 1 lần khi
JSON parse lỗi** thuộc `SmartSetupService` (§4.3 bước 8).

**Bảo mật:** `ApiKey` không bao giờ vào log/exception message; prompt chỉ được log ở mức
`Debug` (độ dài, không nội dung).

### `FakeAiProvider` — proposal mẫu

Trả JSON cố định khớp schema §4.1 với 3 task, phủ sẵn các nhánh §4.3 phải xử lý:
priority không hợp lệ (`null`), task có 2 label, assignee có tên (khớp) và assignee `null`.
`summary` echo độ dài mô tả + 60 ký tự đầu (escape bằng `JsonEncodedText` nên luôn là JSON hợp lệ).
Số task bị cắt theo `MaxTaskCount`.

## Endpoints

| Endpoint | Quyền | Trạng thái |
|---|---|---|
| `POST /api/boards/{boardId}/smart-setup` | Manager/Admin (kiểm tra trong service) | **Hoàn chỉnh (§3.2 + §4.3)** → `200 SmartSetupProposal` |

Pipeline của endpoint (theo `TasksEndpoints`): `RequireAuthorization` → `AntiforgeryValidationEndpointFilter`
(header `X-XSRF-TOKEN`) → `DomainExceptionFilter` → `ISmartSetupService.GenerateAsync` → `200 SmartSetupProposal`.

Giai đoạn 3 **không có endpoint ghi DB**: đây là endpoint duy nhất của module. `POST .../smart-setup/confirm`
thuộc Giai đoạn 4.

## §4 — Smart Setup service (đã hiện thực)

`SmartSetupService.GenerateAsync` chạy đúng 12 bước của spec §4.3:

| # | Bước | Chi tiết |
|---|---|---|
| 1 | Load board | `AsNoTracking`; không thấy (hoặc soft-deleted) → **404** |
| 2 | Phân quyền | `IWorkspaceAccess.RequireManagerAsync` → Member **403** |
| 3 | Validate mô tả | rỗng/khoảng trắng hoặc **> 4000** ký tự → **400** (trước khi tốn token) |
| 4 | Load context | tên cột (`IColumnService`), thành viên (`IWorkspaceMemberService`), nhãn workspace — tất cả read-only |
| 5–6 | Build prompt | `SmartSetupPrompts` (tách riêng): system = quy tắc + schema JSON; user = context + mô tả |
| 7 | Gọi provider | `JsonMode: true`, `Temperature`/`MaxTokens` từ `DeepSeekOptions` |
| 8 | Parse JSON | bỏ code fence phòng thủ → deserialize case-insensitive; **retry 1 lần** với prompt chặt hơn khi lỗi; hỏng cả 2 → **502** |
| 9 | Normalize | title trim 1–200 (sai → **bỏ task**), description cắt ≤ 2000, priority sai → `null`, labels dedupe/trim/≤ 5×(≤ 80), cắt theo `MaxTaskCount`; 0 task → **400** |
| 10 | Resolve assignee | khớp **exact** (case-insensitive) → khớp **contains** → nếu AI để trống **và** workspace chỉ có **1 thành viên** thì gán thành viên đó; không khớp ai thì giữ tên + `matched=false` |
| 11 | Resolve label | khớp nhãn có sẵn (case-insensitive) → `labelId` + `exists=true`; còn lại là nhãn mới `exists=false` |
| 12 | Trả proposal | `SmartSetupProposal` trong bộ nhớ — **không** `SaveChanges` |

### Ngưỡng & hằng số

| Hằng số | Giá trị | Nơi dùng |
|---|---|---|
| `SmartSetupService.MaxDescriptionLength` | 4000 | validate input |
| `SmartSetupService.MaxTitleLength` | 200 | title quá dài → bỏ task |
| `SmartSetupService.MaxTaskDescriptionLength` | 2000 | cắt mô tả |
| `SmartSetupService.MaxLabelsPerTask` | 5 | cắt nhãn |
| `SmartSetupService.MaxLabelLength` | 80 | bỏ nhãn quá dài |
| `DeepSeekOptions.MaxTaskCount` | 20 | trần số sub-task |

### Mã lỗi của endpoint

| Tình huống | Status | Body |
|---|---|---|
| Không đăng nhập | 401 | (auth middleware) |
| Thiếu/sai `X-XSRF-TOKEN` | 403 | `{ error: "CSRF token missing or invalid …" }` |
| Là Member (không phải Manager/Admin) | 403 | `{ error: "Requires Manager or Admin role in this workspace." }` |
| Mô tả rỗng / > 4000 | 400 | `{ error: "Description must be 1–4000 characters…" }` |
| AI không trả sub-task hợp lệ nào | 400 | `{ error: "AI không trả về sub-task hợp lệ nào …" }` |
| Board không tồn tại | 404 | `{ error: "Board not found." }` |
| DeepSeek lỗi/timeout/JSON hỏng sau retry | 502 | `{ error: "DeepSeek …" }` / `"Không parse được JSON từ AI sau 2 lần thử …"` |

### Hàm thuần có thể verify trực tiếp (không cần HTTP/AI)

`SmartSetupService.BuildProposal`, `NormalizeTasks`, `NormalizePriority`, `ResolveAssignee`, `ResolveLabels`
là `public static` để test/verify được logic chuẩn hoá mà không cần dựng API — dùng cho §4 verify và sau này
làm điểm tựa cho test backend (Giai đoạn 8).

### Ghi chú chi phí token

Context gửi AI chỉ gồm **tên**: board, cột, thành viên, nhãn — không gửi nội dung task/lịch sử. Mô tả bị cap
4000 ký tự, `max_tokens`/`temperature` lấy từ config, `MaxTaskCount` chặn số task. Retry chỉ xảy ra khi JSON
hỏng (tối đa 1 lần).

## Phase 4 — Accountability Layer (đã hoàn tất 100%)

### Endpoints Accountability Layer

| Endpoint | Quyền | Method | Thành công | Ghi chú |
|---|---|---|---|---|
| `/api/boards/{boardId}/smart-setup/confirm` | Manager/Admin | POST | 201 `AiActionLog` | Tạo log `Pending` — không ghi task |
| `/api/boards/{boardId}/ai-actions` | Manager/Admin | GET | 200 `AiActionLog[]` | Filter `?status=` và `?take=`, sort `createdAt DESC` |
| `/api/ai-actions/{logId}` | Manager/Admin | GET | 200 `AiActionLogDetail` | Trả chi tiết + snapshots |
| `/api/ai-actions/{logId}/approve` | Manager/Admin | POST | 200 `AiActionLogDetail` | Duyệt và ghi DB thật qua Applier (409 nếu không Pending) |
| `/api/ai-actions/{logId}/reject` | Manager/Admin | POST | 200 `AiActionLogDetail` | Từ chối (body `{ note? }`) |
| `/api/ai-actions/{logId}/undo` | Manager/Admin | POST | 200 `AiActionLogDetail` | Hoàn tác (soft-delete tasks, dọn nhãn, 409 nếu không Approved) |

### Vòng đời trạng thái
```
Pending → Approved → Undone
Pending → Rejected
```
Mọi chuyển trạng thái khác trả **409 Conflict**.

### Contract bàn giao Giai đoạn 5 (AI Observer)
- Mọi hành động AI ghi dữ liệu mới **bắt buộc** đi qua `IAiActionService` và cài đặt một `IAiActionApplier` tương ứng.
- Lớp AI Observer chỉ đọc (`ai_action_logs`, board data) và phát cảnh báo/notifications, không ghi đè dữ liệu nghiệp vụ.

## Phase 5 — AI Observer (đã hoàn tất, verify 178 check PASS)

Observer chạy nền theo chu kỳ, phát hiện bất thường trên board và **chỉ báo cho Manager/Admin** qua
notification trong app. Nó là lớp *đọc/cảnh báo*: chỉ ghi `activity_logs` / `notifications` /
`ai_observer_runs`, **không** chạm `tasks`/`labels`/`task_labels` và **không** đi qua Accountability Layer.

### Endpoints Phase 5

| Endpoint | Quyền | Method | Thành công | Ghi chú |
|---|---|---|---|---|
| `/api/workspaces/{id}/observer/scan` | Manager/Admin | POST | 200 `ObserverScanResponse` | **Quét đồng bộ**; 403 Member; 404 workspace lạ; **502** nếu AI lỗi (run đã ghi `Failed`) |
| `/api/workspaces/{id}/observer/runs?take=` | Manager/Admin | GET | 200 `ObserverRunResponse[]` | sort `startedAt DESC`; `take` 1–50 (default 20) |
| `/api/observer/runs/{runId}` | Manager/Admin | GET | 200 `ObserverRunDetailResponse` | kèm `summary` + `findings[]` |
| `/api/notifications?isRead=&take=` | mọi user (chỉ row của mình) | GET | 200 `NotificationListResponse` | `take` 1–100 (default 20); `isRead` strict (`bogus` ⇒ 400 `{error}`) |
| `/api/notifications/{id}/read` | owner | POST | 200 `NotificationResponse` | idempotent (không đổi `readAt`); alert của người khác ⇒ **404** |
| `/api/notifications/read-all` | mọi user | POST | 200 `{ updated: n }` | chỉ row của mình; gọi lại ⇒ `0` |

`POST` cần `X-XSRF-TOKEN`; `GET` không cần. Body lỗi luôn `{ "error": "…" }` (trừ 400 ProblemDetails
khi sai kiểu tham số, ví dụ `?take=abc`).

### Luồng quét của `ObserverService.ScanAsync` (9 bước)

1. `Enabled=false` + không chỉ định workspace ⇒ `Skipped` (`Disabled`), không ghi DB. Quét tay vẫn chạy.
2. Giành lock: `SemaphoreSlim(1,1)` trong process + `pg_try_advisory_lock` trên connection riêng; thua ⇒ `Skipped` (`AlreadyRunning`); `finally` luôn unlock.
3. Nạp workspace active có task mở, sắp theo hoạt động gần nhất, cap `MaxWorkspacesPerRun`.
4. Nạp snapshot bounded: task + column (`IsDone`) + tên assignee + comment count/`max(created_at)`.
5. `ObserverSignalDetector.Analyze` → 0 tín hiệu ⇒ `Completed`, `aiCalled=false`, **0 token**.
6. `ObserverSummarizer.BuildRequest` → prompt tóm tắt (marker `{"agent":"observer"}` ở dòng đầu, JSON mode).
7. Gọi `IAiProvider` (không retry); lỗi/JSON hỏng ⇒ run `Failed` + `summary.error`, 0 notification.
8. `ObserverFindingValidator` (giao evidence với tín hiệu ⇒ chống hallucination).
9. `NotifyManagersAsync` → ghi `ai_observer_runs` → prune `activity_logs` cũ theo retention.

### Tín hiệu & ngưỡng (config section `Observer`)

| Tín hiệu | Điều kiện | Severity | Ngưỡng |
|---|---|---|---|
| `OverdueTask` | task mở, `due_date < now` | High; **Critical** nếu `overdueDays > 2×` | `CriticalOverdueDays=3` |
| `StalledTask` | `max(updated_at, lastCommentAt, created_at) < now − StalledDays` (**strict**) | Medium; **High** nếu `≥ 2×` | `StalledDays=7` |
| `Overload` | 1 người: `openCount ≥ 5` **hoặc** `overdueCount ≥ 2` | High; **Critical** nếu `openCount ≥ 2×` | `OverloadMinOpenTasks=5`, `OverloadOverdueMin=2` |
| `Bottleneck` | cột không done: `openCount ≥ 5` **và** `stalledInColumn ≥ 2` | Medium | `BottleneckMinTasks=5`, `BottleneckStalledMinTasks=2` |

Mỗi **loại** tín hiệu được gom thành **một** signal (evidence bị cap `MaxEvidenceIdsPerSignal=10`);
`MaxSignalsPerWorkspace=20` cắt khi có nhiều tín hiệu khác loại → `truncatedSignals` ghi vào `summary`.
Thứ tự: severity giảm dần → weight giảm dần → thứ tự phát hiện.

### Kiểm soát chi phí & vận hành

| Key | Mặc định | Ghi chú |
|---|---|---|
| `Enabled` | `true` | `false` ⇒ timer không chạy (nút "Quét ngay" vẫn hoạt động) |
| `IntervalMinutes` / `StartupDelaySeconds` | `30` / `60` | chu kỳ & trễ khởi động |
| `LookbackHours` / `MaxLookbackDays` | `24` / `7` | `LookbackHours` là **sàn**, `MaxLookbackDays` là **trần** (host ngủ lâu ⇒ lùi tối đa 7 ngày) |
| `MaxPromptCharacters` | `12000` | prompt bị cắt tín hiệu yếu nhất cho tới khi vừa; **JSON luôn hợp lệ** |
| `MaxOutputTokens` / `Temperature` | `1500` / `0.0` | cap riêng của Observer; không retry |
| `MaxNotificationsPerRun` / `MaxManagersPerWorkspace` | `50` / `10` | cap fan-out |
| `DeduplicationWindowHours` | `24` | không lặp lại cùng `(workspace, type, taskIds, userIds)` |
| `MinSeverityToNotify` | `Medium` | finding thấp hơn bị bỏ |
| `RetentionDays` / `RetentionDeleteBatchSize` | `30` / `5000` | prune `activity_logs` cũ **theo workspace** cuối run thành công |

**Hạn chế đã biết:** 2 tiến trình quét **thật sự đồng thời** có thể cùng ghi notification trước khi bên nào
commit dedupe (nhịp 30 phút khiến thực tế không xảy ra). Trên free-tier, app sleep ⇒ Observer chỉ chạy khi
app thức (xử lý ở Giai đoạn 8 bằng cron ngoài/keep-alive).

**Kênh gửi:** hiện chỉ **in-app**. Email là hạng mục *tương lai* — thêm implementation mới bên cạnh
`INotificationService` (gợi ý `INotificationChannel`), **không** sửa Observer.

### `FakeAiProvider` — hai nhánh

`CompleteAsync` nhận diện prompt Observer bằng marker `{"agent":"observer"}` ở đầu `UserPrompt` (sau khi
`TrimStart`) ⇒ trả `{ "findings": [...] }` dùng **ID thật lấy từ `evidence` trong prompt** (luôn qua được
validator). Không có marker ⇒ vẫn trả proposal Smart Setup như Phase 3. Payload hỏng ⇒ `findings: []` +
`LogWarning` (không throw, không im lặng).

## Verify §1 (đã chạy)

1. `dotnet build TeamNexus.sln` → Build succeeded, 0 warning / 0 error.
2. Boot API không có key → log Warning + health `200`.
3. Boot API với `DeepSeek__ApiKey` giả → log Information `provider=DeepSeekAiProvider`,
   health `200`; kiểm tra log không chứa giá trị key.
4. `src/TeamNexus.Persistence/Migrations/` không đổi (vẫn 3 migration Phase 1/2).

## Verify §2 (đã chạy)

Harness tạm ngoài workspace (`%TEMP%\tn-ai-s2-verify`, không commit) dùng `HttpMessageHandler`
stub nên **không cần API key thật, không cần mạng** — tất cả check PASS:

1. **Request shape:** method `POST`, URI `https://api.deepseek.com/chat/completions`,
   `Authorization: Bearer …`, body có `model`/`max_tokens`/`temperature`/
   `response_format.type=json_object`/`messages[system,user]`; `JsonMode=false` → không có `response_format`.
2. **Response parse:** `content` + `usage.prompt_tokens`/`completion_tokens`; thiếu `usage` → counts `null`, không lỗi.
3. **Failure mapping:** HTTP 429, `content` rỗng, JSON hỏng, timeout → đều `AiProviderException`
   với `StatusCode == 502`; caller cancel → `OperationCanceledException` (không bọc);
   validate `MaxTokens`/`Temperature`/prompt rỗng → 502 **và không gửi HTTP request nào**.
4. **`FakeAiProvider`:** content không có code fence, parse được JSON, 3 task, priority
   hợp lệ/null, có task 2 label, assignee có tên và `null`, prompt chứa quote/backslash/newline
   vẫn cho JSON hợp lệ, `MaxTaskCount` cắt đúng, cancel token được tôn trọng.
5. **Chọn provider qua DI:** config không key → `FakeAiProvider`; có key → `DeepSeekAiProvider`;
   `BaseUrl` có `/` cuối được chuẩn hoá; `Timeout` = `TimeoutSeconds`.
6. **Boot API 2 nhánh** (cổng 5197/5196) → `GET /api/health` = 200, log đúng nhánh, không lộ key.

> Chưa verify với DeepSeek thật trong bước §2 (chưa có key) — đã chạy ở §3, xem mục dưới.

## Verify §3 (đã chạy)

Harness tạm ngoài workspace (không commit, đã xoá sau khi chạy). Hai phần:

**A. API thật + PostgreSQL thật** (API chạy ở cổng 5190; JWT tự ký bằng chính `Jwt:SigningKey`
trong User Secrets, đúng claims của `JwtService`):

| Kiểm tra | Kết quả |
|---|---|
| `GET /api/health` | 200 |
| `GET .../members` không auth | **401** |
| `GET .../members` có JWT | **200**; số phần tử khớp `workspace_members`; đúng 4 field `userId`/`displayName`/`role`/`avatarUrl`; `role` hợp lệ; mọi `userId` khớp DB; `displayName`+`role` của phần tử đầu khớp DB |
| `GET .../members` workspace không tồn tại | **404** |
| `POST .../smart-setup` không auth | **401** |
| `POST .../smart-setup` có auth nhưng thiếu `X-XSRF-TOKEN` | **403** |
| `GET /api/auth/antiforgery` | cấp được token → dùng cho POST |
| `POST .../smart-setup` auth + CSRF hợp lệ | **500** (đi hết được filter tới stub §4.3 — xác nhận route/auth/CSRF/DI hoàn chỉnh, không 404/405) |
| `workspace_members` / `tasks` / `labels` trước ↔ sau toàn bộ request | **không đổi** (2/10/0) |

> Một case được bỏ qua: "non-member → 404". DB local chỉ có **1 user** và user đó thuộc chính
> workspace đang test, nên không tồn tại outsider để kiểm chứng. Cùng cơ chế
> `IWorkspaceAccess.RequireMemberAsync` đã được kiểm chứng gián tiếp bằng case
> "unknown workspace → 404" (pass).

**B. DeepSeek thật** (key trong User Secrets, resolve `IAiProvider` thật qua `AddAiModule`):

```
provider=DeepSeekAiProvider, keyConfigured=True
latency=2352ms, chars=1293, promptTokens=155, completionTokens=447
```

`content` là JSON parse được, có `summary` + `tasks[]` không rỗng, `tasks[0].title` không rỗng
→ xác nhận key hợp lệ và contract §2 chạy đúng với API thật (không chỉ stub handler).

**C. `GET /api/workspaces/{workspaceId}/members` (module Board, §3.1)** được verify trong phần A;
chi tiết thiết kế xem `src/Modules/Board/TeamNexus.Modules.Board/README.md`.

## Verify §4 (đã chạy)

Harness tạm ngoài workspace (không commit, đã xoá sau khi chạy), chạy **hai nhánh** trên API thật
(cổng 5191) + PostgreSQL thật, JWT tự ký bằng `Jwt:SigningKey`:

| Nhóm | Kết quả |
|---|---|
| Guards | không auth → **401**; thiếu CSRF → **403**; mô tả rỗng → **400**; mô tả 4001 ký tự → **400**; board không tồn tại → **404** |
| DeepSeek thật | **200** trong ~3.1–3.6s, 7 task, `priority=[High,High,High,High,Medium,High,Medium]`, assignee matched 7/7, 21 nhãn mới (`exists=false`) |
| `FakeAiProvider` (không key) | **200** trong **83ms**, đúng 3 task, `priority=[High,Medium,null]`, assignee **matched=2 / unmatched=1** ("Linh" không có trong workspace, giữ tên), nhãn mới 5 — đúng mọi nhánh resolve |
| Shape | mỗi task đúng 5 field `title/description/priority/labels/assignee`; title 1–200; ≤ 5 nhãn, mỗi nhãn ≤ 80; `exists=true` ⇒ có `labelId` và tên khớp nhãn workspace; `exists=false` ⇒ `labelId=null`; `matched=true` ⇒ có `userId`; `matched=false` → `userId=null` + giữ `displayName` |
| Không ghi DB | `tasks` 10→10, `labels` 0→0, `task_labels` 0→0, `workspace_members` 2→2 |
| Hàm thuần | priority `urgent/HIGH` → chuẩn hoá, `Critical` → `null`; title rỗng/> 200 → bỏ task; description 2500 → cắt 2000; labels dedupe (`backend`/`BACKEND`)/bỏ rỗng/bỏ > 80/cắt 5; `MaxTaskCount=2` → 2; bỏ hết task → `BadRequestException`; assignee exact / contains ("Linh" → "Nguyen Linh") / unknown → `matched=false` / null + 1 member → gán / null + 2 members → `null` / 0 member → `null` |

> Case "Member → 403" **chưa verify được**: DB local chỉ có 1 user (Admin) trong workspace, không có
> Member nào để thử. Cơ chế `RequireManagerAsync` là code sẵn có của Board và đã được verify ở Giai đoạn 2.
>
> Case "provider lỗi/timeout → 502" đã verify ở §2 bằng stub handler (HTTP 429/timeout/JSON hỏng) và
> logic retry-1-lần được verify qua code path; không cố tình tạo lỗi DeepSeek thật để tránh tốn token.

## Verify §5 — AI Observer (đã chạy, 178 check PASS toàn Giai đoạn 5)

| Nhóm | Kết quả |
|---|---|
| A. Schema & migration | **28/28** — 3 bảng, 3 jsonb, CHECK `ck_ai_observer_runs_status`, 6 FK `ON DELETE RESTRICT` (`23001`), `status='Bogus'` → `23514`, jsonb hỏng → `22P02` |
| B. Detector + validator/summarizer thuần | **44/44** + **15/15** (+ **7/7** chạy lại `FakeAiProvider` sau fix) |
| C. `ObserverBackgroundService` | **3/3** — `Enabled=false` ⇒ host start/stop sạch, 0 run; `Enabled=true` + delay 30s ⇒ không exception |
| D. Scan end-to-end (DB thật) | **6/6** — `signalsDetected=4` (Overdue/Stalled/Overload/Bottleneck), `notifications = findings × manager`, `tasks`/`ai_action_logs` **không đổi** |
| E. Token/chi phí | **3/3** — 0 tín hiệu ⇒ `aiCalled=false`, provider **không được gọi**, prompt 4133 ≤ 12000 ký tự, không chứa `description` |
| F. HTTP API (§5) | **32/32** — xem bảng endpoint ở trên; Member ⇒ **403** cả 3 route Observer; 401/403/404/405/502/400 đúng |
| G. Activity log | **31/31** — 6 loại sự kiện từ `TaskService`/`CommentService`, mutation thất bại không sinh log |
| H/I/J. Dedupe, concurrency, failure modes | **2/2**, **3/3**, **4/4** — dedupe 24h; 1 `Completed` + 1 `Skipped(AlreadyRunning)`; JSON hỏng/provider lỗi ⇒ `Failed` + 0 notification |

Harness đặt **ngoài workspace** (`%TEMP%`, đã xoá), API thật + PostgreSQL thật + `FakeAiProvider`/stub
⇒ **không gọi DeepSeek, không tốn token**. Chi tiết: `Project-Documents/report/phase-5-ai-observer-test-report.md`.

**Lưu ý khi viết harness cho module này:**
1. Lấy lại token CSRF **sau** khi gắn JWT cookie (token ẩn danh 155 ký tự ≠ token đã bind identity 198 ký tự; echo token cũ ⇒ 403).
2. Seed fixture bằng **raw SQL**: `TeamNexusDbContext.SaveChanges` tự stamp `created_at`/`updated_at` cho mọi `IAuditableEntity` khi insert ⇒ seed qua EF làm task "cũ" thành "vừa tạo".
3. Boot API thật rồi stop trong **< 60s** (`StartupDelaySeconds`) hoặc đặt `Observer__Enabled=false` để observer không quét DB dev.
4. Khi API bật, `AddAiModule` resolve `FakeAiProvider` nếu `DeepSeek:ApiKey` trống — đó là cấu hình đúng cho verify offline.

## Phase 7 — AI Agent Executor (🔄 đang làm)

Nâng AI từ **đề xuất/quan sát** (Phase 3–5) lên **thực thi**: Agent là một **thành viên ảo** của workspace
(`workspace_members.member_type = 'ai_agent'`, một row `users` riêng cho mỗi workspace) và được gán task bằng đúng thao tác
assign sẵn có. Khi Manager bấm **"Chạy Agent"**, Agent chạy một vòng lặp tool-calling và **mọi kết quả ghi dữ liệu thật đều đi
qua Accountability Layer** — không có cơ chế giải trình riêng cho Agent.

> ⚠️ **Mục này mô tả thiết kế đã chốt ở bước lập kế hoạch, chưa phải code đang chạy.** Nguồn duy nhất:
> `Project-Documents/tasks/phase-7-ai-agent-executor.md` (§0 bảng quyết định D1–D20). Schema: `04-database-design.md` §3.3, §3.4, §3.5, §3.8.

### Danh tính Agent (D1)

| Điểm | Quyết định |
|---|---|
| Ai là agent? | **1 `ApplicationUser` riêng cho mỗi workspace**, sinh **lazy** ở lần đầu task được gán cho agent |
| Vì sao per-workspace | `workspace_members` có composite PK `(workspace_id, user_id)` ⇒ một user chỉ thuộc **một** workspace |
| Vì sao phải là `users` row thật | `tasks.assignee_id` FK→`users`; `task_comments.author_id` **NOT NULL** FK→`users` ⇒ agent phải đăng comment và nhận task như người thật |
| Phân biệt với người | `workspace_members.member_type` ∈ {`human`,`ai_agent`} + partial UQ `(workspace_id) WHERE member_type='ai_agent'` |
| Bảo mật | `password_hash = null`, `email = null`, `lockout_enabled = true`, `two_factor_enabled = true`, **không** `user_logins` ⇒ agent **không thể đăng nhập** |
| Vai trò | `role = 'Member'` — agent **không** phải Manager; quyền ghi dữ liệu thật thuộc về con người duyệt |

### Vòng lặp thực thi & bộ tool (whitelist đúng 4, D9)

`IAiProvider.CompleteAsync` (Phase 3) **không đổi**; Phase 7 thêm interface thứ hai `IAiToolCallingProvider.ChatAsync`
(nhiều lượt, `tools`/`tool_calls`) và `DeepSeekAiProvider` implement **cả hai**.

| Tool | Việc | Ghi chú an toàn |
|---|---|---|
| `SearchSystemData` | Truy vấn task/comment/board **nội bộ** | `workspaceId`/`boardId`/`taskId` lấy từ **context**, **không** từ arguments ⇒ model không đọc chéo workspace |
| `WebSearch` | Web search qua Tavily (`api_key` trong body, `results[].content`) | `Tavily:ApiKey` rỗng ⇒ `FakeWebSearchProvider` (kết quả mẫu, offline) |
| `DraftOutput` | Kết quả ngắn → **comment**; dài → **attachment** | **Không** ghi DB; chỉ trả cho model để kết thúc loop |
| `RequestClarification` | Hỏi lại trưởng nhóm khi thiếu thông tin | **Không** đi qua Accountability (là câu hỏi, không phải kết quả ghi dữ liệu) |

Tên tool ngoài whitelist ⇒ trả `{"error": …}` cho model (kịch bản "model gọi bậy" phải verify được). Provider thứ 3 của
`FakeAiProvider` (marker `{"agent":"executor"}`) chạy kịch bản tool-call **xác định** ⇒ verify end-to-end offline, **0 token**.

### Vòng đời một lượt chạy (`agent_runs`)

```
Running ──► AwaitingApproval      (DraftOutput ⇒ AiActionLog Pending, chờ Manager duyệt)
        ──► AwaitingClarification (RequestClarification ⇒ comment agent + task sang cột "Chờ làm rõ")
        ──► Completed
        ──► Failed                (+ stop_reason: ToolLimit | TimeLimit | TokenBudget | ProviderError | Cancelled | TaskChanged | InternalError)
```

- **`status` tách khỏi `stop_reason` (D4):** `BudgetExceeded` **không** phải status riêng — nó là `stop_reason`
  (`ToolLimit`/`TimeLimit`/`TokenBudget`) trên `status = Failed`, theo đúng tiền lệ `ai_observer_runs.status = Skipped`
  **không phải lỗi** ở Phase 5. Nhờ vậy phân biệt được *hết ngân sách (chủ ý)* với *lỗi provider (bất khả kháng)*.
- **Append-only (D14):** "Chạy lại" **tạo run mới** (`previous_run_id` trỏ run cũ); run cũ **không** bao giờ bị sửa.
- **Guardrail (D12):** 15 tool-call · 5 phút wall-clock · ~50 000 token · 20 lượt gọi model — kiểm **sau mỗi vòng**, vượt ⇒
  dừng + `stop_reason` + **đúng một** notification (`AgentRunFailed`, `notification_sent = true` chống spam).
- **Chống chạy chồng (D11):** `pg_try_advisory_lock` theo `task_id` + `SemaphoreSlim(1,1)`; không lấy được lock ⇒ **409**.
- **Reaper (D13):** app free-tier sleep/recycle giữa run ⇒ `AgentRunReaper` lúc khởi động đóng mọi run `Running` quá hạn
  thành `Failed`/`InternalError`, nếu không card Kanban kẹt "Đang chạy" vĩnh viễn.

### Kết quả & duyệt (tái dùng Phase 4)

Hai applier mới đi qua **nguyên** vòng đời `Pending → Approved/Rejected → Undone` (CAS chống duyệt trùng, transaction,
`applied_snapshot`):

| Action | Apply khi được duyệt | Undo |
|---|---|---|
| `PostComment` (`entity_type='Task'`) | `ICommentService.CreateCommentAsync(..., userId = agent_user_id)` ⇒ có luôn `activity_logs` + SignalR `CommentAdded` + `authorName` = tên agent | soft delete comment |
| `PostAttachment` (`entity_type='Task'`) | INSERT `task_attachments` (`bytea`, cap **512 KB**) | **hard delete** (bảng duy nhất không soft-delete — file để lại là tiêu quota thật) |

`requested_by_user_id = agent_user_id`, `decided_by_user_id` = Manager duyệt ⇒ truy vết "agent làm hay người làm" chỉ cần so Guid.

### Endpoint của Phase 7 (7 route)

| Endpoint | Quyền | Thành công |
|---|---|---|
| `POST /api/tasks/{taskId}/agent-runs` | Manager/Admin | **202** `AgentRunResponse` (chạy ở scope nền) |
| `POST /api/tasks/{taskId}/agent-runs/{runId}/rerun` | Manager/Admin | **202** — nút **"Chạy lại"** |
| `GET /api/tasks/{taskId}/agent-runs?take=` | Member+ | 200 `AgentRunResponse[]` |
| `GET /api/agent-runs/{runId}` | Member+ | 200 `AgentRunDetailResponse` (kèm `toolCallTrace`) |
| `POST /api/agent-runs/{runId}/cancel` | Manager/Admin | 200 (409 nếu không còn `Running`) |
| `GET /api/tasks/{taskId}/attachments` | Member+ | 200 `AttachmentResponse[]` |
| `GET /api/tasks/{taskId}/attachments/{id}/download` | Member+ | 200 file bytes (`Results.File`) |

`Agent:Enabled=false` ⇒ **503** ở cả 3 route ghi (tiền lệ `Reports:Enabled`). Real-time: event **`AgentRunProgress`** broadcast
qua `IBoardEventPublisher` có sẵn (thêm 1 method; **không** sửa `BoardHub` constants) — chỉ **thay đổi trạng thái**, **không**
stream token.

### Hạn chế đã biết của Phase 7 (ghi trước để không ai tưởng là bug)

1. **Không** stream token — chỉ broadcast trạng thái.
2. Chạy **in-process** ⇒ app free-tier sleep giữa run là mất run đó (reaper đóng lại + nút "Chạy lại" là lưới an toàn). **Không** dùng Hangfire/Redis.
3. `POST /cancel` chỉ hoạt động trong **cùng instance** đang chạy run đó.
4. Chưa prune `agent_runs`/`task_attachments` (file là nội dung người dùng đã duyệt).
5. Task do agent thực hiện **sẽ** xuất hiện trong `byAssignee` của báo cáo và tín hiệu `Overload` — đây là **hành vi mong muốn**.
6. Chưa có test xUnit (để Phase 8); verify bằng harness tạm ngoài workspace như Phase 2–6.
