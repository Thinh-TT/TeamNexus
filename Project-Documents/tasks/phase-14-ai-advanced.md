# Giai đoạn 14 — Nâng cao AI (Kế hoạch chia task)

> **Trạng thái thi hành:** ✅ **BACKEND ĐÃ HOÀN THÀNH & VERIFY · FRONTEND 📤 BÀN GIAO cho antigravity.**
> **Nguồn:** `Project-Documents/03-roadmap.md` → *Giai đoạn 14: Nâng cao AI* (4 ô A/B/C/D).
> **Tiền đề đã merge:** Giai đoạn 7 · 8 · 9 · 10 · 11 · 12 · **13 (Trực quan hóa & Thông báo Chủ động)**.
> **📤 Note bàn giao Frontend:** `tasks/phase-14-remaining-frontend-handover.md`.
> **Báo cáo nghiệm thu:** `report/phase-14-ai-advanced-test-report.md`.
>
> **Kết quả ĐO THẬT (số đo thắng tài liệu):**
> - `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` → ✅ **0 Warning / 0 Error**
> - `dotnet test` (PostgreSQL thật) → **584 total**, **Failed 0 / Passed 584 / Skipped 0** ở 2/3 lượt chạy đầy đủ
>   (baseline **480** ⇒ **+104**). Lượt thứ ba: **583 PASS + 1 FAIL** là **flake có sẵn**
>   `AgentRun_CancelStopsTheRunAndRecordsCancelled` (chạy riêng ⇒ PASS) — xem **R10**.
> - `dotnet ef migrations list` → ✅ **10** — **KHÔNG đổi** · `has-pending-model-changes` → ✅ **sạch**
> - **Không** file nào trong `Persistence/Migrations/` bị thêm/sửa (kiểm bằng `git diff --stat`)
> - Cổng CI: `ci-backend.yml` `480` → **`584`** · `ci-web.yml` **giữ `-lt 508`** (FE chưa làm)
>
> **⬆️ Lệch so với ước tính trong tài liệu này:** kế hoạch ước **+72**; đo thật **+104**.
> Nguyên nhân: `AiGuardrailTests` (18) là hạng mục **bổ sung khi rà §5** (chưa có trong ước tính), và nhiều
> test có nhiều `[Theory]`/`it(...)`. **Số đo thắng tài liệu** (đúng như **R9** đã dự liệu).
>
> **Schema:** ⛔ **KHÔNG có migration nào.** `dotnet ef migrations list` **vẫn là 10**.
> Cả 3 tính năng đều không cần schema mới — xem **D1**. Đây là điểm **khác** Giai đoạn 13 (giai đoạn đó có 1 migration additive).

---

## 0. Mục tiêu & 4 ô hoàn thiện

Nâng AI từ vai trò **quan sát/đề xuất** lên **tương tác trực tiếp trong ngữ cảnh task**, đồng thời biến Observer
từ "phát hiện sau khi xảy ra" thành **dự báo rủi ro chủ động**.

| # | Yêu cầu roadmap (nguyên văn) | Trạng thái đầu kỳ (đã khảo sát code) | Việc phải làm |
|---|---|---|---|
| **A** | **AI Task Chat**: nút "Hỏi AI" trong `TaskDetailModal` — người dùng chat với AI trong ngữ cảnh task (title, description, comment); backend dùng `IAiProvider` + **streaming SSE**; kết quả đi qua Accountability Layer (Pending → Approve/Reject) | **Chưa có gì.** `grep` `text/event-stream` / `StreamAsync` / `FlushAsync` toàn repo = **0 kết quả** (chỉ 1 comment nhắc "SSE" ở `BoardModule.cs:75` nói về **SignalR**). `IAiProvider`/`IAiToolCallingProvider` đều **không** stream. FE **không** có `EventSource`/`ReadableStream` nào | **§4** (backend port streaming + endpoint SSE + lưu bình luận qua Accountability) + **§8.1** (FE) |
| **B** | **Observer Dự báo Rủi ro**: thêm signal `AtRiskDeadline` (task chưa done, còn < 20% thời gian nhưng 0 update trong 48h) và `ProjectHealthScore` (0–100 tổng hợp) vào `ObserverSignalDetector`; hiển thị gauge "Sức khỏe dự án" trên `WorkspaceDashboardPage` | `ObserverSignalDetector` có **4** detector **thuần** (`OverdueTask`/`StalledTask`/`Overload`/`Bottleneck` + `AiRiskCandidate` sẽ thêm); `ObserverTaskSnapshot` **đã có đủ** `DueDate`/`CreatedAt`/`UpdatedAt`/`LastCommentAt`. `DashboardResponse` **chưa** có field sức khỏe | **§5** (signal + health) + **§8.2** (FE gauge) |
| **C** | **AI Board Template**: mở rộng Smart Setup (Giai đoạn 3) — AI đề xuất luôn **cấu trúc board (column) và 5–10 task khởi đầu** dựa trên mô tả dự án; UI preview trước khi confirm qua Accountability Layer | `SmartSetupService.GenerateAsync` **chỉ** sinh sub-task cho **1 board đã tồn tại**. Tạo board mới phải qua `BoardService.CreateBoardAsync` + `ColumnService.CreateColumnAsync` (**đã có**, dùng lại được). `AiActionTypes` chỉ có `CreateSubtasks`/`PostComment`/`PostAttachment` | **§6** (proposal có columns + confirm/applier mới) + **§8.3** (FE) |
| **D** | *"Guardrail và giới hạn token áp dụng cho mọi tính năng mới; test coverage ≥ baseline"* | `AgentGuardrails` + `DeepSeekOptions.MaxTokens` + `ObserverOptions.MaxPromptCharacters` đã có tiền lệ | **§7** (options + guard) + **§9** (test) |

**Ngoài 4 ô, 8 hạng mục kỹ thuật bắt buộc phát sinh (phát hiện khi đọc code):**

- **E** — **`AiActionContext.BoardId` là non-nullable** (`IAiActionApplier.cs:32–36`). Applier "tạo board" **không có**
  board để trỏ vào ⇒ phải nới thành `Guid?` + helper `RequireBoardId()`. Có **10 call site** phải sửa:
  `CreateSubtasksApplier` (9 chỗ) + `SearchSystemDataTool.cs:82`.
- **F** — **`AiActionService.ResolveAsync` chỉ xử lý `entity_type ∈ {Task, Board}`**; không có nhánh `Workspace`
  ⇒ `CreateBoardFromTemplate` sẽ **400**. Phải thêm nhánh `Workspace` (mirror nhánh `Task`).
- **G** — **`AiActionService.BuildAppliedSnapshotJson` hardcode field**; `createdColumnIds` mới **không** tự xuất hiện
  ⇒ nếu quên thì **Undo không tìm được cột nào**.
- **H** — **`ProjectHealthScore` không thể nằm ở `ObserverSignalDetector`** như roadmap viết: nó là shape để **đọc**
  (Board/dashboard) chứ không phải tín hiệu AI, và `DashboardService` (module **Board**) **không được** tham chiếu
  module **Ai** (bất biến `Ai → Board` một chiều, Giai đoạn 13 §9). Xem **D6** — đây là **lệch DoD có chủ ý, bắt buộc**.
- **I** — **`ObserverSignalDetector.Analyze` cắt tín hiệu theo `MaxSignalsPerWorkspace` (20)**, còn
  `ObserverSummarizer.BuildPayload` cắt **từ cuối danh sách** ⇒ tín hiệu mới `AtRiskDeadline` (Medium) **có thể bị ăn hết**
  ở workspace lớn. Phải đếm lại số tín hiệu **thật sự vào được prompt** (hiện chỉ tính theo `MaxPromptCharacters`).
- **J** — **`ObserverPrompts.SystemPrompt` liệt kê cứng 4 type** (`"OverdueTask, StalledTask, Overload, Bottleneck"`).
  Thêm type mà quên prompt ⇒ model **không bao giờ** phát ra, và validator sẽ drop ⇒ tính năng im lặng.
- **K** — **`ObserverVocabularyTests` assert đúng 4 type** ⇒ **buộc phải sửa 1 test cũ đang xanh**
  (lệch quy tắc "chỉ thêm test"; phải ghi tên test + lý do vào báo cáo).
- **L** — **`FakeAiProvider` và `ScriptedAiProvider` là 2 implementation của `IAiProvider`**. Thêm port streaming
  ⇒ **cả hai** phải implement, nếu không **mọi suite hiện có vỡ**.

---

## 1. Bảng quyết định kiến trúc (D1–D16) — chốt sẵn, không chọn lại

| # | Quyết định | Lý do / ràng buộc |
|---|---|---|
| **D1** | ⛔ **KHÔNG migration nào.** `dotnet ef migrations list` giữ **10**; `has-pending-model-changes` **sạch** | Cả 3 tính năng đều **không cần** schema: chat = transcript ở client + `ai_action_logs` (bảng **đã có**, cột `action`/`entity_type` là **free text varchar(64)**); health = **tính lại** mỗi request (thuần số học trên dữ liệu đã có); template = dùng `boards`/`board_columns`/`tasks` sẵn có. Đúng nguyên tắc "chỉ thêm cột/bảng khi không còn đường nào khác" (Giai đoạn 10 §0 / 12 D1 / 13 D3) |
| **D2** | **Chat KHÔNG lưu transcript ở DB.** Client giữ hội thoại trong state và gửi lại **toàn bộ** lịch sử mỗi request | Bảng `ai_task_chat_messages` là **bảng mới** cho một tính năng *best-effort*, phải trả giá bằng migration + retention + query filter + `TRUNCATE` trong harness. Transcript ở client là đủ cho phạm vi đồ án, và **chính lịch sử client gửi lên đã là input** nên không có rủi ro "server quên ngữ cảnh". Neo cứng: `maxMessages = 12`, `maxTotalChars = 8000` (≈ 2000 token) — vượt ⇒ **400** trước khi gọi provider (không tốn token) |
| **D3** | **SSE qua `POST` + `fetch` (`ReadableStream`), KHÔNG dùng `EventSource`** | `EventSource` **không** gửi được header `X-XSRF-TOKEN` và **không** POST được; mà mọi POST của repo đều qua `AntiforgeryValidationEndpointFilter`. `fetch` giữ nguyên hợp đồng CSRF (dùng lại `getXsrfToken()` của `httpClient.ts`) và `withCredentials` cho cookie HttpOnly |
| **D4** | Endpoint chat: `POST /api/tasks/{taskId}/ai-chat/stream` — **Member+**, `AntiforgeryValidationEndpointFilter` + `DomainExceptionFilter`; **mọi** guard (401/403/404/409/503) chạy **TRƯỚC** khi mở stream ⇒ lỗi trả **JSON + status thật**; sau khi stream đã mở thì lỗi đi ra dưới dạng **SSE `error`** | Trả `Results.Stream` **sau** khi đã validate ⇒ không bao giờ phải "đổi status sau khi đã gửi header". Đúng tiền lệ `ObserverEndpoints` (thrown ⇒ filter map status) |
| **D5** | **Bộ khung SSE** (`meta` → `delta`* → `done` \| `error`) xây bằng **hàm THUẦN** `AiChatSseWriter` | Đúng tiền lệ `EmailTemplates`/`ObserverNotificationFactory`: phần dễ sai nhất (format khung, escape) test được **không cần** DB, không cần mạng, không cần stream thật |
| **D6** | ⚠️ **LỆCH DoD #1 (có chủ ý, bắt buộc):** `ProjectHealthScore` **KHÔNG** đặt trong `ObserverSignalDetector`. Nó là **read model** đặt tại `Board/Services/ProjectHealth.cs` (**HÀM THUẦN**) và được **append** vào `DashboardResponse` | (a) `WorkspaceDashboardPage` là **Member+**, còn `ObserverSignalDetector` là nội bộ pipeline **Manager+**; đặt score ở Ai sẽ ép dashboard 403 với member hoặc phải mở quyền Observer. (b) `Board → Ai` **bị cấm** (Giai đoạn 13 §9) ⇒ nếu score ở Ai thì Board **không thể** đưa nó vào `DashboardResponse` ⇒ phải thêm endpoint thứ hai + fetch thứ hai. (c) Score **không** phải tín hiệu để AI diễn giải, nó là **con số để con người đọc**. Tín hiệu `AtRiskDeadline` **thì vẫn** nằm trong `ObserverSignalDetector` (đúng roadmap). **Bắt buộc** ghi rõ ở `03-roadmap.md` §10.3 + báo cáo + §1 này |
| **D7** | `AtRiskDeadline` = **"task chưa done, còn < 20% cửa sổ thời gian, và 0 update/comment trong 48 h"**, có thêm **sàn cửa sổ ≥ 2 ngày** để chống nhiễu | Đúng nguyên văn roadmap. Sàn 2 ngày là **cần thiết**: task tạo 10:00 hạn 22:00 cùng ngày có "cửa sổ" 12 h ⇒ "còn < 20%" (2 h24) sẽ bắn **cảnh báo giả** cho mọi task ngắn hạn. `Weight` = **số ngày còn lại** (làm tròn, kẹp `[0, 30]`, **không âm**) để ordering tất định. Severity: `Critical` khi còn **≤ 24 h**, `High` khi còn **≤ 72 h**, còn lại `Medium` |
| **D8** | `ProjectHealthScore` = **công thức cố định, tất định**, chạy trên `ProjectHealthInput` (record thuần) | Xem §5.2 cho công thức chính xác. `Clamp(0, 100)`, **không** `NaN`/`Infinity` khi mọi mẫu số = 0; đây là **hàm thuần** ⇒ test biên không cần DB (đúng tiền lệ `ReportAggregator`) |
| **D9** | Chat guard dùng section **`AiChatOptions`** riêng | Tái dùng `DeepSeekOptions.MaxTokens` sẽ khiến "giới hạn token cho **mọi** tính năng mới" (ô D) phụ thuộc vào knob của Smart Setup; một lần chỉnh `DeepSeek:MaxTokens` sẽ âm thầm nới cả chat. Section riêng giữ guardrail **địa phương**, đúng tiền lệ `DigestOptions`/`AgentOptions` |
| **D10** | **`IAiStreamingProvider` là port RIÊNG**, KHÔNG thêm method vào `IAiProvider` | Đúng lý lẽ đã ghi tại `AiProvider.cs:41–47`: thêm method vào `IAiProvider` buộc **mọi** implementation + **mọi** test double đổi cùng lúc. Đăng ký thứ 3 cùng switch `HasApiKey` (giống hệt `IAiToolCallingProvider` tại `AiModule.cs:195–202`) |
| **D11** | **"Kết quả đi qua Accountability Layer"** được thoả bằng: stream **trả lời chat** (không ghi gì) + **`POST .../ai-chat/message`** để người dùng **lưu câu trả lời thành bình luận** ⇒ tạo `ai_action_logs` `Pending` (action `PostComment`) ⇒ Manager **Approve/Reject/Undo** bằng **đúng** endpoint Giai đoạn 4 đã verify | "Mọi lượt chat tự động tạo Pending" sẽ nhấn chìm Notification Center và `AiActionHistoryDrawer` bằng log của mỗi câu hỏi, kể cả câu hỏi vu vơ — Accountability Layer sẽ mất giá trị. Cách chọn giữ **nguyên bất biến**: *không có dữ liệu nghiệp vụ nào bị ghi mà chưa Approved*, và **mọi** lượt ghi đều có log. Nút "Lưu thành bình luận" **disable** khi câu trả lời > 2000 ký tự (giới hạn `task_comments.content`) |
| **D12** | **Board template**: generate = `POST /api/workspaces/{workspaceId}/smart-setup/template` (Manager+); confirm = `POST .../smart-setup/template/confirm` (Pending, `entity_type = 'Workspace'`, action `CreateBoardFromTemplate`) | Smart Setup cũ **bắt buộc** có `boardId`; đề xuất **cấu trúc** board thì chưa có board ⇒ phải là endpoint **cấp workspace**. Tên `.../smart-setup/template` giữ nó trong **cùng họ route** đã verify thay vì mở họ route mới |
| **D13** | **Board template** = **đúng 1 board + N cột + M task**; `columns` 2–6 (tên ≤ 80), `tasks` 5–10 (`DeepSeekOptions.MaxTaskCount` vẫn là trần cứng). Cột `isDone` = cột **AI chỉ định**, nếu không có thì **pin cột cuối** | Roadmap ghi "5–10 task khởi đầu". Cần **đúng một** cột `is_done` để `ReportAggregator`/`DashboardService` (bất biến `isDone`) hoạt động **ngay** sau khi tạo; để AI tự do chọn `isDone` sẽ sinh board không có "done" ⇒ mọi số liệu sai |
| **D14** | **Board template Undo** = dùng `BoardService.DeleteBoardAsync` (**soft delete** `boards.deleted_at`) ⇒ **không** hard delete, **không** gọi API mới | `BoardTaskConfiguration` có query filter ẩn task khi board bị soft-delete (đã verify ở Giai đoạn 12 `DashboardService` remarks). Hard delete sẽ phá bất biến "append-only / mọi FK RESTRICT" của DB design §7 |
| **D15** | **KHÔNG đổi shape hợp đồng đã verify.** `DashboardResponse` **append field cuối** `Health` (nullable) — mọi client cũ vẫn deserialize | Đúng tiền lệ Giai đoạn 13 D13 (`digestEnabled` append cuối). `DashboardApiTests` hiện có đọc DTO **cục bộ** trong test ⇒ thêm field là additive, không vỡ; vẫn phải **chạy lại toàn bộ** `DashboardApiTests` để chứng minh |
| **D16** | `ObserverOptions` += `AtRiskDeadlineEnabled` (**mặc định `true`**), `AtRiskDeadlineRemainingRatio` (**0.20**), `AtRiskDeadlineMinWindowDays` (**2**), `AtRiskDeadlineStaleHours` (**48**); clamp `ratio ∈ [0.05, 0.95]`, các số ngày/giờ `≥ 1` | Đúng tiền lệ `BottleneckDetectionEnabled` (cờ riêng cho từng detector) để vận hành tắt được tín hiệu mới nếu nhiễu. `MaxSignalsPerWorkspace` hiện có thể khiến tín hiệu mới bị cắt ⇒ xử lý bằng **I** (đếm + ưu tiên) chứ **không** nâng trần (nâng trần = tăng token **mọi** workspace) |

### 1.1 Phát sinh **bắt buộc** khi hiển thực (ghi lại để không bị coi là sai lệch kế hoạch)

| # | Phát sinh | Vì sao |
|---|---|---|
| **P1** | `AiActionContext.BoardId` → `Guid?` + `RequireBoardId()`; sửa **10** call site | **E**. Applier cấp workspace không có board. `RequireBoardId()` ném lỗi **rõ ràng** thay vì `Guid.Empty` âm thầm |
| **P2** | `AiActionService.ResolveAsync` += nhánh `entity_type = 'Workspace'` | **F** |
| **P3** | `AiActionService.BuildAppliedSnapshotJson` += `createdColumnIds`; `AiActionAppliedResult` += `CreatedColumnIds` (**optional, mặc định `null`**) | **G**. Optional để **không** phải sửa `CreateSubtasksApplier`/`PostCommentApplier`/`PostAttachmentApplier`/`AgentRunOrchestrator` |
| **P4** | `ObserverService` ghi thêm `signalsPreserved` / `signalsDroppedBeforePrompt` vào jsonb `summary` | **I**. Không có số này thì **không ai biết** tín hiệu `AtRiskDeadline` có thực sự tới được model hay không |
| **P5** | `ObserverPrompts.SystemPrompt` liệt kê **5** type | **J** |
| **P6** | `ObserverSummarizer` ưu tiên tín hiệu khi phải cắt: sort theo `(rank severity desc, weight desc)`, chỉ drop từ **cuối** | **I** |
| **P7** | `ScriptedAiProvider` implement thêm `IAiStreamingProvider` | **L** |
| **P8** | FE `AiActionType` (`aiAction.types.ts:4`) + nhãn trong `AiActionLogItem.tsx:143` phải nhận thêm `'CreateBoardFromTemplate'` | **Việc frontend** — ghi ở `phase-14-remaining-frontend-handover.md` §3 |

---

## 2. §A — Backend: AI Task Chat (streaming SSE + Accountability Layer)

### 2.1 Port streaming (`D10`)

| File | Nội dung |
|---|---|
| `Ai/Services/AiProvider.cs` **(sửa)** | += `public interface IAiStreamingProvider { IAsyncEnumerable<AiStreamChunk> StreamAsync(AiStreamRequest request, CancellationToken ct = default); }` · `record AiStreamRequest(string SystemPrompt, IReadOnlyList<AiChatMessage> Messages, double Temperature, int MaxTokens)` · `record AiStreamChunk(string? Delta, int? PromptTokens, int? CompletionTokens, bool Done)` |
| `Ai/Services/DeepSeekAiProvider.cs` **(sửa)** | `: IAiProvider, IAiToolCallingProvider, IAiStreamingProvider`. `StreamAsync`: cùng payload + `stream = true`, đọc `ReadAsStreamAsync` → `StreamReader.ReadLineAsync`, gom `data: {...}` đến `data: [DONE]`; trích `choices[0].delta.content`; gom `usage`. **Mọi** lỗi transport/parse ⇒ `AiProviderException` (502); **không** log API key; `[EnumeratorCancellation]` trên `ct`. `ChatRequest` += `bool? Stream` (null ⇒ bỏ khỏi payload ⇒ Smart Setup/Agent **byte-identical**) |
| `Ai/Services/FakeAiProvider.cs` **(sửa)** | `: ..., IAiStreamingProvider`. `StreamAsync` chia câu trả lời mẫu tiếng Việt thành ≥ 4 khung, `Task.Yield()` giữa các khung ⇒ luồng **chunked thật**, API key rỗng vẫn verify được end-to-end. Nhận diện bằng `AiChatPrompts.Marker` trong system prompt |
| `Ai/AiModule.cs` **(sửa)** | `services.AddSingleton<IAiStreamingProvider>(...)` **cùng shape** `IAiToolCallingProvider` + 1 dòng log cấu hình `AiChat` |
| `Ai/Options/AiChatOptions.cs` **(mới)** | `SectionName = "AiChat"`, `Enabled = true`, `MaxHistoryMessages = 12`, `MaxHistoryChars = 8000`, `MaxOutputTokens = 1200`, `Temperature = 0.3`, `MaxCommentsInContext = 10`, `MaxCommentCharsInContext = 500`, `MaxTaskContextChars = 6000`, `MaxAnswerChars = 2000` + `Effective` (clamp) |

### 2.2 Service + SSE

| File | Nội dung |
|---|---|
| `Ai/DTOs/AiChatDtos.cs` **(mới)** | `AiChatMessageRequest(string Role, string Content)`; `AiChatSendRequest(IReadOnlyList<AiChatMessageRequest> Messages)`; `SaveAiChatMessageRequest(string Content)`; `AiChatMeta` |
| `Ai/Services/AiChatPrompts.cs` **(mới)** | `Marker` (định danh nhánh fake) + `BuildSystemPrompt()` (tiếng Việt; chỉ dùng ngữ cảnh được cấp, **không** bịa, **không** đề xuất ghi dữ liệu, trả lời ≤ 2000 ký tự) + `BuildContextBlock(...)` |
| `Ai/Services/AiChatSseWriter.cs` **(mới)** | **HÀM THUẦN**: `Meta(...)`, `Delta(string)`, `Done(...)`, `Error(string)` → khung `event: <name>\ndata: <json>\n\n` (JSON camelCase) |
| `Ai/Services/AiChatService.cs` **(mới)** | `IAiChatService.StreamAsync(taskId, request, userId, ct) → IAsyncEnumerable<string>` (**khung đã render**). Bước: (1) task (**404**, query filter ẩn task soft-deleted) → board → **`RequireMemberAsync`** (người ngoài ⇒ 404); (2) `AiChat:Enabled = false` ⇒ **503** `AiChatDisabledException`; (3) validate `Messages` (**400**: rỗng, role lạ, số lượng > `MaxHistoryMessages`, tổng ký tự > `MaxHistoryChars`, nội dung rỗng); (4) nạp ngữ cảnh (title/column/priority/dueDate + `MaxCommentsInContext` comment gần nhất, cắt `MaxCommentCharsInContext`, tổng ≤ `MaxTaskContextChars`); (5) `yield meta`; (6) `await foreach` từ `IAiStreamingProvider` ⇒ `yield Delta(...)` **từng khung, không buffer**; (7) bọc provider `try/catch` ⇒ `yield Error(...)` rồi `return`; (8) `yield Done(...)` |
| `Ai/Services/AiChatService.cs` (phần lưu) | `IAiChatService.SaveAnswerAsync(taskId, content, userId, ct) → AiActionLogResponse`. Guard: `RequireMemberAsync`; `content` 1..`MaxAnswerChars` (400); gọi `IAiActionService.RequestAgentOutputAsync(taskId, userId, AiActionTypes.PostComment, afterSnapshotJson, basisJson, ct)` ⇒ **tái dùng** `PostCommentApplier` đã verify (tác giả = người bấm lưu, Undo soft-delete) ⇒ **không** thêm applier |
| `Ai/Endpoints/AiChatEndpoints.cs` **(mới)** | `MapGroup("/api/tasks/{taskId:guid}/ai-chat")` + `WithTags("AiChat")` + `DomainExceptionFilter`. `POST /stream` (+antiforgery) ⇒ `text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`; `POST /message` (+antiforgery) ⇒ **201** |
| `Ai/AiModule.cs` **(sửa)** | `AddScoped<IAiChatService, AiChatService>()` + `MapAiModuleEndpoints` += `MapAiChatEndpoints()` |
| `src/TeamNexus.Api/appsettings.json` **(sửa)** | + section `"AiChat"` (giá trị mặc định + comment) |

### 2.3 Hợp đồng API chốt (frontend dùng đúng, không đoán)

```http
POST /api/tasks/{taskId}/ai-chat/stream   (Member+ · người ngoài workspace ⇒ 404 · AiChat:Enabled=false ⇒ 503)
  Header: Content-Type: application/json, X-XSRF-TOKEN: <bắt buộc>
  Body:   { "messages": [ { "role": "user" | "assistant", "content": "..." } ] }
  200 OK → Content-Type: text/event-stream

  event: meta
  data: {"taskId":"…","boardId":"…","workspaceId":"…","taskTitle":"…","historyMessages":3,"model":"deepseek-chat"}
  event: delta
  data: {"text":"Xin "}
  event: delta
  data: {"text":"chào…"}
  event: done
  data: {"answer":"…đầy đủ…","promptTokens":812,"completionTokens":240}
  event: error            ← chỉ khi lỗi SAU khi stream đã mở
  data: {"error":"DeepSeek không phản hồi trong 60s (timeout)."}
```
```http
POST /api/tasks/{taskId}/ai-chat/message   (Member+ · antiforgery)
  Body: { "content": "…≤ 2000 ký tự…" }
  201  → AiActionLogResponse { action: "PostComment", entityType: "Task", status: "Pending", … }
  400  → { "error": "…" }   (rỗng hoặc > 2000)
```

**Luật chốt:**
- `messages` vượt `MaxHistoryMessages`/`MaxHistoryChars` ⇒ **400** (không gọi provider).
- `messages` rỗng ⇒ **400**. `role` ngoài `{user, assistant}` ⇒ **400**.
- `done.answer` == nối các `delta.text` (contract test assert).
- Lỗi 4xx/5xx **trước** khi mở stream trả **JSON + status thật** (không phải SSE).
- **KHÔNG** có dữ liệu nghiệp vụ nào bị ghi khi chưa `Approved`.

### 2.4 Bằng chứng cần đo (§2)

```
dotnet build TeamNexus.sln -m:1 -nr:false             → 0 Warning(s) / 0 Error(s)
dotnet test --filter "FullyQualifiedName~AiTaskChat"   → PASS toàn bộ
dotnet ef migrations list --no-build                    → 10   (KHÔNG đổi)
```

---

## 3. §B — Backend: Observer dự báo rủi ro + Sức khỏe dự án

### 3.1 `AtRiskDeadline` (`D7`)

| File | Thay đổi |
|---|---|
| `Ai/Services/ObserverSignalDetector.cs` **(sửa)** | += `public const string AtRiskDeadline = "AtRiskDeadline";` + gọi `AnalyzeAtRiskDeadline(open, now, thresholds)` **sau** `AnalyzeOverdue` (giữ tie-break theo thứ tự khám phá) |
| `Ai/Services/ObserverRisk.cs` **(mới)** | `record AiRiskCandidate(Guid TaskId, int MinutesRemaining, int WindowDays)` + `static IReadOnlyList<AiRiskCandidate> Detect(open, now, thresholds)` + `static string SeverityFor(TimeSpan remaining)` — **HÀM THUẦN**, public để test biên **không** phải dựng cả signal set |
| `Ai/Services/ObserverSeverity.cs` **(sửa)** | `NotificationTypes` += `AtRiskDeadline`; `All` = **5** type, thứ tự `[OverdueTask, StalledTask, AtRiskDeadline, Overload, Bottleneck]` (**K**) |
| `Ai/Options/ObserverOptions.cs` **(sửa)** | **D16**: 4 property mới (clamp) + `ToThresholds()` map thêm 4 field |
| `Ai/Services/ObserverSignalDetector.cs` **(sửa)** | `record ObserverThresholds(...)` += 4 field mới ⇒ **mọi `new ObserverThresholds(...)` phải cập nhật** |
| `Ai/Services/ObserverPrompts.cs` **(sửa)** | **P5**: 5 type trong system prompt + 1 dòng giải thích `AtRiskDeadline` |
| `Ai/Services/ObserverSummarizer.cs` **(sửa)** | **P6**: pin thứ tự `(severity rank desc, weight desc)` trước khi cắt; chỉ `RemoveAt(included.Count - 1)` |
| `Ai/Services/ObserverService.cs` **(sửa)** | **P4**: `signalsPreserved` / `signalsDroppedBeforePrompt` vào `summary`; trả số này trong `ObserverScanOutcome`/`ObserverScanResponse` |

**Luật chốt `AtRiskDeadline`** (`window = dueDate − createdAt`, `remaining = dueDate − now`):
- Bỏ qua nếu `!AtRiskDeadlineEnabled`, `dueDate == null`, task ở cột `is_done`, `window < MinWindowDays ngày`.
- Bỏ qua nếu `remaining <= 0` (đã là `OverdueTask` — **không** báo trùng) hoặc `remaining >= ratio × window`.
- Bỏ qua nếu `now − ActivityAt(task) <= StaleHours` (`ActivityAt` **tái dùng** — comment mới hơn `updated_at` "đánh thức" task).
- Còn lại ⇒ **một** signal: `Type = "AtRiskDeadline"`, `Weight = clamp(round(remaining.TotalDays), 0, 30)`, `Severity` theo `D7`, `TaskIds` ≤ `MaxEvidenceIdsPerSignal` (sắp `MinutesRemaining` tăng dần rồi `TaskId`), `UserIds` = assignee (distinct, sorted). `Summary` tiếng Việt.

### 3.2 `ProjectHealthScore` (`D6`, `D8`)

| File | Nội dung |
|---|---|
| `Board/Services/ProjectHealth.cs` **(mới)** | **HÀM THUẦN**. `record ProjectHealthInput(int TotalTasks, int OpenTasks, int OverdueTasks, int AtRiskTasks, int StalledTasks, int OldestOpenTaskAgeDays, int MaxOpenTasksPerAssignee, int AssigneeCount)`; `record ProjectHealthResult(int Score, string Band, IReadOnlyDictionary<string,double> Components, IReadOnlyList<string> Reasons)`; `record ProjectHealthThresholds(double AtRiskRemainingRatio = 0.20, int AtRiskStaleHours = 48, int AtRiskMinWindowDays = 2, int StalledDays = 7)`; `static ProjectHealthResult Compute(ProjectHealthInput, ProjectHealthThresholds? = null)` |
| `Board/DTOs/DashboardDtos.cs` **(sửa)** | `DashboardResponse` += **field CUỐI** `DashboardProjectHealth? Health` + `record DashboardProjectHealth(int Score, string Band, IReadOnlyDictionary<string,double> Components, IReadOnlyList<string> Reasons)` |
| `Board/Services/DashboardService.cs` **(sửa)** | Trong `GetAsync` (đã có `tasks`/`columns`/`now`) tính `ProjectHealthInput` rồi gọi `ProjectHealth.Compute`; gán vào field cuối. **KHÔNG** truy vấn mới |

**Công thức chốt (`D8`) — tất định, không chia 0:**

```
ratios: overdue/atRisk/stalled = <count> / OpenTasks            (OpenTasks = 0 ⇒ 0)
P_overdue = 40 × overdueRatio
P_atRisk  = 20 × atRiskRatio
P_stalled = 10 × stalledRatio
P_aging   = 15 × clamp(OldestOpenTaskAgeDays / 30, 0, 1)
P_load    = 15 × clamp((MaxOpenTasksPerAssignee − 1) / 4, 0, 1)   (AssigneeCount = 0 ⇒ 0)
Score     = clamp(round(100 − P_overdue − P_atRisk − P_stalled − P_aging − P_load), 0, 100)
Band      = 'Tốt' (≥80) | 'Cần chú ý' (60–79) | 'Rủi ro' (40–59) | 'Nghiêm trọng' (<40)
Components = { overdue, atRisk, stalled, aging, load }
```

> **⬆️ Hiệu chỉnh trọng số so với bản nháp kế hoạch 40/15/10/10/10 (= 85):** 5 trần **phải** cộng lại đúng
> **100**, nếu không thì **không workspace nào chạm được 0** và nửa dưới của thang điểm trở nên bất khả
> (mọi ngưỡng `band` phía dưới mất nghĩa). Đã khoá bằng test bất biến `HEALTH4b`:
> `40 + 20 + 10 + 15 + 15 = 100`.
>
> **`aging` và `load` được gate theo `OpenTasks > 0`** (test `HEALTH5`/`HEALTH5b`): hai khoản này **không
> có mẫu số**, nên nếu để mặc định chúng vẫn trừ điểm khi workspace **không có việc nào đang mở** — tức
> một cặp số đếm mâu thuẫn vẫn làm hỏng điểm. Gate tường minh giữ đúng ngữ nghĩa *"không có việc đang mở
> ⇒ không có gì để chậm"*.

`AtRiskTasks` **dùng lại ĐÚNG** định nghĩa `D7` ⇒ gauge và cảnh báo **không thể** nói hai số khác nhau.
`StalledTasks` = `!IsDone` **và** `now − UpdatedAt > StalledDays ngày`.

---

## 4. §C — Backend: AI Board Template

| File | Nội dung |
|---|---|
| `Ai/DTOs/BoardTemplateDtos.cs` **(mới)** | `BoardTemplateRequest(string Description)`; `BoardTemplateColumnProposal(string Name, bool IsDone)`; `BoardTemplateTaskProposal(string Title, string? Description, string? Priority, string ColumnName, IReadOnlyList<SmartSetupLabelSuggestion> Labels, SmartSetupAssigneeSuggestion? Assignee)`; `BoardTemplateProposal(string? Summary, string BoardName, string? BoardDescription, IReadOnlyList<BoardTemplateColumnProposal> Columns, IReadOnlyList<BoardTemplateTaskProposal> Tasks)`; `ConfirmBoardTemplateRequest(BoardTemplateProposal Proposal)` |
| `Ai/Contracts/BoardTemplateAiModels.cs` **(mới)** | `AiBoardTemplateOutput { Summary, BoardName, BoardDescription, Columns[], Tasks[] }` — mọi field optional (**không** tin AI) |
| `Ai/Services/BoardTemplatePrompts.cs` **(mới)** | System + user + retry prompt; **trích** `CompleteWithJsonRetryAsync` của `SmartSetupService` thành helper static `AiJsonCompletion.CompleteWithRetryAsync(...)` (**không** copy-paste) |
| `Ai/Services/BoardTemplateValidator.cs` **(mới)** | **HÀM THUẦN** `Normalize(AiBoardTemplateOutput, members, labels, maxTasks) → BoardTemplateProposal`: cắt cột 2–6 (tên ≤ 80, dedupe case-insensitive, hậu tố ` (2)`), **pin đúng 1 cột `IsDone`** (**D13**), tên board ≤ 120 (fallback), task 5..`maxTasks`, `ColumnName` không khớp ⇒ cột **đầu tiên** không phải done; tái dùng `SmartSetupService.NormalizePriority/ResolveLabels/ResolveAssignee` |
| `Ai/Services/BoardTemplateService.cs` **(mới)** | `IBoardTemplateService.GenerateAsync(workspaceId, request, userId, ct)` (Manager+, 404 workspace, 400 description 1..4000) + `ConfirmAsync(...) → AiActionLogResponse` (`entity_type = 'Workspace'`, action `CreateBoardFromTemplate`, `basis` = tóm tắt input, `after_snapshot` = proposal đã cap) |
| `Ai/Services/Appliers/CreateBoardFromTemplateApplier.cs` **(mới)** | `ApplyAsync`: `after_snapshot` ⇒ `BoardService.CreateBoardAsync` ⇒ **giữ map tên→columnId** ⇒ `CreateColumnAsync` theo thứ tự ⇒ `CreateTaskAsync` từng task (**tác giả = `ctx.ActingUserId`**) ⇒ `AiActionAppliedResult(EntityType: Workspace, EntityId: workspaceId, …, CreatedColumnIds: […])` (**P3**). `UndoAsync`: `BoardService.DeleteBoardAsync` (**soft**, **D14**) + warning nếu board đã bị xoá trước đó |
| `Ai/Services/IAiActionApplier.cs` **(sửa)** | `AiActionContext.BoardId` ⇒ `Guid?` + `RequireBoardId()` — **P1** |
| `Ai/Services/AiActionService.cs` **(sửa)** | nhánh `entity_type = 'Workspace'` trong `ResolveAsync` (**P2**); `BuildAppliedSnapshotJson` += `createdColumnIds` (**P3**); `AiActionTypes` += `CreateBoardFromTemplate`, `AiEntityTypes` += `Workspace` |
| `Ai/Services/Appliers/CreateSubtasksApplier.cs` + `Agent/Agents/SearchSystemDataTool.cs` **(sửa)** | 10 call site `ctx.BoardId` ⇒ `ctx.RequireBoardId()` (**P1**) |
| `Ai/Endpoints/BoardTemplateEndpoints.cs` **(mới)** | `POST /api/workspaces/{workspaceId:guid}/smart-setup/template` (Manager+, antiforgery, không ghi gì) · `POST .../smart-setup/template/confirm` ⇒ **201** |
| `Ai/AiModule.cs` **(sửa)** | `AddScoped<IBoardTemplateService, BoardTemplateService>()` + `AddScoped<IAiActionApplier, CreateBoardFromTemplateApplier>()` + `MapAiModuleEndpoints` += `MapBoardTemplateEndpoints()` |

**Luật chốt:** generate **không bao giờ** ghi DB (giống Smart Setup Giai đoạn 3). Confirm chỉ tạo log `Pending`.
Approve mới thật sự tạo board/cột/task. Undo = soft-delete board. `MaxTaskCount` vẫn là trần cứng.
**Không** action nào tạo **hai** board trong cùng một lượt.

---

## 5. §D — Guardrail & giới hạn token cho mọi tính năng mới

| Guardrail | Cơ chế | Bằng chứng |
|---|---|---|
| Chat: số lượt/ký tự lịch sử | `AiChatOptions.MaxHistoryMessages/MaxHistoryChars` ⇒ **400 trước khi gọi provider** | `CHAT-8/9`: **không** có khung `meta`, provider call count = 0 |
| Chat: token ra | `AiChatOptions.MaxOutputTokens` truyền vào `AiStreamRequest` | assert `LastStreamRequest.MaxTokens` |
| Chat: độ dài thứ lưu được | `MaxAnswerChars = 2000` ⇒ **400** | `CHAT-SAVE-3` |
| Template: token ra + số task | `DeepSeekOptions.MaxTokens` + `MaxTaskCount` + pin cột 2–6 | `BT-GEN-*`, `BT-CONFIRM-*` |
| Observer: số tín hiệu | `MaxSignalsPerWorkspace` (20) + **đếm `signalsDroppedBeforePrompt`** (**P4**) + ưu tiên khi cắt (**P6**) | `RISK-*` |
| Observer: prompt size | `MaxPromptCharacters` (giữ nguyên 12000) | test pure `ObserverSummarizer` thêm case có `AtRiskDeadline` |
| Tắt được tính năng | `AiChat:Enabled = false` ⇒ **503** (tiền lệ `AgentDisabledException`) | `CHAT-11` |
| Tắt được tín hiệu mới | `Observer:AtRiskDeadlineEnabled = false` ⇒ không có signal đó | `RISK-6` |
| Cấu hình | `appsettings.json` += `"AiChat"` + 4 khoá `Observer:AtRiskDeadline*`; `TeamNexusApiFactory` ép **tường minh** | §6 |

---

## 6. §5 — DoD, CI & tài liệu

### 6.1 Con số mục tiêu

| Chỉ số | Baseline **đo được** | Kết quả **ĐO THẬT** |
|---|---|---|
| Backend `dotnet test` (**DB thật**, `Skipped: 0`) | **480** | ✅ **584** (Failed **0**, Skipped **0**) ⇒ **+104** |
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 / 0 | ✅ **0 / 0** |
| `dotnet ef migrations list` | **10** | ✅ **10** (**không đổi — D1**) |
| `has-pending-model-changes` | sạch | ✅ sạch |
| `git diff --stat` trên `Persistence/Migrations/` | — | ✅ **không file nào** bị thêm/sửa |
| Frontend `npm test` | **508** / 86 file | ⬜ **antigravity** (chưa bắt đầu) |

**Phân bổ backend mới (ĐO THẬT, từng suite):**

| Suite | Số test | Nội dung |
|---|---|---|
| `Pure/ObserverRiskTests` | **21** | luật `AtRiskDeadline`: biên 20%, sàn cửa sổ, cổng 48 h, comment "đánh thức", severity 24/72 h, weight kẹp, thứ tự tất định |
| `Pure/ProjectHealthTests` | **23** | công thức sức khỏe: band, trần từng khoản, gate `aging`/`load`, 512 tổ hợp sinh tất định (không `NaN`) |
| `Pure/AiGuardrailTests` | **18** | **bổ sung khi rà §5**: clamp `AiChatOptions`/`ObserverOptions`, ưu tiên cắt prompt, trần cột/task của template |
| `Integration/AiTaskChatApiTests` | **18** | SSE contract, 401/403/404/400/503, lỗi giữa luồng ⇒ khung `error`, đường lưu bình luận qua Accountability |
| `Integration/BoardTemplateApiTests` | **18** | generate không ghi gì, confirm `Pending`, approve tạo board/cột/task, undo soft-delete, hồi quy Phase 4 |
| `Integration/DashboardApiTests` | **14 → 18** | **+4**: `health` đúng công thức, hồi quy 9 field cũ, `health` là field cuối |
| `Pure/ObserverVocabularyTests` | **10 → 11** | **+1** và **SỬA 1 test cũ** (4 → 5 type) — ngoại lệ duy nhất được phép, xem **K** |
| *(hồi quy)* | **480** | **không** suite nào khác bị sửa |

### 6.2 ⚙️ CI phải nâng (làm **SAU CÙNG**, khi số thật đã đo)

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | `if ($total -ne 480)` ⇒ `-ne <số thật>`; cập nhật comment chuỗi `… 423 (Giai đoạn 12) → 480 (Giai đoạn 13) → <N> (Giai đoạn 14)`. Giữ `$skipped -gt 0` ⇒ fail |
| 2 | `.github/workflows/ci-web.yml` | **Chỉ** nâng nếu FE đã xong. FE Giai đoạn 14 **chưa** xong ⇒ **giữ `-lt 508`** và ghi rõ trong báo cáo là **cổng FE chưa nâng** (đúng trạng thái bàn giao) |

### 6.3 Tài liệu phải cập nhật

| # | File | Việc |
|---|---|---|
| 1 | `Project-Documents/tasks/phase-14-ai-advanced.md` | **Tài liệu này** — điền **số thật** vào §6.1 sau khi đo |
| 2 | `Project-Documents/03-roadmap.md` | Giai đoạn 14: 🔄 (**backend ✅ · frontend 📤 bàn giao**), tick 4 ô backend, **ghi tường minh lệch DoD D6**, link 2 tài liệu |
| 3 | `Project-Documents/01-system-specification.md` | += **§13 Giai đoạn 14**: hợp đồng SSE, guard `AiChat`, "lưu thành bình luận ⇒ Pending ⇒ Approve" (D11), định nghĩa `AtRiskDeadline` + severity, công thức sức khỏe + `band`, board template (1 board / 2–6 cột / 5–10 task / pin 1 cột done / Undo = soft-delete) |
| 4 | `Project-Documents/04-database-design.md` | §6: **khẳng định KHÔNG có migration mới** (vẫn **10**) **kèm lý do** (D1); §7: +4 gạch đầu dòng (lý do không cần cột/bảng/index mới) |
| 5 | `README.md` | += `## Trạng thái (Giai đoạn 14)`; xác nhận Migration vẫn là **10** |
| 6 | `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` | += `## Phase 14` (port streaming, guard `AiChat`, `AtRiskDeadline`, board template, bảng route mới, hạn chế đã biết) |
| 7 | `Project-Documents/report/phase-14-ai-advanced-test-report.md` | **Tạo khi kết thúc** (theo mẫu `report/phase-13-*.md`) |

### 6.4 Điều kiện "xong" (phần backend)
Cả **4 ô** (A chat · B risk + health · C board template · D guardrail) + **8 hạng mục phát sinh** (E–L)
đóng bằng bằng chứng §6.5; **vẫn 10 migration** và `has-pending-model-changes` **sạch**; **`Skipped: 0`** ở backend;
**0 warning / 0 error**; cổng `ci-backend.yml` đã nâng bằng **số thật**; `03-roadmap.md` + `README.md` +
`01-system-specification.md` + `04-database-design.md` + `Ai/README.md` ghi **số thật**; báo cáo đã tạo.

### 6.5 Bảng bằng chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 warning / 0 error | ✅ **0 / 0** |
| 2 | `dotnet ef migrations list` + `has-pending-model-changes` | **10** (không đổi) / sạch | ✅ **10** / sạch |
| 3 | `git diff --stat` | **không** file nào trong `Persistence/Migrations/` bị thêm/sửa | ✅ không file nào |
| 4 | `dotnet test` với `TEAMNEXUS_TEST_DB` (§1) | `Skipped: 0`, `Failed: 0`, `Total = 584` | ✅ **584 / 0 / 0** |
| 5 | **1 lượt kiểm thử tay** (Scalar UI `http://localhost:5000/scalar`): (a) `POST .../ai-chat/stream` ⇒ thấy `meta`/`delta`/`done` (Fake provider, 0 token); (b) `POST .../ai-chat/message` ⇒ `Pending`; `POST /api/ai-actions/{id}/approve` ⇒ bình luận xuất hiện; (c) `POST .../observer/scan` trên workspace có task sắp hết hạn + 49 h không update ⇒ `AtRiskDeadline` trong run detail; (d) `GET .../dashboard` ⇒ `health.score`/`band` khớp thủ công; (e) `POST .../smart-setup/template` ⇒ 2–6 cột + 5–10 task; confirm + approve ⇒ board mới đúng cột/task; undo ⇒ board biến mất, số liệu không còn | — | ⬜ **chưa chạy** (xem báo cáo §"việc còn lại") |
| 6 | Cổng CI backend nâng | `total = 584`, `skipped = 0` | ✅ `ci-backend.yml` = **584** |
| 7 | **Hồi quy bắt buộc**: `AccountabilityApiTests` + `AgentAndReportingApiTests` + `DashboardApiTests` + `KanbanApiTests` + `ObserverVocabularyTests` xanh | — | ✅ tất cả xanh trong lượt 584 |
| 8 | **Flake đã biết**: `AgentRun_CancelStopsTheRunAndRecordsCancelled` chạy lại riêng **phải** xanh | — | ⚠️ **tái hiện 1/3 lượt chạy đầy đủ**; **mỗi lần chạy riêng đều PASS** ⇒ flake có sẵn, **không** do Giai đoạn 14. Số test không đổi (584) ⇒ không mất/skip test nào |

---

## 7. Ca biên & chế độ lỗi (bắt buộc xử lý)

| Ca | Hành vi chốt |
|---|---|
| Chat: `messages` vượt `MaxHistoryMessages`/`MaxHistoryChars` | **400** `{error}` tiếng Việt; **0** token |
| Chat: lỗi provider **trước** khi mở stream | **502** (JSON, nhờ `DomainExceptionFilter`) |
| Chat: lỗi provider **sau** khi mở stream | `event: error` + đóng stream; **không** khung `done`; FE hiện `Alert`, **giữ** phần đã stream |
| Chat: client `abort()` | Server thấy `ct` cancel ⇒ dừng `await foreach`, **không** log lỗi; FE **không** unhandled rejection |
| Chat: `AiChat:Enabled = false` | **503** trước khi chạm DB/provider |
| Chat: người ngoài workspace | **404** (**không** 403 — không tiết lộ workspace tồn tại) |
| Chat: task soft-deleted | **404** (query filter) |
| Chat: thiếu CSRF | **403** |
| Chat: câu trả lời > 2000 ký tự khi lưu | Nút **disable** (FE) **và** **400** (server — không tin client) |
| Chat: `PostComment` Pending chưa duyệt | `task_comments`/`notifications`/`activity_logs` **không** tăng |
| Chat: Approve **hai lần** | **409** (CAS Giai đoạn 4) |
| AtRisk: còn **đúng** 20% | **Không** bắn (biên `<`, không `<=`) |
| AtRisk: cửa sổ < 2 ngày | **Không** bắn (sàn chống nhiễu) |
| AtRisk: task đã quá hạn | **Chỉ** `OverdueTask` (không báo trùng) |
| AtRisk: `dueDate = null` hoặc cột `is_done` | **Không** xét |
| AtRisk: comment mới hơn `updated_at` | "Đánh thức" task ⇒ **không** bắn |
| AtRisk: workspace lớn vượt `MaxSignalsPerWorkspace`/`MaxPromptCharacters` | Ưu tiên `severity → weight`; **đếm** `signalsDroppedBeforePrompt` vào run summary ⇒ nhìn thấy được, **không** im lặng |
| Health: workspace **rỗng** | `Score = 100`, `Band = "Tốt"`, components 0 — **không** `NaN`/chia 0 |
| Health: mọi task quá hạn | `Score` kẹp tại **0**, không âm |
| Health: `AssigneeCount = 0` | `P_load = 0` |
| Health: `health = null` phía FE | `Empty` tiếng Việt (không hiện 0 điểm) |
| Template: AI trả **0/1 cột** | **400** server-side (không tin client) |
| Template: 2 cột cùng tên | Dedupe case-insensitive + hậu tố ` (2)` |
| Template: AI **không** đánh dấu `isDone` | **Pin cột cuối** = `is_done` (**D13**) |
| Template: `columnName` không tồn tại | Gán cột **đầu tiên** không phải done (**không** bỏ task) |
| Template: confirm **hai lần** | **409** |
| Template: Undo | Board **soft-delete**; board đã bị xoá tay ⇒ **warning**, **không** ném |
| Template: `DeepSeek:ApiKey` rỗng | `FakeAiProvider` cho proposal mẫu hợp lệ ⇒ CI/harness verify **toàn bộ** flow với 0 token |
| Mọi lỗi 401/403/404/409/503 ở endpoint/trang mới | `message.error`/`Alert`/`Result` **tiếng Việt** — **không** màn hình trắng |
| Hồi quy | `TaskResponse`/`BoardResponse`/`ColumnResponse`/`CommentResponse`/`NotificationResponse`/`ReportSummaryResponse`/payload SignalR **không** đổi; `DashboardResponse` chỉ **append cuối** |

---

## 8. ⛔ KHÔNG được làm

- ❌ **Không** thêm migration/bảng/cột/index nào. `dotnet ef migrations list` **phải là 10**. Không có ngoại lệ ở giai đoạn này (khác Giai đoạn 13 — ở đây **không** có lệch DoD về schema).
- ❌ **Không** thêm method vào `IAiProvider`/`IAiToolCallingProvider` (dùng port **mới** `IAiStreamingProvider` — D10).
- ❌ **Không** dùng `EventSource` cho chat (không gửi được `X-XSRF-TOKEN`, không POST được — D3).
- ❌ **Không** "buffer toàn bộ rồi mới trả" trong `AiChatService` — phải `yield` từng khung; nếu không thì SSE chỉ là hình thức.
- ❌ **Không** tự động tạo `Pending` cho **mỗi** lượt chat (D11) — chỉ khi người dùng bấm "Lưu thành bình luận".
- ❌ **Không** để chat/template ghi **bất kỳ** dữ liệu nghiệp vụ nào khi chưa `Approved`.
- ❌ **Không** đặt `ProjectHealthScore` trong module **Ai** (D6) và **không** để module **Board** tham chiếu **Ai** (bất biến `Ai → Board` một chiều).
- ❌ **Không** thêm `AtRiskDeadline` vào `NotificationVocabulary.AgentTypes`/`MemberTypes`; nó là tín hiệu **Observer** (đi cùng `NotificationTypes.All`).
- ❌ **Không** bỏ qua **P1/P2/P3** (nullable `BoardId`, nhánh `Workspace`, `createdColumnIds`) — thiếu một trong ba là flow template **hỏng ở Undo** hoặc **400**.
- ❌ **Không** sửa shape hợp đồng đã verify (`DashboardResponse` chỉ **append cuối**; `AiActionAppliedResult` thêm field **optional**).
- ❌ **Không** xoá/hard-delete board khi Undo (D14 — soft-delete).
- ❌ **Không** dùng `DateTime.Now`/`DateTimeOffset.UtcNow` trong `AiRiskCandidate.Detect`/`ProjectHealth.Compute` (phải nhận `now`/input từ tham số) ⇒ nếu không, test biên sẽ flaky theo đồng hồ.
- ❌ **Không** dùng `Enum.TryParse` trần cho priority (bẫy BUG-1 Giai đoạn 10) và **không** quên `ToUtc(...)` cho mọi `DateTimeOffset` (bẫy Npgsql Giai đoạn 10).
- ❌ **Không** thêm package NuGet (backend) hay thư viện npm (frontend). Gauge dùng `Progress`/`Statistic`/`Tooltip` antd; chat dùng `fetch` + `TextDecoder` có sẵn của trình duyệt.
- ❌ **Không** dùng `dangerouslySetInnerHTML` cho câu trả lời AI.
- ❌ **Không** viết lại test cũ đang xanh — **ngoại lệ duy nhất được phép**: `ObserverVocabularyTests.NotificationTypes_ExposeExactlyTheFourDetectorSignals` (**K**, 4 → 5 type), phải ghi tên test + lý do vào báo cáo.
- ❌ **Không** "sửa" `AgentRun_CancelStopsTheRunAndRecordsCancelled` khi nó đỏ do flake có sẵn — chạy lại riêng, ghi nhận, **không** đụng test Giai đoạn 7.
- ❌ **Không** mở rộng phạm vi sang: Slack/Discord/GitHub (Giai đoạn 15), mobile/Flutter (Giai đoạn 16), lưu transcript chat ở DB, chat nhiều task cùng lúc, chat có function-calling/tool, health score **lịch sử**, board template tạo **nhiều** board, template tự thêm member, digest tuần, real-time cho chat, iCal export, hard delete.
- ❌ **Không** thực thi bất kỳ việc **frontend** nào trong phiên này — toàn bộ phần FE chỉ **mô tả** trong note bàn giao.

---

## 9. Thứ tự thi hành đề xuất

1. **§1** — chốt baseline (`dotnet build` 0/0, `dotnet test` **480** + ghi lại flake, `migrations list` = **10**, `has-pending-model-changes` sạch). **Tạo 2 tài liệu Giai đoạn 14** để antigravity bắt đầu **song song**.
2. **§3.2 (D8)** — `ProjectHealth.cs` + `ProjectHealthTests` (thuần, nhanh) ⇒ `+12`.
3. **§3.2 (D6/D15)** — append `health` vào `DashboardResponse` + `DashboardService` + `DashboardApiTests` **+4** ⇒ `+16`.
4. **§3.1 (D7/D16, P4–P6)** — `AtRiskDeadline` + `ObserverRiskTests` **+20** + sửa `ObserverVocabularyTests` (**K**) + prompts/summarizer/service ⇒ `+36`.
5. **§2.1 (D10, P7)** — `IAiStreamingProvider` + `DeepSeek`/`Fake`/`ScriptedAiProvider` + `AiChatOptions` + `AiModule` + `appsettings` ⇒ build 0/0, **mọi test cũ vẫn xanh** (mốc an toàn quan trọng nhất).
6. **§2.2 (D3/D4/D5)** — `AiChatSseWriter` + `AiChatService` + `AiChatEndpoints` + `AiTaskChatApiTests` **+18** ⇒ `+54`.
7. **§4 (D12/D13/D14, P1–P3)** — `BoardTemplate*` + `CreateBoardFromTemplateApplier` + nhánh `Workspace` + `BoardTemplateApiTests` **+18** ⇒ `+72`.
8. **§5** — rà lại **mọi** guardrail bằng bảng §5 (assert `LastStreamRequest.MaxTokens`, provider call count = 0 khi validate fail…).
9. **§6.2** — nâng `ci-backend.yml` bằng **số thật** (chạy `dotnet test` đầy đủ 2 lần để loại flake).
10. **§6.3** — cập nhật `03-roadmap.md`, `01-system-specification.md`, `04-database-design.md`, `README.md`, `Ai/README.md`; viết báo cáo + chép bằng chứng §6.5.

---

## 10. Rủi ro & giả định

| # | Mục | Xử lý |
|---|---|---|
| **R1** | **Lệch DoD #1 (có chủ ý):** roadmap viết `ProjectHealthScore` thêm vào `ObserverSignalDetector`, nhưng đặt ở đó sẽ khiến dashboard **Member+** không đọc được (hoặc phải mở quyền Observer) và ép `Board → Ai` (**bị cấm**) | **D6**: đặt tại `Board/Services/ProjectHealth.cs` (**hàm thuần**), **append** vào `DashboardResponse`; `AtRiskDeadline` **vẫn** trong `ObserverSignalDetector` (đúng roadmap). **Bắt buộc** ghi rõ ở `03-roadmap.md` §6.3(#2) + báo cáo + §1 |
| **R2** | **Lệch DoD #2:** roadmap ghi *"cần endpoint mới"* ⇒ có; nhưng endpoint board-template nằm **ngoài** họ `/api/boards/...` (cấp workspace) | Không tránh được (**D12**): Smart Setup cũ **bắt buộc** có `boardId`, mà đề xuất **cấu trúc** board thì chưa có board. Giảm thiểu: giữ trong **cùng họ route** `.../smart-setup/`, dùng lại `DomainExceptionFilter` + Manager+ |
| **R3** | **`TestServer` đệm response ⇒ không quan sát được "streaming thật"** | Test assert **thứ tự + số khung + `done.answer == Σ delta`** (thoả hợp đồng) và dùng `ResponseHeadersRead`; nếu vẫn đệm ⇒ ghi **hạn chế đã biết** vào báo cáo + `Ai/README.md` (**không** bỏ test, **không** tuyên bố sai) |
| **R4** | **`AtRiskDeadline` làm tăng số tín hiệu ⇒ có thể vượt `MaxSignalsPerWorkspace` (20)** ở workspace lớn | **P4** (đếm `signalsDroppedBeforePrompt`) + **P6** (ưu tiên `severity → weight` khi cắt) + **D16** (cờ `AtRiskDeadlineEnabled`). **KHÔNG** nâng trần `MaxSignalsPerWorkspace` (nâng trần = tăng token **mọi** workspace) |
| **R5** | **Chat không lưu transcript ở DB** ⇒ đóng tab là mất hội thoại | **Cố ý** (**D2**): tránh 1 bảng mới + retention + migration. Giảm thiểu: FE **giữ transcript khi đóng/mở lại tab**; muốn giữ lâu ⇒ bấm **"Lưu thành bình luận"**. Nợ kỹ thuật ghi ở §6.3(#4) |
| **R6** | **`AiActionContext.BoardId` → `Guid?` chạm 10 call site** | **P1** + test hồi quy bắt buộc `AccountabilityApiTests` + `AgentAndReportingApiTests` (§6.5 #7). `RequireBoardId()` ném **rõ ràng** ⇒ hành vi sai lộ ngay, không âm thầm dùng `Guid.Empty` |
| **R7** | **Undo board template xoá mất công việc người dùng đã thêm vào board đó** | **D14** chỉ **soft-delete** (khôi phục được bằng DB); kèm **cảnh báo** trong `warnings` của `applied_snapshot`; FE hiển thị `Popconfirm` **rõ ràng** ở nút Undo (ghi trong note bàn giao) |
| **R8** | **`FakeAiProvider` phải implement port thứ ba** ⇒ rủi ro "sửa fake làm đỏ suite khác" | `StreamAsync` là **method mới**, nhánh `CompleteAsync`/`ChatAsync` **không** chạm ⇒ bước 5 của §9 chạy `dotnet test` **đầy đủ** trước khi viết tính năng (mốc an toàn) |
| **R9** | **Số test thật lệch ước tính** (~+72) | **Số đo thắng tài liệu**: cập nhật §6.1 + cổng CI §6.2 + báo cáo |
| **R10** | **Flake có sẵn** `AgentRun_CancelStopsTheRunAndRecordsCancelled` (409 thay vì 200) | Đã ghi ở §1 + §6.5(#8): chạy riêng để xác nhận, **không** sửa test Giai đoạn 7, ghi vào báo cáo là **flake tồn tại trước** |
| **R11** | Cổng `ci-web.yml` ghi `508` là **baseline của Giai đoạn 13**; FE Giai đoạn 14 **chưa** xong ⇒ nếu nâng sớm sẽ đỏ vô cớ | **Không** nâng ở phiên này; ghi rõ trong note bàn giao là **antigravity** phải nâng bằng số thật **sau khi** FE xong |

**Giả định:**
- Baseline **backend 480** / **frontend 508 (86 file)** / **migration 10**; PostgreSQL thật ở `localhost:5432` (user `postgres`) ⇒ **`Skipped: 0`**. Docker **không** cần.
- Giai đoạn 7–13 đã merge; dùng lại được: `IWorkspaceAccess`, `DomainExceptionFilter`, `AntiforgeryValidationEndpointFilter`, `IAiActionService`/`IAiActionApplier` (Giai đoạn 4/7), `ICommentService`, `IBoardService`/`IColumnService`/`ITaskService`, `IActivityLogWriter`, `NotificationTypes`/`ObserverFindingValidator`, `SmartSetupService.NormalizePriority/ResolveLabels/ResolveAssignee`, `ObserverSignalDetector.ActivityAt`, `ReportAggregator.IsDone`, `TestScenario`/`TeamNexusApiFactory`/`ScriptedAiProvider`/`FixedTimeProvider`.
- `Program.cs` giữ nguyên thứ tự `AddBoardModule()` (36) → `AddAiModule()` (39) → `AddReportingModule()` (43) và `MapAiModuleEndpoints()` (99) ⇒ mọi đăng ký mới nằm trong `AddAiModule`/`MapAiModuleEndpoints`, `Program.cs` **không** đổi.
- Frontend: React 19 + TS 6 + antd **6.6.3** (`Progress`/`Statistic`/`Tooltip`/`Empty`/`Alert`/`Modal`/`Radio`/`Table`/`List` đã có) + Vite 8 + Vitest 5 + `dayjs`; `fetch`/`ReadableStream`/`TextDecoder`/`AbortController` là API trình duyệt, **không** cần package.
- Toàn bộ nhãn UI mới **tiếng Việt có dấu**. Endpoint mới là **append-only**; Flutter (Giai đoạn 16) dùng lại được `.../ai-chat/stream`, `.../dashboard` (`health`), `.../smart-setup/template`.
