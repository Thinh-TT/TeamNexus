# Đánh giá Nhánh `origin/SNHau` — Sub-phase 15.5 (Frontend Integrations) & Kế hoạch Chỉnh sửa

> **Ngày review:** 2026-11-11
> **Nhánh review:** `origin/SNHau` @ `2d2b520` — *feat: implement frontend webhooks integration and GitHub linkage for Kanban tasks and workspace settings*
> **Base:** `develop` @ `4c929d5` (sau đợt khắc phục review 15.3/15.4 + bàn giao). `develop` là **ancestor** ⇒ merge `--ff-only` được.
> **Phạm vi diff:** 29 files, **+3.679 / −121** dòng; **0 thay đổi backend** (`git diff develop...HEAD -- src tests` rỗng), không thêm npm package, không migration.
> **Đối chiếu:** [`phase-15-5`](../tasks/phase-15-5-frontend-integrations-and-e2e-verification.md), [`phase-15-0`](../tasks/phase-15-0-external-integrations-master-plan.md), [`AGENTS.md`](../../AGENTS.md), [báo cáo 15.5 do dev viết](../report/phase-15-external-integrations-test-report.md).

---

## 1. Kết luận Điều hành

**Có thể merge.** Không có lỗi P0/P1: build sạch, drift sạch, toàn bộ backend **1.329 pass**, frontend **786/786 test (127 file)**, lint/tsc/build xanh; API client khớp đúng endpoint/DTO backend, RBAC/CSRF/secret one-shot đều đúng, không làm yếu test cũ.

Có **3 lỗi P2 nên sửa trước khi merge** (đều nhỏ, ~0,5 ngày công) và 8 điểm P3 nên xử lý trong PR kế tiếp:

| Mức | Số lượng | Ý nghĩa |
| :--- | :---: | :--- |
| **P0** | 0 | Không có lỗi chặn build/test/bảo mật |
| **P1** | 0 | Không có lỗi sai chức năng nghiêm trọng |
| **P2** | 3 | Sai lệch hành vi/độ tin cậy tài liệu, nên sửa trước merge |
| **P3** | 8 | Gia cố/UX/nhất quán, có thể follow-up |

---

## 2. Kết quả Đo lường Thực tế (chạy lại trên máy review)

| Cổng | Lệnh | Kết quả | Đánh giá |
| :--- | :--- | :--- | :---: |
| Backend build | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | **0 Warning / 0 Error** (59 s) | ✅ |
| EF Core drift | `dotnet ef migrations has-pending-model-changes …` | `No changes have been made to the model since the last migration.` | ✅ |
| Backend full | `dotnet test … --no-build` với `TEAMNEXUS_TEST_DB` (pgvector `:5433`) | **1.329 passed / 0 failed / 0 skipped** (7 m 03 s) | ✅ |
| Frontend lint | `npm run lint` (`oxlint`) | **0 warning / 0 error** (299 file) | ✅ |
| Typecheck | `npx tsc -b` | exit **0** | ✅ |
| Vitest | `npx vitest run` (JSON reporter) | **127 file / 786 test pass / 0 fail** | ✅ |
| Production build | `npm run build` | exit **0** (3.340 module; cảnh báo chunk > 500 kB — có từ trước, không chặn) | ✅ |
| CI baseline | `.github/workflows/ci-web.yml` | đã nâng `747 → 786`, khớp số đo | ✅ |

**Đối chiếu số liệu dev báo cáo:** khớp chính xác — `747 + 39 = 786` test (8 test mới sau đợt chỉnh sửa); `+4` file test mới (tổng 127); lint 299 file; build 3.340 module. Phần backend dev ghi trung thực là máy họ thiếu `pgvector` nên skip 659 test; trên môi trường review có container `pgvector/pgvector:pg18`, **toàn bộ 1.329 test xanh, 0 skip**.

---

## 3. Tuân thủ Bất biến & Chất lượng

| Hạng mục | Kết quả | Ghi chú |
| :--- | :---: | :--- |
| Không sửa backend / 4 module / Không migration | ✅ | `src/`, `tests/` không đổi; 15 migration giữ nguyên |
| Không thêm npm package / không Tailwind / không `dangerouslySetInnerHTML` | ✅ | Chỉ dùng Ant Design + inline style; markdown render bằng React element |
| API client khớp backend | ✅ | Đã đối chiếu 11 route (`/integrations/webhooks…`, `/integrations/github…`), DTO camelCase (`maskedTargetUrl`, `subscribedEvents`, `webhookPayloadUrl`…), `PATCH` đúng |
| Event catalog khớp `WebhookEventMatcher` | ✅ | 6 canonical key + `TestPing` |
| RBAC | ✅ | Trang Settings đã chặn non-manager bằng `Result 403` từ trước; hook chỉ chạy khi `isManagerOrAdmin` |
| CSRF | ✅ | Mọi mutation đi qua `httpClient` (interceptor tự gắn `X-XSRF-TOKEN` cho POST/PUT/PATCH/DELETE) |
| Zero secret leakage | ✅ | GET chỉ trả `secretHint`; plaintext chỉ ở response tạo/xoay + alert one-shot; test xác nhận |
| `data-testid` cũ | ✅ | `open-settings-modal-btn` được test khẳng định; không xoá/đổi test cũ nào |
| Append-only DTO | ✅ | Chỉ thêm field optional phía frontend; backend DTO không đổi |
| Copy tiếng Việt / nowrap | ✅ | Ngoại lệ hợp lý: nhãn trạng thái `Open/Merged/Draft/Closed` theo đúng kế hoạch 15.5 |
| Test không bị làm yếu | ✅ | Diff các test cũ chỉ thêm assertion mới; sửa đúng 1 mock path sai (`BUG-15.5-3`) |

---

## 4. Danh sách Lỗi & Vấn đề

### F1 — **[P2] Báo cáo 15.5 khai báo có test "clear `DefaultBoardId`/`IssueLabelFilter`" nhưng test không tồn tại**

- **Bằng chứng:** [`phase-15-external-integrations-test-report.md:74`](../report/phase-15-external-integrations-test-report.md) ghi *"…lưu clear `DefaultBoardId`/`IssueLabelFilter`, Rotate Secret & Refresh deliveries"*. Đọc hết 8 test trong [`WorkspaceIntegrations.test.tsx`](../../frontend/src/features/workspace/components/__tests__/WorkspaceIntegrations.test.tsx) **không có test nào** xoá board/nhãn — test "saves updated GitHub configuration" chỉ đổi label filter sang `backend, urgent` và giữ `b-1`.
- **Tác động:** (a) hành vi quan trọng `EMPTY_GUID`/`""` (K1 của 15.5) không được khoá bằng test, dễ regress; (b) báo cáo nghiệm thu không trung thực về độ phủ — vi phạm quy tắc "không làm giả số đo" của `AGENTS.md`.
- **Sửa:** thêm 2 test:
  1. Xoá board (`allowClear`) ⇒ `upsertGitHubIntegration` nhận `defaultBoardId: '00000000-0000-0000-0000-000000000000'`.
  2. Xoá label filter ⇒ nhận `issueLabelFilter: ''`.
  3. (tuỳ chọn) test nút **Xoá cấu hình GitHub** gọi `deleteGitHubIntegration` và quay về trạng thái "Chưa kích hoạt".
  4. Sửa lại câu trong report cho khớp thực tế sau khi thêm test.

### F2 — **[P2] Nút Copy báo "thành công" dù không copy được gì khi `navigator.clipboard` không khả dụng**

- **Bằng chứng:** [`TaskDetailModal.tsx:367`](../../frontend/src/features/board/components/TaskDetailModal.tsx) và [`GitHubIntegrationSection.tsx:100`](../../frontend/src/features/workspace/components/GitHubIntegrationSection.tsx) dùng `await navigator.clipboard?.writeText(...)`. Khi `navigator.clipboard` là `undefined` (trang không secure-context, webview nhúng, chính sách trình duyệt), optional chaining trả `undefined`, `await undefined` **thành công** ⇒ hiện toast *"Đã sao chép…"* nhưng clipboard rỗng.
- **Tác động:** luồng onboarding quan trọng nhất là **copy Webhook Secret / Payload URL**; người dùng tin toast, dán chuỗi rỗng/sai vào GitHub ⇒ webhook không hoạt động mà không có cảnh báo.
- **Sửa:** kiểm tra tồn tại trước khi copy và báo lỗi thật:
  ```ts
  if (!navigator.clipboard?.writeText) { message.error('Trình duyệt không cho phép sao chép — hãy chọn và copy thủ công.'); return }
  ```
  hoặc fallback `document.execCommand('copy')` trên input readonly. Thêm test "clipboard unavailable ⇒ không có toast thành công". Áp dụng cho cả 2 file (3 nút).

### F3 — **[P2] "Kích hoạt tích hợp GitHub" tự chọn bảng đầu tiên và bật tạo thẻ từ **mọi** Issue**

- **Bằng chứng:** khi chưa có cấu hình, form tự điền [`GitHubIntegrationSection.tsx:87-94`](../../frontend/src/features/workspace/components/GitHubIntegrationSection.tsx): `isEnabled: true`, `defaultBoardId: boards[0]?.id`, `issueLabelFilter: ''`, `autoCreateTasksFromIssues: true`. Backend cũng mặc định bật auto-create khi nhận request đầu tiên.
- **Tác động:** manager chỉ muốn "kết nối GitHub" bấm một nút là integration **đã bật** và tự tạo thẻ cho **tất cả Issue** của repo vào bảng đầu tiên (có thể là Backlog/Sprint không mong muốn) — nguy cơ làm ngập board bằng dữ liệu ngoài ý muốn (dù có thể Undo từng thẻ / xoá integration).
- **Sửa:** không preselect board; chỉ bật nút lưu khi đã chọn board (hoặc mặc định `autoCreateTasksFromIssues = false` cho tới khi có board + cảnh báo "mọi Issue sẽ tạo thẻ"). Thêm test: chưa chọn board ⇒ nút disabled / auto-create tắt.

### F4 — **[P3] `applyColumnUpdated` làm rơi `isInReview` khi payload thiếu field**

- **Bằng chứng:** [`boardStore.ts:319`](../../frontend/src/features/board/stores/boardStore.ts) — `isInReview: column.isInReview ?? false`, trong khi `isClarification` dùng `?? c.isClarification` (giữ giá trị cũ).
- **Tác động:** một payload `ColumnUpdated` cũ/partial (rolling deploy, fixture cũ) sẽ **âm thầm xoá** trạng thái In Review trong store; tag biến mất dù DB vẫn đúng.
- **Sửa:** `isInReview: column.isInReview ?? c.isInReview`.

### F5 — **[P3] `ColumnModal` không bắt rejection khi backend từ chối cột In Review thứ hai**

- **Bằng chứng:** [`ColumnModal.tsx:41-50`](../../frontend/src/features/board/components/ColumnModal.tsx) `await onSubmit(...)` không `try/catch`; `useBoard` đã hiện toast lỗi rồi **rethrow**; backend trả `409` khi đã có cột `is_in_review` khác (`ColumnService.EnsureSingleInReviewColumnAsync`) hoặc `400` khi vi phạm loại trừ.
- **Tác động:** toast lỗi hiện đúng, modal giữ nguyên (tốt), nhưng phát sinh **unhandled promise rejection** (console/error boundary) — không sạch.
- **Sửa:** bọc `try { await onSubmit(...) ; onClose() } catch { /* toast đã hiện */ }`.

### F6 — **[P3] Settings page tải trước toàn bộ dữ liệu tích hợp dù đang ở tab "Cài đặt chung"; không huỷ request cũ**

- **Bằng chứng:** [`useWorkspaceIntegrations.ts:41-81`](../../frontend/src/features/workspace/hooks/useWorkspaceIntegrations.ts) — `Promise.all` 3 endpoint + gọi nối tiếp `getGitHubDeliveries`; effect chạy ngay khi page mount (test [`WorkspaceIntegrations.test.tsx:187-189`](../../frontend/src/features/workspace/components/__tests__/WorkspaceIntegrations.test.tsx) còn khẳng định điều này). `ignore` chỉ chặn setState **sau unmount**, không chặn response cũ ghi đè khi đổi workspace nhanh.
- **Tác động:** 4 request thừa mỗi lần mở Settings; race condition nhẹ khi chuyển workspace liên tiếp.
- **Sửa:** chỉ load khi `activeTab === 'integrations'` (truyền `enabled` theo tab), hoặc dùng `AbortController`/request-id trong `reload`, và gộp `getGitHubDeliveries` vào `Promise.all`.

### F7 — **[P3] Nhận diện "GitHub Bot" dựa cả vào tên hiển thị**

- **Bằng chứng:** [`TaskDetailModal.tsx:790-793`](../../frontend/src/features/board/components/TaskDetailModal.tsx) — `comment.authorName === 'GitHub Bot' || authorId === GITHUB_BOT_USER_ID || legacyUserId === …`.
- **Tác động:** một thành viên đặt tên hiển thị "GitHub Bot" sẽ bị gắn avatar/tag GITHUB BOT, comment của họ bị render markdown và **không sửa/xoá được** (`isAuthor` bị loại).
- **Sửa:** chỉ nhận theo `authorId`/`userId` (ID hệ thống), bỏ so sánh tên; giữ fallback ID cho payload legacy.

### F8 — **[P3] Sửa webhook có thể âm thầm xoá các subscription key ngoài catalog UI**

- **Bằng chứng:** [`OutboundWebhooksSection.tsx:117-141`](../../frontend/src/features/workspace/components/OutboundWebhooksSection.tsx) khởi tạo checkbox từ `subscribedEvents` nhưng `Checkbox.Group` chỉ render `WEBHOOK_EVENT_CATALOG`; khi lưu chỉ gửi các key đang check. Endpoint tạo qua API với key fine-grained (`OverdueTask`, `CommentMention`, …) sẽ mất các key này lần đầu được sửa qua UI.
- **Sửa:** giữ các key không thuộc catalog dưới dạng hidden/disabled và gửi lại khi save, hoặc normalize fine-grained → canonical khi đọc (backend đã có `MapToCanonicalCategory`).

### F9 — **[P3] Ghi chú nghiệm thu production còn thiếu**

- [`phase-15-5`](../tasks/phase-15-5-frontend-integrations-and-e2e-verification.md) đánh dấu ✅ là hợp lý cho phần code, nhưng nên ghi rõ **E2E/live verification chưa chạy** (chỉ có checklist deploy ở §6 báo cáo) để phiên sau không hiểu nhầm là đã nghiệm thu production.
- Ghi chú: dòng *"Tiền đề đã merge: 14 migrations/1.167 test"* ở master plan và dòng *"14.7 Track B = 738"* trong `ci-web.yml` là **mốc lịch sử trước Phase 15**, không phải số liệu hiện tại — đã kiểm tra lại và **không phải lỗi**; chỉ thiếu mốc `747` của đợt khắc phục 15.3/15.4 (sẽ bổ sung khi cập nhật baseline 786).

### F10 — **[P3] Tìm kiếm `tn-` trên Kanban so khớp chuỗi con**

- **Bằng chứng:** [`BoardView.tsx:377-381`](../../frontend/src/features/board/components/BoardView.tsx) — `tn-${taskNumber}`.includes(query). Nhập `tn-1` khớp cả `TN-1`, `TN-10…TN-19`, `TN-100…`; nhập `tn` khớp mọi thẻ có số.
- **Sửa:** nếu query khớp `^tn-\d+$` thì so sánh chính xác `===`; ngược lại giữ `includes` cho tìm theo tiêu đề/mô tả.

### F11 — **[P3] Plaintext secret còn nằm trong state cấu hình sau khi lưu**

- **Bằng chứng:** [`useWorkspaceIntegrations.ts:169-172`](../../frontend/src/features/workspace/hooks/useWorkspaceIntegrations.ts) — `setGithubIntegration(saved)` giữ nguyên `webhookSecretPlaintext` (ngoài `oneTimeSecret`).
- **Sửa:** destructure bỏ `webhookSecretPlaintext` trước khi set state (`const { webhookSecretPlaintext, ...config } = saved`), chỉ giữ trong `oneTimeSecret` và xoá khi dismiss. Phòng thủ theo chiều sâu (không phải lỗi rò rỉ hiện tại vì không render field này).

### F12 — **[P3] `TaskCard` vẫn truyền `delete={isCompleted}` rồi vô hiệu bằng `textDecoration: 'none'`**

- **Bằng chứng:** [`TaskCard.tsx:324,330`](../../frontend/src/features/board/components/TaskCard.tsx). `AGENTS.md` §4.2 cấm gạch ngang tiêu đề thẻ hoàn thành (che dấu tiếng Việt); hiện inline style đè nên **không có gạch ngang thực tế**, nhưng prop `delete` là code chết/mâu thuẫn và dev ghi trong L1 là giữ nó "để tôn trọng assertion test cũ".
- **Sửa:** bỏ `delete`, giữ màu muted `#94a3b8` + `opacity 0.85` + icon ✔ (đúng chuẩn), cập nhật assertion cũ nếu nó phụ thuộc prop này.

---

## 5. Kế hoạch Chỉnh sửa

### Trước khi merge (✅ đã thi hành 2026-11-11 — commit `e122ecd`)
| Task | Nội dung | File | Nghiệm thu |
| :--- | :--- | :--- | :--- |
| **R1** (F1) ✅ | Tách `buildGitHubIntegrationRequest`/`requiresDefaultBoardForAutoCreate` và unit-test `EMPTY_GUID`, `''`, trim, điều kiện board; thêm test xoá cấu hình; sửa câu trong report | `utils/githubIntegrationRequest.ts` + test, `WorkspaceIntegrations.test.tsx`, report §3.4 | 4 unit test + 3 integration test mới; report khớp |
| **R2** (F2) ✅ | Guard clipboard trước khi gọi + thông báo lỗi rõ ràng; test "clipboard unavailable ⇒ báo lỗi" | `TaskDetailModal.tsx`, `GitHubIntegrationSection.tsx`, test tương ứng | `navigator.clipboard` undefined ⇒ không toast thành công |
| **R3** (F3) ✅ | Bỏ preselect board; chặn lưu khi `autoCreateTasksFromIssues` bật mà chưa chọn board; thêm ghi chú dưới ô chọn bảng | `GitHubIntegrationSection.tsx` (+ test) | Test "blocks enabling auto-create…" xác nhận không gọi API + hiện cảnh báo |

### Follow-up PR (≈0,5 ngày)
**R4** (F4) store `?? c.isInReview`; **R5** (F5) try/catch ColumnModal + test 409; **R6** (F6) lazy-load theo tab/AbortController; **R7** (F7) bot ID-only; **R8** (F8) giữ key legacy; **R9** (F9) bổ sung ghi chú "production E2E pending" (số liệu master plan/ci-web đã cập nhật trong đợt này); **R10** (F10) exact match `tn-\d+`; **R11** (F11) strip plaintext; **R12** (F12) bỏ `delete`, cập nhật test cũ.

### Cổng kiểm thử sau mỗi bước
```powershell
dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental       # 0/0
dotnet ef migrations has-pending-model-changes --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api
$env:TEAMNEXUS_TEST_DB = "Host=localhost;Port=5433;Database=TeamNexus_Test;Username=postgres;Password=postgres"
dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj  # 1.329 / 0 / 0
cd frontend; npm run lint; npx tsc -b; npm test; npm run build   # 0/0; exit 0; 786+; exit 0
```
Nếu thêm test frontend, nâng baseline `ci-web.yml` tương ứng (chỉ khi chủ ý).

---

## 6. Việc còn lại (không phải lỗi code)

1. **Chưa chạy E2E/live verification trên production** — checklist ở [`report §6`](../report/phase-15-external-integrations-test-report.md) cần thực hiện sau deploy: tab Tích hợp → tạo Slack/Discord + Gửi thử; GitHub Payload URL/Secret; PR `TN-…` → cột Đang duyệt PR + chip; Issue → thẻ + GitHub Bot comment.
2. **Push/PR:** `develop` local đang đi trước `origin/develop`; nhánh `origin/SNHau` có thể merge `--ff-only` sau khi các commit trên nằm trong `develop` (nếu áp dụng R1–R3 trên nhánh mới thì merge theo thứ tự).
3. **Nhánh `backup/main-before-reset-26382b6`** đã merge trước đó; không cần dùng lại.

---

## 7. Phụ lục — Bằng chứng kiểm chứng

- Lệnh test JSON: `npx vitest run --reporter=json --outputFile=...` → `numTotalTests = 786`, `numPassedTests = 786`, `numFailedTests = 0`; `Get-ChildItem -Recurse -Include *.test.ts,*.test.tsx src` = **127 file**. (Lần chạy full đầu tiên có 1 test `ProfilePage` timeout dưới tải song song — chạy lại toàn bộ xanh `786/786`; test này chạy riêng cũng xanh.)
- Backend: `Passed! - Failed: 0, Passed: 1329, Skipped: 0, Total: 1329` (7 m 03 s) với container `pgvector/pgvector:pg18` ở `localhost:5433`.
- Build: `Build succeeded. 0 Warning(s), 0 Error(s)`; drift: `No changes have been made to the model since the last migration.`
- Frontend build cảnh báo *"Some chunks are larger than 500 kB"* — **có từ trước 15.5**, không phải regression; không chặn cổng.
- Đã đối chiếu thủ công route/DTO/event key frontend ↔ backend (không phát hiện mismatch).

---

## 8. Trạng thái Thi hành (2026-11-11)

- **Đã sửa trước merge (R1–R3, commit `e122ecd`):** clipboard guard (F2), bắt buộc chọn Default Board khi bật auto-create (F3), tách + unit-test request builder cho `EMPTY_GUID`/`""` (F1), thêm test xoá cấu hình; cập nhật `ci-web` **786**, roadmap/master plan/report/handover.
- **Cổng sau sửa:** build `0/0`; drift sạch; backend **1.329/0/0**; lint **0/0** (299 file); `tsc` exit 0; Vitest **786/786 (127 file)**; `vite build` exit 0.
- **Follow-up (R4–R12):** giữ nguyên danh sách P3 ở §5; không có thay đổi backend/migration.
- **Trạng thái merge:** nhánh `fix/phase15-5-review` = `origin/SNHau` + commit `e122ecd` (+ commit tài liệu); merge `--ff-only` được vào `develop` sau khi SNHau được chấp nhận.
