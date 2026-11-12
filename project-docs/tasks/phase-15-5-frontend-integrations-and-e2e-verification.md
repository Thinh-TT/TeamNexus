# Giai đoạn 15.5 — Giao diện Frontend (React 19 + Ant Design v6), Kiểm thử E2E & Đồng bộ Tài liệu

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F9`, `F10`), §8 (Frontend Design) & §9 (Testing Strategy).
> **Trạng thái:** ✅ **ĐÃ HOÀN THÀNH (2026-11-11)** — Toàn bộ `T15.5.1` → `T15.5.5` đã thi hành và kiểm chứng xong (cổng Frontend xanh 100%; xem [`phase-15-external-integrations-test-report.md`](../report/phase-15-external-integrations-test-report.md) về kết quả chi tiết cả Frontend & Backend).

---

## 0. Trạng thái Đầu vào & Bàn giao (đọc trước khi code)

### 0.1. Đã có sẵn từ 15.1 → 15.4 + đợt khắc phục review

| Hạng mục | Trạng thái |
| :--- | :--- |
| Backend GitHub Integration (5 endpoint quản trị + webhook công khai) | ✅ Xong — `20/20` test `GitHubWebhookPrTests` |
| GitHub Issue → Task, Jev Triage, comment sync, SignalR khi issue đóng/mở | ✅ Xong — `15/15` test `GitHubWebhookIssueAndCommentTests` |
| Accountability Layer: `GitHubPrMoveTask`, `GitHubIssueCreateTask` + Undo (đúng cột/vị trí/`CompletedAt`) | ✅ Xong |
| Nhãn tiếng Việt + basis GitHub + ẩn tag "0 tasks" trong `AiActionLogItem` (**một phần `T15.5.3` đã làm sớm**) | ✅ Xong (`0bd1aab`) |
| Cấu hình triển khai `Integrations__PublicApiBaseUrl`; key ring DataProtection lưu PostgreSQL `data_protection_keys` (TASK-DP-01, migration #16) | ✅ Đã chốt — `KeysPath` bị loại bỏ, `CertPath` chỉ là tuỳ chọn VPS |
| Cổng hiện tại | Build `0/0`, drift sạch, backend **1.329**, frontend **747**, Pure **665** |

### 0.2. Hợp đồng API frontend được phép dựa vào (đã kiểm chứng bằng test)

- `GET/PUT /api/workspaces/{id}/integrations/github`, `POST .../rotate-secret`, `DELETE ...`, `GET .../deliveries` — trả `SecretHint` (4 ký tự cuối) + `WebhookPayloadUrl` dựng từ `Integrations:PublicApiBaseUrl`; plaintext secret **chỉ** ở response tạo mới/rotate.
- `TaskResponse` đã có `taskNumber` + `externalLinks` (append-only, cuối record); `ColumnResponse` đã có `isInReview`.
- `TaskExternalLinkSummaryResponse`: `provider`, `linkType` (`pull_request`/`issue`), `externalId`, `externalUrl`, `title`, `state` (`draft`/`open`/`merged`/`closed`), `updatedAt`.
- `AiActionLog` action mới: `GitHubPrMoveTask`, `GitHubIssueCreateTask` (đã có nhãn ở `AiActionLogItem`).
- Webhook công khai trả `200` cho `Processed`/`Ignored`, **`502`** cho `Failed`, `413` khi payload > 256 KB, `401` khi sai HMAC, `404` khi token sai/tắt.

### 0.3. Bẫy phải giữ khi làm UI

- Auto-link GitHub chỉ nhận `TN-…`; **không** hiển thị `#n` như mã công việc.
- `AiActionLogItem` đã xử lý 2 action GitHub; đừng thêm lại nhánh trùng.
- Không đổi/đảo thứ tự tham số các record DTO hiện có (append-only, chuẩn bị Phase 16 Flutter).
- `data-testid` hiện có phải giữ nguyên (`open-settings-modal-btn`, ...).
- 1 tích hợp GitHub / workspace (chưa có đa repo/PAT) — UI chỉ cần 1 form cấu hình.

### 0.4. Việc còn lại của 15.5

Toàn bộ `T15.5.1` → `T15.5.5` bên dưới, **trừ** phần nhãn `AiActionLogItem` (đã xong). Checklist bàn giao đầy đủ (gồm env deploy + xác minh sau deploy): [`phase-15-remaining-frontend-handover.md`](./phase-15-remaining-frontend-handover.md).

---

## 1. Mục tiêu của Sub-phase 15.5

1. Cập nhật các kiểu dữ liệu TypeScript và xây dựng dịch vụ gọi API tích hợp (`integrationApi.ts` + custom hooks) cho cả Outbound Webhooks (Slack/Discord) và Inbound GitHub Integration.
2. Hiển thị **mã định danh công việc `TN-123`** (font monospace `JetBrains Mono`) và **huy hiệu trạng thái GitHub PR / Issue** (`Draft`, `Open`, `Merged`, `Closed`) ngay trên **`TaskCard`** và **`TaskDetailModal`**.
3. Hỗ trợ đánh dấu và hiển thị cột hệ thống thứ 3 **`Đang duyệt PR (In Review)`** (`isInReview`) trên **`KanbanColumn`** và biểu mẫu tạo/sửa cột.
4. Nâng cấp **`WorkspaceSettingsPage`** với hệ thống `Tabs` (**`Cài đặt chung`** + **`Tích hợp Slack / Discord / GitHub`**) chuẩn ngôn ngữ thiết kế **Luminous Enterprise v4.2**, đồng thời bảo toàn 100% `data-testid` hiện có.
5. Viết đầy đủ bài test Vitest cho các component mới, vượt qua toàn bộ cổng kiểm thử (`dotnet test`, `oxlint`, `tsc -b`, `vitest run`) và cập nhật bộ tài liệu dự án (`01`, `03`, `04`, `report/`).

---

## 2. Danh sách Tasks Chi tiết (`T15.5.1` → `T15.5.5`)

### Task `T15.5.1` — Cập nhật TypeScript Contracts & Xây dựng `integrationApi` + Hooks

- **Files tạo mới (`[NEW]`):**
  - `frontend/src/features/workspace/types/integration.types.ts`
  - `frontend/src/features/workspace/services/integrationApi.ts`
  - `frontend/src/features/workspace/hooks/useWorkspaceIntegrations.ts`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `frontend/src/features/board/types/board.types.ts`
- **Chi tiết thực hiện:**
  1. **`board.types.ts`:**
     - Thêm `isInReview?: boolean` vào `ColumnResponse`, `CreateColumnRequest`, `UpdateColumnRequest`.
     - Thêm interface `TaskExternalLinkSummary`:
       ```ts
       export interface TaskExternalLinkSummary {
         id: string
         provider: 'github' | string
         linkType: 'pull_request' | 'issue' | string
         externalId: string
         externalUrl: string
         title: string
         state: 'draft' | 'open' | 'merged' | 'closed' | string
         updatedAt: string
       }
       ```
     - Thêm `taskNumber?: number` và `externalLinks?: TaskExternalLinkSummary[]` vào `TaskResponse`.
  2. **`integration.types.ts` & `integrationApi.ts`:**
     - Định nghĩa đầy đủ kiểu dữ liệu và hàm gọi Axios cho:
       - `listWebhooks(workspaceId)`, `createWebhook(workspaceId, req)`, `updateWebhook(workspaceId, id, req)`, `deleteWebhook(workspaceId, id)`, `testWebhook(workspaceId, id)`, `getWebhookDeliveries(workspaceId, id)`.
       - `getGitHubIntegration(workspaceId)`, `upsertGitHubIntegration(workspaceId, req)`, `rotateGitHubSecret(workspaceId)`, `deleteGitHubIntegration(workspaceId)`, `getGitHubDeliveries(workspaceId)`.

---

### Task `T15.5.2` — Hiển thị Mã `TN-123`, Huy hiệu PR/Issue trên `TaskCard` & Vai trò Cột `IsInReview` trên `KanbanColumn`

- **Files chỉnh sửa (`[MODIFY]`):**
  - `frontend/src/features/board/components/TaskCard.tsx`
  - `frontend/src/features/board/components/KanbanColumn.tsx`
  - `frontend/src/features/board/components/BoardView.tsx`
- **Chi tiết thực hiện:**
  1. **Trên `TaskCard.tsx`:**
     - Nếu `task.taskNumber && task.taskNumber > 0`: hiển thị pill nhỏ `TN-${task.taskNumber}` (font `var(--tn-font-mono, 'JetBrains Mono', monospace)`, cỡ `11px`, nền `#f1f5f9`, chữ `#475569`) ở hàng trên cùng của thẻ.
     - Nếu `task.externalLinks && task.externalLinks.length > 0`: hiển thị huy hiệu GitHub nhỏ gọn (icon `GithubOutlined` + số hiệu `#88` + màu trạng thái: `open` xanh lá `#10b981`, `merged` tím `#8b5cf6`, `draft` xám `#64748b`, `closed` đỏ `#ef4444`).
  2. **Trên `KanbanColumn.tsx` & Modal cột:**
     - Hiển thị tag nhận diện `Đang duyệt PR` (màu tím/xanh dương) trên tiêu đề cột khi `column.isInReview === true`.
     - Cho phép chọn vai trò cột `Đang duyệt PR (In Review)` (loại trừ lẫn nhau với `Cột Hoàn thành` và `Cột Cần làm rõ`).

---

### Task `T15.5.3` — Nâng cấp `TaskDetailModal`: Mã `TN-123` (kèm Copy), Khối Liên kết GitHub & Nhãn `GitHub Bot`

- **Files chỉnh sửa (`[MODIFY]`):**
  - `frontend/src/features/board/components/TaskDetailModal.tsx`
  - `frontend/src/features/ai/components/AiActionLogItem.tsx`
- **Chi tiết thực hiện:**
  1. **Header mã công việc (`TaskDetailModal.tsx` dòng 392):**
     - Nếu `task.taskNumber && task.taskNumber > 0`: hiển thị `TN-${task.taskNumber}` kèm nút copy nhanh (`CopyOutlined` → copy chuỗi `TN-${task.taskNumber}` vào clipboard và báo `Đã sao chép mã TN-${task.taskNumber}`).
     - Nếu `task.taskNumber` chưa có (trong các fixture test cũ): fallback về `#{task.id.slice(0, 8).toUpperCase()}` để **không làm gãy bất kỳ bài test Vitest hiện có nào**.
  2. **Khối Liên kết GitHub (Pull Requests & Issues):**
     - Khi `task.externalLinks` có phần tử: hiển thị khung thông tin liên kết GitHub ngay bên dưới phần mô tả/metadata với danh sách PR/Issue, badge trạng thái (`Draft`, `Open`, `Merged`, `Closed`), tiêu đề và link mở tab mới (`target="_blank" rel="noopener noreferrer"`).
  3. **Nhận diện bình luận từ `GitHub Bot` & Nhãn hành động trên `AiActionLogItem.tsx`:**
     - Khi `comment.authorName === 'GitHub Bot'` hoặc `comment.userId === '00000000-0000-0000-0000-00000000b071'`: hiển thị avatar icon GitHub và tag `GITHUB BOT` rõ ràng (**còn lại**).
     - Trong `AiActionLogItem.tsx`: nhãn `GitHubPrMoveTask` → *"Tự động chuyển thẻ (GitHub PR)"* và `GitHubIssueCreateTask` → *"Tạo thẻ từ GitHub Issue"*, kèm basis GitHub + ẩn tag "0 tasks" (**đã hoàn tất sớm** trong đợt khắc phục review — commit `0bd1aab`; chỉ cần giữ nguyên khi sửa file này).

---

### Task `T15.5.4` — Xây dựng Tab `Tích hợp (Slack / Discord / GitHub)` trong `WorkspaceSettingsPage`

- **Files tạo mới (`[NEW]`):**
  - `frontend/src/features/workspace/components/OutboundWebhooksSection.tsx`
  - `frontend/src/features/workspace/components/GitHubIntegrationSection.tsx`
- **Files chỉnh sửa (`[MODIFY]`):**
  - `frontend/src/features/workspace/pages/WorkspaceSettingsPage.tsx`
- **Chi tiết thực hiện:**
  1. **Tổ chức `Tabs` trên `WorkspaceSettingsPage.tsx`:**
     - **Tab 1 — `Cài đặt chung` (`key="general"`, mặc định active):** Giữ nguyên toàn bộ thẻ tổng quan, bảng `Descriptions` và nút `data-testid="open-settings-modal-btn"`.
     - **Tab 2 — `Tích hợp (Slack / Discord / GitHub)` (`key="integrations"`):**
       - **`OutboundWebhooksSection`:**
         - Hiển thị thẻ thống kê 24h (`Đã gửi`, `Thất bại`, `Đang chờ Outbox`).
         - Bảng danh sách Slack / Discord endpoints (hiển thị icon nhà cung cấp, tên kênh, các sự kiện đăng ký dạng `Tag`, trạng thái hoạt động, nút **Gửi thử**, **Lịch sử gửi**, **Sửa**, **Xóa**).
         - Modal thêm/sửa Webhook: chọn `Slack` hoặc `Discord`, nhập tên, URL HTTPS, và chọn các sự kiện (`Cảnh báo rủi ro AI Observer`, `Đề xuất AI chờ duyệt`, `Thẻ cần làm rõ`, `Giao việc mới`, `Nhắc tên trong bình luận`, `Thành viên mới tham gia`).
         - Drawer xem lịch sử 20 lần gửi gần nhất trong Outbox (trạng thái `Sent`/`Failed`/`Pending`, mã HTTP, số lần thử).
       - **`GitHubIntegrationSection`:**
         - Biểu mẫu cấu hình: công tắc `Bật tích hợp GitHub`, chọn `Bảng mặc định nhận Issue (Default Board)`, nhập `Bộ lọc nhãn Issue` (VD: `teamnexus, bug`), và 3 công tắc tự động hóa (`Tự động chuyển cột khi mở/merge PR`, `Tự động tạo thẻ & Jev Triage từ Issue`, `Đồng bộ bình luận từ GitHub`).
         - Hộp hiển thị **`Webhook Payload URL`** (kèm nút Copy) và cảnh báo hiển thị **`Webhook Secret` duy nhất 1 lần** khi vừa tạo hoặc bấm **`Xoay vòng Secret (Rotate Secret)`**.
         - Bảng nhật ký 30 lần nhận sự kiện GitHub gần nhất (`X-GitHub-Delivery`, loại sự kiện `pull_request`/`issues`/`issue_comment`/`ping`, trạng thái `Processed`/`Ignored`/`Failed`, tóm tắt kết quả).

---

### Task `T15.5.5` — Kiểm thử Toàn diện (Vitest + xUnit) & Cập nhật Tài liệu Dự án

- **Files tạo mới / chỉnh sửa (`[NEW]` / `[MODIFY]`):**
  - `[NEW]` `frontend/src/features/workspace/components/__tests__/WorkspaceIntegrations.test.tsx`
  - `[NEW]` `frontend/src/features/workspace/services/__tests__/integrationApi.test.ts`
  - `[MODIFY]` `frontend/src/features/board/components/__tests__/TaskCard.test.tsx`
  - `[MODIFY]` `frontend/src/features/board/components/__tests__/TaskDetailModal.test.tsx`
  - `[MODIFY]` `project-docs/01-system-specification.md`
  - `[MODIFY]` `project-docs/03-roadmap.md`
  - `[MODIFY]` `project-docs/04-database-design.md`
  - `[NEW]` `project-docs/report/phase-15-external-integrations-test-report.md`
- **Chi tiết kiểm thử & nghiệm thu cuối kỳ:**
  1. **Frontend Vitest (`>= 20` bài test mới):**
     - `TaskCard`: hiển thị đúng `TN-42` và huy hiệu `PR #88 · Merged`.
     - `TaskDetailModal`: hiển thị `TN-42`, copy mã, hiển thị danh sách liên kết GitHub PR/Issue, và fallback về `#XXXXXXXX` khi không có `taskNumber`.
     - `WorkspaceSettingsPage` & `WorkspaceIntegrations`: chuyển tab `Tích hợp`, tạo Slack/Discord webhook, bấm `Gửi thử`, cấu hình GitHub Integration và hiển thị `WebhookSecretPlaintext` 1 lần.
  2. **Chạy toàn bộ cổng kiểm thử (Verification Gates):**
     - `dotnet build TeamNexus.sln --no-incremental` → `0 Warning(s), 0 Error(s)`.
     - `dotnet ef migrations has-pending-model-changes` → sạch.
     - `dotnet test TeamNexus.sln` → `0 Failed`.
     - `npm run lint` (`oxlint`) → `0 warnings, 0 errors`.
     - `npx tsc -b` → `exit 0`.
     - `npx vitest run` → `100% PASS`.
  3. **Cập nhật tài liệu dự án:** Đồng bộ số lượng bảng (21 → 27 bảng domain + 7 bảng Identity), số lượng migrations (14 → 15 migrations) và số lượng bài test trong `01-system-specification.md`, `02-tech-stack-decisions.md`, `03-roadmap.md`, `04-database-design.md` và lập báo cáo nghiệm thu `report/phase-15-external-integrations-test-report.md`.

---

## 3. Nhật ký thi hành (Execution Log — 2026-11-11)

### 3.1. Quyết định kỹ thuật chính (`K1..K4`)
- **K1 — Xóa sạch cấu hình tùy chọn từ `GitHubIntegrationSection` đúng hợp đồng Backend (`EMPTY_GUID` & `""`):** Gửi `00000000-0000-0000-0000-000000000000` khi người dùng xóa chọn `defaultBoardId` và chuỗi rỗng `""` khi xóa `issueLabelFilter` (thay vì `null`/`undefined`) để khớp logic chuẩn hóa trong `WorkspaceGitHubIntegrationService`.
- **K2 — Fallback an toàn cho fixture cũ & nhận diện `GitHub Bot` kép:** `TaskCard`, `TaskDetailModal` và `TaskSearchResultList` chỉ hiển thị badge `TN-123` khi `taskNumber > 0`; `TaskDetailModal` fallback về `#XXXXXXXX` khi thiếu `taskNumber`, và nhận diện comment bot qua cả `authorName === 'GitHub Bot'`, `authorId === GITHUB_BOT_USER_ID` lẫn legacy `userId` (`00000000-0000-0000-0000-00000000b071`).
- **K3 — Bảo toàn `data-testid` & nguyên tắc không trùng lặp UI:** Giữ nguyên `open-settings-modal-btn` trong header `WorkspaceSettingsPage`, đặt nội dung cũ vào tab mặc định `general` (`Cài đặt chung`), mở tab mới `integrations` (`Tích hợp (Slack / Discord / GitHub)`), và không sửa trùng lặp logic `AiActionLogItem.tsx` đã hoàn tất ở commit `0bd1aab`.
- **K4 — Chống click-through & giao thức an toàn trên Kanban Card:** Mọi chip PR/Issue trên `TaskCard` giới hạn hiển thị tối đa 3 chip + `+N`, lọc URL qua `isSafeHref`, và gọi `stopPropagation()` ở `onClick`/`onPointerDown` để tránh kéo nhầm hoặc mở modal thẻ ngoài ý muốn.

### 3.2. Độ lệch có chủ đích (`L1..L2`)
- **L1 — Mở rộng phạm vi tìm kiếm `BoardView` / `TaskSearchResultList` / `ColumnModal.tsx` & giữ `delete={isCompleted}` trên `TaskCard`:** Bổ sung lọc `tn-{taskNumber}` trực tiếp trên thanh tìm kiếm client của `BoardView`, tách riêng `ColumnModal.test.tsx` (4 test) và giữ `delete={isCompleted}` trên tiêu đề `TaskCard` để tôn trọng assertion của bài test `TaskCard.test.tsx` hiện có.
- **L2 — Kế thừa lệch thiết kế Backend 15.3 (1 GitHub Integration / workspace, không PAT / manual URL paste):** UI `GitHubIntegrationSection` bám sát API thật (cấu hình 1 tích hợp / workspace bằng `WebhookToken` + `WebhookSecret`, không có form nhập PAT hay dán URL PR thủ công).

### 3.3. Lỗi thật phát hiện & xử lý trong lúc thi hành (`BUG1..BUG4`)
- **BUG1 — `boardStore.applyColumnUpdated` làm rơi thuộc tính cột:** Phát hiện khi nối `isInReview` rằng hàm `applyColumnUpdated` chỉ copy `{ name, isDone }` nên cập nhật cột cục bộ hoặc SignalR `ColumnUpdated` sẽ làm mất `isInReview`, `isClarification`, `position` và `updatedAt`. Đã sửa để cập nhật đầy đủ các trường này.
- **BUG2 — Cảnh báo lint `react(set-state-in-effect)` trong `useWorkspaceIntegrations`:** Gọi `reload()` trực tiếp đồng bộ trong `useEffect` kích hoạt rule của `oxlint`. Đã bọc qua `Promise.resolve().then(...)` kèm cờ cleanup `ignore` để đảm bảo an toàn unmount.
- **BUG3 — Đường dẫn mock cũ sai trong `TaskDetailModal.test.tsx`:** `vi.mock('../services/boardApi')` trỏ sai thư mục (`components/__tests__` tới `services` phải là `../../services/boardApi`). Đã sửa lại chuẩn trong lúc bổ sung 4 bài test 15.5.
- **BUG4 — `destroyOnClose` trên `ColumnModal`:** Đã chuyển sang `destroyOnHidden` theo chuẩn Ant Design v6 khi nâng cấp `ColumnModal.tsx`.

### 3.4. Kết quả kiểm chứng đo thật sau chỉnh sửa cuối
| Cổng | Kết quả đo thật | Ghi chú |
|---|---|---|
| `npm run lint` (`oxlint`) | ✅ **0 warnings, 0 errors** (299 files, 116 rules) | Chạy từ `frontend/` |
| `npm run build` (`tsc -b && vite build`) | ✅ **exit 0** (3,340 modules, 1.31s) | Không phát sinh lỗi TS/bundle |
| `npm test` (Vitest) | ✅ **127/127 files PASS · 786/786 tests PASS** | `+39` test mới so với baseline 747; đã nâng gate [`ci-web.yml`](../../.github/workflows/ci-web.yml) lên `786` |
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | ✅ **0 Warning(s), 0 Error(s)** | Backend không đổi mã nguồn từ sau 15.4 |
| `dotnet test --filter "FullyQualifiedName~Pure"` | ✅ **Failed: 0 · Passed: 665 · Skipped: 0 · Total: 665** | Toàn bộ Pure unit tests xanh |
| `dotnet test` (toàn suite trên máy cục bộ Windows) | ⚠️ **Failed: 0 · Passed: 670 · Skipped: 659 · Total: 1,329** | Docker daemon tắt & PostgreSQL 18 native không cài extension `pgvector`, nên 659 DB integration tests tự skip sạch qua `DatabaseFixture.EnsureVectorExtensionAvailable`; bộ 1,329 toàn phần & `dotnet ef migrations has-pending-model-changes` (tool `dotnet-ef` chưa cài global trên máy cục bộ) cần chạy với container `pgvector/pgvector:pg18` như ở `.github/workflows/ci-backend.yml` |

### 3.5. Khắc phục sau Review 15.5 trước khi merge (2026-11-11)

| Mã | Lỗi | Thay đổi | Kiểm chứng |
| :--- | :--- | :--- | :--- |
| **R15.5-1** | Báo cáo khai báo có test "clear `DefaultBoardId`/`IssueLabelFilter`" nhưng chưa có | Tách `buildGitHubIntegrationRequest` + `requiresDefaultBoardForAutoCreate` ra file utils và unit-test 4 case (`EMPTY_GUID`, `""`, giữ board, điều kiện board) | `githubIntegrationRequest.test.ts` (4 tests) |
| **R15.5-2** | Copy báo thành công giả khi `navigator.clipboard` không khả dụng | Guard trước khi gọi + thông báo lỗi rõ ràng ở `TaskDetailModal` và `GitHubIntegrationSection` | 2 test "clipboard unavailable ⇒ báo lỗi" |
| **R15.5-3** | "Kích hoạt" tự chọn bảng đầu tiên và bật auto-create từ mọi Issue | Bỏ preselect board; chặn lưu khi `autoCreateTasksFromIssues` bật mà chưa chọn board; thêm ghi chú dưới ô chọn bảng | Test "blocks enabling auto-create…" (không gọi API + hiện cảnh báo) |

**Còn lại (follow-up sau merge, xem báo cáo review §5):** `boardStore` giữ `isInReview` khi thiếu field; `ColumnModal` bắt rejection 409; lazy-load theo tab / huỷ request; nhận diện GitHub Bot theo ID; giữ key subscription legacy khi sửa webhook; exact-match tìm `tn-…`; bỏ plaintext secret khỏi state; bỏ prop `delete` trên tiêu đề `TaskCard`; **E2E/live verification trên production vẫn chưa chạy** (dùng checklist §6 của báo cáo nghiệm thu sau deploy).


