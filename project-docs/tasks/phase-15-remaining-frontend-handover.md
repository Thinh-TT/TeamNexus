# Bàn giao Phase 15 — Trạng thái sau 15.3/15.4 + Khắc phục Review (dành cho phiên sau)

> **Cập nhật lần cuối:** 2026-11-11 (sau đợt review + khắc phục; người dùng đã đặt biến môi trường deploy).
> **Nhánh hiện tại:** `develop` @ `8891bac` (local đang ahead `origin/develop`, **chưa push** — xem `git log --oneline origin/develop..develop`).
> **Phạm vi:** Phase 15.1 → 15.4 đã xong; **15.5 (UI + E2E) là việc tiếp theo**.
> **Đọc kèm:** [`phase-15-0`](./phase-15-0-external-integrations-master-plan.md), [`phase-15-3`](./phase-15-3-inbound-github-webhooks-and-pr-automation.md), [`phase-15-4`](./phase-15-4-github-issues-jev-triage-and-comments.md), [`phase-15-5`](./phase-15-5-frontend-integrations-and-e2e-verification.md), [báo cáo review](../report/phase-15-3-15-4-code-review-and-remediation-plan.md).

---

## 1. Tình trạng hiện tại (một bảng)

| Hạng mục | Trạng thái |
| :--- | :--- |
| 15.1 Migration #15 + `TN-123` + `ITaskAutomation` | ✅ Xong (đã merge từ trước) |
| 15.2 Slack/Discord outbound + outbox | ✅ Xong (đã merge từ trước) |
| 15.3 Inbound GitHub webhook + PR State Machine + Undo | ✅ Xong, **20/20** test sau khắc phục |
| 15.4 Issue → Task + Jev Triage + Comment sync | ✅ Xong, **15/15** test sau khắc phục |
| Khắc phục review R1–R4 (14 lỗi F1–F16) | ✅ Xong — xem §3 |
| 15.5 Frontend (badge `TN-123`, chip PR, tab Tích hợp, E2E) | ⏳ **Chưa bắt đầu** — xem §5 |
| Migration | Đúng **1 migration** Phase 15 (`Phase15ExternalIntegrations`), không drift |
| Cổng kiểm thử | Build `0/0`; backend **1.329 passed / 0 failed / 0 skipped**; frontend **747 passed / 123 files**; Pure **665** |
| Git | `develop` ahead `origin/develop` **36 commit**; `backup/main-before-reset-26382b6` đã merge, không còn file rác `sqlite_mcp_server.db` |

---

## 2. Cấu hình triển khai đã đặt & cách xác minh

Người dùng đã khai báo trên môi trường deploy (2026-11-11):

| Biến môi trường | Giá trị | Tác dụng |
| :--- | :--- | :--- |
| `Integrations__PublicApiBaseUrl` | `https://api.teamnexus.cloud` | Dựng `WebhookPayloadUrl` trả cho UI; tránh rơi về host SPA `app.teamnexus.cloud` (Vercel rewrite về `index.html`) |
| `Integrations__DataProtection__KeysPath` | `/var/data/dataprotection-keys` | Key ring bền cho `GitHubSecretProtector`; `/var/data` phải là **persistent disk** của Render/VPS |

`Program.cs` (`8891bac`) cho phép **dùng đồng thời** `KeysPath` (độ bền) và `CertPath`/`CertPassword` (mã hoá key ring at-rest). Render hiện chỉ có `KeysPath` ⇒ key ring lưu **không mã hoá at-rest** (log sẽ có cảnh báo *"No XML encryptor configured"*); VPS nên đặt thêm `CertPath` + `CertPassword` và mount cả hai đường dẫn.

**Checklist xác minh sau deploy/merge (làm 1 lần):**
1. `GET https://api.teamnexus.cloud/api/health` → `200`.
2. `GET /api/workspaces/{id}/integrations/github` (Manager) → `webhookPayloadUrl` bắt đầu bằng `https://api.teamnexus.cloud/api/webhooks/github/`. Nếu vẫn là `app.teamnexus.cloud` ⇒ biến env chưa được nạp (kiểm tra restart service).
3. Bấm **Ping** trên GitHub (hoặc gửi `ping`) → `200`, delivery `Processed`.
4. **Restart/redeploy** service → gửi `ping` lần nữa: vẫn `200` ⇒ key ring bền đúng; nếu `401` ⇒ `KeysPath` không nằm trên volume bền.
5. Kiểm tra `ls /var/data/dataprotection-keys/key-*.xml` có file sau lần dùng đầu.
6. (Tuỳ chọn) Sai chữ ký → `401`; payload > 256 KB → `413`; xoá board mặc định rồi gửi `issues` → `502` + delivery `Failed`.

> **Lưu ý Render free-tier:** GitHub không tự retry khi service ngủ (đã ghi ở thiết kế §13.2). Delivery `Failed` giờ trả `502` để GitHub coi là thất bại và có thể redeliver thủ công.

---

## 3. Đợt khắc phục review — đã làm gì (commit map)

| Commit | Nội dung |
| :--- | :--- |
| `2087e24` | Merge nhánh review; xoá `sqlite_mcp_server.db`; thêm `.gitignore` cho artifact MCP |
| `3fa1bc9` | **R1** — sửa test Undo (stale `DbContext`); `Integrations:PublicApiBaseUrl` + resolver; key ring `KeysPath`/`CertPath` + cảnh báo production |
| `a32b35f` | **R2** — `Degraded` dùng chung (`Usage is null`); auto-link chỉ `TN-`; SignalR `TaskUpdated` khi issue đóng/mở; audit log trước Jev (fail-soft); Undo khôi phục vị trí/`CompletedAt` |
| `6595eb1` | **R3** — body limit 256 KB khi đọc (`413`); `Failed` → `502`; validate `IssueLabelFilter` (`400`); guard `sender`/`labels` |
| `0bd1aab` | **R4** — nhãn tiếng Việt cho action GitHub + basis GitHub + ẩn "0 tasks" trong `AiActionLogItem` |
| `a083d5d` | Cập nhật 15.0/15.3/15.4/roadmap + nâng CI baseline **1.329 / 747** + báo cáo review |
| `8891bac` | (Người dùng) `KeysPath` và `CertPath` độc lập, có thể bật đồng thời |

Chi tiết từng lỗi (bằng chứng file:line, cách sửa, test): [`report/phase-15-3-15-4-code-review-and-remediation-plan.md`](../report/phase-15-3-15-4-code-review-and-remediation-plan.md) §4 + §9.

---

## 4. Hợp đồng API/UI đã sẵn sàng (không cần sửa backend cho 15.5)

| Backend | Dùng cho UI |
| :--- | :--- |
| `GET/PUT /api/workspaces/{id}/integrations/github`, `POST .../rotate-secret`, `DELETE` | Form GitHub: `isEnabled`, `defaultBoardId`, `issueLabelFilter`, 3 công tắc; `secretHint`; `webhookSecretPlaintext` chỉ 1 lần |
| `GET .../integrations/github/deliveries?take=30` | Bảng delivery: `deliveryId`, `eventName`, `action`, `repositoryFullName`, `status`, `summary`, `receivedAt` |
| `GET/POST/PUT/DELETE .../integrations/webhooks`, `POST .../test`, `GET .../deliveries` | Tab Slack/Discord (15.2) |
| `TaskResponse.taskNumber`, `.externalLinks[]` | Pill `TN-123`, chip `PR #88 · Merged/Open/Draft/Closed` |
| `ColumnResponse.isInReview` | Tag "Đang duyệt PR" trên cột + form cột |
| `AiActionLogItem` | Đã có nhãn/basis cho 2 action GitHub (đừng làm lại) |

Bẫy cần giữ: auto-link chỉ `TN-…`; `Failed` trả `502`; giữ nguyên `data-testid`; DTO append-only.

---

## 5. Việc tiếp theo — 15.5 (đọc [`phase-15-5`](./phase-15-5-frontend-integrations-and-e2e-verification.md))

1. **T15.5.1** — `integration.types.ts`, `integrationApi.ts`, `useWorkspaceIntegrations.ts`; thêm `isInReview`, `taskNumber`, `externalLinks` vào `board.types.ts`.
2. **T15.5.2** — pill `TN-123` + chip GitHub trên `TaskCard`; tag `Đang duyệt PR` trên `KanbanColumn`.
3. **T15.5.3** — `TaskDetailModal`: `TN-123` + Copy, khối liên kết PR/Issue, badge `GITHUB BOT` cho comment. **Nhãn `AiActionLogItem` đã xong** — chỉ giữ nguyên.
4. **T15.5.4** — tab `Cài đặt chung` / `Tích hợp` trong `WorkspaceSettingsPage`; `OutboundWebhooksSection` + `GitHubIntegrationSection`.
5. **T15.5.5** — Vitest mới (≥ 20), chạy đủ cổng, cập nhật `01`, `03`, `04`, tạo `report/phase-15-external-integrations-test-report.md`.

**Trước khi code phiên sau:** chạy lại 4 cổng dưới đây để xác nhận baseline (không tin số liệu cũ nếu máy khác/DB khác):

```powershell
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental   # 0/0
$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj   # 1.329 / 0 / 0
cd frontend; npm run lint; npx tsc -b; npm test                      # 0/0; exit 0; 747
```

DB test phải là `pgvector/pgvector:pg18` (container `:5433` trong phiên này); thiếu nó ⇒ 35 test GitHub bị **skip** và cổng "Skipped = 0" sai.

---

## 6. Rủi ro & quyết định còn mở

1. **Mô hình 1 tích hợp GitHub / workspace** (không có bảng repo/PAT, không liên kết URL thủ công) — hệ quả schema 15.1, đã ghi `L2`; đa repo/PAT để Phase 16.
2. **Chưa allow-list repo** — token+secret là capability; repo nào giữ đúng token đều điều khiển được workspace.
3. **Dedupe Issue chưa tuyệt đối khi 2 delivery song song** — unique index hiện `(task_id, provider, link_type, external_id)`; đề xuất Phase 16: `(workspace_id, provider, link_type, external_id)`.
4. **Key ring at-rest chưa mã hoá trên Render** — chấp nhận nếu volume được bảo vệ; VPS nên đặt `CertPath`.
5. **Render free-tier ngủ** ⇒ có thể mất event GitHub (không tự retry).
6. **Push/PR:** `develop` chưa push (`ahead 36`). Người dùng đang chuẩn bị push + PR + merge; không có thay đổi cục bộ nào chưa commit.
7. **CI baseline** đã nâng lên `1.329`/`747`; nếu bỏ test nào vì lý do chính đáng thì phải hạ baseline kèm lý do trong PR.

---

## 7. Nhật ký phiên (2026-11-11)

- Review nhánh `backup/main-before-reset-26382b6`: phát hiện 1 test đỏ + 3 lỗi P1 + 6 lỗi P2 + 7 P3; lập kế hoạch R1–R4.
- Thi hành đủ 4 bước trên nhánh `fix/phase15-review`, merge fast-forward vào `develop`.
- Số đo cuối: backend **1.329/0/0**, Pure **665**, frontend **747**, build `0/0`, drift sạch.
- Ghi chú trung thực: một lần chạy Vitest đầu tiên có **1 test flake**, chạy lại toàn bộ xanh `747/747` (không tái hiện); không xác định được tên test do reporter lần đó không in ra.
