# Báo cáo Kiểm thử Giai đoạn 7 — AI Agent Executor

> **Phạm vi:** Giai đoạn 7 — danh tính AI Agent (pseudo-member), vòng lặp tool-calling, trạng thái "Chờ làm rõ" + "Chạy lại",
> kết quả đi qua Accountability Layer (comment/attachment), guardrail ngân sách, `agent_runs` + broadcast SignalR.
> **Không** gồm: streaming token (non-goal), prune retention (optional), test xUnit (để Giai đoạn 8).
>
> **Kết luận: 🔄 CHƯA CHẠY — khung báo cáo.** Giai đoạn 7 mới ở bước chốt kế hoạch:
> `tasks/phase-7-ai-agent-executor.md` (§0 quyết định D1–D20) và `04-database-design.md` §3.8. Chưa có dòng code nào của
> giai đoạn này trong `src/` hay `frontend/src/`.
>
> **Baseline trước khi bắt đầu (đã ghi nhận):** backend Phase 1–6 xong; frontend **30 test files / 155 tests PASS**,
> `oxlint` 0/0, `tsc -b` sạch; `dotnet ef migrations list` = **5**.

---

## 1. Môi trường & phương pháp (dự kiến)

| Thành phần | Chi tiết |
|---|---|
| Runtime | .NET SDK 10 (`net10.0`), ASP.NET Core + Kestrel |
| Database | PostgreSQL local, DB `TeamNexus` (connection string trong User Secrets) |
| Xác thực harness | JWT HS256 tự ký (`Jwt:SigningKey` trong User Secrets), cookie `access_token` (`Issuer=TeamNexus`, `Audience=TeamNexus.Web`) |
| AI | `FakeAiProvider` nhánh thứ 3 (marker `{"agent":"executor"}`) + `FakeWebSearchProvider` + stub `HttpMessageHandler` ⇒ **không gọi DeepSeek/Tavily, 0 token** |
| API | Boot thật `dotnet run --project src/TeamNexus.Api --no-build --urls http://127.0.0.1:<cổng tạm>` (dự kiến 3 instance: mặc định / `Agent__Enabled=false` / ngưỡng hạ thấp) |
| Fixture | Seed bằng **raw SQL** (tránh `SaveChanges` tự stamp `created_at`), hard-delete trong `finally`, DB về baseline |
| Quy ước | Harness ngoài workspace (`%TEMP%\tn-p9-*`), xoá sau khi chạy, **không** commit (quyết định D17) |

**Bốn điều kiện tiên quyết (bài học Phase 5 — bắt buộc):**

1. Lấy lại token CSRF **sau** khi gắn JWT cookie (token ẩn danh 155 ký tự ≠ token đã bind identity 198 ký tự; echo token cũ ⇒ 403).
2. Seed fixture bằng raw SQL, **không** qua EF (EF stamp `created_at`/`updated_at` ⇒ task "cũ" thành "vừa tạo").
3. Boot API rồi stop trong **< 60s** (`Observer:StartupDelaySeconds`) hoặc `Observer__Enabled=false` để Observer không quét DB dev.
4. `AgentRunReaper` là `IHostedService` chạy ngay lúc boot ⇒ seed run `Running` mồ côi **trước** khi boot, hoặc gọi trực tiếp
   method dọn (khuyến nghị: cả hai — 1 case gọi trực tiếp + 1 case boot thật).

**Vì sao không dùng test xUnit:** nhất quán Phase 2–6 (D17) — Phase 7 verify bằng harness tạm; xUnit để Giai đoạn 8.
Các hàm quyết định (guardrail, chọn Comment↔Attachment, cắt trace, tên file) là `public static` (D19) nên harness gọi trực tiếp
được và test xUnit của Phase 8 sẽ tái dùng đúng những hàm đó.

---

## 2. Tổng hợp kết quả (chờ điền)

| Nhóm | Nội dung | Check | Kết quả |
|---|---|---|---|
| **A** | Schema & migration: 2 cột `workspace_members` (+CHECK, partial UQ 1 agent/workspace), `board_columns.is_clarification` (+partial UQ), `agent_runs`, `task_attachments`; `jsonb`/`bytea`; FK RESTRICT; `migrations list` = 6 | — | ⬜ chưa chạy |
| **B** | Hàm thuần: `AgentGuardrails.Evaluate` (biên từng ngưỡng, `null` tokens), `ChooseKind`, cắt `tool_call_trace`, `SafeFileName` (tiếng Việt, `..`, `/`, `\`), tỉ lệ phình base64 | — | ⬜ chưa chạy |
| **C** | Tool/transport contract: DeepSeek `tools`+`tool_choice="auto"`, parse `tool_calls[]`, message `role="tool"` + `tool_call_id`, `finish_reason="length"` ⇒ `TokenBudget`; Tavily body/parse; lỗi ⇒ 502; tool ngoài whitelist; `arguments` hỏng | — | ⬜ chưa chạy |
| **D** | Loop end-to-end (DB thật): 202 → `AwaitingApproval` → approve ⇒ comment/attachment thật → undo ⇒ revert; **không** ghi gì trước khi approve; counters khớp trace | — | ⬜ chưa chạy |
| **E** | "Chờ làm rõ" + "Chạy lại": run `AwaitingClarification`, cột `is_clarification` tạo lazy, comment agent `author_id = agent_user_id`, notification Manager; rerun tạo run mới (`previous_run_id`), run cũ **không** đổi | — | ⬜ chưa chạy |
| **F** | Guardrail: hạ `MaxToolCalls`/`MaxRunTokens`/`RunTimeoutSeconds`/`MaxRunLlmCalls` ⇒ đúng `stop_reason` (`ToolLimit`/`TokenBudget`/`TimeLimit`/`InternalError`); **đúng 1** notification; đọc lại run không sinh thêm | — | ⬜ chưa chạy |
| **G** | Failure modes & concurrency: provider lỗi ⇒ `ProviderError`; 2 request cùng task ⇒ 202 + 409; cancel (Running ⇒ Cancelled + 0 notification; đã xong ⇒ 409); `TaskChanged`; reaper đóng run mồ côi | — | ⬜ chưa chạy |
| **H** | HTTP + quyền + disabled: 7 route (401/403/404/405/409), Member 403 ở route ghi, `Agent:Enabled=false` ⇒ 503, download đúng byte + header, `take` clamp | — | ⬜ chưa chạy |
| **I** | Bất biến & không hồi quy: GET không đổi dữ liệu; **`CreateSubtasks` (Phase 4) vẫn đúng** sau khi `ResolveContextAsync` → `ResolveAsync`; `GET .../members` cũ vẫn 200 với field mới; `is_done` cũ vẫn hoạt động | — | ⬜ chưa chạy |
| **J** | Frontend Vitest: assignee picker (đổi được sang agent), badge theo `status`, nút "Chạy lại" chỉ khi `AwaitingClarification`, disable khi `Running`, `AttachmentList` blob, nhãn notification mới | — | ⬜ chưa chạy |

---

## 3. Số liệu đo thật (chờ điền)

| Chỉ số | Baseline (trước Giai đoạn 7) | Sau Giai đoạn 7 |
|---|---|---|
| Migration | **5** | — |
| `dotnet build TeamNexus.sln` | 0 warning / 0 error | — |
| Frontend tests | **30 files / 155 tests PASS** | — |
| `oxlint` / `tsc -b` / `npm run build` | 0/0 · exit 0 · OK | — |
| Thời gian 1 lượt chạy (fake provider, kịch bản 3 tool) | — | — |
| Token dùng cho 1 lượt chạy thật (DeepSeek, 1 lần đối chiếu) | — | — |
| Kích thước `after_snapshot` khi attachment (base64, cap 512 KB) | — | — |
| Row `task_attachments` sau approve + undo | — | — |

---

## 4. Bug & phát hiện thật (chờ điền)

| # | Khi nào | Triệu chứng | Nguyên nhân gốc | Cách sửa | Verify lại |
|---|---|---|---|---|---|
| — | — | — | — | — | — |

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
7. Tên/field của Tavily API phải xác nhận lại bằng stub trước khi viết prompt (tài liệu gốc chưa đủ để chốt) — nhóm **C**.
