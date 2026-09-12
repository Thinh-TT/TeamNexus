# Giai đoạn 8 – Hoàn thiện, Test & Deploy

> **Mục tiêu:** đưa TeamNexus từ "chạy được ở máy dev" thành **một sản phẩm có URL công khai để đưa vào CV/demo**: viết test tự động thật
> (xUnit + Vitest), dựng CI/CD bằng GitHub Actions, deploy 3 tầng lên hạ tầng free-tier, xử lý cold-start ảnh hưởng SignalR, và rà soát
> lại UI/UX tổng thể.
>
> **Khác biệt so với các giai đoạn 3–7:** đây **không phải** giai đoạn feature. Phần lớn công việc là *test, hạ tầng, cấu hình và rà soát* —
> chỉ có **3 vá mã nguồn nhỏ** (§3) là bắt buộc để bản deploy thật sự chạy được. Vì vậy tài liệu này **giữ tinh thần** của
> `phase-4..7` (mục lục theo §, bảng quyết định D, non-goals) nhưng **giảm nghi thức format**: không có bảng "trạng thái đầu vào S1..S18",
> không có mục "bàn giao", không cần mọi hạng mục phải là checkbox code.
>
> **Công nghệ:** xUnit (+ `WebApplicationFactory<Program>`) · PostgreSQL 18 thật cho test (không dùng EF InMemory) · Vitest + React Testing Library ·
> GitHub Actions · Render (API) · Neon (PostgreSQL) · Vercel (frontend) · `@microsoft/signalr` với retry policy tuỳ biến.
>
> **Tham chiếu:** `03-roadmap.md` (Giai đoạn 8 — 5 ô hoàn thiện) · `02-tech-stack-decisions.md` §3, §4 · `01-system-specification.md`
> · `04-database-design.md` §6 (chiến lược migration) · `tasks/phase-7-ai-agent-executor.md` (D17, D19 — lý do giai đoạn này mới làm test) ·
> `README.md` (mục "Chạy ở local", cách chạy migration trong sandbox).

---

## 0. Bối cảnh & quyết định kiến trúc (đã chốt)

Đọc hết mục này trước khi bắt đầu. Các quyết định dưới đây **đã chốt, không chọn lại**.

### 0.1 Sự thật đã kiểm chứng trong repo

| # | Sự thật | Nguồn kiểm chứng | Hệ quả cho Giai đoạn 8 |
|---|---|---|---|
| B1 | **Không có** project test nào trong repo; verify Phase 2–7 làm bằng **harness tạm ngoài workspace**, đã xoá sau khi chạy | `glob **/*.Tests.csproj` → 0 kết quả; phase-7 D17 | §2 phải tạo project test **từ số 0** và các hàm thuần của Phase 4–7 (đã được viết `public static` đúng D19) chính là "mỏ than" để viết test nhanh |
| B2 | **Không có** thư mục `.github/` | `Test-Path .github` → `False` | §4 làm CI/CD hoàn toàn mới |
| B3 | Frontend đang có **35 test files / 187 tests PASS**, `oxlint` 0/0, `tsc -b` exit 0, `vite build` OK | `README.md` mục Giai đoạn 7 | Baseline phải được **giữ hoặc vượt**, không được tụt |
| B4 | **6 migration**, mới nhất `Phase7AiAgentSchema`; app **không** tự động migrate lúc boot | `src/TeamNexus.Persistence/Migrations/`; `Program.cs` không có `.Migrate()` | Migration lên DB cloud phải chạy **bằng lệnh thủ công** (§5.7). Đây là quyết định có ý thức (D12), không phải thiếu sót |
| B5 | `Program.cs` đã có `public partial class Program { }` với ghi chú "dành cho test/harness" | `Program.cs` dòng 140–147 | `WebApplicationFactory<Program>` dùng được ngay, không cần sửa `Program.cs` |
| B6 | Cookie auth dùng `SameSite=Lax` + `Secure=SameAsRequest`, áp cho **cả** token cookie (`TokenCookieService`) **và** antiforgery cookie (`DependencyInjection`) | `TokenCookieService.cs` 35–51; `DependencyInjection.cs` 188–195 | **Đây là blocker số 1 khi deploy tách domain** (FE Vercel ↔ API Render là khác site). Xem D7 + §3.2 |
| B7 | SignalR client hard-code `/hubs/board` và dùng `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])`; sau khi hết 5 lần thử thì `onclose` → `disconnected` và **không bao giờ tự thử lại** | `frontend/src/features/board/hooks/useBoardHub.ts` 49–57, 132–135 | Cold-start (API thức dậy ~1 phút) **vượt** dải reconnect này ⇒ §6 là hạng mục thật, không phải "cho đẹp" |
| B8 | `useBoardHub` **có** re-join group khi reconnect, **nhưng không refetch dữ liệu** | `useBoardHub.ts` 120–130 | Sự kiện xảy ra trong lúc mất kết nối **mất vĩnh viễn** với client đó ⇒ phải thêm refetch (§6) |
| B9 | `HubConnectionStatus = 'connected' \| 'connecting' \| 'reconnecting' \| 'disconnected'`; `useBoard` đã expose sẵn `refetch`; `BoardView` đã nhận `refetch` và đã có hàm `renderConnectionStatus` | `board.types.ts` 159; `useBoard.ts` 332–341; `BoardView.tsx` 66–106 | §6 **không cần** state/UI mới — chỉ mở rộng đúng chỗ đã có |
| B10 | `AgentRunReaper` (`IHostedService`) dọn run mồ côi lúc boot; `ObserverBackgroundService` là `PeriodicTimer` in-process | `AiModule.cs` 95, 160; `AgentRunReaper.cs` | Free-tier sleep giết background service giữa chừng — **lưới an toàn đã có sẵn**, §6 không phải sửa backend |
| B11 | OAuth redirect URI được dựng **động** `{Scheme}://{Host}/api/auth/external-login`; callback path cố định `/api/auth/callback/{github\|google}`; sau login redirect về `Frontend:BaseUrl` | `AuthEndpoints.cs` 85, 100; `AuthConstants.cs` 30–32 | Không phải hard-code domain trong code, chỉ cần **cập nhật redirect URI ở console của Google/GitHub** + set `Frontend__BaseUrl` (§5.5) |
| B12 | OpenAPI + Scalar **chỉ** được map khi `IsDevelopment()` | `Program.cs` 57–65 | Production **không** có `/scalar` và `/openapi` — giữ nguyên (D14) |
| B13 | Các key đang **rỗng** trong `appsettings.json`: `ConnectionStrings:DefaultConnection`, `Jwt:SigningKey`, `Authentication:Google:*`, `Authentication:GitHub:*`, `DeepSeek:ApiKey`, `Tavily:ApiKey`. `Cors:AllowedOrigins` và `Frontend:BaseUrl` đang trỏ localhost | `src/TeamNexus.Api/appsettings.json` | Bảng env của §5.6 chính là danh sách này được "dịch" sang biến môi trường |
| B14 | Frontend lấy API base từ `VITE_API_BASE_URL` (mặc định `/api`), **nhưng hook SignalR thì không dùng biến này** | `httpClient.ts` 22–23; `useBoardHub.ts` 50 | Nếu FE ở Vercel và API ở Render mà vẫn để `/hubs/board` tương đối thì WebSocket sẽ gọi vào **chính Vercel** ⇒ real-time chết. Đây là vá §3.3 (D8) |
| B15 | Máy dev hiện có: .NET SDK `10.0.201` (+ `8.0.417`), `dotnet ef` `10.0.8`, Node `v24.15.0`, npm `11.12.1` | `dotnet --list-sdks`, `node --version` | Lệnh trong tài liệu viết theo đúng toolchain này; CI phải ghim `10.0.x` và Node 22/24 |

### 0.2 Bảng quyết định

| # | Quyết định | Lý do / ghi chú |
|---|---|---|
| **D1** | Backend test = **project xUnit thật** tại `tests/TeamNexus.Api.Tests/`, dùng `WebApplicationFactory<Program>` | B1 + B5. Phase 7 đã cố ý hoãn test sang Phase 8 (D17 của phase-7); giờ là lúc dựng nền vĩnh viễn thay vì harness tạm |
| **D2** | **KHÔNG** dùng EF InMemory / SQLite. Test tích hợp chạy trên **PostgreSQL thật** | Schema thật dùng `jsonb`, `bytea`, `pg_try_advisory_lock`, CHECK constraint, partial unique index, query filter — InMemory/SQLite sẽ cho kết quả **sai lệch mà vẫn xanh**, tệ hơn là không test |
| **D3** | Nguồn DB cho test lấy từ env `TEAMNEXUS_TEST_DB`; mặc định `Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=postgres`. CI dùng `services: postgres:18` | Một biến duy nhất dùng được cho cả local và CI; không đụng database `TeamNexus` đang dùng để dev/demo |
| **D4** | Test cần DB sẽ **skip kèm thông báo** khi không kết nối được (không fail cứng); test thuần thì luôn chạy | Dev mới clone repo (hoặc chỉ sửa frontend) vẫn chạy được `dotnet test` phần lớn; đồng thời CI vẫn phải fail nếu **có** DB mà test sai |
| **D5** | Test AI **offline 100%** qua `FakeAiProvider` / `FakeWebSearchProvider`. Test gọi DeepSeek/Tavily thật bị gate sau env `TEAMNEXUS_TEST_REAL_AI=1`, mặc định **skip** | Đúng cách Phase 3–7 đã verify (0 token) và đúng README: đặt `DeepSeek__ApiKey = " "` (một khoảng trắng) để fake thắng User Secrets |
| **D6** | Ưu tiên test theo **giá trị**, không theo coverage %. Ưu tiên 1 = hàm thuần đã có sẵn; 2 = Auth + Kanban; 3 = Smart Setup + Accountability; 4 = Agent + Reporting | Test hết là bất khả thi với một người; ưu tiên theo "hỏng cái này thì sản phẩm chết" thay vì theo con số |
| **D7** | Vá cookie cho cross-site: thêm option `Auth:CookieSameSite` ∈ {`Lax`, `None`}, mặc định `Lax`; áp cho **token cookie và antiforgery cookie**; giữ `Secure=SameAsRequest` | B6. Giữ mặc định `Lax` ⇒ hành vi local **không đổi** (không hồi quy 187 test). Production đặt `None` ⇒ cookie đi được cross-site. Nếu chỉ vá token cookie mà quên antiforgery thì mọi POST/PUT/DELETE sẽ **403 CSRF** trên production và rất khó đoán ra |
| **D8** | Vá SignalR URL: thêm hàm thuần `resolveHubUrl()` đọc `VITE_API_BASE_URL` | B14. Hàm thuần ⇒ test Vitest được, và sửa đúng một chỗ thay vì rải base URL khắp nơi |
| **D9** | Cold-start: thay `withAutomaticReconnect(array)` bằng **`IRetryPolicy` thử lại vô hạn có trần backoff** + nút "Kết nối lại" thủ công + **refetch board khi reconnect** | B7 + B8. Render free ngủ sau 15 phút, thức ~1 phút — dài hơn nhiều so với 5 lần thử trong ~47 giây hiện tại |
| **D10** | CI = **2 workflow** (`ci-backend`, `ci-web`) chạy trên `push` + `pull_request`; **không** đặt secret nào trong CI | Build/test không cần key thật (D5). Không giữ `Jwt:SigningKey`/API key trong GitHub ⇒ giảm bề mặt rò rỉ |
| **D11** | Deploy dựa vào **auto-deploy của Render + Vercel** (push lên GitHub → tự build & deploy). GitHub Actions **không** có job deploy | Render/Vercel đã làm đúng việc deploy tốt hơn YAML tự viết, và như vậy **không** phải đưa DB connection string vào CI. Đánh đổi: không có "migration tự động sau deploy" ⇒ xem D12 |
| **D12** | Migration lên DB cloud chạy bằng **lệnh thủ công** `dotnet ef database update` với connection string của Neon, **không** migrate lúc boot | B4. Auto-migrate lúc boot trên free-tier (nhiều instance, cold-start, deploy trùng) dễ gây race; một người, vài lần deploy/tháng ⇒ thủ công rẻ hơn và an toàn hơn |
| **D13** | Đường chính: API = **Render**, DB = **Neon**, FE = **Vercel**. Railway / Supabase / Netlify chỉ là **phương án B** với hướng dẫn ngắn | Render là nền tảng free-tier duy nhất trong danh sách còn cho **web service chạy 24/7 kiểu free** (có spin-down) mà không cần thẻ; Neon Free ngủ 5 phút rồi **tự thức**, còn Supabase Free **pause cả project sau 1 tuần** phải bấm restore tay (nguồn: [Render – free tier 2026](https://render.com/articles/platforms-with-a-real-free-tier-for-developers-in-2026), [Neon vs Supabase Free Plan](https://neon.com/guides/neon-vs-supabase-free-plan)) |
| **D14** | **Giữ nguyên** việc ẩn OpenAPI/Scalar ở production | B12. Muốn mở cho nhà tuyển dụng xem API thì chỉ cần 1 dòng điều kiện (`§7.3`), nhưng mặc định an toàn hơn |
| **D15** | Báo cáo test của giai đoạn này là `report/phase-8-completion-test-deploy-report.md`, ghi **số thật** (số test, số check, URL, thời gian cold-start đo được) | Giữ thông lệ Phase 4–7; nhưng vì giai đoạn này "sản phẩm" là hạ tầng, báo cáo phải có **bằng chứng chạy được** (URL, log, ảnh) chứ không chỉ số test |

### 0.3 Non-goals Giai đoạn 8 (ghi rõ để không over-scope)

Flutter/mobile (Giai đoạn 9); tối ưu hiệu năng hay read-model; observability nâng cao (Sentry, OpenTelemetry, tracing phân tán);
auto-scaling; custom domain; load test/stress test; chạy test trên nhiều OS (chỉ `ubuntu-latest`); ngưỡng coverage cứng (không đặt gate %);
test UI end-to-end bằng Playwright/Cypress; đổi REST contract hiện có; đổi schema/DB (không migration mới); thêm provider AI;
tự động migrate lúc boot; multi-region; SSR.

### 0.4 Sơ đồ luồng tổng thể của giai đoạn

```
                        GitHub (monorepo: src/, frontend/, tests/, .github/)
                                   │ push / pull_request
             ┌─────────────────────┴─────────────────────┐
             ▼                                           ▼
   [CI] ci-backend.yml                        [CI] ci-web.yml
   ubuntu + .NET 10 + postgres:18             ubuntu + Node 22/24
   restore → build → dotnet test              npm ci → lint → tsc → test → build
             │                                           │
             └──────────────── push lên main ────────────┘
                                   │
        auto-deploy (không cần secret trong CI — D11)
        ┌──────────────────────────┼───────────────────────────┐
        ▼                          ▼                           ▼
   Render                     Neon                        Vercel
   ASP.NET Core API           PostgreSQL 18               React static (dist)
   /api/health (health check) 6 migration đã áp           SPA rewrite → index.html
   container ảo ngủ sau 15'   compute ngủ sau 5'          100 GB bandwidth
        │                          │                            │
        └────── Npgsql + SslMode=Require ─────┘                 │
        ◄─────────── XHR (cookie SameSite=None) ─────────────────┘
        ◄─────────── WebSocket /hubs/board (retry vô hạn) ───────┘
```

---

## 1. Checklist tổng theo §

| § | Hạng mục | Trạng thái |
|---|---|---|
| §2 | **Test backend** — project `tests/TeamNexus.Api.Tests`, fixture DB thật, nhóm test ưu tiên 1–4 | ✅ **XONG** — **172 test PASS** (0 fail / 0 skip / 0 warning) trên PostgreSQL 18 thật; chi tiết ở §2.8 |
| §2b | **Test frontend bổ sung** — `hubUrl`, `reconnectPolicy`, `useBoardHub` reconnect, `BoardView` nút kết nối lại | 🔻 **ĐÃ BÀN GIAO** — `tasks/phase-8-2b-frontend-handover.md` (gộp §2b + §3.2 + §3.3) |
| §3 | **3 vá mã nguồn bắt buộc** — cookie cross-site (D7), SignalR URL (D8), reconnect policy (D9) | **§3.1 XONG** ✅ (172/172 test xanh) · §3.2 + §3.3 gộp vào lượt §2b của Antigravity |
| §4 | **CI/CD** — `.github/workflows/ci-backend.yml` + `ci-web.yml`, badge ở README | [ ] |
| §5 | **Deploy 3 tầng** — Neon → Render → Vercel + OAuth console + migration + checklist deploy lần đầu | [ ] |
| §6 | **Cold-start & SignalR reconnect** — retry vô hạn, refetch khi reconnect, UX thông báo, tiêu chí nghiệm thu 90 s | [ ] |
| §7 | **Rà soát UI/UX** — checklist 1 vòng toàn app + sửa các mục trong danh sách chốt + CV readiness | [ ] |
| §8 | Migrations & công việc bên ngoài repo (OAuth console, tài khoản cloud) | [ ] |
| §9 | Definition of Done + nghiệm thu + ma trận rủi ro + báo cáo `report/phase-8-*` | [ ] |

> **Thứ tự thi hành đề xuất:** §2 → §2b → §3 → §4 → §5 → §6 → §7 → §9.
> Lý do: §3 phải xong **trước** §5 (nếu không, deploy xong cũng không đăng nhập được); §4 nên xong **trước** §5 để mọi thứ đẩy lên đã được
> kiểm tra tự động; §6 cần §5 xong mới đo được cold-start thật.

---

## 2. [Test] Backend — project xUnit

> Mục tiêu: có `dotnet test` chạy được **cả ở local và trên CI**, tạo nền test vĩnh viễn thay cho harness tạm của Phase 2–7.

### 2.1 Dựng project

- [ ] Tạo thư mục `tests/TeamNexus.Api.Tests/` chứa `TeamNexus.Api.Tests.csproj`:
  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
      <IsPackable>false</IsPackable>
    </PropertyGroup>
    <ItemGroup>
      <PackageReference Include="Microsoft.NET.Test.Sdk" Version="..." />
      <PackageReference Include="xunit" Version="..." />
      <PackageReference Include="xunit.runner.visualstudio" Version="..." />
      <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.x" />
      <PackageReference Include="FluentAssertions" Version="..." />
      <PackageReference Include="Npgsql" Version="10.0.x" />
    </ItemGroup>
    <ItemGroup>
      <ProjectReference Include="..\..\src\TeamNexus.Api\TeamNexus.Api.csproj" />
    </ItemGroup>
  </Project>
  ```
  - `TargetFramework`/`Nullable`/`ImplicitUsings` **kế thừa** từ `Directory.Build.props` ở gốc repo — không khai lại.
  - Ghim version cụ thể khi tạo (không để `*`); dùng `dotnet add package` để lấy bản mới nhất tương thích .NET 10 rồi **chốt số** vào file.
  - `Microsoft.AspNetCore.Mvc.Testing` phải **trùng major với ASP.NET Core của app** (10.x) để tránh lệch `Microsoft.Extensions.*`.
- [ ] Thêm project vào `TeamNexus.sln` dưới solution folder `tests` (giữ nguyên cấu trúc folder hiện có của các project khác).
- [ ] ⚠️ **Không** sửa `Directory.Build.props` để bật warning-as-error cho toàn repo — file đó dùng chung cho cả 6 project sản phẩm; nếu muốn siết thì đặt riêng trong csproj test.
- [ ] Đảm bảo `dotnet build TeamNexus.sln` vẫn **0 warning / 0 error** và `dotnet ef migrations list` vẫn = **6**.

### 2.2 Hạ tầng test (fixture)

- [ ] `TestDatabase.cs`:
  - Đọc `TEAMNEXUS_TEST_DB`; nếu rỗng dùng default của D3.
  - `CanConnect()` — probe nhanh (`NpgsqlConnection.Open` + `SELECT 1`) trong thời gian ngắn; thất bại ⇒ đặt cờ `Database.Unavailable` cho `Skip.If` (D4) và **in ra hướng dẫn** cần `docker run postgres:18` hoặc set env.
  - `EnsureMigrated()` — chạy `DbContext.Database.Migrate()` **một lần** cho cả process test (dùng `static` + lock), không migrate lại mỗi test class.
  - `ResetAsync(DbContext)` — xoá dữ liệu giữa các test class theo **thứ tự ngược FK** (`task_attachments` → `agent_runs` → `ai_action_logs` → `notifications` → `activity_logs` → `ai_observer_runs` → `task_comments` → `task_labels` → `tasks` → `board_columns` → `boards` → `labels` → `workspace_members` → `refresh_tokens` → `users` → `workspaces`), **tôn trọng** bảng có soft-delete (`DELETE` thật cho test, vì đây là DB test).
- [ ] `TeamNexusApiFactory.cs` — `WebApplicationFactory<Program>` override cấu hình in-memory:
  - `ConnectionStrings:DefaultConnection` = connection string test.
  - `Jwt:SigningKey` = chuỗi test ≥ 32 byte (hằng số trong code test, **không** secret thật).
  - `Authentication:Google:*` và `Authentication:GitHub:*` **để rỗng** ⇒ provider không được đăng ký ⇒ `/api/auth/login/google` trả 400 (đúng thiết kế "app vẫn boot với một tập con provider").
  - `DeepSeek__ApiKey = " "` (một khoảng trắng) để `FakeAiProvider` thắng User Secrets (D5) — **không** dùng chuỗi rỗng, vì .NET coi env rỗng là "unset".
  - `Agent:Enabled`, `Observer:Enabled`, `Reports:Enabled` = `true` mặc định (test 503 thì override riêng).
  - **Tắt `ObserverBackgroundService` trong test mặc định** (đặt `Observer:IntervalMinutes` rất lớn hoặc thêm cờ test) để background quét không làm nhiễu assert.
- [ ] Helper dựng JWT test: tự ký token bằng cùng `SigningKey` + issuer/audience trong config ⇒ gọi API như một user đã đăng nhập mà không cần OAuth thật (đúng cách Phase 6–7 đã làm).
- [ ] Helper `HttpClient` đã gắn cookie + header `X-XSRF-TOKEN` cho endpoint có CSRF (gọi `/api/auth/antiforgery` trước, lấy cookie, gắn header).

### 2.3 Nhóm test — Ưu tiên 1: hàm thuần (không DB, chạy siêu nhanh)

> Đây chính là "cổ tức" của quyết định D19 ở phase-7: mọi hàm quyết định đã là `public static` **chính là để** giai đoạn này test.

- [ ] **Agent guardrails** (`AgentGuardrails`): 4 ngưỡng `MaxToolCalls`=15 · `RunTimeoutSeconds`=300 · `MaxRunTokens`=50 000 · `MaxRunLlmCalls`=20 — mỗi ngưỡng test **đúng tại biên** (`= ngưỡng` chưa vượt, `= ngưỡng + 1` đã vượt) và **thứ tự ưu tiên** khi nhiều ngưỡng cùng vượt (phải ra đúng một `stop_reason` xác định).
- [ ] **Cắt `tool_call_trace`**: vượt `ToolTraceMaxEntries`=30 ⇒ `trace_truncated = true`, giữ đúng số entry, cắt theo **entry** không cắt giữa chuỗi JSON; `ToolTraceResultChars`=500 cắt từng kết quả.
- [ ] **Chọn Comment vs Attachment**: `DraftOutput` ≤ `AttachmentThresholdChars`=2000 ⇒ comment; lớn hơn ⇒ attachment; đúng tại biên.
- [ ] **Tên file attachment**: dùng `ReportFileName.Slugify` — loại `..`, `/`, `\`, ký tự ngoài ASCII; tên rỗng/khoảng trắng ⇒ fallback; độ dài sau slug không vượt giới hạn header.
- [ ] **Observer thuần**: `ObserverSignalDetector` (4 tín hiệu `OverdueTask`/`StalledTask`/`Overload`/`Bottleneck` với ngưỡng config — test tại biên), `ObserverFindingValidator` (chặn hallucination: evidence id lạ, severity lạ, title quá dài), `ObserverSummarizer` (cắt prompt theo `MaxPromptCharacters` **không** cắt giữa JSON; 0 tín hiệu ⇒ không gọi AI), `ObserverSeverity` (so sánh `MinSeverityToNotify`).
- [ ] **Reporting thuần**: `ReportAggregator` (progress/performance/byBoard/byAssignee/activity/health; **đếm `activeUsers` không trùng** — đây là bug thật đã bắt ở Phase 6), `ReportThresholds.BuildRange` (mặc định 30 ngày, clamp 365, `from > to` ⇒ 400), `ReportFileName.Slugify`.
- [ ] Ước lượng: nhóm này nên cho **≥ 120 test case** mà chạy dưới vài giây và **không cần DB** — đây là phần chống hồi quy rẻ nhất của cả dự án.

### 2.4 Nhóm test — Ưu tiên 2: Auth + Kanban (cần DB)

- [ ] **Auth**
  - [ ] `GET /api/health` ⇒ 200 (không cần auth) — dùng luôn làm smoke test.
  - [ ] Gọi endpoint `RequireAuthorization` **không** có token ⇒ **401**.
  - [ ] Token sai chữ ký / hết hạn ⇒ **401** (test `ClockSkew` 1 phút ở biên).
  - [ ] `POST` endpoint có CSRF mà **thiếu** header `X-XSRF-TOKEN` ⇒ **403**; có header ⇒ thành công.
  - [ ] `POST /api/auth/refresh` ⇒ cấp access token mới **và rotate refresh token**; dùng lại refresh token **cũ** ⇒ **401** (replay bị chặn).
  - [ ] `POST /api/auth/logout` ⇒ cookie bị xoá (`Set-Cookie` hết hạn) và refresh token trong DB bị thu hồi.
  - [ ] `GET /api/auth/login/google` khi provider chưa cấu hình ⇒ **400** (không phải 500).
- [ ] **Kanban CRUD**
  - [ ] Board: tạo/list/get/update/delete; user **ngoài** workspace ⇒ **404/403** (assert đúng mã, không assert "khác 200").
  - [ ] Column: tạo với `position`, reorder nhiều cột ⇒ `UQ (board_id, position)` không bị vỡ; cột `is_done` ⇒ kéo task vào set `completed_at`, kéo ra ⇒ `null`.
  - [ ] Task: CRUD; di chuyển giữa column ⇒ `column_id` + `position` đổi đúng; tạo task với `assigneeId` **ngoài** workspace ⇒ **400** (đúng bản vá §3.2 của phase-7 — test này là *regression guard* cho lỗ hổng đã từng tồn tại).
  - [ ] Comment: tạo/xoá; comment soft-delete không xuất hiện trong `GET`.
  - [ ] `board_columns.is_clarification`: trùng cờ `is_done` ⇒ **400**; xoá cột "Chờ làm rõ" ⇒ **409** (kể cả khi cột rỗng).
- [ ] **SignalR (tuỳ chọn nhưng nên có 1–2 test)**: dùng `HubConnection` client thật tới `TestServer`/Kestrel: `JoinBoard` khi **không** là member ⇒ bị từ chối; member nhận được event khi client khác đổi task (group isolation: client của board khác **không** nhận).

### 2.5 Nhóm test — Ưu tiên 3: Smart Setup + Accountability

- [ ] `FakeAiProvider` trả JSON **sai schema** (thiếu field, sai kiểu, `title` rỗng) ⇒ **400**/`{ error }` và **không** có row nào ghi vào `tasks`.
- [ ] `POST /api/boards/{id}/smart-setup` với fake hợp lệ ⇒ trả về đề xuất, DB **không** đổi (đúng bất biến "chưa ghi thẳng vào DB").
- [ ] `AiActionService`: tạo log `Pending` ⇒ **chưa** có task; `approve` ⇒ task xuất hiện đúng số lượng/nội dung; `reject` ⇒ không ghi gì và trạng thái = `Rejected`.
- [ ] **Undo** hành động `CreateSubtasks` ⇒ task soft-delete, log = `Undone`.
- [ ] **CAS chống apply trùng**: `approve` hai lần cùng lúc/hai lần liên tiếp ⇒ lần thứ hai **409**, và **chỉ một** bộ task được ghi.
- [ ] Resolve assignee/label: tên assignee không khớp ai ⇒ xử lý xác định (không tạo user), label chưa có ⇒ xử lý theo hành vi đã chốt ở Phase 3.

### 2.6 Nhóm test — Ưu tiên 4: Agent Executor + Reporting

- [ ] Tạo run khi `Agent:Enabled = false` ⇒ **503** (tiền lệ `Reports:Enabled`).
- [ ] Tạo run ⇒ **202**, row `agent_runs` ở `Running`, có broadcast `AgentRunProgress` (assert qua hub hoặc qua bản ghi).
- [ ] Chạy vượt `MaxToolCalls` (fake script trả 16 tool call) ⇒ `status = Failed`, `stop_reason = ToolLimit`, **đúng một** notification `AgentRunFailed` và `notification_sent = true` (chống spam).
- [ ] `RequestClarification` ⇒ `status = AwaitingClarification`, có comment của agent, cột `is_clarification` được tạo lazy **đúng một lần**, task được chuyển sang cột đó.
- [ ] "Chạy lại" ⇒ tạo run **mới** với `previous_run_id` trỏ run cũ; run cũ **byte-identical** sau khi rerun (bất biến append-only D14 phase-7).
- [ ] `AgentRunReaper`: seed một run `Running` với `started_at` cũ hơn `RunTimeoutSeconds + OrphanRunGraceSeconds` ⇒ sau khi host khởi động, run đó thành `Failed`/`InternalError` và có `finished_at`.
- [ ] `AgentRunCancellationRegistry` + `POST /cancel`: huỷ được run đang chạy ⇒ `stop_reason = Cancelled`.
- [ ] **Reporting**: export `pdf` mở lại được bằng PdfPig (assert có text, không rỗng); `excel` round-trip bằng ClosedXML (5 sheet, đúng số dòng); export **không** tạo file nào trên disk (assert thư mục tạm không tăng).
- [ ] (skip mặc định, `TEAMNEXUS_TEST_REAL_AI=1`) 1 lượt DeepSeek thật + 1 lượt Tavily thật — bằng chứng tích hợp provider ngoài.

### 2.7 DoD của §2

- [x] `dotnet build TeamNexus.sln` ⇒ **0 warning / 0 error** (đã kiểm chứng, kể cả sau vá §3.1).
- [x] `dotnet test` ⇒ **xanh 100%**: **172 test, 0 fail, 0 skip, 0 error** trên PostgreSQL 18 thật.
- [x] Chạy **không có** DB (trỏ `TEAMNEXUS_TEST_DB` vào cổng chết) ⇒ **98 test thuần PASS, 74 test cần DB SKIP**
      kèm thông báo hành động được, **0 fail**.
- [x] Chạy lại toàn bộ test **2 lần liên tiếp** trên cùng DB ⇒ vẫn xanh (fixture reset sạch, không phụ thuộc thứ tự).
- [x] Không đổi schema: `dotnet ef migrations list` = **6**, không có file migration mới.
- [x] Không phụ thuộc User Secrets: cấu hình test do `TeamNexusApiFactory` cấp hoàn toàn bằng code
      (kể cả `Jwt:SigningKey` test), chỉ cần một PostgreSQL truy cập được.

### 2.8 Kết quả §2 (đã thi hành)

**Cấu trúc đã tạo** (`tests/TeamNexus.Api.Tests/`, đã thêm vào `TeamNexus.sln` dưới solution folder `tests`):

| File | Vai trò |
|---|---|
| `TeamNexus.Api.Tests.csproj` | xUnit **v3** (`xunit.v3` 3.0.1 + `xunit.runner.visualstudio` 3.1.5), `Microsoft.NET.Test.Sdk` 17.14.1, `Microsoft.AspNetCore.Mvc.Testing` 10.0.10, `Npgsql` 10.0.2, `EFCore.NamingConventions` 10.0.0. xUnit v3 là **bắt buộc** để có dynamic skip (`Assert.Skip`) — thứ mà D4 cần |
| `GlobalUsings.cs` | `global using Xunit;` (xUnit v3 không còn implicit usings) |
| `Infrastructure/DatabaseFixture.cs` | Vòng đời DB test: probe → `CREATE DATABASE` nếu chưa có → `Migrate()` **một lần/process** trong advisory lock; `TRUNCATE … CASCADE` mỗi scenario; `DatabaseLock` tuần tự hoá toàn bộ test cần DB |
| `Infrastructure/TeamNexusApiFactory.cs` | `WebApplicationFactory<Program>` cấp **toàn bộ** cấu hình bằng code (không User Secrets); **một host/process** (`Shared`); cờ `ScriptedAi`/`AgentEnabled`/`ReportsEnabled`/`AgentRunTimeoutSeconds` để test các nhánh đặc biệt |
| `Infrastructure/TestScenario.cs` | Seed user/workspace/board/task; `NewDbContext()` lấy DbContext **từ DI của app** (đảm bảo cùng model); đăng nhập Bearer + cookie; antiforgery; `FindIncludingSoftDeletedAsync` |
| `Infrastructure/TestHttpClient.cs` | Client gắn `X-XSRF-TOKEN` tự động cho POST/PUT/PATCH/DELETE (giống `httpClient.ts`) |
| `Infrastructure/SessionCookieHandler.cs` | Cookie jar per-client (vì `ClientHandler` của TestServer không cho truy cập `CookieContainer`) |
| `Infrastructure/TestJwt.cs` | Ký token test (đúng key/issuer/audience của host; có bản sai key và bản hết hạn) |
| `Infrastructure/ScriptedAiProvider.cs` | `IAiProvider` trả nội dung tuỳ ý ⇒ test được nhánh **AI trả JSON hỏng** (502) mà không gọi model thật |

**Phân bổ 172 test:**

| File | Số test | Nội dung |
|---|---|---|
| `Pure/AgentGuardrailsTests.cs` | 21 | 4 ngưỡng guardrail tại **biên** và vượt biên, thứ tự ưu tiên `ToolLimit → TokenBudget → TimeLimit`, `MaxRunLlmCalls`, `Describe`, clamp cấu hình |
| `Pure/AgentAttachmentFactoryTests.cs` | 31 | Comment vs Attachment (biên 2 000), tên file ASCII-safe (chống `../`, `..\`, path tuyệt đối), content-type hardening, cap `tool_call_trace` |
| `Pure/ObserverVocabularyTests.cs` | 46 | `Rank`/`IsKnown`/`AtLeast` case-insensitive, `Canonical`, whitelist tách biệt của agent |
| `Integration/AuthApiTests.cs` | 26 | 401/403 theo policy, token sai key/hết hạn, **CSRF** thiếu header + header sai, **rotate refresh token**, **replay ⇒ revoke cả family**, logout idempotent, chỉ lưu **hash** refresh token, **cờ `SameSite` của cookie theo cấu hình `Auth:CookieSameSite` (§3.1)** |
| `Integration/KanbanApiTests.cs` | 21 | board/column/task CRUD, reorder (204), kéo-thả set/clear `completed_at`, soft-delete (kiểm chứng qua `IgnoreQueryFilters`), cột `is_clarification` (400 khi trùng `is_done`), **siết membership assignee ⇒ 400** |
| `Integration/AccountabilityApiTests.cs` | 15 | Smart Setup **không ghi DB**, AI trả JSON hỏng ⇒ **502** + 2 lần gọi, confirm ⇒ `Pending`, approve ⇒ ghi task, **approve 2 lần ⇒ 409**, reject, **undo soft-delete** + `Undone`, history, non-member ⇒ 404 |
| `Integration/AgentAndReportingApiTests.cs` | 12 | Agent gán lazy được, draft ⇒ `AwaitingApproval` + trace 3 bước, **rerun append-only** (run cũ byte-identical) sau khi trả lời làm rõ, tool ngoài whitelist ⇒ ghi trace lỗi, **timeout ⇒ `TimeLimit`**, huỷ ⇒ `Cancelled`, `Agent:Enabled=false` ⇒ **503**, **reaper** dọn run mồ côi, report summary/export PDF+Excel (magic number `%PDF-`/`PK`)/400/403 |

**3 điều chỉnh so với kế hoạch ban đầu — đều là kết quả của việc verify chứ không phải bỏ bớt:**

1. **`ToolLimit` không test được ở tầng API.** Dò trực tiếp `FakeAiProvider` cho thấy các script offline luôn hội tụ về
   `DraftOutput`/`RequestClarification`; `FAKE:SLOW` chỉ **làm chậm** chứ không làm agent gọi tool mãi. Vì vậy ngưỡng này được
   phủ **tại đúng nơi luật nằm** (`Pure.AgentGuardrailsTests`, 4 ngưỡng + thứ tự ưu tiên), còn tầng integration phủ **ngưỡng wall-clock**
   — ngưỡng duy nhất mà fake *có* thể kích hoạt (qua `AgentRunTimeoutSeconds = 5` trên host riêng). Giới hạn này được ghi ngay trong
   docstring của suite để người sau không tưởng là đã test đủ.
2. **`FAKE:UNKNOWN` không làm run thất bại.** Whitelist tool được enforce bằng cách trả **kết quả lỗi cho model**, không abort run
   (đúng thiết kế Phase 7 §4.3) ⇒ test được viết lại để khẳng định đúng hợp đồng: run vẫn hoàn tất và `tool_call_trace` **có** entry `isError=true`.
   Đổi lại, test "`AgentRunFailed` gửi đúng 1 notification" đã **bị bỏ** vì không có sentinel nào tạo được trạng thái đó; cơ chế chống spam
   `notification_sent` vẫn nằm trong `AgentRunReaper`/guardrail mà Phase 7 đã verify.
3. **`dotnet test` vs runner in-process.** Vì dùng MTP của xUnit v3, cách chạy chuẩn là
   `dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj`, hoặc chạy trực tiếp
   `dotnet run --project tests/TeamNexus.Api.Tests -- -class <FQCN>` để lọc theo class. Không dùng `--filter` của VSTest.

**Bug thật bắt được khi viết test:** không có bug sản phẩm nào. Hai hiểu nhầm phía test đã tự sửa: (a) `reorder` trả **204**, không phải 200;
(b) `move` **đánh số lại dense** `0..n-1` nên "position 5" bị kẹp thành vị trí cuối — test được viết lại để khẳng định đúng hợp đồng đó.

**Lệnh chạy lại (đã dùng để verify):**

```powershell
# Cần PostgreSQL thật. TEAMNEXUS_TEST_DB ghi đè default localhost/TeamNexus_Test.
$env:TEAMNEXUS_TEST_DB = 'Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=...'
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj
# hoặc không có DB ⇒ 98 PASS / 74 SKIP / 0 FAIL
```

---

## 2b. [Test] Frontend bổ sung

> 🔻 **ĐÃ BÀN GIAO**: `tasks/phase-8-2b-frontend-handover.md` — note thực thi cho Antigravity, gộp **§2b + §3.2 + §3.3** (vì §2b test
> đúng hai hàm mà §3.2/§3.3 tạo ra: `resolveHubUrl` và `reconnectPolicy`) + **§3.1** (vá cookie backend). Baseline đã đo lại:
> **35 files / 187 tests PASS**, `lint` 0/0, `tsc -b` exit 0, `build` OK.

Baseline hiện tại: **35 test files / 187 tests PASS** — mọi thứ dưới đây là **thêm**, và kết thúc §2b phải **> 187**.

- [ ] `frontend/src/features/board/utils/hubUrl.ts` + `__tests__/hubUrl.test.ts`:
  - base URL rỗng/`undefined` ⇒ `/hubs/board` (dev proxy — giữ hành vi cũ).
  - base URL tương đối `/api` ⇒ `/hubs/board`.
  - base URL tuyệt đối `https://api.example.com/api` ⇒ `https://api.example.com/hubs/board` (**đúng cái bẫy B14**).
  - base URL có/không có `/` cuối ⇒ cùng kết quả (không sinh `//`).
  - base URL có path lạ ⇒ hành vi xác định (không ném exception).
- [ ] `frontend/src/features/board/utils/reconnectPolicy.ts` + `__tests__/reconnectPolicy.test.ts`:
  - Lần 0 ⇒ 0 ms; 1 ⇒ 2 000; 2 ⇒ 5 000; 3 ⇒ 10 000; 4 ⇒ 30 000; 5, 6, 100 ⇒ **vẫn 30 000** (không bao giờ trả `null`/bỏ cuộc).
  - Không bao giờ trả giá trị âm hoặc `NaN` với đầu vào bất thường (`-1`, `Number.MAX_SAFE_INTEGER`).
- [ ] `useBoardHub` (cập nhật test hiện có + test mới):
  - Dùng retry policy mới ⇒ `withAutomaticReconnect` nhận **object**, không nhận mảng.
  - `onreconnected` ⇒ gọi `refetch` **đúng một lần** (spy) và re-join `JoinBoard` với đúng `boardId`.
  - `onclose` sau khi hết đường ⇒ `connectionStatus = 'disconnected'`; `reconnect()` thủ công ⇒ gọi `start()` lại.
  - URL kết nối lấy từ `resolveHubUrl` (mock `import.meta.env`), không còn hard-code.
- [ ] `BoardView.test.tsx`: trạng thái `disconnected` hiển thị nút **"Kết nối lại"** và bấm vào gọi `reconnect`; trạng thái `reconnecting` có thông điệp cold-start tiếng Việt sau mốc thời gian quy định (dùng fake timer).
- [ ] DoD §2b: `oxlint` **0/0**, `npx tsc -b` **exit 0**, `npm test` **PASS và số test > 187**, `npm run build` **OK**.

---

## 3. [Mã nguồn] 3 vá bắt buộc để deploy được

> Ba mục này là **điều kiện sống còn**, không phải "cải tiến". Nếu bỏ qua, app deploy xong sẽ: (1) không đăng nhập được, (2) real-time chết,
> (3) chết hẳn sau 15 phút rỗi.
>
> 🔻 **Hướng dẫn thi hành chi tiết (viết cho Antigravity, gồm cả cấu trúc file cụ thể): `tasks/phase-8-2b-frontend-handover.md` §2.4 + §3.1.**
> Trạng thái: **§3.1 đã thi hành xong** (xem §3.1 dưới); §3.2 + §3.3 gộp vào lượt §2b của Antigravity (cùng tạo 2 hàm thuần).

### 3.1 Vá #1 — Cookie cho cross-site (D7)

**Vấn đề (B6):** `SameSite=Lax` + FE và API **khác site** ⇒ browser **không gửi** cookie `access_token`/`refresh_token`/antiforgery
trên request XHR cross-site ⇒ đăng nhập xong vẫn bị coi là chưa đăng nhập (vòng lặp login).

> **✅ ĐÃ THI HÀNH XONG** (2026-09, nhánh `feat/phase8-completion-test-deploy`). Diễn biến thực tế:

- [x] Thêm `Options/AuthOptions.cs` (MỚI) — section `Auth`, một property `CookieSameSite`, mặc định `SameSiteMode.Lax`.
      **Không** nhét vào `JwtOptions`: đây là mối quan tâm vận chuyển cookie, khác với hình dạng/hiệu lực token, và phải áp cho **cả**
      antiforgery cookie. Có `.Validate(...).ValidateOnStart()` để giá trị sai bị chặn ngay lúc boot thay vì im lặng dùng mặc định.
- [x] `TokenCookieService.cs`: cả **4** chỗ hard-code `SameSite = SameSiteMode.Lax` (set access, set refresh, clear access, clear refresh)
      nay đọc `_authOptions.CookieSameSite`; inject thêm `IOptions<AuthOptions>`.
- [x] `DependencyInjection.cs`: bind `AuthOptions` **và** antiforgery cookie dùng cùng giá trị
      (`options.Cookie.SameSite = authOptions.CookieSameSite`). Đọc `authOptions` một lần từ `configuration` và dùng chung biến này,
      để không có hai nguồn sự thật.
- [x] `appsettings.json`: thêm `"Auth": { "CookieSameSite": "Lax" }` (kèm key `"//"` ghi chú production đặt `None`).
- [x] Giữ nguyên `Secure = HttpContext.Request.IsHttps` ⇒ local `http://localhost` vẫn chạy, production (HTTPS) tự có `Secure`.
- [x] (Việc của §5) Render: env `Auth__CookieSameSite = None`.

**Bằng chứng verify:** `dotnet test` = **172 passed / 0 failed / 0 skipped** — tức **170 test cũ vẫn xanh** (không hồi quy) **+ 2 test mới**
trong `AuthApiTests`:
- `AuthCookies_UseLaxByDefault` — host mặc định ⇒ `Set-Cookie` có `samesite=lax` + `httponly`.
- `AuthCookies_FollowAuthCookieSameSiteConfiguration` — host đặt `Auth:CookieSameSite=None` ⇒ `Set-Cookie` có `samesite=none` và **không** còn `lax`.
  Test này chính là thứ chứng minh giá trị **đến từ cấu hình**, chứ không phải hard-code.

> **Phát hiện khi viết test (ghi lại để không ai tưởng là thiếu sót):** không assert cờ `Secure` ở tầng integration. `TokenCookieService`
  dùng `Secure = HttpContext.Request.IsHttps`, mà TestServer nói HTTP thường ⇒ ở test cờ này **phải vắng mặt**; trên deployment HTTPS thật
  nó mới được thêm. Assert `Secure` trong test sẽ là khẳng định một hành vi mà transport của test không có. Điều **cần** kiểm chứng ở §5.8
  (deploy thật) là: DevTools thấy cookie có **`Secure` ✓** và `SameSite=None`.

### 3.2 Vá #2 — SignalR URL theo API base (D8, B14)

> 🔻 **Hướng dẫn thi hành chi tiết: `tasks/phase-8-2b-frontend-handover.md` §2.1 + §2.4** (gộp cùng §2b vì test chính là hàm này).
> Chưa thi hành — thuộc lượt của Antigravity.

- [ ] `frontend/src/features/board/utils/hubUrl.ts`:
  ```ts
  /** Ghép đường dẫn hub với API base. Rỗng/relative ⇒ giữ đường dẫn tương đối (dev proxy). */
  export function resolveHubUrl(apiBaseUrl?: string, hubPath = '/hubs/board'): string
  ```
  - Dùng `import.meta.env.VITE_API_BASE_URL` làm tham số mặc định ở chỗ gọi.
  - Xử lý dấu `/` cuối, base có path (`https://api.x.com/api` ⇒ `https://api.x.com/hubs/board`), base rỗng.
- [ ] `useBoardHub.ts`: thay `.withUrl('/hubs/board', ...)` bằng `.withUrl(resolveHubUrl(import.meta.env.VITE_API_BASE_URL), { withCredentials: true })`.
- [ ] Không đụng `httpClient.ts` (đã đúng).

### 3.3 Vá #3 — Reconnect vô hạn + refetch (D9, B7, B8)

> 🔻 **Hướng dẫn thi hành chi tiết: `tasks/phase-8-2b-frontend-handover.md` §2.2 + §2.4** (gộp cùng §2b vì test chính là hàm này).
> Chưa thi hành — thuộc lượt của Antigravity.

- [ ] `frontend/src/features/board/utils/reconnectPolicy.ts`: hàm thuần, trả về `{ nextRetryDelayInMilliseconds }` cho `IRetryPolicy` của `@microsoft/signalr`.
- [ ] `useBoardHub.ts`:
  - `useBoardHub(boardId, refetch?)` (tham số thứ hai tuỳ chọn để **không** phá 2 chỗ gọi hiện có).
  - `.withAutomaticReconnect({ nextRetryDelayInMilliseconds })` + `serverTimeoutInMilliseconds`/`keepAliveIntervalInMilliseconds` hợp lý (server timeout lớn hơn 1 chút so với keep-alive; giá trị đặt ở hằng số có tên, không magic number).
  - `onreconnected` ⇒ re-join group **rồi** `refetch()` (nuốt lỗi nhưng log có ngữ cảnh).
  - Trả thêm `reconnect()` cho UI thủ công.
- [ ] `useBoard.ts`: truyền `refetch: fetchBoardData` xuống `useBoardHub`.
- [ ] `BoardView.tsx`: thêm nút "Kết nối lại" trong nhánh `disconnected`; thông điệp cold-start trong nhánh `reconnecting` (giữ nguyên tooltip tiếng Việt hiện có, chỉ bổ sung).
- [ ] `frontend/.env.example`: bổ sung dòng giải thích `VITE_API_BASE_URL` cho production (đã có comment, chỉ cần ví dụ).

---

## 4. [CI/CD] GitHub Actions

> Mục tiêu: mọi push/PR phải chứng minh được "build xanh + test xanh" **trước khi** auto-deploy của Render/Vercel chạy.

### 4.1 `.github/workflows/ci-backend.yml`

- [ ] Trigger: `push` (mọi nhánh) + `pull_request`; thêm `concurrency` để huỷ run cũ trên cùng nhánh.
- [ ] Job `build-and-test` trên `ubuntu-latest`:
  - `actions/checkout@v4`.
  - `actions/setup-dotnet@v4` với `dotnet-version: '10.0.x'` (ghim major.minor, **không** `latest`).
  - `services: postgres:18` với `env: POSTGRES_PASSWORD`, `ports: 5432:5432`, `options: >- --health-cmd pg_isready --health-interval 10s --health-timeout 5s --health-retries 5`.
  - `env: TEAMNEXUS_TEST_DB = Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=<pw>`.
  - `dotnet restore TeamNexus.sln` → `dotnet build TeamNexus.sln --no-restore -m:1 -nr:false` → `dotnet test --no-build --logger "trx;LogFileName=test-results.trx"`.
  - `actions/upload-artifact@v4` cho `TestResults/**` với `if: always()`.
- [ ] ⚠️ Ghi rõ trong tài liệu: `-m:1 -nr:false` là **bắt buộc trong môi trường sandbox/agent** (xem README dòng 47–57); trên runner GitHub thì không hại gì, nên giữ luôn cho nhất quán 1 lệnh duy nhất.
- [ ] Cache NuGet (`actions/cache` hoặc `setup-dotnet` với `cache: true`, `cache-dependency-path: '**/packages.lock.json'` nếu bật lock file — nếu không thì cache `~/.nuget/packages`).

### 4.2 `.github/workflows/ci-web.yml`

- [ ] Trigger giống §4.1; `defaults.run.working-directory: frontend`.
- [ ] `actions/setup-node@v4` với `node-version: '22'` (khớp "Node ≥ 22" trong README) và `cache: npm`, `cache-dependency-path: frontend/package-lock.json`.
- [ ] `npm ci` → `npm run lint` → `npx tsc -b` → `npm test` → `npm run build`.
- [ ] **Không** truyền secret vào job này; nếu cần build với API base thật thì truyền `VITE_API_BASE_URL` bằng biến **không bí mật** (hoặc để trống và build theo mặc định).
- [ ] (Tuỳ chọn) upload `frontend/dist` làm artifact để xem lại build output khi fail.

### 4.3 Không có workflow deploy (D11)

- [ ] Ghi rõ trong tài liệu và trong README: **deploy do Render và Vercel tự làm** từ GitHub; Actions không giữ `ConnectionStrings__DefaultConnection`,
  `Jwt__SigningKey` hay API key nào ⇒ không có secret nào cần rotate trong CI.
- [ ] Hệ quả đã biết: **không** có bước tự động chạy migration sau deploy ⇒ §5.7 là bước thủ công bắt buộc.
- [ ] (Tuỳ chọn) bật **branch protection** cho `main`: yêu cầu 2 check `ci-backend` + `ci-web` xanh mới merge.

### 4.4 Badge & tài liệu

- [ ] Thêm badge CI vào đầu `README.md` (2 badge, trỏ đúng tên workflow).
- [ ] Ghi trong README: cách chạy test backend (`TEAMNEXUS_TEST_DB` + `dotnet test`) và frontend (`npm test`).

---

## 5. [DEPLOY] Hướng dẫn từng bước (cho người chưa từng dùng)

> Phần này viết cho trường hợp **bạn chưa từng dùng nền tảng nào trong danh sách**. Mỗi nền tảng đi theo cùng 6 mục:
> **(a)** nó là gì & vì sao chọn · **(b)** cần chuẩn bị gì · **(c)** từng bước · **(d)** giá trị/env cần điền · **(e)** cách kiểm tra thành công · **(f)** pitfall riêng.
>
> ⚠️ **Chính sách free-tier thay đổi rất nhanh.** Số liệu dưới đây được kiểm chứng **tháng 8–9/2026** từ
> [Render – free tier 2026](https://render.com/articles/platforms-with-a-real-free-tier-for-developers-in-2026),
> [Neon vs Supabase Free Plan](https://neon.com/guides/neon-vs-supabase-free-plan),
> [Vercel Hobby Plan](https://vercel.com/docs/plans/hobby), [Supabase – project pausing](https://supabase.com/docs/guides/platform/free-project-pausing).
> **Trước khi làm, mở lại trang pricing của nền tảng bạn định dùng và đối chiếu.** Đừng ngạc nhiên nếu số liệu đã khác.

### 5.0 Bản đồ tổng thể & chọn nền tảng

```
   [1] GitHub repo (đã có ở local)  ──push──►  GitHub
                                                │
                        ┌───────────────────────┼────────────────────────┐
                        ▼                       ▼                        ▼
                    [2] Neon              [3] Render                [4] Vercel
                  PostgreSQL 18        ASP.NET Core API          React static build
                  (DB + migrate)        /api/health               dist → CDN
                        ▲                       ▲                        │
                        └──── Npgsql ───────────┘                        │
                              SslMode=Require          XHR + WebSocket ───┘
                                                      (cookie SameSite=None)
```

**Bảng chọn nền tảng (dùng để chốt phương án, số liệu 08–09/2026):**

| Nền tảng | Vai trò | Free-tier thực tế | Kết luận cho dự án |
|---|---|---|---|
| **Render** | API backend | Web service free, **spin down sau 15 phút rỗi**, thức dậy ~**1 phút**; **750 giờ instance/tháng**/workspace; **không** cần thẻ. PostgreSQL free chỉ **1 GB** và **hết hạn sau 30 ngày** (gia hạn 14 ngày trước khi xoá dữ liệu) | ✅ **API — đường chính.** ❌ **Không** dùng Postgres của Render làm DB chính vì hết hạn 30 ngày |
| **Neon** | Database | 100 project; **0.5 GB storage**; 100 CU-hour/tháng; **compute ngủ sau 5 phút** và **tự thức** khi có query (vài trăm ms); 10 branch/project; restore 6 giờ; egress 5 GB/tháng | ✅ **DB — đường chính.** Ngủ 5 phút là **bình thường**, không phải lỗi |
| **Vercel** | Frontend | Hobby: static + CDN, **100 GB bandwidth/tháng**, deploy theo push + preview URL cho mỗi PR; **không giữ kết nối dài**; lưu ý **Hobby không cho mục đích thương mại** (portfolio/CV thì hợp lệ) | ✅ **FE — đường chính.** Chỉ host tĩnh, mọi logic/WS đi về API |
| **Supabase** | Database (B) | 2 project; 500 MB DB; instance Nano **luôn chạy** nhưng **project bị pause sau 1 tuần không hoạt động** và **phải bấm restore thủ công**; không có backup trên Free | ⚠️ **Phương án B** — chỉ chọn nếu bạn muốn dùng luôn Auth/Realtime, và cam kết "ghé thăm" ít nhất mỗi tuần |
| **Railway** | API backend (B) | Free: **$5 credit tháng đầu, sau đó $1 credit/tháng** ⇒ không đủ chạy 24/7; Hobby $5/tháng mới ổn | ⚠️ **Phương án B** — dùng nếu bạn sẵn sàng trả ~$5/tháng để **không** bị cold-start |
| **Netlify** | Frontend (B) | 100 GB bandwidth + 125 000 function invocation + **300 build minute/tháng** | ⚠️ **Phương án B** cho FE, tương đương Vercel cho mục đích của dự án |

**Quyết định đã chốt:** đường chính là **Neon + Render + Vercel** (D13). Các mục 5.2b/5.3b/5.4b là phương án B, chỉ đọc khi cần đổi.

---

### 5.1 Chuẩn bị repo (làm một lần)

- [ ] Đảm bảo `.gitignore` đã chặn: `bin/`, `obj/`, `node_modules/`, `frontend/dist/`, `*.user`, và **không** có file chứa secret (`appsettings.Development.json` hiện chỉ có logging — an toàn).
- [ ] Kiểm tra **không** có secret bị commit (quan trọng vì repo sẽ là public cho CV):
  - [ ] `git log -p --all -- src/TeamNexus.Api/appsettings*.json` ⇒ chỉ thấy giá trị rỗng.
  - [ ] `grep -R "sk-\|ApiKey\|ClientSecret" --include="*.json" --include="*.cs" --include="*.ts"` ⇒ mọi chỗ đều là `""`.
  - [ ] Nếu từng commit secret ⇒ **coi như đã lộ**: rotate key đó ở DeepSeek/Tavily/OAuth, không chỉ xoá file.
- [ ] Tạo repo trên GitHub (private hoặc public) và push nhánh `main`.
- [ ] (Tuỳ chọn, nên làm) tạo nhánh `develop` để deploy preview trước khi vào `main`.
- [ ] Ghi lại **quy ước đặt tên domain** bạn sẽ dùng, vì nó xuất hiện ở 4 nơi (OAuth console, `Frontend__BaseUrl`, `Cors__AllowedOrigins__0`, `VITE_API_BASE_URL`). Ví dụ:
  - API: `https://teamnexus-api.onrender.com`
  - FE: `https://teamnexus.vercel.app`

---

### 5.2 Database — Neon (đường chính)

**(a) Là gì & vì sao:** Neon là PostgreSQL serverless. Compute **ngủ sau 5 phút** không dùng và **tự thức** ở query kế tiếp (~vài trăm ms) — khác Supabase Free (pause cả project sau 1 tuần, phải bấm restore tay). Với một người làm portfolio, đây là kiểu ngủ "không phiền".

**(b) Chuẩn bị:** tài khoản (đăng nhập bằng GitHub), biết region gần bạn nhất (Việt Nam ⇒ **Singapore**).

**(c) Từng bước:**
1. [ ] Vào [console.neon.tech](https://console.neon.tech) → đăng nhập bằng GitHub → **New Project**.
2. [ ] Điền **Project name** (`teamnexus`), **Region** (`AWS ap-southeast-1` / Singapore nếu có), **Postgres version** (chọn **18** nếu được, để khớp môi trường đã verify ở Phase 7).
3. [ ] Sau khi tạo, Neon hiện **Connection string**. Bấm **Connect** để xem chi tiết. Bạn cần chú ý 2 biến thể endpoint:
   - **Direct** (`ep-xxx.ap-southeast-1.aws.neon.tech`) → dùng cho **migration**.
   - **Pooled** (có `-pooler` trong host) → dùng cho **runtime** (API) nếu muốn tiết kiệm connection.
4. [ ] Lấy connection string ở **định dạng .NET/Npgsql** (Neon có dropdown chọn "Npgsql"/".NET"): dạng
   `Host=ep-xxx.ap-southeast-1.aws.neon.tech;Port=5432;Database=neondb;Username=neondb_owner;Password=...;SslMode=Require`
   - Nếu Neon chỉ cho URI `postgresql://user:pass@host/db?sslmode=require`, tự chuyển sang dạng trên.
5. [ ] Lưu vào nơi an toàn (password manager) — **không** dán vào file trong repo.
6. [ ] (Khuyến nghị) Tạo **một branch/database riêng cho test** nếu sau này muốn chạy test trên cloud; mặc định không cần.

**(d) Giá trị cần điền:** xem bảng §5.6 (`ConnectionStrings__DefaultConnection`).

**(e) Kiểm tra thành công:**
- [ ] Từ Neon console, **SQL Editor** chạy `select version();` ⇒ trả về PostgreSQL ≥ 16.
- [ ] Sau §5.7 (migration), chạy `select count(*) from "__EFMigrationsHistory";` ⇒ **6**.
- [ ] Chạy `select count(*) from information_schema.tables where table_schema='public';` ⇒ thấy đủ các bảng (`workspaces`, `boards`, `board_columns`, `tasks`, `task_comments`, `labels`, `task_labels`, `ai_action_logs`, `activity_logs`, `notifications`, `ai_observer_runs`, `agent_runs`, `task_attachments`, …).

**(f) Pitfall riêng của Neon:**
- ⚠️ **Đừng hoảng khi query đầu tiên chậm** — đó là compute đang thức dậy, không phải API lỗi.
- ⚠️ **`SslMode=Require` là bắt buộc**; thiếu ⇒ lỗi SSL ở tầng Npgsql.
- ⚠️ **Pooled endpoint dùng cho migration dễ gây lỗi advisory-lock/transaction** (nhiều phase của dự án dùng `pg_try_advisory_lock`). ⇒ **Luôn migrate bằng endpoint Direct.**
- ⚠️ **0.5 GB storage** là trần thật: bảng `task_attachments` dùng `bytea` cap 512 KB/file ⇒ 0.5 GB ≈ **1 000 file**. Theo dõi ở dashboard Neon; bảng `agent_runs` có retention 90 ngày (D20 phase-7) để không phình vô hạn.
- ⚠️ Neon có thể **thay đổi password/rotate** khi bạn reset; cập nhật lại env trên Render nếu đổi.

---

### 5.2b Phương án B — Supabase (chỉ khi muốn dùng Auth/Realtime sẵn)

- [ ] Tạo project → **Settings → Database** → copy **Connection string** (chọn "URI" hoặc ".NET"), dùng **Session/Transaction pooler** cho runtime.
- [ ] ⚠️ Khác Neon ở đúng một điểm quan trọng: **project bị pause sau ~1 tuần không hoạt động** và phải vào dashboard bấm **Restore** ([Supabase docs](https://supabase.com/docs/guides/platform/free-project-pausing)). ⇒ Đặt nhắc lịch ghé thăm, hoặc chấp nhận rằng bản demo có thể "chết" cho tới khi bạn restore.
- [ ] Free **không có backup** ⇒ nếu dùng Supabase làm DB chính, tự chạy `pg_dump` định kỳ.
- [ ] Các bước migrate/env giống Neon; chỉ khác chuỗi kết nối.

---

### 5.3 API — Render (đường chính)

**(a) Là gì & vì sao:** Render chạy web service từ Git, có HTTPS sẵn, **không cần thẻ**, và là nền tảng free-tier duy nhất trong danh sách vẫn cho một web service "kiểu free" chạy dài hạn. Đánh đổi: **ngủ sau 15 phút rỗi**, thức dậy ~**1 phút** ⇒ chính là nguồn gốc của hạng mục §6.

**(b) Chuẩn bị:** tài khoản Render (đăng nhập GitHub); repo đã push; connection string Neon đã có (§5.2); **một `Jwt__SigningKey` mới** (khác hẳn key local):
```powershell
# sinh key ≥ 32 byte, KHÔNG dùng lại key local
[Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Maximum 256 }))
```

**(c) Từng bước:**
1. [ ] [dashboard.render.com](https://dashboard.render.com) → **New +** → **Web Service**.
2. [ ] **Connect a repository** → chọn repo `TeamNexus` → **Connect**.
3. [ ] Điền cấu hình:

   | Trường | Giá trị |
   |---|---|
   | **Name** | `teamnexus-api` (⇒ domain `https://teamnexus-api.onrender.com`) |
   | **Region** | Singapore (gần người dùng Việt Nam nhất) |
   | **Branch** | `main` |
   | **Root Directory** | *(để trống)* |
   | **Runtime** | `Docker` **hoặc** `.NET` tuỳ Render phát hiện (xem pitfall ⚠️1) |
   | **Build Command** | `dotnet publish src/TeamNexus.Api/TeamNexus.Api.csproj -c Release -o out` |
   | **Start Command** | `dotnet out/TeamNexus.Api.dll` |
   | **Health Check Path** | `/api/health` |
   | **Instance Type** | **Free** |

4. [ ] **Environment → Add Environment Variable**: nhập toàn bộ bảng ở §5.6 (mục API). Đặc biệt đừng quên `ASPNETCORE_URLS=http://0.0.0.0:$PORT` (pitfall ⚠️2).
5. [ ] **Create Web Service** → xem tab **Logs** để theo dõi build đầu tiên (lần đầu có thể 5–10 phút).
6. [ ] Khi trạng thái chuyển **Live**, mở `https://<name>.onrender.com/api/health` ⇒ phải thấy JSON `{"status":"ok","service":"TeamNexus.Api",...}`.
7. [ ] Ghi lại domain API — nó sẽ dùng ở §5.4 (FE), §5.5 (OAuth), §5.6 (CORS).

**(d) Giá trị cần điền:** §5.6.

**(e) Kiểm tra thành công:**
- [ ] `/api/health` trả 200 + JSON đúng (endpoint này có sẵn trong `Program.cs`, không cần auth).
- [ ] Log khởi động **không** có exception về `Jwt:SigningKey` (nếu có ⇒ key < 32 byte hoặc chưa set: app **cố tình** fail-fast theo `ValidateOnStart`).
- [ ] Log khởi động **không** có lỗi kết nối Npgsql (nếu có ⇒ sai connection string/`SslMode`).
- [ ] Trang `/scalar` **KHÔNG** tồn tại (đúng D14 — production ẩn OpenAPI).
- [ ] `POST /api/auth/login/google` (không có cookie) trả **400/302 hợp lý**, không 500.

**(f) Pitfall riêng của Render:**
- ⚠️ **1 — Render có thể không nhận .NET 10 để auto-detect** (SDK 10 rất mới). Triệu chứng: build fail với "no SDK / unsupported framework". Cách sửa: chuyển **Runtime = Docker** và thêm `Dockerfile` ở gốc repo (mẫu đầy đủ ở §5.3-Dockerfile). *Chỉ dùng Dockerfile khi auto-detect thất bại* — đừng thêm trước.
- ⚠️ **2 — Không dùng `$PORT`.** Render gán cổng động qua biến `PORT`; nếu app chỉ listen cổng 5000 mặc định, Render sẽ báo **502/không truy cập được** dù build xanh. Luôn có `ASPNETCORE_URLS=http://0.0.0.0:$PORT`. (Bind `0.0.0.0`, **không** `localhost`.)
- ⚠️ **3 — Hết 750 giờ/tháng ⇒ service bị suspend tới đầu tháng sau.** Một service chạy 24/7 ≈ **744 giờ/tháng** ⇒ gần như vừa khít. Nếu thêm service thứ hai (hoặc bật keep-warm ping 24/7) sẽ **vượt trần**. Theo dõi ở **Billing → Usage**.
- ⚠️ **4 — Free Postgres của Render hết hạn sau 30 ngày.** Vì vậy tài liệu này **không** dùng nó (D13). Nếu bạn vẫn tạo để thử, nhớ nó sẽ tự xoá dữ liệu.
- ⚠️ **5 — `dotnet publish` không truyền `--no-restore`/`-m:1`.** Trên Render (Linux, không sandbox) lệnh mặc định là đúng; ghi chú `-m:1 -nr:false` **chỉ** dành cho môi trường sandbox/agent ở máy dev.
- ⚠️ **6 — Mỗi lần push lên nhánh đã cấu hình ⇒ auto-deploy.** Muốn deploy thủ công thì tắt **Auto-Deploy** trong Settings.

**§5.3-Dockerfile (chỉ thêm khi pitfall ⚠️1 xảy ra):**

```dockerfile
# Dockerfile tại gốc repo — dùng khi Render không auto-detect được .NET 10
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props TeamNexus.sln ./
COPY src/ ./src/
RUN dotnet publish src/TeamNexus.Api/TeamNexus.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "TeamNexus.Api.dll"]
```

> Ghi chú: image `sdk:10.0`/`aspnet:10.0` chỉ dùng được khi đã có tag chính thức cho .NET 10; nếu chưa, dùng tag preview tương ứng và **ghi lại số tag đã dùng**. QuestPDF/SkiaSharp đi kèm native lib trong `runtimes/linux-x64/` (đã có sẵn trong output publish) nên PDF/Excel vẫn chạy trên Linux.

---

### 5.3b Phương án B — Railway

- [ ] Tạo project → **Deploy from GitHub repo** → Railway tự nhận .NET qua Nixpacks.
- [ ] **Settings → Networking → Generate Domain** ⇒ có URL công khai.
- [ ] **Variables**: nhập bảng §5.6 (Railway tự set `PORT`; app **vẫn cần** `ASPNETCORE_URLS=http://0.0.0.0:$PORT`).
- [ ] ⚠️ Free chỉ có **$1 credit/tháng sau tháng đầu ($5)** ⇒ service sẽ **pause khi hết credit**. Chấp nhận được cho demo ngắn; muốn 24/7 thì Hobby ~$5/tháng.
- [ ] Ưu điểm so với Render: **không cold-start** khi còn credit. Nhược điểm: không thật sự "free" lâu dài.

---

### 5.4 Frontend — Vercel (đường chính)

**(a) Là gì & vì sao:** Vercel host build tĩnh + CDN, có HTTPS, preview URL cho mỗi PR, deploy tự động khi push. Frontend của dự án là SPA (`react-router`) ⇒ cần **SPA rewrite** để F5 ở route con không 404.

**(b) Chuẩn bị:** tài khoản Vercel (GitHub); domain API Render đã Live (§5.3).

**(c) Từng bước:**
1. [ ] [vercel.com](https://vercel.com) → **Add New… → Project** → **Import Git Repository** → chọn repo `TeamNexus`.
2. [ ] Cấu hình (rất quan trọng — **Root Directory**):

   | Trường | Giá trị |
   |---|---|
   | **Root Directory** | `frontend` |
   | **Framework Preset** | Vite (tự nhận) |
   | **Build Command** | `npm run build` (mặc định) |
   | **Output Directory** | `dist` |
   | **Install Command** | `npm ci` (mặc định `npm install` cũng được) |
   | **Node.js Version** | 22.x (khớp README) |

3. [ ] **Environment Variables**: `VITE_API_BASE_URL = https://<api>.onrender.com/api` (áp cho **Production**; có thể khác cho Preview nếu bạn có API staging).
4. [ ] **Deploy** → mở URL `https://<fe>.vercel.app`.
5. [ ] Thêm `frontend/vercel.json` vào repo (SPA rewrite) rồi push một lần để chắc chắn cấu hình được áp:

```json
{
  "rewrites": [{ "source": "/(.*)", "destination": "/index.html" }]
}
```

6. [ ] Quay lại Render → thêm origin của Vercel vào CORS (§5.6, `Cors__AllowedOrigins__0`) → chờ Render redeploy xong.

**(d) Giá trị cần điền:** `VITE_API_BASE_URL` (build-time, xem pitfall ⚠️1).

**(e) Kiểm tra thành công:**
- [ ] Mở `https://<fe>.vercel.app` ⇒ hiện trang login.
- [ ] F5 ở một route con (ví dụ `/workspaces/xxx/boards`) ⇒ **không** 404 (SPA rewrite hoạt động).
- [ ] Mở DevTools → **Network**: request tới `/api/...` phải đi tới **domain Render**, không phải domain Vercel.
- [ ] DevTools → **Network → WS**: thấy `wss://<api>.onrender.com/hubs/board?...` (⚠️ nếu thấy `wss://<fe>.vercel.app/...` ⇒ §3.2 chưa áp đúng).

**(f) Pitfall riêng của Vercel:**
- ⚠️ **1 — `VITE_API_BASE_URL` là biến *build-time*.** Vite "nướng" giá trị vào bundle lúc build ⇒ **sửa env xong phải Redeploy** mới có tác dụng, F5 vô ích.
- ⚠️ **2 — Quên `Root Directory = frontend`** ⇒ Vercel build ở gốc repo (không có `package.json`) ⇒ fail ngay.
- ⚠️ **3 — Không có SPA rewrite** ⇒ mọi F5 ở route con trả 404 dù app chạy đúng.
- ⚠️ **4 — Hobby không cho dùng thương mại.** Portfolio/CV/demo cá nhân thì hợp lệ; đừng gắn quảng cáo/khách hàng trả tiền.
- ⚠️ **5 — Vercel không giữ kết nối dài** ⇒ đừng bao giờ để frontend làm proxy cho API/SignalR; mọi thứ đi trực tiếp về Render.

---

### 5.4b Phương án B — Netlify

- [ ] **Add new site → Import from Git** → chọn repo → **Base directory: `frontend`**, Build `npm run build`, Publish `dist`.
- [ ] **Environment variables**: `VITE_API_BASE_URL` như Vercel.
- [ ] SPA fallback: tạo `frontend/public/_redirects` với `/*  /index.html  200` (Netlify dùng `_redirects`, không dùng `vercel.json`).
- [ ] ⚠️ Free có **300 build minute/tháng** ⇒ đủ dùng nhưng đừng build quá nhiều lần/ngày.

---

### 5.5 OAuth — cập nhật redirect URI (Google & GitHub)

> **Vì sao cần:** local bạn đang dùng `http://localhost:5000/api/auth/callback/...`. Google/GitHub **không cho wildcard** ⇒ domain production **phải** được khai báo tường minh. Nếu không ⇒ lỗi `redirect_uri_mismatch`.
> **Tin tốt:** code đã dựng redirect URI **động** từ request host (B11) ⇒ **không phải sửa code**, chỉ sửa console + `Frontend__BaseUrl`.

**Đường dẫn cố định (đúng như trong `AuthConstants.cs`):**

| Provider | Callback path | URI production cần khai báo |
|---|---|---|
| Google | `/api/auth/callback/google` | `https://<api>.onrender.com/api/auth/callback/google` |
| GitHub | `/api/auth/callback/github` | `https://<api>.onrender.com/api/auth/callback/github` |

**(c) Từng bước — Google Cloud Console:**
1. [ ] [console.cloud.google.com](https://console.cloud.google.com) → chọn project đang dùng → **APIs & Services → Credentials**.
2. [ ] Ở **OAuth 2.0 Client IDs**, bấm vào client **Web application** đang dùng → **Edit**.
3. [ ] **Authorized redirect URIs → Add URI** → dán URI production ở bảng trên → **Save**.
   - ⚠️ **Giữ lại** URI localhost cũ, đừng xoá — nếu không, dev local sẽ hỏng.
4. [ ] Nếu Google yêu cầu **Authorized JavaScript origins**: thêm `https://<fe>.vercel.app` (luồng của dự án là server-side redirect nên thường không bắt buộc, nhưng thêm cho chắc).
5. [ ] Copy **Client ID**/**Client secret** → nhập vào env Render (§5.6).
6. [ ] Nếu **OAuth consent screen** còn ở chế độ **Testing**: thêm email của bạn vào **Test users**, nếu không sẽ bị chặn khi đăng nhập bằng tài khoản khác.

**(c) Từng bước — GitHub OAuth App:**
1. [ ] GitHub → **Settings → Developer settings → OAuth Apps** → chọn app đang dùng → **Edit** (hoặc **New OAuth App**).
2. [ ] **Authorization callback URL** → thêm/đổi thành `https://<api>.onrender.com/api/auth/callback/github`.
   - ⚠️ GitHub chỉ cho **một** callback URL mỗi app. Muốn giữ cả localhost **và** production ⇒ tạo **app thứ hai** (một `TeamNexus Local`, một `TeamNexus Prod`) rồi dùng cặp Client ID/Secret khác nhau theo môi trường. Đây là cách đúng, đừng cố nhồi 2 URL vào 1 app.
3. [ ] Copy **Client ID** + tạo/copy **Client secret** → nhập vào env Render (§5.6).
4. [ ] Nếu app cũ đang là **OAuth App** thì không cần scope thêm; quyền `user:email` đã được code yêu cầu sẵn.

**(e) Kiểm tra thành công:**
- [ ] Mở `https://<api>.onrender.com/api/auth/login/google` trong tab ẩn danh ⇒ được đưa sang màn hình chọn tài khoản Google (không phải trang lỗi `redirect_uri_mismatch`).
- [ ] Sau khi chọn tài khoản ⇒ được redirect về `https://<fe>.vercel.app` (đúng `Frontend__BaseUrl`) với cookie được set.
- [ ] DevTools → **Application → Cookies → `https://<api>.onrender.com`**: thấy `access_token`, `refresh_token`, `TeamNexus.Antiforgery` và **`SameSite=None`, `Secure` ✓**.
  - ⚠️ Nếu `SameSite=Lax` ⇒ §3.1 chưa áp đúng hoặc quên env `Auth__CookieSameSite=None`.

**(f) Pitfall riêng:**
- ⚠️ Sửa xong console nhưng **quên nhập env trên Render** ⇒ provider coi như chưa cấu hình và `/api/auth/login/google` trả **400** (đúng thiết kế "app vẫn boot với một tập con provider").
- ⚠️ Redirect về FE nhưng FE lại báo chưa đăng nhập ⇒ gần như luôn là **cookie cross-site** (§3.1), không phải lỗi OAuth.
- ⚠️ Đổi domain Render (xoá/tạo lại service) ⇒ **phải cập nhật lại cả Google và GitHub**.

---

### 5.6 Bảng biến môi trường đầy đủ

> .NET đọc config bằng dấu `:` trong `appsettings.json`, nhưng env dùng `__` (hai gạch dưới). Ví dụ `ConnectionStrings:DefaultConnection` ⇒ `ConnectionStrings__DefaultConnection`.
> Mảng cũng theo chỉ số: `Cors:AllowedOrigins[0]` ⇒ `Cors__AllowedOrigins__0`.

**A. API trên Render (hoặc Railway)**

| Env | Giá trị | Bắt buộc | Ghi chú |
|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | ✔ | Ẩn Scalar/OpenAPI (D14); tắt dev pages |
| `ASPNETCORE_URLS` | `http://0.0.0.0:$PORT` | ✔ | ⚠️ Render gán port động — thiếu ⇒ 502 |
| `ConnectionStrings__DefaultConnection` | `Host=<neon-host>;Port=5432;Database=neondb;Username=...;Password=...;SslMode=Require` | ✔ | Dùng **pooled** cho runtime, **direct** cho migrate |
| `Jwt__SigningKey` | chuỗi ngẫu nhiên **≥ 32 byte** | ✔ | Key mới, **khác** key local; app fail-fast nếu thiếu/ngắn |
| `Frontend__BaseUrl` | `https://<fe>.vercel.app` | ✔ | Nơi redirect sau login/logout |
| `Cors__AllowedOrigins__0` | `https://<fe>.vercel.app` | ✔ | Thêm origin production; **giữ** localhost chỉ khi còn dev chung cấu hình |
| `Auth__CookieSameSite` | `None` | ✔ | §3.1 — thiếu ⇒ không đăng nhập được cross-site |
| `Authentication__Google__ClientId` | từ Google Console | ✔* | *nếu dùng Google |
| `Authentication__Google__ClientSecret` | từ Google Console | ✔* | |
| `Authentication__GitHub__ClientId` | từ GitHub OAuth App | ✔* | *nếu dùng GitHub |
| `Authentication__GitHub__ClientSecret` | từ GitHub OAuth App | ✔* | |
| `DeepSeek__ApiKey` | key thật | ✔ | Smart Setup, Observer, Agent Executor |
| `Tavily__ApiKey` | key thật | ➖ | Bắt buộc **chỉ** nếu dùng tool `WebSearch` của Agent |
| `Agent__Enabled` | `true` | ➖ | `false` ⇒ endpoint agent trả **503** |
| `Observer__Enabled` | `true` | ➖ | |
| `Observer__IntervalMinutes` | `60` | ➖ | Tăng lên (mặc định 30) để Observer đỡ "đốt" instance-hour free-tier |
| `Observer__StartupDelaySeconds` | `120` | ➖ | Tránh quét ngay lúc cold-start ập tới |
| `Reports__Enabled` | `true` | ➖ | `false` ⇒ export trả **503** |

**B. Frontend trên Vercel (hoặc Netlify)** — ⚠️ build-time

| Env | Giá trị | Bắt buộc | Ghi chú |
|---|---|---|---|
| `VITE_API_BASE_URL` | `https://<api>.onrender.com/api` | ✔ | Sửa xong **phải Redeploy**; dùng bởi cả `httpClient` và `resolveHubUrl` (§3.2) |
| `VITE_DEV_API_TARGET` | *(không đặt ở production)* | ➖ | Chỉ dùng cho Vite dev proxy ở local |

**C. Local (User Secrets, không commit)**

```powershell
dotnet user-secrets set --project src/TeamNexus.Api "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=TeamNexus;Username=postgres;Password=..."
dotnet user-secrets set --project src/TeamNexus.Api "Jwt:SigningKey" "<key ≥ 32 byte>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientId" "<...>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:GitHub:ClientSecret" "<...>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientId" "<...>"
dotnet user-secrets set --project src/TeamNexus.Api "Authentication:Google:ClientSecret" "<...>"
dotnet user-secrets set --project src/TeamNexus.Api "DeepSeek:ApiKey" "<...>"
dotnet user-secrets set --project src/TeamNexus.Api "Tavily:ApiKey" "<...>"
```

> 💡 Để verify **offline, 0 token**, đặt env `DeepSeek__ApiKey` = **một khoảng trắng** `' '` (env rỗng bị .NET coi là "unset" nên User Secrets sẽ thắng trở lại). Xem README dòng 137–138 và D5.

---

### 5.7 Migration lên DB cloud (thủ công — D12)

```powershell
# 1) Trỏ tạm vào Neon (dùng endpoint DIRECT, không dùng -pooler)
$env:ConnectionStrings__DefaultConnection = "Host=<neon-host>;Port=5432;Database=neondb;Username=...;Password=...;SslMode=Require"

# 2) Xem chuỗi migration (phải thấy 6 migration, mới nhất Phase7AiAgentSchema)
dotnet ef migrations list --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api

# 3) Áp lên Neon  (⚠️ môi trường sandbox/agent phải build trước rồi mới --no-build — xem README)
dotnet ef database update --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api

# 4) Xoá biến môi trường khỏi session
Remove-Item Env:\ConnectionStrings__DefaultConnection
```

- [ ] Kiểm tra: `select count(*) from "__EFMigrationsHistory";` trên Neon ⇒ **6**.
- [ ] Ghi lại **thời điểm** và **migration cuối** đã áp vào báo cáo §9.
- [ ] ⚠️ Trong môi trường sandbox/agent: **build 0/0 trước** (`dotnet build TeamNexus.sln -m:1 -nr:false`), rồi mọi lệnh `dotnet ef` phải `--no-build`; nếu bỏ qua, `migrations add` sẽ đọc DLL cũ trong `bin` và sinh migration **sai một cách im lặng** (README dòng 47–57).
- [ ] ⚠️ `ConnectionStrings__DefaultConnection` có thể chứa ký tự đặc biệt (`;`, `$`) ⇒ **dùng biến môi trường** như trên, đừng truyền qua tham số dòng lệnh.
- [ ] ⚠️ Code-first **không idempotent** nếu DB đã có schema do cách khác tạo; nếu `database update` báo bảng đã tồn tại ⇒ dừng lại, đối chiếu thủ công thay vì "xoá DB cho nhanh".

---

### 5.8 Deploy lần đầu — thứ tự bắt buộc & checklist nghiệm thu

**Thứ tự (làm sai thứ tự sẽ mất thời gian debug vô ích):**

1. [ ] Push repo lên GitHub (đã có nội dung §3, §4 xong).
2. [ ] Tạo **Neon** + lấy connection string (§5.2).
3. [ ] Chạy **migration** lên Neon (§5.7) — **trước khi** deploy API, để app không khởi động vào DB rỗng.
4. [ ] Deploy **API lên Render** (§5.3) → chờ Live → `/api/health` = 200.
5. [ ] Cập nhật **OAuth redirect URI** ở Google + GitHub (§5.5) với domain API mới.
6. [ ] Đặt `Frontend__BaseUrl` = domain FE mà bạn **sẽ** tạo (đoán trước `https://<name>.vercel.app`).
7. [ ] Deploy **FE lên Vercel** với `VITE_API_BASE_URL` = domain API (§5.4).
8. [ ] Thêm `Cors__AllowedOrigins__0` = domain FE thật vào Render → chờ Render redeploy.
9. [ ] Chạy checklist nghiệm thu bên dưới.
10. [ ] Ghi toàn bộ URL + ảnh chụp vào báo cáo `report/phase-8-*`.

**Checklist nghiệm thu deploy (10 bước, làm trên bản production thật):**

- [ ] `GET https://<api>.onrender.com/api/health` ⇒ **200** + JSON `status=ok`.
- [ ] Mở `https://<fe>.vercel.app` ⇒ trang login hiển thị (không màn hình trắng, không lỗi console).
- [ ] Đăng nhập **Google** ⇒ vào được app, DevTools thấy cookie `SameSite=None; Secure`.
- [ ] Đăng nhập **GitHub** ⇒ vào được app (nếu dùng app OAuth riêng cho prod).
- [ ] Tạo **Board** + **Column** + **Task** ⇒ F5 vẫn còn dữ liệu (chứng minh DB Neon ghi được).
- [ ] Mở **2 tab** cùng board ⇒ kéo task ở tab A, tab B cập nhật **không cần reload** (SignalR production hoạt động ⇒ chứng minh §3.2 + CORS + cookie đúng).
- [ ] **Smart Setup** với DeepSeek thật ⇒ có đề xuất; `approve` ⇒ task được ghi (Accountability Layer production hoạt động).
- [ ] Gán 1 task cho **AI Agent** + bấm "Chạy Agent" ⇒ thấy `AgentRunPanel` đổi trạng thái real-time; 1 lượt DeepSeek thật.
- [ ] **Export PDF + Excel** ⇒ file tải về mở được, tiếng Việt có dấu (chứng minh QuestPDF/ClosedXML chạy trên Linux).
- [ ] **Cold-start**: đóng mọi tab, chờ **≥ 20 phút**, mở lại ⇒ board tự phục hồi (xem tiêu chí §6.5) và đo **thời gian từ mở trang đến khi thao tác được**.

---

### 5.9 Lỗi thường gặp (tra nhanh)

| # | Triệu chứng | Nguyên nhân | Cách sửa |
|---|---|---|---|
| 1 | Đăng nhập xong nhưng app vẫn coi là chưa đăng nhập; F5 lại về trang login | Cookie `SameSite=Lax` không gửi được cross-site (B6) | §3.1 + env `Auth__CookieSameSite=None`; kiểm tra cookie trong DevTools |
| 2 | Google/GitHub báo `redirect_uri_mismatch` | Redirect URI production chưa khai báo | §5.5 — thêm đúng `https://<api>/api/auth/callback/{google\|github}` |
| 3 | Console browser báo CORS, request bị chặn | Thiếu origin FE trong `Cors:AllowedOrigins` | Thêm `Cors__AllowedOrigins__0` = domain FE rồi để Render redeploy |
| 4 | Build Render xanh nhưng truy cập báo **502** | App không listen đúng cổng động | Thêm `ASPNETCORE_URLS=http://0.0.0.0:$PORT` (bind `0.0.0.0`, không `localhost`) |
| 5 | API báo lỗi SSL / Npgsql khi kết nối DB | Thiếu `SslMode=Require` trong connection string | Thêm vào chuỗi kết nối Neon |
| 6 | Một tháng sau DB "biến mất" | Bạn đã dùng **PostgreSQL free của Render** (hết hạn 30 ngày) | Chuyển sang Neon (§5.2); đây là lý do D13 chọn Neon |
| 7 | SignalR trả 400/401 khi vào từ FE | CORS/credentials hoặc thiếu `Auth__CookieSameSite=None`; hoặc FE gọi WS vào sai host | §3.1 + §3.2; kiểm tra tab Network → WS |
| 8 | Real-time không chạy dù REST vẫn chạy | `resolveHubUrl` chưa áp dụng ⇒ WS gọi vào chính Vercel (B14) | §3.2, sau đó **Redeploy** Vercel |
| 9 | Mọi POST/PUT/DELETE trả **403 CSRF** trên production | Antiforgery cookie vẫn `SameSite=Lax` (chỉ vá token cookie) | §3.1 — áp option cho **cả** antiforgery cookie |
| 10 | F5 ở route con ⇒ 404 | Thiếu SPA rewrite | `frontend/vercel.json` (§5.4) hoặc `_redirects` cho Netlify |
| 11 | API trả 500 `relation "xxx" does not exist` | Migration chưa chạy trên DB cloud | §5.7 |
| 12 | Service bị suspend giữa tháng dù còn "free" | Vượt **750 giờ instance**/tháng của Render | Giảm service/keep-warm; theo dõi Billing → Usage |
| 13 | Đổi env trên Vercel mà app không đổi | `VITE_*` là build-time | Redeploy (không cần đổi code) |
| 14 | `dotnet ef` chạy local báo lỗi thiếu implementation / build fail | Đã gặp ở Phase 6 (thiếu `IReportExportService`) và sandbox MSBuild | Build **0/0 trước** rồi dùng `--no-build`; xem README dòng 47–57 |
| 15 | Log khởi động báo `Jwt:SigningKey must be at least 32 bytes` | Env sai tên hoặc key ngắn | Kiểm tra `Jwt__SigningKey` (hai gạch dưới) và độ dài ≥ 32 **byte** (không phải 32 ký tự) |

---

### 5.10 Vòng đời sau deploy (update / rollback / log / xoá)

- **Update code thường ngày:** push lên `main` ⇒ CI chạy ⇒ Render + Vercel tự deploy. Nếu có migration mới ⇒ **chạy §5.7 trước khi push** (vì app không tự migrate — D12).
- **Rollback:** Render → **Events** → chọn deploy trước → **Redeploy**; Vercel → **Deployments** → deploy cũ → **Promote to Production**. ⚠️ Rollback **không** rollback migration ⇒ nếu bản mới đã thêm cột, phải xử lý DB thủ công.
- **Xem log:** Render → tab **Logs** (build + runtime); Vercel → **Deployments → Functions/Logs** (chủ yếu là build log).
- **Xoá tài nguyên khi kết thúc demo:** xoá web service Render, xoá project Neon/Vercel (⚠️ xoá Neon ⇒ **mất toàn bộ dữ liệu**, tải `pg_dump` trước nếu cần giữ).

---

## 6. [Cold-start] Phân tích & xử lý SignalR reconnect

### 6.1 Phân tích (grounded vào code + nền tảng)

Ba tầng cùng "ngủ", nhưng mức độ ảnh hưởng khác nhau:

| Tầng | Hành vi ngủ | Ảnh hưởng | Cách xử lý |
|---|---|---|---|
| **Render API** | Spin down sau **15 phút** rỗi, thức dậy ~**1 phút** | ⚠️ **Nặng nhất**: WebSocket đứt; request đầu tiên (và mọi request trong lúc boot) fail/timeout | §6.2–§6.4 |
| **Neon DB** | Compute ngủ sau **5 phút**, tự thức khi có query (~vài trăm ms) | Nhẹ: request đầu chậm hơn chút; connection pool tự retry | **Không** cần làm gì; ghi nhận để không chẩn đoán sai |
| **Background service in-process** (`ObserverBackgroundService`, `AgentRunReaper`) | Bị **giết** khi app sleep/recycle | Run đang chạy dở có thể kẹt `Running`; Observer bỏ lỡ chu kỳ | ✅ **Đã có lưới an toàn**: `AgentRunReaper` dọn run mồ côi lúc boot (B10). **Không sửa backend** |

**Điểm chí tử hiện tại (B7):** `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])` chỉ thử **5 lần trong ~47 giây** rồi **bỏ cuộc vĩnh viễn**
(`onclose` ⇒ `disconnected`). Trong khi app cần tới **~60 giây** để thức dậy ⇒ **hệ thống hiện tại sẽ thua cold-start một cách xác định**.
Và ngay cả khi reconnect thành công, `useBoardHub` **không refetch** (B8) ⇒ sự kiện xảy ra trong lúc mất kết nối **mất vĩnh viễn** với client đó
(board vẫn "cũ" cho tới khi user F5).

### 6.2 Việc làm — frontend (đã mô tả ở §3.3)

- [ ] Retry **vô hạn có trần backoff** (`reconnectPolicy.ts`, hàm thuần + test).
- [ ] **Refetch board khi reconnect** (nối `refetch` từ `useBoard` xuống `useBoardHub`).
- [ ] `serverTimeoutInMilliseconds` / `keepAliveIntervalInMilliseconds` đặt thành hằng số có tên, đủ rộng để không tự ngắt trong lúc API bận.
- [ ] Nút **"Kết nối lại"** thủ công (trường hợp người dùng không muốn chờ backoff).
- [ ] **UX thông báo** phân biệt rõ 2 tình huống (giữ phong cách Tooltip/Tag tiếng Việt đã có ở `BoardView`):
  - `reconnecting` bình thường: "Đang kết nối lại máy chủ Real-time…" (giữ nguyên như hiện tại).
  - `reconnecting` **kéo dài** (sau mốc ~5 giây hoặc từ lần thử thứ 3): "Máy chủ đang khởi động lại (bản miễn phí tự ngủ khi rảnh). Quá trình có thể mất khoảng 1 phút — dữ liệu của bạn vẫn an toàn."
  - `disconnected`: "Mất kết nối máy chủ Real-time." + nút **Kết nối lại**.
- [ ] ⚠️ Không đổi kiến trúc: vẫn 1 hub, vẫn group theo `boardId`, vẫn optimistic-update như Phase 2.

### 6.3 Việc làm — backend

- [ ] **Không sửa gì** (B10 đã đủ). Ghi lại rõ trong tài liệu để người đọc không đi tìm "bug backend" khi gặp card kẹt `Running`.
- [ ] Nếu muốn chắc chắn hơn (tuỳ chọn): giảm `Observer:IntervalMinutes`/tăng `Observer:StartupDelaySeconds` để Observer không đốt instance-hour vào lúc cold-start (§5.6).

### 6.4 Keep-warm (tuỳ chọn — mặc định **KHÔNG** bật)

- [ ] Cách làm nếu muốn demo "không bao giờ ngủ": dùng dịch vụ ping miễn phí (cron-job.org, UptimeRobot) gọi `https://<api>.onrender.com/api/health` mỗi 10 phút.
- [ ] ⚠️ **Tính toán trước khi bật:** 10 phút/lần ≈ **744 giờ/tháng** — sát trần **750 giờ** của Render (một service). Chỉ cần thêm 1 lần trùng giờ hoặc một service thứ hai là **vượt trần ⇒ bị suspend**.
- [ ] Vì vậy: **khuyến nghị để nguyên spin-down** và dạy UX tự phục hồi (§6.2). Đây cũng là hành vi trung thực với người xem CV ("app free tier, tự phục hồi sau cold-start" là điểm cộng, không phải điểm trừ).

### 6.5 Tiêu chí nghiệm thu cold-start (đo được, ghi vào báo cáo)

- [ ] Đóng tất cả tab, chờ **≥ 20 phút** để chắc chắn Render đã spin down.
- [ ] Mở lại `https://<fe>.vercel.app`, vào board đã có dữ liệu.
- [ ] **Kết quả mong đợi:** board hiển thị dữ liệu và chuyển về trạng thái **"Đã kết nối"** trong **≤ 90 giây**, **không cần F5**, không có vòng lặp lỗi.
- [ ] Kéo 1 task ⇒ **không** mất dữ liệu; mở tab thứ hai ⇒ real-time hoạt động trở lại.
- [ ] Ghi lại **số giây thật** từ lúc mở trang đến lúc tag "Đã kết nối" (đây là một trong những số liệu phải có trong báo cáo §9).
- [ ] Kiểm tra màn hình không báo lỗi đỏ khó hiểu cho người dùng cuối (chỉ hiển thị thông báo cold-start tiếng Việt).

---

## 7. [Hoàn thiện] Rà soát UI/UX + sẵn sàng demo/CV

> Cách làm **nhẹ hơn** các giai đoạn feature: đây là **rà soát theo checklist**, và chỉ sửa các mục nằm trong **danh sách chốt** dưới đây.
> Tránh biến giai đoạn test/deploy thành một giai đoạn redesign mới (xem Non-goals §0.3).

### 7.1 Đi một vòng toàn app (ghi lại phát hiện trước khi sửa)

- [ ] Đăng nhập (Google + GitHub) · trang lỗi OAuth (`?auth=error`) · đăng xuất
- [ ] Danh sách board · tạo/sửa/xoá board · trạng thái **rỗng** (chưa có board nào)
- [ ] Board Kanban: 0 cột · 1 cột · nhiều cột · kéo-thả cùng cột/khác cột · kéo vào cột `is_done` · nhiều tab cùng lúc
- [ ] Task detail: sửa tiêu đề/mô tả/hạn/priority · đổi assignee (gồm **AI Agent**) · label · comment
- [ ] Smart Setup: mô tả ngắn · mô tả rất dài · kết quả AI sai schema (mô phỏng) · luồng Pending → Approve/Reject/**Undo**
- [ ] Drawer: Lịch sử AI · Cảnh báo AI (Observer) · Thông báo (badge unread) · Lần quét Observer
- [ ] Agent Executor: gán task cho agent · Chạy · **Chờ làm rõ** (badge + modal câu hỏi) · Chạy lại · Huỷ · duyệt draft · tải attachment
- [ ] Reporting: chọn khoảng thời gian · summary · export PDF/Excel
- [ ] Trạng thái hệ thống: đang tải · lỗi mạng · 401 (hết phiên) · 403 · 404 · cold-start (§6)
- [ ] Kích thước: 1366×768 · 1920×1080 · cửa sổ hẹp ~1024 · tab mobile 390 (chỉ cần **không vỡ bố cục**, không yêu cầu tối ưu mobile — đó là Giai đoạn 9)
- [ ] Kiểm tra nhanh nội dung: còn chuỗi tiếng Anh lẫn vào UI tiếng Việt? lỗi chính tả? ngày tháng dd/MM/yyyy nhất quán?

### 7.2 Danh sách chốt những gì **được phép** sửa trong giai đoạn này

- [ ] **Empty state** cho: danh sách board, board chưa có cột, cột chưa có task, danh sách thông báo, kết quả Observer rỗng, báo cáo rỗng.
- [ ] **Loading state**: không có màn hình trắng khi chờ; skeleton/spinner ở chỗ đang tải.
- [ ] **Thông báo lỗi tiếng Việt thống nhất** cho các thao tác thất bại (dùng `message`/`antd` như code hiện có, không dựng hệ thống toast mới).
- [ ] **Trang 404** cho route không tồn tại + giữ nguyên hành vi bảo vệ route hiện có.
- [ ] **Favicon + `<title>`** (`index.html`) đúng tên sản phẩm.
- [ ] Thay các `console.error`/`console.warn` lộ ra ở luồng người dùng bằng thông báo người dùng hiểu được (giữ log kỹ thuật nhưng hạ mức, ví dụ `console.debug`).
- [ ] Sửa các phát hiện **rõ ràng là lỗi** từ §7.1 (text sai, căn lệch, nút chết, trạng thái không đồng bộ).
- [ ] ❌ **Không** trong danh sách: đổi layout/luồng chính, thêm tính năng, dark mode, i18n đa ngôn ngữ, refactor style.

### 7.3 Sẵn sàng demo / CV

- [ ] `README.md`: mô tả kiến trúc + **link demo** (FE + API health) + **link hình ảnh/ảnh chụp** board + badge CI + số test thật + hướng dẫn chạy local/test.
- [ ] Ghi rõ trong README: OpenAPI/Scalar **đang ẩn ở production** (D14) và cách mở nếu muốn cho nhà tuyển dụng xem API:
  ```csharp
  // trong Program.cs — mở docs cả ở production (tuỳ chọn, D14)
  if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
  ```
  rồi đặt env `OpenApi__Enabled=true` trên Render. **Chỉ làm nếu** bạn thật sự muốn public API docs.
- [ ] Kịch bản demo (viết thành 5–7 bước, tập 1 lần trước khi quay/trình bày): đăng nhập → tạo board → Smart Setup → duyệt AI action → gán task cho AI Agent và chạy → trạng thái "Chờ làm rõ" → export báo cáo PDF.
- [ ] Chuẩn bị **phương án B khi demo trực tiếp**: nếu API đang ngủ, bắt đầu demo bằng việc đã mở sẵn 1 tab từ trước (hoặc chấp nhận nói rõ "bản free tự ngủ, đang thức dậy" — §6.2 đã có thông báo cho người xem thấy).
- [ ] (Tuỳ chọn) quay **video 1–2 phút** làm bằng chứng cold-start + real-time, vì đó là thứ khó khoe bằng ảnh tĩnh.

---

## 8. Migrations & công việc bên ngoài repo

- [ ] **Không có migration mới** trong Giai đoạn 8 (D2–D3 không tạo bảng; §3 chỉ đổi cookie + frontend). Nếu bắt buộc phải thêm ⇒ cập nhật `04-database-design.md` trước và ghi lại quyết định.
- [ ] Xác nhận sau khi xong §2–§7: `dotnet ef migrations list` = **6**, `TeamNexusDbContextModelSnapshot` **không đổi**, không có file migration mới.
- [ ] **Công việc bên ngoài repo** (ghi lại ngày làm + kết quả để đưa vào báo cáo):
  - [ ] Tạo repo GitHub + (tuỳ chọn) branch protection cho `main`.
  - [ ] Tạo project **Neon** (region Singapore, Postgres 18) + lưu connection string vào password manager.
  - [ ] Tạo **Render** Web Service (Free, `teamnexus-api`) + nhập đủ env §5.6.
  - [ ] Tạo **Vercel** project (Root Directory `frontend`) + `VITE_API_BASE_URL`.
  - [ ] Google Cloud Console: thêm redirect URI production; kiểm tra **Test users** nếu consent screen còn Testing.
  - [ ] GitHub OAuth Apps: **tạo app riêng cho production** (giữ app local riêng) và thêm callback URL.
  - [ ] (Tuỳ chọn) dịch vụ ping nếu quyết định bật keep-warm (§6.4).
- [ ] **Không** commit bất cứ secret nào trong suốt quá trình; kiểm tra lại lần cuối trước khi push nhánh cuối (§5.1).

---

## 9. Definition of Done, nghiệm thu & rủi ro

### 9.1 DoD của Giai đoạn 8

- [ ] **Test:** `dotnet test` xanh (số test thật ghi vào báo cáo); `npm test` **> 187** test; `oxlint` 0/0; `npx tsc -b` exit 0; `npm run build` OK.
- [ ] **Build sản phẩm:** `dotnet build TeamNexus.sln` **0 warning / 0 error**; `dotnet ef migrations list` = **6**.
- [ ] **CI:** 2 workflow xanh trên một PR thật; badge hiển thị trong README.
- [ ] **Deploy:** API Live trên Render (`/api/health` 200), DB Neon đã migrate (6), FE Live trên Vercel; các URL ghi vào README + báo cáo.
- [ ] **Chức năng trên bản production:** 10 mục checklist §5.8 đều PASS, **đặc biệt** mục real-time 2 tab, Agent Executor và export PDF/Excel (đây là những chỗ dễ "chạy local được mà production không").
- [ ] **Cold-start:** đo được ≤ **90 giây** tự phục hồi sau ≥ 20 phút rỗi, không cần F5 (§6.5), có số thật.
- [ ] **UI/UX:** đi hết §7.1 một vòng, mọi mục trong danh sách chốt §7.2 đã xử lý, ❌ không phát sinh redesign ngoài danh sách.
- [ ] **5 ô roadmap Giai đoạn 8** đều có thể tick, mỗi ô dẫn được tới bằng chứng cụ thể.
- [ ] **Báo cáo:** `Project-Documents/report/phase-8-completion-test-deploy-report.md` tồn tại, có số liệu thật + URL + ảnh chụp + thời gian cold-start.

### 9.2 Ma trận rủi ro

| Rủi ro | Khả năng | Ảnh hưởng | Giảm thiểu |
|---|---|---|---|
| Render không auto-detect .NET 10 | Trung bình | Cao (không deploy được) | Đã có Dockerfile mẫu §5.3; chỉ dùng khi auto-detect fail |
| Chính sách free-tier đổi (giá/hạn mức) | Cao (theo thời gian) | Trung bình | Tài liệu ghi rõ nguồn + ngày kiểm chứng; luôn đối chiếu pricing trước khi làm |
| `SameSite=None` gây hồi quy luồng login local | Thấp (mặc định vẫn `Lax`) | Cao | Giữ `Lax` mặc định; chạy lại test Auth + login local thật sau khi vá |
| Vượt 750 giờ instance/tháng của Render | Trung bình nếu bật keep-warm | Trung bình (service bị suspend) | Mặc định **không** keep-warm (§6.4); theo dõi Billing → Usage |
| Neon hết 0.5 GB do attachment `bytea` | Thấp | Trung bình | Cap 512 KB/file đã có; theo dõi quota; `agent_runs` retention 90 ngày |
| Test chạy song song trên cùng DB test gây flaky | Trung bình | Trung bình | Fixture reset theo test class + tách dữ liệu theo workspace; chạy `dotnet test` 2 lần liên tiếp trong DoD §2.7 |
| CI fail vì quên `-m:1 -nr:false` (thói quen sandbox) | Thấp | Thấp | Dùng đúng 1 lệnh duy nhất trong workflow (§4.1) |
| `dotnet ef` hỏng vì thiếu implementation DI (lỗi đã gặp ở Phase 6) | Thấp | Trung bình | Build 0/0 trước; ghi lại lỗi này ở §5.9 dòng 14 để tra nhanh |
| Cold-start dài hơn 90 giây | Trung bình | Trung bình | Nới tiêu chí trong báo cáo kèm số thật + nêu rõ là hành vi free-tier; cân nhắc Railway $5/tháng nếu cần |

### 9.3 Báo cáo cần nộp

- [ ] `Project-Documents/report/phase-8-completion-test-deploy-report.md` — gồm: bảng số test backend/frontend thật; kết quả 2 workflow CI (link run);
  URL production (API + FE); kết quả 10 mục checklist §5.8; **thời gian cold-start đo được**; danh sách phát hiện UI/UX + đã sửa gì + cố ý bỏ gì;
  bug thật bắt được trong giai đoạn (nếu có) và cách sửa; ảnh chụp màn hình làm bằng chứng.
- [ ] Cập nhật `03-roadmap.md` (tick 5 ô) + `README.md` (link demo, badge, số test) sau khi hoàn thành.
