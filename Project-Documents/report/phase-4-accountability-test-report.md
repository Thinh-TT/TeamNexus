# BÁO CÁO KIỂM THỬ GIAI ĐOẠN 4: ACCOUNTABILITY LAYER (BACKEND)

**Dự án:** TeamNexus – AI-Powered Collaborative Workspace
**Giai đoạn:** Phase 4 – Accountability Layer (§1 Schema, §2 `AiActionService`, §3 Endpoints, §5.1 Verify backend)
**Đối tượng kiểm thử:** `ai_action_logs`, `AiActionService` + `CreateSubtasksApplier`, 6 endpoint Accountability Layer
**Ngày thực hiện:** 10/09/2026
**Người thực hiện:** DSH coding agent (backend §1–§3 + §5.1); frontend §4/§5.2 bàn giao antigravity
**Môi trường thực thi:**
- Backend: ASP.NET Core (.NET 10) chạy tại `http://127.0.0.1:5196`, `ASPNETCORE_ENVIRONMENT=Development`
- AI provider: `FakeAiProvider` (biến môi trường `DeepSeek__ApiKey` để trắng ⇒ `HasApiKey=false`) — **không gọi DeepSeek thật, không tốn token**
- CSDL: PostgreSQL local (`localhost:5432`, DB `TeamNexus`), migration `Phase4AccountabilityLayer` (`20260910073621`)
- Xác thực khi test: JWT tự ký bằng chính `JwtService` (key đọc từ User Secrets `Jwt:SigningKey`), đặt trong cookie `access_token`; CSRF lấy từ cookie `XSRF-TOKEN` sau `GET /api/auth/antiforgery`
- Real-time: **SignalR client thật** (`Microsoft.AspNetCore.SignalR.Client` 8.0.24) join group `board-{boardId}` qua `/hubs/board`

---

## 1. Mục Tiêu & Tiêu Chí Nghiệm Thu

1. **Schema đúng thiết kế** (`04-database-design.md` §3.5): 15 cột, 4 cột `jsonb`, CHECK trên `status`, index theo board + index FK.
2. **"Log trước – ghi sau"**: `confirm` chỉ sinh 1 row `ai_action_logs` (`Pending`), **không** chạm `tasks`/`labels`/`task_labels`; chỉ `approve` mới ghi dữ liệu thật.
3. **`AiActionService` là cổng duy nhất**: không còn đường ghi `Tasks`/`Labels`/`TaskLabels` nào khác trong module Ai.
4. **Vòng đời & idempotency**: `Pending → Approved → Undone`, `Pending → Rejected`; mọi chuyển trạng thái sai → **409**, không tạo dữ liệu trùng.
5. **Undo thật**: soft-delete task do action tạo, dọn `task_labels`, xử lý nhãn đúng (xoá nếu không còn tham chiếu / giữ + cảnh báo nếu còn), có broadcast real-time.
6. **Hợp đồng HTTP**: đủ mã 400/401/403/404/405/409, body lỗi `{ error }`, JSON camelCase, `Location` header khi 201.

---

## 2. Phương Pháp Kiểm Thử

| Lớp | Công cụ | Phạm vi | Kết quả |
|---|---|---|---|
| §1 Schema | Harness Npgsql tạm (`information_schema`, `pg_constraint`, `pg_indexes`) + insert/read/delete round-trip qua EF | Cấu trúc bảng, ép kiểu `jsonb`, CHECK, FK RESTRICT | **33/33 PASS** |
| §2 Service | Harness DI thật (`AddTeamNexusPersistence` + `AddBoardModule` + `AddAiModule`), gọi thẳng `IAiActionService` | Validate, lifecycle, CAS, rollback, undo, list | **83/83 PASS** |
| §3 Endpoint | Probe HTTP thật (cookie `access_token` + `X-XSRF-TOKEN`) | Routing, auth/CSRF, mã lỗi, JSON casing, 405 | **36/36 PASS** |
| **§5.1 End-to-end** | API thật + PostgreSQL thật + **SignalR client thật**, fixture tự tạo rồi hard-delete | Ma trận row DB ở từng bước + sự kiện real-time | **54/54 PASS** ✅ |

> Tất cả harness nằm **ngoài workspace** (`%TEMP%`), **đã xoá** sau khi chạy; DB dev được trả về đúng baseline (số row `ai_action_logs` trước/sau bằng nhau); API test **đã stop** (cổng 5196 đóng).

---

## 3. Kết Quả §5.1 — Chi Tiết 54 Check

### 3.1 Nhóm A — Schema & Migration (4 check)

| Mã | Nội dung | Kết quả | Bằng chứng |
|---|---|---|---|
| A1 | 15 cột đúng thứ tự thiết kế | ✅ | `id, action, entity_type, entity_id, basis, before_snapshot, after_snapshot, applied_snapshot, status, requested_by_user_id, decided_by_user_id, decided_at, decision_note, created_at, updated_at` |
| A2 | 4 cột `jsonb` | ✅ | `information_schema` đếm `data_type='jsonb'` = 4 |
| A3 | CHECK `ck_ai_action_logs_status` | ✅ | `pg_constraint` có đúng tên + 4 giá trị hợp lệ |
| A4 | 3 index (board-history + 2 FK) | ✅ | `ix_ai_action_logs_entity_type_entity_id_created_at`, `…_requested_by_user_id`, `…_decided_by_user_id` |

### 3.2 Nhóm B — Hàm thuần (validate & JSON) (9 check)

| Mã | Nội dung | Kết quả |
|---|---|---|
| B1 | `description` rỗng / 4001 ký tự bị từ chối; 4000 hợp lệ | ✅ |
| B2 | `tasks` rỗng / 21 task bị từ chối; 20 hợp lệ (trần `MaxTaskCount`) | ✅ |
| B3 | `title` rỗng / 201 ký tự bị từ chối; 200 hợp lệ | ✅ |
| B4 | priority `"urgent"` chấp nhận (case-insensitive), `"Critical"` → 400 | ✅ |
| B5 | 6 nhãn / nhãn 81 ký tự → 400 | ✅ |
| B6 | `labelId` khác workspace / assignee ngoài member / `columnId` khác board → 400 | ✅ |
| B7 | phần tử task `null` → 400 | ✅ |
| B8 | `BuildBasis`: `descriptionLength` đúng, excerpt ≤ 300 ký tự, đủ member/column/task count | ✅ |
| B9 | `BuildAppliedSnapshotJson` round-trip id; `MergeUndoWarnings` giữ key cũ + thêm `undoWarnings` | ✅ |

### 3.3 Nhóm C — End-to-end HTTP + DB + SignalR (39 check)

| Mã | Kịch bản | Kết quả | Bằng chứng định lượng |
|---|---|---|---|
| C0 | SignalR client kết nối + `JoinBoard` thành công (không `HubException`) | ✅ | `State=Connected` |
| C1 | Bắt tay antiforgery (`XSRF-TOKEN`) | ✅ | 204 + cookie có giá trị |
| C2–C3 | `confirm` thiếu CSRF → 403; thiếu auth → 401 (authorization chạy trước filter) | ✅ | 403 / 401 |
| C4, C4b | `title` rỗng → 400 và **không** sinh row log nào | ✅ | `count(ai_action_logs)=0` |
| C5–C8 | 21 task / `labelId` khác workspace / assignee ngoài member / `columnId` khác board → 400 | ✅ | 400 ×4 |
| C9–C10 | Board thuộc workspace role `Member` → confirm **403**, list **403** | ✅ | 403 / 403 (dùng user thật) |
| C11–C12 | Board không tồn tại → 404; log không tồn tại → 404 | ✅ | 404 / 404 |
| C13, C13b | `confirm` → **201 Pending**; `tasks`/`labels`/`task_labels` **không đổi** (1/1/1); `basis` object + `after_snapshot->'tasks'` = 3 | ✅ | row matrix giữ nguyên trước/sau |
| C14 | `approve` → **200 Approved**; `applied_snapshot.createdLabelIds` = 1; `decided_by_user_id` + `decided_at` có giá trị | ✅ | 200 + kiểm tra SQL |
| C15 | 3 task tạo **đúng cột** đích, `position ≥ 1` (nối cuối cột), priority `High` + description đúng | ✅ | active tasks 1 → 4 |
| C16 | Nhãn `exists=false` được tạo + gắn: `labels` 1→2, `task_labels` 1→3 (tái dùng nhãn có sẵn + 1 nhãn mới) | ✅ | 2 / 3 |
| C17 | `assignee_id` áp đúng cho assignee còn là member | ✅ | 1 task mang `assignee_id` |
| C18 | **SignalR nhận đúng 3 `TaskCreated`** | ✅ | `got 3` |
| C19 | `approve` lần 2 → **409**, số task không tăng | ✅ | 409 + active = 4 |
| C20 | (setup) gắn nhãn do action tạo vào 1 task "người" → nhãn trở thành **còn được dùng** | ✅ | 204 |
| C21 | `undo` → **200 Undone**; task action bị soft-delete (active 4→1, tổng vẫn 4); `task_labels` của chúng bị xoá (3→2) | ✅ | active=1, links=2 |
| C22 | Nhãn còn tham chiếu ⇒ **giữ lại** + `undoWarnings` ≥ 1 | ✅ | labels=2, warnings ≥1 |
| C23 | **SignalR nhận đúng 3 `TaskDeleted`** | ✅ | `got 3` |
| C24 | `undo` lần 2 → 409; `approve` sau `undo` → 409 | ✅ | 409 / 409 |
| C25 | Log #2 (nhãn mới **không** còn ai dùng): `undo` → nhãn bị **xoá** (3→2) và `undoWarnings` = 0 | ✅ | labels 3→2 |
| C26 | `reject` + note → **200 Rejected** + `decision_note` + `decided_at`; không ghi task nào | ✅ | active không đổi |
| C27 | `reject` lần 2 / `approve` sau reject / `undo` sau reject → **409** ×3 | ✅ | 409/409/409 |
| C28–C29 | Assignee là Manager **rời workspace** sau confirm ⇒ `approve` → 200, task tạo **không gán** (3 task `assignee_id IS NULL`) + `warnings` ≥ 1 | ✅ | 200 + warnings ≥1 |
| C30 | Board **0 cột** khi approve → **400**, log **vẫn `Pending`**, 0 task | ✅ | 400 + `Pending` |
| C31 | List board 1 → 200, **4 log**, sort `createdAt DESC` | ✅ | count=4 |
| C32–C33 | `?status=Pending` (board 3) → 1; `?status=Rejected` (board 1) → 1 | ✅ | 1 / 1 |
| C34 | `take=1000` bị clamp; `take=0` về mặc định | ✅ | 4 / 4 |
| C35 | `taskCount` trong list khớp `after_snapshot->'tasks'` (=3) | ✅ | taskCount=3 |
| C36 | Detail trả đủ `basis`/`afterSnapshot`/`appliedSnapshot` object, `beforeSnapshot` null, `createdTaskIds` = 3 | ✅ | 200 + shape |
| — | Cleanup: log/task/workspace/user fixture **đã xoá**, `ai_action_logs` về baseline | ✅ | 2 check |

---

## 4. Kiểm Tra Kiến Trúc "Cổng Duy Nhất"

```
grep -E "_db\.(Tasks|Labels|TaskLabels)\.(Add|AddRange|Remove|RemoveRange)|_db\.AiActionLogs\.Add" src/Modules/Ai
→ AiActionService.cs:122      _db.AiActionLogs.Add(log);              # chỉ ghi log (Pending)
→ Appliers/CreateSubtasksApplier.cs:152  _db.TaskLabels.RemoveRange(links);  # dọn junction khi Undo
```

Kết luận: trong module Ai **chỉ** có 1 chỗ ghi dữ liệu nghiệp vụ (`task_labels` khi undo) và 1 chỗ ghi log. Mọi write task/nhãn khác đều đi qua service Board (`ITaskService`, `ILabelService`) — đúng yêu cầu roadmap "mọi hành động AI ghi dữ liệu phải đi qua `AiActionService`".

---

## 5. Vấn Đề Phát Hiện & Cách Xử Lý Trong Quá Trình Verify

| # | Hiện tượng | Nguyên nhân | Xử lý |
|---|---|---|---|
| 5.1 | Round-trip `jsonb` **không** byte-identical (`{"a":"b"}` đọc lại thành `{"a": "b"}` và key bị xếp lại) | PostgreSQL chuẩn hoá `jsonb` (thứ tự key/khoảng trắng) | Không so sánh snapshot bằng string; dùng `JsonElement.DeepEquals`/parse. Đã ghi vào §1.2 + §2.5 của task doc; **frontend cũng phải tuân theo** (chỉ render pretty JSON) |
| 5.2 | `jsonb_array_length(applied_snapshot->'undoWarnings')` trả **NULL** khi key vắng mặt | `->` trả NULL cho key không tồn tại | Bọc `COALESCE(..., 0)` trong test; **UI phải đọc `undoWarnings` optional** |
| 5.3 | `GET …/ai-actions?take=abc` trả **400 ProblemDetails** (không có `error`) | Binding của framework (`int?`) | Ghi nhận là khác biệt contract (đã ghi §3.3); UI chỉ cần xử lý theo status 400 |
| 5.4 | `?status=2` bị từ chối 400 | `ParseStatus` chỉ nhận **tên** enum (tránh nhận nhầm giá trị số) | Giữ nguyên (có chủ ý); UI gửi tên canonical |
| 5.5 | Hai route group cùng prefix `/api/boards/{id}/smart-setup` (`/` và `/confirm`) | Tách "đề xuất (không ghi)" khỏi "ghi qua Accountability Layer" | Đã verify **không ambiguous**: `POST /smart-setup` (generate) vẫn 200 với Fake provider |
| 5.6 | `Board` (entity) bị namespace `TeamNexus.Modules.Board` che trong `AiActionService` | Trùng tên namespace/type | Dùng alias `BoardEntity` (đúng pattern `TaskService`/`ColumnService`) |
| 5.7 | Undo ban đầu "tưởng" nhãn không còn tham chiếu dù task khác đang dùng | Query filter của `TaskLabelConfiguration` **ẩn** junction của task đã soft-delete | Bắt buộc `IgnoreQueryFilters()` khi đếm/dọn `task_labels` (đã ghi §2.4) — verify bằng C21/C22/C25 |

---

## 6. Kết Luận Backend Giai Đoạn 4

- ✅ `ai_action_logs` + migration `Phase4AccountabilityLayer` đúng thiết kế, đã áp lên PostgreSQL local.
- ✅ `AiActionService` là cổng duy nhất: `confirm` **không** ghi dữ liệu thật; `approve` mới ghi qua `ITaskService`/`ILabelService`; `reject`/`undo` là chuyển trạng thái có CAS (chống double-decision 409).
- ✅ Undo hoạt động thật cho `CreateSubtasks` (soft-delete + dọn junction + xử lý nhãn theo tham chiếu), có broadcast `TaskCreated`/`TaskDeleted` cho client thứ 2.
- ✅ 6 endpoint trả đúng mã (201/200/400/401/403/404/405/409) và JSON camelCase; rollback giữ log `Pending` khi apply lỗi.
- ✅ **54/54 check PASS** ở lớp end-to-end (§5.1), cộng thêm 33 (schema) + 83 (service) + 36 (endpoint) check đã chạy ở các bước trước.

**Còn lại của Giai đoạn 4:** §4 frontend (`src/features/ai/`) + §5.2 test Vitest — bàn giao **antigravity**, theo note bàn giao trong `Project-Documents/tasks/phase-4-accountability-layer.md` §4.

---

## 7. Phần UI — Bổ Sung Sau §4 (placeholder)

> Antigravity bổ sung sau khi hoàn thành frontend §4 + §5.2:
> - Ảnh chụp `SmartSetupModal` bước 3 (log `Pending` + nút Duyệt/Từ chối/Hoàn tác).
> - Ảnh chụp `AiActionHistoryDrawer` (4 trạng thái + badge pending ở top bar `BoardView`).
> - Ảnh chụp board sau khi `approve` (task mới xuất hiện realtime) và sau `undo` (task biến mất).
> - Kết quả `npm run lint` / `tsc -b` / `vite build` / `vitest run`.
