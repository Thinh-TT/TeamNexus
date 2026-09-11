# Giai đoạn 7 – AI Agent Executor

> **Mục tiêu:** nâng AI từ vai trò **đề xuất/quan sát** (giai đoạn 3–5) lên vai trò **thực thi thật**. AI Agent trở thành một
> **thành viên ảo** của workspace, được gán task bằng đúng thao tác assign/đổi người thực hiện sẵn có, tự chạy một **vòng lặp
> tool-calling** (truy vấn dữ liệu nội bộ, web search qua Tavily, soạn kết quả, hỏi lại khi thiếu thông tin), và mọi kết quả
> **ghi dữ liệu thật** đều đi qua **Accountability Layer** đã có (Pending → Approve/Reject/Undo) — không xây cơ chế giải trình riêng.
>
> **Công nghệ:** ASP.NET Core (.NET 10) Modular Monolith · EF Core (Npgsql, `jsonb`, `bytea`) · DeepSeek function-calling
> (OpenAI-compatible `tools`/`tool_calls`) · Tavily Search API · PostgreSQL advisory lock · SignalR (`BoardHub`) ·
> Minimal API + endpoint filters (CSRF/domain-error) · React + TypeScript + Vite + Ant Design · Vitest.
>
> **Tham chiếu:** `03-roadmap.md` (Giai đoạn 7 — đang làm, gồm 2 hạng mục bắt buộc phát sinh) ·
> `02-tech-stack-decisions.md` §2.8 & §5 · `01-system-specification.md` §7 · `04-database-design.md` §3.3, §3.4, §3.5, §3.8, §4, §5, §7
> · contract bàn giao ở `tasks/phase-4-accountability-layer.md` §6 và `tasks/phase-5-ai-observer.md` §8 ·
> `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`.

---

## 0. Tiền đề & quyết định kiến trúc (đã chốt)

Đọc hết mục này trước khi code — các quyết định dưới đây đã chốt, **không chọn lại**.

### 0.1 Trạng thái đầu vào (đã kiểm tra thực tế trong repo)

| # | Sự thật đã kiểm chứng | Hệ quả cho giai đoạn 9 |
|---|---|---|
| S1 | **Frontend không có chỗ đổi assignee.** `frontend/src/features/board/components/TaskDetailModal.tsx` dòng 609–630 **chỉ hiển thị** `task.assigneeName`; dòng 153 luôn gửi lại `assigneeId: task.assigneeId`. `KanbanColumn.tsx` quick-add chỉ có `{ columnId, title }`. Grep `getMembers\|membersApi\|workspaceMembers` trong `features/board` → **0 kết quả** | Ô roadmap "gán được task qua đúng UI assignee hiện có" **không thể hoàn thành** nếu không thêm dropdown. Đây là **§5 (frontend A)**, không phải hạng mục phụ |
| S2 | `GET /api/workspaces/{workspaceId}/members` đã có (`MembersEndpoints.cs`), trả `WorkspaceMemberResponse(UserId, DisplayName, Role, AvatarUrl)`, quyền Member+ | Đúng nguồn dữ liệu cho dropdown — chỉ cần thêm `MemberType` |
| S3 | `AiActionService.ResolveContextAsync` (`AiActionService.cs` dòng 320–336) **throw 400** nếu `entity_type != 'Board'` | 2 applier mới không thể dùng `entity_type = 'Task'` nếu không mở rộng resolver — **§4.6** |
| S4 | `AiActionService.RequestCreateSubtasksAsync` chạy `RequireManagerAsync` ⇒ agent (role `Member`, `member_type = 'ai_agent'`) **bị 403** | Cần API ghi log **nội bộ, không qua HTTP** cho agent — **§4.7** |
| S5 | `ai_observer_runs.status = Skipped` là **trạng thái không phải lỗi** và không sinh notification (Phase 5 §0) | Tiền lệ để tách `agent_runs.status` (vòng đời) khỏi `agent_runs.stop_reason` (nguyên nhân) — **D4** |
| S6 | `DeepSeekAiProvider` gửi cố định `[system, user]`, `AiCompletionResult` **không** có tool-call | Phải **mở rộng** contract provider, **không** sửa `CompleteAsync` (Phase 3/5 không được đổi) — **§4.2** |
| S7 | `TaskService.CreateTaskAsync`/`UpdateTaskAsync` chỉ kiểm `_db.Users.AnyAsync(u => u.Id == AssigneeId)` — **không** kiểm workspace membership | Siết lại thành kiểm `workspace_members` — **§3.2**; nếu không, agent là lỗ hổng mới |
| S8 | `task_comments.author_id` là `Guid` **NOT NULL**, FK → `users`; `CommentResponse` resolve `Author.DisplayName` | Agent **phải** là row `users` thật ⇒ xác nhận **D1** |
| S9 | **Không có** `task_attachments`, không có blob/disk/`IFileStorage` ở đâu trong repo (grep `attachment\|upload\|IFileStorage\|blob` chỉ ra comment trong Reporting) | Bảng + storage mới **phải** thiết kế từ đầu — **§2.4** |
| S10 | `FakeAiProvider` nhận diện prompt bằng **marker ở đầu `UserPrompt`** (`ObserverPrompts.AgentMarker`) | Nhánh thứ 3 của fake phải theo đúng pattern để verify offline không tốn token — **§4.5** |
| S11 | `IBoardEventPublisher` là nơi broadcast **duy nhất**, publish **sau** commit, nuốt exception + log warning | Thêm 1 event method ở đây rẻ hơn tạo port mới; **không** cần sửa `BoardHub` constants (Phase 2 artifact) — **§3.3** |
| S12 | `BoardModule` đăng ký `IActivityLogWriter → NullActivityLogWriter`, module Ai ghi đè sau (vì `Program.cs` gọi `AddBoardModule` **trước** `AddAiModule`) | Cùng mẹo cho `IAiAgentResolver` của Board — **D7** |
| S13 | `WorkspaceMember` có composite PK `(workspace_id, user_id)`; `WorkspaceMemberConfiguration` có CHECK role + query filter theo workspace soft-delete | 1 user chỉ thuộc **1** workspace ⇒ agent là **1 `ApplicationUser` mỗi workspace** — **D1** |
| S14 | `IBoardColumnService.ColumnService.DeleteColumnAsync` từ chối xoá cột còn task (`ConflictException`) | Cột "Chờ làm rõ" còn phải chặn xoá **cả khi rỗng** — **§3.4** |
| S15 | `ICommentService.CreateCommentAsync(taskId, request, userId)` nhận `userId` tùy ý sau khi `RequireMemberAsync` | **Không** dùng được cho agent (agent là member nhưng route gọi sẽ là Manager). Agent ghi comment qua applier với `userId = agent_user_id` — **§4.6** |
| S16 | `ReportFileName.Slugify` đã có (slug ASCII, lọc `..`/`/`/`\`; kế hoạch ghi nhầm tên là `Normalize`) và đã verify ở Phase 6 | Tái dùng cho tên file attachment — **§4.4**, không viết lại |
| S17 | Phase 2–6 verify bằng **harness tạm ngoài workspace** + API thật + PostgreSQL thật, **không** tạo project xUnit (để Phase 8) | Giai đoạn 7 giữ nguyên cách làm — **§7** |
| S18 | Baseline frontend hiện tại: **30 test files / 155 tests PASS**, `oxlint` 0/0, `tsc -b` sạch | Mọi §5 phải kết thúc với baseline mới tốt hơn baseline này |

### 0.2 Bảng quyết định

| # | Quyết định | Lý do / ghi chú |
|---|---|---|
| **D1** | **1 user `AI_AGENT` lazy-created cho MỖI workspace** + `workspace_members.member_type` ∈ {`human`,`ai_agent`} + partial unique `(workspace_id) WHERE member_type='ai_agent'`. Row `users` của agent: `email = null`, `password_hash = null`, `lockout_enabled = true`, `two_factor_enabled = true`, **không** tạo `user_logins` | `tasks.assignee_id` và `task_comments.author_id` đều FK → `users` (S8) nên agent **buộc** phải là user thật. Composite PK `(workspace_id, user_id)` (S13) khiến 1 user chỉ thuộc 1 workspace ⇒ agent phải là **per-workspace**. Không dùng id string ở bất kỳ chỗ nào ⇒ không phải sửa `Guid` ở 3 module. Agent là "user ảo": không thể đăng nhập (an toàn), không hiện trong OAuth |
| **D2** | **Trigger thủ công**: `POST /api/tasks/{taskId}/agent-runs` trả **202 Accepted**, phần chạy nằm ở scope nền riêng. Nút **"Chạy lại"** tái dùng đúng đường đó (tạo run mới, `previous_run_id` trỏ run cũ) | Người dùng chốt. Xác định, không có cron tiêu token, retry chỉ là bấm nút. Cần 202 vì `RunTimeoutSeconds` = 300 > timeout HTTP của proxy — **không** chạy đồng bộ trong request |
| **D3** | **`board_columns.is_clarification`** — đối xứng với `is_done`, cột do agent **tạo lazy** (1/board, partial unique), **không** auto-seed cho board cũ, **không** cho xoá/đổi cờ | Cơ chế trạng thái Kanban là **cột**, không phải enum trên task (`04` §3.4). `is_done` là tiền lệ ⇒ không thêm enum `TaskStatus` (tránh 2 nguồn sự thật với `column_id`) |
| **D4** | `agent_runs.status` ∈ {`Running`,`AwaitingClarification`,`AwaitingApproval`,`Completed`,`Failed`} **tách khỏi** `stop_reason` ∈ {`DraftProduced`,`QuestionAsked`,`ToolLimit`,`TimeLimit`,`TokenBudget`,`ProviderError`,`Cancelled`,`TaskChanged`,`InternalError`} | `BudgetExceeded` **không** là status riêng — nó là `stop_reason` trên `status=Failed`, theo đúng tiền lệ `Skipped` (S5). Phải phân biệt được "hết ngân sách" với "lỗi provider" để báo Manager đúng việc cần làm |
| **D5** | **`task_attachments` + `content bytea`**, cap `Agent:MaxAttachmentBytes` (mặc định **512 KB**), **hard delete** khi Undo | Repo không có blob/disk storage nào (S9) và `04` §7 + `02` §3 chốt "generate on-demand, không lưu file lâu dài trên server" (S10). `bytea` giữ bất biến đó, trả file bằng đúng pattern `Results.File(byte[], …)` đã verify Phase 6, và xoá được **vật lý** ⇒ không phình quota free-tier. Đây là **ngoại lệ duy nhất không soft-delete** trong schema — ghi rõ trong `04` §7 |
| **D6** | Không tạo module mới. Entity ở `Persistence`, service/endpoint của agent nằm **trong module `Ai`** | Khớp `02` §1 (module `Ai` = Smart Setup + Accountability + Observer) và tránh làm `public` các helper `internal` của Ai (`AiEndpointHelpers`, `DomainExceptionFilter`, `ObserverSeverity`) |
| **D7** | Port `IAiAgentResolver` **khai báo trong module Board**, `NullAiAgentResolver` là default, **implementation thật ở module Ai**; resolve = "ai là agent của workspace này" | Cùng mẹo `IActivityLogWriter` (S12): Board **không** được tham chiếu Ai, mà `TaskService`/`ColumnService` lại cần biết agent. **Không** dùng hằng số `Guid` "well-known" cho agent |
| **D8** | Broadcast qua **`IBoardEventPublisher.AgentRunProgress`** (1 method + 1 hằng tên event), **không** sửa `BoardHub` | S11. Board đã có sẵn publisher, publish sau commit, fail-soft |
| **D9** | Kết quả ngắn → **comment**; dài/định dạng file → **attachment**; **cả hai** qua `ai_action_logs` với `entity_type='Task'`, `entity_id=taskId`, `requested_by_user_id = agent_user_id` | `02` §2.8. Đúng cột/index sẵn có `(entity_type, entity_id, created_at)` ⇒ **không** cần index mới cho `ai_action_logs` |
| **D10** | **Câu hỏi làm rõ không đi qua Accountability Layer** — agent đăng comment ngay (`author_id = agent_user_id`) + notification cho Manager | Đây là **câu hỏi**, không phải kết quả ghi dữ liệu nghiệp vụ; bắt Manager duyệt một câu hỏi thì mất tính "agent hỏi lại". Tiền lệ: Observer ghi `notifications` mà **không** qua `AiActionService` (Phase 5 §0) |
| **D11** | **Chống chạy chồng theo task**: `pg_try_advisory_lock(hash(taskId))` (tái dùng nguyên pattern `ObserverService.TryAcquireAdvisoryLockAsync`) + `SemaphoreSlim(1,1)` trong process. Không lấy được lock ⇒ **409** | S15 tiền lệ. 409 (không phải 202) vì người dùng vừa bấm nút ⇒ phải biết ngay là đang chạy |
| **D12** | **Guardrail** đọc từ config section `Agent`, kiểm **sau mỗi vòng** loop: `MaxToolCalls`=15 · `RunTimeoutSeconds`=300 (wall-clock qua `CancellationTokenSource.CancelAfter`) · `MaxRunTokens`=50 000 (cộng dồn mọi lượt) · `MaxRunLlmCalls`=20. Vượt ⇒ `Failed` + `stop_reason` tương ứng + **1** notification `AgentRunFailed` + `notification_sent=true` | Đúng 3 ngưỡng của `03-roadmap.md` + `02` §2.8. `notification_sent` để không spam mỗi lần đọc run |
| **D13** | **Reaper** (`AgentRunReaper`, `IHostedService`) lúc khởi động: mọi run `Running` có `started_at < now − RunTimeoutSeconds − OrphanRunGraceSeconds` ⇒ `Failed`/`InternalError` + `finished_at` | Free-tier sleep/recycle giữa run (S17) ⇒ nếu không có reaper, card Kanban kẹt "Đang chạy" vĩnh viễn. Dùng partial index `(status) WHERE status='Running'` |
| **D14** | **Bất biến append-only** của `agent_runs`: "Chạy lại" **tạo run mới**, **không** sửa run cũ | Audit không bị ghi đè; run cũ giữ `stop_reason=QuestionAsked` để giải trình được |
| **D15** | **KHÔNG** dùng Hangfire/Redis/queue. Chạy trong process, mất run khi app sleep là **chấp nhận được** trong giai đoạn này; reaper + nút "Chạy lại" là lưới an toàn | `02` §5 "ưu tiên giải pháp built-in, chỉ thêm hạ tầng khi thực sự cần" |
| **D16** | `FakeAiProvider` thêm **nhánh thứ 3** (marker `{"agent":"executor"}`) trả kịch bản tool-call **xác định**; `FakeWebSearchProvider` khi `Tavily:ApiKey` rỗng | S10. Verify end-to-end **offline, 0 token** — đúng cách Phase 3–5 đã làm |
| **D17** | **Không** tạo project xUnit (để Phase 8). Verify bằng harness tạm **ngoài workspace**, đã xoá sau khi chạy | S17 |
| **D18** | Mọi ngưỡng/cap nằm trong config section `Agent` (+ `Tavily`), **không** hard-code. Có `Agent:Enabled` (mặc định `true`) ⇒ `false` trả **503** | Demo/tinh chỉnh không cần build lại; công tắc tắt an toàn cho production (tiền lệ `Reports:Enabled` → 503) |
| **D19** | Mọi hàm quyết định thuần (kiểm guardrail, chọn Comment vs Attachment, cắt `tool_call_trace`, tên file) là **`public static`** | Điểm tựa verify không cần DB/AI (tiền lệ `ObserverSignalDetector`, `ReportAggregator`) và là test xUnit của Phase 8 sau này |
| **D20** | **KHÔNG** prune `task_attachments` tự động; prune `agent_runs` cũ hơn `Agent:RetentionDays` (90) là hạng mục **optional**, chỉ làm sau khi phần chính xong | File là nội dung do người dùng đã duyệt — không được tự xoá. Run cũ chỉ là audit ⇒ prune được |

### 0.3 Non-goals Giai đoạn 7 (ghi rõ để không over-scope)

Streaming token qua SignalR (chỉ broadcast **thay đổi trạng thái**); đa agent/workspace (đúng 1); agent tự tạo task/board;
agent tự chạy lại khi có comment mới (`02` §2.8 chốt: **thao tác thủ công**); agent trả lời trong comment thread (chỉ đăng câu hỏi 1 chiều);
email/push; memory/vector store/RAG; nhiều assignee per task; agent gọi tool ngoài whitelist 4 tool; upload file do **người** đính kèm
(chỉ file do agent sinh); sửa `ReportFileName` thành thư viện chung (chỉ gọi lại hàm sẵn có); agent đọc attachment trong `SearchSystemData`;
prune tự động; test xUnit; đa ngôn ngữ UI (tiếng Việt cố định).

### 0.4 Luồng tổng thể

```
1) GÁN VIỆC (con người — UI Kanban)
   TaskDetailModal / KanbanColumn (dropdown mới, nguồn GET /api/workspaces/{id}/members)
        │ assigneeId = <ai_agent user của workspace>
        ▼
   PUT /api/tasks/{taskId}  (TaskService.UpdateTaskAsync)
        └─ RequireAssigneeInWorkspaceAsync (S7: siết membership)
        └─ nếu assignee là agent ⇒ IAiAgentResolver.EnsureAsync(workspaceId) đã tạo row users + membership (lazy)

2) CHẠY (Manager bấm "Chạy Agent")
   POST /api/tasks/{taskId}/agent-runs          (Manager/Admin, CSRF, Agent:Enabled, advisory lock per task)
        → INSERT agent_runs (Running) → broadcast AgentRunProgress(Running) → 202 Accepted
        ┌─────────────────────── phần chạy ở scope nền riêng ───────────────────────┐
        │ AgentRunOrchestrator                                                      │
        │  a. Load task/board/column + comment gần nhất + câu trả lời của run trước │
        │  b. LOOP: IAiToolCallingProvider.ChatAsync(systemPrompt, messages, tools) │
        │       ├─ tool_calls[] ⇒ AgentToolRegistry.Dispatch ⇒ append role="tool"   │
        │       └─ sau MỖI vòng: kiểm guardrail (D12) ⇒ vượt ⇒ dừng + stop_reason  │
        │  c. DraftOutput      ⇒ AgentOutputService ⇒ AiActionLog Pending (D9)      │
        │                       ⇒ status = AwaitingApproval (chờ Manager duyệt)     │
        │     RequestClarification ⇒ comment agent (D10) + notification             │
        │                       ⇒ status = AwaitingClarification                    │
        │                       ⇒ EnsureClarificationColumn + MOVE task sang cột đó │
        │  d. UPDATE agent_runs (counters, trace, finished_at, stop_reason)         │
        │     → broadcast AgentRunProgress(terminal) → release lock                 │
        └──────────────────────────────────────────────────────────────────────────┘

3) DUYỆT (con người — UI tái dùng drawer Accountability sẵn có)
   POST /api/ai-actions/{logId}/approve ⇒ PostCommentApplier / PostAttachmentApplier
        ├─ PostCommentApplier    ⇒ ICommentService.CreateCommentAsync(userId = agent_user_id)
        └─ PostAttachmentApplier ⇒ INSERT task_attachments (bytea, cap 512 KB)
   Reject / Undo  ⇒ đúng vòng đời Phase 4 (Undo = soft-delete comment / HARD delete attachment)

4) LÀM RÕ (nếu Agent dừng ở bước c)
   Task nằm ở cột is_clarification, câu hỏi là comment của agent
        → trưởng nhóm trả lời (comment) → bấm "Chạy lại"
   POST /api/tasks/{taskId}/agent-runs/{prevRunId}/rerun
        → run MỚI (previous_run_id = prevRunId), bất biến D14
```

---

## 1. Checklist tổng theo §

| § | Hạng mục | Trạng thái |
|---|---|---|
| §2 | Backend – Schema & 2 migration (agent identity, `is_clarification`, `agent_runs`, `task_attachments`) | ✅ **XONG** — migration `Phase7AiAgentSchema`, verify **36/36 PASS** (nhóm A, `report/phase-7-ai-agent-executor-test-report.md` §2.1) |
| §3 | Backend – Module Board (siết assignee, `MemberType`, event SignalR, `IAiAgentResolver`, `is_clarification`) | ✅ **XONG** — verify **44/44 PASS** (nhóm I, `report/phase-7-ai-agent-executor-test-report.md` §2.2); **1 bug thật của §2 đã sửa** (`agent_runs.status` 16→32) |
| §4 | Backend – Module Ai (provider tool-calling, Tavily, 4 tool, orchestrator, guardrail, 2 applier, endpoints) | ✅ **XONG** — verify **223/223 PASS** (nhóm B/C/D/E/F/G/H + **1 lần gọi DeepSeek thật**; `report/phase-7-ai-agent-executor-test-report.md` §2.3); **3 bug thật đã sửa** (2 trong code, 1 lỗ hổng spec) |
| §5 | Frontend – dropdown assignee, trạng thái agent trên Kanban, drawer duyệt, attachment | [ ] |
| §6 | Config & DI (`Agent`, `Tavily`, `appsettings.json`, 1 dòng log) | ✅ **XONG cùng §4** — `appsettings.json` có 2 section `Agent`/`Tavily`, 1 dòng log trong `LogResolvedConfiguration` (không chứa secret); chỉ còn đối chiếu tài liệu |
| §7 | Verify bằng harness ngoài workspace (A–J) | ✅ **XONG A–I** — **291/291 check PASS** (A 36+11 · B/C 89 · C-real Tavily 5 · D 43 · E 19 · F 44 · G 21 · H 22 · I 44+29 · **DeepSeek thật + Tavily thật** 8); ⬜ **nhóm J (frontend) chờ §5** |
| §8 | Đối chiếu 6 ô hoàn thiện của roadmap + Definition of Done | ✅ **XONG** — 6 ô gốc đã tick phần backend (+ ghi rõ ô nào cần §5), ô phát sinh "membership" đã tick, ô phát sinh "dropdown UI" chuyển §5; DoD còn đúng 1 dòng (frontend test > 155) thuộc §5 |

---

## 2. Backend – Schema & Migration

> **✅ ĐÃ XONG.** Migration `20260911145639_Phase7AiAgentSchema` đã sinh + áp lên DB local; `dotnet ef migrations list` = **6**;
> `dotnet build TeamNexus.sln` = **0 warning / 0 error**; verify nhóm **A** = **36/36 PASS** (hình dạng schema + cưỡng chế ràng buộc
> trên PostgreSQL thật, harness ngoài workspace đã xoá). Chi tiết: `report/phase-7-ai-agent-executor-test-report.md` §2.1.
>
> Mục tiêu: **1 migration duy nhất** `Phase7AiAgentSchema`. Chỉ tách migration thứ 2 nếu EF Core bắt buộc thứ tự FK.
> Sau khi xong: `dotnet ef migrations list` = **6 migration**.
>
> ⚠️ **Quy trình bắt buộc trong môi trường sandbox** (đã kiểm chứng — nếu bỏ qua sẽ sinh migration sai **im lặng**):
> `dotnet build TeamNexus.sln -m:1 -nr:false` (0/0) **rồi** mới `dotnet ef migrations add … --no-build`
> (và mọi lệnh `dotnet ef` khác cũng phải `--no-build`). Lý do: `--no-build` đọc DLL trong `bin`, không đọc source.

### 2.1 `workspace_members` (sửa bảng có sẵn)

- [x] `Data/Entities/WorkspaceMember.cs`:
  ```csharp
  /// <summary>Loại thành viên: người thật hay trợ lý AI (Phase 7 §2.1).</summary>
  public enum MemberType { Human, AiAgent }

  public MemberType MemberType { get; set; } = MemberType.Human;

  /// <summary>Tên hiển thị của agent trong workspace; null với thành viên người.</summary>
  public string? AiAgentName { get; set; }
  ```
- [x] `WorkspaceMemberConfiguration.cs`:
  - `MemberType` → `HasConversion<string>().HasMaxLength(16)` (đúng quy ước "enum = text + CHECK", `04` §1.2).
  - CHECK `ck_workspace_members_member_type`: `"member_type" IN ('human', 'ai_agent')`.
  - `AiAgentName` → `HasMaxLength(120)`.
  - **Partial unique index** `uq_workspace_members_ai_agent` trên `WorkspaceId` với `HasFilter("\"member_type\" = 'ai_agent'")` ⇒ mỗi workspace đúng **1** agent.
  - **KHÔNG** dùng `AiAgentName` để tìm agent (tên có thể bị đổi) — luôn lọc theo `MemberType`.
  - Giữ nguyên CHECK `ck_workspace_members_role` (agent vẫn phải có `role`; dùng `Member`).
- [x] ⚠️ **Query filter hiện có của `WorkspaceMember`** (`Workspace == null || Workspace.DeletedAt == null`) phải được giữ; **không** lọc bỏ `member_type='ai_agent'` ở tầng DbContext — việc ẩn/hiện là quyết định của tầng service (§3.2).

### 2.2 `board_columns` (sửa bảng có sẵn)

- [x] `BoardColumn.cs`: `public bool IsClarification { get; set; }` — docstring nêu rõ **đối xứng với `IsDone`** và **loại trừ nhau**.
- [x] `BoardColumnConfiguration.cs`: **partial unique index** `uq_board_columns_clarification` trên `BoardId` với `HasFilter("\"is_clarification\"")` ⇒ tối đa 1 cột "Chờ làm rõ"/board.
- [x] Index sẵn có `UQ (board_id, position)` **không** đổi.

### 2.3 `agent_runs` (bảng mới)

- [x] `Data/Entities/AgentRun.cs` — `IAuditableEntity`, không navigation tới `Task` (tránh query filter `deleted_at` của task làm ẩn run khi task bị xoá mềm):

  | Field | Kiểu C# | Ghi chú |
  |---|---|---|
  | `Id` | `Guid` | PK, sinh ở app layer |
  | `WorkspaceId` | `Guid` | FK → `workspaces`, required |
  | `BoardId` | `Guid` | FK → `boards`, required (SignalR group) |
  | `TaskId` | `Guid` | FK → `tasks`, required |
  | `AgentUserId` | `Guid` | FK → `users`, required |
  | `TriggeredByUserId` | `Guid` | FK → `users`, required (Manager bấm nút) |
  | `Status` | `AgentRunStatus` | enum (§2.5) |
  | `StopReason` | `AgentStopReason?` | enum (§2.5); null khi đang chạy |
  | `ClarificationQuestion` | `string?` | ≤ 2000 |
  | `ClarificationCommentId` | `Guid?` | FK → `task_comments`, Restrict |
  | `ResolutionCommentId` | `Guid?` | FK → `task_comments`, Restrict |
  | `PreviousRunId` | `Guid?` | FK → `agent_runs` (self), Restrict |
  | `ToolCallTrace` | `string` | jsonb, default `"[]"`; mảng compact |
  | `TraceTruncated` | `bool` | true khi vượt `ToolTraceMaxEntries` |
  | `ToolCallCount` | `int` | |
  | `LlmCallCount` | `int` | |
  | `PromptTokens` | `int` | **cộng dồn mọi lượt gọi** trong run |
  | `CompletionTokens` | `int` | |
  | `OutputKind` | `string?` | `Comment` \| `Attachment` (§2.5) |
  | `AiActionLogId` | `Guid?` | FK → `ai_action_logs`, Restrict |
  | `NotificationSent` | `bool` | đã cảnh báo Manager khi dừng bất thường |
  | `Error` | `string?` | ≤ 2000 |
  | `StartedAt` / `FinishedAt` | `DateTimeOffset` / `DateTimeOffset?` | |
  | `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | tự stamp qua `IAuditableEntity` |

- [x] `AgentRunConfiguration.cs`:
  - `builder.ToTable("agent_runs", t => t.HasCheckConstraint("ck_agent_runs_status", "\"status\" IN ('Running','AwaitingClarification','AwaitingApproval','Completed','Failed')"));`
  - CHECK `ck_agent_runs_stop_reason` (9 giá trị) + CHECK `ck_agent_runs_output_kind` (`output_kind IS NULL OR output_kind IN ('Comment','Attachment')`).
  - `Status`/`StopReason`/`OutputKind` → `HasConversion<string>()` + `HasMaxLength(32)`.
  - `ToolCallTrace` → `.HasColumnType("jsonb")`, `IsRequired()`, default `"[]"`; `ClarificationQuestion` ≤ 2000; `Error` ≤ 2000.
  - **Mọi FK `DeleteBehavior.Restrict`** (`04` §7: không cascade vật lý).
  - `PreviousRunId` self-FK `Restrict`.
  - Index: `(TaskId, StartedAt)`; `(WorkspaceId, StartedAt)`; partial `(Status)` với `HasFilter("\"status\" = 'Running'")` cho reaper.
  - **Không** `HasQueryFilter` (audit append-only, giống `ai_action_logs`).

### 2.4 `task_attachments` (bảng mới)

- [x] `Data/Entities/TaskAttachment.cs`:

  | Field | Kiểu C# | Ghi chú |
  |---|---|---|
  | `Id` | `Guid` | PK |
  | `TaskId` | `Guid` | FK → `tasks`, required |
  | `CreatedByUserId` | `Guid` | FK → `users`, required (= `agent_user_id` đợt này) |
  | `SourceRunId` | `Guid?` | FK → `agent_runs`, Restrict |
  | `FileName` | `string` | ≤ 255, ASCII-safe (tái dùng `ReportFileName.Normalize`) |
  | `ContentType` | `string` | ≤ 128 |
  | `SizeBytes` | `int` | = `Content.Length`; app layer chặn > `MaxAttachmentBytes` |
  | `Content` | `byte[]` | `bytea` |
  | `CreatedAt` | `DateTimeOffset` | |

- [x] `TaskAttachmentConfiguration.cs`: `builder.Property(x => x.Content).HasColumnType("bytea").IsRequired();`; FK Restrict; index `TaskId`.
  **KHÔNG** `IAuditableEntity` (không có `UpdatedAt` — file sinh ra là bất biến), **KHÔNG** soft delete (**D5** — ngoại lệ duy nhất của schema).

### 2.5 Enum & hằng số (Persistence + module Ai)

- [x] Trong `TeamNexus.Persistence.Data.Entities` (dùng cho CHECK ở `04` §4):
  ```csharp
  public enum AgentRunStatus { Running, AwaitingClarification, AwaitingApproval, Completed, Failed }
  public enum AgentStopReason { DraftProduced, QuestionAsked, ToolLimit, TimeLimit, TokenBudget, ProviderError, Cancelled, TaskChanged, InternalError }
  ```
- [x] Trong module Ai (`Services/Agent/AgentConstants.cs`): `AgentOutputKinds.Comment` / `.Attachment`; `AgentRunStatuses` (string khớp enum) để DTO/`stop_reason` dùng nhất quán; `AgentEntityTypes.Task`.
- [x] Ghi chú ngay trong code: `BudgetExceeded` **không** phải status — xem `AgentStopReason.TokenBudget` / `.TimeLimit` / `.ToolLimit` (D4).

### 2.6 Migration

- [x] `dotnet ef migrations add Phase7AiAgentSchema --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api`
  > Tên migration khớp số giai đoạn hiện hành (**Phase7** = AI Agent Executor). Migration **chưa từng tồn tại** trước đây nên đổi tên không tốn gì; chỉ mất một lần tìm–thay khi đọc tài liệu cũ.
- [x] Kiểm tra file migration sinh ra **chỉ** chứa: 2 cột mới trên `workspace_members` + CHECK + partial UQ; 1 cột mới trên `board_columns` + partial UQ; 2 `CreateTable` (`agent_runs`, `task_attachments`) + CHECK + index + FK. **Không** có diff ngoài dự kiến (nhất là **không** đụng Identity table).
- [x] `dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api` → `dotnet ef migrations list` = **6**.
- [x] `dotnet build TeamNexus.sln` → **0 warning / 0 error**.

---

## 3. Backend – Module Board (thay đổi nhỏ nhưng bắt buộc)

> **✅ ĐÃ XONG.** Verify bằng harness thật (API Kestrel + PostgreSQL 18 local + JWT tự ký) — **44/44 check PASS**,
> chi tiết ở `report/phase-7-ai-agent-executor-test-report.md` §2.2. `dotnet build TeamNexus.sln` = **0 warning / 0 error**;
> frontend **155 tests** không đổi (thay đổi DTO chỉ **thêm** field ở cuối nên tương thích hai chiều).
>
> **Hai bug thật bắt được khi verify §3** (đã sửa; xem bảng bug trong report):
> 1. `agent_runs.status` khai `varchar(16)` nhưng `AwaitingClarification` dài **21** ký tự ⇒ mọi run dừng ở trạng thái
>    "Chờ làm rõ" **không thể INSERT**. Đã đổi `HasMaxLength(32)` và **sinh lại migration** `Phase7AiAgentSchema` (§2).
> 2. `activeRunIds.GetValueOrDefault(t.Id)` trả `Guid.Empty` khi task không có run ⇒ JSON thành
>    `"00000000-0000-0000-0000-000000000000"` thay vì `null`. Đã đổi sang `TryGetValue` ở **cả** `TaskService` và `BoardService`.
>
> ⚠️ **Điều chỉnh có chủ ý so với §3.4 (chốt khi verify):** `EnsureAgentAsync` **được** gọi từ
> `WorkspaceMemberService.GetMembersAsync` (read path), không chỉ từ "luồng assign" như câu chữ cũ. Lý do: danh sách thành viên
> là **nguồn duy nhất** của dropdown assignee, nếu không bảo đảm row agent tồn tại thì **không workspace nào gán được việc cho
> agent** — tức ô roadmap "gán task cho AI Agent qua đúng UI assignee hiện có" không thể hoàn thành. Vẫn **lazy theo nhu cầu**
> (chỉ tạo khi có người mở danh sách thành viên), **không** auto-seed workspace, và gọi trong `try/catch` best-effort
> (lỗi tạo agent không làm hỏng endpoint đọc — tiền lệ `IActivityLogWriter`/`IBoardEventPublisher`).
>
> ⚠️ **Ghi nhận để §4 xử lý (không chặn §3):** `ResolvePageAssigneeIsAiAgentAsync` hiện hỏi resolver **một lần cho mỗi
> `AssigneeId` khác nhau trong trang**. Đúng theo bất biến "1 agent/workspace" nên fixture thật chỉ tốn 1 truy vấn, và đây
> **không** phải N+1 theo số task; nhưng với board lớn nhiều assignee thì §4 nên đổi sang một truy vấn gộp
> (`WorkspaceMembers` lọc `member_type = 'ai_agent'`) khi bật resolver thật.

### 3.1 DTO

- [x] `DTOs/TaskDtos.cs` → `TaskResponse` thêm 2 field **cuối** (giữ thứ tự cũ để frontend cũ không vỡ):
  ```csharp
  bool AssigneeIsAiAgent,     // true khi assignee là agent của workspace
  Guid? ActiveAgentRunId      // run đang Running/AwaitingClarification/AwaitingApproval, null nếu không có
  ```
- [x] `DTOs/MemberDtos.cs` → `WorkspaceMemberResponse` thêm `string MemberType` (`"human"` | `"ai_agent"`).
- [x] `DTOs/ColumnDtos.cs` → `ColumnResponse` thêm `bool IsClarification`; `CreateColumnRequest`/`UpdateColumnRequest` thêm `bool? IsClarification`.

### 3.2 Siết assignee (**hạng mục bắt buộc phát sinh**, S7)

- [x] `TaskService`: thêm helper dùng chung
  ```csharp
  /// <summary>Phase 7 §3.2: assignee phải là thành viên của workspace chứa task (trước đây chỉ kiểm users.AnyAsync).</summary>
  private async Task RequireAssigneeInWorkspaceAsync(Guid workspaceId, Guid? assigneeId, CancellationToken ct)
  ```
  - `null` ⇒ trả về ngay (bỏ trống người thực hiện là hợp lệ).
  - Không phải member ⇒ `BadRequestException("Assignee is not a member of this workspace.")`.
- [x] Gọi trong `CreateTaskAsync` (thay khối `_db.Users.FirstOrDefaultAsync`) và `UpdateTaskAsync` (thay `_db.Users.AnyAsync`).
- [x] **Giữ** `Assignee = assignee` gán navigation để `MapTask` trả `AssigneeName` như cũ (đọc user qua `_db.Users` sau khi đã kiểm membership — 1 truy vấn, không N+1).
- [x] ⚠️ `CreateSubtasksApplier` (Phase 4) **đã** tự kiểm membership và có warning ⇒ **không** sửa; kiểm tra lại nó vẫn chạy đúng sau khi `TaskService` siết chặt (assignee không hợp lệ bị applier set `null` **trước khi** gọi service).

### 3.3 `TaskResponse` mới cần dữ liệu mới

- [x] `TaskService.GetTasksAsync` (list) + `GetTaskByIdAsync` (detail): 1 truy vấn phụ gom theo `taskIds`
  ```csharp
  // 1 query, không N+1: run "đang sống" của các task trong trang
  var activeRuns = await _db.AgentRuns
      .AsNoTracking()
      .Where(r => taskIds.Contains(r.TaskId)
                  && (r.Status == AgentRunStatus.Running
                      || r.Status == AgentRunStatus.AwaitingClarification
                      || r.Status == AgentRunStatus.AwaitingApproval))
      .OrderByDescending(r => r.StartedAt)
      .Select(r => new { r.TaskId, r.Id, r.Status })
      .ToListAsync(ct);
  // giữ run MỚI NHẤT mỗi task; ActiveAgentRunId = run mới nhất có status Running trước, rồi tới run đang chờ
  ```
- [x] `AssigneeIsAiAgent` = `AssigneeId != null && IAiAgentResolver.IsAiAgentAsync(workspaceId, AssigneeId.Value)` — **resolve 1 lần cho cả trang** (không gọi theo từng task).
- [x] `DtoMapping.MapTask` (hiện `internal static`, dòng 13) nhận thêm **2 tham số tuỳ chọn ở cuối**: `bool assigneeIsAiAgent = false, Guid? activeAgentRunId = null` ⇒ **4 call site hiện có không vỡ** vì đều dùng construction positional:
  - `TaskService.GetTasksAsync` (dòng 67) — list, truyền giá trị đã resolve theo trang;
  - `TaskService.GetTaskByIdAsync` (dòng 82) — detail;
  - `TaskService.CreateTaskAsync` (dòng 129) — task mới ⇒ `false`/`null` (agent chưa chạy);
  - `BoardService` (dòng 66) — board kèm tasks ⇒ **cũng** phải truyền giá trị resolve theo trang, nếu bỏ qua thì card trên board sẽ **không** hiện badge agent (bug im lặng, dễ sót — kiểm bằng test §7I).
- [x] **Ghi rõ trong code:** `ActiveAgentRunId` **không** phải nguồn sự thật duy nhất — frontend còn phải GET run để bắt kịp event SignalR đã rớt (§5C).

### 3.4 `IAiAgentResolver` (port của Board, impl ở Ai — D7)

- [x] Mới `Services/IAiAgentResolver.cs`:
  ```csharp
  /// <summary>Port khai báo trong Board, cài đặt ở module Ai (mẹo IActivityLogWriter — Phase 5 §2).</summary>
  public interface IAiAgentResolver
  {
      /// <summary>Agent của workspace (đã tạo lazy nếu bật). Throw khi workspace không tồn tại.</summary>
      Task<Guid> EnsureAgentAsync(Guid workspaceId, CancellationToken ct = default);

      /// <summary>true khi userId là member_type = ai_agent của workspace này.</summary>
      Task<bool> IsAiAgentAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

      /// <summary>Bảo đảm board có cột "Chờ làm rõ"; trả columnId (tạo lazy ở vị trí cuối nếu chưa có).</summary>
      Task<Guid> EnsureClarificationColumnAsync(Guid boardId, CancellationToken ct = default);
  }
  ```
- [x] `Services/NullAiAgentResolver.cs`: `EnsureAgentAsync`/`EnsureClarificationColumnAsync` ⇒ `NotSupportedException`; `IsAiAgentAsync` ⇒ `false`. Đăng ký trong `BoardModule` **cùng chỗ** `NullActivityLogWriter` (giữ bất biến "Board đăng ký trước Ai" — S12).
- [x] ⚠️ `AgentRunReaper`/`TaskService` chỉ gọi `IsAiAgentAsync`; `EnsureAgentAsync` chỉ được gọi từ luồng **assign** (§5) và từ module Ai.

### 3.5 `ColumnService` + cột "Chờ làm rõ" (D3)

- [x] `CreateColumnAsync`: nhận `IsClarification`; **từ chối** khi `IsDone && IsClarification` ⇒ `BadRequestException("A column cannot be both Done and Awaiting clarification.")`.
- [x] `UpdateColumnAsync`: cùng kiểm (theo giá trị **sau** khi merge request), tính cả trường hợp bật `is_clarification` lên cột đang `is_done`.
- [x] `DeleteColumnAsync`: **thêm điều kiện chặn** — `column.IsClarification` ⇒ `ConflictException("The 'Awaiting clarification' column is managed by the AI Agent and cannot be deleted.")` (kể cả khi **rỗng** — khác luồng chặn cột có task ở S14).
- [x] `ToResponse` thêm `IsClarification`.
- [x] **KHÔNG** auto-seed cột cho board cũ: cột được tạo **lazy** bởi `WorkspaceAiAgentResolver.EnsureClarificationColumnAsync` (chỉ tạo **một lần**, dùng partial unique index làm chốt chống race, bắt `DbUpdateException`/`ConflictException` ⇒ đọc lại cột đã thắng).

### 3.6 `WorkspaceMemberService`

- [x] `GetMembersAsync` trả thêm `MemberType` (đọc `wm.MemberType.ToString()` ⇒ `"Human"`/`"AiAgent"`; **chuẩn hoá về snake_case** `"human"`/`"ai_agent"` ở DTO để khớp `04` §4 và JSON của frontend).
- [x] `OrderBy(wm => wm.JoinedAt)` giữ nguyên ⇒ agent (tạo sau) nằm cuối danh sách dropdown.
- [x] **Không** lọc bỏ agent khỏi danh sách — dropdown assignee **cần** thấy agent (§5A). (Nếu sau này cần danh sách "chỉ người", thêm query param — ngoài phạm vi.)

### 3.7 Debug/kiểm tra nhanh

- [x] `GET /api/workspaces/{id}/members` phải trả agent **sau** lần assign đầu tiên (agent tạo lazy). Ghi rõ trong README module để không ai tưởng agent "mất".
- [x] Danh sách thành viên của workspace **chưa** từng gán agent ⇒ **không** có row `ai_agent` nào (không auto-seed workspace — chỉ lazy theo nhu cầu).

---

## 4. Backend – Module Ai

> **✅ ĐÃ XONG.** Verify bằng harness tạm ngoài workspace (`%TEMP%\tn-p7-ai`, **đã xoá**) = API Kestrel thật +
> PostgreSQL 18 thật + JWT tự ký: **nhóm B/C** 89/89 (hàm thuần + stub `HttpMessageHandler`) · **D/E/H/I** 107/107 ·
> **F + 503** 46/46 · **G** 21/21 · **1 lần gọi DeepSeek thật** 7/7 ⇒ **270/270 check PASS**
> (`report/phase-7-ai-agent-executor-test-report.md` §2.3). `dotnet build TeamNexus.sln` = **0 warning / 0 error**;
> `dotnet ef migrations list` = **6** (**không** sinh migration mới); frontend **không bị chạm** (baseline 155 test giữ nguyên).
>
> **Ba bug thật bắt được khi verify §4** (đã sửa; chi tiết ở bảng bug trong report):
> 1. **`DeepSeekAiProvider` không map `tool_calls`/`finish_reason`.** Response đọc bằng web-defaults (camelCase +
>    case-insensitive) nên khớp `toolCalls`/`finishReason` nhưng **không** khớp `tool_calls`/`finish_reason` của wire.
>    Hậu quả: **mọi lượt function-calling của DeepSeek bị coi là "lượt rỗng"** ⇒ agent không bao giờ gọi được tool nào.
>    Đã thêm `[JsonPropertyName]`; đây là bug chỉ nhóm C (stub) + 1 lần gọi thật bắt được, nhóm D/E với fake provider
>    **không** thể phát hiện.
> 2. **`FakeAiProvider` chọn kịch bản dựa trên cả nội dung tool result.** `SearchSystemData` trả về tiêu đề task của
>    board, nên một task có sentinel `FAKE:*` trong tiêu đề đã "cướp" nhánh kịch bản của run khác (run thường bị chuyển
>    sang nhánh hỏi lại). Sentinel nay **chỉ** đọc từ system prompt + message `role="user"` (đúng "task đang chạy"),
>    không bao giờ đọc dữ liệu sống do con người nhập.
> 3. **`POST /agent-runs/{id}/cancel` thiếu cổng `Agent:Enabled`.** Bảng §4.9 chỉ ghi 403/404/409 cho route này, nhưng
>    nhóm verify **H** yêu cầu "503 cả 3 route ghi" ⇒ đã thêm kiểm `AgentDisabledException` **trước** khi load run
>    (tính năng đang tắt không được tiết lộ run có tồn tại hay không).
>
> **Sáu điều chỉnh so với bản kế hoạch §4 (bắt buộc, có lý do):**
>
> | # | Kế hoạch §4 viết | Thực tế code | Cách làm đã chốt |
> |---|---|---|---|
> | X1 | `ICommentService.DeleteCommentAsync(taskId, commentId, userId, ct)` | chữ ký thật chỉ có `(commentId, userId, ct)` | gọi đúng chữ ký thật |
> | X2 | Undo/Apply dùng `ctx.ActingUserId` cho cả author lẫn người duyệt | `ctx.ActingUserId` = **người duyệt** (Manager), do `ResolveAsync` truyền `userId` của request | **author/created_by = `log.RequestedByUserId`** (= agent); Undo vẫn dùng `ctx.ActingUserId` (Manager) để đủ quyền xoá comment của người khác. Nếu không sửa thì công của AI bị ghi nhận cho người bấm duyệt |
> | X3 | Chống chồng bằng `SemaphoreSlim(1,1)` **toàn cục** theo tiền lệ Observer | semaphore toàn cục khiến task B bị **409 oan** khi task A đang chạy | **đặt chỗ theo task** (`ConcurrentDictionary<Guid,byte>` trong `AgentRunCancellationRegistry`) + kiểm row `Running` cùng task (đa instance) + `pg_try_advisory_lock(hash(taskId))` cho phần chạy |
> | X4 | Advisory lock lấy trong **request** nhưng nhả trong `finally` của `ExecuteAsync` | lock session-level nằm trên connection của scope đã lấy nó; scope request chết ngay khi trả 202 ⇒ lock nhả sớm / rò qua pool | `StartAsync` **tạo scope nền trước**, lấy lock trên connection của scope nền đó rồi mới `Task.Run(ExecuteAsync)`; `finally` nhả lock + đặt chỗ + dispose CTS (lỗi giữa chừng ⇒ nhả tường minh) |
> | X5 | Thông báo agent dùng chung `NotificationTypes.All` | `NotificationTypes.All` là **whitelist chống hallucination của Observer** — thêm 3 loại agent vào đó cho phép model Observer sinh `AgentRunFailed` và **qua** validator | tách `AgentNotificationTypes` riêng; `NotificationTypes.All` giữ nguyên 4 giá trị (có check trong nhóm B) |
> | X6 | Tái dùng `ReportFileName.Normalize` | hàm thật tên **`Slugify`** và module Ai **không** reference Reporting | thêm `ProjectReference Ai → Reporting` và gọi `ReportFileName.Slugify` (không có chu trình: Reporting → Shared/Persistence/Board); cập nhật `04` §3.8 |
>
> **Hai key config thêm ngoài bảng §6** (có lý do, không hard-code chuỗi tiếng Việt / biến thể auth):
> `Agent:ClarificationColumnName` (mặc định `"Chờ làm rõ"`) và `Tavily:AuthMode` (`Bearer` *(mặc định)* | `Body`).
> `AuthMode` tồn tại để chốt biến thể Tavily **bằng thực nghiệm** đúng như §4.3 yêu cầu: nhóm C verify wire-shape của
> biến thể đang chọn, và nếu lần gọi Tavily thật trả 401 thì chỉ cần đổi config, không sửa code.
>
> **§6 đã làm cùng §4** (2 section trong `appsettings.json` + 1 dòng log `Ai module: Agent enabled=…`) — mục §6 dưới
> đây chỉ còn phần đối chiếu tài liệu và ghi chú `dotnet user-secrets set "Tavily:ApiKey"`.

### 4.1 Cấu trúc thư mục (mới)

```
src/Modules/Ai/TeamNexus.Modules.Ai/
  Options/AgentOptions.cs              (section "Agent")
  Options/TavilyOptions.cs             (section "Tavily")
  Services/Agent/
    AgentConstants.cs                  (AgentOutputKinds, AgentRunStatuses, AgentMarkers)
    WorkspaceAiAgentResolver.cs        (IAiAgentResolver — impl thật, D1/D7)
    AgentPrompts.cs                    (system prompt + context builder)
    AgentToolDefinitions.cs            (whitelist 4 tool + JSON schema + tool_choice auto)
    AgentToolRegistry.cs               (dispatch theo tên tool)
    Agents/AgentToolContext.cs         (workspaceId/boardId/taskId/agentUserId/runId)
    Agents/SearchSystemDataTool.cs
    Agents/WebSearchTool.cs
    Agents/DraftOutputTool.cs
    Agents/RequestClarificationTool.cs
    IWebSearchProvider.cs              (port + WebSearchResult)
    TavilyWebSearchProvider.cs
    FakeWebSearchProvider.cs
    AgentRunOrchestrator.cs            (vòng lặp + guardrail + ghi agent_runs)
    AgentGuardrails.cs                 (public static — D19)
    AgentOutputService.cs              (tạo AiActionLog Pending cho PostComment/PostAttachment)
    AgentAttachmentFactory.cs          (public static: chọn kind theo độ dài + tên file)
    AgentRunReaper.cs                  (IHostedService — D13)
  DTOs/AgentRunDtos.cs
  Endpoints/AgentRunEndpoints.cs
  Endpoints/AttachmentEndpoints.cs
  Endpoints/AgentEndpointHelpers.cs    (bản copy RequireUserId — board's CurrentUser là internal)
  Services/Appliers/PostCommentApplier.cs
  Services/Appliers/PostAttachmentApplier.cs
```

### 4.2 Mở rộng `IAiProvider` (không phá hợp đồng cũ — S6)

- [x] **Giữ nguyên** `IAiProvider`, `AiCompletionRequest`, `AiCompletionResult`, `CompleteAsync` ⇒ **Phase 3/5 không đổi một dòng**.
- [x] Thêm interface + model mới trong `Services/AiProvider.cs` (cùng file, cùng namespace):
  ```csharp
  /// <summary>Phase 7: provider có khả năng function-calling (nhiều lượt, tool_calls/tool results).</summary>
  public interface IAiToolCallingProvider
  {
      Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default);
  }

  public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);

  public sealed record AiToolInvocation(string Id, string Name, string ArgumentsJson);

  /// <summary>role ∈ {system,user,assistant,tool}; tool ⇒ bắt buộc ToolCallId; assistant ⇒ có thể có ToolCalls.</summary>
  public sealed record AiChatMessage(string Role, string? Content, string? ToolCallId = null, string? Name = null,
                                     IReadOnlyList<AiToolInvocation>? ToolCalls = null);

  public sealed record AiChatRequest(string SystemPrompt, IReadOnlyList<AiChatMessage> Messages,
                                     IReadOnlyList<AiToolDefinition> Tools, double Temperature, int MaxTokens);

  /// <summary>FinishReason: "stop" | "tool_calls" | "length" (length ⇒ hết ngân sách 1 lượt).</summary>
  public sealed record AiChatResult(string? Content, IReadOnlyList<AiToolInvocation> ToolCalls,
                                    int? PromptTokens, int? CompletionTokens, string FinishReason);
  ```
- [x] `DeepSeekAiProvider` implement **cả hai** interface:
  - Dùng **chung** named `HttpClient` `AiModule.HttpClientName` và **chung** payload record (`ChatRequest` thêm `Tools` + `ToolChoice`, `DefaultIgnoreCondition = WhenWritingNull` ⇒ tự bỏ field khi null — đúng pattern `response_format` hiện có).
  - `tools = [{ type: "function", function: { name, description, parameters } }]`, `tool_choice = "auto"` khi có tool.
  - **KHÔNG** dùng `strict: true`/beta base URL ở giai đoạn này (schema `strict` cấm `minLength/maxLength`, cấm property không `required` — sẽ trói việc mô tả tool; ghi lý do vào code).
  - Parse response: `choices[0].message.content` (nullable — lượt gọi tool **không** có content), `choices[0].message.tool_calls[]` → `Id`/`Function.Name`/`Function.Arguments`, `choices[0].finish_reason`, `usage.*`.
  - `FinishReason == "length"` **không** phải lỗi ⇒ để orchestrator map thành `TokenBudget` (§4.8).
  - Lỗi HTTP/timeout/JSON hỏng ⇒ `AiProviderException` (502) **cùng class sẵn có**; key **không** vào log/exception. **Không** retry HTTP.

### 4.3 `TavilyWebSearchProvider` (§4.3a) + port + fake (§4.3b)

- [x] `IWebSearchProvider`:
  ```csharp
  public sealed record WebSearchResult(string Title, string Url, string Snippet);

  public interface IWebSearchProvider
  {
      /// <summary>Kết quả đã cap/cắt sẵn cho prompt. Mọi lỗi ⇒ AiProviderException (502).</summary>
      Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int maxResults, CancellationToken ct = default);
  }
  ```
- [x] `TavilyWebSearchProvider`:
  - Named `HttpClient` **riêng** `AiModule.TavilyHttpClientName` (`"Tavily"`), timeout từ `TavilyOptions.TimeoutSeconds`.
  - `POST {TavilyOptions.NormalizedBaseUrl}/search`, body JSON: `api_key`, `query`, `max_results`, `search_depth` (snake_case — dùng **cùng** `RequestJson` options của DeepSeek provider).
  - `Accept: application/json`; `Authorization` chỉ dùng nếu chọn biến thể Bearer — **chốt 1 biến thể sau khi verify §7C** và ghi lại đúng biến thể đó trong code + README.
  - Parse phòng thủ `results[]` → `{ Title = title, Url = url, Snippet = content }`; cắt `Snippet` ≤ `Agent:MaxToolResultChars`; giới hạn `maxResults` ≤ `Agent:WebSearchMaxResults`.
  - Thiếu `results`/JSON hỏng ⇒ trả **mảng rỗng** + `LogWarning` (giống `FakeAiProvider` xử lý payload hỏng) — **không** throw.
  - HTTP ≠ 2xx / timeout ⇒ `AiProviderException` (502) — **không** retry.
  - ⚠️ **Bắt buộc** ghi rõ trong code + §7: mọi tên field của Tavily (`api_key` trong body vs header Bearer, `results[].content` vs `.snippet`) **phải** xác nhận bằng stub `HttpMessageHandler`/1 lần gọi thật **trước** khi viết prompt; không suy đoán.
- [x] `FakeWebSearchProvider` (`Tavily:ApiKey` rỗng — cùng triết lý `FakeAiProvider`): trả 2–3 kết quả cố định có URL thật dạng `https://example.test/...`, `Snippet` ngắn; **không** gọi mạng.
- [x] Đăng ký (theo `HasApiKey`, giống `IAiProvider`):
  ```csharp
  services.AddSingleton<IWebSearchProvider>(sp => tavily.HasApiKey
      ? ActivatorUtilities.CreateInstance<TavilyWebSearchProvider>(sp)
      : ActivatorUtilities.CreateInstance<FakeWebSearchProvider>(sp));
  ```

### 4.4 Bộ tool (whitelist **đúng 4**, không tool tự do)

- [x] `AgentToolDefinitions.cs` — `public static IReadOnlyList<AiToolDefinition> All` (JSON schema viết tay bằng `JsonSerializer.Serialize` từ object literal, **không** dùng reflection):

  | Tool | Tham số | Trả về cho model | Cap |
  |---|---|---|---|
  | `SearchSystemData` | `scope` ∈ {`task`,`board`,`workspace`} (required), `query` (string, optional), `limit` (int, optional) | JSON compact: task của scope (title, description **cắt**, column, assignee, priority, dueDate, comment gần nhất + thời điểm) | `Agent:MaxToolResultChars`; `limit` clamp 1–20 |
  | `WebSearch` | `query` (required), `maxResults` (optional) | JSON `[{title,url,snippet}]` | `Agent:WebSearchMaxResults` + `MaxToolResultChars` |
  | `DraftOutput` | `content` (required), `fileName` (optional), `contentType` (optional) | `{ "accepted": true, "kind": "Comment"\|"Attachment" }` | `content` ≤ `Agent:MaxDraftChars` |
  | `RequestClarification` | `question` (required), `reason` (optional) | `{ "accepted": true }` | `question` ≤ 2000 |

- [x] **Ràng buộc bảo vệ (bắt buộc ghi trong code):**
  - `SearchSystemData` **chỉ** đọc được workspace của `AgentToolContext.WorkspaceId`. **Không** tool nào nhận `workspaceId`/`boardId`/`taskId` khác từ model (lấy từ context, **không** từ arguments) — model không thể đọc chéo workspace.
  - `SearchSystemData` **không** trả: `bytea` attachment, `description` quá dài, thông tin user ngoài workspace, `ai_action_logs`, `agent_runs`.
  - `DraftOutput` **không** ghi gì vào DB (chỉ trả kết quả cho model); việc ghi nằm ở `AgentOutputService` **sau khi** loop kết thúc.
  - `RequestClarification` **không** tự ghi comment; orchestrator làm (§4.8 bước c) để chỉ có **1** nơi ghi.
- [x] `AgentToolRegistry`: `Task<string> DispatchAsync(string name, string argumentsJson, AgentToolContext ctx, CancellationToken ct)`.
  - Tên ngoài whitelist ⇒ trả `{"error":"Unknown tool '<name>'."}` cho model (kịch bản "model gọi bậy" phải verify được, §7C).
  - `argumentsJson` không parse được ⇒ trả lỗi JSON cho model để tự sửa; **không** throw, **không** tính là tool-call thành công (vẫn cộng `tool_call_count`).
  - Mọi exception **của tool** ⇒ bắt, log, trả `{"error":"…"}` cho model; **không** làm run `Failed` (model có quyền thử lại/đổi hướng). Riêng `OperationCanceledException` ⇒ rethrow.

### 4.5 `FakeAiProvider` nhánh thứ 3 (D16, S10)

- [x] Marker: `AgentMarkers.Executor = "{\"agent\":\"executor\"}"` — đặt ở **dòng đầu** `SystemPrompt` (khác Observer/Phase 3 dùng `UserPrompt`; ghi rõ lý do: prompt của agent dài và bắt đầu bằng context, marker ở system prompt đọc rõ hơn).
- [x] Kịch bản xác định theo **số lượt gọi tool đã có** trong `Messages` (đếm `role == "tool"` + số `assistant` có `ToolCalls`) ⇒ trả lần lượt:
  1. `SearchSystemData({scope:"board", limit:10})`
  2. `WebSearch({query:"…", maxResults:2})`
  3. `DraftOutput({content:"…", fileName:"bao-cao-ai.md", contentType:"text/markdown"})`
  4. (nếu bị hỏi lại — nhánh test riêng) `RequestClarification({question:"…"})`
- [x] `content` ngắn kèm 1 câu tiếng Việt ở mỗi lượt (mô phỏng assistant message thật). Prompt không có marker ⇒ giữ **nguyên** hành vi Phase 3 (proposal) và Phase 5 (findings).
- [x] Ghi chú trong README: nhánh này **chỉ** dùng cho dev/verify — không tốn token, không cần mạng.

### 4.6 `AiActionService` — mở rộng tối thiểu (S3, S15)

- [x] `AiActionTypes`: `+ PostComment`, `+ PostAttachment`.
- [x] `AiEntityTypes`: `+ Task`, và hằng mới `AiEntityTypes.ForTask`.
- [x] `AiActionContext` thêm `Guid? TaskId` (**nullable**, default null) ⇒ `CreateSubtasksApplier` **không** phải sửa.
- [x] `ResolveContextAsync` → `ResolveAsync`:
  ```
  entity_type == Board  → giữ NGUYÊN hành vi hiện tại (LoadBoards → RequireManagerAsync)
  entity_type == Task   → load task (404) → load board của task (404) → RequireManagerAsync(workspace)
                          → AiActionContext(boardId, workspaceId, actingUserId, taskId)
  khác                  → BadRequestException như cũ
  ```
- [x] **Bất biến phải giữ:** mọi thay đổi khác của `AiActionService` (CAS `ExecuteUpdateAsync`, transaction, `applied_snapshot`, `decision_note`, `UndoAsync`) **không** đổi. Verify §7I phải chứng minh đường `CreateSubtasks` không hồi quy.
- [x] `RequestAgentOutputAsync(...)` (**API nội bộ mới**, S4): tạo `AiActionLog` `Pending` cho `PostComment`/`PostAttachment` **bỏ qua `RequireManagerAsync`**, thay bằng:
  - assert `task.AssigneeId == agentUserId` (agent **phải** đang được gán task) ⇒ `ForbiddenException` nếu không;
  - assert `agentUserId` là `member_type='ai_agent'` của workspace của task (chống gọi sai);
  - KHÔNG expose method này ra endpoint HTTP nào — trust boundary là **service trong process**.

### 4.7 Appliers

- [x] `PostCommentApplier : IAiActionApplier` — `ActionType => AiActionTypes.PostComment`
  - `ApplyAsync`: parse `after_snapshot` (`{ taskId, content }`) → `ICommentService.CreateCommentAsync(taskId, new CreateCommentRequest(content), ctx.ActingUserId, ct)` với **`ctx.ActingUserId = agent_user_id`** (S15) ⇒ được lợi: `activity_logs` (Phase 5) + `CommentAdded` SignalR + `authorName = agent display name` **miễn phí**.
  - Trả `AiActionAppliedResult(AiEntityTypes.Task, taskId, [], [], warnings)` và **phải** đưa `commentId` vào `applied_snapshot`. ⚠️ `AiActionAppliedResult` hiện chỉ có `CreatedTaskIds`/`CreatedLabelIds` ⇒ **thêm** `Guid? CreatedCommentId` + `Guid? CreatedAttachmentId` (nullable, không phá applier cũ) **hoặc** thêm mảng `warnings` + ghi `refs` trong `applied_snapshot`. **Chốt:** mở rộng record bằng 2 field nullable — đơn giản hơn và verify được.
  - `UndoAsync`: đọc `commentId` từ `applied_snapshot` → `ICommentService.DeleteCommentAsync(taskId, commentId, ctx.ActingUserId, ct)` (Manager có quyền xoá comment của người khác — đã có sẵn ở `CommentService.AuthorizeAsync`); `NotFoundException` ⇒ warning, không throw.
- [x] `PostAttachmentApplier : IAiActionApplier` — `ActionType => AiActionTypes.PostAttachment`
  - `ApplyAsync`: parse `after_snapshot` (`{ taskId, fileName, contentType, contentBase64, sourceRunId? }`)
    - decode base64 → kiểm `content.Length <= Agent:MaxAttachmentBytes` ⇒ vượt ⇒ `BadRequestException` (400) **trước** khi insert;
    - `AgentAttachmentFactory.SafeFileName(fileName, contentType)` (tái dùng `ReportFileName.Slugify` — S16; kế hoạch ghi nhầm là `Normalize`) ⇒ tên ASCII, loại `..`/`/`/`\`, fallback `"ai-output.md"`;
    - INSERT `task_attachments` (FK `CreatedByUserId = agentUserId`, `SourceRunId`, `SizeBytes`).
  - `UndoAsync`: **hard delete** row (`_db.TaskAttachments.Remove`), không có `deleted_at` (D5); đã xoá tay ⇒ warning.
- [x] Đăng ký trong `AiModule`: `services.AddScoped<IAiActionApplier, PostCommentApplier>(); services.AddScoped<IAiActionApplier, PostAttachmentApplier>();` — applier được resolve qua `IEnumerable<IAiActionApplier>` nên **chỉ cần 1 dòng/class** (đúng ghi chú `IAiActionApplier`).

### 4.8 `AgentRunOrchestrator` — trình tự thực thi (cố định)

- [x] `Task<AgentRunDetail> StartAsync(Guid taskId, Guid userId, Guid? previousRunId, CancellationToken ct)` — **phần guard đồng bộ** (chạy trong request, trả 202):
  1. `Agent:Enabled = false` ⇒ `AgentDisabledException` (**503**) — **trước** khi chạm DB (tiền lệ `ReportingDisabledException`).
  2. Load task (404 nếu không thấy/soft-deleted) + board + column.
  3. `IWorkspaceAccess.RequireManagerAsync(board.WorkspaceId, userId, ct)` ⇒ 403 Member / 404 workspace lạ.
  4. `task.AssigneeId` không phải agent của workspace ⇒ `BadRequestException("Task is not assigned to the AI Agent.")`. ⚠️ Endpoint **không** tự gán agent thay người dùng (giữ 1 hành động = 1 nút).
  5. `previousRunId` (chỉ ở đường rerun): run phải thuộc **cùng task**, `Status == AwaitingClarification`, và phải có `ResolutionCommentId` ⇒ sai ⇒ 400. Chọn comment trả lời: **comment mới nhất của người khác agent** có `created_at > previousRun.FinishedAt`; không có ⇒ 400 `"No answer found for the clarification request."`
  6. Lấy advisory lock theo task (D11): `SemaphoreSlim(1,1)` + `pg_try_advisory_lock(@key)` với `key = hash(taskId)` (lấy `taskId.GetHashCode()` **không** dùng — dùng `BitConverter` của `Guid` → int, deterministic, ghi rõ trong code). Không lấy được ⇒ **409**.
  7. INSERT `agent_runs` (`Running`, `StartedAt = now`, `TriggeredByUserId = userId`, `AgentUserId`, `PreviousRunId`) + UPDATE `ResolutionCommentId` nếu là rerun.
  8. Broadcast `AgentRunProgress(Running)` → trả `AgentRunDetail` cho endpoint **202**.
  9. KHÔNG chạy vòng lặp ở đây. Handler tạo scope mới `IServiceScopeFactory.CreateAsyncScope()` rồi `_ = Task.Run(...)` gọi `ExecuteAsync(runId, ct-from-scope)` — **không** dùng `CancellationToken` của request (request đã kết thúc khi trả 202). Ghi rõ trong code + README.
- [x] `Task ExecuteAsync(Guid runId, ...)` — **phần chạy nền**:
  ```
  a. Build prompt: AgentPrompts.BuildSystemPrompt() + BuildContext(task, column, comments, previousRun)
       - comment: TỐI ĐA 20 comment gần nhất, mỗi comment cắt ≤ 1000 ký tự, tổng ≤ Agent:MaxTaskContextChars
       - KHÔNG gửi attachment/bytea vào prompt
       - nếu là rerun: thêm câu trả lời của trưởng nhóm + câu hỏi cũ của agent (rõ ràng "câu hỏi này đã được trả lời")
  b. messages = [ { role:"user", content: context } ]
     tools = AgentToolDefinitions.All ; guardrail state = { toolCalls=0, llmCalls=0, promptTokens=0, completionTokens=0 }
     using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(scopeCt);
     timeoutCts.CancelAfter(TimeSpan.FromSeconds(Agent:RunTimeoutSeconds));   // wall-clock (D12)
  c. LOOP (while true):
       1. Kiểm guardrail TRƯỚC lượt gọi: llmCalls >= MaxRunLlmCalls ⇒ InternalError
       2. result = IAiToolCallingProvider.ChatAsync(...)  (catch AiProviderException ⇒ ProviderError; timeoutCts ⇒ TimeLimit)
       3. cộng llmCalls, promptTokens, completionTokens (dùng giá trị provider trả; provider trả null ⇒ giữ nguyên)
       4. kiểm guardrail SAU lượt gọi (AgentGuardrails.Evaluate — public static, D19):
            toolCalls > MaxToolCalls        ⇒ ToolLimit
            tokens   > MaxRunTokens         ⇒ TokenBudget
            wall-clock > RunTimeoutSeconds  ⇒ TimeLimit
       5. result.ToolCalls rỗng:
            result.FinishReason == "length" ⇒ TokenBudget
            result.Content rỗng            ⇒ InternalError ("model trả về lượt trống nhiều lần")
            ELSE ⇒ model tự kết thúc KHÔNG qua DraftOutput ⇒ InternalError + warning (agent phải dùng DraftOutput/RequestClarification)
       6. append assistant message (kèm ToolCalls) vào messages
       7. với MỖI tool call: dispatch ⇒ append { role:"tool", tool_call_id, content = result }
            - cộng toolCalls += 1 và kiểm guardrail ngay sau mỗi tool (không đợi hết batch)
            - nếu tool == DraftOutput            ⇒ dừng loop, stopReason = DraftProduced
            - nếu tool == RequestClarification   ⇒ dừng loop, stopReason = QuestionAsked
       8. nén lịch sử nếu tổng ký tự tool results > MaxTotalToolResultChars:
            thay nội dung tool cũ nhất bằng "[nội dung cũ đã lược bỏ để tiết kiệm token]"
            (KHÔNG bỏ message ⇒ giữ tính hợp lệ của cặp assistant/tool_call_id)
       9. ghi tool_call_trace theo từng tool (cap ToolTraceMaxEntries, mỗi entry ToolTraceResultChars, cờ TraceTruncated)
  d. Xử lý theo stopReason:
     DraftProduced     ⇒ AgentOutputService.CreatePendingAsync(...) ⇒ status=AwaitingApproval, OutputKind,
                          AiActionLogId; notification `AgentOutputPending` cho Manager/Admin
     QuestionAsked     ⇒ CommentService (userId = agent) đăng câu hỏi ⇒ ClarificationCommentId,
                          status=AwaitingClarification, ClarificationQuestion
                          ⇒ IAiAgentResolver.EnsureClarificationColumnAsync(boardId) + MOVE task sang cột đó
                          (đi qua ITaskService.MoveTaskAsync với ActingUserId = TriggeredByUserId để có SignalR + activity log)
                          ⇒ notification `AgentAwaitingClarification` cho Manager/Admin
     ToolLimit/TimeLimit/TokenBudget/ProviderError/TaskChanged/InternalError
                       ⇒ status=Failed, Error (≤2000), NotificationSent=true,
                          notification `AgentRunFailed` (Message nêu rõ ngưỡng nào bị vượt + số đã dùng)
     Cancelled         ⇒ status=Failed, stop_reason=Cancelled (do POST /cancel), KHÔNG notification
  e. Kiểm lại task TRƯỚC khi ghi kết quả (TaskChanged): task bị soft-delete, đổi assignee, hoặc đổi cột khỏi cột clarification
     ⇒ status=Failed, stop_reason=TaskChanged, KHÔNG ghi comment/attachment/log mới
  f. UPDATE agent_runs (FinishedAt, counters, trace, notification_sent) → broadcast AgentRunProgress(terminal)
     → release advisory lock + SemaphoreSlim trong `finally` (không bao giờ rò lock)
  ```
- [x] `AgentOutputService.CreatePendingAsync` (D9/D19):
  - `AgentAttachmentFactory.ChooseKind(content, contentType)`: **public static** — có `fileName`/`contentType` **hoặc** `content` dài > `Agent:AttachmentThresholdChars` (mặc định 2000, khớp cap comment 4000/2) ⇒ `Attachment`, còn lại ⇒ `Comment`.
  - `Comment` ⇒ `after_snapshot = { taskId, content }`, action `PostComment`.
  - `Attachment` ⇒ `after_snapshot = { taskId, fileName, contentType, contentBase64, sourceRunId }`, action `PostAttachment`. ⚠️ base64 làm `after_snapshot` phình ~1.37×; **đã cap 512 KB** ở tool (`MaxDraftChars`) để tổng row jsonb ≤ ~700 KB — ghi rõ trong code + §7B.
  - Gọi `IAiActionService.RequestAgentOutputAsync(...)` (S4) ⇒ **Pending**, **không** ghi dữ liệu thật.
- [x] `AgentGuardrails.Evaluate(...)` — **public static**, đầu vào là record `AgentGuardrailState` (toolCalls, llmCalls, promptTokens, completionTokens, elapsed) + `AgentOptions` ⇒ trả `AgentStopReason?`. Thuần, không I/O, không thời gian nội tại (nhận `elapsed` từ ngoài) ⇒ verify không cần AI/DB.
- [x] `AgentPrompts`: system prompt tiếng Việt nêu rõ (a) vai trò trợ lý thực thi task của workspace, (b) **bắt buộc** kết thúc bằng đúng 1 trong 2 tool `DraftOutput`/`RequestClarification`, (c) **không** tự bịa dữ liệu ngoài tool, (d) hỏi lại khi thiếu thông tin thay vì suy đoán (đúng `01` §7), (e) ngôn ngữ trả lời tiếng Việt.
- [x] `AgentRunReaper` (`IHostedService`, D13): lúc khởi động (sau delay ngắn, chỉ 1 lần — **không** dùng `PeriodicTimer`), `ExecuteUpdateAsync` cho mọi run `Running` có `started_at < now − RunTimeoutSeconds − OrphanRunGraceSeconds` ⇒ `Failed`/`InternalError`/`FinishedAt = now`; log số row. Bọc `try/catch` để **không** làm chết host (bài học `ObserverBackgroundService`).
  - ⚠️ Reaper **không** dùng chung `Agent:Enabled` (phải dọn dù tính năng đang tắt).

### 4.9 Endpoints (7 route)

- [x] `AgentRunEndpoints.cs` — group `/api/tasks/{taskId:guid}/agent-runs` + `/api/agent-runs/{runId:guid}`; `.WithTags("AgentRuns")` + `.AddEndpointFilter<DomainExceptionFilter>()`; mọi route `.RequireAuthorization()`; POST thêm `AntiforgeryValidationEndpointFilter`.

| # | Method + path | Quyền | Thành công | Lỗi |
|---|---|---|---|---|
| 1 | `POST /api/tasks/{taskId}/agent-runs` | Manager/Admin | **202** `AgentRunResponse` | 400 chưa gán agent · 403 · 404 · **409** đang chạy · 503 tắt |
| 2 | `POST /api/tasks/{taskId}/agent-runs/{runId}/rerun` | Manager/Admin | **202** `AgentRunResponse` | 400 (run không `AwaitingClarification` / không có câu trả lời) · 403 · 404 · 409 · 503 |
| 3 | `GET /api/tasks/{taskId}/agent-runs?take=` | Member+ | **200** `AgentRunResponse[]` | 403/404; `take` clamp 1–50, default 20, sort `startedAt DESC` |
| 4 | `GET /api/agent-runs/{runId}` | Member+ | **200** `AgentRunDetailResponse` (kèm `toolCallTrace`) | 403/404 |
| 5 | `POST /api/agent-runs/{runId}/cancel` | Manager/Admin | **200** `AgentRunDetailResponse` | 403 · 404 · **409** (không còn `Running`) · **503** (§7H yêu cầu 503 cho **cả 3** route ghi) |
| 6 | `GET /api/tasks/{taskId}/attachments` | Member+ | **200** `AttachmentResponse[]` | 403/404 |
| 7 | `GET /api/tasks/{taskId}/attachments/{attachmentId}/download` | Member+ | **200** `Results.File(content, contentType, fileName)` + `Content-Disposition` + `Content-Length` | 403/404 |

- [x] `POST /cancel`: set `CancellationTokenSource` **của process** cho run đó (registry `ConcurrentDictionary<Guid, CancellationTokenSource>` singleton) ⇒ orchestrator dừng ở vòng lặp kế tiếp, `stop_reason = Cancelled`, **không** notification. Run không còn `Running` ⇒ 409. Run không tìm thấy trong registry (đã xong / khác instance) ⇒ 409 + ghi rõ trong doc là hạn chế đã biết (D15: không có queue phân tán).
- [x] **Bảo mật:** route 1/2/5 kiểm quyền **trong service** bằng `RequireManagerAsync` (nhất quán Phase 5/6); route 3/4/6/7 chỉ cần `RequireMemberAsync` vì là **đọc**.
- [x] Body lỗi **luôn** `{ "error": "…" }` (trừ 400 ProblemDetails khi framework bind sai kiểu `taskId`/`take` — known note như Phase 5/6).
- [x] `AgentEndpointHelpers.RequireUserId(HttpContext)` — bản copy (board's `CurrentUser` là `internal`, S2 của Phase 6).

### 4.10 DTO

- [x] `DTOs/AgentRunDtos.cs`:
  ```csharp
  public sealed record AgentRunResponse(
      Guid Id, Guid TaskId, Guid BoardId, Guid AgentUserId, string AgentDisplayName,
      Guid TriggeredByUserId, string TriggeredByName, string Status, string? StopReason,
      string? ClarificationQuestion, Guid? AiActionLogId, string? OutputKind, string? Error,
      int ToolCallCount, int LlmCallCount, int PromptTokens, int CompletionTokens, int TotalTokens,
      DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, bool TraceTruncated);

  public sealed record AgentToolTraceEntryResponse(
      string Name, string? Arguments, string? ResultSummary, bool IsError, DateTimeOffset At, int DurationMs);

  public sealed record AgentRunDetailResponse(
      AgentRunResponse Run, IReadOnlyList<AgentToolTraceEntryResponse> ToolCallTrace,
      Guid? PreviousRunId, string? PreviousQuestion, string? ResolutionCommentContent);

  public sealed record AttachmentResponse(
      Guid Id, Guid TaskId, string FileName, string ContentType, int SizeBytes,
      Guid CreatedByUserId, string CreatedByName, Guid? SourceRunId, DateTimeOffset CreatedAt);
  ```
- [x] `AgentRunResponse.From(AgentRun run, string agentName, string triggeredByName)` + `AgentRunDetailResponse.From(...)` — map một chiều, không rải khởi tạo (tiền lệ `ObserverRunFinding.From`).
- [x] **KHÔNG** trả draft content / base64 trong `AgentRunResponse`; `ToolCallTrace` **đã** bị cap ở tầng ghi (D19) nên không cần cắt lại ở DTO.

### 4.11 Options & DI module Ai

- [x] `Options/AgentOptions.cs` — `SectionName = "Agent"`, có `HasApiKey`-style helper `IsAttachmentKind(...)` **không** cần; các key ở §6.
- [x] `Options/TavilyOptions.cs` — `SectionName = "Tavily"`, `ApiKey`, `BaseUrl`, `TimeoutSeconds`, `SearchDepth`, `NormalizedBaseUrl`, `HasApiKey`, `Timeout` (theo **đúng** khuôn `DeepSeekOptions`).
- [x] `AiModule.AddAiModule` bổ sung:
  ```csharp
  services.AddOptions<AgentOptions>().Bind(configuration.GetSection(AgentOptions.SectionName));
  services.AddOptions<TavilyOptions>().Bind(configuration.GetSection(TavilyOptions.SectionName));
  services.AddHttpClient(AiModule.TavilyHttpClientName, (sp, c) => c.Timeout = sp.GetRequiredService<IOptions<TavilyOptions>>().Value.Timeout);

  // Ghi đè NullAiAgentResolver của Board (phải đăng ký SAU AddBoardModule ở Program.cs — bất biến sẵn có)
  services.AddScoped<IAiAgentResolver, WorkspaceAiAgentResolver>();

  services.AddSingleton<IWebSearchProvider>(...);          // Tavily | Fake
  services.AddScoped<IAiActionApplier, PostCommentApplier>();
  services.AddScoped<IAiActionApplier, PostAttachmentApplier>();
  services.AddScoped<IAgentOutputService, AgentOutputService>();
  services.AddScoped<IAgentRunService, AgentRunService>();  // orchestrator (guard đồng bộ + ExecuteAsync)
  services.AddSingleton<AgentRunCancellationRegistry>();
  services.AddHostedService<AgentRunReaper>();
  ```
- [x] `MapAiModuleEndpoints` thêm: `endpoints.MapAgentRunEndpoints(); endpoints.MapAttachmentEndpoints();`.
- [x] `LogResolvedConfiguration` thêm **đúng 1 dòng**:
  `Ai module: Agent enabled=True, provider=DeepSeekAiProvider|FakeAiProvider, webSearch=Tavily|Fake, maxToolCalls=15, timeout=300s, tokenBudget=50000, llmCalls=20, maxAttachmentKB=512.` (**không** chứa key).

---

## 5. Frontend (React + TS + Vite + Ant Design)

> **🔻 ĐÃ BÀN GIAO — xem `tasks/phase-7-frontend-handover.md`.** Đây là **hạng mục duy nhất còn lại** của Giai đoạn 7. Note bàn giao
> chứa: hợp đồng API đã verify (7 route + mã lỗi thật), 11 sự thật backend mà UI **phải** tôn trọng (ví dụ `status=Completed` không
> bao giờ được set ⇒ phán quyết nằm ở `ai_action_logs`; `activeAgentRunId` gồm cả run đang chờ; agent được tạo ở lần đọc `/members`
> đầu tiên), danh sách file cần sửa với số dòng cụ thể, cách tự kiểm offline **không tốn token** (đặt `DeepSeek__ApiKey` = một
> khoảng trắng), và Definition of Done của §5.
>
> **Trạng thái: CHƯA LÀM** (không có dòng code frontend nào cho giai đoạn 7). Trước khi bắt đầu: chạy baseline `npm run lint` (0/0),
> `npx tsc -b` (exit 0), `npm run build` (OK), `npm test` (**30 files / 155 tests PASS**).
> Contract dưới đây **đóng băng** — đổi gì phải sửa cả hai phía.

### 5.1 Contract bàn giao (🔻 BÀN GIAO §5)

**a) Kiểu TS cần mirror** (viết tay theo `DTOs/TaskDtos.cs`, `MemberDtos.cs`, `ColumnDtos.cs`, `AgentRunDtos.cs`):

```ts
export type MemberType = 'human' | 'ai_agent'
export type AgentRunStatus = 'Running' | 'AwaitingClarification' | 'AwaitingApproval' | 'Completed' | 'Failed'
export type AgentStopReason =
  | 'DraftProduced' | 'QuestionAsked' | 'ToolLimit' | 'TimeLimit' | 'TokenBudget'
  | 'ProviderError' | 'Cancelled' | 'TaskChanged' | 'InternalError'

export interface TaskResponse {
  // …các field hiện có giữ nguyên…
  assigneeIsAiAgent: boolean
  activeAgentRunId: string | null
}

export interface ColumnResponse {
  // …các field hiện có giữ nguyên…
  isClarification: boolean
}

export interface WorkspaceMemberResponse {
  userId: string
  displayName: string
  role: 'Admin' | 'Manager' | 'Member'
  avatarUrl: string | null
  memberType: MemberType
}

export interface AgentRunResponse {
  id: string; taskId: string; boardId: string
  agentUserId: string; agentDisplayName: string
  triggeredByUserId: string; triggeredByName: string
  status: AgentRunStatus; stopReason: AgentStopReason | null
  clarificationQuestion: string | null
  aiActionLogId: string | null; outputKind: 'Comment' | 'Attachment' | null
  error: string | null
  toolCallCount: number; llmCallCount: number
  promptTokens: number; completionTokens: number; totalTokens: number
  startedAt: string; finishedAt: string | null; traceTruncated: boolean
}

export interface AgentToolTraceEntryResponse {
  name: string; arguments: string | null; resultSummary: string | null
  isError: boolean; at: string; durationMs: number
}

export interface AgentRunDetailResponse {
  run: AgentRunResponse
  toolCallTrace: AgentToolTraceEntryResponse[]
  previousRunId: string | null
  previousQuestion: string | null
  resolutionCommentContent: string | null
}

export interface AttachmentResponse {
  id: string; taskId: string; fileName: string; contentType: string; sizeBytes: number
  createdByUserId: string; createdByName: string; sourceRunId: string | null; createdAt: string
}
```

**b) Bảy route** (base `/api`, dùng `frontend/src/shared/api/httpClient.ts`):

| # | Method + path | Query/body | Thành công | Ghi chú |
|---|---|---|---|---|
| 1 | `POST /tasks/{taskId}/agent-runs` | — | **202** `AgentRunResponse` | cần `X-XSRF-TOKEN` (httpClient tự gắn) |
| 2 | `POST /tasks/{taskId}/agent-runs/{runId}/rerun` | — | **202** `AgentRunResponse` | nút **"Chạy lại"** |
| 3 | `GET /tasks/{taskId}/agent-runs` | `take?` | **200** `AgentRunResponse[]` | |
| 4 | `GET /agent-runs/{runId}` | — | **200** `AgentRunDetailResponse` | |
| 5 | `POST /agent-runs/{runId}/cancel` | — | **200** `AgentRunDetailResponse` | |
| 6 | `GET /tasks/{taskId}/attachments` | — | **200** `AttachmentResponse[]` | |
| 7 | `GET /tasks/{taskId}/attachments/{id}/download` | — | **200** file bytes | `responseType: 'blob'` + `URL.createObjectURL` (tái dùng `utils/reportDownload.ts`) — **không** `window.open` (sẽ vỡ auto-refresh 401) |

**c) Sự kiện SignalR** (nhóm `board-{boardId}` sẵn có, tên event `AgentRunProgress`):

```ts
export interface AgentRunProgressEvent {
  runId: string; taskId: string; boardId: string
  status: AgentRunStatus; stopReason: AgentStopReason | null
  toolCallCount: number; totalTokens: number
  clarificationQuestion: string | null
}
```

> ⚠️ **SignalR có thể đã rớt khi free-tier sleep** ⇒ mọi màn hình Kanban **phải** GET lại run khi mount và khi reconnect (D15). Event chỉ là tăng tốc, **không** là nguồn sự thật.

**d) Mã lỗi → UX**

| Status | Khi nào | `error` thật | Gợi ý UI |
|---|---|---|---|
| 400 | Task chưa gán agent | `"Task is not assigned to the AI Agent."` | `message.warning` + mở dropdown assignee |
| 400 | Rerun khi chưa có câu trả lời | `"No answer found for the clarification request."` | nhắc trưởng nhóm trả lời bằng comment trước |
| 403 | Member gọi route ghi | `"Requires Manager or Admin role in this workspace."` | **ẩn** nút "Chạy Agent"/"Huỷ" với Member |
| 404 | task/run/attachment không thấy | `"Task not found."` / `"Agent run not found."` | `Alert` + reload board |
| 409 | Đang có run chạy | `"An agent run is already in progress for this task."` | disable nút + hiện trạng thái "Đang chạy" |
| 503 | `Agent:Enabled=false` | `"AI Agent Executor is disabled."` | `Alert` "Tính năng AI Agent đang tạm tắt" |

### 5.2 Checklist §5

**A. Dropdown assignee (hạng mục bắt buộc — S1)**

- [ ] `types/board.types.ts`: thêm `MemberType`, `WorkspaceMemberResponse`, 2 field mới của `TaskResponse`, `isClarification` của `ColumnResponse`, `isClarification?` trong `CreateColumnRequest`/`UpdateColumnRequest`.
- [ ] `services/boardApi.ts`: `getMembers(workspaceId)`, `getTaskAttachments(taskId)`, `downloadAttachment(taskId, attachmentId, fileName)`.
- [ ] `hooks/useWorkspaceMembers.ts` (mới): cache theo `workspaceId`, gọi 1 lần cho cả board (không gọi theo task).
- [ ] `TaskDetailModal.tsx`: thay khối read-only (dòng 609–630) bằng `Select` (avatar + `Tag color="purple"` "AI Agent" khi `memberType === 'ai_agent'`), `allowClear`, ghi thẳng qua `onUpdateTask` (`assigneeId`), `data-testid="assignee-select"`.
- [ ] `KanbanColumn.tsx`: quick-add thêm `Select` người thực hiện (optional, cùng nguồn) ⇒ `createTask({ columnId, title, assigneeId })`.
- [ ] Sau khi đổi assignee thành/khỏi agent, **refresh board** (assignee là agent ⇒ agent row được tạo ⇒ dropdown có thêm 1 lựa chọn mới).

**B. Trạng thái "Chờ làm rõ" trên Kanban**

- [ ] `KanbanColumn.tsx`: icon `QuestionCircleOutlined` (`#f59e0b`) + tooltip khi `column.isClarification` (đối xứng `isDone`).
- [ ] `TaskCard.tsx`: badge agent theo `activeAgentRunId` + câu hỏi làm rõ nổi bật (icon + 2 dòng text `line-clamp`).
- [ ] `TaskDetailModal.tsx`: panel `AgentRunPanel` + câu hỏi làm rõ + nút **"Chạy lại"** (chỉ Manager/Admin).

**C. Agent panel + real-time + duyệt**

- [ ] `features/ai/services/agentApi.ts`, `types/agentRun.types.ts`, `hooks/useAgentRuns.ts`, `hooks/useAgentRunHub.ts` (mở rộng `useBoardHub` thêm handler `AgentRunProgress`).
- [ ] `components/AgentRunPanel.tsx`: nút **"Chạy Agent"** / **"Chạy lại"** / **"Huỷ"**, `Tag` trạng thái (map tiếng Việt), số tool-call/token đã dùng, `Timeline` cho `toolCallTrace`, cảnh báo khi `traceTruncated`, `Alert` lỗi đọc từ `stopReason`/`error`.
- [ ] `components/AgentDraftApproval.tsx`: hiện `aiActionLogId` ⇒ **tái dùng** `AiActionLogItem` + `useAiActions` sẵn có để Duyệt/Từ chối/Hoàn tác — **không** dựng lại UI accountability.
- [ ] `components/AttachmentList.tsx`: tên file, kích thước (`formatBytes`), "do AI Agent tạo", nút tải (blob).
- [ ] Nhãn tiếng Việt cho `notification.type` mới (`AgentRunFailed` / `AgentAwaitingClarification` / `AgentOutputPending`) trong `NotificationItem.tsx` (giữ mã gốc trong ngoặc để truy vết).
- [ ] Vào board / reconnect ⇒ GET run hiện tại (không chỉ dựa vào event).

**D. Chất lượng**

- [ ] `npm run lint` 0/0 · `npx tsc -b` exit 0 · `npm test` — số test **tăng** so với baseline 155.
- [ ] Test mới: assignee picker đổi được assignee (kể cả agent), badge trạng thái theo từng `status`, nút "Chạy lại" chỉ hiện khi `AwaitingClarification`, `AgentRunPanel` disable nút "Chạy Agent" khi `Running`, `AttachmentList` tải blob, mapping nhãn notification mới.
- [ ] **Không** viết lại `httpClient` / `reportDownload` (tái dùng); **không** thêm thư viện UI mới.

---

## 6. Config (`appsettings.json`) & nhật ký khởi động

- [x] Section `"Agent"` (đủ key, **không** secret):

| Key | Mặc định | Nguồn |
|---|---|---|
| `Enabled` | `true` | D18 — `false` ⇒ 503 |
| `AgentDisplayName` | `"TeamNexus Agent"` | D1 |
| `AgentAvatarUrl` | `null` | UI |
| `MaxToolCalls` | `15` | `03-roadmap.md` |
| `RunTimeoutSeconds` | `300` | `03-roadmap.md` |
| `MaxRunTokens` | `50000` | `03-roadmap.md` |
| `MaxRunLlmCalls` | `20` | chống loop |
| `MaxToolResultChars` | `4000` | kiểm soát token |
| `MaxTotalToolResultChars` | `40000` | nén lịch sử |
| `MaxTaskContextChars` | `8000` | kiểm soát token |
| `MaxCommentsInContext` | `20` | kiểm soát token |
| `MaxCommentCharsInContext` | `1000` | kiểm soát token |
| `MaxDraftChars` | `384000` | trần base64 của attachment 512 KB |
| `AttachmentThresholdChars` | `2000` | `AgentAttachmentFactory.ChooseKind` |
| `MaxAttachmentBytes` | `524288` | D5 |
| `DefaultAttachmentContentType` | `"text/markdown"` | |
| `ToolTraceMaxEntries` | `30` | cap trace |
| `ToolTraceResultChars` | `500` | cap trace |
| `OrphanRunGraceSeconds` | `60` | D13 |
| `RetentionDays` | `90` | optional (D20) |
| `WebSearchMaxResults` | `5` | tool |
| `ClarificationColumnName` | `"Chờ làm rõ"` | **thêm khi hiện thực §4** (X-note): tên cột D3 không hard-code trong code |

- [x] Section `"Tavily"`:

| Key | Mặc định | Ghi chú |
|---|---|---|
| `ApiKey` | `""` | Secret — User Secrets; rỗng/whitespace ⇒ `FakeWebSearchProvider` |
| `BaseUrl` | `https://api.tavily.com` | `NormalizedBaseUrl` theo khuôn `DeepSeekOptions` |
| `TimeoutSeconds` | `20` | |
| `SearchDepth` | `"basic"` | |
| `AuthMode` | `"Bearer"` | **thêm khi hiện thực §4**: `Bearer` (header, mặc định) hoặc `Body` (`api_key` trong body) — để chốt biến thể Tavily bằng thực nghiệm, đổi config thay vì sửa code |

- [x] Đặt key (khuyến nghị, **không** lưu file tracked):
  `dotnet user-secrets set --project src/TeamNexus.Api "Tavily:ApiKey" "tvly-..."`
- [x] `LogResolvedConfiguration` in **1** dòng (§4.11) — **không** có key/secret.

---

## 7. Verify (harness tạm ngoài workspace — D17)

> Harness ở `%TEMP%\tn-p9-*`, **đã xoá** sau khi chạy. API thật + PostgreSQL thật + `FakeAiProvider`/`FakeWebSearchProvider` + stub `HttpMessageHandler`
> ⇒ **không gọi DeepSeek/Tavily, không tốn token**. Kết quả ghi vào `report/phase-7-ai-agent-executor-test-report.md`.

**Bốn điều kiện tiên quyết (bài học Phase 5, lặp lại để không mất thời gian):**
1. Lấy lại token CSRF **sau** khi gắn JWT cookie (token ẩn danh ≠ token đã bind identity — echo token cũ ⇒ 403).
2. Seed fixture bằng **raw SQL** (`SaveChanges` của EF tự stamp `created_at`/`updated_at` ⇒ task "cũ" thành "vừa tạo", hỏng test về sau).
3. Boot API thật rồi stop **< 60s** (`Observer:StartupDelaySeconds`) **hoặc** đặt `Observer__Enabled=false` để Observer không quét DB dev.
4. `AgentRunReaper` là `IHostedService` nên chạy ngay lúc boot ⇒ test reaper phải seed run `Running` **trước** khi boot, hoặc gọi trực tiếp method dọn (khuyến nghị: gọi trực tiếp + 1 case boot thật).

| Nhóm | Nội dung | Kỳ vọng |
|---|---|---|
| **A. Schema & migration (§2)** | 2 cột `workspace_members` + CHECK + partial UQ; 1 cột `board_columns` + partial UQ; 2 bảng mới + CHECK + index + FK `RESTRICT`; `jsonb`; `bytea` | `dotnet ef migrations list` = **6**; vi phạm CHECK ⇒ `23514`, trùng partial UQ ⇒ `23505`, FK sai ⇒ `23503`; `member_type='Bogus'` bị chặn; `agent_runs.status='Bogus'`/`stop_reason='Bogus'` bị chặn; 2 agent/workspace bị chặn; 2 cột clarification/board bị chặn |
| **B. Hàm thuần (§4.8, D19)** | `AgentGuardrails.Evaluate` biên (đúng ngưỡng / vượt 1 đơn vị / null tokens), `AgentAttachmentFactory.ChooseKind`, cắt `tool_call_trace`, `SafeFileName` (tiếng Việt có dấu, `..`, `/`, `\`, rỗng ⇒ fallback) | Thuần, tất định (build 2 lần ⇒ giống byte-đối-byte), không I/O; `MaxDraftChars` × 1.37 ≤ ~700 KB |
| **C. Tool/transport contract (stub `HttpMessageHandler`)** | DeepSeek: body có `tools` + `tool_choice="auto"`, **không** có `response_format`; parse `tool_calls[]` (`id`/`function.name`/`function.arguments`) + `finish_reason`; message `role="tool"` mang `tool_call_id`; `finish_reason="length"` ⇒ orchestrator ra `TokenBudget`; HTTP 429/timeout/JSON hỏng ⇒ `AiProviderException(502)`; Tavily: body/`api_key`/parse `results[]`; HTTP ≥ 300 ⇒ 502; JSON hỏng ⇒ mảng rỗng + warning | Key **không** xuất hiện trong bất kỳ log/exception nào; tool ngoài whitelist ⇒ model nhận `{"error":...}`; `arguments` sai JSON ⇒ model nhận lỗi và loop **không** crash |
| **D. Loop end-to-end (DB thật, fake provider)** | task gán agent ⇒ `POST /agent-runs` **202**; `agent_runs` `Running` → `AwaitingApproval`; `ai_action_log_id` khác null; **trước khi Approve**: `task_comments`/`task_attachments`/`activity_logs` **không** đổi; `POST /ai-actions/{id}/approve` ⇒ comment xuất hiện (`author_id = agent_user_id`, `authorName` = `AgentDisplayName`) **hoặc** attachment (`SizeBytes == content length`, `ContentType` đúng); `POST /ai-actions/{id}/undo` ⇒ comment soft-delete / attachment **hard delete** (đếm row) | Counters khớp trace: `tool_call_count == số entry trace`, `prompt+completion == totalTokens`; nhánh attachment chọn đúng khi content dài |
| **E. "Chờ làm rõ" + "Chạy lại" (§4.8d)** | `RequestClarification` ⇒ run `AwaitingClarification`, `clarification_question` khác null, comment của agent tồn tại, task **đã** sang cột `is_clarification` (cột tạo lazy, `position` = max+1), notification `AgentAwaitingClarification` cho **Manager/Admin** (1 row/người); trưởng nhóm comment trả lời ⇒ `rerun` tạo run **mới** (`previous_run_id` = run cũ); run cũ **không** đổi byte nào | Bất biến append-only D14; rerun khi chưa có câu trả lời ⇒ **400** |
| **F. Guardrail (§4.8c, D12)** | Hạ `MaxToolCalls=2` / `MaxRunTokens=10` / `RunTimeoutSeconds=1` / `MaxRunLlmCalls=1` từng case ⇒ `Failed` + đúng `stop_reason` (`ToolLimit`/`TokenBudget`/`TimeLimit`/`InternalError`); **đúng 1** notification `AgentRunFailed`; `notification_sent=true`; gọi lại `GET` run **không** sinh thêm notification; số tool-call đã dispatch ≤ ngưỡng + 1 | Phân biệt `TokenBudget` ≠ `ProviderError` ≠ `ToolLimit`; timeout thật (wall-clock) không treo harness |
| **G. Failure modes & concurrency** | Provider lỗi ⇒ run `Failed`/`ProviderError` + 0 ghi nghiệp vụ; 2 request đồng thời cùng task ⇒ 1 × **202** + 1 × **409**; `POST /cancel` khi `Running` ⇒ 200 + `Cancelled` + **0** notification; cancel khi đã xong ⇒ 409; task soft-delete/đổi assignee giữa run ⇒ `Failed`/`TaskChanged` + không ghi; seed run `Running` cũ rồi boot ⇒ reaper đóng thành `Failed`/`InternalError` + `finished_at` | Không run nào kẹt `Running`; lock `finally` luôn nhả (chạy lại được ngay sau run thất bại) |
| **H. HTTP + quyền + disabled** | 7 route: 401 (không auth) / 403 (Member ở route ghi; thiếu CSRF) / 404 (task/run/attachment lạ) / 405 (sai method) / 409; `Agent:Enabled=false` ⇒ **503** cả 3 route ghi; `download` trả đúng byte + `Content-Length` + `Content-Disposition` ASCII; `take` clamp (0 ⇒ 20, 999 ⇒ 50) | Body lỗi luôn `{ "error": … }`; route đọc cho Member **200** |
| **I. Bất biến & không hồi quy** | Trước/sau mọi **GET**: `tasks`/`labels`/`task_labels`/`ai_action_logs`/`activity_logs` **không** đổi; `agent_runs`/`task_attachments` chỉ đổi đúng 1 lượt chạy; **`CreateSubtasks` (Phase 4) vẫn chạy đúng** sau khi `ResolveContextAsync` thành `ResolveAsync` (approve + undo + 409 CAS); **`GET /api/workspaces/{id}/members` cũ vẫn 200** với field mới; column `is_done` cũ vẫn hoạt động; **`assigneeIsAiAgent`/`activeAgentRunId` được điền ở CẢ 3 đường trả task** (`GET /boards/{id}` qua `BoardService`, `GET /boards/{id}/tasks`, `GET /tasks/{id}`) — thiếu `BoardService` là bug im lặng vì card Kanban không hiện badge agent | `git diff` **không** chạm Identity/migration cũ; `dotnet build` 0 warning/0 error |
| **J. Frontend** | Vitest cho §5 (assignee picker, badge theo `status`, nút "Chạy lại", disable khi `Running`, `AttachmentList` blob, nhãn notification mới) | `oxlint` 0/0 · `tsc -b` exit 0 · `npm run build` OK · số test > **155** |

---

## 8. Đối chiếu 6 ô hoàn thiện của `03-roadmap.md` §9

> **Trạng thái §8: BACKEND XONG, còn 1 hạng mục frontend (§5/nhóm J).** Verify **§7 = 291/291 check PASS** (xem §2.3 + §2.4 của
> report). Cột "Bằng chứng" dưới đây ghi rõ ô nào **đã xong bằng backend** và ô nào **cần §5** để hoàn tất trọn vẹn.

| # | Ô hoàn thiện | Bằng chứng | Trạng thái |
|---|---|---|---|
| 1 | Pseudo-member AI Agent trong `workspace_members`, gán task qua **đúng UI assignee hiện có** | §2.1 + §3.2 (membership guard, 400 khi ngoài workspace) + §5.2A; nhóm **A/B/D/J**. DB: 1 `users` + 1 `workspace_members(member_type='ai_agent')` mỗi workspace, partial UQ chứng minh; `PUT` task với agent ⇒ **200 + `assigneeIsAiAgent=true`** | ✅ backend / ⬜ **UI dropdown (§5A)** |
| 2 | Vòng lặp tool-calling qua DeepSeek function-calling với `SearchSystemData`/`WebSearch`(Tavily)/`DraftOutput`/`RequestClarification` | §4.2–§4.5 + §4.8; nhóm **C/D** + **C-real** (Tavily thật) + **1 lượt DeepSeek thật** (2–3 tool call, 4 356→5 114 token, không tool nào lỗi) | ✅ **XONG** |
| 3 | Trạng thái "Chờ làm rõ": agent dừng đúng lúc, câu hỏi hiển thị trên Kanban, nút "Chạy lại" hoạt động sau khi trưởng nhóm trả lời | §2.2 + §3.5 + §4.8d; nhóm **E** (run `AwaitingClarification`, cột lazy ở `max(position)+1`, comment của agent, notification Manager, rerun tạo run mới và run cũ **byte-identical**) | ✅ backend / ⬜ **hiển thị Kanban + nút "Chạy lại" (§5B)** |
| 4 | Kết quả AI (comment ngắn / file dài) đi qua đúng Accountability Layer (Pending → Approve/Reject/Undo) | §4.6/§4.7/§4.9; nhóm **D/I** (cả 2 kind: comment **soft** delete, tệp **hard** delete; CAS 409; `author_id`/`created_by` = **agent**) | ✅ **XONG** |
| 5 | Guardrail: 15 tool-call / 5 phút / ~50 000 token, có log & thông báo khi vượt ngưỡng | §4.8c + §4.11; nhóm **F** (**4** ngưỡng: `ToolLimit`/`TokenBudget`/`TimeLimit`/`MaxRunLlmCalls`, mỗi ngưỡng một lần boot; **đúng 1** notification + `notification_sent=true`; đọc lại run không sinh thêm) | ✅ **XONG** |
| 6 | `agent_runs` ghi đầy đủ trạng thái/tool trace, broadcast real-time qua SignalR để thấy tiến trình trên Kanban | §2.3 + §3.3 + §4.8f; nhóm **D/E/F/G** (trace cap + `traceTruncated`, counters ghi **dần mỗi vòng**, `AgentRunProgress` broadcast 2 lần/run) | ✅ backend / ⬜ **tiêu thụ event + badge (§5C)** |
| ➕ | (phát sinh) API assignee phải kiểm **membership** | §3.2 + nhóm **I** (user ngoài workspace ⇒ 400; agent qua đúng đường) | ✅ **XONG** |
| ➕ | (phát sinh) Dropdown chọn người thực hiện trong UI | chưa làm — **§5A**, đã bàn giao ở `phase-7-frontend-handover.md` | ⬜ **§5** |

**Definition of Done:**

- [x] `dotnet build TeamNexus.sln` → **0 warning / 0 error** (đo lại sau mọi thay đổi §4: PASS)
- [x] `dotnet ef migrations list` → **6** migration; `04-database-design.md` §3.8 khớp migration thật (nhóm A đọc `information_schema`/`pg_constraint`/`pg_indexes`)
- [x] Harness **A–I** PASS, số check ghi vào report: **A 36/36 (§2.1) + I 44/44 (§2.2) + §4 270/270 (§2.3) + §7 tổng 291/291 (§2.4)** — mỗi nhóm có bảng
- [ ] Frontend: `lint` + `tsc -b` + `build` + `test` sạch, test **> 155** ⇒ **hạng mục §5 (nhóm J)**; baseline hiện tại **không đổi** vì §4 không chạm frontend
- [x] `03-roadmap.md` §9: 6 ô gốc **backend** + ô phát sinh (membership) đã tick kèm ghi chú phần còn lại thuộc §5; khối Trạng thái cập nhật
- [x] `04-database-design.md` (§1, §2, §3.3, §3.4, §3.5, **§3.8**, §4, §5, §7, §8), `README.md`, `src/Modules/Ai/README.md`, `src/Modules/Board/README.md`, `report/phase-7-ai-agent-executor-test-report.md` đã cập nhật và **khớp với code** (+ `03-roadmap.md`, `tasks/phase-7-frontend-handover.md`)
- [x] Ghi rõ trong report các **hạn chế đã biết** (report §5, 14 mục): không streaming token; run mất khi app sleep; `cancel` chỉ trong cùng instance; chưa prune; chưa test xUnit; `status=Completed` chưa được set (phán quyết ở `ai_action_logs`); `after_snapshot` của attachment mang base64; Tavily `Bearer` đã xác nhận bằng gọi thật nhưng `Body` vẫn là phương án dự phòng cấu hình

---

## 9. Rủi ro & biện pháp

| # | Rủi ro | Mức | Biện pháp đã cài trong kế hoạch |
|---|---|---|---|
| R1 | Sửa `AiActionService.ResolveContextAsync` (code Phase 4 đã verify) gây hồi quy | **Cao** | Chỉ **thêm nhánh**, `Board` giữ nguyên đường cũ; `AiActionContext.TaskId` nullable; nhóm verify **I** bắt buộc chạy lại `CreateSubtasks` (approve/reject/undo/CAS 409) |
| R2 | Tên field API Tavily khác tài liệu ⇒ tool `WebSearch` chết | Trung bình | Nhóm **C** verify bằng stub `HttpMessageHandler` **trước** khi viết prompt; lỗi cô lập trong `TavilyWebSearchProvider`; `FakeWebSearchProvider` bảo đảm loop vẫn verify được offline |
| R3 | DeepSeek function-calling khác kỳ vọng (không trả `tool_calls`, `arguments` hỏng) | Trung bình | `FinishReason`/`tool_calls` rỗng có nhánh xử lý rõ; `arguments` hỏng ⇒ trả lỗi cho model sửa; nhóm **C/D** cover; 1 lần gọi thật (key có sẵn trong User Secrets) đối chiếu trước khi tuyên bố xong |
| R4 | `after_snapshot` chứa base64 làm phình row jsonb / tốn quota | Trung bình | Cap `MaxDraftChars` (384 000 ≈ 512 KB sau base64) + `MaxAttachmentBytes` chặn lần nữa ở applier; nhóm **B** verify tỉ lệ phình |
| R5 | App free-tier sleep cắt run ⇒ card kẹt "Đang chạy" | Trung bình | `AgentRunReaper` (D13) + nút "Chạy lại" + frontend luôn GET run khi mount/reconnect; ghi vào hạn chế đã biết |
| R6 | Loop tiêu token ngoài kiểm soát | **Cao (chi phí)** | 4 ngưỡng độc lập + timeout wall-clock thật; tool result bị cap và nén; không gọi AI khi task không được gán agent; verify **F** dùng fake provider ⇒ harness không tốn token |
| R7 | Agent đọc dữ liệu ngoài workspace qua `SearchSystemData` | **Cao (bảo mật)** | `workspaceId`/`boardId`/`taskId` **chỉ** lấy từ `AgentToolContext`, không bao giờ từ arguments; nhóm **C/D** có case "model truyền taskId lạ ⇒ tool từ chối" |
| R8 | Agent là user ⇒ có thể bị dùng để đăng nhập / leo quyền | **Cao (bảo mật)** | Row `users` agent: `lockout_enabled=true`, `two_factor_enabled=true`, `password_hash=null`, `email=null`, **không** `user_logins`; verify nhóm **A**; ghi vào `04` §7 |
| R9 | `WorkspaceMember` query filter + `member_type` mới làm hỏng truy vấn cũ (Reporting `byAssignee`, Observer `Overload`) | Trung báo | **Không** đổi query filter; agent là assignee bình thường nên Reporting/Observer vẫn thấy như một người — ghi rõ trong `04` §3.8/§7 rằng agent **sẽ** xuất hiện trong `byAssignee`/`Overload` và đây là **hành vi mong muốn** (task của agent cũng là task đang mở). Nếu sau này cần tách, thêm cờ ở Reporting (ngoài phạm vi) |
| R10 | Bỏ `ResolveContextAsync`: đường `ai-actions` list theo task chưa có endpoint | Thấp | Đợt này **không** thêm `GET /api/tasks/{taskId}/ai-actions`; UI duyệt đọc qua `aiActionLogId` (route 4) rồi `GET /api/ai-actions/{logId}` **sẵn có** ⇒ không phát sinh endpoint mới |
