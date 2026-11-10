# AGENTS.md — TeamNexus AI Coding Agent Guidelines

> **Project:** TeamNexus — Intelligent Workspace Orchestration Platform (Real-time Kanban + Hybrid AI System-1/System-2 + Semantic RAG + Human-in-the-Loop Accountability Layer)
> **Architecture:** Modular Monolith (.NET 10 + EF Core 10 + PostgreSQL 18 `pgvector`) & React 19 + TypeScript 6 + Vite 8 + Ant Design v6

This file defines the mandatory workflow, architectural invariants, historical pitfalls, UI/UX standards, and verification gates for all AI coding agents working on **TeamNexus**.

---

## 1. Repository Map & Module Boundaries

```text
TeamNexus/
├── src/
│   ├── TeamNexus.Api/                          # Host entry point (Program.cs), CORS, Scalar OpenAPI (/scalar)
│   ├── TeamNexus.Persistence/                  # Single TeamNexusDbContext, Entities, Configurations, Migrations
│   ├── Shared/TeamNexus.Shared/                # Cross-cutting contracts, CSRF filter, shared domain rules
│   └── Modules/                                # STRICTLY 4 BUSINESS MODULES (Do not add a 5th .csproj)
│       ├── Auth/TeamNexus.Modules.Auth/        # Google/GitHub OAuth, JWT + HttpOnly Cookie, Refresh Token, Identity Roles
│       ├── Board/TeamNexus.Modules.Board/      # Workspaces, Boards, Columns, Tasks, Comments, Search, Dashboard, BoardHub (SignalR)
│       ├── Ai/TeamNexus.Modules.Ai/            # Jev System-1, DeepSeek System-2, Accountability Layer, Observer, Personas, ReAct Tools, RAG, Email/Digest, External Integrations
│       └── Reporting/TeamNexus.Modules.Reporting/ # Progress/Velocity analytics, on-demand PDF (QuestPDF) & Excel (ClosedXML)
├── frontend/                                   # React 19.2 + TypeScript 6.0 + Vite 8.2 + Ant Design 6.6 SPA
│   └── src/
│       ├── app/                                # Router & top-level shell wiring
│       ├── shared/                             # httpClient (Axios + CSRF), AppHeader, WorkspaceShell, common utilities
│       └── features/                           # Feature modules: auth, board, ai, dashboard, knowledge, members, profile, reporting, search, workspace, landing
├── tests/
│   └── TeamNexus.Api.Tests/                    # xUnit suite: Pure/ (zero-DB unit tests), Integration/ (real PostgreSQL), Infrastructure/
└── project-docs/                               # Authoritative specifications, ADRs, roadmap, phase task plans & test reports
```

### Core Documentation Index (`project-docs/`)
- [`01-system-specification.md`](project-docs/01-system-specification.md): System boundaries & functional requirements across phases.
- [`02-tech-stack-decisions.md`](project-docs/02-tech-stack-decisions.md): Architectural Decision Records (ADRs) & infrastructure constraints.
- [`03-roadmap.md`](project-docs/03-roadmap.md): Master roadmap (Phases 1 → 16) with verified metrics and deviation summaries.
- [`04-database-design.md`](project-docs/04-database-design.md): PostgreSQL ERD, table schemas, index strategies, and migration history.
- [`06-adjust-and-upgrade-with-jev.md`](project-docs/06-adjust-and-upgrade-with-jev.md): Hybrid AI architecture (TypeSafe Jev 1.13 System-1 + DeepSeek System-2).
- [`07-external-integrations-design.md`](project-docs/07-external-integrations-design.md): Design specification for Phase 15 external integrations (Slack, Discord, GitHub).
- [`tasks/`](project-docs/tasks/): Phase-by-phase implementation plans (`phase-*.md`), frontend handover contracts, and execution logs.
- [`report/`](project-docs/report/): Verification reports, live production audit matrices, and performance benchmarks.

---

## 2. Spec-Driven Agentic Workflow

1. **Pre-Implementation Audit (*Khảo sát đầu kỳ*):**
   - Always read the relevant spec in `project-docs/01..07-*.md` and the active task plan in `project-docs/tasks/phase-*.md` before writing code.
   - Inspect existing services, endpoints, hooks, and components first. **Never rewrite or duplicate functionality that already exists in the repo.**
2. **Codebase Conventions Beat Draft Docs (*"Số đo thắng tài liệu"*):**
   - When a task plan or design draft diverges from established codebase conventions, align with the actual codebase:
     - **Keep 4 Modules:** Place Phase 15 External Integrations inside `TeamNexus.Modules.Ai` (under `Services/Integrations`, `DTOs/Integrations`, `Endpoints`), communicating with `Board` via narrow ports (`ITaskAutomation`). Do **not** create a 5th `TeamNexus.Modules.Integrations` project.
     - **Minimal APIs Only:** Always implement HTTP routes as Minimal APIs in `Endpoints/*Endpoints.cs` registered via `Map*ModuleEndpoints()` (never MVC `*Controller.cs` classes, even if a task draft uses the word `Controller`).
     - **Match Real Entity & Service Names:** Use `AiActionLog` (`Action`, `BeforeSnapshot`, `AppliedSnapshot`), `BoardTask`, `IWorkspaceAccess`, and `BoardModuleException` + `DomainExceptionFilter`.
3. **Respect `"⛔ KHÔNG được làm"` (Do Not Do) Lists:**
   - Every phase task document defines explicit negative constraints (e.g., no schema changes in frozen-schema phases, no unapproved npm packages, no modifying locked DTO shapes). Treat violating an invariant as an immediate stop-and-report condition.
4. **Execution Logging (*Nhật ký thi hành*):**
   - Upon completing a phase or track, record honest, measured results in the task file and `03-roadmap.md`:
     - **`K1..Kn` (Key Decisions):** Concrete engineering choices made during implementation.
     - **`L1..Ln` (Intentional Deviations):** Any justified divergence from the initial plan and the technical reason.
     - **`BUG/P/T1..Tn` (Real Bugs Caught):** Bugs uncovered by tests during implementation and how they were fixed.
   - **Never fake metrics or silently lower Definition of Done (DoD) thresholds.** Report real build/test counts and real latency numbers.

---

## 3. Backend Architectural Invariants & Historical Pitfalls (.NET 10)

### 3.1. Module Dependency Direction (`Ai → Board` is Strictly One-Way)
- `TeamNexus.Modules.Board` **MUST NEVER** reference `TeamNexus.Modules.Ai` (`using TeamNexus.Modules.Ai.*` in `Board` is forbidden).
- **Port + Null-Default Override Pattern:** When `Board` needs a capability implemented in `Ai` (e.g., `IActivityLogWriter`, `INotificationWriter`, `IEmailGateway`, `IAiAgentResolver`, `IWorkspaceKnowledgeStore`):
  1. Declare the port interface and a safe no-op `Null*` default in `Board` (or `Shared`) and register it in `BoardModule.AddBoardModule`.
  2. Register the real implementation in `AiModule.AddAiModule` (which executes **after** `AddBoardModule` in `Program.cs`, overriding the default in DI).
- When `Ai` needs to mutate `Board` entities (e.g., appliers or GitHub webhook automations), `Board` exposes a narrow service port (`ITaskService`, `ITaskAutomation`).

### 3.2. Minimal APIs, Two-Layer RBAC & CSRF Protection
- **Minimal APIs:** Group routes in static `Endpoints/*Endpoints.cs` classes and attach `.AddEndpointFilter<DomainExceptionFilter>()` so domain exceptions (`NotFoundException`, `ForbiddenException`, `BadRequestException`, `ConflictException`, `AiProviderException`) map deterministically to `{ error, status }`.
- **CSRF Filter:** Attach `.AddEndpointFilter<AntiforgeryValidationEndpointFilter>()` on all cookie-authenticated state-changing endpoints (`POST`, `PUT`, `PATCH`, `DELETE`).
  - *Exception:* Server-to-server inbound webhooks (e.g., `POST /api/webhooks/github/{webhookToken:guid}`) must use `.AllowAnonymous()`, skip cookie/CSRF filters, enforce a strict request body size limit, and authenticate raw request bytes via `HMAC-SHA256` + `CryptographicOperations.FixedTimeEquals`.
- **Two-Layer RBAC:**
  - **Global Identity Role (`Admin` | `User`):** Only use `SystemAdminPolicy` for platform-wide administration.
  - **Workspace Role (`Admin` | `Manager` | `Member` in `workspace_members.role`):** All workspace/board/task endpoints use `.RequireAuthorization()` + service-layer verification via `IWorkspaceAccess`.
  - **Non-Member Privacy Rule:** Users outside a workspace must receive `404 Not Found` (never `403 Forbidden`, which leaks workspace existence). Members with insufficient role receive `403 Forbidden`.
  - **System Accounts (`IsSystem = true`):** Accounts like `GitHub Bot` (`00000000-0000-0000-0000-00000000b071`) must be blocked from interactive OAuth/JWT login and token refresh in `AuthService`.

### 3.3. Accountability Layer (`AiActionService` & `IAiActionApplier`)
- **Single Write Gateway:** AI-initiated data mutations must go through `AiActionService` (`Pending` → human `Approve`/`Reject` → reversible `Undo` via `before_snapshot` / `applied_snapshot`, or `Approved` with `RequestedByUserId = SystemUsers.GitHubBotUserId` + `BeforeSnapshot` / `AppliedSnapshot` for reversible GitHub webhook automations).
- **⚠️ Pitfall P2 (Never Open Nested Transactions in Appliers):** `AiActionService.ApproveAsync` and `UndoAsync` already open and commit the database transaction. Implementations of `IAiActionApplier` (and any domain methods they invoke) **MUST NOT** call `_db.Database.BeginTransactionAsync()`, which causes a `500` crash at runtime.
- **⚠️ Pitfall T1 (Call `SaveChangesAsync` in Direct-DbContext Appliers):** `AiActionService.ApproveAsync` commits the transaction but does **not** call `_db.SaveChangesAsync()` itself. If an `IAiActionApplier` adds/modifies entities directly on `TeamNexusDbContext` (rather than calling a service that saves), it **MUST** call `await _db.SaveChangesAsync(ct)` in both `ApplyAsync` and `UndoAsync`.
- **Subtask Delegation Snapshot:** Appliers that delegate task creation to `CreateSubtasksApplier` (such as `RecoveryPlanApplier` for `SplitSubtasks`) must copy `createdTaskIds` to their own `applied_snapshot` so `UndoAsync` can soft-delete the created tasks.

### 3.4. Append-Only DTO Contract Rule
- Existing response/request positional records (`TaskResponse`, `ColumnResponse`, `BoardResponse`, `DashboardResponse`, `WorkspaceMemberResponse`, `AiActionLogResponse`, `ExternalAlertMessage`) are locked contracts shared with tests and the upcoming Phase 16 Flutter mobile app.
- **Never rename, remove, or reorder existing positional parameters.** Always append new fields at the very end with default values (e.g., `long TaskNumber = 0, IReadOnlyList<TaskExternalLinkSummaryResponse>? ExternalLinks = null`).

### 3.5. Hybrid AI (Jev 1.13 System-1 + DeepSeek System-2) & Security Guards
- **Jev 1.13 Decisions API (`IJevDecisionProvider`):**
  - Calls `POST {Jev:BaseUrl}{Jev:DecisionsPath}` (`/api/alpha/decisions`), **never** `/chat/completions`.
  - Do not compare `response.model` with `request.model` (OpenRouter appends date suffixes).
  - Jev `noul` responses only contain `{ type, noul }` with **no `confidence` field** on the wire—any confidence score for `noul` must be mathematically derived from `|noul - 0.5|` and explicitly documented as derived.
  - **Confidence Routing (`ConfidenceRouting`):** Enforced in C# (`>= 0.85` → `DirectAssign`, `0.50..0.85` → `OfferChoices`, `< 0.50` → `ManualRequired` with suggested values nulled out). Note: A valid Jev response (`LatencyMs > 0`) with confidence `< 0.50` (`ManualRequired`) is a normal low-confidence result—do **not** set `Degraded = true` unless the provider actually failed or fell back (`LatencyMs == 0`).
- **Zero GUID / PII Leakage:**
  - Never send user `email` or raw `Guid` identifiers to Jev (map candidates to opaque server labels like `cand-1`, `cand-2` and strip any GUID patterns in validators).
  - Keep `AiChatAnswerSanitizer` (C#) and `redactIdentifiers.ts` (Frontend) active on all chat outputs while leaving raw JSON action proposal blocks intact.
- **Zero Secret Leakage & SSRF Guard:**
  - Encrypt third-party secrets (e.g., GitHub webhook secrets) at rest using `IDataProtectionProvider`. Return plaintext secrets **at most once** (on initial creation or explicit rotation); subsequent `GET` requests must only return a masked hint.
  - Validate all outbound webhook URLs (`WebhookUrlValidator`): enforce `https://`, allow-list `hooks.slack.com` (`/services/...`) and `discord.com`/`discordapp.com` (`/api/webhooks/...`), and block all IP literals, `localhost`, loopback (`127.0.0.0/8`, `::1`), link-local (`169.254.0.0/16`), and RFC1918 private ranges.
- **Deterministic Offline Fakes (`HasApiKey` Switch):**
  - When API keys (`DeepSeek:ApiKey`, `Jev:ApiKey`, `Tavily:ApiKey`, `Embeddings:ApiKey`, `Email:ApiKey`) are empty, DI wires offline deterministic fakes (`FakeAiProvider`, `FakeJevDecisionProvider`, `FakeWebSearchProvider`, `FakeEmbeddingProvider`, `NullEmailSender`).
  - **Never** alter `AgentChatPrompts.Marker` (`"AI Agent Chat của TeamNexus"`) or `FAKE:JEV_*` state sentinels relied upon by offline test suites.

### 3.6. Database, EF Core 10 & BackgroundService Patterns
- **Single `TeamNexusDbContext`:** All tables and columns use `snake_case`. Primary keys are `Guid`. Enums are stored as `text` with SQL `CHECK` constraints.
- **UTC Timestamps Only:** Normalize all `DateTimeOffset` inputs (e.g., `DueDate`) to UTC (`Offset == TimeSpan.Zero`) before saving to `timestamptz` columns; Npgsql throws on non-zero offsets.
- **⚠️ Pitfall T6 (Matching Global Query Filters):** Whenever a soft-deleted entity (`deleted_at == null` filter) is the required principal of a child entity (e.g., `WorkspaceKnowledgeDocument` → `WorkspaceKnowledgeChunk`), configure a matching query filter on the child entity to prevent EF Core warning `10622` from failing the `0 Warning` build gate.
- **⚠️ Pitfall T7/T8 (Migration Discipline):**
  - Always run a full build **before** running `dotnet ef migrations add` (never pass `--no-build` against stale binaries).
  - Always inspect the generated migration file: write raw `migrationBuilder.Sql(...)` for `CREATE EXTENSION IF NOT EXISTS vector;` (`HasPostgresExtension` only writes an annotation) and for PostgreSQL expression indexes.
  - Run `dotnet ef migrations has-pending-model-changes` to verify zero model drift.
- **Scoped Runner + Thin `BackgroundService` Pattern:**
  - Always extract background pass logic into a scoped service (`DailyDigestRunner`, `AgentRunReaperRunner`, `KnowledgeEmbeddingRunner`, `WebhookOutboxRunner`) and keep the `BackgroundService` as a thin timer wrapper that creates a scope and invokes the runner. This allows integration tests to execute background passes deterministically without sleeping.

---

## 4. Frontend & UI/UX Standards (Luminous Enterprise v4.2)

1. **Design System & Styling:**
   - Use **Ant Design v6 (`antd`)** components configured via `luminousTheme` in `frontend/src/App.tsx` and the `--tn-*` CSS custom properties in `frontend/src/index.css`.
   - **Typography:** `Be Vietnam Pro` for UI copy (optimized for Vietnamese diacritics) and `JetBrains Mono` (`var(--tn-font-mono)`) for technical badges (`TN-123`, `#TASK-ID`, latency `ms`, confidence `%`, `Ctrl+K`, vector dimensions).
   - **Semantic Palette:** Canvas `#f8fafc`, Surface `#ffffff`, Subtle `#f1f5f9`, Brand Primary `#4f46e5` (Royal Indigo), AI Accent `#7c3aed` (Electric Violet), Healthy `#10b981`, Warning `#f59e0b`, Danger `#ef4444`.
   - **Strict Prohibitions:** Do **not** use Tailwind CSS classes. Do **not** install new npm packages without explicit user approval. Do **not** use `dangerouslySetInnerHTML`.
2. **Layout & Vietnamese UI Polish Rules:**
   - Prevent mid-word wrapping on badges, headers, and pills using `whiteSpace: 'nowrap'` and `flexShrink: 0`.
   - Never apply `textDecoration: 'line-through'` to completed task titles (strikethrough obscures Vietnamese diacritics); use muted text (`#94a3b8`) + `opacity: 0.85` + a green check icon (`✔`).
   - Do not duplicate workspace-level navigation buttons (`Báo cáo`, `Hoạt động`, `Cài đặt`) inside child page toolbars that already render inside `WorkspaceShell` (Left Sidebar).
   - All user-facing copy, empty states, alerts, and error messages must be written in clear, natural **Vietnamese**.
3. **Data Fetching, SSE Streaming & Testing:**
   - Follow the established hook pattern (`useState + useEffect + reload` or Zustand stores) and use `httpClient` (`frontend/src/shared/api/httpClient.ts`) for REST API calls.
   - For Server-Sent Events (SSE) endpoints (`/ai-chat/stream`, `/agent-chat/stream`), always use `fetch` + `ReadableStream` + `TextDecoder` + `getXsrfToken()` (`credentials: 'include'`) and check `res.ok` before reading the stream. Never use `EventSource`.
   - **Preserve `data-testid` & Existing Tests:** Never remove or rename existing `data-testid` attributes. Never delete or weaken existing passing tests in `*.test.ts(x)`. Provide safe fallbacks (e.g., falling back to `#${task.id.slice(0, 8).toUpperCase()}` when `task.taskNumber` is undefined in older test fixtures).

---

## 5. Build, Test & Verification Commands

### Backend (.NET 10 + PostgreSQL 18 `pgvector`)
```powershell
# 1. Build entire solution (use -m:1 -nr:false in agent/sandbox shells to prevent MSBuild named-pipe hangs)
# Gate: MUST produce 0 Warning(s), 0 Error(s)
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental

# 2. Verify zero EF Core model drift
dotnet ef migrations has-pending-model-changes --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api

# 3. Run Pure unit tests (fast, zero database required)
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build --filter "FullyQualifiedName~Pure"

# 4. Run full Backend test suite (requires pgvector/pgvector:pg18 container on localhost:5432 or TEAMNEXUS_TEST_DB)
# Gate: Failed = 0, Skipped = 0, Total >= CI baseline (.github/workflows/ci-backend.yml)
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build
```
> **Note on Local PostgreSQL Image:** Always use `pgvector/pgvector:pg18` (not `postgres:18`, which lacks the third-party `vector` extension required by `Phase147KnowledgeBase` migration):
> `docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d pgvector/pgvector:pg18`

### Frontend (Node 24 + Vite 8 + Vitest)
Run from the `frontend/` directory:
```powershell
# 1. Static lint check — Gate: 0 warnings, 0 errors
npm run lint

# 2. TypeScript typecheck — Gate: exit code 0
npx tsc -b

# 3. Unit & component tests — Gate: 100% PASS, Total >= CI baseline (.github/workflows/ci-web.yml)
npm test

# 4. Production bundle build — Gate: exit code 0
npm run build
```
> **CI Baseline Rule:** Only raise the minimum test count thresholds in `.github/workflows/ci-backend.yml` and `.github/workflows/ci-web.yml` at the end of a phase using the actual measured test counts.

