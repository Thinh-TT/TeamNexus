# TeamNexus

[![ci-backend](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml)
[![ci-web](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml)

Trợ lý điều phối không gian làm việc thông minh — nền tảng quản lý công việc & giao tiếp nhóm
kết hợp Kanban real-time với AI Agent (xem `Project-Documents/`).

## 🚀 Live Demo (Production)

- 🌐 **Web App (Frontend)**: [https://team-nexus-taupe.vercel.app](https://team-nexus-taupe.vercel.app) *(Host trên Vercel)*
- ⚙️ **API Health Check**: [https://teamnexus-api.onrender.com/api/health](https://teamnexus-api.onrender.com/api/health) *(Host trên Render với Docker .NET 10)*
- 🗄️ **Database**: Neon Serverless PostgreSQL (Singapore `ap-southeast-1`, 6/6 EF Core migrations)

> 💡 **Lưu ý Cold-start**: Do chạy trên Free Tier của Render, khi không có request trong 15 phút máy chủ sẽ tạm ngủ. Request đầu tiên có thể mất ~30–60 giây để máy chủ thức dậy. Giao diện frontend đã tích hợp sẵn cơ chế auto-reconnect & thông báo tiếng Việt mượt mà.

## Kiến trúc

**Modular Monolith** ASP.NET Core (.NET 10) + React (TypeScript, Vite) + PostgreSQL (EF Core).

```
src/                        # Backend (.NET)
  TeamNexus.Api/            #   Entry point: Program.cs, DI, middleware, OpenAPI/Scalar
  TeamNexus.Persistence/    #   Single DbContext + entities + EF migrations (one migration chain)
  Modules/Auth/TeamNexus.Modules.Auth/   #   Module Auth (Identity, OAuth, JWT – Phase 1)
  Modules/Board/TeamNexus.Modules.Board/ #   Module Board (Kanban CRUD + SignalR – Phase 2)
  Modules/Ai/TeamNexus.Modules.Ai/       #   Module Ai (Smart Setup, Accountability, Observer, Agent Executor – Phase 3–5, 7)
  Modules/Reporting/TeamNexus.Modules.Reporting/  #   Module Reporting (PDF/Excel on-demand – Phase 6, xem tasks/phase-6-reporting-export.md)
  Shared/TeamNexus.Shared/  #   Contracts & helpers dùng chung
frontend/                   # Web (React + TS + Vite + Ant Design)
TeamNexus.sln
Project-Documents/          # Spec, tech decisions, roadmap, DB design, tasks
```

## Chạy ở local (dev)

Yêu cầu: .NET SDK 10, Node.js ≥ 22.12 (khuyến nghị 24 — đúng bản CI dùng; `vitest` yêu cầu `^22.12.0 || ^24.0.0 || >=26.0.0`).

```bash
# 1) Backend – http://localhost:5000  (Scalar UI: http://localhost:5000/scalar)
dotnet run --project src/TeamNexus.Api

# 2) Frontend – http://localhost:5173  (proxy /api → backend :5000)
cd frontend
npm install
npm run dev
```

### Migration (EF Core)

Một chuỗi migration duy nhất trên `TeamNexusDbContext` (xem `Project-Documents/04-database-design.md` §1.1). Hiện có **6** migration, mới nhất là `Phase7AiAgentSchema` (Giai đoạn 7 — agent identity, `board_columns.is_clarification`, `agent_runs`, `task_attachments`).

```bash
dotnet ef migrations list   --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
dotnet ef database update   --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
```

> ⚠️ **Trong môi trường sandbox/agent** (named pipe của MSBuild bị chặn), `dotnet build` mặc định **fail im lặng** với
> "0 Warning(s) / 0 Error(s)", và `dotnet ef` (tự build nội bộ) cũng fail theo. Khi đó dùng đúng quy trình sau — nếu bỏ qua,
> `migrations add --no-build` sẽ đọc **DLL cũ trong `bin`** và sinh migration **sai một cách im lặng**:
>
> ```bash
> dotnet build TeamNexus.sln -m:1 -nr:false    # bắt buộc: chạy 1 node, không tái dùng node
> dotnet ef migrations add <Tên> --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
> dotnet ef database update      --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api --no-build
> ```
>
> Thứ tự bắt buộc: **sửa source → build 0/0 → mới `migrations add`**.

### Cấu hình & secrets
- Mọi secret local (OAuth Client ID/Secret, connection string, JWT key) đặt trong **User Secrets**:
  `dotnet user-secrets init --project src/TeamNexus.Api` rồi `dotnet user-secrets set "<Key>" "<Value>"`.
- Connection string PostgreSQL đọc từ `ConnectionStrings:DefaultConnection`, ví dụ:
  `dotnet user-secrets set --project src/TeamNexus.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=TeamNexus;Username=postgres;Password=..."`.
- Frontend override backend URL qua `frontend/.env` (xem `.env.example`).
- Mục `Cors:AllowedOrigins` cho phép gọi trực tiếp từ origin Vite (`http://localhost:5173`).

## Trạng thái (Giai đoạn 1 – Nền tảng & Auth)

- [x] §1 Khởi tạo Project — backend modular monolith + frontend Vite build/run được
- [x] §2 Schema PostgreSQL (EF Core migration) — `TeamNexusDbContext` tại `src/TeamNexus.Persistence`, migration `InitialSchema` đã áp dụng lên DB `TeamNexus` local
- [x] §3 Auth Backend — GitHub + Google OAuth ✅ (liên kết provider theo email: cùng user, roles giữ nguyên), JWT + refresh HttpOnly cookie + rotate, CSRF, RBAC policies + endpoint mẫu
- [x] §4 Auth Frontend (login, protected routes, auto-refresh)

## Trạng thái (Giai đoạn 2 – Kanban Core)

- [x] §1 Schema Kanban (EF migration `Phase2KanbanSchema`) — thêm `board_columns`, `tasks`, `labels`, `task_labels`, `task_comments` (đã migrate lên DB local)
- [x] §2 Backend Module Board — CRUD Board/Column/Task + Label/Comment (Minimal API, quyền theo workspace_members.role), migration `Phase2BoardColumnIsDone` (cột `is_done`) — verify 40 check API
- [x] §3 SignalR Real-time — BoardHub tại `/hubs/board`, JoinBoard/LeaveBoard theo group `board-{id}` (verify membership), broadcast Task/Column/Comment events qua `IBoardEventPublisher` — verify 14 check WebSocket (2 client cùng board + group isolation)
- [x] §4 Frontend Kanban UI (drag & drop, useBoardHub, modals, filters)
- [x] §5 Kiểm thử & hoàn thiện (45 automated tests Vitest + Testing Library, build & lint 100% PASS, live browser testing)

## Trạng thái (Giai đoạn 3 – AI Smart Setup)

- [x] §1–§4 Backend Module Ai — `IAiProvider`, `DeepSeekAiProvider` (OpenAI-compatible) & `FakeAiProvider`, `POST /api/boards/{boardId}/smart-setup`, `GET /api/workspaces/{workspaceId}/members`, chuẩn hoá & resolve assignee/label, không ghi DB
- [x] §5 Frontend UI Smart Setup — `src/features/ai/` (`smartSetupApi`, `useSmartSetup`, `SmartSetupModal`, `ProposedTaskItem`), tích hợp nút "AI Smart Setup" vào header Kanban Board
- [x] §6 Kiểm thử & hoàn thiện (64 automated tests Vitest + Testing Library, 0 lint warnings/errors, TypeScript & Vite build 100% PASS)

## Trạng thái (Giai đoạn 4 – Accountability Layer)

- [x] §1 Schema Accountability (EF migration `Phase4AccountabilityLayer`) — thêm `ai_action_logs` (`jsonb`, CHECK status, audit fields, FK RESTRICT, indexes)
- [x] §2–§3 Backend Module Ai — Cổng trung gian `AiActionService`, applier registry (`CreateSubtasksApplier`), 6 endpoints (`confirm`, `ai-actions` list, detail, `approve`, `reject`, `undo`), CAS chống apply trùng, transaction rollback an toàn
- [x] §4 Frontend UI Accountability — `src/features/ai/` (`aiActionApi`, `useAiActions`, `SmartSetupModal` với luồng Pending/Approve/Reject/Undo, `AiActionHistoryDrawer`, `AiActionLogItem`), nút "Lịch sử AI" + badge pending tại header Kanban Board
- [x] §5 Kiểm thử & hoàn thiện — 54 backend checks PASS (harness API thật + DB PostgreSQL thật + SignalR client thật), 88 frontend automated tests Vitest + Testing Library PASS, 0 lint warning/error, build .NET & Vite 100% PASS

## Trạng thái (Giai đoạn 5 – AI Observer) — Backend hoàn tất, frontend đã bàn giao

- [x] §1 Schema Observer (EF migration `Phase5AiObserverSchema`) — thêm `activity_logs` (event store append-only), `notifications` (1 row/người nhận), `ai_observer_runs` (audit + token); `jsonb`, CHECK `status`, FK RESTRICT, index `(workspace_id, created_at)` / `(recipient_user_id, is_read)`
- [x] §2–§4 Backend Module Ai — thu thập `activity_logs` từ Board (`IActivityLogWriter`), 4 tín hiệu (`OverdueTask`/`StalledTask`/`Overload`/`Bottleneck`) tính bằng hàm thuần, prompt tóm tắt trước khi gọi AI (0 tín hiệu ⇒ **không gọi AI**), validator chống hallucination, notification chỉ cho Manager/Admin + dedupe 24h, `ObserverBackgroundService` (PeriodicTimer + advisory lock) và prune retention
- [x] §5 Backend – DTO/Endpoints/DI — 6 endpoint (`/api/notifications` ×3, `/api/workspaces/{id}/observer/scan|runs`, `/api/observer/runs/{id}`) với quyền Manager/Admin, CSRF, mã lỗi `{ error }`; `Program.cs` không đổi
- [x] §7.1 Verify backend — **178 check PASS** (API thật + PostgreSQL thật + `FakeAiProvider`, không tốn token): schema 28, detector/validator thuần 44+15, background service 3, scan end-to-end 6, chi phí 3, HTTP API 32, activity log 31, dedupe/concurrency/failure 2+3+4
- [x] §6 + §7.2 Frontend — `src/features/ai/` (`notificationApi`, `observerApi`, `useNotifications`, `useObserverRuns`, `NotificationDrawer`, `NotificationItem`, `ObserverRunsDrawer`, `ObserverFindingCard`) + nút "Cảnh báo AI" & badge ở `BoardView` — **bàn giao antigravity**, contract chốt tại `Project-Documents/tasks/phase-5-ai-observer.md` mục "🔻 BÀN GIAO"

## Trạng thái (Giai đoạn 6 – Báo cáo & Xuất dữ liệu)

- [x] §0 Kế hoạch & chia task — `Project-Documents/tasks/phase-6-reporting-export.md` (module `Reporting` mới, **không** schema/migration, quyền Manager/Admin, cửa sổ 30 ngày clamp 365, file export trong RAM không lưu server); `04-database-design.md` §3.7/§7/§8 đã cập nhật theo quyết định
- [x] §1 Backend – Aggregation thuần: `ReportAggregator` (progress/performance/byBoard/byAssignee/activity/health + `metricDefinitions` tiếng Việt), `ReportSnapshots`, `ReportThresholds.BuildRange`, options `Reports`, module `TeamNexus.Modules.Reporting` vào `.sln` + `Program.cs` — verify harness thuần **76/76 PASS**, `dotnet build` 0 warning/0 error, **không** migration/schema mới
- [x] §3 Backend – `ReportService` (projection read-only trên `tasks`/`boards`/`board_columns`/`activity_logs`/`ai_observer_runs`, quyền Manager/Admin, `ExportFormats` pdf/excel, `IReportExportService` interface cho §4) + 3 exception domain (`{ error }` 400/503) — verify harness **37/37 PASS** (DI thật + PostgreSQL thật, fixture raw SQL đã hard-delete, DB về baseline), **8 truy vấn/request** không N+1, 0 warning/0 error
- [x] §4 Backend – renderer: **QuestPDF 2026.8.0** (PDF đủ bố cục, tiếng Việt có dấu, khổ A4/A3/Letter) + **ClosedXML 0.105.1** (Excel 5 sheet + data bar/autofilter/freeze pane, giá trị literal), `ReportFileName` (tên file ASCII), `ReportLabels` (nhãn tiếng Việt), `ReportExportService` (chọn định dạng + `Rows` + metadata) — verify harness **43/43 PASS** (đọc lại PDF bằng PdfPig, round-trip Excel bằng ClosedXML, không file rác/IO/DB), app boot thật health **200**; `AddReportingModule` tự đăng ký renderer nên**không còn** phụ thuộc thứ tự đăng ký
- [x] §5 Backend – DTO + 3 endpoint `GET /api/workspaces/{id}/reports/{summary|export|boards}`: `ReportDtos` (16 record + `From(...)`), parse `from`/`to` strict ISO-8601 (400 `{ error }`), export trả file RAM + 3 header phụ, quyền Manager/Admin, `{ error }` 400/403/404/503, không CSRF vì là GET — verify **34/34 PASS** bằng API thật 3 instance (Kestrel + PostgreSQL thật + JWT tự ký) gồm mở lại PDF/Excel qua HTTP; **bug thật đã sửa**: header `X-Report-Period` chứa en dash làm Kestrel ném ⇒ mọi export 500
- [x] §6 Frontend – `src/features/reporting/` + route `/workspaces/:id/reports` + nút "Báo cáo" ở `BoardListPage`/`BoardView` (ẩn với Member) — **bàn giao antigravity**, contract chốt tại `tasks/phase-6-reporting-export.md` mục "🔻 BÀN GIAO §6" + hướng dẫn verify ở §7.2 (baseline FE trước khi làm: oxlint 0/0, `tsc -b` exit 0, **122 tests PASS**)
- [x] §7.1 Verify backend — **264/264 check PASS** qua 5 đợt harness ngoài workspace (§1 thuần 76 · §3 loader+DB 37 · §4 renderer 43 · §5 HTTP 34 · §7.1 chốt A–F 74); 3 bug thật đã bắt & sửa (đếm trùng `activeUsers`, hai mốc thời gian trong một request, header en dash làm mọi export 500) + 1 lỗi hạ tầng (`dotnet ef` hỏng vì thiếu implementation `IReportExportService`) — chi tiết ở `report/phase-6-reporting-test-report.md`
- [x] §7.2 Test frontend (Vitest) — thuộc phần bàn giao antigravity; DoD: `lint` + `tsc -b` + `build` + `test` sạch kèm số test thật

## Trạng thái (Giai đoạn 7 – AI Agent Executor) — ✅ HOÀN THÀNH (Cả Backend & Frontend)

> **Thứ tự thi hành:** 1 → 2 → 3 → 4 → 5 → 6 → **7 (đã hoàn thành)** → **8 (đang làm)** → 9. Giai đoạn 8 (test/CI/deploy) và 9 (Flutter) **không bị bỏ**, chỉ được lùi
> vì không chặn Giai đoạn 7 (xem `Project-Documents/03-roadmap.md` phần đầu tài liệu).
> Kế hoạch chi tiết + bảng quyết định kiến trúc (D1–D20): `Project-Documents/tasks/phase-7-ai-agent-executor.md`.
> Schema đã chốt: `Project-Documents/04-database-design.md` §3.3, §3.4, §3.5, **§3.8**.
> Báo cáo kiểm thử: `Project-Documents/report/phase-7-ai-agent-executor-test-report.md`.

- [x] §2 Schema & migration `Phase7AiAgentSchema` — `workspace_members.member_type`/`ai_agent_name` (+ CHECK `ck_workspace_members_member_type` + partial UQ 1 agent/workspace), `board_columns.is_clarification` (+ partial UQ), 2 bảng mới `agent_runs` (`jsonb` tool trace, token, `status`/`stop_reason`, 3 CHECK, 3 index, 8 FK RESTRICT) và `task_attachments` (`bytea`, cap 512 KB, FK RESTRICT, **không** soft-delete/`updated_at`) — `dotnet ef migrations list` = **6**, `dotnet build` 0/0; verify **36/36 check PASS** trên PostgreSQL thật (nhóm A — `report/phase-7-ai-agent-executor-test-report.md` §2.1)
- [x] §3 Module Board — `TaskService` siết **membership** của assignee (400 khi ngoài workspace; agent đi qua đúng đường này), `TaskResponse` thêm `assigneeIsAiAgent`/`activeAgentRunId` (điền ở **cả 3** đường trả task), cột `board_columns.is_clarification` (400 khi trùng cờ `is_done`, 409 khi xoá), `WorkspaceMemberResponse.memberType` + agent tạo **lazy** để dropdown assignee thật sự có lựa chọn agent, port `IAiAgentResolver` (+ `NullAiAgentResolver`) và hợp đồng SignalR `AgentRunProgress` — verify **44/44 check PASS** (nhóm I) trên API thật + PostgreSQL thật; **2 bug thật đã sửa** (`agent_runs.status` 16→32 ký tự làm run "Chờ làm rõ" không insert được; `GetValueOrDefault` trả `Guid.Empty` thay vì `null` cho `activeAgentRunId`) — chi tiết `report/phase-7-ai-agent-executor-test-report.md` §2.2
- [x] §4 Module Ai — provider **function-calling** (`IAiToolCallingProvider.ChatAsync` song song `IAiProvider`, `CompleteAsync` **không** đổi), `TavilyWebSearchProvider` (+ `Tavily:AuthMode` chọn biến thể auth) + `FakeWebSearchProvider` offline, whitelist **4 tool** (`SearchSystemData`/`WebSearch`/`DraftOutput`/`RequestClarification`), `AgentRunOrchestrator` + **guardrail** (`MaxToolCalls`/`RunTimeoutSeconds`/`MaxRunTokens`/`MaxRunLlmCalls`), 2 applier mới (`PostComment`/`PostAttachment`) đi qua Accountability Layer sẵn có, **7 endpoint**, `AgentRunReaper` cho run mồ côi, `WorkspaceAiAgentResolver` (impl thật của port Board) — verify **270/270 check PASS** (nhóm B/C 89 · D/E/H/I 107 · F+503 46 · G 21 · **1 lần gọi DeepSeek thật** 7) trên API thật + PostgreSQL thật; **3 bug thật đã sửa** (DeepSeek **không** map `tool_calls`/`finish_reason` ⇒ mọi lượt function-calling bị coi là "lượt rỗng"; `FakeAiProvider` bị "cướp" kịch bản bởi tiêu đề task trong tool result; `POST /cancel` thiếu cổng `Agent:Enabled` ⇒ trả 404 thay vì 503) — chi tiết `report/phase-7-ai-agent-executor-test-report.md` §2.3
- [x] §5 Frontend — **dropdown chọn người thực hiện** (task detail `TaskDetailModal` + thêm thẻ nhanh `KanbanColumn` qua `useWorkspaceMembers`), badge AI Agent và clamp 2 dòng câu hỏi làm rõ trên `TaskCard`, cột "Chờ làm rõ" `QuestionCircleOutlined`, panel `AgentRunPanel` (start/rerun/cancel, counters, timeline trace), `AgentDraftApproval` (tái dùng `AiActionLogItem`), `AttachmentList` (tải blob), nhãn notification mới tiếng Việt — verify: `oxlint` 0/0, `tsc -b` exit 0, `npm run build` OK, Vitest **35 test files / 187 tests PASS 100%**
- [x] §6 Config `Agent` + `Tavily` (không hard-code ngưỡng; `Agent:Enabled=false` ⇒ 503) — 2 section trong `appsettings.json` (22 + 5 key, có `ClarificationColumnName`/`AuthMode`) + **1 dòng log khởi động** không chứa secret; đã làm cùng §4
- [x] §7 Verify bằng harness tạm ngoài workspace **A–I** + **J (Frontend)** — Backend: **291/291 check PASS** ở đợt verify tổng trên API Kestrel thật + PostgreSQL 18 thật ⇒ **371/371** cộng dồn cả giai đoạn; Frontend: **187/187 tests PASS**
- [x] §8 Đối chiếu 6 ô hoàn thiện + 2 hạng mục bắt buộc phát sinh của roadmap — cả 6 ô gốc và 2 ô phát sinh (siết membership + dropdown UI) đã hoàn thành 100% — `report/phase-7-ai-agent-executor-test-report.md` §2.5 + `03-roadmap.md` Giai đoạn 7

**Hai hạng mục bắt buộc phát sinh** (phát hiện khi khảo sát code):

- `TaskService.CreateTask/UpdateTask` chỉ kiểm `users.AnyAsync` — **không** kiểm membership ⇒ **đã siết** ở §3.2 (`RequireAssigneeInWorkspaceAsync`: user ngoài workspace ⇒ **400**; agent đi qua đúng đường này) và verify ở nhóm I.
- UI **chưa có** đường đổi người thực hiện: `TaskDetailModal` và `KanbanColumn` quick-add ⇒ **đã hoàn thành** ở §5 với `useWorkspaceMembers` cache theo `workspaceId`, dropdown Select có avatar và purple tag cho AI Agent.

**Môi trường đã có key thật (không commit):** `DeepSeek:ApiKey` + **`Tavily:ApiKey`** nằm trong User Secrets của
`src/TeamNexus.Api`; biến thể auth Tavily đã xác nhận bằng gọi thật (`Tavily:AuthMode=Bearer`, `Body` là phương án dự phòng).
Để verify offline **không tốn token**, đặt env `DeepSeek__ApiKey` = **một khoảng trắng** `' '` (env rỗng bị .NET coi là "unset"
nên User Secrets sẽ thắng trở lại).

## Trạng thái (Giai đoạn 8 – Hoàn thiện, Test & Deploy) — 🔄 ĐANG LÀM

> **Đây không phải giai đoạn feature.** Phần lớn công việc là test, hạ tầng và cấu hình; chỉ có **3 vá mã nguồn nhỏ** là bắt buộc để bản
> deploy thật sự chạy được. Kế hoạch chi tiết đã chia task (kèm hướng dẫn deploy từng bước cho người **chưa từng dùng**
> Render/Neon/Vercel/Railway/Supabase/Netlify/GitHub Actions):
> `Project-Documents/tasks/phase-8-completion-test-deploy.md`.
> Baseline frontend: **35 files / 206 tests PASS**, `oxlint` 0/0, `tsc -b` exit 0, `npm run build` OK (baseline cuối Giai đoạn 7 là 187 — §2b đã cộng thêm 19 test).
> Schema đã đóng băng ở Giai đoạn 7 (**6 migration**) — giai đoạn này **không** thêm migration.

- [x] §2 Test backend — project `tests/TeamNexus.Api.Tests` (xUnit **v3** + `WebApplicationFactory<Program>` + PostgreSQL 18 thật, **không** dùng EF InMemory): nhóm ưu tiên 1 (hàm thuần `public static` của Phase 4–7) + Auth/CSRF/refresh-rotation + Kanban CRUD/kéo-thả/siết membership + Smart Setup & Accountability Layer (Pending → Approve/Reject/Undo) + Agent Executor (rerun append-only, wall-clock guardrail, huỷ, reaper, 503) + export PDF/Excel — **172 test PASS** (0 fail / 0 skip / 0 warning) trên PostgreSQL 18 thật; **98 PASS / 74 SKIP / 0 FAIL** khi không có DB; `dotnet build TeamNexus.sln` = 0/0; `migrations list` = **6** (không đổi schema)
- [x] §3.1 Vá **cookie cross-site** — `AuthOptions` (section `Auth`) điều khiển `SameSite` cho **cả** token cookie **và** antiforgery cookie; mặc định `Lax` (local không đổi), production đặt env `Auth__CookieSameSite=None`. **Đây là blocker số 1 khi tách domain FE/API** (Vercel + Render); verify bằng 2 test mới (`AuthCookies_UseLaxByDefault`, `AuthCookies_FollowAuthCookieSameSiteConfiguration`)
- [x] §2b Test frontend bổ sung + §3.2/§3.3 — `utils/hubUrl.ts` (`resolveHubUrl`), `utils/reconnectPolicy.ts` (`nextRetryDelay` trần 30 s **thử lại vô hạn** + `isColdStartLikely`), `useBoardHub` dùng 2 hàm đó + `refetch` khi reconnect + `reconnect()` thủ công, `BoardView` có nút **"Kết nối lại"** + thông điệp cold-start tiếng Việt — **206 test PASS** (0 fail, +19 so với 187)
- [x] §4 **CI/CD GitHub Actions** — `.github/workflows/ci-backend.yml` (.NET 10 + `postgres:18`, build `-m:1 -c Release`, 172 test, artifact TRX) và `ci-web.yml` (Node 24: `lint` → `tsc` → `test` → **assert số test > 187** → `build`, artifact `dist` + báo cáo JSON); cache NuGet/npm; có `concurrency` huỷ run cũ + `timeout-minutes`; **hoàn toàn không có secret** (D11 — deploy do Render/Vercel tự làm)
- [ ] §5 Deploy — **Neon** (Postgres) → **Render** (API, `/api/health`) → **Vercel** (FE) + cập nhật redirect URI Google/GitHub + migrate thủ công bằng `dotnet ef` + checklist nghiệm thu 10 bước
- [x] §6.2 Cold-start (phần frontend) — đã làm cùng §2b: thay `withAutomaticReconnect([…])` (5 lần trong ~47 s rồi **bỏ cuộc vĩnh viễn**) bằng retry vô hạn có trần, refetch board khi reconnect, nút "Kết nối lại", thông điệp tiếng Việt. Việc còn lại của §6 là **đo cold-start thật trên Render** (§6.5 — cần deploy trước)
- [ ] §6.5 Đo cold-start trên Render: tự phục hồi ≤ 90 s sau ≥ 20 phút rỗi, không cần F5
- [ ] §7 Rà soát UI/UX 1 vòng toàn app + sửa các mục trong danh sách chốt + chuẩn bị demo/CV (link demo, ảnh chụp, kịch bản trình bày)
- [ ] §9 Báo cáo `Project-Documents/report/phase-8-completion-test-deploy-report.md` — số test thật, link CI, URL production, thời gian cold-start đo được, bug thật bắt được

### CI (GitHub Actions)

Hai workflow chạy trên `ubuntu-latest` cho mọi push lên `main`/`develop`/`feat/**` và mọi PR vào `main`/`develop`:

| Workflow | Làm gì | Artifact |
|---|---|---|
| `ci-backend.yml` | `restore` → `build -c Release` (**0 warning / 0 error**) → `dotnet test` trên **PostgreSQL 18** (service container) → **assert không test nào bị skip** | `backend-test-results` (TRX) |
| `ci-web.yml` | `npm ci` → `lint` → `tsc -b` → `vitest` → **assert số test > 187** → `vite build` | `web-build-output` (`dist/` + báo cáo JSON) |

**Trạng thái đã verify:** cả hai workflow **xanh** trên run thật — backend `Passed: 172, Skipped: 0`, frontend `vitest: total=206 failed=0`.

- **Vì sao không có workflow deploy:** Render và Vercel tự deploy từ GitHub (quyết định **D11**). Nhờ vậy CI **không giữ một secret nào** —
  toàn bộ cấu hình cho test do `TeamNexusApiFactory` cấp bằng code (`Jwt:SigningKey` test, `DeepSeek:ApiKey` rỗng ⇒ dùng fake provider).
  Hệ quả đã biết: **migration lên DB cloud là bước thủ công** (xem `Project-Documents/tasks/phase-8-completion-test-deploy.md` §5.7).
- **Hai cổng chống "xanh giả":** `vitest` vẫn xanh nếu ai đó xoá test ⇒ CI đọc báo cáo JSON và **fail nếu tổng ≤ 187**.
  Và **một test bị `SKIP` vẫn tính là xanh** ⇒ job backend đọc counters trong TRX và **fail nếu `skipped > 0`**
  (lần chạy CI đầu tiên đã lọt lưới đúng kiểu này: job xanh nhưng chỉ **111/172** test thực sự chạy — xem §4.6 của task doc).
- **Node 24 (không phải 22):** `vitest` khai báo `engines: ^22.12.0 || ^24.0.0 || >=26.0.0`, nên Node 22.0–22.11 sẽ hỏng; 24 cũng khớp máy dev.

### Chạy test backend (§2 đã xong)

```bash
# Cần một PostgreSQL thật (schema dùng jsonb/bytea/advisory lock/CHECK ⇒ KHÔNG dùng EF InMemory).
# Fixture tự tạo database test nếu chưa có và tự chạy migration một lần cho cả process.
$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=..."

dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj

# Lọc theo class (runner in-process của xUnit v3):
dotnet run --project tests/TeamNexus.Api.Tests -- -class TeamNexus.Api.Tests.Pure.AgentGuardrailsTests

# Docker thay cho PostgreSQL local:
docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:18
```

> **Không có DB ⇒ không fail:** nhóm test thuần (ưu tiên 1) vẫn chạy (**98 PASS**), nhóm cần DB tự **skip kèm thông báo**
> hành động được (**74 SKIP**, 0 fail) — đúng quyết định D4 của giai đoạn 8. Fixture in một dòng cho biết DB có sẵn sàng hay không;
> **trên CI thì skip bị coi là lỗi** (job backend fail nếu `skipped > 0`), vì ở đó PostgreSQL đã được bảo đảm bằng service container.

### Chạy test frontend

```bash
cd frontend
npm run lint && npx tsc -b && npm test && npm run build

# Giống hệt CI (kèm cổng bảo vệ baseline số test):
npm test -- --reporter=json --outputFile=test-results.json
```

> Kết quả hiện tại: **35 file / 206 test PASS** (baseline cuối Giai đoạn 7 là 187; §2b cộng thêm 19 test cho
> `hubUrl`, `reconnectPolicy`, `useBoardHub` reconnect và `BoardView`).


