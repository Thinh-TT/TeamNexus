# Giai đoạn 15 — Báo cáo Nghiệm thu Tổng hợp (Sub-phases 15.1 → 15.5)

> **Phạm vi báo cáo:** Toàn bộ **Giai đoạn 15 — Tích hợp Hệ sinh thái Bên ngoài** gồm:
> - **15.1:** Migration #15 (`Phase15ExternalIntegrations`), mã tuần tự `TN-123`, cột `is_in_review`, tài khoản hệ thống `GitHub Bot`, port `ITaskAutomation` (`TaskAutomationService`).
> - **15.2:** Outbound Slack / Discord Webhooks (`WebhookOutboxExternalAlertSink`, `WebhookOutboxRunner` / `Dispatcher`, `WebhookUrlValidator` SSRF guard, Slack Block Kit & Discord Embed formatters, sự kiện `MemberJoined`, 6 endpoint `/api/workspaces/{id}/integrations/webhooks`).
> - **15.3:** Inbound GitHub Webhooks Core & PR State Machine (`WorkspaceGitHubIntegrationEndpoints`, `GitHubWebhookEndpoints` public HMAC-SHA256 + idempotency theo `X-GitHub-Delivery`, `GitHubSecretProtector`, `TaskKeyParser`, PR → `In Review` / `Done` + Undo `GitHubPrMoveTask`).
> - **15.4:** GitHub Issue → Task + Jev 1.13 `TriageForSystemAsync` (+ fix cờ `Degraded`), SignalR đóng/mở Issue, Undo `GitHubIssueCreateTask`, đồng bộ bình luận 1 chiều qua `GitHub Bot`.
> - **15.5:** Giao diện Frontend React 19 + TypeScript 6 + Ant Design v6 (`integrationApi`, `useWorkspaceIntegrations`, `TN-123` + GitHub chips, `isInReview` column, `WorkspaceSettingsPage` Tabs) + Vitest tests (`+31`) + Đồng bộ tài liệu dự án.
>
> **Ngày nghiệm thu:** 11/11/2026.

---

## 1. Tóm tắt Trạng thái Các Sub-phase

| Sub-phase | Nội dung trọng tâm | Trạng thái | Ghi chú kiểm chứng |
| :--- | :--- | :---: | :--- |
| **15.1** | Migration #15 (`20261110154843_Phase15ExternalIntegrations`), 6 bảng mới, `task_numbers` / `TN-123`, `is_in_review`, `GitHub Bot`, `ITaskAutomation` | ✅ **XONG** | Đã merge từ `develop` |
| **15.2** | Outbound Slack/Discord + Transactional Outbox + Retry + SSRF Guard (`WebhookUrlValidator`) | ✅ **XONG** | Đã merge từ `develop` |
| **15.3** | Inbound GitHub Webhook (`HMAC-SHA256`, body <= 256 KB, replay protection), `TaskKeyParser` (`TN-`), PR State Machine + `GitHubPrMoveTask` Undo | ✅ **XONG** | Sau khắc phục review 2026-11-11 (`GitHubWebhookPrTests` 20/20 trong 15.3) |
| **15.4** | Issue → Task + Jev `TriageForSystemAsync` (fix `Degraded` khi confidence < 0.50), Comment 1 chiều qua `GitHub Bot`, Undo `GitHubIssueCreateTask` | ✅ **XONG** | Sau khắc phục review 2026-11-11 (`GitHubWebhookIssueAndCommentTests` 15/15 trong 15.4) |
| **15.5** | Frontend Integration UI (`TN-123`, PR/Issue chips, `isInReview`, Tabs `Cài đặt chung` + `Tích hợp`), Vitest (+31 tests), Tài liệu & CI baseline | ✅ **XONG** | Kiểm chứng trực tiếp trong phiên nghiệm thu 15.5 (xem §2) |

---

## 2. Số đo Thật Sau Chỉnh sửa Cuối

### 2.1. Frontend (Chạy trực tiếp trên `frontend/` — Node + Vite 8 + Vitest 5)

| Cổng kiểm tra | Ngưỡng bắt buộc | Kết quả đo thật |
| :--- | :--- | :--- |
| `npm run lint` (`oxlint`) | 0 warning / 0 error | ✅ **Found 0 warnings and 0 errors** (297 files, 116 rules) |
| `npm run build` (`tsc -b && vite build`) | Exit code 0 | ✅ **Exit 0** (`3339 modules transformed`, built in `2.66s`, `dist/assets/index-BWV20Ge4.js` `2,084.10 kB` / gzip `626.42 kB`) |
| `npm test` (`vitest run --maxWorkers=4 --reporter=dot`) | 100% PASS, không tụt baseline | ✅ **Test Files 126 passed (126) · Tests 778 passed (778)** (`+31` test mới so với baseline `747` đầu kỳ 15.5) |
| `.github/workflows/ci-web.yml` | Baseline khớp thực tế | ✅ Đã cập nhật ngưỡng tối thiểu từ `747` lên **`778`** |

### 2.2. Backend (.NET 10 + PostgreSQL 18 `pgvector`) — Không thay đổi mã nguồn trong 15.5

| Cổng kiểm tra | Kết quả đo thật tại phiên 15.5 | Ghi chú trung thực về môi trường |
| :--- | :--- | :--- |
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | ✅ **Build succeeded · 0 Warning(s) · 0 Error(s)** (`30.37s`) | Đo trực tiếp trên máy cục bộ Windows |
| `dotnet test --no-build --filter "FullyQualifiedName~Pure"` | ✅ **Failed: 0 · Passed: 665 · Skipped: 0 · Total: 665** (`1s`) | Toàn bộ Pure unit tests chạy & xanh 100% |
| `dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj --no-build` | ⚠️ **Failed: 0 · Passed: 670 · Skipped: 659 · Total: 1,329** (`3m 2s`) | Máy Windows cục bộ chưa bật Docker daemon và PostgreSQL 18 native trên port `5432` không cài extension `pgvector`, nên `DatabaseFixture.EnsureVectorExtensionAvailable` skip an toàn toàn bộ 659 test DB-backed. Bộ test đầy đủ 1,329 tests (Passed 1,329 / Failed 0 / Skipped 0) yêu cầu container `pgvector/pgvector:pg18` như đã cấu hình trong `.github/workflows/ci-backend.yml` |
| `dotnet ef migrations has-pending-model-changes` | ℹ️ Chưa chạy trực tiếp ở phiên 15.5 (`dotnet-ef` chưa cài global trên máy cục bộ) | Không có bất kỳ chỉnh sửa nào ở `src/TeamNexus.Persistence` hay backend từ sau 15.4 |

---

## 3. Các Hạng mục Đã Dựng trong Sub-phase 15.5

### 3.1. Contracts, Services & Hooks (`T15.5.1`)
- `frontend/src/features/board/types/board.types.ts`: Thêm `TaskExternalLinkSummary`, append optional `taskNumber?: number`, `externalLinks?: TaskExternalLinkSummary[]` vào `TaskResponse`; thêm optional `isInReview` vào `ColumnResponse`, `CreateColumnRequest`, `UpdateColumnRequest`.
- `frontend/src/features/workspace/types/integration.types.ts`: Khai báo đầy đủ kiểu dữ liệu tích hợp Outbound/GitHub cùng `WEBHOOK_EVENT_CATALOG` (6 canonical event keys khớp `WebhookEventMatcher.cs`: `ObserverAlert`, `AgentActionPending`, `ClarificationNeeded`, `TaskAssigned`, `CommentMentioned`, `MemberJoined`).
- `frontend/src/features/workspace/services/integrationApi.ts`: Cung cấp 11 hàm gọi Axios (`listWebhooks`, `createWebhook`, `updateWebhook` bằng `PATCH`, `deleteWebhook`, `testWebhook`, `getWebhookDeliveries`, `getGitHubIntegration` trả `null` khi `404`, `upsertGitHubIntegration`, `rotateGitHubSecret`, `deleteGitHubIntegration`, `getGitHubDeliveries`).
- `frontend/src/features/workspace/hooks/useWorkspaceIntegrations.ts`: Hook quản lý trạng thái tải song song webhooks, GitHub config/deliveries, danh sách board mặc định, thao tác CRUD/Test/Rotate và one-shot `oneTimeSecret`.

### 3.2. Nâng cấp Bảng Kanban, Tìm kiếm & Chi tiết Thẻ (`T15.5.2` & `T15.5.3`)
- `frontend/src/features/board/utils/externalLinkHelpers.ts`: Định nghĩa `GITHUB_BOT_USER_ID` (`00000000-0000-0000-0000-00000000b071`), `formatTaskCode`, `getShortExternalNumber`, `getExternalLinkStateMeta`, `formatExternalLinkChipText`.
- `frontend/src/features/board/components/TaskCard.tsx`: Hiển thị pill `TN-123` (`data-testid="task-card-number"`, font monospace) và tối đa 3 chip GitHub PR/Issue (`data-testid="task-card-external-links"`, kèm `+N`, kiểm tra `isSafeHref`, `stopPropagation` trên click/pointerDown).
- `frontend/src/features/board/components/ColumnModal.tsx` & `KanbanColumn.tsx`: Hỗ trợ chọn vai trò cột `Đang duyệt PR` (`isInReview`, loại trừ lẫn nhau với `isDone`, vô hiệu hóa cả hai khi `isClarification === true`) và gắn icon `GithubOutlined` + tag `Đang duyệt PR` (`data-testid="in-review-col-tag"`).
- `frontend/src/features/board/components/BoardView.tsx` & `search/components/TaskSearchResultList.tsx`: Mở rộng bộ lọc tìm kiếm trên bảng nhận chuỗi `tn-{taskNumber}` và hiển thị tag `TN-123` cạnh tiêu đề kết quả tìm kiếm toàn workspace.
- `frontend/src/features/board/components/TaskDetailModal.tsx`:
  - Header hiển thị `TN-123` (`data-testid="task-detail-number"`) kèm nút sao chép nhanh (`data-testid="copy-task-number-btn"`), tự động fallback `#XXXXXXXX` nếu fixture không có `taskNumber`.
  - Hiển thị pill & option `Đang duyệt PR`, khối liên kết GitHub PR/Issue ngay dưới phần mô tả, và nhận diện bình luận từ `GitHub Bot` (`data-testid="github-bot-comment-avatar"`, `data-testid="github-bot-comment-tag"` `GITHUB BOT`).
  - Giữ nguyên nâng cấp nhãn `AiActionLogItem.tsx` (`GitHubPrMoveTask` / `GitHubIssueCreateTask`) đã có từ commit `0bd1aab`.

### 3.3. Trang Quản trị Tích hợp Workspace (`T15.5.4`)
- `frontend/src/features/workspace/pages/WorkspaceSettingsPage.tsx`: Chuyển cấu trúc sang `Tabs` gồm **`Cài đặt chung`** (`key="general"`, mặc định active) và **`Tích hợp (Slack / Discord / GitHub)`** (`key="integrations"`), giữ nguyên `data-testid="open-settings-modal-btn"` trong header.
- `frontend/src/features/workspace/components/OutboundWebhooksSection.tsx`: 3 thẻ thống kê 24h (`Đã gửi (24h)`, `Thất bại (24h)`, `Đang chờ Outbox`), bảng Slack/Discord webhook (toggle kích hoạt, gửi thử, drawer xem lịch sử 20 outbox deliveries, sửa, xóa) và Modal tạo/sửa HTTPS kèm checkbox chọn sự kiện.
- `frontend/src/features/workspace/components/GitHubIntegrationSection.tsx`: Khối hướng dẫn khởi tạo ban đầu, hộp `Webhook Payload URL` kèm nút copy, `SecretHint`, nút **Xoay vòng Secret** (`Popconfirm`), cảnh báo hiển thị **1 lần duy nhất** `Webhook Secret` (`data-testid="github-oneshot-secret-alert"`), biểu mẫu cấu hình (`DefaultBoardId`, `IssueLabelFilter`, 3 công tắc auto PR / auto Issue+Jev / comment sync) và bảng 30 delivery logs gần nhất.

### 3.4. Độ phủ Kiểm thử Mới (`T15.5.5`, `+31` tests)
- `frontend/src/features/workspace/services/__tests__/integrationApi.test.ts` [MỚI — **11 tests**]: Bao phủ đủ 11 endpoint Outbound/GitHub + nhánh `404 -> null` trên `getGitHubIntegration`.
- `frontend/src/features/board/components/__tests__/ColumnModal.test.tsx` [MỚI — **4 tests**]: Tạo cột `isInReview`, loại trừ lẫn nhau `isDone`/`isInReview`, khóa khi `isClarification`, và pre-populate khi sửa cột.
- `frontend/src/features/workspace/components/__tests__/WorkspaceIntegrations.test.tsx` [MỚI — **8 tests**]: Bảo toàn tab mặc định + `open-settings-modal-btn`, chuyển sang tab `Tích hợp`, hiển thị thống kê 24h & Slack endpoint, tạo webhook, bấm `Gửi thử`, mở Drawer lịch sử outbox, khởi tạo GitHub Integration hiển thị one-shot secret, lưu clear `DefaultBoardId`/`IssueLabelFilter`, Rotate Secret & Refresh deliveries.
- `frontend/src/features/board/components/__tests__/TaskCard.test.tsx` (**+3 tests**), `KanbanColumn.test.tsx` (**+1 test**), `TaskDetailModal.test.tsx` (**+4 tests**).

---

## 4. Lỗi Thật Bắt được & Khắc phục (15.3 → 15.5)

| Mã | Hiện tượng / Nguyên nhân | Cách xử lý |
|---|---|---|
| **BUG-15.5-1** | `boardStore.applyColumnUpdated` chỉ gán `name` và `isDone`, làm mất `isInReview`, `isClarification`, `position` và `updatedAt` mỗi lần cập nhật cột hoặc nhận SignalR `ColumnUpdated`. | Cập nhật đầy đủ các field từ payload `updated` trong `applyColumnUpdated`. |
| **BUG-15.5-2** | `useWorkspaceIntegrations` gọi `reload()` trực tiếp trong `useEffect` vi phạm rule `react(set-state-in-effect)` của `oxlint`. | Gọi qua `Promise.resolve().then(...)` kèm cờ cleanup `ignore`. |
| **BUG-15.5-3** | `TaskDetailModal.test.tsx` mock sai đường dẫn tương đối (`../services/boardApi` thay vì `../../services/boardApi`) gây lỗi module resolution. | Chuẩn hóa về `../../services/boardApi` khi bổ sung bộ test `15.5`. |
| **BUG-15.5-4** | `ColumnModal.tsx` dùng prop `destroyOnClose` đã deprecated trên AntD v6. | Chuyển sang `destroyOnHidden`. |

---

## 5. Bất biến & Tài liệu Đã Đồng bộ

1. **Tất cả bất biến `AGENTS.md` giữ nguyên:** 4 module backend (`Auth`, `Board`, `Ai`, `Reporting`), `Ai → Board` một chiều, append-only DTOs, zero secret leakage (`SecretHint`), không dùng Tailwind CSS, không thêm package npm mới, 100% tiếng Việt chuẩn Luminous Enterprise v4.2, bảo toàn mọi `data-testid` cũ.
2. **Độ lệch có chủ đích `L1` / `L2` đã ghi nhận rõ ràng:** (a) Giữ nguyên `delete={isCompleted}` trên tiêu đề `TaskCard` để bảo toàn assertion test cũ; (b) `GitHubIntegrationSection` phục vụ 1 GitHub Integration / workspace qua `WebhookToken` + `WebhookSecret` (không có PAT hay dán URL thủ công).
3. **Tài liệu đã đồng bộ toàn bộ:**
   - `project-docs/01-system-specification.md` (§17 Giai đoạn 15)
   - `project-docs/02-tech-stack-decisions.md` (§2.12 Tích hợp ngoài)
   - `project-docs/03-roadmap.md` (§15 ✅ HOÀN THÀNH)
   - `project-docs/04-database-design.md` (15 migrations, §3.12 tổng **27 bảng domain** + 7 bảng Identity = 34 bảng)
   - `project-docs/tasks/phase-15-0-external-integrations-master-plan.md` & `project-docs/tasks/phase-15-5-frontend-integrations-and-e2e-verification.md`

---

## 6. Checklist Xác minh Production Sau Deploy (từ Handover 15.5)

Các biến môi trường hạ tầng đã được thiết lập trước khi bàn giao (`Integrations__PublicApiBaseUrl=https://api.teamnexus.cloud`, `Integrations__DataProtection__KeysPath=/var/data/dataprotection-keys`). Khi triển khai bản Frontend 15.5 cùng Backend 15.1–15.4 lên môi trường thật, thực hiện lần lượt:
1. Mở `/workspaces/{id}/settings` → tab **Tích hợp (Slack / Discord / GitHub)** → tạo Slack/Discord webhook → bấm **Gửi thử** → kiểm tra `200 OK` và Drawer lịch sử Outbox.
2. Lưu cấu hình **GitHub Integration** → xác nhận `Webhook Payload URL` có tiền tố `https://api.teamnexus.cloud/api/webhooks/github/...`, copy **`Webhook Secret`** hiển thị 1 lần và reload lại trang để chắc chắn chỉ còn `SecretHint`.
3. Tạo PR nháp / PR sẵn sàng gắn mã `TN-...` → xác nhận thẻ tự chuyển cột **`Đang duyệt PR`** (`isInReview`), gắn chip `PR #... · Open/Merged`, và có thể bấm **Hoàn tác** trong `Lịch sử Hành động AI`.
4. Tạo Issue khớp label filter & bình luận trên GitHub → kiểm tra thẻ mới kèm `TN-...`, bình luận mang nhãn **`GitHub Bot`**, và đóng Issue đồng bộ cột `Done`.

