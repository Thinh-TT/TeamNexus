# Giai đoạn 11 — Quản lý Member & Profile (Kế hoạch chia task)

> **Trạng thái thi hành:** 🔄 **Backend XONG; frontend + CI bàn giao antigravity** (`tasks/phase-11-remaining-frontend-handover.md`).
> ✅ **§1 Persistence & migration** (2 bảng mới, migration `Phase11MemberProfile`, **9 migration**) ·
> ✅ **§2 Email gateway (Resend)** (`EmailTemplateTests` 14/14 PASS) ·
> ✅ **§3 Mời member qua email** (`InvitationApiTests`) ·
> ✅ **§4 Gửi email nhanh + Quản lý member** (`QuickEmailApiTests`, `WorkspaceMemberApiTests`) ·
> ✅ **§5 Profile cá nhân** (`ProfileApiTests`, `AvatarUrlTests`) ·
> ✅ **§6 Notification Center + trigger assign/comment** (`NotificationTriggerApiTests`).
> ⬜ **§7 Frontend** và **§8 CI & tài liệu** — đã viết note bàn giao.
>
> **Đo được trên máy dev (không có PostgreSQL ⇒ 237 test bị SKIP — xem §1.2):**
> `dotnet build TeamNexus.sln -m:1 -nr:false` = **0 warning / 0 error** ·
> `dotnet test` = **total 371 / Passed 134 / Skipped 237 / Failed 0** · test thuần **134/134 PASS** ·
> `dotnet ef migrations list` = **9** · `has-pending-model-changes` = **không có**.

> **Nguồn:** `Project-Documents/03-roadmap.md` → *Giai đoạn 11: Quản lý Member & Profile*.
> **Tiền đề:** Giai đoạn 7 (AI Agent Executor) · 8 (Test/CI/Deploy) · 9 (Đơn giản hoá Role) · 10 (Nâng cao Task & Workspace UX) — **đã merge**.
> **Nhánh:** `feat/phase11-member-profile`
> **Schema:** ✅ **CÓ migration đầu tiên sau Giai đoạn 9.** `20260913180740_Phase9ModelSync` (thứ 8) → thêm `Phase11MemberProfile` ⇒ **9 migration**.
> Giai đoạn này chỉ **thêm 2 bảng mới** (`workspace_invitations`, `email_messages`); **không** sửa cột/bảng nào đang tồn tại.

---

## 0. Mục tiêu & 5 ô hoàn thiện

Xây luồng mời thành viên qua email, quản lý danh sách member, profile cá nhân và notification center.

| # | Yêu cầu roadmap | Trạng thái đầu kỳ (đã khảo sát code) | Việc phải làm |
|---|---|---|---|
| **A** | **Mời member qua email**: Manager/Admin nhập email → gửi link qua Resend → người nhận click link, chọn role (mặc định Member), tham gia workspace | **Chưa có gì**: không bảng, không service, không endpoint, không gửi email. `workspace_invitations` mới chỉ nằm trong `04` §3.3 | §2, §3 |
| **B** | **Gửi email nhanh**: Manager soạn tiêu đề + nội dung → gửi tới 1 hoặc nhiều member qua Resend | **Chưa có gì**; repo **không có** bất kỳ hạ tầng email nào | §2 (`IEmailSender` + `email_messages`), §4 |
| **C** | **Quản lý member**: xem danh sách + role · huỷ invitation đang chờ · đổi role member · kick member | `GET /api/workspaces/{id}/members` **đã có** (`WorkspaceMemberService`, 5 field) — nhưng **không** có email/`joinedAt`/`isOwner`, **không** có mutation nào | §4 (mở rộng response), §5 |
| **D** | **Profile cá nhân**: sửa `display_name` + `avatar_url` · xem workspace đang tham gia + role · tự rời workspace | `users.display_name`/`avatar_url` **đã có** từ Giai đoạn 1 và `GET /api/auth/me` đã trả; **không** có endpoint sửa, **không** trang profile, **không** API rời workspace | §6, §9 |
| **E** | **Notification Center**: badge unread + drawer tại header — thông báo khi **được assign task**, **có comment mới trên task của mình**, **được mời vào workspace** | Hạ tầng **đã có toàn bộ**: bảng `notifications`, `INotificationService` (list/read/read-all/count), `NotificationDrawer` + `NotificationItem` + `useNotifications` (đã test). **Nhưng chỉ có 3 nguồn ghi**: Observer (Manager/Admin) + 2 loại cảnh báo Agent. **Không** có trigger nào cho assign/comment, và drawer **chưa xuất hiện ở header** trang chủ | §7, §8 |

**Ngoài 5 ô trên, 4 hạng mục kỹ thuật bắt buộc phát sinh** (phát hiện khi khảo sát code):

- **F** — **Module `Ai` đang sở hữu "notification"** (bảng, service, endpoint, drawer) nhưng Giai đoạn 11 **không còn là tính năng AI**. Phải **đổi nhãn UI** ("Cảnh báo AI Observer" → "Trung tâm thông báo") + bổ sung từ vựng type mới, **không** di chuyển code (xem D11).
- **G** — **Header bị copy 5 lần** (`DashboardPage`, `BoardListPage`, `ReportsPage`, `WorkspaceSettingsPage`, `WorkspaceActivityPage`). Yêu cầu E nói *"badge unread + drawer **tại header**"* ⇒ tách `AppHeader` dùng chung (cùng tinh thần hạng mục G của Giai đoạn 10 với `useWorkspaceRole`).
- **H** — `NotificationDrawer` **bắt buộc** có prop `workspaceId` (truyền xuống `NotificationItem` để điều hướng) và tiêu đề hard-code *"Cảnh báo AI Observer"*. Phải sửa để dùng được ở header toàn cục mà **không** phá `BoardView` đang truyền prop.
- **I** — `TestScenario.ResetDatabaseAsync` **TRUNCATE theo danh sách tường minh**. Thiếu 2 bảng mới ⇒ dữ liệu invitation **rò rỉ giữa các test**. ✅ **đã xử lý ở §1**.

---

## 1. Baseline & bối cảnh đã khảo sát

### 1.1 Đã có sẵn — **KHÔNG làm lại** (bằng chứng trong repo)

| Hạng mục | Bằng chứng (file · dòng) |
|---|---|
| `users.display_name` / `users.avatar_url` | `Persistence/Data/Entities/ApplicationUser.cs` 11, 13 · `04-database-design.md` §3.1 |
| `GET /api/auth/me` trả `id/email/displayName/avatarUrl/roles` | `Modules/Auth/…/Endpoints/AuthEndpoints.cs` 131–151 |
| JWT **có** claim `ClaimTypes.Email` khi user có email | `Modules/Auth/…/Services/JwtService.cs` 44–47 · `TestJwt.cs` 39–42 |
| `useAuthStore` đã map `displayName`/`avatarUrl` + event `auth:unauthorized` | `frontend/src/features/auth/store/useAuthStore.ts` 4–10, 69–77 |
| Bảng `notifications` (1 row/người nhận, `(recipient_user_id, is_read)`), **không soft-delete** | `Entities/Notification.cs` · `Configurations/NotificationConfiguration.cs` 52 |
| `INotificationService`: `ListAsync` / `MarkReadAsync` / `MarkAllReadAsync` / `CountUnreadAsync` + `NotifyManagersAsync` ×2 | `Modules/Ai/…/Services/NotificationService.cs` 37–73 |
| Endpoint `/api/notifications` (list · read · read-all) — **đã scoped theo người gọi** | `Modules/Ai/…/Endpoints/NotificationEndpoints.cs` 29–37 |
| FE: `notificationApi`, `useNotifications`, `NotificationDrawer`, `NotificationItem` — **đã có test** (3 file) | `frontend/src/features/ai/{services,hooks,components}/…` |
| `WorkspaceMember` + role/`member_type`/`joined_at` + query filter theo workspace soft-delete | `Entities/WorkspaceMember.cs` · `WorkspaceMemberConfiguration.cs` 13–21, 59 |
| `RequireMemberAsync` (404) · `RequireManagerAsync` (403) · `IsAtLeast` | `Modules/Board/…/Services/WorkspaceAccess.cs` 12–27 |
| Port–adapter Board khai báo / Ai cài đặt thật (2 tiền lệ) | `IActivityLogWriter` (+`NullActivityLogWriter`) `BoardModule.cs` · `IAiAgentResolver` · Ai ghi đè ở `AiModule.cs` |
| `DomainExceptionFilter` (`{ error }` + status từ `BoardModuleException`) | `Modules/Board/…/Endpoints/DomainExceptionFilter.cs` 19–21 · `CurrentUser.cs` `RequireUserId()` |
| Pattern gọi HTTP ngoài không cần SDK: named `HttpClient` + Options + `HasApiKey` switch | `AiModule.cs` (DeepSeek/Tavily) · `Options/TavilyOptions.cs` |
| Hash token: `JwtService.HashToken` = `Convert.ToHexString(SHA256.HashData(...))` | `Modules/Auth/…/Services/JwtService.cs` 71–72 |
| `isSafeHref` (chỉ `http:`/`https:`) đã có ở FE | `frontend/src/features/board/utils/markdown.tsx` |

### 1.2 Baseline **đo tại máy dev** (phiên lập kế hoạch)

```
frontend/  npm test -- --reporter=json        → 279 passed / 0 failed  (47 file test)
backend/   dotnet test (KHÔNG có PostgreSQL)  → Failed 0 / Passed 98 / Skipped 128 / Total 226
backend/   dotnet build TeamNexus.sln -m:1 -nr:false → 0 warning / 0 error
src/TeamNexus.Persistence/Migrations/*.cs     → 8 migration (nay 9 sau §1)
```

> ⚠️ **Cổng "xanh giả" (di truyền từ Phase 10 §1.2).** Máy dev của phiên này **không** có PostgreSQL
> (`TEAMNEXUS_TEST_DB` chưa đặt; `postgres/postgres` ở `localhost:5432` bị skip sạch; **Docker daemon
> không chạy** nên không dựng được container `postgres:18`). Vì vậy **128 test phụ thuộc DB đang bị
> SKIP** và số **226** **không** được dùng làm bằng chứng đạt DoD.
>
> **Bắt buộc chạy lại trước khi push:**
> ```powershell
> $env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5432;Database=TeamNexus_Test;Username=postgres;Password=<pw>"
> dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj -m:1 -nr:false
> ```
> Điều kiện chốt: **`Skipped: 0`**. Nếu số lệch 226 ⇒ **số đo thắng tài liệu**, cập nhật §1.2 + cổng CI.

---

## 2. Bảng quyết định kiến trúc (D1–D14) — chốt sẵn, không chọn lại

| # | Quyết định | Lý do / ràng buộc |
|---|---|---|
| **D1** | Thêm đúng **2 bảng** trong **1 migration** `Phase11MemberProfile`: `workspace_invitations` (theo `04` §3.3) và `email_messages` (**bảng mới, quyết định của giai đoạn này**). **KHÔNG** sửa bảng/cột hiện có | `workspace_invitations` đã được thiết kế sẵn từ Giai đoạn 1 — hiện thực đúng để không lệch DB design. `email_messages` là bằng chứng đã gửi + bộ đếm cho hạn mức free-tier (Resend 3.000 email/tháng) |
| **D2** | Partial unique index `uq_workspace_invitations_pending` = `(workspace_id, invited_email) WHERE status = 'Pending'`; `uq_workspace_invitations_token_hash` UQ; IX `(workspace_id, status)` | `04` §3.3 yêu cầu; EF Core dùng `.HasFilter(...)` (tiền lệ `uq_workspace_members_ai_agent`) |
| **D3** | Token mời = **64 byte random** → **chỉ lưu SHA-256 hex**; token thô chỉ nằm trong email. **Trả token qua BODY của POST**, không qua URL | Đúng pattern `refresh_tokens.token_hash`; body không lọt vào access log/proxy. Hash viết cục bộ trong `InvitationService` (Board **không** tham chiếu Auth — cùng lý do đã ghi ở `IEmailGateway`) |
| **D4** | Vai trò khi accept = **`invited_role` do người mời chọn** (mặc định `Member`). **Người nhận KHÔNG tự chọn role** | Roadmap nói *"chọn role (mặc định Member)"*; `04` §3.3 nói *"Accept → tạo `workspace_members` với `invited_role`"*. Nếu để người nhận chọn ⇒ tự nâng lên `Admin` = leo thang đặc quyền. UI accept **hiển thị** role sắp nhận |
| **D5** | Accept yêu cầu **đăng nhập** + email tài khoản **khớp `invited_email`** (không phân biệt hoa/thường). Không khớp ⇒ **403**. Chưa đăng nhập ⇒ FE lưu token vào `sessionStorage` → OAuth → accept lại | Link mời **không** là đường vào ẩn danh. Khớp `04` §3.3 *"redirect sang đăng nhập/đăng ký OAuth trước"* |
| **D6** | **Lazy expiry**, **không** BackgroundService: mọi lần đọc/accept gặp row `Pending` quá hạn ⇒ set `Expired`; accept ⇒ **410** | `04` §3.3 cho phép *"kiểm tra lazy khi accept"*; tránh thêm hosted service. Hết hạn **không** chặn Manager mời lại (D2 chỉ áp cho `Pending`) |
| **D7** | Quyền mới: **xem member** Member+ · **mời/huỷ invitation** Manager+ · **đổi role** Admin · **kick** Admin · **gửi email nhanh** Manager+ · **rời workspace** chính mình (trừ owner) | `03-roadmap.md` 191 ghi rõ *"đổi role member (**chỉ Admin**), kick member (**chỉ Admin**)"*; ô A ghi *"Manager/Admin nhập email"* ⇒ mời = Manager+ |
| **D8** | Bất biến bảo vệ: không đổi role/kick **owner**; không hạ cấp/xoá **Admin đang hoạt động cuối cùng**; **không** thao tác trên `member_type = 'ai_agent'` (**400**); tự kick/đổi role chính mình ⇒ **400** | Cùng tinh thần D3 Giai đoạn 10. Không có luật "Admin cuối cùng" thì workspace có thể mất hết người quản trị |
| **D9** | **Port do module `Board` khai báo, `Ai` cài đặt thật**: `IEmailGateway` (mới) + `INotificationWriter` (mới); `BoardModule` đăng ký `NullEmailGateway`/`NullNotificationWriter`, `AddAiModule` ghi đè (`Program.cs` gọi Board **trước** Ai) | **Đúng tiền lệ `IActivityLogWriter` / `IAiAgentResolver`** — Board **không được** tham chiếu Ai. Writer/gateway **không bao giờ được ném** (best-effort) |
| **D10** | Từ vựng type mới nằm ở **lớp hằng riêng `MemberNotificationTypes`**, **KHÔNG** thêm vào `NotificationTypes.All` | `NotificationTypes.All` là **whitelist chống hallucination** của Observer (`ObserverFindingValidator`). Thêm vào đó ⇒ model Observer có thể sinh `TaskAssigned` và **qua** validator. Đúng lý do đã tách `AgentNotificationTypes` (Phase 7) |
| **D11** | **KHÔNG** di chuyển notification/email ra module mới: email ở `Modules/Ai/…/Services/Email/`, `INotificationService` giữ nguyên chỗ | Tách module ⇒ phải di chuyển `NotificationService` + DTO + factory + test và tạo cạnh phụ thuộc mới; lợi ích chỉ là tên gọi. Rủi ro phá 4 file test đang xanh **không** tương xứng. Ghi lại thành **nợ kỹ thuật có chủ ý** (§11 E2) |
| **D12** | Gửi email qua **`HttpClient` + `JsonSerializer`** tới `POST {BaseUrl}/emails`, **KHÔNG** thêm package `Resend` | Đúng tiền lệ DeepSeek/Tavily (0 dependency SDK). `EmailOptions.HasApiKey` trống ⇒ `NullEmailSender` (offline/CI) |
| **D13** | **Không thêm thư viện npm** | Đúng hợp đồng "⛔ KHÔNG được làm" của Phase 10 §10 |
| **D14** | Nhãn UI: *"Cảnh báo AI Observer"* → **"Trung tâm thông báo"**; **giữ nguyên** tên event SignalR, shape `NotificationResponse`, endpoint `/api/notifications`, `httpClient.ts` | Bảo toàn hợp đồng đã verify Phase 5/7. Chỉ **thêm** type key, **append** field cuối DTO |

### 2.1 Phát sinh **bắt buộc** khi hiện thực (ghi lại để không bị coi là sai lệch kế hoạch)

| # | Phát sinh | Vì sao |
|---|---|---|
| **P1** | `InvitationSettings` (FrontendBaseUrl + expiry days) là **port thứ ba** của Board, Ai cung cấp | Bản kế hoạch để `InvitationService` tự dựng link từ `IConfiguration`; nhưng Board **không được** tham chiếu Ai, và `Frontend:BaseUrl` nằm ở section chung. `InvitationSettings` là cách nhỏ nhất để Board vẫn tự đứng được (`AddBoardModule` có tham số `IConfiguration?`) |
| **P2** | `AddBoardModule()` → `AddBoardModule(IConfiguration? configuration = null)` | Tham số **optional** ⇒ không phá harness nào chỉ nạp module Board (Phase 5 §2.1 yêu cầu Board tự đứng được). Chỉ `Program.cs` truyền config |
| **P3** | `FK workspace_invitations → workspaces` khai **optional** (`.IsRequired(false)`) | Bảng **cố ý không** có query filter (partial UQ phải đọc được invitation của workspace soft-deleted). EF cảnh báo CS10622 khi principal có filter mà navigation là required ⇒ khai optional để hết warning mà giữ đúng hành vi. **0 warning** vẫn là DoD |
| **P4** | `AcceptTokenRequest` (record mới) + `HttpContext.GetUserEmail()` | Token đi qua body (D3); đọc email từ claim thay vì join DB (JWT đã có claim) |
| **P5** | `EmailTemplates.Encoder = HtmlEncoder.Create(UnicodeRanges.All)` | `WebUtility.HtmlEncode` mã hoá **mọi** ký tự non-ASCII ⇒ tiếng Việt thành `&#x1EC7;` trong body. Encoder giới hạn vẫn escape đủ 5 ký tự cấu trúc (`& < > " '`) mà giữ chữ có dấu đọc được |
| **P6** | `EmailTemplates.EncodeMultiline` | Encoder mặc định ghi newline thành `&#xA;`; phải tách dòng → encode từng dòng → nối bằng `<br />`, nếu không thì **không bao giờ** có `<br />` trong HTML |

---

## 3. §1 — Persistence & migration — ✅ **XONG**

### 1.1 File **mới**
| File | Nội dung |
|---|---|
| `Persistence/Data/Entities/WorkspaceInvitation.cs` | `InvitationStatus` (Pending/Accepted/Cancelled/Expired) + entity; token **chỉ** lưu hash |
| `Persistence/Data/Entities/EmailMessage.cs` | `EmailMessageStatus` (Queued/Sent/Failed) + entity; `BodyPreview` ≤ 500, **không** lưu body đầy đủ, **không** lưu token |
| `Persistence/Data/Configurations/WorkspaceInvitationConfiguration.cs` | 2 CHECK (`invited_role`, `status`), UQ `token_hash`, **partial** UQ `(workspace_id, invited_email) WHERE status='Pending'`, IX `(workspace_id, status)`, 3 FK Restrict (workspace **optional** — P3) |
| `Persistence/Data/Configurations/EmailMessageConfiguration.cs` | 1 CHECK (`status`), IX `(workspace_id, created_at)`, 2 FK Restrict |

### 1.2 File **sửa**
| File | Thay đổi |
|---|---|
| `Persistence/Data/TeamNexusDbContext.cs` | +2 `DbSet` (`WorkspaceInvitations`, `EmailMessages`) |
| `tests/…/Infrastructure/TestScenario.cs` | +`"email_messages"`, `"workspace_invitations"` vào mảng TRUNCATE (**hạng mục I**) |

### 1.3 Migration
`20260914105025_Phase11MemberProfile` — sinh bằng `dotnet ef migrations add … --no-build` (`dotnet ef` **không** nhận `-m:1`/`-nr:false`; phải build trước rồi dùng `--no-build`).

### 1.4 Bằng chứng đã đo
```
dotnet build TeamNexus.sln -m:1 -nr:false            → 0 Warning(s) / 0 Error(s)
dotnet ef migrations list --no-build                  → 9 (mới nhất 20260914105025_Phase11MemberProfile)
dotnet ef migrations has-pending-model-changes        → "No changes have been made to the model since the last migration."
Đọc lại Up(): CHỈ CreateTable ×2 + CreateIndex ×7 + CreateForeignKey ×5 — không ALTER/DROP nào
```

---

## 4. §2 — Email gateway (Resend) — ✅ **XONG**

### 2.1 File **mới**
| File | Nội dung |
|---|---|
| `Ai/Options/EmailOptions.cs` | Section `Email`: `ApiKey` (bí mật), `BaseUrl`, `FromAddress`, `TimeoutSeconds`, `InvitationExpiryDays` (7), `MaxRecipientsPerQuickEmail` (50), `MaxEmailsPerHourPerWorkspace` (100) + `HasApiKey` |
| `Ai/Services/Email/IEmailSender.cs` | `EmailEnvelope`, `EmailSendResult` (Sent/Failed), `IEmailSender` (**không bao giờ ném**), `NullEmailSender` |
| `Ai/Services/Email/ResendEmailSender.cs` | `POST {BaseUrl}/emails` snake_case + `Authorization: Bearer`; timeout/HttpRequestException ⇒ `Failed`; parse `id` là **audit data**, body lỗi parse **không** làm fail |
| `Ai/Services/Email/EmailDispatcher.cs` | `IEmailDispatcher` = **cổng duy nhất**: ghi row `Queued` → gửi → set `Sent`/`Failed` + `ProviderMessageId`/`Error`; 2 lớp fail-soft |
| `Ai/Services/Email/EmailTemplates.cs` | **Hàm THUẦN**: `Invitation` / `Quick` / `InvitationPreview` / `QuickPreview`; `EmailKinds`; HTML-escape mọi giá trị nội suy (P5, P6) |
| `Ai/Services/EmailGateway.cs` | Cài đặt **port của Board** `IEmailGateway`: render + gọi dispatcher, trả `bool` |
| `tests/…/Pure/EmailTemplateTests.cs` | **14 test** — giới hạn subject 200, acceptUrl có mặt, **escape** `<script>`/`<img onerror>`, escape quote trong href, preview **không** chứa token, giữ dấu tiếng Việt, `<br />` theo dòng, truncate preview 500 |

### 2.2 File **sửa**
| File | Thay đổi |
|---|---|
| `Ai/AiModule.cs` | `EmailHttpClientName = "Resend"`; `AddOptions<EmailOptions>`; named HttpClient (timeout); `IEmailSender` theo `HasApiKey`; `IEmailDispatcher`; **ghi đè** `IEmailGateway` + `InvitationSettings`; 1 dòng log cấu hình email (không chứa secret) |
| `src/TeamNexus.Api/appsettings.json` | +section `"Email"` (ApiKey để **rỗng**) |
| `tests/…/Infrastructure/TeamNexusApiFactory.cs` | +`builder.UseSetting("Email:ApiKey", " ")` — **bắt buộc**, nếu không key trong User Secrets sẽ khiến suite gửi email thật |

### 2.3 Bằng chứng đã đo
`dotnet test --filter "FullyQualifiedName~EmailTemplateTests"` → **Passed: 14 / Failed: 0 / Skipped: 0**.

---

## 5. §3 — Mời member qua email (ô **A**) — ✅ **XONG phần code, test chờ DB**

### 3.1 File **mới**
| File | Nội dung |
|---|---|
| `Board/DTOs/InvitationDtos.cs` | `InvitationResponse` (**append** `EmailSent` cuối cùng), `CreateInvitationRequest`, `InvitationPreviewResponse`, `AcceptInvitationResponse` |
| `Board/Services/InvitationService.cs` | `ListAsync` (Manager+, lazy-expire trước khi đọc, LeftJoin tên người mời) · `CreateAsync` (validate email RFC-lite + `ParseRole` **chặt**, chặn member trùng, **409** khi còn Pending, sinh token 64 byte, gửi email best-effort) · `CancelAsync` (404 khác workspace, 409 đã Accepted, idempotent) · `PreviewAsync` (ẩn danh) · `AcceptAsync` (email khớp, idempotent, **không** ghi đè role cũ, bắt `DbUpdateException` cho accept đua nhau) |
| `Board/Services/IEmailGateway.cs` | Port + `NullEmailGateway` (trả `false` để thiếu adapter **hiện ra** ở response, không bị giả là đã gửi) |
| `Board/Services/InvitationSettings.cs` | P1 |
| `Board/Endpoints/InvitationsEndpoints.cs` | `GET /api/invitations/preview` (**AllowAnonymous**) · `POST /api/invitations/accept` (**RequireAuthorization + CSRF**) · `GET|POST /api/workspaces/{id}/invitations` (Manager+) · `DELETE …/{invitationId}` (Manager+) |
| `tests/…/Integration/InvitationApiTests.cs` | **21 test method** (§5.3) |

### 3.2 File **sửa**
| File | Thay đổi |
|---|---|
| `Board/Services/DomainExceptions.cs` | +`GoneException` ⇒ **410** (token hết hạn/đã huỷ ≠ 404 "không tồn tại") |
| `Board/Services/IActivityLogWriter.cs` | +`InvitationCreated` / `InvitationCancelled` / `InvitationAccepted` (+`MemberRoleChanged`/`MemberRemoved`/`MemberLeft` dùng ở §4) |
| `Board/Endpoints/CurrentUser.cs` | +`GetUserEmail()` đọc `ClaimTypes.Email` |
| `Board/Endpoints/BoardEndpoints.cs` | +`endpoints.MapInvitationsEndpoints();` |
| `Board/BoardModule.cs` | +`IInvitationService`; +port `IEmailGateway`/`InvitationSettings`; +tham số `IConfiguration?` (P2) |
| `src/TeamNexus.Api/Program.cs` | `AddBoardModule(builder.Configuration)` |

### 3.3 Test đã viết (`InvitationApiTests` — 21 method)
Invite: tạo + email audit + **hash 64 char** + lifetime ~7 ngày · Member ⇒ 403 · người ngoài ⇒ 404 ·
`[Theory ×6]` email sai định dạng ⇒ 400 · chuẩn hoá hoa/thường · đã là member ⇒ 400 ·
trùng khi Pending ⇒ **409** · mời lại sau Cancel ⇒ 201 · `[Theory ×3]` role `"Boss"`/`"1"`/`"99"` ⇒ 400 ·
role `"manager"` ⇒ `Manager` · thiếu CSRF ⇒ 403.
List/Cancel: Member ⇒ 403 · quá hạn ⇒ `Expired` (**và** row trong DB được cập nhật) · Cancel ⇒ 204 + activity `board_id IS NULL` ·
Cancel chéo workspace ⇒ 404 · Cancel lời mời đã Accepted ⇒ 409 · Cancel 2 lần ⇒ 204.
Preview: ẩn danh **chỉ 7 key** · token rác ⇒ 404 · quá hạn ⇒ **410** + row `Expired`.
Accept: đúng email ⇒ tạo membership **đúng `invited_role`** + Accepted + activity · email khác ⇒ 403 + **vẫn** Pending ·
đã là member ⇒ `alreadyMember=true` và role **không** bị ghi đè (`Admin` invited vs `Member` sẵn có) ·
ẩn danh ⇒ 401 · hết hạn ⇒ 410 · đã huỷ ⇒ 410 · token rác ⇒ 404 ·
**token thô không xuất hiện** trong `activity_logs.payload`, `email_messages.body_preview`, hay response list.

> ⚠️ **CHƯA CHẠY ĐƯỢC** trên máy không có PostgreSQL (xem §1.2). Đã chạy được: `dotnet build` **0/0**.

---

## 6. §4 — Gửi email nhanh + Quản lý member (ô **B**, **C**) — ✅ **XONG** (backend)

- **`Board/Services/QuickEmailService.cs`** (mới): Manager+; validate `Subject` 1–200, `Body` 1–8000,
  recipients 1…`MaxRecipientsPerQuickEmail`; recipients **phải** là `human` member của workspace;
  **rate-limit** đếm `email_messages` trong 1 giờ qua ⇒ **429**; gửi tuần tự qua `IEmailGateway`,
  bỏ qua chính người gửi; trả `{ requested, sent, failed, errors[] }` (partial success hợp lệ).
- **`POST /api/workspaces/{workspaceId}/quick-email`** — Manager+, CSRF, **200** `QuickEmailResult`.
- **`DTOs/MemberDtos.cs`** (sửa): `WorkspaceMemberResponse` **append** `Email`, `JoinedAt`, `IsOwner` ở cuối
  (5 field cũ giữ nguyên tên/thứ tự — `useWorkspaceMembers` + assignee dropdown đang parse).
- **`Services/WorkspaceMemberService.cs`** (sửa): `GetMembersAsync` map thêm 3 field (**không** N+1);
  +`UpdateMemberRoleAsync` (Admin; chặn owner/agent/chính mình/**Admin cuối cùng**; parse role chặt) ·
  +`RemoveMemberAsync` (Admin; cùng bất biến; **xoá vật lý** row junction ⇒ 204 + activity `MemberRemoved`).
- **`Endpoints/MembersEndpoints.cs`** (sửa): `PUT /members/{memberUserId:guid}/role` (Admin, CSRF) ·
  `DELETE /members/{memberUserId:guid}` (Admin, CSRF).
- **Test:** `WorkspaceMemberApiTests` (M-1…M-18) + `QuickEmailApiTests` (Q-1…Q-10).

---

## 7. §5 — Profile cá nhân (ô **D**) — ✅ **XONG** (backend)

- **`Shared/Profile/AvatarUrl.cs`** (mới, **hàm THUẦN**): `IsSafe`/`Normalize` — chỉ `http:`/`https:` tuyệt đối,
  ≤ 2048; `javascript:`/`data:`/`file:`/`vbscript:`/`//x` ⇒ false. Bản backend đối chiếu `isSafeHref` của FE.
- **`Board/DTOs/UserProfileDtos.cs`** (mới): `UserProfileResponse`, `UpdateProfileRequest`, `MyWorkspaceResponse`.
- **`Board/Services/UserProfileService.cs`** (mới): `GetAsync` · `UpdateAsync` (displayName 1–120; avatarUrl
  validate **400** nếu scheme xấu, `""` ⇒ `null`) · `ListMyWorkspacesAsync` (**KHÔNG** tái dùng
  `WorkspaceService.ListForUserAsync` vì hàm đó **tự tạo workspace mặc định** — side effect mà `DashboardPage`
  phụ thuộc) · `LeaveWorkspaceAsync` (owner ⇒ 400; Admin cuối cùng ⇒ 400; xoá row ⇒ 204 + `MemberLeft`).
  > `users` **không** có `updated_at` và `ApplicationUser` **không** implement `IAuditableEntity` ⇒ chỉ ghi đè 2 field.
- **`Board/Endpoints/ProfileEndpoints.cs`** (mới): `GET|PUT /api/users/me` · `GET /api/users/me/workspaces` ·
  `DELETE /api/users/me/workspaces/{workspaceId:guid}` (mọi route **scoped theo người gọi**, không có `{userId}` trên path).
- **Test:** `ProfileApiTests` (P-1…P-16) + `Pure/AvatarUrlTests` (~10 case).

---

## 8. §6 — Notification Center (ô **E**) — ✅ **XONG** (backend)

- **`Ai/Services/MemberNotificationTypes.cs`** (mới): `TaskAssigned`, `CommentOnTask`, `WorkspaceInvitation` — **KHÔNG** vào `NotificationTypes.All` (D10).
- **`Ai/Services/NotificationService.cs`** (sửa): +`NotifyUsersAsync(workspaceId, recipientUserIds, type, title, message, payloadJson)`
  — 1 row/người nhận, tự loại trùng + loại chính người gây ra, không ném, cap `MaxRecipientsPerNotification = 100`.
- **`Board/Services/INotificationWriter.cs`** (mới): port + `NullNotificationWriter` (D9).
- **`Ai/Services/NotificationWriter.cs`** (mới): adapter, `try/catch` toàn bộ.
- **`Board/Services/TaskService.cs`** (sửa): `CreateTaskAsync` phát `TaskAssigned` khi assignee ≠ người tạo và **không phải** AI Agent;
  `UpdateTaskAsync` **chỉ** phát khi assignee **đổi** (không spam khi sửa title/priority). Nằm **sau** `SaveChanges`, ngoài transaction.
- **`Board/Services/CommentService.cs`** (sửa): `CreateCommentAsync` phát **1** row `CommentOnTask` cho **assignee** (≠ tác giả, không phải agent);
  `message` cắt 120 ký tự, **không** copy toàn văn comment.
- **`Board/BoardModule.cs`** + **`Ai/AiModule.cs`**: đăng ký/ghi đè `INotificationWriter`.
- **Test:** `NotificationTriggerApiTests` (N-1…N-12).
- **Tài liệu:** thêm 3 type vào bảng `NotificationType` ở `04-database-design.md` §4/§3.8 (free text ⇒ **không** migration).

---

## 9. §7 — Frontend — ⬜ **BÀN GIAO** (xem `tasks/phase-11-remaining-frontend-handover.md`)

Thư mục **mới**: `shared/components/AppHeader.tsx` (**G**) · `shared/components/NotificationBell.tsx` ·
`shared/hooks/useUnreadCount.ts` · `features/members/{types,services,utils,components,pages}` ·
`features/profile/{types,services,pages}` · `features/invitations/{services,utils,pages}`.

- **`AppHeader`** (hạng mục **G**): title · `children` · `NotificationBell` · `Dropdown` avatar (Hồ sơ cá nhân / Đăng xuất);
  refactor **5 trang** đang copy header (giữ đúng `children` hiện có; chỉ **thêm** `data-testid`, **không** viết lại test cũ).
- **`NotificationDrawer`** (hạng mục **H**): `workspaceId?: string` (**optional**, giữ `BoardView` đang truyền), tiêu đề **"Trung tâm thông báo"** (D14);
  không có `workspaceId` ⇒ **ẩn** nút điều hướng tới task, không crash.
- **`NotificationItem`**: thêm nhãn tiếng Việt cho 3 type mới; không có `severity` ⇒ chip `default` (không đỏ).
- **Members page** `/workspaces/:workspaceId/members`: `MembersTable` (đổi role/kick **chỉ Admin**, disable với owner/agent/chính mình) ·
  `PendingInvitationsTable` (huỷ khi `Pending`) · `InviteMemberModal` (role mặc định `Member`; `emailSent=false` ⇒ cảnh báo + "Gửi lại") ·
  `QuickEmailModal` (không hiện AI Agent). Nút **"Thành viên"** thêm ở `AppHeader` của `BoardListPage` qua `children`.
- **Profile page** `/profile`: tab Thông tin cá nhân (validate avatar bằng `isSafeHref` **đã có**) + tab Workspace của tôi (nút "Rời", ẩn/disable khi owner).
- **Accept page** `/invitations/accept` (**công khai**): `sessionStorage` giữ token qua OAuth; preview ⇒ "Tham gia"/"Từ chối";
  403 (email khác) / 410 (hết hạn–đã huỷ) ⇒ `<Result>` tiếng Việt; **luôn** xoá token sau khi xử lý.
- **`app/router.tsx`**: +3 route.
- **Test:** ~62 test mới / ~15 file (chi tiết từng file ở §7.7 của bản kế hoạch gốc).

---

## 10. §8/§9 — CI & tài liệu — ⬜ **BÀN GIAO** (xem `tasks/phase-11-remaining-frontend-handover.md`)

| # | File | Việc |
|---|---|---|
| 1 | `.github/workflows/ci-backend.yml` | Nâng cổng `if ($total -ne 226)` → **số đo thật**; +biến `Email__ApiKey: " "` để CI **không** gửi email thật; cập nhật comment chuỗi |
| 2 | `.github/workflows/ci-web.yml` | `if ($total -le 206)` → **`-le 279`** + comment baseline mới |
| 3 | `Project-Documents/04-database-design.md` | +**§3.9** (`email_messages` **bảng mới** + ghi chú `workspace_invitations`); cập nhật bảng `NotificationType`; +2 dòng index ở §5; **sửa §6 chuỗi migration 6 → 9** (đang lệch thực tế); +3 gạch đầu dòng ở §7 |
| 4 | `Project-Documents/03-roadmap.md` | Giai đoạn 11: gắn link tài liệu này + baseline + "+1 migration (9 tổng)" |
| 5 | `Project-Documents/01-system-specification.md` | §10: chốt luồng accept (email phải khớp tài khoản; role do người mời chọn) |
| 6 | `README.md` | +`## Trạng thái (Giai đoạn 11)`; **sửa mục Migration** (đang ghi sai *"6 migration, mới nhất `Phase7AiAgentSchema`"* → **9**); hướng dẫn `Email:ApiKey` |
| 7 | `Project-Documents/report/phase-11-member-profile-management-test-report.md` | Tạo khi kết thúc |

---

## 11. Ca biên & chế độ lỗi (bắt buộc xử lý)

| Ca | Hành vi chốt |
|---|---|
| Mời email đã là member | **400** |
| Mời trùng khi còn `Pending` | **409** (không 500 do UQ) |
| Mời lại sau `Cancelled`/`Expired` | **201** |
| Email sai định dạng / rỗng / có khoảng trắng | **400**; không ghi row |
| Role không hợp lệ (kể cả `"1"`, `"99"`) | **400** (không 500 do CHECK) |
| Gửi email thất bại / chưa cấu hình `Email:ApiKey` | Invitation **vẫn** tạo; **201** + `emailSent=false`; row `Failed` |
| Token preview/accept: rác | **404** |
| Token: quá `expires_at` hoặc đã `Cancelled` | **410**; row ⇒ `Expired` khi quá hạn |
| Accept: email tài khoản khác | **403**; invitation **giữ** `Pending` |
| Accept khi đã là member | **200** `alreadyMember=true`; role **không** bị ghi đè |
| Accept 2 request **đồng thời** | Không tạo 2 row (PK composite); bên thua nhận `ConflictException` **409** hoặc `alreadyMember=true`; invitation kết thúc `Accepted` |
| Accept khi chưa đăng nhập | FE lưu token → OAuth → accept lại; OAuth bằng email khác ⇒ **403** |
| Huỷ lời mời của workspace khác | **404** |
| Huỷ lời mời `Accepted` | **409**; huỷ `Cancelled` lần 2 ⇒ **204** (idempotent) |
| Đổi role/kick: không phải Admin | **403** |
| Đổi role/kick chính mình · owner · AI Agent · Admin cuối cùng | **400** |
| Rời workspace khi là owner | **400** + lý do + link tới chuyển quyền sở hữu |
| `PUT /api/users/me`: displayName rỗng / > 120 | **400** |
| `PUT /api/users/me`: avatarUrl `javascript:`/`data:`/`file:`/`//x` | **400** (không silently `null`); `""` ⇒ `null` (**200**) |
| Quick email: vượt cap recipients / recipient lạ / AI Agent | **400**, không gửi gì |
| Quick email: một địa chỉ lỗi | **200** partial + `errors[]` |
| Quick email: vượt `MaxEmailsPerHourPerWorkspace` | **429** |
| Assign task cho AI Agent / chính mình | **0** notification |
| `PUT /tasks` chỉ sửa title/priority (assignee không đổi) | **0** notification (không spam) |
| Comment khi mình là assignee / task không assignee / assignee là agent | **0** notification |
| Kick người đang được gán task | Cho phép; `tasks.assignee_id` **giữ nguyên** (FK Restrict) — hành vi mong muốn |
| Notification type lạ / `payload = null` | UI hiện nhãn trần, không crash |
| `workspaceId` vắng ở `NotificationDrawer` | Không crash; ẩn nút điều hướng |
| 401/403/404/409/410/429 ở mọi trang mới | `message.error` / `<Result>` tiếng Việt — **không** màn hình trắng |

---

## 12. DoD & bằng chứng

### 12.1 Con số mục tiêu

| Chỉ số | Baseline | Kỳ vọng sau Giai đoạn 11 |
|---|---|---|
| Backend `dotnet test` (PostgreSQL 18 thật) | **226** (0 fail / 0 skip) ⚠️ *chưa đo được — §1.2* | ~**286** (chốt bằng số thật) |
| Backend `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 / 0 | ✅ **0 / 0** (đang giữ) |
| Frontend `npm test` | **279** (47 file) | ~**341** (~62 file) |
| Frontend `npm run lint` · `npx tsc -b` · `npm run build` | 0/0 · exit 0 · OK | giữ nguyên |
| `dotnet ef migrations list` | **8** | ✅ **9** (`Phase11MemberProfile`) |
| `has-pending-model-changes` | không có | ✅ **không có** |
| Bảng DB mới | — | ✅ `workspace_invitations`, `email_messages` (**đúng 2**) |

### 12.2 Bảng bằng chứng

| # | Bằng chứng | Ngưỡng | Trạng thái |
|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 warning / 0 error | ✅ sau mỗi bước |
| 2 | `dotnet ef migrations list` + `has-pending-model-changes` | 9 / sạch | ✅ |
| 3 | `dotnet test --filter EmailTemplateTests` | 14 passed / 0 skipped | ✅ |
| 4 | `dotnet test` (DB thật) | `Skipped: 0` | ⬜ **cần máy có PostgreSQL** |
| 5 | `\d workspace_invitations` trong `psql` | 2 CHECK, 2 UQ (1 partial), IX đúng | ⬜ |
| 6 | `npm run lint` / `tsc -b` / `npm test` / `npm run build` | 0-0 / exit 0 / ≥ 341 / OK | ⬜ |
| 7 | **1 lượt thao tác thật, có ảnh** | mời → **mở hộp thư thật** → click link → accept → thấy mình trong bảng member; gửi email nhanh; đổi avatar/tên; rời workspace; **gán task ⇒ chuông header nổi số ⇒ drawer hiện thông báo** | ⬜ |
| 8 | Row `email_messages` `Status=Sent` + `provider_message_id ≠ null` | chứng minh đi qua Resend thật (không phải `NullEmailSender`) | ⬜ **cần key Resend** |
| 9 | 2 workflow CI xanh | `ci-backend` (total = số thật) + `ci-web` (≥ 341) | ⬜ |

### 12.3 Điều kiện "xong"
Cả **5 ô** (A–E) + **4 hạng mục phát sinh** (F, G, H, I) đóng bằng bằng chứng §12.2; **đúng 1 migration mới** (9 tổng) và `has-pending-model-changes` sạch; 2 cổng CI đã nâng baseline và chạy xanh; `03-roadmap.md` + `README.md` + `04-database-design.md` ghi **số thật**; báo cáo tại `Project-Documents/report/phase-11-member-profile-management-test-report.md`.

---

## 13. ⛔ KHÔNG được làm

- ❌ **Không** sửa/xoá cột hay bảng **đang tồn tại**; chỉ **thêm 2 bảng mới**.
- ❌ **Không** đổi shape hợp đồng đã verify: `TaskResponse`, `BoardResponse`, `ColumnResponse`, `NotificationResponse`, `HubConnectionStatus`, payload SignalR. `WorkspaceMemberResponse` chỉ được **append cuối**.
- ❌ **Không** thêm type mới vào `NotificationTypes.All` (whitelist Observer) — dùng `MemberNotificationTypes` (D10).
- ❌ **Không** đổi tên event SignalR, không đổi kiến trúc 1-hub-1-group.
- ❌ **Không** sửa `shared/api/httpClient.ts`, không sửa `features/reporting/utils/reportDownload.ts`.
- ❌ **Không** thêm package NuGet (kể cả `Resend`) hay thư viện npm.
- ❌ **Không** di chuyển `NotificationService`/`NotificationEndpoints`/`NotificationDrawer` sang module/thư mục mới (D11 — nợ kỹ thuật có chủ ý).
- ❌ **Không** dùng `dangerouslySetInnerHTML`; nội dung email **phải** HTML-escape mọi giá trị nội suy.
- ❌ **Không** ghi token mời thô vào `email_messages`, `activity_logs.payload`, log, hay response API.
- ❌ **Không** cho người nhận lời mời tự chọn role vượt `invited_role` (D4).
- ❌ **Không** để `POST /api/invitations/accept` là `GET`, và **không** bỏ CSRF filter trên nó.
- ❌ **Không** dùng `Enum.TryParse` trần cho role (bẫy BUG-1 Phase 10: `"1"` ⇒ `Medium`).
- ❌ **Không** quên 2 bảng mới trong `TestScenario.ResetDatabaseAsync` (**I** ⇒ suite flaky).
- ❌ **Không** mở rộng phạm vi sang: Dashboard/Tìm kiếm/**@mention** (Giai đoạn 12); Mobile (13); hard delete; đa assignee; SSO/SCIM; template email tuỳ biến.
- ❌ **Không** viết lại test cũ đang xanh (chỉ **thêm**; nếu buộc sửa ⇒ ghi rõ lý do + tên test vào báo cáo).

---

## 14. Rủi ro & giả định

| # | Mục | Xử lý |
|---|---|---|
| **R1** | **Không có key Resend thật** ⇒ chưa chứng minh "gửi tới inbox thật" | `Email:ApiKey` trống ⇒ `NullEmailSender` + row `email_messages` vẫn được ghi ⇒ mọi assertion chạy offline. Bằng chứng §12.2 #7/#8 **cần** 1 lượt gửi thật |
| **R2** | **CI sẽ gửi email thật** nếu key rò từ secrets | ✅ Đã đặt `Email:ApiKey = " "` trong `TeamNexusApiFactory` (trick "một khoảng trắng"). ⬜ Còn phải thêm biến môi trường tương ứng vào `ci-backend.yml` (§10 #1) |
| **R3** | Thêm 2 bảng mà quên `ResetDatabaseAsync` ⇒ rò dữ liệu giữa test | ✅ Đã thêm (hạng mục **I**) |
| **R4** | Accept đua nhau ⇒ 2 membership hoặc `UpdateException` lộ ra ngoài | PK composite là chốt chặn vật lý; `AcceptAsync` bắt `DbUpdateException` ⇒ trả `alreadyMember=true` và **vẫn** set invitation `Accepted` (không kẹt `Pending`) |
| **R5** | Trần `MaxEmailsPerHourPerWorkspace` chặn nhầm demo | Là **quyết định có chủ ý**, cấu hình được; `429` kèm số phút chờ |
| **R6** | Refactor 5 header (hạng mục G) làm đỏ test cũ | Chỉ **thêm** `data-testid`; giữ text/role hiện có; chạy `npm test` **trước và sau** từng trang |
| **R7** | Kick member để lại task `assignee_id` trỏ user ngoài workspace | **Hành vi mong muốn** (FK Restrict): không mất dữ liệu. Ghi vào `04` §7 |
| **R8** | `GetMembersAsync` thêm 3 field ⇒ phình payload assignee dropdown | 3 field nhỏ; chỉ tách `?view=full` **khi có bằng chứng**, không làm trước |
| **R9** | Route mới đụng route cũ | Đã soát: `/api/users/me*`, `/api/invitations/*`, `/api/workspaces/{id}/members/*`, `/api/workspaces/{id}/quick-email` **không** trùng route nào đang có; literal segment nên không có `/{id:guid}` conflict |
| **R10** | Số test backend thật **chưa đo được** (không có DB, Docker daemon không chạy) | §1.2 bắt buộc đo lại bằng DB thật; lệch 226 ⇒ **số đo thắng tài liệu** |
| **R11** | `email_messages.body_preview` vô tình chứa token | Preview của invitation **chỉ** chứa tiêu đề + tên workspace; có test assert |
| **R12** | `WebUtility.HtmlEncode` làm hỏng tiếng Việt trong email | Dùng `HtmlEncoder.Create(UnicodeRanges.All)` (P5) — escape đủ `< > & " '`, giữ dấu |

**Giả định:**
- Baseline **226 backend / 279 frontend (47 file) / 8 migration**; **số đo thắng tài liệu**.
- PostgreSQL 18 sẵn sàng cho `dotnet test`; không có DB ⇒ nhóm DB skip và **không** được tính là đạt DoD.
- Giai đoạn 9 đã merge ⇒ Identity role chỉ còn `Admin`/`User`; mọi cổng quyền của giai đoạn này đọc `workspace_members.role`.
- `Program.cs` vẫn gọi `AddBoardModule()` **trước** `AddAiModule()` (điều kiện của D9).
- Frontend: React 19 + TypeScript 6 + AntD 6 + Vite 8 + Vitest 5; toàn bộ nhãn UI **tiếng Việt có dấu**.
- Có **một** tài khoản Resend + API key cho bằng chứng gửi thật; tên miền gửi dùng `onboarding@resend.dev` giai đoạn đầu.
- Endpoint mới **không** ảnh hưởng mobile (Flutter — Giai đoạn 13) ngoài `WorkspaceMemberResponse` (append-only).
