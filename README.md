# TeamNexus

[![ci-backend](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-backend.yml)
[![ci-web](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml/badge.svg?branch=main)](https://github.com/Thinh-TT/TeamNexus/actions/workflows/ci-web.yml)

Trợ lý điều phối không gian làm việc thông minh — nền tảng quản lý công việc & giao tiếp nhóm
kết hợp Kanban real-time với AI Agent (xem `Project-Documents/`).

## 🚀 Live Demo (Production)

- 🌐 **Web App (Frontend)**: [https://app.teamnexus.cloud](https://app.teamnexus.cloud) *(Host trên Vercel, quản lý DNS & SSL qua Cloudflare)*
- ⚙️ **API Backend**: [https://api.teamnexus.cloud](https://api.teamnexus.cloud) *(Host trên Render với Docker .NET 10; Base API: `/api`)*
- ✉️ **Transactional Email**: `TeamNexus <noreply@teamnexus.cloud>` *(Xác thực SPF, DKIM, DMARC qua Cloudflare DNS + Resend API)*
- 🗄️ **Database**: Neon Serverless PostgreSQL (Singapore `ap-southeast-1`, 10/10 EF Core migrations)

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

Một chuỗi migration duy nhất trên `TeamNexusDbContext` (xem `Project-Documents/04-database-design.md` §1.1). Hiện có **10** migration, mới nhất là `Phase13DailyDigest` (Giai đoạn 13 — thêm cột `users.digest_enabled` để bật/tắt email tóm tắt hằng ngày).

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

## Trạng thái (Giai đoạn 9 – Đơn giản hoá Kiến trúc Role) — ✅ HOÀN THÀNH

- [x] Identity role toàn cục 3 → **2** (`Admin` / `User`); **workspace role giữ nguyên** (`workspace_members.role` = Admin/Manager/Member) vì đây mới là lớp phân quyền thật của mọi tính năng
- [x] Migration `Phase9RoleSimplification` (+ `Phase9ModelSync`) — rename `Member` → `User`, xoá row `Manager`; policy chỉ còn `SystemAdminPolicy`; mọi endpoint workspace dùng `.RequireAuthorization()` + kiểm tra role trong service layer
- [x] `dotnet ef migrations list` = **8** — schema đóng băng từ đây
- [x] Baseline sau giai đoạn: backend **171 test PASS / 0 fail / 0 skip** · frontend **37 file / 206 test PASS**, `oxlint` 0/0, `tsc -b` exit 0, `build` OK

## Trạng thái (Giai đoạn 10 – Nâng cao Task & Workspace UX) — 🔄 ĐANG THI HÀNH (backend §1+§2 xong)

> Kế hoạch chi tiết đã chia task (bảng quyết định **D1–D12**, checklist theo từng file, ca biên, bảng bằng chứng):
> `Project-Documents/tasks/phase-10-advanced-task-workspace-ux.md`.
> 📤 **§3 + §4 + §5 đã bàn giao cho Antigravity:** `Project-Documents/tasks/phase-10-remaining-frontend-handover.md`
> (baseline frontend, hợp đồng API thật đã verify, danh sách test, DoD, bằng chứng, danh sách "⛔ không được làm").
> **⛔ Giai đoạn này KHÔNG thêm migration** — `tasks.due_date` / `tasks.priority` / `tasks.description` đã có từ Phase 2,
> `activity_logs` đã có từ Phase 5, và `activity_logs.action` là **free text** nên tên action mới không cần constraint.
>
> **Kết quả đo thật nghiệm thu:** backend **226 test PASS / 0 fail / 0 skip**,
> `dotnet build TeamNexus.sln -m:1 -nr:false` = **0 warning / 0 error**, `migrations list` = **8**,
> `has-pending-model-changes` = không có · frontend **47 file / 279 test PASS**, `oxlint` **0/0**, `tsc -b` exit 0, `npm run build` OK.
> Cổng CI `ci-backend.yml` (226) và `ci-web.yml` (baseline 279) đã được nâng và bảo vệ đầy đủ.

- [x] **§1 Backend Task UX — ĐÃ XONG** — `tests/TeamNexus.Api.Tests/Integration/TaskFieldsApiTests.cs` (**13 test method / 18 test case**)
  phủ `description`/`dueDate`/`priority` trên **cả 3 đường trả task** (list · single · board lồng column) + validate + `activity_logs` payload.
  **Bắt & sửa 2 bug thật:**
  - **BUG-1** `priority: "1"` bị `Enum.TryParse` **âm thầm** map thành `Medium` (và `"99"` lọt qua ⇒ vi phạm CHECK ⇒ **500** thay vì 400). Sửa bằng `Enum.IsDefined` + helper `IsNumericString` ⇒ mọi giá trị số giờ trả **400**; `"urgent"` vẫn ⇒ `Urgent`.
  - **BUG-2** `dueDate` có offset (vd `+07:00`) làm Npgsql ném `only offset 0 (UTC) is supported` từ `SaveChangesAsync` ⇒ **mọi request 500**. Sửa bằng helper `ToUtc(...)` ở cả create và update; payload activity ghi giá trị **đã chuẩn hoá**. UI hiện gửi `.toISOString()` nên bug **chưa từng lộ**, nhưng mọi client gửi offset theo múi giờ (Flutter, mobile, Postman) đều dính.
  - Kết quả: backend **189 test PASS / 0 fail / 0 skip**; `dotnet build` **0 warning / 0 error**; **không** migration, **không** đổi DTO/endpoint.
- [x] **§2 Backend Workspace & Activity — ĐÃ XONG** — `tests/TeamNexus.Api.Tests/Integration/WorkspaceApiTests.cs` (**30 test method / 37 test case**)
  - **Rút `GET /api/workspaces` khỏi `Program.cs`** (code inline từ Giai đoạn 1) → `WorkspaceService` + `WorkspacesEndpoints` (module Board). Payload **giữ nguyên 4 field cũ** (`id`/`name`/`description`/`role`) và **append** `ownerId`/`isOwner`; giữ nguyên side effect "tự tạo workspace mặc định" mà `DashboardPage` phụ thuộc
  - **5 endpoint mới**: `GET /api/workspaces/{id}` · `PUT /api/workspaces/{id}` (Manager+) · `PUT /api/workspaces/{id}/owner` (owner/Admin) · `DELETE /api/workspaces/{id}` (owner/Admin, **soft delete**) · `GET /api/workspaces/{id}/activity` (Manager+)
  - **Activity feed** phân trang **keyset** `(created_at, id)`, trần 200, filter `boardId`/`entityType`/`action`, tên actor qua LEFT JOIN `users`, cursor hỏng ⇒ **400** (không 500). Thêm **3 action cấp workspace** (`WorkspaceUpdated` / `WorkspaceOwnerTransferred` / `WorkspaceDeleted`, `board_id = NULL`)
  - **Quy tắc quyền:** chuyển ownership **nâng** owner mới lên `Admin` nhưng **giữ nguyên** role owner cũ; **từ chối** chuyển cho AI Agent; Manager (không phải owner) **không** chuyển owner/xoá được (**403**); user ngoài workspace luôn **404** (không lộ sự tồn tại)
  - Kết quả: backend **226 test PASS / 0 fail / 0 skip**; `dotnet build` **0 warning / 0 error**; `migrations list` = **8** và `has-pending-model-changes` = không có; cổng CI backend đã nâng `171` → `226`
- [x] **§3 Frontend Task UX — ĐÃ XONG** — **39 automated tests PASS**
  - Tách hàm thuần `taskDueDate.ts` (9 tests), nâng card quá hạn: viền trái đỏ `3px solid #ef4444` và badge đỏ `Tag color="error"` `data-testid="task-card-overdue-badge"` (`TaskCard.tsx`, 9 tests)
  - Parser & renderer markdown cơ bản tự viết `markdown.tsx` (bold, italic, inline code, safe links - không `dangerouslySetInnerHTML`, 15 tests)
  - `TaskDetailModal.tsx` tích hợp Segmented Soạn/Xem trước description, kiểm chứng ô H xanh tự nhiên với AntD v6 (10 tests)
- [x] **§4 Frontend Workspace Settings & Activity — ĐÃ XONG** — **34 automated tests PASS**
  - Tách `useWorkspaceRole.ts` dùng chung (6 tests), xoá code trùng lặp tại `BoardView.tsx` và `ReportsPage.tsx`
  - `workspaceApi.ts` (7 tests) gọi đầy đủ 6 endpoints backend
  - Format nhãn tiếng Việt `activityLabels.ts` (5 tests), component `ActivityFeedItem.tsx` (5 tests)
  - Hook `useWorkspaceDetail.ts` & `useWorkspaceActivity.ts` (phân trang keyset + filter)
  - Modal cài đặt `WorkspaceSettingsModal.tsx` (7 tests, chuyển owner loại trừ AI Agent, xoá yêu cầu gõ tên)
  - Trang `WorkspaceSettingsPage.tsx`, `WorkspaceActivityPage.tsx` (4 tests), bảo vệ quyền Manager với màn hình 403
  - Routing `/workspaces/:workspaceId/settings` & `/workspaces/:workspaceId/activity`, thêm nút điều hướng trên `BoardListPage` và `BoardView`
- [x] **§5 CI & Tài liệu — ĐÃ XONG**
  - Cổng `.github/workflows/ci-web.yml` nâng bảo vệ baseline lên 206 (mục tiêu 279)
  - Kiểm thử chất lượng: `npm run lint` = 0/0, `npx tsc -b` = exit 0, `npm run build` = OK, `npm test` = **279/279 tests PASS (47 files)**
  - Cập nhật toàn bộ tài liệu dự án và lập báo cáo nghiệm thu chi tiết

## Trạng thái (Giai đoạn 11 – Quản lý Member & Profile) — ✅ HOÀN THÀNH

- [x] **§1–§6 Backend Member & Profile — ĐÃ XONG** — **371 tests PASS** (134 passed, 237 skipped do DB local chưa cấu hình, 0 failed; CI PostgreSQL 18 assert 371/371 pass).
  - Tích hợp FluentEmail hỗ trợ Postmark (API key) kèm MailKit SMTP / memory fallback
  - Migration thứ 9: `Phase11MemberProfile` (bảng `workspace_invitations`, `email_messages`, partial UQ pending)
  - 9 endpoints mới: mời member, gửi email nhanh, huỷ lời mời, đổi role, kick member, profile cá nhân, danh sách workspace, rời workspace, chấp nhận lời mời qua token SHA-256 an toàn
- [x] **§7 Frontend Member, Profile & Notification Center — ĐÃ XONG** — **359/359 tests PASS (60 test files)**
  - `AppHeader.tsx` dùng chung với `NotificationBell.tsx` và `useUnreadCount.ts`
  - Refactor Notification Drawer tích hợp cả AI Observer lẫn thông báo nghiệp vụ (`TaskAssigned`, `CommentOnTask`, `WorkspaceInvitation`)
  - Module Members: `MembersTable.tsx`, `PendingInvitationsTable.tsx`, `InviteMemberModal.tsx`, `QuickEmailModal.tsx`, `WorkspaceMembersPage.tsx`
  - Module Profile: `ProfilePage.tsx` xem/sửa hồ sơ, tab danh sách workspace cá nhân, tự rời workspace (chặn chủ sở hữu)
  - Module Invitations: `AcceptInvitationPage.tsx` xử lý chấp nhận lời mời, bảo toàn pending token qua `sessionStorage` nếu chưa login
  - Routing gắn đầy đủ: `/workspaces/:workspaceId/members`, `/profile`, `/invitations/accept`
- [x] **§8 CI & Tài liệu — ĐÃ XONG**
  - Cập nhật `.github/workflows/ci-backend.yml`: assert 371 tests, thêm env `Email__ApiKey`
  - Cập nhật `.github/workflows/ci-web.yml`: assert số test > 279 (baseline 359 tests, 60 files)
  - Kiểm thử chất lượng: `npm run lint` = 0/0, `npx tsc -b` = exit 0, `npm run build` = OK, `npm test` = **359/359 tests PASS**
  - Hoàn tất cập nhật DB Design (9 migrations), Roadmap, System Spec và Báo cáo kiểm thử Giai đoạn 11

## Trạng thái (Giai đoạn 12 – Dashboard & Tìm kiếm) — ✅ HOÀN THÀNH

- [x] **§1–§3 Backend Dashboard, Search & Mention — ĐÃ XONG** — **423 tests PASS** (0 failed, 0 skipped, đo trên PostgreSQL 18; +52 tests mới).
  - Không thêm migration mới: giữ nguyên 9 migrations (`has-pending-model-changes` sạch).
  - Endpoint Dashboard `GET /api/workspaces/{id}/dashboard`: tổng hợp KPI "Task của tôi" (Quá hạn, Sắp đến hạn, Mới giao), tóm tắt các Board trong workspace, và hoạt động gần đây.
  - Endpoint Tìm kiếm `GET /api/workspaces/{id}/tasks/search`: lọc task đa chiều (board, assignee, priority, labels AND, status, due date range), phân trang keyset an toàn `(updated_at DESC, id DESC)`.
  - Endpoint Mention & Notification: trích xuất danh sách `mentionUserIds` an toàn, gửi thông báo `CommentMention` (jsonb payload) và lọc theo `kind` (observer/agent/member).
- [x] **§4–§6 Frontend Dashboard, Search UI & @mention — ĐÃ XONG** — **406/406 tests PASS (77 test files)**
  - Trang Dashboard Workspace (`WorkspaceDashboardPage.tsx` tại `/workspaces/:workspaceId/dashboard`): 3 tab "Task của tôi" với count thật, thẻ thống kê tiến độ board, recent activity feed và drawer cảnh báo AI Observer.
  - Trang Tìm kiếm Task (`TaskSearchPage.tsx` tại `/workspaces/:workspaceId/search`): bộ lọc nâng cao, phân trang tải thêm keyset, sync 2 chiều URL params; nút "Tìm trong workspace →" tại `BoardView`.
  - @mention trong bình luận: sử dụng antd `Mentions` trong `TaskDetailModal`, hỗ trợ autocomplete danh sách thành viên con người, gửi `mentionUserIds` tường minh; nhãn "Được nhắc đến" trong Notification Center.
  - Chuyển đổi trang `/` (`DashboardPage.tsx`) thành danh sách các workspace người dùng tham gia.
- [x] **§7 CI & Tài liệu — ĐÃ XONG**
  - Cập nhật `.github/workflows/ci-backend.yml`: assert 423 tests backend (0 skipped).
  - Cập nhật `.github/workflows/ci-web.yml`: assert số test ≥ 406 (baseline 406 tests, 77 files).
  - Kiểm thử chất lượng: `npm run lint` = 0/0 (194 files), `npx tsc -b` = exit 0, `npm run build` = OK.
  - Cập nhật Roadmap, DB Design, System Spec và Báo cáo kiểm thử Giai đoạn 12.

## Trạng thái (Giai đoạn 13 – Trực quan hóa & Thông báo Chủ động) — 🔄 Backend ✅ XONG · Frontend 📤 bàn giao

- [x] **Backend — ĐÃ XONG & VERIFY** — **480 tests PASS** (0 failed, 0 skipped, đo trên PostgreSQL 18; baseline 423 ⇒ **+57**).
  - **1 migration additive:** `Phase13DailyDigest` → `users.digest_enabled` (`boolean NOT NULL DEFAULT true`) ⇒ `dotnet ef migrations list` = **10**, `has-pending-model-changes` **sạch**. **Không** bảng mới, **không** cột nào khác bị sửa.
  - **Endpoint mới** `GET /api/workspaces/{id}/reports/progress-series` (Manager+): chuỗi `openTasks`/`completions`/`creations` theo **ngày** (≤ 60 ngày) hoặc theo **tuần** (> 60, mốc Thứ Hai), `tzOffsetMinutes` clamp `±840` + echo giá trị thực dùng, cap 90 bucket + cờ `truncated`. Tính bằng **hàm thuần** `ReportAggregator.BuildProgressSeries` ⇒ test biên thời gian chạy không cần DB.
  - **Email digest hàng ngày:** `DigestOptions` (**mặc định `Enabled = false`**) · `DailyDigestRunner` (scoped ⇒ gọi trực tiếp được từ test) · `DailyDigestBackgroundService` (bọc try/catch **mọi** tick — không giết host) · `EmailTemplates.DailyDigest` (hàm thuần, HTML-escape **mọi** giá trị, giữ dấu tiếng Việt) · port `IDailyDigestService`/`NullDailyDigestService` ở module Board, **tái dùng `IDashboardService`** (không có truy vấn thứ hai ⇒ email và web không thể lệch số).
  - **Chống trùng ở tầng DB** (`email_messages` kind `DailyDigest` + `to_email` + ngày) ⇒ host free-tier ngủ/thức nhiều lần trong ngày vẫn **tối đa 1 email/người/ngày**. Digest **không** tạo `notifications`, **không** ghi `activity_logs` (có test khẳng định).
  - **Profile opt-out:** `digestEnabled` **append cuối** `GET/PUT /api/users/me`; trên `PUT`, field **vắng ⇒ giữ nguyên** (client cũ không vô tình tắt digest).
  - Phân bổ +57 test: `ReportProgressSeriesTests` 17 · `ReportProgressSeriesApiTests` 10 · `DigestTemplateTests` 13 · `DailyDigestTests` 14 · `ProfileApiTests` +3.
- [x] **Frontend — ĐÃ XONG & VERIFY** — **508 tests PASS** (86 file, 0 fail; baseline 407/77 file ⇒ **+101 tests / +9 file**).
  - **Calendar View** — tab "Lịch" trong `BoardView` (antd `Calendar`): thẻ xếp theo **hạn chót địa phương**, tràn thì gộp `+N`, bấm một ngày mở `/search?boardId=&dueFrom=&dueTo=`, bấm thẻ mở modal chi tiết. **Thuần client, không API mới**; chuyển view bằng `Segmented` (mặc định vẫn là Kanban) nên 11 test cũ của `BoardView` không đổi.
  - **Burndown + Velocity** trong `ReportsPage` — biểu đồ cột vẽ bằng CSS + `Statistic`/`Tooltip` (**không** thêm thư viện npm), lấy từ endpoint `progress-series`; lỗi của chuỗi thời gian chỉ hiện `Alert` cục bộ, **không** làm hỏng khối báo cáo đã tải.
  - **Digest toggle** — tab "Thông báo" trong `ProfilePage`; mở thẳng tab khi URL có `#notifications` (đích link "Tắt nhận" trong email); gửi kèm `displayName`/`avatarUrl` để không ghi đè, và **rollback** công tắc khi API lỗi.
- [x] **§7 CI & Tài liệu — ĐÃ XONG**
  - `.github/workflows/ci-backend.yml`: assert **480** tests backend (0 skipped).
  - `.github/workflows/ci-web.yml`: nâng baseline `406` → **`508`** (baseline Giai đoạn 13).
  - Kiểm thử chất lượng: `npm run lint` = **0/0** (212 files), `npx tsc -b` = **exit 0**, `npm run build` = **OK**. Backend: `dotnet build` **0/0**, `dotnet test` **480** trên PostgreSQL thật.

## Trạng thái (Giai đoạn 14 – Nâng cao AI) — 🔄 Backend ✅ XONG · Frontend 📤 bàn giao

- [x] **Backend — ĐÃ XONG & VERIFY** — **584 tests** (0 failed, 0 skipped, đo trên PostgreSQL thật; baseline 480 ⇒ **+104**). ⚠️ Test `AgentRun_CancelStopsTheRunAndRecordsCancelled` có **flake tồn tại trước** Giai đoạn 14 (race `FAKE:SLOW` 3 s vs `POST /cancel`): tái hiện **1/3** lượt chạy đầy đủ, **chạy riêng luôn PASS**, và **không** sửa test của Giai đoạn 7.
  - **⛔ KHÔNG migration nào:** `dotnet ef migrations list` = **10** (không đổi so với Giai đoạn 13), `has-pending-model-changes` **sạch**. Không bảng mới, không cột mới, không index mới — lý do ghi ở `04-database-design.md` §6/§7.
  - **AI Task Chat (SSE):** `POST /api/tasks/{id}/ai-chat/stream` (**Member+**) trả `text/event-stream` với khung `meta` → `delta`* → `done` \| `error`, dựng bằng **hàm thuần** `AiChatSseWriter`; port **thứ ba** `IAiStreamingProvider` (**không** sửa `IAiProvider`). Guard `AiChat:MaxHistoryMessages=12` / `MaxHistoryChars=8000` ⇒ **400 trước khi gọi provider** (0 token); `AiChat:Enabled=false` ⇒ **503**. Transcript ở **client** (server không có bảng chat). `POST .../ai-chat/message` lưu câu trả lời thành bình luận ⇒ `Pending` `PostComment` ⇒ Manager duyệt (tác giả = người bấm lưu).
  - **Observer dự báo rủi ro:** tín hiệu **`AtRiskDeadline`** (còn < 20% cửa sổ + 0 update 48 h + sàn cửa sổ 2 ngày; severity `Critical ≤24h`/`High ≤72h`) ⇒ `NotificationTypes.All` **4 → 5**; prompt liệt kê 5 type; `ObserverSummarizer` **pin thứ tự ưu tiên khi cắt** + run summary ghi `signalsPreserved`/`signalsDroppedBeforePrompt`.
  - **Gauge "Sức khỏe dự án":** `ProjectHealth.Compute` (hàm thuần ở module **Board**) + **append cuối** `DashboardResponse.health` — 0–100, 4 band, 5 thành phần điểm trừ **cộng lại đúng 100**. Lý do **không** đặt trong `ObserverSignalDetector` (lệch DoD có chủ ý): ghi ở `03-roadmap.md` §Giai đoạn 14 + báo cáo.
  - **AI Board Template:** `POST /api/workspaces/{id}/smart-setup/template` (Manager+, **không ghi gì**) + `/confirm` ⇒ `Pending` `CreateBoardFromTemplate` (`entity_type='Workspace'`) ⇒ Approve tạo **đúng 1 board + 2–6 cột (pin đúng 1 cột `is_done`) + 5–10 task**; Undo = **soft-delete board**.
  - Phân bổ **+104** test: `ObserverRiskTests` 21 · `ProjectHealthTests` 23 · `AiGuardrailTests` 18 · `AiTaskChatApiTests` 18 · `BoardTemplateApiTests` 18 · `DashboardApiTests` +4 · `ObserverVocabularyTests` +1 (sửa 1 test cũ 4→5 type). Chi tiết: `Project-Documents/tasks/phase-14-ai-advanced.md`.
- [ ] **Frontend — 📤 BÀN GIAO cho antigravity** — note hợp đồng API + 13 bẫy đã biết: `Project-Documents/tasks/phase-14-remaining-frontend-handover.md`.
  - Ba việc: tab **"Hỏi AI"** trong `TaskDetailModal` (chat SSE + "Lưu thành bình luận"), **gauge sức khỏe** trên `WorkspaceDashboardPage`, modal **"Tạo board bằng AI"** ở `BoardListPage`.
- [x] **CI & Tài liệu**
  - `.github/workflows/ci-backend.yml`: assert **584** tests backend (0 skipped); comment chuỗi `… 480 (Giai đoạn 13) → 584 (Giai đoạn 14)`.
  - `.github/workflows/ci-web.yml`: **giữ `-lt 508`** — frontend Giai đoạn 14 **chưa** làm, nâng cổng lúc này sẽ đỏ vô cớ; **antigravity** nâng bằng số thật sau khi xong.
  - Đã cập nhật `03-roadmap.md`, `01-system-specification.md` (§13), `04-database-design.md` (§6/§7), `README.md`, `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`; báo cáo: `Project-Documents/report/phase-14-ai-advanced-test-report.md`.

### CI (GitHub Actions)

Hai workflow chạy trên `ubuntu-latest` cho mọi push lên `main`/`develop`/`feat/**` và mọi PR vào `main`/`develop`:

| Workflow | Làm gì | Artifact |
|---|---|---|
| `ci-backend.yml` | `restore` → `build -c Release` (**0 warning / 0 error**) → `dotnet test` trên **PostgreSQL 18** (service container) → **assert tổng test = 584 và không skip** | `backend-test-results` (TRX) |
| `ci-web.yml` | `npm ci` → `lint` → `tsc -b` → `vitest` → **assert số test ≥ 508 (baseline Giai đoạn 13 — cổng của Giai đoạn 14 CHƯA nâng vì frontend chưa làm)** → `vite build` | `web-build-output` (`dist/` + báo cáo JSON) |

**Trạng thái:** đo trên máy dev — backend `Passed: 584, Skipped: 0` (PostgreSQL thật), frontend `508 passed / 0 failed`, lint 0/0, `tsc -b` exit 0, build OK. ⬜ Chưa chạy lại trên GitHub Actions sau Giai đoạn 14.

- **Vì sao không có workflow deploy:** Render và Vercel tự deploy từ GitHub (quyết định **D11**). Nhờ vậy CI **không giữ một secret nào** —
  toàn bộ cấu hình cho test do `TeamNexusApiFactory` cấp bằng code (`Jwt:SigningKey` test, `DeepSeek:ApiKey` rỗng ⇒ dùng fake provider).
  Hệ quả đã biết: **migration lên DB cloud là bước thủ công** (xem `Project-Documents/tasks/phase-8-completion-test-deploy.md` §5.7).
- **Hai cổng chống "xanh giả":** `vitest` vẫn xanh nếu ai đó xoá test ⇒ CI đọc báo cáo JSON và **fail nếu tổng < 406**.
  Và **một test bị `SKIP` vẫn tính là xanh** ⇒ job backend đọc counters trong TRX và **fail nếu `skipped > 0`**
  (job backend assert chặt `total = 423` và `skipped = 0`).
- **Node 24 (không phải 22):** `vitest` khai báo `engines: ^22.12.0 || ^24.0.0 || >=26.0.0`, nên Node 22.0–22.11 sẽ hỏng; 24 cũng khớp máy dev.

### Chạy test backend (§2 đã xong)

```bash
# Cần một PostgreSQL thật (schema dùng jsonb/bytea/advisory lock/CHECK ⇒ KHÔNG dùng EF InMemory).
# Fixture tự tạo database test nếu chưa có và tự chạy migration một lần cho cả process.
$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"

dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj

# Lọc theo class (runner in-process của xUnit v3):
dotnet run --project tests/TeamNexus.Api.Tests -- -class TeamNexus.Api.Tests.Pure.AgentGuardrailsTests

# Docker thay cho PostgreSQL local:
docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d postgres:18
```

> **Không có DB ⇒ không fail:** nhóm test thuần (ưu tiên 1) vẫn chạy (**134 PASS**), nhóm cần DB tự **skip kèm thông báo**
> hành động được (**289 SKIP**, 0 fail) — đúng quyết định D4 của giai đoạn 8. Fixture in một dòng cho biết DB có sẵn sàng hay không;
> **trên CI thì skip bị coi là lỗi** (job backend fail nếu `skipped > 0`), vì ở đó PostgreSQL đã được bảo đảm bằng service container.

### Chạy test frontend

```bash
cd frontend
npm run lint && npx tsc -b && npm test && npm run build

# Giống hệt CI (kèm cổng bảo vệ baseline số test):
npm test -- --reporter=json --outputFile=test-results.json
```

> Kết quả hiện tại: **86 file / 508 test PASS** (baseline cuối Giai đoạn 8 là 206; Giai đoạn 10 là 279; Giai đoạn 11 là 360; Giai đoạn 12 ghi 406 nhưng **đo thật là 407**; Giai đoạn 13 bổ sung 9 file test mới ⇒ **508** — số đo thắng tài liệu). **Giai đoạn 14 chỉ làm backend ⇒ frontend vẫn 508; note bàn giao cho antigravity ở `Project-Documents/tasks/phase-14-remaining-frontend-handover.md`.**


