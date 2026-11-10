# Giai đoạn 15: Kế hoạch Hiện thực Tích hợp GitHub (GitHub Integration Implementation Plan)

> **Mã kế hoạch:** `PHASE-15-GITHUB-INTEGRATION`  
> **Kế thừa từ:** `project-docs/03-roadmap.md` §15, `project-docs/01-system-specification.md` §15, `project-docs/05-capstone-and-itgottalent.md` §6.6.  
> **Mục tiêu:** Xây dựng tính năng tích hợp GitHub hai chiều cho TeamNexus:
> 1. Tự động chuyển thẻ công việc sang cột Hoàn thành (`is_done`) khi Pull Request liên kết được **Merged**.
> 2. Gắn và hiển thị huy hiệu GitHub PR / Issue trực tiếp trên thẻ Kanban (`TaskCard`) và modal chi tiết (`TaskDetailModal`).
> 3. Tiếp nhận sự kiện Webhook an toàn qua chữ ký số **HMAC-SHA256**, chống tấn công phát lại (Replay Attack) với cơ chế **Idempotency**.
> 4. Quản lý cấu hình Repository & mã hóa Personal Access Token (PAT) an toàn theo từng Workspace.
> 5. Bảo đảm nguyên tắc **Graceful Degradation** — workspace không cấu hình GitHub thì hệ thống vẫn hoạt động trơn tru 100%.

---

## 0. Nguyên tắc Bất biến (Architectural Invariants)

1. **Bảo mật Webhook Tuyệt đối:** Mọi request gửi tới `POST /api/webhooks/github` **bắt buộc** phải có header `X-Hub-Signature-256`. Xác thực chữ ký bằng hàm so sánh hằng số thời gian `CryptographicOperations.FixedTimeEquals` chống tấn công Timing Attack. Chữ ký sai $\to$ lập tức trả về `401 Unauthorized`.
2. **Không lưu Plaintext Secret:** GitHub Personal Access Token (PAT) và Webhook Secret của Workspace **phải** được mã hóa bằng `IDataProtector` (ASP.NET Core Data Protection) trước khi ghi vào database PostgreSQL. API `GET` cấu hình tuyệt đối **không** trả về PAT thô (chỉ trả về chuỗi mặt nạ `ghp_••••••••`).
3. **Idempotency & Chống Trùng lặp:** Mọi lượt gửi webhook được kiểm soát qua header `X-GitHub-Delivery`. Nếu delivery ID đã được xử lý thành công, hệ thống bỏ qua và trả về `200 OK` mà không thực hiện di chuyển task lần 2.
4. **Không Vi phạm Ranh giới Module:** Logic GitHub Integration thuộc module `Board` (quản lý Workspace và Task), kết nối với `TaskService.MoveTaskAsync` và phát sự kiện qua `BoardHub`. Module `Ai` nếu muốn nghe webhook (ví dụ Jev Triage cho Issue mới) sẽ thông qua event/seam nội bộ, không tạo phụ thuộc vòng `Board ↔ Ai`.
5. **Đồng bộ Thời gian thực (Real-time):** Khi PR merge kích hoạt di chuyển thẻ, sự kiện `TaskMoved` **bắt buộc** phải được phát qua SignalR `BoardHub` để toàn bộ người dùng đang xem bảng thấy thẻ tự nhảy cột ngay tức thì.
6. **Graceful Degradation:** Mọi lỗi kết nối ra GitHub REST API (rate limit, token expired, network timeout) chỉ log cảnh báo, **không bao giờ** ném lỗi 500 hay làm gián đoạn luồng làm việc Kanban trong app.

---

## 1. Thiết kế Cơ sở Dữ liệu & Migration (Migration `Phase15GitHubIntegration`)

### 1.1. Bảng Mới: `workspace_integrations`
Lưu trữ thông tin tích hợp của Workspace với các dịch vụ bên ngoài (bắt đầu với GitHub):

| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
|---|---|---|---|
| `id` | `uuid` | PK | Khóa chính |
| `workspace_id` | `uuid` | FK $\to$ `workspaces(id)` | Workspace sở hữu tích hợp |
| `provider` | `text` | NOT NULL | Định danh dịch vụ (`'github'`) |
| `repo_name` | `text` | NULL | Tên repository dạng `owner/repo` (vd: `Thinh-TT/TeamNexus`) |
| `encrypted_pat` | `text` | NULL | GitHub Personal Access Token đã mã hóa |
| `encrypted_webhook_secret` | `text` | NULL | Webhook Secret dùng để xác thực HMAC-SHA256 |
| `is_enabled` | `boolean` | DEFAULT `true` | Bật/tắt trạng thái tích hợp |
| `created_at` | `timestamptz` | NOT NULL | Thời điểm tạo |
| `updated_at` | `timestamptz` | NOT NULL | Thời điểm cập nhật gần nhất |

> **Ràng buộc duy nhất:** `UNIQUE (workspace_id, provider)` đảm bảo mỗi workspace chỉ có tối đa một cấu hình GitHub.

### 1.2. Mở rộng Bảng `tasks` (`BoardTask`)
Thêm các cột metadata liên kết GitHub:

| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
|---|---|---|---|
| `github_pr_url` | `text` | NULL | Đường dẫn đầy đủ của Pull Request (vd: `https://github.com/org/repo/pull/42`) |
| `github_pr_number` | `int` | NULL | Số hiệu PR (vd: `42`) |
| `github_pr_status` | `text` | NULL | Trạng thái PR (`'open'`, `'merged'`, `'closed'`) |
| `github_issue_url` | `text` | NULL | Đường dẫn của Issue liên kết (nếu có) |
| `github_issue_number` | `int` | NULL | Số hiệu Issue (vd: `15`) |

### 1.3. Bảng Mới: `webhook_delivery_logs` (Kiểm soát Idempotency)
Ghi nhận mã định danh lượt giao webhook để phòng ngừa replay attack và trùng lặp:

| Tên cột | Kiểu dữ liệu | Ràng buộc | Mô tả |
|---|---|---|---|
| `id` | `uuid` | PK | Khóa chính |
| `delivery_id` | `varchar(128)` | UNIQUE, NOT NULL | Giá trị từ header `X-GitHub-Delivery` |
| `provider` | `varchar(32)` | NOT NULL | `'github'` |
| `event_type` | `varchar(64)` | NOT NULL | `'pull_request'`, `'issues'`, `'ping'` |
| `processed_at` | `timestamptz` | NOT NULL | Thời điểm xử lý |
| `status` | `varchar(32)` | NOT NULL | `'Processed'`, `'Ignored'`, `'Failed'` |
| `error_message` | `text` | NULL | Lý do lỗi nếu xử lý thất bại |

---

## 2. Thiết kế Backend (.NET 10)

```
src/
├── TeamNexus.Persistence/
│   ├── Data/
│   │   ├── Entities/
│   │   │   ├── WorkspaceIntegration.cs         # Entity lưu cấu hình tích hợp
│   │   │   ├── WebhookDeliveryLog.cs           # Entity lưu vết idempotency
│   │   │   └── BoardTask.cs                    # Bổ sung 5 trường github_*
│   │   └── Configurations/
│   │       ├── WorkspaceIntegrationConfiguration.cs
│   │       └── WebhookDeliveryLogConfiguration.cs
│   └── Migrations/
│       └── 20261110XXXXXX_Phase15GitHubIntegration.cs
├── Modules/Board/TeamNexus.Modules.Board/
│   ├── DTOs/
│   │   ├── GitHubIntegrationDtos.cs            # Request/Response cho Settings & Webhook
│   │   └── TaskGitHubLinkDtos.cs               # Gắn link PR/Issue vào Task
│   ├── Services/
│   │   ├── Integrations/
│   │   │   ├── IIntegrationSecretProtector.cs  # Mã hóa/giải mã PAT và Webhook Secret
│   │   │   ├── IntegrationSecretProtector.cs
│   │   │   ├── IGitHubIntegrationService.cs    # Quản lý cấu hình & Test connection
│   │   │   ├── GitHubIntegrationService.cs
│   │   │   ├── IGitHubWebhookHandler.cs        # Pipeline xử lý sự kiện webhook
│   │   │   ├── GitHubWebhookHandler.cs
│   │   │   └── GitHubSignatureValidator.cs     # Xác thực HMAC-SHA256 FixedTimeEquals
│   └── Endpoints/
│       ├── GitHubEndpoints.cs                  # REST Endpoints cho Workspace Settings
│       └── GitHubWebhookEndpoints.cs           # POST /api/webhooks/github (Public)
```

### 2.1. Hợp đồng API (Endpoints Contract)

#### A. Cấu hình Tích hợp Workspace (Manager / Admin)
- **`GET /api/workspaces/{workspaceId}/integrations/github`**
  - *Quyền hạn*: `Manager+`
  - *Response 200*:
    ```json
    {
      "workspaceId": "uuid",
      "provider": "github",
      "repoName": "Thinh-TT/TeamNexus",
      "hasPat": true,
      "maskedPat": "ghp_••••••••3944",
      "hasWebhookSecret": true,
      "isEnabled": true,
      "webhookUrl": "https://api.teamnexus.cloud/api/webhooks/github",
      "updatedAt": "2026-11-10T08:00:00Z"
    }
    ```
- **`PUT /api/workspaces/{workspaceId}/integrations/github`**
  - *Quyền hạn*: `Manager+`
  - *Request Body*:
    ```json
    {
      "repoName": "Thinh-TT/TeamNexus",
      "personalAccessToken": "ghp_xxxxxxxxxxxx", // nếu rỗng thì giữ nguyên PAT cũ
      "webhookSecret": "secret_abc123",          // nếu rỗng thì giữ nguyên secret cũ
      "isEnabled": true
    }
    ```
  - *Response 200*: Trả về `GitHubIntegrationResponse` tương tự GET.
- **`POST /api/workspaces/{workspaceId}/integrations/github/test`**
  - *Quyền hạn*: `Manager+`
  - *Mô tả*: Gọi trực tiếp GitHub REST API `GET https://api.github.com/repos/{owner}/{repo}` bằng PAT đã lưu.
  - *Response 200*: `{ "success": true, "repoFullName": "Thinh-TT/TeamNexus", "defaultBranch": "main" }`
  - *Response 400*: `{ "success": false, "error": "Bad credentials hoặc Repository không tồn tại" }`
- **`DELETE /api/workspaces/{workspaceId}/integrations/github`**
  - *Quyền hạn*: `Manager+`
  - *Response 204*: Xóa cấu hình tích hợp của workspace.

#### B. Gắn Link GitHub vào Thẻ (Member+)
- **`PUT /api/tasks/{taskId}/github-link`**
  - *Quyền hạn*: `Member+`
  - *Request Body*:
    ```json
    {
      "prUrl": "https://github.com/Thinh-TT/TeamNexus/pull/142",
      "issueUrl": "https://github.com/Thinh-TT/TeamNexus/issues/89"
    }
    ```
  - *Response 200*: Trả về `TaskResponse` với đầy đủ thông tin `githubPrNumber`, `githubPrStatus`, v.v.

#### C. Webhook Tiếp nhận Sự kiện (Public - GitHub Signature)
- **`POST /api/webhooks/github`**
  - *Xác thực*: Header `X-Hub-Signature-256`, `X-GitHub-Event`, `X-GitHub-Delivery`.
  - *Luồng thực thi*:
    1. Đọc raw body stream thành mảng byte.
    2. Trích xuất tên repository từ payload JSON (`payload.repository.full_name`).
    3. Tìm workspace đang tích hợp với repository đó. Nếu không tìm thấy $\to$ trả `200 OK` (bỏ qua nhẹ nhàng, graceful degradation).
    4. Giải mã `encrypted_webhook_secret` của workspace.
    5. Tính mã HMAC-SHA256 trên raw body và đối soát với `X-Hub-Signature-256` qua `FixedTimeEquals`. Sai $\to$ trả `401 Unauthorized`.
    6. Kiểm tra `webhook_delivery_logs`. Nếu `delivery_id` đã có $\to$ trả `200 OK` (`"Already processed"`).
    7. Ghi nhận `webhook_delivery_logs` trạng thái `Processed`.
    8. Nếu `X-GitHub-Event == "pull_request"`:
       - Kiểm tra `action == "closed"` và `merged == true`.
       - Lấy `prNumber = payload.pull_request.number`, `prUrl = payload.pull_request.html_url`.
       - **Tìm task liên kết**:
         - Cách 1: Task có `github_pr_number == prNumber` và thuộc workspace đó.
         - Cách 2: Tìm mã Task (UUID hoặc định dạng `TN-xxx`) trong `payload.pull_request.title`, `payload.pull_request.body`, hoặc `payload.pull_request.head.ref` (branch name).
       - Nếu tìm thấy task:
         - Cập nhật `task.github_pr_status = "merged"`.
         - Tìm cột có `is_done = true` trong board của task.
         - Nếu task chưa ở cột Done $\to$ gọi `TaskService.MoveTaskAsync(taskId, doneColumnId, targetPosition)`.
         - Ghi `activity_logs` với action `TaskCompletedViaGitHub`.
         - Phát SignalR `TaskMoved` tới nhóm `board-{boardId}`.
    9. Nếu `X-GitHub-Event == "ping"`: Trả về `{ "status": "pong", "zen": "..." }`.

---

## 3. Thiết kế Frontend (React 19 + TypeScript)

```
frontend/src/
├── features/
│   ├── workspace/
│   │   ├── components/
│   │   │   ├── GitHubIntegrationSettings.tsx   # Form cấu hình GitHub trong Settings
│   │   │   └── __tests__/
│   │   │       └── GitHubIntegrationSettings.test.tsx
│   │   └── services/
│   │       └── workspaceIntegrationsApi.ts     # Client Axios gọi API integrations
│   └── board/
│       ├── components/
│       │   ├── TaskGitHubChip.tsx              # Chip hiển thị PR/Issue trên TaskCard
│       │   └── __tests__/
│       │       └── TaskGitHubChip.test.tsx
│       ├── types/
│       │   └── board.types.ts                  # Mở rộng TaskResponse có trường github*
│       └── utils/
│           └── githubUrlParser.ts              # Parse PR number & Issue number từ URL
```

### 3.1. Giao diện Cài đặt Workspace (`GitHubIntegrationSettings.tsx`)
- Tab **"Tích hợp GitHub"** tại trang `/workspaces/:workspaceId/settings`:
  - Form nhập:
    - **Repository Name**: Ô input với placeholder `owner/repository` (vd: `facebook/react`).
    - **Personal Access Token (PAT)**: Ô password input, hiển thị trạng thái `Đã lưu (ghp_••••••••)` nếu đã có sẵn. Có link hướng dẫn cách tạo PAT trên GitHub với quyền `repo:read`.
    - **Webhook Secret**: Khóa bí mật dùng để ký webhook payload.
    - **Webhook Payload URL**: Hiển thị đường link `https://api.teamnexus.cloud/api/webhooks/github` kèm nút **"Sao chép liên kết"** 1-click để người dùng mang sang GitHub Webhook Settings.
  - Nút **"Kiểm tra kết nối"** (Test Connection): Click sẽ gọi API test, hiển thị trạng thái tick xanh `"Kết nối Repository thành công"` hoặc báo lỗi đỏ nếu PAT sai quyền.
  - Nút **"Lưu cấu hình"** và **"Ngắt kết nối"**.

### 3.2. Hiển thị Huy hiệu trên Kanban (`TaskGitHubChip.tsx` & `TaskCard.tsx`)
- Thẻ công việc có liên kết PR sẽ hiển thị một Chip nhỏ ở góc dưới:
  - Nếu PR đang Open: Badge màu xanh lục `#10b981` — `GitHub #142 Open`
  - Nếu PR đã Merged: Badge màu tím sang trọng `#8b5cf6` — `✔ PR #142 Merged`
  - Nếu PR đã Closed: Badge màu xám mờ `#64748b` — `✕ PR #142 Closed`
- Bấm vào Chip: Mở tab trình duyệt mới dẫn thẳng tới trang Pull Request trên GitHub (`target="_blank" rel="noopener noreferrer"`).
- Ngăn chặn sự kiện nổi bọt (stop propagation) để việc click vào chip không vô tình kích hoạt mở modal chi tiết thẻ.

### 3.3. Gắn Link PR trong Modal Chi tiết Thẻ (`TaskDetailModal.tsx`)
- Trong cột thông tin bên phải của Modal:
  - Mục **"Liên kết GitHub"**: Cho phép người dùng dán đường dẫn PR hoặc Issue.
  - Hệ thống tự động phân tích (parse) số hiệu PR và tự động hiển thị trạng thái hiện tại.

---

## 4. Danh mục Task Thực thi Tuần tự (Bite-Sized Action Checklist)

### Task 1: Khởi tạo Entity, DbContext Configuration & Migration Backend
- [x] **Step 1.1**: Tạo file `WorkspaceIntegration.cs` và `WebhookDeliveryLog.cs` trong `TeamNexus.Persistence.Data.Entities`.
- [x] **Step 1.2**: Bổ sung 5 trường `GithubPrUrl`, `GithubPrNumber`, `GithubPrStatus`, `GithubIssueUrl`, `GithubIssueNumber` vào `BoardTask.cs`.
- [x] **Step 1.3**: Tạo cấu hình Fluent API trong `TeamNexus.Persistence.Data.Configurations`:
  - `WorkspaceIntegrationConfiguration.cs` (snake_case, unique index `(workspace_id, provider)`).
  - `WebhookDeliveryLogConfiguration.cs` (unique index trên `delivery_id`).
- [x] **Step 1.4**: Đăng ký các `DbSet` mới vào `TeamNexusDbContext.cs`.
- [x] **Step 1.5**: Thêm EF Core migration `Phase15GitHubIntegration` và kiểm tra `has-pending-model-changes`.

### Task 2: DTOs & Dịch vụ Bảo vệ Secret (`IIntegrationSecretProtector`)
- [x] **Step 2.1**: Viết unit test cho `IntegrationSecretProtectorTests.cs` (kiểm tra mã hóa, giải mã, phát hiện dữ liệu bị can thiệp).
- [x] **Step 2.2**: Hiện thực `IIntegrationSecretProtector` sử dụng `IDataProtectionProvider`.
- [x] **Step 2.3**: Tạo các DTOs: `GitHubIntegrationDtos.cs`, `TaskGitHubLinkDtos.cs`.

### Task 3: Xác thực Chữ ký Webhook HMAC-SHA256 (`GitHubSignatureValidator`)
- [x] **Step 3.1**: Viết unit test cho `GitHubSignatureValidatorTests.cs`:
  - Chữ ký chuẩn xác $\to$ `true`.
  - Chữ ký giả mạo $\to$ `false`.
  - Body trống hoặc thiếu header $\to$ `false`.
  - Chống Timing Attack qua `CryptographicOperations.FixedTimeEquals`.
- [x] **Step 3.2**: Hiện thực lớp `GitHubSignatureValidator` dạng static pure function.

### Task 4: Dịch vụ Quản lý Tích hợp (`GitHubIntegrationService`)
- [x] **Step 4.1**: Viết test tích hợp `WorkspaceGitHubIntegrationApiTests.cs` (lưu PAT, test connection, phân quyền Manager+).
- [x] **Step 4.2**: Hiện thực interface `IGitHubIntegrationService` và class `GitHubIntegrationService`:
  - CRUD cấu hình tích hợp theo workspace.
  - Phương thức `TestConnectionAsync` sử dụng `IHttpClientFactory` gọi GitHub API.
- [x] **Step 4.3**: Tạo `GitHubEndpoints.cs` và đăng ký vào routing của Board module.

### Task 5: Webhook Handler & Tự động Chuyển Trạng thái Thẻ (`GitHubWebhookHandler`)
- [x] **Step 5.1**: Viết test tích hợp `GitHubWebhookApiTests.cs`:
  - Ping event $\to$ trả 200 Pong.
  - Signature không hợp lệ $\to$ 401 Unauthorized.
  - Pull Request merged $\to$ tìm thấy task $\to$ chuyển sang Done column $\to$ cập nhật `github_pr_status = 'merged'` $\to$ phát SignalR `TaskMoved`.
  - Gửi lại cùng `delivery_id` $\to$ trả 200 mà không di chuyển thẻ lần 2 (Idempotency test).
  - Webhook cho repo không tồn tại trong hệ thống $\to$ trả 200 bỏ qua nhẹ nhàng (Graceful degradation).
- [x] **Step 5.2**: Hiện thực `IGitHubWebhookHandler` và `GitHubWebhookHandler`.
- [x] **Step 5.3**: Ánh xạ endpoint `POST /api/webhooks/github` trong `GitHubWebhookEndpoints.cs`.

### Task 6: Frontend API & Giao diện Cấu hình (`GitHubIntegrationSettings`)
- [x] **Step 6.1**: Viết unit test cho `workspaceIntegrationsApi.test.ts`.
- [x] **Step 6.2**: Tạo module API `workspaceIntegrationsApi.ts` trong `frontend/src/features/workspace/services`.
- [x] **Step 6.3**: Viết unit test cho `GitHubIntegrationSettings.test.tsx`.
- [x] **Step 6.4**: Xây dựng component `GitHubIntegrationSettings.tsx` với form nhập, nút test kết nối và copy webhook URL.
- [x] **Step 6.5**: Tích hợp tab "Tích hợp GitHub" vào `WorkspaceSettingsPage.tsx`.

### Task 7: Frontend Thẻ Kanban & Modal Chi tiết (`TaskGitHubChip` & `TaskDetailModal`)
- [x] **Step 7.1**: Xây dựng helper `githubUrlParser.ts` và test tương ứng.
- [x] **Step 7.2**: Tạo component `TaskGitHubChip.tsx` và test component.
- [x] **Step 7.3**: Nhúng `TaskGitHubChip` vào `TaskCard.tsx`.
- [x] **Step 7.4**: Bổ sung khu vực gắn link PR/Issue trong `TaskDetailModal.tsx`.

### Task 8: Nghiệm thu Toàn diện & Báo cáo Giai đoạn 15
- [x] **Step 8.1**: Chạy toàn bộ test suite backend (`dotnet test`) $\to$ Đảm bảo 100% test PASS, 0 warning.
- [x] **Step 8.2**: Chạy kiểm thử frontend (`npm run lint`, `npm test`, `npm run build`) $\to$ Đảm bảo 0 lỗi.
- [x] **Step 8.3**: Lập báo cáo kiểm thử nghiệm thu `project-docs/report/phase-15-github-integration-test-report.md`.
- [x] **Step 8.4**: Cập nhật trạng thái trong `03-roadmap.md` và `01-system-specification.md`.
