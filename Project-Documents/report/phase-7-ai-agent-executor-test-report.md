# Báo cáo Kiểm thử Giai đoạn 7 — AI Agent Executor

> **Phạm vi:** Giai đoạn 7 — danh tính AI Agent (pseudo-member), vòng lặp tool-calling, trạng thái "Chờ làm rõ" + "Chạy lại",
> kết quả đi qua Accountability Layer (comment/attachment), guardrail ngân sách, `agent_runs` + broadcast SignalR.
> **Không** gồm: streaming token (non-goal), prune retention (optional), test xUnit (để Giai đoạn 8).
>
> **Kết luận: 🔄 BACKEND XONG (§2–§4 + §6). §5 frontend (nhóm J) đã bàn giao.** **§2 (Schema): 36/36** · **§3 (Board): 44/44** ·
> **§4 (Module Ai): 270/270** · **§7 đợt verify tổng (A–I + gọi thật): 291/291** ⇒ **371 check PASS** cho cả giai đoạn.
> Đã bắt và sửa **5 bug thật** (2 ở §3, 3 ở §4 — xem §4), trong đó bug #1 của §4 (**DeepSeek không map
> `tool_calls`/`finish_reason`**) sẽ làm **toàn bộ tính năng không chạy được với provider thật** mà nhóm D/E không thể phát hiện.
> Còn lại: **§5 frontend (nhóm J)** — đã bàn giao ở `tasks/phase-7-frontend-handover.md`.
> Kế hoạch chi tiết + quyết định D1–D20: `tasks/phase-7-ai-agent-executor.md`; schema: `04-database-design.md` §3.3, §3.4, §3.8.
>
> **Baseline trước khi bắt đầu (đã ghi nhận):** backend Phase 1–6 xong; frontend **30 test files / 155 tests PASS**,
> `oxlint` 0/0, `tsc -b` sạch; `dotnet ef migrations list` = **5**.

---

## 1. Môi trường & phương pháp

| Thành phần | Chi tiết |
|---|---|
| Runtime | .NET SDK 10 (`net10.0`), ASP.NET Core + Kestrel |
| Database | PostgreSQL 18 local, DB `TeamNexus` (connection string trong User Secrets) |
| Xác thực harness | JWT HS256 tự ký (`Jwt:SigningKey` trong User Secrets), cookie `access_token` (`Issuer=TeamNexus`, `Audience=TeamNexus.Web`) |
| AI | `FakeAiProvider` nhánh thứ 3 (marker `{"agent":"executor"}`) + `FakeWebSearchProvider` + stub `HttpMessageHandler` ⇒ **0 token** cho mọi case **trừ 2 lượt gọi thật có chủ ý** (DeepSeek **và** Tavily, §2.4). Key Tavily lấy từ User Secrets, **không** vào file tracked |
| API | Chạy thật `dotnet <bin>\TeamNexus.Api.dll` trên `http://127.0.0.1:5197`; **8 lần boot** khác cấu hình (mặc định / `Agent:Enabled=false` / 4 ngưỡng hạ thấp / provider hỏng / DeepSeek thật) |
| Fixture | Seed bằng **raw SQL** (tránh `SaveChanges` tự stamp `created_at`), cleanup bằng SQL hard-delete theo đúng thứ tự FK RESTRICT, DB về baseline |
| Quy ước | Harness ngoài workspace (`%TEMP%\tn-p7-ai\`), **đã xoá** sau khi chạy, **không** commit (quyết định D17) |

**Sáu điều kiện tiên quyết (bài học Phase 5 + 2 bài học mới của §4):**

1. Lấy lại token CSRF **sau** khi gắn JWT cookie (token ẩn danh ≠ token đã bind identity; echo token cũ ⇒ 403).
2. Seed fixture bằng raw SQL, **không** qua EF (EF stamp `created_at`/`updated_at` ⇒ task "cũ" thành "vừa tạo").
3. Boot API rồi stop trong **< 60s** (`Observer:StartupDelaySeconds`) hoặc `Observer__Enabled=false` để Observer không quét DB dev.
4. `AgentRunReaper` chạy lúc boot ⇒ seed run `Running` mồ côi **trước** khi boot (đã làm đúng như vậy ở nhóm G).
5. **Env var rỗng ≠ tắt cấu hình:** `SetEnvironmentVariable(name, "")` **xoá** biến ⇒ `DeepSeek__ApiKey=''` không ép được
   `FakeAiProvider` (User Secrets thắng lại và harness gọi DeepSeek thật ngoài ý muốn). Dùng **một khoảng trắng** `' '` vì
   `HasApiKey` dùng `IsNullOrWhiteSpace`. Đồng thời phải **clear** mọi key override trước mỗi lần boot (nếu không, ngưỡng của
   case trước rò sang case sau).
6. **PowerShell 5.1:** script phải **ASCII-only** (một dấu `⇒` bị đọc theo ANSI thành `'` làm vỡ cú pháp), `ConvertFrom-Json`
   trả **1 object mảng** nên `@(hàm-JSON)` **lồng mảng** (phải `@($x | ForEach-Object { $_ })`), và `-Headers` bám vào
   `WebSession` (check CSRF phải dùng session mới).

**Vì sao không dùng test xUnit:** nhất quán Phase 2–6 (D17) — Phase 7 verify bằng harness tạm; xUnit để Giai đoạn 8.
Các hàm quyết định (guardrail, chọn Comment↔Attachment, cắt trace, tên file) là `public static` (D19) nên harness gọi trực tiếp
được và test xUnit của Phase 8 sẽ tái dùng đúng những hàm đó.

---

## 2. Tổng hợp kết quả

| Nhóm | Nội dung | Check | Kết quả |
|---|---|---|---|
| **A** | Schema & migration: 2 cột `workspace_members` (+CHECK, partial UQ 1 agent/workspace), `board_columns.is_clarification` (+partial UQ), `agent_runs`, `task_attachments`; `jsonb`/`bytea`; FK RESTRICT; `migrations list` = 6 | **36** | ✅ **36/36 PASS** (chi tiết §2.1) |
| **B** | Hàm thuần: `AgentGuardrails.Evaluate` (biên từng ngưỡng, `null` tokens), `ChooseKind`, cắt `tool_call_trace`, `SafeFileName` (tiếng Việt, `..`, `/`, `\`), tỉ lệ phình base64, `SelectComments`, `CompactToolResults`, registry đặt chỗ/`AdvisoryLockKey` | **48** | ✅ **48/48 PASS** (chi tiết §2.3) |
| **C** | Tool/transport contract: DeepSeek `tools`+`tool_choice="auto"`, parse `tool_calls[]`, message `role="tool"` + `tool_call_id`, `finish_reason="length"`; Tavily body/parse; lỗi ⇒ 502; tool ngoài whitelist; `arguments` hỏng; **key không vào log** | **41** | ✅ **41/41 PASS** (chi tiết §2.3) |
| **D** | Loop end-to-end (DB thật): 202 → `AwaitingApproval` → approve ⇒ comment/attachment thật → undo ⇒ revert; **không** ghi gì trước khi approve; counters khớp trace | **43** | ✅ **43/43 PASS** (chi tiết §2.3) |
| **E** | "Chờ làm rõ" + "Chạy lại": run `AwaitingClarification`, cột `is_clarification` tạo lazy, comment agent `author_id = agent_user_id`, notification Manager; rerun tạo run mới (`previous_run_id`), run cũ **không** đổi | **19** | ✅ **19/19 PASS** (chi tiết §2.3) |
| **F** | Guardrail: hạ `MaxToolCalls`/`MaxRunTokens`/`RunTimeoutSeconds`/`MaxRunLlmCalls` ⇒ đúng `stop_reason` (`ToolLimit`/`TokenBudget`/`TimeLimit`/`InternalError`); **đúng 1** notification; đọc lại run không sinh thêm | **40** | ✅ **40/40 PASS** (chi tiết §2.3) |
| **G** | Failure modes & concurrency: provider lỗi ⇒ `ProviderError`; 2 request cùng task ⇒ 202 + 409; cancel (Running ⇒ Cancelled + 0 notification; đã xong ⇒ 409); `TaskChanged`; reaper đóng run mồ côi | **21** | ✅ **21/21 PASS** (chi tiết §2.3) |
| **H** | HTTP + quyền + disabled: 7 route (401/403/404/405/409), Member 403 ở route ghi, `Agent:Enabled=false` ⇒ 503, download đúng byte + header, `take` clamp | **22** | ✅ **22/22 PASS** (chi tiết §2.3) |
| **I** | Bất biến & không hồi quy: GET không đổi dữ liệu; **`CreateSubtasks` (Phase 4) vẫn đúng** sau khi `ResolveContextAsync` → `ResolveAsync`; `GET .../members` cũ vẫn 200 với field mới; `is_done` cũ vẫn hoạt động; `assigneeIsAiAgent`/`activeAgentRunId` ở **cả 3** đường | **44 + 29** | ✅ **44/44** cho phần **§3** (§2.2) + **29/29** cho phần **§4** (§2.3) |
| **J** | Frontend Vitest: assignee picker (đổi được sang agent), badge theo `status`, nút "Chạy lại" chỉ khi `AwaitingClarification`, disable khi `Running`, `AttachmentList` blob, nhãn notification mới | — | ⬜ **chưa chạy** — thuộc §5 (frontend **không** bị chạm ở §4; baseline 155 test giữ nguyên) |
| **Thật** | **1 lần gọi DeepSeek thật** (R3): `tools`/`tool_calls`/`finish_reason`/`usage` đối chiếu provider thật | **7** | ✅ **7/7 PASS** — 2 tool call, 4 730 token, 4,3 s |

**Tổng §2+§3+§4: 36 + 44 + 270 = 350/350 check PASS.**

---

## 2.1 Nhóm A — Schema & migration (§2 task doc) — ✅ 36/36 PASS

**Cách chạy:** harness tạm ngoài workspace (`%TEMP%\tn-p7-schema\verify-schema.ps1`, đã xoá sau khi chạy), dùng `psql`
(PostgreSQL 18.4 local, DB `TeamNexus`) — **không** gọi API, **không** gọi AI, **0 token**. Mọi test ghi dữ liệu đều nằm trong
`BEGIN … ROLLBACK` (hoặc `SAVEPOINT`) nên DB trở về **đúng baseline** sau khi chạy; test **B12** là bất biến chứng minh điều đó.

**Bối cảnh:** baseline `dotnet ef migrations list` = 5 → sau `Phase7AiAgentSchema` = **6**.

### A. Hình dạng schema (15 check)

| # | Kiểm | Kỳ vọng | Kết quả |
|---|---|---|---|
| A1 | `workspace_members` có `member_type` + `ai_agent_name` | 2 cột | PASS |
| A2 | `member_type` = `character varying(16)`, NOT NULL, default `human` | khớp `04` §3.3 | PASS |
| A3 | `ai_agent_name` = `character varying(120)`, nullable | | PASS |
| A4 | `board_columns.is_clarification` = `boolean`, NOT NULL, default `false` | | PASS |
| A5 | có bảng `agent_runs` + `task_attachments` | | PASS |
| A6 | 26 cột/kiểu/nullable của `agent_runs` khớp **chính xác** thiết kế §2.3 (gồm `tool_call_trace` = `jsonb`) | so chuỗi theo `ordinal_position` | PASS |
| A7 | 9 cột của `task_attachments` khớp §2.4 (`content` = `bytea`, **không** có `updated_at`) | | PASS |
| A8 | 5 CHECK mới/tồn tại: `ck_agent_runs_status`, `ck_agent_runs_stop_reason`, `ck_agent_runs_output_kind`, `ck_workspace_members_member_type` (+ `ck_workspace_members_role` cũ giữ nguyên) | | PASS |
| A9 | partial UQ `uq_workspace_members_ai_agent` = UNIQUE(`workspace_id`) `WHERE member_type='ai_agent'` | | PASS |
| A10 | partial UQ `uq_board_columns_clarification` = UNIQUE(`board_id`) `WHERE is_clarification` | | PASS |
| A11 | partial IX `ix_agent_runs_status` `WHERE status='Running'` (reaper D13) | | PASS |
| A12 | composite IX `(task_id, started_at)` + `(workspace_id, started_at)` | | PASS |
| A13 | IX `ix_task_attachments_task_id` | | PASS |
| A14 | **mọi** FK mới (12 FK trên 2 bảng) đều `confdeltype='r'` (RESTRICT, không cascade) | `04` §7 | PASS |
| A15 | 7 bảng Identity vẫn còn (không bị diff ngoài dự kiến) | | PASS |

### B. Ràng buộc được cưỡng chế thật (21 check)

| # | Kiểm | SQLSTATE / kỳ vọng | Kết quả |
|---|---|---|---|
| B1 | `member_type='Bogus'` | `23514` CHECK | PASS |
| B2 / B2b | agent `ai_agent` **thứ hai** trong cùng workspace | `23505` (partial UQ thật sự bắn, không phải lỗi khác) | PASS |
| B3 | cột `is_clarification` **thứ hai** trong cùng board | `23505` | PASS |
| B3b | nhiều cột **không** phải clarification trong cùng board | vẫn cho phép (không siết quá) | PASS |
| B4 | `agent_runs.status='Bogus'` | `23514` | PASS |
| B5 | `stop_reason='BudgetExceeded'` | `23514` — chứng minh **D4**: không có status/stop_reason gộp ngân sách | PASS |
| B6 | `output_kind='Both'` | `23514` | PASS |
| B7 / B7b / B7c | FK sai: `workspace_id` / `agent_user_id` / `task_id` | `23503` | PASS |
| B8 | `bytea` round-trip: insert `'\x68656c6c6f'` → `octet_length(content)` | = 5 | PASS |
| B9 | `DELETE` task đang có `agent_runs` | `23001` RESTRICT (`fk_agent_runs_tasks_task_id`) — không xoá được run | PASS |
| B10 | insert `workspace_members` **bỏ qua** `member_type` | đọc lại = `human` (DB default) | PASS |
| B11 | insert `board_columns` **bỏ qua** `is_clarification` | đọc lại = `f` | PASS |
| B12 | bất biến "không rò dữ liệu": `workspaces`/`users`/`agent_runs`/`task_attachments` fixture | **0 / 0 / 0 / 0** | PASS |
| B13 | `"__EFMigrationsHistory"` có đúng 6 dòng | = 6 | PASS |
| B14 | migration áp cuối cùng = `20260911145639_Phase7AiAgentSchema` | | PASS |
| B15 | `task_attachments` **không** có `updated_at`/`deleted_at` | D5 (ngoại lệ duy nhất không soft-delete) | PASS |
| B16 | `agent_runs` chỉ có **đúng một** partial index (của reaper) | | PASS |
| B17 | `agent_runs` không lẫn cột/index của bảng khác | | PASS |

### C. Lệnh đã chạy (0 warning / 0 error)

```
dotnet build TeamNexus.sln -m:1 -nr:false                                  # Build succeeded, 0 warning / 0 error
dotnet ef migrations add Phase7AiAgentSchema --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
dotnet ef migrations list --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build   # = 6
```

> ⚠️ **Hai điều kiện môi trường của phiên làm việc này** (đã kiểm chứng bằng thực nghiệm, ghi để lần sau không mất thời gian):
> 1. `dotnet build` mặc định **FAIL với "0 Warning(s) / 0 Error(s)"** (MSBuild worker node dùng named pipe bị chặn trong sandbox).
>    Workaround: luôn thêm **`-m:1 -nr:false`**.
> 2. `dotnet ef` tự build nội bộ vẫn fail ⇒ **bắt buộc pre-build rồi luôn dùng `--no-build`** cho mọi lệnh `dotnet ef`.
> 3. `dotnet ef migrations add --no-build` đọc **DLL đã biên dịch**, không đọc source ⇒ nếu sửa entity/config mà chưa build lại,
>    migration sinh ra **sai một cách im lặng** (đã gặp: migration đầu tiên thiếu `HasCheckConstraint` chỉnh sửa của `ToTable`).
>    Quy trình đúng: **sửa source → build (0/0) → mới `migrations add`** (đã xác nhận bằng kiểm tra timestamp DLL > source).

---

## 2.2 Nhóm I (phần §3 Module Board) — ✅ 44/44 PASS

**Cách chạy:** harness tạm ngoài workspace (`%TEMP%\tn-p7-board\`, đã xoá) chạy trên **API Kestrel thật** (`:5199`) +
**PostgreSQL 18 thật** + **JWT tự ký** (cùng shape `JwtService`, cookie `access_token`, CSRF lấy từ cookie `XSRF-TOKEN`
**sau khi** gắn cookie danh tính — đúng bài học Phase 5 §7). Fixture seed bằng **raw SQL**, mọi bước ghi đều `BEGIN … ROLLBACK`
hoặc hard-delete trong `finally`; check **I-10a** là bất biến chứng minh DB về baseline.

Vì §3 chạy **trước** §4, harness dùng **2 đăng ký tạm trong repo** (đã xoá sau khi chạy; `git diff` của `Program.cs` rỗng):
`TempDbAiAgentResolver` (impl `IAiAgentResolver` theo đúng D1: row `users` email/password null, `lockout_enabled`/`two_factor_enabled`
true, không `user_logins`; cột clarification lazy) và `TempRecordingBoardEventPublisher` (ghi lại event để khẳng định hợp đồng).
Đây chính là shape mà `WorkspaceAiAgentResolver` của §4 phải cài đặt.

| # | Kiểm | Kỳ vọng | Kết quả |
|---|---|---|---|
| I-6a…I-6f | `GET /workspaces/{id}/members`: 200, người thật `memberType="human"`, agent xuất hiện **ở lần gọi đầu**, nằm **cuối** danh sách, gọi lại **không** nhân bản, id ổn định | đúng Q1-A | PASS (6) |
| I-7a/I-7b | Row `users` của agent: `email`/`password_hash` = NULL, `lockout_enabled`/`two_factor_enabled` = true, **0** `user_logins`; membership = `Member` + `ai_agent` | D1 / R8 | PASS (2) |
| I-5a…I-5h | Cột clarification: `isDone+isClarification` ⇒ **400** (cả 2 chiều khi update); cột thứ hai ⇒ vi phạm `uq_board_columns_clarification`; `DELETE` cột clarification (rỗng) ⇒ **409**; cột thường rỗng ⇒ **204**; danh sách cột trả `isClarification` | §3.5 | PASS (8) |
| I-1a…I-1f | Assignee: member ⇒ 201 + `assigneeName`; **user ngoài workspace ⇒ 400** (cả create và update); `null` ⇒ 201; **agent ⇒ 200**; response gắn agent ⇒ `assigneeIsAiAgent=true` | S7 (bắt buộc phát sinh) | PASS (6) |
| I-2a/I-2c/I-2d/I-2e/I-2f | `assigneeIsAiAgent` + `activeAgentRunId` điền ở **cả 3** đường (`GET /workspaces/{id}/boards/{boardId}`, `GET /boards/{id}/tasks`, `GET /tasks/{id}`); task chỉ có run `Completed` ⇒ `null`; task không phải agent ⇒ `false`/`null` | §3.3 (bug im lặng nếu sót) | PASS (5) |
| I-8a…I-8d | Hợp đồng `AgentRunProgress`: hằng số trên `BoardHub`, method trên `IBoardEventPublisher`, forward đúng `BoardHub.AgentRunProgress`, payload đủ **8** field khớp contract frontend §5.1c | D8 | PASS (4) |
| I-11a…I-11d | Member đọc board **200**; Member tạo column **403**; Member **gán được** member cho task **200**; user ngoài workspace **404** | quyền không đổi | PASS (4) |
| I-9a…I-9g | **Không hồi quy Phase 4:** smart-setup proposal 200 → `confirm` **201** + `ai_action_logs` Pending → `approve` **200** (tạo task, membership guard của §3.2 **không** chặn) → approve lần 2 **409** (CAS giữ nguyên) → `undo` **200** (soft-delete task) | R1 | PASS (7) |
| I-10a/I-10b | Sau cleanup: `users`/`workspaces`/`boards`/`agent_runs`/`tasks` fixture = **0**; `__EFMigrationsHistory` = **6** (không drift schema) | D17 | PASS (2) |

**Đo bổ sung:** `dotnet build TeamNexus.sln -m:1 -nr:false` = **0 warning / 0 error**; frontend `oxlint` **0/0**, `tsc -b` **exit 0**,
`npm run build` OK, `npm test` = **30 files / 155 tests PASS** (không đổi — thay đổi DTO chỉ thêm field ở cuối nên frontend hiện tại
không vỡ).

> ⚠️ **Chưa đo được một cách sạch:** số truy vấn EF cho một `GET /boards/{id}` với 30 task / 10 assignee khác nhau.
> Về **code**, `AgentRunLookup` là **1 truy vấn gộp cho cả trang** và `assigneeIsAiAgent` resolve **1 lần cho mỗi assignee khác
> nhau** (theo bất biến "1 agent/workspace"), nên **không** phải N+1 theo số task; nhưng con số chính xác cần một phép đo riêng.
> Việc tối ưu thành 1 truy vấn gộp đã được ghi vào banner §3 của task doc như việc của §4 (khi resolver thật được bật).

---

## 2.3 Nhóm B/C/D/E/F/G/H + 1 lần gọi DeepSeek thật (phần §4 Module Ai) — ✅ 270/270 PASS

**Cách chạy:** harness tạm **ngoài workspace** (`%TEMP%\tn-p7-ai\`, **đã xoá**), API Kestrel thật (`:5197`, chạy trực tiếp
`TeamNexus.Api.dll`), **PostgreSQL 18 thật**, **JWT tự ký** (`JwtService` + `Jwt:SigningKey` từ User Secrets, cookie
`access_token`), CSRF lấy từ cookie `XSRF-TOKEN` **sau khi** gắn cookie danh tính. Fixture seed bằng **raw SQL**; cleanup
bằng SQL hard-delete; check "I" chứng minh **GET không đổi dữ liệu** và DB về baseline.

**Hai bài học mới của harness (ghi lại để lần sau không mất thời gian):**
1. **Env var rỗng ≠ tắt cấu hình.** `.NET` coi `SetEnvironmentVariable(name, "")` là **xoá biến**, nên `DeepSeek__ApiKey=''`
   **không** ép `FakeAiProvider` — User Secrets (`DeepSeek:ApiKey` có key thật) thắng trở lại và harness **gọi DeepSeek thật
   ngoài ý muốn**. Cách đúng: đặt **một khoảng trắng** (`' '`) — `HasApiKey` dùng `IsNullOrWhiteSpace` nên đó đúng là
   trạng thái "chưa cấu hình".
2. **PowerShell 5.1 + UTF-8:** file `.ps1`/SQL tiếng Việt bị đọc theo ANSI ⇒ mọi ký tự ngoài ASCII phải tránh trong script
   (một dấu `⇒` bị dịch thành `'` làm **vỡ cú pháp** cả file). Ngoài ra `ConvertFrom-Json` trả **một object mảng** nên
   `@(hàm-trả-JSON)` **lồng mảng** — phải dùng `@($x | ForEach-Object { $_ })`, và `-Headers` của một lần gọi **bám vào
   `WebSession`** (check CSRF phải dùng session mới).

### Nhóm B/C — hàm thuần + hợp đồng transport (harness .NET, **0 token**)

| # | Nhóm kiểm | Kỳ vọng | Kết quả |
|---|---|---|---|
| B1…B33 | `AgentGuardrails.Evaluate` (đúng ngưỡng ⇒ `null`; vượt 1 đơn vị ⇒ `ToolLimit`/`TokenBudget`/`TimeLimit`; **thứ tự ưu tiên**; `CanCallProvider` biên); `ChooseKind`; `SafeFileName` (tiếng Việt có dấu, `..`, `/`, `\`, rỗng, `..` trần, dài 400 ký tự, suy extension từ contentType); `NormalizeContentType`; cắt trace entry + cap trace; `SelectComments` (cap số lượng/độ dài/ngân sách); `CompactToolResults` (giữ **nguyên** số message ⇒ cặp `assistant/tool_call_id` còn hợp lệ); tất định; `MaxDraftChars × 1.37 = 514 KB ≤ 700 KB` | D19/§7B | **33/33 PASS** |
| B34…B42 | `AgentRunCancellationRegistry`: đặt chỗ theo task (task khác **không** bị chặn), nhả rồi đặt lại được, `TryCancel`/`Take`/`IsRunning`; `AdvisoryLockKey` tất định + khác nhau theo task | D11 (X3) | **9/9 PASS** |
| B43…B46 | `AgentNotificationFactory`: message nêu **đúng ngưỡng bị vượt** + số đã dùng; payload có `runId`/`stopReason`/`thresholds`; 3 loại agent **không** nằm trong whitelist Observer | §4.8d, X5 | **4/4 PASS** |
| B47/B48 | Whitelist **đúng 4 tool**, mỗi tool có JSON Schema `type=object` + `properties` + `required` | §4.4 | **2/2 PASS** |
| C1…C8 | DeepSeek chat: body có `tools` (4 function) + `tool_choice="auto"`, **không** `response_format`; `CompleteAsync` giữ `response_format=json_object` và **không** có `tools`; parse `tool_calls[]` (bỏ call thiếu `name`); `finish_reason` đọc được; assistant echo `tool_calls` đúng `id`/`function.name`; message `role="tool"` mang `tool_call_id` | §4.2, S6 | **8/8 PASS** |
| C11…C15 | `finish_reason="length"` **không** throw (⇒ orchestrator map `TokenBudget`); HTTP 429 ⇒ `AiProviderException(502)`; JSON hỏng ⇒ 502; timeout ⇒ 502; lượt rỗng (không content, không tool) ⇒ 502 | §4.2 | **5/5 PASS** |
| C16/C24 | **Key không xuất hiện trong bất kỳ log line nào** (DeepSeek + Tavily) | §4.2/§4.3 | **2/2 PASS** |
| C17…C23 | Tavily: parse `results[]` (`content`, fallback `snippet`, title rỗng ⇒ dùng URL); clamp `max_results` theo `Agent:WebSearchMaxResults`; `Bearer` ⇒ header + **không** `api_key` trong body; `Body` ⇒ `api_key` trong body + **không** header; `search_depth`; HTTP 500 ⇒ 502; JSON hỏng ⇒ **mảng rỗng + warning, không throw** | §4.3, R2 | **7/7 PASS** |
| C25…C32 | Registry: tên ngoài whitelist ⇒ `{"error":"Unknown tool …"}` (kèm `isError`); `arguments` hỏng/không phải object ⇒ error JSON; tool ném exception ⇒ error JSON, run **không** chết; `DraftOutput` ⇒ `StopLoop`/`DraftProduced`; draft vượt `MaxDraftChars` ⇒ **từ chối, không nhận**; `RequestClarification` ⇒ `StopLoop`/`QuestionAsked`; câu hỏi quá dài ⇒ từ chối | §4.4, R3 | **8/8 PASS** |
| C33…C38 | `FakeAiProvider`: nhánh agent theo sentinel; **token count xác định** (điều kiện để test được `TokenBudget`); kịch bản mặc định bắt đầu bằng `SearchSystemData(board)`; prompt không phải agent ⇒ **từ chối** (không im lặng sai); **nhánh Phase 3 (proposal) và Phase 5 (findings) không đổi** | D16, S10 | **6/6 PASS** |
| C39…C41 | `FAKE:ATTACH` kết thúc bằng draft **dài + có fileName** và `ChooseKind` ⇒ `Attachment`; `FAKE:CLARIFY` hỏi ngay lượt đầu; **sentinel nằm trong tool result KHÔNG được cướp kịch bản** (regression của bug #2) | §4.5 | **3/3 PASS** |

### Nhóm D/E/H/I — vòng lặp end-to-end, "Chờ làm rõ", HTTP, bất biến

| # | Nhóm kiểm | Kỳ vọng | Kết quả |
|---|---|---|---|
| I1…I8 | `GET /members` 200 + người thật `human`; agent tạo **lazy** ở lần gọi đầu, role `Member`; row `users` agent: password/email NULL, lockout+2FA true, **0** `user_logins`; `PUT` task gán agent ⇒ 200 + `assigneeIsAiAgent=true` | §3.6/D1/R8 | **8/8 PASS** |
| D1…D11 | `POST /agent-runs` ⇒ **202** (body là row `Running` + `agentDisplayName`); run → `AwaitingApproval`/`DraftProduced`/`outputKind=Comment`; `aiActionLogId` ≠ null; `tool_call_count == số entry trace` = **3** `[SearchSystemData,WebSearch,DraftOutput]`; `prompt+completion == totalTokens > 0` | §4.8/§7D | **11/11 PASS** |
| D12…D22 | **Trước Approve**: 0 comment, 0 attachment, action chưa `Approved`; log `entity_type='Task'`, `requested_by_user_id` = **agent**; approve **200** → comment có `author_id` = agent; approve lần 2 **409** (CAS Phase 4 còn nguyên); `activity_logs` có `CommentAdded`; undo **200** → comment **soft-delete** | D9/R1 | **11/11 PASS** |
| D23…D39 | `FAKE:ATTACH` ⇒ `outputKind=Attachment`; trước Approve **0** row; `after_snapshot` **9 991 ký tự** (≤ envelope); approve → row có `size_bytes == octet_length(content)`, `file_name = bao-cao-ai.md` (slug ASCII), `content_type` từ model, `created_by` = **agent**, `source_run_id` = run; `GET /attachments` 200 + `createdByName` = tên agent; `download` 200 + `Content-Length` khớp + `Content-Disposition` ASCII; undo → **hard delete** (0 row) | §4.7, D5, R4 | **17/17 PASS** |
| D40…D43 | Tool ngoài whitelist và `arguments` hỏng (end-to-end qua DB thật) ⇒ run **vẫn hoàn thành bình thường**, trace entry có `isError=true` | §4.4 | **4/4 PASS** |
| E1…E11 | `FAKE:CLARIFY` ⇒ `AwaitingClarification`/`QuestionAsked`; `clarificationQuestion` ≠ null; comment do **agent** đăng + run link `clarification_comment_id`; cột clarification tạo **lazy** ở `max(position)+1`; task **đã** sang cột đó; **đúng 1** cột (partial UQ); notification `AgentAwaitingClarification` **1 row** cho Manager + payload trỏ đúng run | D3/D10 | **11/11 PASS** |
| E12…E19 | Rerun khi **chưa có câu trả lời** ⇒ **400** `{error}`; Manager trả lời bằng comment ⇒ rerun **202 + run MỚI** (`previousRunId` = run cũ), rerun đọc được câu trả lời + câu hỏi cũ; **run cũ byte-identical** (append-only) | D14 | **8/8 PASS** |
| H1…H16 | 7 route không auth ⇒ **401** (7/7); Member gọi route ghi ⇒ **403**; POST thiếu CSRF (session **mới**) ⇒ **403**; Member **đọc được** ⇒ 200; task/run lạ ⇒ 404 `{error}`; sai method ⇒ 405; task chưa gán agent ⇒ 400 "not assigned to the AI Agent"; cancel run đã xong ⇒ 409; `take` clamp `0⇒20`, `999⇒50`, `abc⇒` mặc định; sort `startedAt DESC` | §4.9 | **16/16 PASS** |
| I9…I29 | Approve lần 2 (Phase 4) 409; `assigneeIsAiAgent`/`activeAgentRunId` điền ở **cả 3** đường + `isClarification` có trên cột; **GET không đổi** `tasks`/`labels`/`agent_runs`/`task_attachments`/`ai_action_logs`/`activity_logs`; **`CreateSubtasks` (Phase 4) không hồi quy**: confirm 201 → approve 200 → approve 2 **409** → undo 200 (task soft-delete); **không** log nào có `entity_type` ngoài `Board`/`Task` | §7I, R1 | **21/21 PASS** |

### Nhóm F — guardrail (mỗi ngưỡng một lần boot)

| Case | Cấu hình | Kỳ vọng | Kết quả |
|---|---|---|---|
| F1 | `Agent:MaxToolCalls=2` (kịch bản cần 3 tool) | `Failed` + `ToolLimit` + **đúng 1** notification + `notification_sent=true`; đọc lại run **không** sinh thêm; 0 ghi nghiệp vụ; retry ngay **202** (lock đã nhả) | PASS (10/10) |
| F2 | `Agent:MaxRunTokens=10` (fake trả token xác định) | `Failed` + `TokenBudget` + 1 notification | PASS (10/10) |
| F3 | `Agent:RunTimeoutSeconds=1` + task `FAKE:SLOW` (delay 3 s thật) | `Failed` + `TimeLimit` (timeout **wall-clock thật**) | PASS (10/10) |
| F4 | `Agent:MaxRunLlmCalls=1` | `Failed` + `InternalError` (không có stop reason riêng cho lượt gọi model) | PASS (10/10) |
| H17…H22 | `Agent:Enabled=false` | **503** ở **cả 3** route ghi (`{error}`), route đọc **200**, **0** run được tạo | PASS (6/6) |

### Nhóm G — failure modes, reaper, chạy chồng, cancel, task đổi giữa run

| # | Kiểm | Kỳ vọng | Kết quả |
|---|---|---|---|
| G1/G2 | Seed run `Running` cũ (`started_at - 1 day`) rồi boot ⇒ `AgentRunReaper` | `Failed`/`InternalError` + `finished_at`; **không** notification (sau restart không có ai để báo) | PASS (2) |
| G3…G8 | Provider thật trỏ vào port chết ⇒ `Failed`/`ProviderError`; 0 comment/attachment/log; **đúng 1** notification; lock nhả (retry **202**) | §7G | PASS (6) |
| G9/G10 | **2 request đồng thời** (2 process riêng) cùng task ⇒ **1×202 + 1×409**; chỉ **1** row `agent_runs` | D11 (X3) | PASS (2) |
| G11…G16 | `POST /cancel` khi `Running` ⇒ **200** + response **đã** là `Cancelled`; **0** notification; cancel lần 2 ⇒ **409**; sau đó row không kẹt `Running` và **không** bị loop ghi đè | §7G, D4 | PASS (6) |
| G17…G20 | Task đổi người thực hiện **giữa run** (task `FAKE:SLOW FAKE:CLARIFY`) ⇒ `Failed`/`TaskChanged`; **không** comment, **không** action log; **không** run nào kẹt `Running` | §4.8e | PASS (4) |
| G21 | Run mới chạy được ngay sau run thất bại (đặt chỗ + lock đã nhả) | §7G | PASS (1) |

### 1 lần gọi DeepSeek **thật** (R3) — đo được

| Chỉ số | Giá trị đo thật |
|---|---|
| Kết quả | `AwaitingApproval` / `DraftProduced` / `outputKind=Comment` |
| Tool call | **2** — `[SearchSystemData, DraftOutput]` (model tự bỏ `WebSearch`, đúng như prompt "chỉ khi cần") |
| Lượt gọi model | **2** (không phải N+1) |
| Token | prompt **4 135** + completion **595** = **4 730** |
| Tool lỗi | 0 |
| Wall-clock | **4,3 s** |
| Accountability | tạo `ai_action_logs` Pending → reject được **200** |

⇒ Hợp đồng function-calling của DeepSeek (`tools`/`tool_calls`/`finish_reason`) đã được xác nhận bằng **provider thật**,
không chỉ bằng stub — đây chính là nhóm bắt được bug #1 dưới đây.

---

## 2.4 Nhóm A–J của §7 (đợt verify **tổng**, sau khi có key Tavily thật) — ✅ 291/291 PASS

**Cách chạy (đợt cuối, sau §4 + key Tavily):** harness ngoài workspace `%TEMP%\tn-p7-h\` (**đã xoá**), API chạy trực tiếp
`TeamNexus.Api.dll` trên `:5197`, **PostgreSQL 18 thật**, JWT tự ký, fixture raw SQL, cleanup SQL hard-delete.
**8 lần boot**: mặc định (fake AI + **Tavily thật**) · `Agent:Enabled=false` · 4 ngưỡng guardrail hạ thấp · provider hỏng ·
**DeepSeek thật + Tavily thật**.

| Nhóm §7 | Nội dung | Check | Kết quả |
|---|---|---|---|
| **B/C** | Hàm thuần (guardrail biên + thứ tự ưu tiên, `ChooseKind`, `SafeFileName`, cắt trace, `SelectComments`, `CompactToolResults`, registry đặt chỗ, khoá advisory) + hợp đồng transport với stub `HttpMessageHandler` (DeepSeek `tools`/`tool_choice`/parse `tool_calls`/`role=tool`/`finish_reason=length`; Tavily Bearer/Body/parse/500/JSON hỏng; registry unknown-tool/arguments hỏng/tool ném exception/draft quá dài; 3 nhánh fake + regression "sentinel trong tool result") | **89** | ✅ |
| **C-real** | **Tavily THẬT** qua tool `WebSearch` của agent: trace entry không lỗi, payload chứa URL thật và **không** chứa `example.test` (dấu hiệu fake), log khởi động ghi `webSearch=TavilyWebSearchProvider`, **key không xuất hiện trong log** | **5** | ✅ |
| **A** | Schema (re-check tổng): `migrations=6`, `status` rộng 32, `member_type`+CHECK, 3 partial UQ, 3 CHECK của `agent_runs`, `jsonb`/`bytea`, FK RESTRICT, index reaper; cưỡng chế thật (`member_type='Bogus'` ⇒ 23514, `status='Bogus'` ⇒ 23514, agent thứ 2 ⇒ 23505, cột clarification thứ 2 ⇒ 23505) | **11** | ✅ |
| **D** | Loop end-to-end: 202 → `AwaitingApproval`; `tool_call_count == trace`; counters khớp; **trước Approve không ghi gì**; approve ⇒ comment/tệp thật (đúng author = agent, `size_bytes == octet_length`, tên ASCII, `source_run_id`, header tải file); undo ⇒ comment **soft** delete / tệp **hard** delete; tool ngoài whitelist & `arguments` hỏng ⇒ run vẫn hoàn thành + trace `isError=true` | **43** | ✅ |
| **E** | "Chờ làm rõ": `AwaitingClarification`/`QuestionAsked`, comment agent, cột clarification lazy ở `max(position)+1`, 1 cột/board, notification Manager + payload `runId`; rerun khi chưa trả lời ⇒ 400; sau khi trả lời ⇒ run **mới** (`previousRunId`, `resolutionCommentContent`, `previousQuestion`) và run cũ **byte-identical** | **19** | ✅ |
| **F** | Guardrail: `MaxToolCalls=2` ⇒ `ToolLimit`; `MaxRunTokens=10` ⇒ `TokenBudget`; `RunTimeoutSeconds=1` + `FAKE:SLOW` ⇒ `TimeLimit` (wall-clock thật); `MaxRunLlmCalls=1` ⇒ `InternalError`; mỗi case: **đúng 1** notification `AgentRunFailed`, `notification_sent=true`, đọc lại **không** thêm, 0 ghi nghiệp vụ, lock đã nhả (retry 202), số tool-call ≤ ngưỡng+1 | **44** | ✅ |
| **G** | Reaper đóng run mồ côi (`Failed`/`InternalError` + `finished_at`, **không** notification); provider lỗi ⇒ `ProviderError` + 0 ghi; **2 request đồng thời** (2 process) ⇒ **1×202 + 1×409**, chỉ 1 row; cancel ⇒ 200 + `Cancelled` ngay + **0** notification, cancel lần 2 ⇒ 409, không kẹt `Running`, không bị ghi đè; task đổi giữa run ⇒ `TaskChanged` + không ghi gì; run mới chạy được ngay sau run thất bại | **21** | ✅ |
| **H** | 7 route: 401 (7/7), 403 (Member ở route ghi + thiếu CSRF với **session mới**), 404 + `{error}`, 405, 400 "not assigned to the AI Agent", 409, `take` clamp/fallback, sort DESC; `Agent:Enabled=false` ⇒ **503 cả 3 route ghi**, route đọc 200, 0 run được tạo | **22** | ✅ |
| **I** | Identity (agent lazy, role Member, login-proof, 1 agent/workspace) + `assigneeIsAiAgent` ở **cả 3** đường trả task + `isClarification` trên cột + **GET không đổi dữ liệu** (6 bảng) + **không hồi quy Phase 4** (`CreateSubtasks` confirm 201 → approve 200 → approve 2 **409** → undo 200) + không có `entity_type` lạ | **29** (+44 của §2.2) | ✅ |
| **Thật** | 1 lượt **DeepSeek thật** cùng boot **Tavily thật**: `R0` cả 2 provider thật đã đăng ký, terminal state, tokens > 0, ≥1 tool call, không lỗi tool, draft vào Accountability và reject được | **8** | ✅ |

**Tổng §7 = 291** (89 + 5 + 11 + 43 + 19 + 44 + 21 + 22 + 29 + 8), cộng §2.1 (36) và §2.2 (44) ⇒ **371 check PASS** cho cả giai đoạn.

**Kết quả 1 lượt DeepSeek thật (đo được):** `AwaitingApproval`/`DraftProduced`/`Comment`; **3** tool call
`[SearchSystemData, SearchSystemData, DraftOutput]`; 2 lượt gọi model (model tự bỏ `WebSearch` — hợp lệ);
**5 114 token** (prompt 4 356 + completion 758); **5,0 s**; 0 tool lỗi; draft reject **200**.

**Key Tavily:** đã lưu bằng `dotnet user-secrets set "Tavily:ApiKey"` (58 ký tự, **không** có trong file tracked — đã kiểm
`git grep`). Probe gọi thật cho thấy **cả hai biến thể đều được Tavily chấp nhận** (`Bearer` mặc định: OK 2 kết quả;
`Body`: OK 2 kết quả) ⇒ giữ mặc định `Bearer`; `Tavily:AuthMode` vẫn là van an toàn đổi bằng config nếu sau này Tavily đổi
hợp đồng. **Không** còn hạn chế "chưa xác nhận Tavily" (mục 7 của §5 đã đóng).

**Sáu bài học harness (đã ghi vào §1 + README module):** env var rỗng = xoá biến (phải dùng `' '`); clear override trước mỗi
boot; script PowerShell **ASCII-only**; `ConvertFrom-Json` trả **1 object mảng** (`.Count` trên `PSCustomObject` là `$null` ⇒
phải giữ mảng bằng dấu `,` và **không** pipe trực tiếp từ hàm); `-Headers` bám vào `WebSession` (CSRF phải dùng session mới);
`__EFMigrationsHistory` là **case-sensitive** (phải quote).

---

## 3. Số liệu đo thật

| Chỉ số | Baseline (trước Giai đoạn 7) | Sau §2 (schema) | Sau §3 (Board) | Sau §4 (Module Ai) |
|---|---|---|---|---|
| Migration | **5** | **6** (`Phase7AiAgentSchema`) | **6** (không đổi schema; §3 chỉ code) | **6** (không sinh migration — §4 thuần code) |
| `dotnet build TeamNexus.sln` | 0 warning / 0 error | **0 warning / 0 error** | **0 warning / 0 error** | **0 warning / 0 error** |
| Check nhóm A (schema) | — | **36/36 PASS** | **36/36 PASS** | **36/36 PASS** |
| Check nhóm I (§3 Board) | — | — | **44/44 PASS** | **44/44 PASS** (không hồi quy) |
| Check §4 (B/C/D/E/F/G/H + gọi thật) | — | — | — | **270/270 PASS** (89 + 107 + 46 + 21 + 7) |
| Frontend tests | **30 files / 155 tests PASS** | (chưa đổi) | **30 files / 155 tests PASS** | **30 files / 155 tests PASS** (frontend không bị chạm) |
| `oxlint` / `tsc -b` / `npm run build` | 0/0 · exit 0 · OK | (chưa đổi) | **0/0 · exit 0 · OK** | (không chạy lại — không có thay đổi frontend) |
| Thời gian 1 lượt chạy (fake provider, kịch bản 3 tool) | — | chờ §4 | chờ §4 | **< 1 s** (mỗi `SubmitChanges` dưới 10 ms; nhóm D/E hoàn tất tức thì) |
| Token dùng cho 1 lượt chạy thật (DeepSeek, 1 lần đối chiếu) | — | chờ §4 | chờ §4 | **4 730** (prompt 4 135 + completion 595), 2 lượt gọi model, **4,3 s** |
| Kích thước `after_snapshot` khi attachment (base64, cap 512 KB) | — | chờ §4 | chờ §4 | **9 991 ký tự** cho 1 tệp markdown ~7 KB ⇒ tỉ lệ phình base64 nằm trong envelope đã tính |
| Row `task_attachments` sau approve + undo | — | chờ §4 | chờ §4 | approve ⇒ **1** row `size_bytes == octet_length(content)`; undo ⇒ **0** row (hard delete) |
| Row DB sau cleanup harness | — | — | — | `users=1` (chỉ user dev), `agent_runs=0`, `task_attachments=0`, `harness users/ws/tasks = 0`; `__EFMigrationsHistory` = **6** |
| Check §7 tổng (A–I + gọi thật DeepSeek & Tavily) | — | — | — | **291/291 PASS** (§2.4); cộng dồn cả giai đoạn **371/371** |
| Token 1 lượt chạy thật thứ 2 (DeepSeek, cùng boot Tavily thật) | — | — | — | **5 114** (prompt 4 356 + completion 758), 2 lượt gọi model, 3 tool call, **5,0 s** |
| Tavily: biến thể auth được server thật chấp nhận | — | — | — | **cả `Bearer` (mặc định) và `Body`** ⇒ giữ `Bearer`; key **không** vào log |

---

## 4. Bug & phát hiện thật

| # | Khi nào | Triệu chứng | Nguyên nhân gốc | Cách sửa | Verify lại |
|---|---|---|---|---|---|
| A-bug-1 | Viết `AgentRunConfiguration`/`WorkspaceMemberConfiguration` (§2) | `build` fail `CS1929`: `'CheckConstraintBuilder' does not contain a definition for 'HasCheckConstraint'` khi chain 2 CHECK trong cùng `ToTable(name, t => t.HasCheckConstraint(...).HasCheckConstraint(...))` | `HasCheckConstraint` trả về `CheckConstraintBuilder`, **không** trả về `EntityTypeBuilder` ⇒ không chain được (API mới của EF Core 10) | Đổi sang `ToTable("x", table => { table.HasCheckConstraint(...); table.HasCheckConstraint(...); })` (đúng khuôn `AiObserverRunConfiguration` sẵn có) | `build` 0/0 + A8 (5 CHECK tồn tại trong DB) |
| A-bug-2 | Viết partial index cho 2 cột bool | Thử `HasIndex(...).HasFilter(r => r.IsClarification)` (overload `lambda`) ⇒ compile fail `CS1660` | EF Core **10.0.8** (bản đang dùng) chưa có overload nhận lambda; chỉ có `HasFilter(string)` | Dùng `HasFilter("\"is_clarification\"")` / `HasFilter("\"member_type\" = 'ai_agent'")` + `HasDatabaseName(...)` để tên index cố định | A9/A10/A11 (đọc `pg_indexes.indexdef`, thấy đúng `WHERE`) |
| A-bug-3 | Đổi `builder.HasCheckConstraint(...)` sang `ToTable(...)` **sau khi đã build**, rồi chạy `migrations add --no-build` | Migration sinh ra **thiếu** `ck_workspace_members_member_type`; sau đó `database update` báo `PendingModelChangesWarning` rất khó lần ra | `dotnet ef --no-build` đọc **DLL đã biên dịch** (bin), không đọc source; build bị coi là "up-to-date" nên DLL cũ vẫn nằm trong `bin` | Xoá file migration vừa sinh + `git checkout` snapshot, **build sạch (xoá `obj`/`bin` của Api)** rồi `migrations add` lại; thêm kiểm tra `timestamp(DLL) > timestamp(source)` vào quy trình | Migration mới chứa đủ CHECK (A8) và `database update` áp thành công (B13/B14) |
| A-bug-4 | Chẩn đoán `PendingModelChangesWarning` bằng harness so model | Mọi so sánh đều ra **351 differences** dù snapshot đúng ⇒ hướng điều tra sai | Harness dựng `TeamNexusDbContext` **thiếu** `UseSnakeCaseNamingConvention()` + `UseNpgsql(..., MigrationsAssembly)` và dùng `context.Model` thay vì `IDesignTimeModel` ⇒ so hai model khác cấu hình | Dựng options **đúng như `Program.cs`**; sau khi sửa: `DIAG differences=0` (khẳng định model khớp snapshot) | `DIAG differences=0` trước khi `database update` |
| A-bug-5 | Kiểm tra `pg_constraint` cho FK | Test dự đoán `DELETE workspace` bị chặn bởi FK của `agent_runs` ⇒ không bao giờ thấy | `DELETE workspace` đã bị chặn **trước** bởi FK cũ `fk_boards_workspaces_workspace_id` (`23001`, không phải `23503`) | Đổi test B9 sang đúng mục tiêu: `DELETE` **task** đang có `agent_run` ⇒ `23001` + đúng tên constraint `fk_agent_runs_tasks_task_id` | B9 PASS |
| **I-bug-1** | Verify §3, seed một run `status='AwaitingClarification'` | `23503`/`22001`: `value too long for type character varying(16)` — **không thể ghi run "Chờ làm rõ"** | `AgentRunConfiguration` khai `Status` `HasMaxLength(16)`, nhưng `AwaitingClarification` dài **21** ký tự (CHECK constraint cho phép, cột thì không) | `HasMaxLength(32)` + comment nêu rõ vì sao không được siết lại; **sinh lại** `Phase7AiAgentSchema` (xa schema Phase-7 về trạng thái pre-Phase-7 rồi `database update` lại, giữ đúng 1 migration cho giai đoạn) | I-2a (run Running đọc được) + `information_schema` cho `status = 32`; `__EFMigrationsHistory` = 6 |
| **I-bug-2** | Verify §3, đọc task không có agent run | `activeAgentRunId` trả `"00000000-0000-0000-0000-000000000000"` thay vì `null` cho **mọi** task không có run | `Dictionary<Guid, Guid>.GetValueOrDefault(taskId)` trả `Guid.Empty` (không phải `null`) khi thiếu key — `Guid` là value type | Đổi sang `TryGetValue(...) ? runId : null` ở **cả** `TaskService` (list + detail) và `BoardService` (đường board) + comment cảnh báo | I-2c, I-2f (đều `null`), I-2d/I-2e giữ đúng id run thật |
| **AI-bug-1** | Verify §4 nhóm **C** (stub `HttpMessageHandler`) — và sẽ **không** phát hiện được bằng nhóm D/E vì `FakeAiProvider` đi đường khác | Mọi lượt function-calling của DeepSeek bị coi là "lượt rỗng" ⇒ `InternalError` "Model trả về lượt trống", agent **không bao giờ** gọi được tool nào | Response DeepSeek được đọc bằng `JsonSerializerDefaults.Web` (camelCase + case-insensitive) nên khớp `toolCalls`/`finishReason`, **không** khớp `tool_calls`/`finish_reason` của wire; hai field này thiếu `[JsonPropertyName]` | Thêm `[property: JsonPropertyName("tool_calls")]` / `("finish_reason")` + comment nêu rõ vì sao bắt buộc phải map tay | C7/C8 (parse `tool_calls[]` + `finish_reason`) và **1 lần gọi DeepSeek thật** (2 tool call, `DraftProduced`) |
| **AI-bug-2** | Verify §4 nhóm **D/E**: task `FAKE:ATTACH` lại ra `outputKind=Comment`, trace chỉ có 2 entry `[SearchSystemData,DraftOutput]` | `FakeAiProvider` chọn **sai nhánh kịch bản**: run "bình thường" bị chuyển sang nhánh **hỏi lại** (comment "Cảm ơn anh/chị…") | `HasSentinel(...)` quét **cả nội dung tool result**; `SearchSystemData` trả về tiêu đề task của board, nên tiêu đề chứa `FAKE:*` của **task khác** đã "cướp" kịch bản của run này (dữ liệu sống do người dùng nhập điều khiển provider offline) | Sentinel **chỉ** đọc từ system prompt + message `role="user"` + comment giải thích; thêm check **C41** khoá regression | C39/C40/C41 + D23 (`outputKind=Attachment`) |
| **AI-bug-3** | Verify §4 nhóm **H**: `POST /agent-runs/{id}/cancel` khi `Agent:Enabled=false` trả **404** thay vì **503** (bug phát hiện do **đối chiếu spec**: bảng §4.9 không ghi 503 cho route này, nhưng §7H yêu cầu 503 cho **cả 3** route ghi) | Cổng `Agent:Enabled` chỉ có ở `StartAsync`; `CancelAsync` load run trước nên run id lạ ⇒ 404, còn run thật thì bị "huỷ" trong khi tính năng đang tắt | Thiếu guard (lỗ hổng spec chứ không phải lỗi biên dịch) | Thêm `AgentDisabledException` **đầu** `CancelAsync` (trước khi load run: tính năng tắt không được tiết lộ run có tồn tại) + cập nhật bảng §4.9 của task doc | H20 (503), H21 (route đọc vẫn 200), H22 (0 run được tạo) |

> **Phát hiện trước khi code (đã ghi vào roadmap + phase-7 doc §0.1):**
> 1. UI Kanban **chưa có** đường đổi người thực hiện (`TaskDetailModal` chỉ hiển thị `assigneeName`; `KanbanColumn` quick-add
>    không có assignee) ⇒ ô roadmap "gán task cho Agent qua đúng UI assignee hiện có" **không thể** hoàn thành nếu không thêm dropdown.
> 2. `TaskService.CreateTask/UpdateTask` chỉ kiểm `users.AnyAsync` — **không** kiểm membership ⇒ phải siết lại; nếu không, agent
>    là một `users` row mới sẽ trở thành đường vào cho lỗi "gán task cho người ngoài workspace".
> 3. `AiActionService.ResolveContextAsync` chỉ chấp nhận `entity_type = 'Board'` ⇒ 2 applier mới cần nhánh `Task`.
> 4. `FakeAiProvider`/marker pattern + `ai_observer_runs.status = Skipped` là tiền lệ phải theo (verify offline; tách `status` khỏi `stop_reason`).

---

## 5. Hạn chế đã biết (chấp nhận có chủ ý)

1. **Không** stream token — chỉ broadcast **thay đổi trạng thái** qua `AgentRunProgress`.
2. Chạy **in-process**, không queue ⇒ app free-tier sleep giữa run làm mất run đó; `AgentRunReaper` đóng lại và nút "Chạy lại" là
   lưới an toàn. Không dùng Hangfire/Redis (`02` §5).
3. `POST /cancel` chỉ tác dụng **trong cùng instance** đang chạy run đó (không có registry phân tán).
4. **Không** prune `agent_runs`/`task_attachments`; `task_attachments` là bảng duy nhất không soft-delete ⇒ file đã Undo
   **không thể** khôi phục.
5. Task do agent thực hiện **sẽ** xuất hiện trong `byAssignee` của báo cáo và trong tín hiệu `Overload` — **hành vi mong muốn**.
6. Chưa có test xUnit (Giai đoạn 8); chưa có agent tự chạy lại khi có comment mới (chủ ý — "Chạy lại" thủ công).
7. **(§4, đã ĐÓNG ở §7)** Tavily: biến thể auth là config (`Tavily:AuthMode`, mặc định `Bearer`) ⇒ chốt được bằng thực nghiệm
   không cần sửa code. **Đợt §7 đã gọi Tavily THẬT**: cả `Bearer` và `Body` đều được server chấp nhận (2 kết quả/lần), dữ liệu
   thật về qua tool `WebSearch` (nhóm C-real), key **không** xuất hiện trong log. Giữ `Bearer`; `Body` là phương án dự phòng.
8. **(§4)** Run **giữ** `AwaitingApproval` sau khi con người Approve/Reject: phán quyết nằm ở `ai_action_logs`
   (`approved`/`rejected`/`undone`) và UI đọc qua `agentRun.aiActionLogId`. Hệ quả: `status = Completed` **chưa** được set ở
   giai đoạn này (giá trị enum dành cho bước đồng bộ run↔action sau này), và `activeAgentRunId` của task vẫn trỏ vào run đang
   "chờ duyệt" sau khi đã duyệt — card Kanban hiển thị theo trạng thái run, còn trạng thái *quyết định* thì đọc từ action log.
9. **(§4)** `after_snapshot` của `PostAttachment` chứa **base64** (cap 512 KB ⇒ row jsonb ≤ ~700 KB). Vì vậy
   `GET /api/ai-actions/{logId}` (drawer duyệt sẵn có của Phase 4) sẽ trả về payload đó — frontend **không** cần render nó,
   nhưng đây là chi phí đã biết khi duyệt một attachment.
10. **(§4)** Đặt chỗ chống chồng theo task là **trong process**; đa instance vẫn dựa vào `pg_try_advisory_lock` ⇒ khi lock
    không lấy được ở instance khác, request đã trả 202 sẽ thấy run `Failed`/`InternalError` (đã ghi rõ trong code). Chạy 2
    instance là ngoài phạm vi giai đoạn này (D15).
11. **(§3)** Agent là "pseudo-member" tạo **lazy ở lần đọc danh sách thành viên đầu tiên** của workspace (điều chỉnh có chủ ý so với
    câu chữ §3.4 — xem banner §3 của task doc). Hệ quả: một workspace mà chưa ai mở dropdown assignee thì **chưa** có row `ai_agent`;
    mở danh sách lần đầu ⇒ có thêm 1 thành viên cuối danh sách. Agent **không** xuất hiện trong `notifications` (role `Member`) và
    sẽ xuất hiện trong prompt Smart Setup như một thành viên bình thường.
12. **(§3)** `assigneeIsAiAgent` resolve **1 lần cho mỗi `AssigneeId` khác nhau trong trang** (không phải N+1 theo task, nhưng chưa
    phải 1 truy vấn gộp). Tối ưu thành 1 truy vấn gộp vẫn là việc **còn để ngỏ** (đã ghi ở banner §3).
13. **(§3)** `WorkspaceMemberService.GetMembersAsync` gọi `EnsureAgentAsync` best-effort: nếu tạo agent thất bại (DB lỗi/khoá),
    endpoint vẫn trả **200** với danh sách không có agent và chỉ ghi `LogWarning` (fail-soft, cùng triết lý `IActivityLogWriter`).
14. **(§4)** Nhóm **J** (frontend) của §7 **chưa** chạy: §4 không chạm frontend nên baseline 155 test giữ nguyên; nhóm J thuộc §5.
