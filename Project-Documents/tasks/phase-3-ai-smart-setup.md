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

- [ ] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Options/DeepSeekOptions.cs` (theo pattern `JwtOptions`):
  - `SectionName = "DeepSeek"`.
  - Thuộc tính: `ApiKey` (string, mặc định `""`), `BaseUrl` (mặc định `https://api.deepseek.com`), `Model` (mặc định `deepseek-chat`), `TimeoutSeconds` (mặc định 60), `MaxTokens` (mặc định 4096), `Temperature` (mặc định 0.2), `MaxTaskCount` (mặc định 20).
- [ ] Thêm section vào `src/TeamNexus.Api/appsettings.json`:
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
- [ ] Ghi chú đặt key qua User Secrets: `dotnet user-secrets set --project src/TeamNexus.Api "DeepSeek:ApiKey" "sk-..."`.
- [ ] Thêm `ProjectReference` Ai vào `src/TeamNexus.Api/TeamNexus.Api.csproj`; thêm project Ai vào `TeamNexus.sln`.

### 1.2 `AiModule.cs`

- [ ] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/AiModule.cs`:
  - `AddAiModule(IConfiguration)`:
    - `AddOptions<DeepSeekOptions>().Bind(configuration.GetSection("DeepSeek"))` (không `ValidateOnStart` cứng — cho phép dev không key).
    - `AddHttpClient<DeepSeekAiProvider>()` (timeout từ `TimeoutSeconds`).
    - Đăng ký `IAiProvider` → `FakeAiProvider` nếu `ApiKey` rỗng, ngược lại `DeepSeekAiProvider`.
    - `AddScoped<ISmartSetupService, SmartSetupService>()`; `AddTransient<DomainExceptionFilter>()` + `AddTransient<AntiforgeryValidationEndpointFilter>()`.
  - `MapAiModuleEndpoints()` → map `SmartSetupEndpoints`.
- [ ] Đăng ký ở `src/TeamNexus.Api/Program.cs`:
  - `builder.Services.AddAiModule(builder.Configuration);`
  - `app.MapAiModuleEndpoints();` (sau `UseAuthentication`/`UseAuthorization`, cùng chỗ với Board).

---

## 2. Backend – `IAiProvider` & DeepSeek

### 2.1 Contract (`Services/AiProvider.cs`)

- [ ] Định nghĩa:
  ```csharp
  public interface IAiProvider
  {
      Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);
  }
  public sealed record AiCompletionRequest(string SystemPrompt, string UserPrompt, double Temperature, int MaxTokens, bool JsonMode);
  public sealed record AiCompletionResult(string Content, int? PromptTokens, int? CompletionTokens);
  ```
- [ ] Interface đủ tổng quát để Giai đoạn 5 (Observer) tái sử dụng (chỉ đổi prompt).

### 2.2 `DeepSeekAiProvider` (`Services/DeepSeekAiProvider.cs`)

- [ ] Gọi `POST {BaseUrl}/chat/completions` qua `HttpClient` (từ `IHttpClientFactory`), header `Authorization: Bearer {ApiKey}`.
- [ ] Body OpenAI-compatible: `model`, `messages = [system, user]`, `temperature`, `max_tokens`; nếu `JsonMode` → `response_format = { "type": "json_object" }`.
- [ ] Deserialize response `choices[0].message.content` (+ `usage` tokens) → `AiCompletionResult`.
- [ ] Map mọi lỗi (HTTP ≠ 2xx, timeout, content rỗng) → `AiProviderException` (502). Không log API key; chỉ log ở mức Warning.

### 2.3 `FakeAiProvider` (`Services/FakeAiProvider.cs`)

- [ ] Trả proposal JSON mẫu cố định (vài sub-task có label + assignee gợi ý) đúng schema §4.1 — để dev/test offline, không tốn chi phí.

---

## 3. Backend – Dữ liệu hỗ trợ & Endpoints

### 3.1 (Board, bổ sung nhỏ) Liệt kê thành viên workspace

- [ ] Thêm `GET /api/workspaces/{workspaceId:guid}/members` (Member+): trả `[{ userId, displayName, role, avatarUrl? }]`.
- [ ] Hiện thực trong module Board: `Endpoints/MembersEndpoints.cs` + `Services/IWorkspaceMemberService` / `WorkspaceMemberService` (query `workspace_members` + `users`), đăng ký ở `AddBoardModule`. Lý do: cần danh sách thành viên cho dropdown chọn người phụ trách khi chỉnh sửa đề xuất (tái dùng cho Giai đoạn 4/5).

### 3.2 `SmartSetupEndpoints.cs` (Ai)

- [ ] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Endpoints/SmartSetupEndpoints.cs`:
  - `POST /api/boards/{boardId:guid}/smart-setup` (body `SmartSetupRequest`).
  - `RequireAuthorization` + `AntiforgeryValidationEndpointFilter` + `DomainExceptionFilter`.
  - Gọi `ISmartSetupService.GenerateAsync(...)` → `200 { SmartSetupProposal }`.
- [ ] Không có endpoint ghi DB ở Giai đoạn 3.

---

## 4. Backend – Smart Setup Service & JSON Schema

### 4.1 Contract đầu ra của AI (`Contracts/SmartSetupAiModels.cs`)

- [ ] Định nghĩa raw AI output (khớp chặt system prompt):
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

- [ ] Định nghĩa:
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

### 4.3 `ISmartSetupService` / `SmartSetupService`

- [ ] `GenerateAsync(boardId, request, userId, ct)`:
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

- [ ] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/Services/AiExceptions.cs`: `AiProviderException : BoardModuleException` (status 502) — dùng cho lỗi gọi API / JSON sai schema.

---

## 5. Frontend – UI Smart Setup

### 5.1 Feature `src/features/ai/`

- [ ] `types/smartSetup.types.ts` — mirror DTO §4.2 (camelCase).
- [ ] `services/smartSetupApi.ts` — `generateSmartSetup(boardId, { description })` qua `httpClient` (base `/api`).
- [ ] `hooks/useSmartSetup.ts` — state `{ status: idle|generating|ready|error, proposal, error }`, action `generate(description)`, cho phép sửa danh sách task local trước khi xác nhận.
- [ ] `components/SmartSetupModal.tsx` — Modal Ant Design:
  - Bước 1: `Input.TextArea` mô tả + nút "Tạo đề xuất" (loading).
  - Bước 2: hiển thị danh sách `ProposedTaskItem` **có thể chỉnh sửa** (title, description, priority Select, assignee Select từ `/workspaces/{wsId}/members`, label tags từ `/workspaces/{wsId}/labels` + nhãn mới).
  - Bước 3: nút "Xác nhận" → Giai đoạn 3 **không ghi DB**; hiển thị `Result.success` "Đề xuất đã xác nhận — sẽ được áp dụng qua Accountability Layer ở Giai đoạn 4".
  - Xử lý trạng thái lỗi (403, 502, empty) bằng `Alert`.
- [ ] `components/ProposedTaskItem.tsx` — dòng chỉnh sửa 1 task đề xuất.

### 5.2 Tích hợp vào Board

- [ ] Thêm nút "AI Smart Setup" (icon robot/spark) vào top bar của `BoardView.tsx`, mở `SmartSetupModal` truyền `boardId`, `workspaceId`, `columns`, `workspaceLabels`.
- [ ] Visibility: hiển thị nút luôn, backend enforce Manager/Admin (403 hiện thông báo rõ). Nâng cao (tuỳ chọn): ẩn nút cho Member bằng cách đọc `role` từ `GET /api/workspaces` hiện có.

---

## 6. Kiểm thử & xác minh (theo phong cách Giai đoạn 1–2)

### 6.1 Verify backend (Scalar + API probe)

- [ ] `POST /smart-setup` khi chưa đăng nhập → 401; Member → 403; mô tả rỗng → 400; board không tồn tại → 404.
- [ ] Dùng `FakeAiProvider` (không key): trả proposal đúng schema, resolve assignee/label đúng (matched/unmatched).
- [ ] Bật key thật (nếu có): gọi DeepSeek trả JSON đúng schema, validate + normalize (priority sai → null, label dupe → gọn).
- [ ] Verify **không ghi DB**: số row `tasks`/`labels`/`ai_action_logs` không đổi sau generate.
- [ ] `GET /api/workspaces/{wsId}/members` trả đúng danh sách, Member+ đọc được.

### 6.2 Frontend (Vitest + Testing Library)

- [ ] Test `smartSetupApi` (gọi đúng URL/body).
- [ ] Test `useSmartSetup` (idle → generating → ready → error).
- [ ] Test `SmartSetupModal` (hiển thị proposal, sửa task, trạng thái lỗi, nút xác nhận không gọi API ghi).
- [ ] `oxlint` 0 warning/error, `tsc -b` + `vite build` pass.

### 6.3 Điều kiện hoàn thành Giai đoạn 3 (map `03-roadmap.md`)

- [ ] `IAiProvider` triển khai xong cho DeepSeek (+ `FakeAiProvider` dev).
- [ ] Nhập mô tả → AI trả danh sách sub-tasks có nhãn + đề xuất người phụ trách; JSON đúng schema, có validate.
- [ ] UI hiển thị kết quả đề xuất, cho phép chỉnh sửa trước khi xác nhận.
- [ ] Không ghi thẳng DB — chờ xác nhận (bàn giao Accountability Layer Giai đoạn 4).

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

- [ ] Tạo `Project-Documents/tasks/phase-3-ai-smart-setup.md` (checklist như trên).
- [ ] Tạo `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` (endpoints, service, cấu hình DeepSeek, contract bàn giao).
- [ ] Cập nhật `README.md` root (mục "Trạng thái Giai đoạn 3") và tick checklist `03-roadmap.md` khi hoàn tất.

---

## Checklist Hoàn thiện Giai đoạn 3

> Tất cả các mục dưới đây phải ✅ trước khi chuyển sang Giai đoạn 4.

- [ ] Interface `IAiProvider` triển khai xong cho DeepSeek (OpenAI-compatible) + `FakeAiProvider` cho dev
- [ ] Nhập mô tả dự án/tính năng → AI trả về danh sách sub-tasks có nhãn và đề xuất người phụ trách (JSON đúng schema, có validate + normalize)
- [ ] Giao diện hiển thị kết quả AI đề xuất, cho phép chỉnh sửa trước khi xác nhận
- [ ] Chưa ghi thẳng vào DB — chờ xác nhận (bàn giao contract cho Accountability Layer ở Giai đoạn 4)
