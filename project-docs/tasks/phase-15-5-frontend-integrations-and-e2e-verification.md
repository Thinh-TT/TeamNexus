# Giai đoạn 15.5 — Giao diện Frontend (React 19 + Ant Design v6), Kiểm thử E2E & Đồng bộ Tài liệu

> **Thuộc:** [Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài](./phase-15-0-external-integrations-master-plan.md)
> **Tham chiếu thiết kế:** [`07-external-integrations-design.md`](../07-external-integrations-design.md) §2 (`F9`, `F10`), §8 (Frontend Design) & §9 (Testing Strategy).
> **Trạng thái:** 📋 Kế hoạch chi tiết (`T15.5.1` → `T15.5.5`).

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
     - Khi `comment.authorName === 'GitHub Bot'` hoặc `comment.userId === '00000000-0000-0000-0000-00000000b071'`: hiển thị avatar icon GitHub và tag `GITHUB BOT` rõ ràng.
     - Trong `AiActionLogItem.tsx`: hiển thị nhãn tiếng Việt cho `log.action === 'GitHubPrMoveTask'` (`'Tự động chuyển cột từ GitHub PR'`) và `log.action === 'GitHubIssueCreateTask'` (`'Tự động tạo thẻ từ GitHub Issue'`).

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
  3. **Cập nhật tài liệu dự án:** Đồng bộ số lượng bảng (21 → 27 bảng), số lượng migrations (14 → 15 migrations) và số lượng bài test trong `01-system-specification.md`, `03-roadmap.md`, `04-database-design.md` và lập báo cáo nghiệm thu `report/phase-15-external-integrations-test-report.md`.

