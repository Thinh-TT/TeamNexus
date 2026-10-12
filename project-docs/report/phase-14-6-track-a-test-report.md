# Báo cáo nghiệm thu — Giai đoạn 14.6 **Track A**: Hoàn thiện trải nghiệm & bịt rò rỉ ID

> **Phạm vi báo cáo:** **Track A** (A1–A4). **Track B** (Multi-Agent Personas, Proactive Auto-Healing,
> ReAct tool-calling, Workspace RAG) **chưa bắt đầu** — vẫn là kế hoạch trong
> `tasks/phase-14-6-ai-agent-enhancements.md` §2.
>
> **Kế hoạch nguồn:** `tasks/phase-14-6-ai-agent-enhancements.md` §1 + §3.
> **Nguyên tắc bất biến đã giữ:** Accountability Layer **không đổi một dòng nào** (mọi đường ghi dữ liệu
> nghiệp vụ vẫn `Pending` → Approve → Undo); **0 migration mới**; mọi trần token/ngân sách vẫn bị clamp.
>
> **Nguồn gốc:** kiểm thử thực chiến trên live `app.teamnexus.cloud` sau Giai đoạn 14.5.

---

## 1. Kết quả ĐO THẬT

| # | Cổng | Ngưỡng | Baseline (14.5) | Kết quả 14.6 Track A |
|---|---|---|---|---|
| 1 | `dotnet build TeamNexus.sln -m:1 -nr:false` | 0 warning / 0 error | 0 / 0 | ✅ **0 / 0** |
| 2 | `dotnet test` (PostgreSQL 18 thật, `Skipped: 0`) | 0 failed, 0 skipped | 643 / 0 / 0 | ✅ **665 / 0 / 0** (+22) |
| 3 | `dotnet ef migrations list` | vẫn **11** | 11 | ✅ **11** (không sinh migration nào) |
| 4 | `dotnet ef migrations has-pending-model-changes` | sạch | sạch | ✅ *"No changes have been made to the model since the last migration."* |
| 5 | FE `npm test` | 0 failed | 633 / 107 file | ✅ **657 / 110 file** (+24 / +3) |
| 6 | FE `npm run lint` | 0 warning | 0 | ✅ **0 warning / 0 error** (260 file) |
| 7 | FE `npx tsc -b` | exit 0 | 0 | ✅ exit **0** |
| 8 | FE `npm run build` | OK | OK | ✅ **built in 13.59s** |
| 9 | Cổng CI nâng **sau cùng**, bằng số thật | `-ne 665` · `-lt 657` | 643 · 633 | ✅ cả hai workflow đã cập nhật |

**Môi trường đo:** PostgreSQL 18 trong container (`teamnexus-test-pg`, cổng 5433) qua
`TEAMNEXUS_TEST_DB`; Node 24; .NET 10. **Không** dùng EF InMemory — suite chạy trên schema thật
(`jsonb`, CHECK constraint, query filter).

---

## 2. Phân bổ test mới

### Backend +22

| Suite | Số test | Nội dung |
|---|---|---|
| `Pure/AiChatAnswerSanitizerTests` | **7** | Đúng câu đã rò trên live (GUID của cột) bị thay bằng `[mã nội bộ]` mà câu vẫn đọc được · idempotent · nhiều GUID trong một câu · GUID **in hoa** · **không** cắn nhầm blob hex dài hơn (digest) · prose/`null`/`""` giữ nguyên · **ranh giới có chủ ý**: bộ lọc **không** biết khối ```json (vì nó chỉ nhận prose đã tách) |
| `Pure/AgentChatDigestTests` | **7** | Digest có đủ số thẻ mỗi cột + top thẻ (chỉ tên hiển thị, **không** GUID) · trường trống đọc thành chữ ("chưa đặt"/"không hạn"/"chưa gán") · **trần ký tự không bao giờ bị vượt** và cắt theo **dòng nguyên vẹn** · digest rỗng/không có ⇒ **không** sinh khối · digest **chỉ** xuất hiện khi **không** có thẻ đính kèm · prompt có rule §A2 + rule §A4 và **template không chứa GUID thật** |
| `Integration/Phase146TrackATests` | **8** | **A3**: ghi đúng 2 row `task_id` + `board_id = null` + workspace, nạp lại đúng thứ tự hội thoại, đọc lại **idempotent**; **isolation** hai transcript (không thấy câu hỏi của nhau); phân quyền (người ngoài **404**, task lạ **404**); keyset paging `before`; **A2**: provider *script* in GUID thật ⇒ `done` frame vẫn thô (bất biến `Σdelta == done.answer`) nhưng **transcript lưu DB đã lọc**; cùng luật cho chat của thẻ; **A4**: prompt **có** digest khi không gắn thẻ (đếm đúng số thẻ mỗi cột) và **không** có khi đã gắn thẻ |

### Frontend +24 (110 file, +3)

| File test | Số test | Nội dung |
|---|---|---|
| `ai/utils/__tests__/redactIdentifiers.test.ts` | **7** | câu rò thật bị ẩn mà vẫn đọc được · nhiều GUID + idempotent · **không** cắn nhầm digest · prose/rỗng giữ nguyên · `redactAnswerProse` **giữ nguyên khối JSON đề xuất** (ID thật phải sống để duyệt được) · vẫn lọc khi fence **chưa** đóng |
| `ai/components/__tests__/AgentChatPanel.picker.test.tsx` | **5** | gắn thẻ bằng picker (không kéo) ⇒ pill y hệt đường drop · tìm theo **tiêu đề** và theo **người phụ trách**, có trạng thái "không tìm thấy" · thẻ đã gắn bị **disable** (không gắn hai lần) · ở trần 8 ⇒ nói rõ + disable hết · còn chỗ ⇒ hiện `x/8` |
| `board/components/__tests__/TaskCard.aiChat.test.tsx` | **4** | "Gửi vào AI Chat" gọi handler với đúng thẻ · **không** mở modal thẻ khi bấm menu (React bubble xuyên portal) · **không** có menu khi không truyền handler · **không** có menu trên drag overlay |
| `board/components/__tests__/BoardView.agentChat.test.tsx` *(mở rộng)* | **+2** | từ menu `...` của thẻ ⇒ panel **tự mở** + thẻ được gắn, `moveTask` **không** bị gọi · picker của board liệt kê được thẻ và gắn qua picker |
| `ai/hooks/__tests__/useAiChat.test.ts` *(mở rộng)* | **+4** | nạp transcript khi mở tab · **phát lại lịch sử đã nạp làm ngữ cảnh** cho lượt hỏi kế tiếp · lỗi mạng ⇒ tab **vẫn dùng được** (không chặn chat) · ẩn GUID ở **cả hai chiều** (row cũ trong DB và câu trả lời mới) |
| `ai/hooks/__tests__/useAgentChat.test.ts` *(mở rộng)* | **+2** | GUID model rò ra bị ẩn ở **bubble đang stream** lẫn **lượt được lưu** · khối JSON đề xuất **giữ nguyên** `taskId`/`columnId` |

> **Không** test cũ nào bị viết lại. **Một** assertion của Phase 14.5 phải đổi — xem §5.

---

## 3. Đã hiện thực gì

### A1 — Bộ chọn đính kèm thẻ trực quan

| Việc | File |
|---|---|
| Popover tìm kiếm thẻ (title + priority + assignee; tìm theo tiêu đề/người phụ trách) | `frontend/src/features/ai/components/AgentChatTaskPicker.tsx` (**mới**) |
| Nút **"Đính kèm thẻ"** ở header vùng đính kèm; **dùng chung** luật từ chối với đường kéo-thả (`attachRefusalReason`) | `frontend/src/features/ai/components/AgentChatPanel.tsx` |
| Menu `...` trên mỗi `TaskCard` với action **"Gửi vào AI Chat"** | `frontend/src/features/board/components/TaskCard.tsx` |
| Truyền handler qua cột ⇒ board | `KanbanColumn.tsx` · `BoardView.tsx` |
| Chỉ mở panel **một lần** (giữ độ rộng người dùng đã kéo) | `BoardView.openAgentChat` (ref `agentChatOpenRef`) |

Kéo-thả **giữ nguyên**: `useDroppable` + nhánh `handleDragEnd` không đổi một dòng nào.

### A2 — Chặn rò rỉ ID kỹ thuật

| Việc | File |
|---|---|
| Rule nghiêm ngặt trong system prompt (board) + ví dụ phản diện **không** dùng GUID thật | `Services/AgentChatPrompts.cs` |
| Rule tương tự cho prompt chat của thẻ | `Services/AiChatPrompts.cs` |
| Bộ lọc **thuần** `RedactIdentifiers` + `ContainsIdentifier` | `Services/AiChatAnswerSanitizer.cs` (**mới**) |
| Lọc **trước khi ghi DB** (cả board lẫn task) | `AgentChatService.PersistTranscriptBestEffortAsync` · `AiChatService.PersistTranscriptBestEffortAsync` |
| Lọc **phần hiển thị** trên client + lọc lại khi **nạp** transcript cũ | `frontend/src/features/ai/utils/redactIdentifiers.ts` (**mới**) · `useAgentChat.ts` · `useAiChat.ts` |

**Vì sao không viết lại luồng SSE trực tiếp** (ghi rõ để không ai "sửa lại cho đúng ý"):
`done.answer == Σ delta` là bất biến client dựa vào để khôi phục khung bị mất. Một GUID **vắt qua hai
khung** nên lọc từng khung vừa phá bất biến đó, vừa cắt nát khối JSON đề xuất **thật** (`taskId`/`columnId`
là ID hợp lệ và **phải sống** để Manager duyệt được). Vì vậy: prompt (yêu cầu) + **server lọc phần lưu** +
**client lọc phần hiển thị trên chuỗi tích luỹ**. → Đây là **một phần hợp đồng API**: client Phase 16
(Flutter) phải lọc prose trước khi render, **đừng** render thẳng `answer`.

### A3 — Hợp nhất transcript của thẻ vào `agent_chat_messages`

| Việc | File |
|---|---|
| Ghi cặp hỏi–đáp với `task_id`, `board_id = null`, `workspace_id` (best-effort, sau khi đã stream) | `Services/AiChatService.cs` |
| Nạp transcript của thẻ (Member+, keyset `before`, `take` clamp) | `IAiChatService.ListMessagesAsync` |
| Route mới | `Endpoints/AiChatEndpoints.cs` → `GET /api/tasks/{taskId}/agent-chat/messages` |
| **Luật đọc dùng chung** cho cả hai transcript + mapper dùng chung | `Services/AgentChatTranscriptQuery.cs` · `Services/AgentChatTranscriptMapper.cs` (**mới**) |
| Trần `MaxMessageChars` riêng cho chat của thẻ | `Options/AiChatOptions.cs` |
| FE: nạp transcript khi mở tab, phát lại làm ngữ cảnh, không chặn chat khi lỗi | `frontend/src/features/ai/hooks/useAiChat.ts` |

**0 migration**: dùng đúng cột `task_id` mà Migration 11 chừa sẵn; CHECK
`num_nonnulls(board_id, task_id) = 1` làm việc tách hai phạm vi trở thành **ràng buộc cấu trúc**, không
phải quy ước. Retention hiện có (`AgentChatRetentionRunner`) quét theo `created_at` **toàn bảng** nên lượt
của thẻ tự động được dọn cùng — **không** cần chủ sở hữu retention thứ hai.

### A4 — Lightweight Board Digest

| Việc | File |
|---|---|
| Hàm **thuần** dựng khối digest, **tự cưỡng chế trần ký tự**, cắt theo dòng nguyên vẹn | `Services/AgentChatPrompts.BuildDigestBlock` |
| Chỉ dựng khi `attachedTaskIds` **rỗng**; đếm thẻ **chính xác** (query riêng) + xếp hạng top thẻ **trong bộ nhớ** | `Services/AgentChatService.LoadDigestAsync` |
| 2 knob mới, đều bị clamp | `Options/AgentChatOptions.cs` (`MaxDigestTasks` 10 · `MaxDigestChars` 2000) |

**Vì sao xếp hạng trong bộ nhớ:** `tasks.priority` là **text**, nên `ORDER BY priority` sắp theo **chữ cái**
(`High, Low, Medium, Urgent`) — đúng thứ tự **ngược** với mức độ gấp, và digest sẽ nêu tên những thẻ **ít**
gấp nhất. Một trang ứng viên bị chặn (`DigestCandidateLimit = 200`) được đọc rồi xếp bằng enum thật; số thẻ
mỗi cột vẫn **chính xác** vì đó là query gộp riêng (board 300 thẻ vẫn báo đúng "300").

---

## 4. Bug thật đã bắt & sửa (phần quan trọng nhất của báo cáo này)

### B1 — Thứ tự transcript do **Guid ngẫu nhiên** quyết định (có sẵn từ Phase 14.5)

**Phát hiện bởi** `PHASE146A_A1` (test mới của A3) ngay lần chạy đầu: nạp transcript trả về
`assistant` **trước** `user`.

**Nguyên nhân gốc:** `TeamNexusDbContext.StampAuditableTimestamps` lấy **một**
`var now = DateTimeOffset.UtcNow;` cho **mỗi** `SaveChangesAsync`. Cả hai row của một lượt hỏi–đáp được ghi
trong **cùng một** lần lưu ⇒ `created_at` **giống hệt nhau**. Truy vấn cũ là
`ORDER BY created_at DESC, id DESC` rồi `Reverse()` ⇒ khi hoà, **`id`** phân xử, mà `id = Guid.NewGuid()`
là **ngẫu nhiên**. Kết quả: khoảng **một nửa** số lần mở lại, câu trả lời hiện **trên** câu hỏi.

**Vì sao suite Phase 14.5 không bắt được:** `AGENTCHAT3` **có** khẳng định `messages[0].role == "user"` —
nó chỉ đơn giản là **may** qua mọi lần chạy cho tới nay. Đây là bài học: một test khẳng định thứ tự mà
khoá sắp xếp không xác định thì không phải là test.

**Cách sửa:** luật đọc duy nhất ở `AgentChatTranscriptQuery.NewestPageAsync` —
`created_at` → **`role`** (câu hỏi trước câu trả lời) → `id` (chỉ để **ổn định**, không để đúng).
Sửa ở **luật đọc dùng chung** nên **cả** transcript của board (Phase 14.5) lẫn của thẻ (14.6) đều được vá.
Test khoá lại: `PHASE146A_A1` (một lượt) + `PHASE146A_A4` (hai lượt ⇒ `user, assistant, user, assistant`).

> **Không** sửa `StampAuditableTimestamps` (dùng chung cho **mọi** bảng, và thứ tự của `ChangeTracker.Entries`
> không được bảo đảm) và **không** thêm cột thứ tự (sẽ cần migration — trái nguyên tắc bất biến của Track A).

### B2 — Menu `...` của thẻ làm **mở luôn modal chi tiết**

**Phát hiện bởi** `TASKCARD_AI2`. React bubble sự kiện synthetic theo **cây component** kể cả **xuyên portal**,
nên menu của antd `Dropdown` (render qua portal) vẫn là "click bên trong thẻ" với `onClick` ở div gốc:
chọn "Gửi vào AI Chat" thì modal chi tiết **cũng** mở.

**Cách sửa:** `info.domEvent.stopPropagation()` trong `onClick` của menu item (không phải chỉ
`stopPropagation` trên nút trigger). Test `TASKCARD_AI2` khoá lại.

### B3 — Digest bị "có" dù đã gắn thẻ (test tự bắt lỗi của chính nó)

Assertion đầu tiên dùng `DigestHeader` (`"TỔNG QUAN BẢNG"`) — nhưng rule 7 của prompt **nhắc tên** tiêu đề
này, nên `DoesNotContain` **luôn đỏ** dù digest có mặt hay không. Đã tách hằng số
`AgentChatPrompts.DigestBlockMarker` (`"- TỔNG QUAN BẢNG:"`) — dòng **mở đầu** khối — và dùng nó ở cả hai
chiều khẳng định. (Ghi lại vì đây đúng là kiểu test "xanh giả" nếu làm ngược.)

---

## 5. Thay đổi có chủ ý đối với thứ đã verify

| # | Thay đổi | Vì sao & phạm vi |
|---|---|---|
| **1** | `AgentChatApiTests.AGENTCHAT22` — **1 assertion** đổi từ *"task chat ghi 0 row"* thành *"2 row, `task_id` set, `board_id` null"*, đổi tên test cho đúng nội dung mới. | Phase 14 **D2** ("server không giữ bảng chat nào") bị **thay thế có chủ ý** bởi §A3 — đúng cảnh báo mà tài liệu Phase 14.5 đã ghi ở §8 ("task chat vẫn transcript ở client — nợ có chủ ý… chỗ chừa để hợp nhất sau"). Phần **hợp đồng SSE + `meta` tên TASK** của test **giữ nguyên**; test còn được **tăng cường** thêm khẳng định isolation với transcript của board. |
| **2** | `AgentChatService` gọi `AgentChatTranscriptQuery` / `AgentChatTranscriptMapper` thay vì tự viết truy vấn + map. | Gộp **một** luật đọc cho hai transcript — chính là điều kiện để B1 chỉ phải sửa **một** chỗ. Hành vi quan sát được chỉ **đúng hơn** (B1). |
| **3** | `AgentChatPromptTests`/prompt text: `SystemPromptTemplate` của cả hai chat **thêm rule** (6/7). | Không test cũ nào khẳng định nội dung template; `FakeAiProvider` chọn nhánh bằng **Markers** nên không bị ảnh hưởng. |

**Ngoại lệ duy nhất của giai đoạn:** dòng #1.

---

## 6. Kiểm thử tay (1 lượt — hướng dẫn để chạy lại)

`dotnet run --project src/TeamNexus.Api` + `cd frontend && npm run dev`, rồi:

**(a)** Mở một board ⇒ nút nổi góc phải-dưới ⇒ panel mở. Bấm **"Đính kèm thẻ"** ⇒ gõ một phần tiêu đề ⇒
chọn thẻ ⇒ pill xuất hiện **không cần kéo**. Làm lại với **menu `...`** trên một `TaskCard`: panel **tự mở**
và thẻ đã được gắn; modal chi tiết thẻ **không** mở.

**(b)** Hỏi *"Tiến độ bảng này thế nào?"* **khi chưa gắn thẻ nào** ⇒ câu trả lời có nội dung tổng quan
(số thẻ mỗi cột), **không** còn từ chối rỗng.

**(c)** Hỏi một câu khiến model nhắc tới cột ⇒ văn bản trả lời **không** chứa chuỗi dạng GUID; nếu model có
in ra, người dùng thấy `[mã nội bộ]`. **F5** ⇒ transcript **vẫn còn** và **vẫn** sạch GUID.

**(d)** Mở một thẻ ⇒ tab **"Hỏi AI về thẻ này"** ⇒ hỏi 1 câu ⇒ **đóng modal, mở lại** ⇒ hội thoại **vẫn còn**
(trước 14.6 là mất). Kiểm tra `agent_chat_messages`: 2 row với `task_id` set, `board_id` **null**.

**(e)** Panel board và tab của thẻ **không** thấy hội thoại của nhau.

**(f)** Bất biến Accountability: "Gửi đề xuất" ⇒ `Pending` trong *Lịch sử AI* ⇒ Approve ⇒ thẻ đổi cột thật ⇒
Undo ⇒ về cột cũ. Không có đường nào ghi dữ liệu nghiệp vụ mà chưa duyệt.

---

## 7. Việc còn lại / nợ kỹ thuật (ghi rõ, không giấu)

1. **Track B chưa làm** (Personas, Proactive Auto-Healing, ReAct tool-calling, Workspace RAG) — nguyên kế hoạch §2.
2. **`AiChat:MaxMessageChars` mặc định giống `AgentChat` (16000)** nhưng là **knob riêng** — cố ý: hai bề mặt,
   hai ngân sách, đúng nguyên tắc D3 của Phase 14.5.
3. **GUID vẫn tồn tại trên dây SSE** (khung `delta`/`done` **không** bị viết lại — xem §3/A2). Client **phải**
   lọc prose. Đây là **hợp đồng**, không phải sơ suất; Flutter Phase 16 phải làm theo.
4. **Không** có test integration nào chứng minh byte-level incremental streaming — giới hạn của `TestServer`
   đã ghi từ Phase 14 và **không đổi** ở giai đoạn này.
5. **`DigestCandidateLimit = 200`**: trên board > 200 thẻ, "top thẻ" chỉ xét 200 thẻ đầu theo `position`;
   **số thẻ mỗi cột vẫn chính xác**. Đủ cho quy mô hiện tại, ghi lại để không ai tưởng là bug.
6. **Tab của thẻ vẫn nạp tối đa 50 lượt** (`TRANSCRIPT_PAGE_SIZE`) — chưa có UI "nạp thêm", dù API đã hỗ trợ
   keyset `before`.
7. **`AgentChat` log lúc khởi động chưa in** hai knob digest mới (không ảnh hưởng hành vi).

---

## 8. Bảng bằng chứng & nơi đối chiếu

| Hạng mục | Nơi đối chiếu |
|---|---|
| Kế hoạch & DoD | `tasks/phase-14-6-ai-agent-enhancements.md` §1, §3 |
| Trạng thái giai đoạn | `03-roadmap.md` §Giai đoạn 14.6 · `README.md` §Trạng thái 14.6 |
| Schema (không đổi) | `04-database-design.md` §3.10 + §7 (mục "Giai đoạn 14.6 §A3 dùng đúng chỗ chừa") |
| Module AI (chi tiết kỹ thuật + bug B1) | `src/Modules/Ai/TeamNexus.Modules.Ai/README.md` §Phase 14.6 Track A |
| Cổng CI | `.github/workflows/ci-backend.yml` (665) · `.github/workflows/ci-web.yml` (657) |
| Test backend mới | `tests/TeamNexus.Api.Tests/Pure/AiChatAnswerSanitizerTests.cs` · `Pure/AgentChatDigestTests.cs` · `Integration/Phase146TrackATests.cs` |
| Test frontend mới | `frontend/src/features/ai/utils/__tests__/redactIdentifiers.test.ts` · `ai/components/__tests__/AgentChatPanel.picker.test.tsx` · `board/components/__tests__/TaskCard.aiChat.test.tsx` (+2 mở rộng) |
