# Giai đoạn 3 – AI Smart Setup

> **Mục tiêu:** Tích hợp DeepSeek API qua interface `IAiProvider`, xây luồng: trưởng nhóm nhập mô tả → AI phân rã sub-tasks/nhãn/đề xuất người phụ trách → hiển thị để con người chỉnh sửa & xác nhận. **Không ghi thẳng vào DB** — chờ xác nhận (bàn giao cho Accountability Layer ở Giai đoạn 4).
>
> **Công nghệ:** ASP.NET Core (Modular Monolith) · DeepSeek API (OpenAI-compatible, gọi qua `HttpClient` + `IHttpClientFactory`) · System.Text.Json · React + TypeScript + Vite + Ant Design · Vitest.

---

## 0. Tiền đề & quyết định kiến trúc (đã chốt)

- Tạo module mới `src/Modules/Ai/TeamNexus.Modules.Ai` (đúng phân module `Auth`/`Board`/`Ai` ở `02-tech-stack-decisions.md` §1).
- Module Ai tham chiếu 3 project:
  - `TeamNexus.Shared` — reuse `AntiforgeryValidationEndpointFilter`.
  - `TeamNexus.Persistence` — dùng chung `TeamNexusDbContext` + entity.
  - `TeamNexus.Modules.Board` — reuse `IWorkspaceAccess`, `BoardModuleException` + các exception con, `DomainExceptionFilter`.
- **Reuse thay vì sao chép:** lỗi nghiệp vụ dùng lại exception của Board. Riêng lỗi gọi provider/parse JSON định nghĩa `AiProviderException : BoardModuleException` (status 502), được `DomainExceptionFilter` của Board bắt sẵn. **Không sửa module Board** ngoại trừ 1 endpoint bổ sung ở §3.1.
- **Không ghi DB ở Giai đoạn 3:** endpoint chỉ `generate` + trả proposal; nút "Xác nhận" ở UI là trạng thái frontend (không persist). Endpoint `confirm/apply` đi qua `AiActionService` làm ở Giai đoạn 4 (kèm contract bàn giao §7).
- **Chạy được khi chưa có API key:** nếu `DeepSeek:ApiKey` rỗng → đăng ký `FakeAiProvider` (trả proposal mẫu, log cảnh báo) để dev/test offline; có key → `DeepSeekAiProvider` thật.

---

## 1. Backend – Cấu hình & đăng ký module

### 1.1 Options & config

- [x] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Options/DeepSeekOptions.cs` (theo pattern `JwtOptions`):
  - `SectionName = "DeepSeek"`.
  - Thuộc tính: `ApiKey` (string, mặc định `""`), `BaseUrl` (mặc định `https://api.deepseek.com`), `Model` (mặc định `deepseek-chat`), `TimeoutSeconds` (mặc định 60), `MaxTokens` (mặc định 4096), `Temperature` (mặc định 0.2), `MaxTaskCount` (mặc định 20).
- [x] Thêm section vào `src/TeamNexus.Api/appsettings.json`:
  ```json
  "DeepSeek": {
    "ApiKey": "",
    "BaseUrl": "https://api.deepseek.com",
    "Model": "deepseek-chat",
    "TimeoutSeconds": 60,
    "MaxTokens": 4096,
    "Temperature": 0.2,
    "MaxTaskCount": 20
  }
  ```
- [x] Ghi chú đặt key qua User Secrets: `dotnet user-secrets set --project src/TeamNexus.Api "DeepSeek:ApiKey" "sk-..."`.
- [x] Thêm `ProjectReference` Ai vào `src/TeamNexus.Api/TeamNexus.Api.csproj`; thêm project Ai vào `TeamNexus.sln`.

### 1.2 `AiModule.cs`

- [x] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/AiModule.cs`:
  - `AddAiModule(IConfiguration)`:
    - `AddOptions<DeepSeekOptions>().Bind(configuration.GetSection("DeepSeek"))` (không `ValidateOnStart` cứng — cho phép dev không key).
    - Named `HttpClient` `"DeepSeek"` (`AiModule.HttpClientName`) với timeout từ `TimeoutSeconds` — dùng `IHttpClientFactory` thay vì typed client, để §2 đăng ký `IAiProvider` theo cấu hình (Fake vs DeepSeek) mà không lệch lifetime.
    - Đăng ký `DomainExceptionFilter` + `AntiforgeryValidationEndpointFilter` (transient).
    - Log lúc khởi động: Warning + `FakeAiProvider` khi thiếu key, Information + `DeepSeekAiProvider` khi có key (**không bao giờ log giá trị key**).
  - `MapAiModuleEndpoints()` → hiện là no-op (extension point); §3/§4 chỉ cần thêm `endpoints.MapSmartSetupEndpoints();` vào đây.
- [x] Đăng ký ở `src/TeamNexus.Api/Program.cs`:
  - `builder.Services.AddAiModule(builder.Configuration);`
  - `app.MapAiModuleEndpoints();` (sau `UseAuthentication`/`UseAuthorization`, cùng chỗ với Board).

> **Ghi chú phạm vi §1:** phần "đăng ký `IAiProvider` → Fake/DeepSeek" và `AddScoped<ISmartSetupService, SmartSetupService>()` trong checklist gốc §1.2 được **dời sang §2 và §4** (nơi các type đó được hiện thực), thay vì tạo stub `NotImplementedException` chỉ để đăng ký DI. `MapAiModuleEndpoints()` đã có sẵn nên `Program.cs` không phải sửa lại khi làm §3/§4. Verify §1: `dotnet build` 0 warning/0 error; boot API ở cả hai trường hợp (có/không `DeepSeek:ApiKey`) đều OK, `GET /api/health` → 200; không có migration/entity mới.

---

## 2. Backend – `IAiProvider` & DeepSeek

### 2.1 Contract (`Services/AiProvider.cs`)

- [x] Định nghĩa:
  ```csharp
  public interface IAiProvider
  {
      Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);
  }
  public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt, double Temperature, int MaxTokens, bool JsonMode);
  public sealed record AiCompletionResult(string Content, int? PromptTokens, int? CompletionTokens);
  ```
- [x] Interface đủ tổng quát để Giai đoạn 5 (Observer) tái sử dụng (chỉ đổi prompt).

### 2.2 `DeepSeekAiProvider` (`Services/DeepSeekAiProvider.cs`)

- [x] Gọi `POST {BaseUrl}/chat/completions` qua `HttpClient` (từ `IHttpClientFactory`), header `Authorization: Bearer {ApiKey}`.
- [x] Body OpenAI-compatible: `model`, `messages = [system, user]`, `temperature`, `max_tokens`; nếu `JsonMode` → `response_format = { "type": "json_object" }`.
- [x] Deserialize response `choices[0].message.content` (+ `usage` tokens) → `AiCompletionResult`.
- [x] Map mọi lỗi (HTTP ≠ 2xx, timeout, content rỗng) → `AiProviderException` (502). Không log API key; chỉ log ở mức Warning.
  - Bổ sung: validate input (prompt rỗng, `MaxTokens ≤ 0`, `Temperature` ngoài `0–2`) → 502 **trước khi** gọi mạng; caller cancel → rethrow `OperationCanceledException` (không bọc thành 502); thiếu `usage` không phải lỗi.

### 2.3 `FakeAiProvider` (`Services/FakeAiProvider.cs`)

- [x] Trả proposal JSON mẫu cố định (vài sub-task có label + assignee gợi ý) đúng schema §4.1 — để dev/test offline, không tốn chi phí.
  - 3 task mẫu phủ sẵn các nhánh §4.3: priority hợp lệ + `null`, task 2 label, assignee có tên + `null`; cắt theo `MaxTaskCount`.

### 2.4 Exception riêng (`Services/AiExceptions.cs`)

- [x] `AiProviderException : BoardModuleException` (status 502) — `DomainExceptionFilter` của Board map sẵn thành `{ error }`.

> **Ghi chú phạm vi §2:** toàn bộ §2 nằm trong `Services/` (khớp path spec); 3 thư mục scaffold rỗng `Abstractions/`, `Configuration/`, `Providers/` đã được xoá. Việc **retry 1 lần khi JSON parse lỗi** nằm ở §4.3 bước 8 (`SmartSetupService`), không làm ở tầng provider. Verify §2 bằng harness tạm với `HttpMessageHandler` stub (không cần key thật/mạng): request shape, parse `usage`, 502 cho HTTP 429/content rỗng/JSON hỏng/timeout, cancel không bị bọc, `FakeAiProvider` hợp lệ JSON + cắt `MaxTaskCount`, và chọn provider qua `AddAiModule` (không key → Fake, có key → DeepSeek).

---

## 3. Backend – Dữ liệu hỗ trợ & Endpoints

### 3.1 (Board, bổ sung nhỏ) Liệt kê thành viên workspace

- [x] Thêm `GET /api/workspaces/{workspaceId:guid}/members` (Member+): trả `[{ userId, displayName, role, avatarUrl? }]`.
- [x] Hiện thực trong module Board: `Endpoints/MembersEndpoints.cs` + `Services/IWorkspaceMemberService` / `WorkspaceMemberService` (query `workspace_members` + `users`), đăng ký ở `AddBoardModule`. Lý do: cần danh sách thành viên cho dropdown chọn người phụ trách khi chỉnh sửa đề xuất (tái dùng cho Giai đoạn 4/5).
  - `WorkspaceMemberService` chỉ đọc; dùng global query filter sẵn có (workspace soft-deleted bị loại) và bỏ membership trỏ tới user đã xoá cứng. DTO `WorkspaceMemberResponse` ở `DTOs/MemberDtos.cs`.

### 3.2 `SmartSetupEndpoints.cs` (Ai)

- [x] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/SmartSetupEndpoints.cs`:
  - `POST /api/boards/{boardId:guid}/smart-setup` (body `SmartSetupRequest`).
  - `RequireAuthorization` + `AntiforgeryValidationEndpointFilter` + `DomainExceptionFilter`.
  - Gọi `ISmartSetupService.GenerateAsync(...)` → `200 { SmartSetupProposal }`.
- [x] Không có endpoint ghi DB ở Giai đoạn 3.

> **Ghi chú phạm vi §3:** `SmartSetupRequest` + các response record (`SmartSetupProposal`, `SmartSetupTaskProposal`,
> `SmartSetupLabelSuggestion`, `SmartSetupAssigneeSuggestion`) được tạo ngay ở `DTOs/SmartSetupDtos.cs` vì endpoint
> trả chúng (spec ghi ở §4.2). `ISmartSetupService` có interface đầy đủ và `SmartSetupService` đăng ký scoped,
> nhưng **thân `GenerateAsync` còn là stub §3.2** → gọi endpoint với CSRF hợp lệ trả `500` cho tới khi §4.3 hoàn tất;
> route/filter/auth/DI đã xong và không phải sửa lại. Module Ai đọc claim `NameIdentifier` trực tiếp
> (helper `CurrentUser` của Board là `internal`, không dùng chéo module được).

---

## 4. Backend – Smart Setup Service & JSON Schema

### 4.1 Contract đầu ra của AI (`Contracts/SmartSetupAiModels.cs`)

- [x] Định nghĩa raw AI output (khớp chặt system prompt):
  ```csharp
  public sealed class AiSmartSetupOutput
  {
      public string? Summary { get; set; }
      public List<AiSmartSetupTask> Tasks { get; set; } = [];
  }
  public sealed class AiSmartSetupTask
  {
      public string Title { get; set; } = string.Empty;
      public string? Description { get; set; }
      public string? Priority { get; set; }          // Low|Medium|High|Urgent|null
      public List<string> Labels { get; set; } = [];
      public string? SuggestedAssignee { get; set; } // tên thành viên gợi ý
  }
  ```

### 4.2 DTO API (`DTOs/SmartSetupDtos.cs`)

- [x] Định nghĩa:
  ```csharp
  public sealed record SmartSetupRequest(string Description);
  public sealed record SmartSetupAssigneeSuggestion(Guid? UserId, string? DisplayName, bool Matched);
  public sealed record SmartSetupLabelSuggestion(Guid? LabelId, string Name, bool Exists);
  public sealed record SmartSetupTaskProposal(
      string Title, string? Description, string? Priority,
      IReadOnlyList<SmartSetupLabelSuggestion> Labels,
      SmartSetupAssigneeSuggestion? Assignee);
  public sealed record SmartSetupProposal(string? Summary, IReadOnlyList<SmartSetupTaskProposal> Tasks);
  ```
  (đã tạo sớm ở §3.2 vì endpoint cần kiểu trả về)

### 4.3 `ISmartSetupService` / `SmartSetupService`

- [x] `GenerateAsync(boardId, request, userId, ct)`:
  1. Load board → lấy `workspaceId` (404 nếu không thấy).
  2. `IWorkspaceAccess.RequireManagerAsync(workspaceId, userId)` → 403 cho Member.
  3. Validate `Description`: không rỗng, ≤ 4000 ký tự → 400.
  4. Load context: thành viên workspace (id + displayName), nhãn hiện có (name), tên các cột của board.
  5. Build **system prompt** (ép JSON: chỉ trả 1 object `{ summary, tasks[] }`; mỗi task có `title` ≤ 200, `description` nullable, `priority` ∈ `Low|Medium|High|Urgent` hoặc null, `labels` ≤ 5 nhãn ngắn, `suggestedAssignee` là tên thành viên hoặc null; không bịa thành viên ngoài danh sách; giữ ngôn ngữ đầu vào).
  6. Build **user prompt** = context + mô tả của trưởng nhóm.
  7. Gọi `_aiProvider.CompleteAsync(..., JsonMode: true, Temperature 0.2, MaxTokens)`.
  8. `JsonSerializer.Deserialize<AiSmartSetupOutput>` (UTF-8, case-insensitive); fail → `AiProviderException` (502). Tùy chọn retry 1 lần khi parse lỗi.
  9. Normalize + validate từng task: trim title (1–200), cắt mô tả (≤2000), ưu tiên sai enum → null, labels dedupe/trim/≤5 (name ≤80), giới hạn `MaxTaskCount`. `Tasks` rỗng → 400.
  10. Resolve assignee: khớp `displayName` theo thành viên (exact case-insensitive trước, sau đó contains) → `AssigneeSuggestion` (`UserId` + `Matched=true` khi khớp, ngược lại giữ tên + `Matched=false`).
  11. Resolve label: khớp nhãn hiện có theo tên (case-insensitive) → `LabelSuggestion` (`LabelId` + `Exists=true`) hoặc nhãn mới (`Exists=false`).
  12. Trả `SmartSetupProposal`. **Không `SaveChanges`.**

### 4.4 Exception riêng

- [x] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Services/AiExceptions.cs`: `AiProviderException : BoardModuleException` (status 502) — dùng cho lỗi gọi API / JSON sai schema.
  (đã tạo ở §2)

> **Ghi chú hiện thực §4 (khác biệt nhỏ so với spec, có chủ ý):**
> - Prompt đặt ở `Services/SmartSetupPrompts.cs` (không đặt trong `DTOs/SmartSetupDtos.cs`) để không trùng tên
>   với DTO đã có từ §3.2 và để tách phần "chữ nghĩa" dễ review.
> - **Retry 1 lần** khi parse JSON lỗi: gọi lại provider với prompt chặt hơn (kèm lỗi parse); hỏng lần 2 → 502.
> - **3 quyết định biên** đã chốt: (a) AI để `suggestedAssignee = null` **và** workspace chỉ có **1 thành viên**
>   → gán luôn thành viên đó; có ≥ 2 thành viên → giữ `null`. (b) nhãn > 80 ký tự → **bỏ** (không cắt).
>   (c) `title` rỗng hoặc > 200 → **bỏ cả task**; nếu bỏ hết → 400.
> - `BuildProposal`/`NormalizeTasks`/`NormalizePriority`/`ResolveAssignee`/`ResolveLabels` để `public static`
>   nhằm verify được logic thuần mà không cần dựng HTTP/AI.
> - Verify §4: API thật + PostgreSQL thật, 2 nhánh provider. DeepSeek thật → 200 với 7 task (~3.1s);
>   `FakeAiProvider` → 200 với 3 task (83ms, `priority=[High,Medium,null]`, matched=2/unmatched=1).
>   Không ghi DB (tasks 10→10, labels 0→0, task_labels 0→0, members 2→2). Chi tiết xem
>   `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` mục "Verify §4".

---

## 5. Frontend – UI Smart Setup

> **Ghi chú bàn giao (backend §1–§4 đã hoàn thiện):**
> Phần này (§5) và phần kiểm thử (§6) do **antigravity** đảm nhiệm. Backend đã xong và verify
> end-to-end — đọc hết mục này trước khi code.
>
> **1) Endpoint duy nhất cần dùng**
> - `POST /api/boards/{boardId}/smart-setup` — body `{ description: string }` (1–4000 ký tự).
> - Dùng `frontend/src/shared/api/httpClient.ts` (base `/api`, `withCredentials`). Instance này **tự**
>   gắn `X-XSRF-TOKEN` cho POST/PUT/DELETE/PATCH, tự lấy lại token khi 403 CSRF, tự refresh khi 401.
>   ⇒ **Không** tự set header `X-XSRF-TOKEN`, không tự gọi `/auth/antiforgery`.
> - Response **camelCase**. Ví dụ thật (rút gọn) từ lần verify với DeepSeek:
> ```json
> {
>   "summary": "Phân rã tính năng bình luận trên thẻ kanban thành các sub-task …",
>   "tasks": [
>     {
>       "title": "Thiết kế schema bình luận cho task",
>       "description": "Bảng comments, FK tới tasks và users, index theo task_id.",
>       "priority": "High",
>       "labels": [
>         { "labelId": null, "name": "backend", "exists": false },
>         { "labelId": null, "name": "database", "exists": false }
>       ],
>       "assignee": { "userId": "0f0a…-…", "displayName": "Thinh-TT", "matched": true }
>     }
>   ]
> }
> ```
> - Kiểu TS cần mirror (viết tay theo `src/Modules/Ai/DTOs/SmartSetupDtos.cs`):
>   ```ts
>   type SmartSetupRequest = { description: string }
>   type SmartSetupLabelSuggestion = { labelId: string | null; name: string; exists: boolean }
>   type SmartSetupAssigneeSuggestion = { userId: string | null; displayName: string | null; matched: boolean }
>   type SmartSetupTaskProposal = {
>     title: string; description: string | null; priority: 'Low'|'Medium'|'High'|'Urgent'|null
>     labels: SmartSetupLabelSuggestion[]; assignee: SmartSetupAssigneeSuggestion | null
>   }
>   type SmartSetupProposal = { summary: string | null; tasks: SmartSetupTaskProposal[] }
>   ```
> - **Proposal không có `id`** (chưa lưu DB) → React key dùng index hoặc `crypto.randomUUID()` tạm.
>
> **2) Endpoint bổ trợ cho dropdown khi chỉnh sửa đề xuất**
> - Người phụ trách: `GET /api/workspaces/{workspaceId}/members` → `[{ userId, displayName, role, avatarUrl }]`
>   (Member+ đọc được). `BoardView` đã có sẵn `workspaceId` + `boardId` từ props.
> - Nhãn hiện có: `GET /api/workspaces/{workspaceId}/labels` → `[{ id, workspaceId, name, color, createdAt }]`.
>   `useBoard` đã expose `workspaceLabels` — có thể tái dùng thay vì gọi lại.
>
> **3) Mã lỗi & thông báo UI gợi ý** (body lỗi luôn là `{ "error": "…" }`)
> | Status | Khi nào | Gợi ý UI |
> |---|---|---|
> | 400 | mô tả rỗng/> 4000, hoặc AI không trả sub-task hợp lệ nào | `Alert` lỗi; với 400 "không có sub-task" thì mời nhập mô tả chi tiết hơn + nút "Thử lại" |
> | 401 | hết phiên | `httpClient` tự refresh; nếu vẫn lỗi → điều hướng login |
> | 403 | user là **Member** (không phải Manager/Admin) | `Alert` "Bạn cần quyền Manager/Admin trong workspace này" — xem mục 5 bên dưới |
> | 404 | board không tồn tại/không thấy | `Alert` + đóng modal |
> | 502 | DeepSeek lỗi/timeout/JSON hỏng sau 2 lần thử | `Alert` "AI tạm thời không phản hồi, thử lại sau" + nút "Thử lại" (giữ nguyên mô tả đã nhập) |
>
> **4) Giới hạn đã biết — đừng code sai kỳ vọng**
> - **Chưa có endpoint `confirm/apply`.** Giai đoạn 4 mới có `AiActionService` + bảng `ai_action_logs`.
>   Nút "Xác nhận" ở §5 chỉ là **trạng thái frontend** (hiện `Result.success` "Đề xuất đã xác nhận — sẽ
>   được áp dụng qua Accountability Layer ở Giai đoạn 4"), **không gọi API ghi nào**.
> - Mọi thay đổi người dùng sửa trong modal chỉ nằm ở state React, **không** persist.
> - `assignee.matched = false` (AI nêu tên không có trong workspace): hiển thị cảnh báo và **bắt buộc
>   người dùng chọn lại** từ dropdown members (hoặc chọn "Không gán").
> - `assignee = null`: xảy ra khi workspace có ≥ 2 thành viên mà AI không gợi ý ai → để trống cho user chọn.
> - `label.exists = false`: UI nên cho "tạo nhãn mới" (gọi `POST /api/workspaces/{wsId}/labels` — cần
>   quyền Manager/Admin) hoặc bỏ nhãn đó; **không** tự tạo nhãn ngầm.
> - Mô tả bị cap 4000 ký tự ở backend → đặt `maxLength` cho `TextArea` để tránh 400.
> - `priority = null` là hợp lệ (AI trả enum sai đã bị chuẩn hoá) → `Select` phải cho phép "Không ưu tiên".
>
> **5) Phạm vi chi phí & thời gian gọi AI**
> - 1 lần "Tạo đề xuất" = 1 call DeepSeek (~2–4s trong verify). Retry chỉ xảy ra khi AI trả JSON hỏng.
>   ⇒ Disable nút trong lúc `status === 'generating'`, không cho double-submit.
> - Nếu `DeepSeek:ApiKey` chưa cấu hình, backend trả proposal mẫu (`FakeAiProvider`) ngay (~100ms) —
>   UI không cần biết, nhưng đừng ngạc nhiên khi nội dung là dữ liệu mẫu.
>
> **6) Ghi chú cho §6 (test frontend)**
> - Test ở tầng frontend, **không cần** dựng backend: mock `httpClient` (axios instance) như các test
>   `boardApi.test.ts` hiện có. Assert `smartSetupApi.generateSmartSetup` gọi đúng URL
>   `/boards/{boardId}/smart-setup` với body `{ description }`.
> - `useSmartSetup`: kiểm tra state machine `idle → generating → ready → error` (mock resolve/reject).
> - `SmartSetupModal`: hiển thị proposal, sửa được title/priority/assignee/label, hiện `Alert` khi lỗi,
>   và nút "Xác nhận" **không** gọi API nào.
> - Backend đã được verify sẵn (API thật + DB thật, cả DeepSeek thật lẫn Fake). **Không** cần test lại
>   backend ở §6; nếu muốn thử tay thì xem mục "Verify §4" trong
>   `src/Modules/Ai/TeamNexus.Modules.Ai/README.md`.
>
> **7) Tham chiếu**
> - `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` — pipeline endpoint, 12 bước service, ngưỡng
>   normalize, bảng mã lỗi, kết quả verify.
> - `src/Modules/Board/TeamNexus.Modules.Board/README.md` — endpoint members/labels + quyền.

### 5.1 Feature `src/features/ai/`

- [x] `types/smartSetup.types.ts` — mirror DTO §4.2 (camelCase).
- [x] `services/smartSetupApi.ts` — `generateSmartSetup(boardId, { description })` qua `httpClient` (base `/api`).
- [x] `hooks/useSmartSetup.ts` — state `{ status: idle|generating|ready|error, proposal, error }`, action `generate(description)`, cho phép sửa danh sách task local trước khi xác nhận.
- [x] `components/SmartSetupModal.tsx` — Modal Ant Design:
  - Bước 1: `Input.TextArea` mô tả + nút "Tạo đề xuất" (loading).
  - Bước 2: hiển thị danh sách `ProposedTaskItem` **có thể chỉnh sửa** (title, description, priority Select, assignee Select từ `/workspaces/{wsId}/members`, label tags từ `/workspaces/{wsId}/labels` + nhãn mới).
  - Bước 3: nút "Xác nhận" → Giai đoạn 3 **không ghi DB**; hiển thị `Result.success` "Đề xuất đã xác nhận — sẽ được áp dụng qua Accountability Layer ở Giai đoạn 4".
  - Xử lý trạng thái lỗi (403, 502, empty) bằng `Alert`.
- [x] `components/ProposedTaskItem.tsx` — dòng chỉnh sửa 1 task đề xuất.

### 5.2 Tích hợp vào Board

- [x] Thêm nút "AI Smart Setup" (icon robot/spark) vào top bar của `BoardView.tsx`, mở `SmartSetupModal` truyền `boardId`, `workspaceId`, `columns`, `workspaceLabels`.
- [x] Visibility: hiển thị nút luôn, backend enforce Manager/Admin (403 hiện thông báo rõ). Nâng cao (tuỳ chọn): ẩn nút cho Member bằng cách đọc `role` từ `GET /api/workspaces` hiện có.

---

## 6. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–2)

### 6.1 Verify backend (Scalar + API probe)

- [x] `POST /smart-setup` khi chưa đăng nhập → 401; Member → 403; mô tả rỗng → 400; board không tồn tại → 404.
- [x] Dùng `FakeAiProvider` (không key): trả proposal đúng schema, resolve assignee/label đúng (matched/unmatched).
- [x] Bật key thật (nếu có): gọi DeepSeek trả JSON đúng schema, validate + normalize (priority sai → null, label dupe → gọn).
- [x] Verify **không ghi DB**: số row `tasks`/`labels`/`ai_action_logs` không đổi sau generate.
- [x] `GET /api/workspaces/{wsId}/members` trả đúng danh sách, Member+ đọc được.

### 6.2 Frontend (Vitest + Testing Library)

- [x] Test `smartSetupApi` (gọi đúng URL/body).
- [x] Test `useSmartSetup` (idle → generating → ready → error).
- [x] Test `SmartSetupModal` (hiển thị proposal, sửa task, trạng thái lỗi, nút xác nhận không gọi API ghi).
- [x] `oxlint` 0 warning/error, `tsc -b` + `vite build` pass.

### 6.3 Điều kiện hoàn thành Giai đoạn 3 (map `03-roadmap.md`)

- [x] `IAiProvider` triển khai xong cho DeepSeek (+ `FakeAiProvider` dev).
- [x] Nhập mô tả → AI trả danh sách sub-tasks có nhãn + đề xuất người phụ trách; JSON đúng schema, có validate.
- [x] UI hiển thị kết quả đề xuất, cho phép chỉnh sửa trước khi xác nhận.
- [x] Không ghi thẳng DB — chờ xác nhận (bàn giao Accountability Layer Giai đoạn 4).

---

## 7. Edge cases, failure modes & bàn giao Giai đoạn 4

**Edge cases:**
- Mô tả rỗng / > 4000 ký tự → 400.
- Không phải Manager/Admin → 403; board không tồn tại/không thấy → 404.
- DeepSeek timeout/lỗi HTTP/JSON sai → 502 (`AiProviderException`), frontend hiện lỗi, không partial-write (luồng không ghi DB nên an toàn).
- AI trả `tasks` rỗng → 400 kèm gợi ý nhập lại mô tả.
- Priority sai enum → null; labels rỗng/trùng/quá dài → lọc/chuẩn hóa.
- Không có thành viên workspace → assignee `null`/`Matched=false`, UI hiện "chưa có thành viên".
- Không có nhãn → toàn bộ là nhãn mới (`Exists=false`).
- Chưa cấu hình key → `FakeAiProvider` (log Warning), không crash khi dev.

**Kiểm soát token/chi phí (theo `02` §2.3/§5):** cap độ dài mô tả, `max_tokens=4096`, `temperature=0.2`, giới hạn `MaxTaskCount`, chỉ log prompt/response ở Debug.

**Contract bàn giao Giai đoạn 4 (Accountability Layer):**
- Đối tượng confirmed = `SmartSetupProposal` (sau khi người dùng sửa) → Giai đoạn 4 thêm endpoint `POST .../smart-setup/confirm` đi qua `AiActionService` duy nhất: tạo `AiActionLog` (`action=CreateSubtasks`, `basis` = tóm tắt mô tả, `after_snapshot` = proposal, `status=Pending`) → Approve mới áp dụng thật (tạo `tasks`/`labels`/`task_labels`/`assignee_id`). Bảng `ai_action_logs` đã được thiết kế ở `04` §3.5 — Giai đoạn 3 không cần tạo.

---

## 8. Tài liệu

- [x] Tạo `Project-Documents/tasks/phase-3-ai-smart-setup.md` (checklist như trên).
- [x] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` (endpoints, service, cấu hình DeepSeek, contract bàn giao).
- [x] Cập nhật `README.md` root (mục "Trạng thái Giai đoạn 3") và tick checklist `03-roadmap.md` khi hoàn tất.

---

## Checklist Hoàn thiện Giai đoạn 3

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 4.
> **Trạng thái: Hoàn tất toàn bộ Backend (§1–§4), Frontend UI (§5), và Test Suite (§6).**

- [x] Interface `IAiProvider` triển khai xong cho DeepSeek (OpenAI-compatible) + `FakeAiProvider` cho dev
- [x] Nhập mô tả dự án/tính năng → AI trả về danh sách sub-tasks có nhãn và đề xuất người phụ trách (JSON đúng schema, có validate + normalize)
      — verify với DeepSeek thật (200, 7 task) và `FakeAiProvider` (200, 3 task)
- [x] Giao diện hiển thị kết quả AI đề xuất, cho phép chỉnh sửa trước khi xác nhận (§5 — hoàn tất)
- [x] Chưa ghi thẳng vào DB — chờ xác nhận (bàn giao contract cho Accountability Layer ở Giai đoạn 4)
      — verify: `tasks`/`labels`/`task_labels`/`workspace_members` không đổi sau khi gọi endpoint

### Việc của antigravity (§5 + §6) — Đã hoàn thành

- [x] §5 frontend `src/features/ai/` (`types`, `smartSetupApi`, `useSmartSetup`, `SmartSetupModal`,
      `ProposedTaskItem`) + gắn nút "AI Smart Setup" vào top bar `BoardView.tsx`.
- [x] §6.2 test Vitest cho `smartSetupApi` / `useSmartSetup` / `SmartSetupModal` / `ProposedTaskItem`; chạy `oxlint` (0 warn/0 err) +
      `tsc -b` + `vite build` sạch.
- [x] Cập nhật `README.md` root và tick `03-roadmap.md`.

