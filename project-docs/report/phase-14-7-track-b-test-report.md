# Giai đoạn 14.7 Track B — Báo cáo Nghiệm thu

> **Phạm vi báo cáo:** **Track B** (§B1 Multi-Agent Personas · §B2 ReAct Tool-calling · §B3 Workspace RAG với pgvector).
> **Track A** (§A1–§A5) đã có báo cáo riêng: `report/phase-14-7-track-a-*` (§11 của tài liệu chia task).
> **Tài liệu nguồn:** `tasks/phase-14-7-autonomous-agent-jev.md` §5 (kế hoạch §B1–§B3) + **§12** (nhật ký thi hành Track B).
> **Môi trường đo:** PostgreSQL **18 + pgvector** (`pgvector/pgvector:pg18`, container `teamnexus-pg-147`), backend `net10.0` (SDK 10.0.201), frontend React 19 + Vite + Ant Design v6.
> **Ngày:** 15/10/2026.

---

## 1. Tóm tắt

Track B đưa TeamNexus từ "một agent trả lời được" lên nền tảng tự trị: **nhiều AI Agent theo persona**, **vòng lặp ReAct có 3 tool mới**, và **bộ nhớ dự án ngữ nghĩa bằng pgvector**. Toàn bộ nằm sau các cờ **mặc định tắt** (`Jev:RagEnabled=false`, `Embeddings:Enabled=false`) nên **không thể ảnh hưởng hành vi Track A đã deploy**: 925 test cũ xanh mà **không sửa assertion nào** ngoài **một** dòng (thêm `"personaKey"` vào danh sách key chính xác của `WorkspaceMemberApiTests` — lệch **L8**, cùng luật append-only Phase 10 §4.2).

| Hạng mục | Trạng thái |
|---|---|
| §B1 — Persona: schema + backfill + index mới + `PersonaCatalog` + `EnsurePersonaAgentAsync` + `?persona=` + prompt | ✅ |
| §B2 — 3 tool ReAct mới, dùng chung ngân sách guardrail cũ | ✅ |
| §B3 — pgvector: 2 bảng + extension + HNSW + 3 endpoint + applier + runner nhúng vector + khối `KIẾN THỨC DỰ ÁN` | ✅ |
| Hạ tầng — image `pgvector/pgvector:pg18` + preflight `DatabaseFixture` + CI | ✅ |
| Tests, cổng chốt, tài liệu | ✅ |

---

## 2. Số đo thật

| Cổng | Ngưỡng | Kết quả đo |
|---|---|---|
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | 0 warning / 0 error | **0 / 0** |
| `dotnet test` (PostgreSQL thật + pgvector) | Failed 0 / Skipped 0 | **Failed 0 / Passed 1167 / Skipped 0** (baseline 925 ⇒ **+242**; Pure 562 · Integration 605) |
| `dotnet ef migrations list` | 14 | **14** (`…Phase147AgentPersonas`, `…Phase147KnowledgeBase`) |
| `dotnet ef migrations has-pending-model-changes` | sạch | **"No changes have been made to the model since the last migration."** |
| `npm test` (Vitest) | Failed 0 / Skipped 0 | **738 / 123 file** (baseline 692 / 117 ⇒ **+46 test, +6 file**) |
| `npm run lint` (oxlint) | 0 / 0 | **0 / 0** (287 file, 116 rule) |
| `npx tsc -b` | exit 0 | **exit 0** |
| `npm run build` | OK | **OK** (`dist/assets/index-*.js` 1 976,29 kB / gzip 600,88 kB — cảnh báo "chunk > 500 kB" là **có sẵn từ trước**, không phải của Track B) |
| **Chạy một lượt KHÔNG-Docker** | skip nhóm DB, KHÔNG xanh giả | **Failed 0 / Passed 0 / Skipped 57** trên nhóm DB; **Pure vẫn chạy hết** |
| **Kiểm chứng image (đo thật bằng Docker)** | `vector` có mặt; `pg_isready` có mặt | `pgvector/pgvector:pg18` có `vector.control` + `vector--0.8.7.sql` và `/usr/bin/pg_isready`; **`postgres:18` có 0 file `vector*`** ⇒ đổi image là **bắt buộc**, và healthcheck của CI chạy được với image mới |

> **⚠️ Flake CÓ SẴN, đã xác nhận không phải của Track B:** một lượt chạy `npm test` đầy đủ **đỏ 1 test** — `ProfilePage.test.tsx › disables Rời button for owner with explanation tooltip` (test này tự đặt hạn **15 s**). Chạy **riêng file đó: 13/13 xanh**, test này mất **6,2 s**; nó chậm vì chờ hết các nhánh bất đồng bộ, và dưới tranh chấp CPU của cả suite thì vượt 15 s. File đó **không** import bất kỳ module nào Track B chạm tới. Báo cáo ở đây thay vì im lặng: một flake do hạn thời gian là **nợ kỹ thuật thật**, nhưng sửa nó nằm ngoài phạm vi Track B.

> Nhóm test DB skip **cả nhóm** (lệch **L10**) chứ không chỉ nhóm RAG: `CREATE EXTENSION` nằm ở đầu migration cuối nên migration dừng giữa chuỗi (migration persona đã commit) và database rơi vào trạng thái nửa vời. `DatabaseFixture` probe `pg_extension`/`pg_available_extensions` **TRƯỚC** `MigrateAsync` nên nó trở thành **một** thông điệp nói rõ cách sửa. Không `try/catch` quanh migration: nuốt lỗi migration sẽ che bug schema thật.

---

## 3. Những gì đã dựng (theo hệ thống)

### 3.1. Persistence — 2 migration
- **`Phase147AgentPersonas`:** `workspace_members.persona_key varchar(40) NULL` + 2 CHECK; **backfill `'general'` TRƯỚC khi tạo index**; index `uq_workspace_members_ai_persona` trên **`(workspace_id, COALESCE(persona_key,'general')) WHERE member_type='ai_agent'`** (SQL thô — EF Core không có API expression index) tạo **trước** khi `DROP INDEX uq_workspace_members_ai_agent`.
- **`Phase147KnowledgeBase`:** `CREATE EXTENSION IF NOT EXISTS vector;` (SQL thô, đặt đầu) + `workspace_knowledge_documents` + `workspace_knowledge_chunks` (`vector(1536)`, index **HNSW `vector_cosine_ops`**).
- **Package:** `Pgvector.EntityFrameworkCore` 0.3.0 (+ `Pgvector` 0.3.2) + `npgsql => npgsql.UseVector()`.

### 3.2. Ai module
- **§B1:** `PersonaCatalog` (thuần), `EnsurePersonaAgentAsync`, `AgentChat:DefaultPersona`, query param `?persona=` (sai ⇒ 400), khối `VAI TRÒ:` **prepend** (marker `FakeAiProvider` giữ nguyên byte), `session` trả `personas[]`/`defaultPersona`.
- **§B2:** `IMemberWorkloadReader` (dùng chung với §A4) + `InspectMemberWorkloadTool`, `MatchSkillsTool` (thuần EF), `ValidateAcceptanceCriteriaTool` + `IJevDecisionService.ValidateAcceptanceCriteriaAsync` (trả `null` chứ không ném).
- **§B3:** `EmbeddingOptions` (+`ValidateOnStart`), `IEmbeddingProvider` + `OpenRouterEmbeddingProvider` + `FakeEmbeddingProvider`, `KnowledgeChunking` (thuần), `KnowledgeBaseService`, `WorkspaceKnowledgeStore` (port Board ↔ impl Ai), `KnowledgeEmbeddingRunner` + hosted service, `CreateKnowledgeDocumentApplier`, `KnowledgeEndpoints`, khối `KIẾN THỨC DỰ ÁN` trong prompt.

### 3.3. Board module
`IAiAgentResolver.EnsurePersonaAgentAsync` (append 1 method), `IWorkspaceKnowledgeStore` + `NullWorkspaceKnowledgeStore`, `WorkspaceMemberResponse.PersonaKey` (field thứ 13).

### 3.4. Frontend
Chọn persona trong Agent Chat (gửi `?persona=` mỗi lượt, nhãn theo **lượt đang trả lời**, không suy diễn ngược lịch sử), tên persona trên lịch sử chạy agent, trang **Knowledge Base** (thêm/xoá ⇒ hiển thị rõ là **chờ Manager duyệt**; `enabled:false` ⇒ cảnh báo tính năng đang tắt chứ không phải "chưa có tài liệu").

---

## 4. Bug thật bắt được trong lúc thi hành

| # | Bug | Vì sao test bắt được & cách sửa |
|---|---|---|
| **T1** | **`BuildContextBlock` treo một tiêu đề rỗng.** Khi ngân sách chỉ đủ cho dòng `- Tài liệu X:` mà không đủ cho nội dung, code cũ **append tiêu đề rồi mới phát hiện nội dung không vừa** ⇒ model nhận một tài liệu **được trích dẫn nhưng không có chữ nào** dưới nó. | Nguy hiểm hơn cả việc không trích dẫn: nó nói "đây là kiến thức liên quan" rồi để trống, và model sẽ tự bù. Sửa: kiểm **cả tiêu đề + thân** trước khi ghi bất cứ thứ gì; snippet được giữ **nguyên vẹn hoặc bỏ hẳn**. |
| **T2** | **Ghi chú "(còn N đoạn …)" bị nuốt im lặng.** Chú thích cuối khối cũng đi qua `AppendCapped`, nên khi ngân sách đã cạn thì **không có gì nói rằng đã có kiến thức bị bỏ**. | Người đọc (và model) tưởng workspace chỉ có đúng mấy đoạn được hiện. Sửa: **giữ chỗ** cho ghi chú và chạy **hai lượt** — lượt tham lam để biết số lượng bị bỏ, rồi lượt quyết định giữ chỗ **đúng độ dài thật** của ghi chú. ⚠️ Lần sửa **đầu tiên** (giữ chỗ bằng ước lượng 40 ký tự) lại gây **bug thứ hai**: ước lượng lớn hơn cả ngân sách ⇒ khối **rỗng** dù header + 1 snippet vừa khít. Đây là ví dụ rõ của "sửa một chỗ, hỏng chỗ khác" mà chỉ có test mới bắt được. |
| **T3** | **Ghi chú "0 đoạn bị bỏ".** Khi không snippet nào vừa, code cũ vẫn có thể ghi ghi chú về **0 đoạn hiển thị**. | Một khối nói "còn N đoạn không hiển thị" mà không hiển thị gì cả là vô nghĩa và gây hiểu sai. Sửa: không snippet nào được nhận ⇒ trả **chuỗi rỗng** (và ghi chú chỉ được ghi khi đã có ít nhất một snippet **được hiện**). |
| **T4** | **`noul` không có `confidence`.** Shape `noul` của Jev chỉ có `{ type, noul }` ⇒ tool `ValidateAcceptanceCriteria` **không thể** đọc độ chắc chắn từ provider. | Nếu bịa một con số thì không ai phân biệt được với số thật ở tầng dưới. Sửa: **suy ra** độ chắc chắn từ khoảng cách xác suất tới ngưỡng 0.5 và **ghi rõ trong code rằng nó là suy ra**; đồng thời `verified` là **`null`** (không phải `false`) khi Jev tắt/lỗi, vì "chưa kiểm được" và "đã kiểm và không đạt" dẫn tới hành động ngược nhau. |
| **T5** | **EF cảnh báo `10622` làm bẩn cổng "0 warning".** `WorkspaceKnowledgeDocument` có query filter soft-delete và là **đầu bắt buộc** của quan hệ tới chunk ⇒ EF cảnh báo rằng chunk có thể được nạp trong khi document đã bị ẩn. | Cổng CI của repo là "0 warning", và cảnh báo này mô tả đúng một lỗi thật. Sửa: thêm query filter **khớp** cho chunk, đi **qua document** (không qua `workspace_id` denormalize) để chỉ có một nguồn sự thật về khả năng hiển thị. |
| **T6** | **EF Core KHÔNG phát `CREATE EXTENSION`.** `modelBuilder.HasPostgresExtension("vector")` chỉ được ghi thành **annotation** trên `AlterDatabase()`; migration đầu tiên sinh ra **không có** câu lệnh nào tạo extension. | Phát hiện khi **đọc file migration đã sinh** (không phải khi chạy). Nếu để nguyên thì schema phụ thuộc vào việc người vận hành tự chạy câu lệnh đó — đúng kiểu "works on my machine". Sửa: viết SQL thô, đặt **trước** mọi cột `vector`, và ghi lệch **L9** để không ai tưởng annotation tự lo. |
| **T7** | **`--no-build` đọc DLL cũ ⇒ migration sinh ra SAI.** Suýt tạo một migration "persona + knowledge" gộp chung và một migration rỗng chỉ chứa `AlterDatabase()`. | Phát hiện vì migration sinh ra **không khớp** thay đổi vừa viết (chi tiết ở nhật ký phiên). Sửa: **rebuild trước mỗi lần `migrations add`**, và vì mọi thay đổi model đều được gộp vào một lần sinh, phải **tách phạm vi** bằng cách tạm rút entity khỏi `DbContext` rồi khôi phục. Đây là bài học về quy trình, ghi lại để lần sau không mất 4 vòng. |

---

## 5. Bất biến đã giữ

1. **Accountability Layer 100%:** tài liệu kiến thức — đường **ghi dữ liệu** — đi qua `Pending` → Approve → Undo (`CreateKnowledgeDocument` + `CreateKnowledgeDocumentApplier`). Endpoint trả **201 kèm action**, không trả document.
2. **`Ai → Board` một chiều:** port `IWorkspaceKnowledgeStore` khai báo ở **Board**, implementation đăng ký ở `AddAiModule` (chạy sau nên thắng resolve).
3. **Jev không bao giờ trả GUID:** assignee vẫn dùng nhãn `cand-N`; tool mới nhận `userId` **từ chính `AgentToolContext`** cho phần scope, không đọc id workspace/board từ tham số model.
4. **Guardrail không nới:** `MaxToolCalls 15` / `RunTimeoutSeconds 300` / `MaxRunTokens 50000` / `MaxRunLlmCalls 20` — có test khẳng định thêm 3 tool **không** đổi trần nào.
5. **Không nới trần prompt:** khối `KIẾN THỨC DỰ ÁN` bị cưỡng chế bởi `Jev:RagMaxContextChars` **ở tầng hàm thuần**, và **không bao giờ** vượt trần (test ở **đúng biên** từ 1 ký tự trở lên).
6. **Không lộ GUID trong prompt:** khối kiến thức chỉ có `title` + `content` (test khẳng định không có GUID).
7. **Suy giảm an toàn mọi nơi:** thiếu extension / lệch số chiều / provider embedding lỗi / Jev tắt ⇒ chat **vẫn trả lời**, chunk ở `pending`/`failed` + `error`, tool trả "không xác minh được" — **không** 500 và **không** làm chết lượt chạy.
8. **Marker prompt `FakeAiProvider` giữ nguyên byte** ⇒ suite offline vẫn kiểm **đúng nhánh** (không xanh giả).

---

## 6. Nợ kỹ thuật có chủ ý

| # | Nợ | Lý do |
|---|---|---|
| **N1** | Tool `SearchProjectContext` (dự định ở 14.6 §B3) **không** làm | `SearchSystemData` (Phase 7) đã phủ chức năng; thêm tool trùng chỉ làm whitelist khó đọc (lệch **L5**) |
| **N2** | Persona **không** lưu vào `agent_chat_messages` | Cần migration thứ ba; và suy diễn ngược persona cho lịch sử cũ là **bịa dữ liệu** (lệch **L6**) |
| **N3** | Đổi model embedding sang số chiều khác ⇒ `ALTER COLUMN TYPE vector(N)` + **re-embed toàn bộ** | Đánh đổi có chủ ý của cột **có** số chiều: đổi lấy việc lệch số chiều là **fail-fast lúc khởi động** thay vì lỗi im lặng lúc ghi |
| **N4** | Chưa có giao diện nhập liệu kéo-thả tài liệu, chưa crawl `source_url` | `source_url` mới chỉ là **nhãn**; việc lấy nội dung từ URL là tính năng của Phase 15 (tích hợp ngoài) |
| **N5** | HNSW dùng tham số mặc định (không `m`/`ef_construction`) | Chưa có dữ liệu thật để tinh chỉnh; đổi sau là một migration `DROP INDEX`/`CREATE INDEX`, không đụng code |

---

## 7. Giá trị học thuật bổ sung của Track B

1. **"Một hàng rào cho nhiều người dùng":** 7 tool ReAct tiêu **cùng** một ngân sách (`MaxToolCalls`/timeout/token/lượt LLM). Thêm năng lực **không** có nghĩa là nới chi phí — và điều đó được **khoá bằng test**, không phải bằng lời hứa.
2. **Ngữ nghĩa thật, không phải từ khoá:** pgvector + HNSW cosine cho phép agent trả lời bằng **kiến thức dự án** (quy ước, spec, bài viết) chứ không chỉ dữ liệu đã có cấu trúc trong bảng `tasks`.
3. **Chi phí biên bằng 0 khi tắt:** `Jev:RagEnabled=false` + `Embeddings:Enabled=false` ⇒ **0 mạng, 0 token, 0 tiền**; bật lên là một quyết định tường minh, và mọi cờ đều có đường lui bằng **một dòng cấu hình**.
4. **Suy giảm được thiết kế, không phải được hy vọng:** nhà cung cấp embedding chết **không** làm hỏng nút duyệt của con người; Jev tắt **không** làm chết lượt chạy agent; thiếu extension ⇒ thông điệp nói rõ cách sửa thay vì lỗi khó hiểu. Ba đường hỏng khác nhau, ba cách xử lý khác nhau, cả ba đều **có test**.
5. **"Ngân sách là ràng buộc cứng":** một hàm render prompt 40 dòng đã sinh ra **3 bug thật** (T1–T3) — minh chứng rằng cả phần "chỉ là nối chuỗi" của một hệ AI cũng cần test ở **đúng biên**.

---

## 8. Việc vận hành cần làm để DoD đạt đủ trên bản đang chạy

1. **Đổi image PostgreSQL** ở CI (đã sửa trong `.github/workflows/ci-backend.yml`) và ở **máy dev local**: `postgres:18` **không** chạy được migration 14. Lệnh đúng:
   `docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d pgvector/pgvector:pg18`
2. **Bật RAG trên production** (chỉ khi muốn dùng): `Jev__RagEnabled=true` + `Embeddings__Enabled=true` + `Embeddings__ApiKey=sk-or-v1-…`. Giữ nguyên mặc định `false` nếu chỉ muốn Track A.
3. **Ghim `Typesafe`/OpenRouter contract:** `Jev:DecisionsPath` nằm dưới `/api/alpha/` ⇒ nếu OpenRouter đổi đường dẫn, sửa bằng **biến môi trường**, không cần rebuild.
4. **Số test baseline trong CI** đã chuyển sang **cận dưới** (giống `ci-web.yml`) và nay là 925; sau Track B nên nâng lên số đo thật trong §2 để cổng CI phản ánh đúng mức bảo vệ hiện có.
