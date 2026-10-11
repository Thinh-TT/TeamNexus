# Báo cáo nghiệm thu — Giai đoạn 14: Nâng cao AI (Backend)

> **Phạm vi báo cáo:** **backend** (toàn bộ phần backend đã hiện thực & verify). Phần **frontend** ở trạng thái
> **📤 bàn giao** — theo dõi ở `tasks/phase-14-remaining-frontend-handover.md`.
> **Kế hoạch đã chốt:** `tasks/phase-14-ai-advanced.md`.
> **Ngày đo:** phiên thi hành Giai đoạn 14. **Máy đo:** dev local, PostgreSQL thật ở `localhost:5432`.

---

## 1. Kết quả ĐO THẬT

| # | Chỉ số | Baseline | Kết quả | Kết luận |
|---|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 / 0 | **0 Warning / 0 Error** | ✅ |
| 2 | `dotnet test` (PostgreSQL thật, `Skipped: 0`) | **480** | **Failed 0 / Passed 584 / Skipped 0 / Total 584** | ✅ **+104** |
| 3 | `dotnet ef migrations list` | **10** | **10** | ✅ **không đổi** |
| 4 | `has-pending-model-changes` | sạch | *"No changes have been made to the model since the last migration."* | ✅ |
| 5 | `git diff --stat` trên `src/TeamNexus.Persistence/Migrations/` | — | **không file nào** | ✅ **không migration** |
| 6 | `.github/workflows/ci-backend.yml` | assert `480` | assert **`584`** + `skipped = 0` | ✅ |
| 7 | `.github/workflows/ci-web.yml` | `-lt 508` | **giữ `-lt 508`** (FE chưa làm) | ✅ đúng trạng thái bàn giao |

**Ba lượt chạy đầy đủ cuối cùng:** `584 / 0 / 0` → `584 / 0 / 0` → **583 PASS + 1 FAIL (`584` total)**.
Lượt ghi nhận **566** là lượt chạy **trước khi** thêm `Pure/AiGuardrailTests` (18 test).
Test đỏ ở lượt thứ ba **chính là flake có sẵn đã ghi ở §1 baseline và §6 cuối báo cáo**
(`AgentRun_CancelStopsTheRunAndRecordsCancelled`), và **chạy lại riêng ⇒ PASS**. Số test **không tăng không giảm**
(584 cả ba lượt) ⇒ không có test nào bị mất hay bị skip.

**Phân bổ +104 test (đo theo suite, không theo ước tính):**

| Suite | Trước | Sau | Δ | Nội dung |
|---|---|---|---|---|
| `Pure/ObserverRiskTests` **(mới)** | 0 | **21** | +21 | luật `AtRiskDeadline`: biên 20% (strict `<`), sàn cửa sổ 2 ngày, cổng 48 h, comment "đánh thức" task, severity 24/72 h, `Weight` kẹp `[0,30]`, thứ tự tất định, prompt/validator biết type mới |
| `Pure/ProjectHealthTests` **(mới)** | 0 | **23** | +23 | band tại đúng các biên 80/60/40, trần từng khoản trừ, gate `aging`/`load`, 512 tổ hợp sinh tất định (không `NaN`/`Infinity`), bất biến "5 trọng số = 100" |
| `Pure/AiGuardrailTests` **(mới)** | 0 | **18** | +18 | **bổ sung khi rà §5 của kế hoạch**: clamp `AiChatOptions`/`ObserverOptions`, ưu tiên cắt prompt của `ObserverSummarizer`, trần cột/task + pin cột done của board template |
| `Integration/AiTaskChatApiTests` **(mới)** | 0 | **18** | +18 | SSE contract (`meta`→`delta`*→`done`, `done.answer == Σ delta`), 401/403/404/400/503, lỗi giữa luồng ⇒ khung `error`, đường "lưu bình luận" qua Accountability Layer |
| `Integration/BoardTemplateApiTests` **(mới)** | 0 | **18** | +18 | generate **không ghi gì**, confirm ⇒ `Pending` workspace-scoped, approve tạo board/cột/task, undo **soft-delete**, hồi quy Phase 4 |
| `Integration/DashboardApiTests` | 14 | **18** | +4 | `health` đúng công thức, hồi quy 9 field cũ, `health` là field **cuối**, thẻ không hạn không bị coi là "sắp hết hạn" |
| `Pure/ObserverVocabularyTests` | 10 | **11** | +1 | `All` **4 → 5** type (**sửa 1 test cũ** — xem §4) |
| *(tất cả suite còn lại)* | 456 | 456 | 0 | **không** suite nào khác bị sửa |

---

## 2. Đã hiện thực gì (theo 4 ô của roadmap)

### Ô A — AI Task Chat (SSE + Accountability Layer) ✅

- **Port thứ ba `IAiStreamingProvider`** (`AiProvider.cs`): `IAsyncEnumerable<AiStreamChunk>` + `AiStreamRequest`.
  **Không** thêm method vào `IAiProvider`/`IAiToolCallingProvider` — đúng lý lẽ đã ghi ở port thứ hai (Phase 7 §4.2).
  Đăng ký **cùng switch** `DeepSeek:ApiKey` như hai port kia.
- **`DeepSeekAiProvider.StreamAsync`**: `"stream": true`, `HttpCompletionOption.ResponseHeadersRead`, đọc `data: {...}`
  đến `data: [DONE]`, trích `choices[0].delta.content`. `ChatRequest` += `bool? Stream` (**null** ⇒ bỏ khỏi payload ⇒
  body của Smart Setup/Observer/Agent **byte-identical**).
- **`FakeAiProvider.StreamAsync`**: chia câu trả lời mẫu thành khung 24 ký tự có `Task.Yield()` giữa các khung ⇒
  luồng **chunked thật**, chạy hoàn toàn offline (0 token). Không cắt đôi surrogate pair.
- **`AiChatSseWriter`** (hàm thuần): `meta`/`delta`/`done`/`error`, JSON camelCase trên **một dòng** (an toàn
  khỏi `\n` trong dữ liệu người dùng).
- **`AiChatService.PrepareAsync`** (eager) tách khỏi **`StreamPreparedAsync`** (lazy): nhờ vậy **mọi** guard
  (503/404/403/400) vẫn trả **JSON + status thật**, còn lỗi **sau** khi stream mở đi ra dưới dạng khung `error`.
- **Guard `AiChatOptions`** (section riêng `"AiChat"`): `Enabled` (mặc định `true`), `MaxHistoryMessages` (**12**),
  `MaxHistoryChars` (**8000**), `MaxOutputTokens` (**1200**), `Temperature` (0.3), `MaxCommentsInContext` (10),
  `MaxCommentCharsInContext` (500), `MaxTaskContextChars` (6000), `MaxAnswerChars` (**2000**). **Mọi** giá trị
  **clamp** trong `Effective` ⇒ cấu hình `0`/âm/khổng lồ **không thể** vô hiệu hoá chốt chặn chi phí.
- **Đường ghi có trách nhiệm `POST .../ai-chat/message`**: tạo `ai_action_logs` **`Pending`** action `PostComment`
  (`entity_type = 'Task'`) ⇒ Manager **Approve** mới ghi `task_comments` (tác giả = **người bấm lưu**), **Undo**
  soft-delete bình luận. Lượt chat thường **không ghi gì**.

### Ô B — Observer dự báo rủi ro + Sức khỏe dự án ✅

- **`AtRiskDeadline`** vào `ObserverSignalDetector` + `NotificationTypes.All` (**4 → 5**) + `ObserverPrompts`
  (liệt kê 5 type, có 1 dòng giải thích nghĩa).
- **`ObserverRisk`** (adapter mỏng) + **`TeamNexus.Shared.Risk.DeadlineRiskRules`** (**định nghĩa dùng chung**).
- `ObserverSummarizer` **pin thứ tự cắt** `(severity desc → weight desc)`, chỉ bỏ từ **cuối**.
- Run summary ghi thêm **`signalsPreserved`** / **`signalsDroppedBeforePrompt`**.
- **`ProjectHealth.Compute`** (hàm thuần, module **Board**) + **append cuối** `DashboardResponse.health`.

### Ô C — AI Board Template ✅

- Generate (workspace-scoped, Manager+, **không ghi DB**) → proposal `{ summary, boardName, boardDescription,
  columns[2..6], tasks[5..10] }`, server **pin đúng 1** cột `isDone`.
- Confirm → `Pending` `CreateBoardFromTemplate` (`entity_type = 'Workspace'`).
- **`CreateBoardFromTemplateApplier`**: approve tạo **đúng 1 board + N cột + M task** qua
  `IBoardService`/`IColumnService`/`ITaskService` (tác giả = Manager duyệt); `applied_snapshot` ghi
  `createdBoardId` + `createdColumnIds`; **undo = soft-delete board**.

### Ô D — Guardrail & giới hạn token ✅

Đã hiện thực đủ 9 dòng của bảng §5 (kế hoạch) và **bổ sung `Pure/AiGuardrailTests` (18 test)** vì rà lại cho thấy
"guardrail chỉ được kiểm qua happy-path" là chưa đủ: các test mới khoá trực tiếp phần **clamp**, phần **ưu tiên
cắt prompt**, và phần **re-validate proposal** ở bước confirm (chống client sửa tay).

---

## 3. Phát sinh bắt buộc khi hiện thực (E–L của kế hoạch) — tất cả đã xử lý

| # | Phát sinh | Cách xử lý | Test chứng minh |
|---|---|---|---|
| **E/P1** | `AiActionContext.BoardId` non-nullable, nhưng board template **tạo** board | Nới thành `Guid?` + `RequireBoardId()`; sửa **9** call site ở `CreateSubtasksApplier`. Cố ý **KHÔNG** sửa `SearchSystemDataTool` (đó là `AgentToolContext`, không phải `AiActionContext` — sửa nhầm sẽ vỡ build) | `AccountabilityApiTests` + `BoardTemplateApiTests` xanh |
| **F/P2** | `ResolveAsync` không có nhánh `Workspace` ⇒ confirm sẽ 400 | Thêm nhánh `Workspace` (404 workspace lạ, 403 Member, `BoardId = null`) | `BT_CONFIRM_1/2/6` |
| **G/P3** | `BuildAppliedSnapshotJson` hardcode field ⇒ `createdColumnIds` không được ghi ⇒ Undo hỏng | Thêm `CreatedColumnIds` + `CreatedBoardId` (**optional**) và `JsonIgnoreCondition.WhenWritingNull` | `BT_APPROVE_2`, hồi quy `BT_REGRESS_1` |
| **I/P4/P6** | Tín hiệu mới có thể bị cắt khỏi prompt ở workspace lớn | Ưu tiên `(severity, weight)` khi cắt + đếm `signalsPreserved`/`signalsDroppedBeforePrompt` | `RISK*`, `GUARD6/7/8` |
| **J/P5** | System prompt liệt kê cứng 4 type | Đổi thành 5 type + 1 dòng giải thích | `RISK14` |
| **K** | `ObserverVocabularyTests` assert đúng 4 type | **Sửa 1 test cũ** (đổi tên thành `...ExactlyTheFiveDetectorSignals`, thêm `AtRiskDeadline` vào `[Theory]` canonical) | `ObserverVocabularyTests` |
| **L/P7** | `FakeAiProvider` + `ScriptedAiProvider` phải implement port mới | Cả hai implement `IAiStreamingProvider`; `TeamNexusApiFactory` override **cả ba** port bằng **cùng một** instance `ScriptedAiProvider` | toàn bộ suite chat |

**Hai phát sinh CHƯA có trong kế hoạch, phát hiện khi thi hành (ghi để không ai tưởng là sai lệch):**

- **P9 — `PostCommentApplier` cần `taskId` trong `after_snapshot`.** `AgentCommentSnapshot` yêu cầu
  `taskId` + `content`; `AiChatService` ban đầu chỉ gửi `content` ⇒ approve trả **400**. Đã bổ sung `taskId`
  (trùng với `entity_id`, nhưng đó **là** hợp đồng của applier).
- **P10 — KHÔNG tái dùng `RequestAgentOutputAsync` cho đường lưu bình luận của người.** Nó assert *"task phải
  đang gán cho AI Agent"* và *"người gọi là member_type = 'ai_agent'"* ⇒ nếu tái dùng, **mọi** lượt lưu hợp lệ
  của người dùng sẽ bị **403** với thông điệp nói về một AI Agent mà người dùng không hề nhắc tới. Đã thêm
  `IAiActionService.RequestChatCommentAsync` (guard của **người**: Member+ + task hiển thị). Bất biến
  "chưa duyệt thì chưa ghi" **không đổi**: quyền duyệt vẫn là Manager+.

---

## 4. Thay đổi có chủ ý đối với thứ đã verify (ghi rõ)

| # | Thay đổi | Vì sao **buộc** phải làm | Phạm vi ảnh hưởng |
|---|---|---|---|
| 1 | **`AiActionContext.BoardId` → `Guid?`** | Board template tạo board ⇒ không có board để trỏ vào | 9 call site trong `CreateSubtasksApplier` (dùng `RequireBoardId()`). Hành vi khi scope đúng **không đổi**; scope sai giờ **ném lỗi rõ ràng** thay vì dùng `Guid.Empty` |
| 2 | **Snapshot jsonb bỏ khoá `null`** | Hai field optional mới sẽ làm snapshot Phase 4/7 mọc thêm `null` | Đã kiểm: `ReadGuid`/`ReadString`/`ReadGuidArray`/`ParseJson` đều coi "thiếu" và "null" như nhau; **có test hồi quy** `BT_REGRESS_1` khẳng định snapshot cũ **không** có `createdBoardId`/`createdColumnIds` |
| 3 | **Sửa `ObserverVocabularyTests` (4 → 5)** | Danh sách này là **whitelist chống hallucination**: thêm tín hiệu mà quên ⇒ model **không bao giờ** được phép báo type mới ⇒ tính năng im lặng | 1 test (đổi tên cho đúng nghĩa mới) + 2 dòng `[Theory]` bổ sung |
| 4 | **`ObserverPrompts.SystemPrompt`** | Prompt là hợp đồng thật với model | Chỉ **thêm** type + 1 dòng giải thích; 4 type cũ giữ nguyên |
| 5 | **`TeamNexusApiFactory` override cả 3 port AI** | Scripted provider phải dùng chung cho mọi port, nếu không suite chat sẽ lặng lẽ test nhầm `FakeAiProvider` | Chỉ ảnh hưởng khi `ScriptedAi != null`; các suite cũ dùng `FakeAiProvider` **không đổi** |

---

## 5. Ba giới hạn & nợ kỹ thuật (cố ý, ghi rõ để không bị coi là bug)

1. **`TestServer` có thể đệm thân response ⇒ "streaming thật" không chứng minh được ở tầng byte.** Suite
   `AiTaskChatApiTests` vì vậy khoá **hợp đồng**: content type `text/event-stream`, `meta` **trước** mọi `delta`,
   đúng **một** `done`, `done.answer == nối các delta.text`, và dùng `HttpCompletionOption.ResponseHeadersRead`
   (thêm `TestHttpClient.SendStreamingAsync`). Chứng minh từng byte đến dần cần **socket thật**, nằm ngoài phạm vi
   test in-process. `SseStreamResult` có `FlushAsync` sau **mỗi** khung — nếu điều đó bị bỏ, không test nào trong
   suite này sẽ đỏ, nên đây là điểm cần review bằng mắt khi sửa file đó.
2. **Chat không lưu transcript ở DB (D2, cố ý).** Đóng tab là mất hội thoại. Muốn giữ lâu dài: bấm
   **"Lưu thành bình luận"** (đường Accountability Layer). Giải pháp đầy đủ là bảng `ai_task_chat_messages` —
   **nợ kỹ thuật có chủ ý**, không thuộc phạm vi đồ án ở giai đoạn này.
3. **Board template Undo soft-delete cả board ⇒ công việc người dùng đã thêm vào board đó cũng bị ẩn.** Chọn
   soft-delete vì hard delete phá bất biến append-only + FK RESTRICT (DB design §7), và vì có thể khôi phục bằng
   DB. FE **phải** có `Popconfirm` cảnh báo (đã ghi trong note bàn giao §6.1 D18).

---

## 6. Hai lỗi thật mà test bắt được khi thi hành (ghi lại để thấy suite có giá trị thật)

1. **`Health` không thể là tham số positional của `DashboardResponse`.** Thêm nó vào constructor làm
   `Pure/DigestTemplateTests` **vỡ build** (test cũ tạo `DashboardResponse` với 9 đối số). Đã chuyển thành
   **property `init` với giá trị mặc định** ⇒ mọi call site cũ vẫn biên dịch, và đó cũng đúng tinh thần "append,
   không sửa" của quyết định D15.
2. **5 trọng số của công thức sức khỏe ban đầu cộng lại chỉ 85** (`40+15+10+10+10`) ⇒ **không workspace nào chạm
   được 0**, và nửa dưới của thang điểm trở nên vô nghĩa. Test bất biến `HEALTH4b` (`Σ = 100`) bắt được ngay khi
   viết; đã cân lại thành **40/20/10/15/15**. Ngoài ra `HEALTH5` phát hiện `aging`/`load` **không có mẫu số** nên
   vẫn trừ điểm khi workspace **không có việc mở** ⇒ đã **gate** hai khoản này theo `OpenTasks > 0`.

**Về flake có sẵn:** baseline đầu kỳ có **1 test đỏ** (`AgentAndReportingApiTests.AgentRun_CancelStopsTheRunAndRecordsCancelled`,
nhận **409** thay vì **200 OK**). Chạy riêng ⇒ **PASS**; trong **2/3** lượt chạy đầy đủ ⇒ PASS, lượt thứ ba ⇒ tái hiện
(mỗi lần đều **chạy riêng thì PASS**). Kết luận: **flake tồn tại trước Giai đoạn 14** — race giữa `FAKE:SLOW`
3 s và `POST /cancel` (nếu lượt chạy chậm hơn 3 s thì run đã ở trạng thái terminal, `cancel` trả 409). **Không** sửa
test của Giai đoạn 7 (đúng chỉ dẫn §12 của kế hoạch). **Việc còn lại cho người duyệt:** nếu muốn CI hết flake thì
cần một thay đổi **ngoài** phạm vi Giai đoạn 14 (ví dụ `ScriptedAiProvider`/`FakeAiProvider` cho phép chờ tín hiệu
thay vì `Task.Delay(3s)`, hoặc test chấp nhận cả 200 và 409) — **không** tự ý làm trong giai đoạn này.

---

## 7. Việc còn lại (bàn giao)

| # | Việc | Ai | Ghi chú |
|---|---|---|---|
| 1 | **Toàn bộ frontend Giai đoạn 14** | **antigravity** | `tasks/phase-14-remaining-frontend-handover.md` (hợp đồng API + 13 bẫy) |
| 2 | Nâng `.github/workflows/ci-web.yml` từ `-lt 508` lên số thật | **antigravity** | Làm **sau cùng**, khi FE xong |
| 3 | **1 lượt kiểm thử tay** bằng Scalar UI (bằng chứng §6.5 #5 của kế hoạch) | người duyệt | 5 bước, gồm cả `POST .../observer/scan` để thấy `AtRiskDeadline` thật |
| 4 | Chạy lại 2 workflow CI trên GitHub Actions | người duyệt | Cần `postgres:18` service container của CI (máy dev chỉ có PostgreSQL local) |

**Điều kiện "xong" của phần backend (§6.4 kế hoạch) — đã đạt:** 4 ô + 8 hạng mục phát sinh đóng bằng bằng chứng;
**vẫn 10 migration** và `has-pending-model-changes` **sạch**; **`Skipped: 0`**; **0 warning / 0 error**; cổng
`ci-backend.yml` đã nâng lên **584**; `03-roadmap.md` + `README.md` + `01-system-specification.md` +
`04-database-design.md` + `Ai/README.md` + 2 tài liệu Giai đoạn 14 đã ghi **số thật**.
