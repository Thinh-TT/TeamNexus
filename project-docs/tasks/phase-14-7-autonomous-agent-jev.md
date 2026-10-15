# Giai đoạn 14.7 — Autonomous Agent & Tối ưu hóa Ra Quyết định với Jev (Hybrid AI Platform)

> **Trạng thái:** 🟡 **TRACK A ĐÃ THI HÀNH XONG (§A1–§A5, đã đo thật).** Track B (§B1–§B3) vẫn là 📋 kế hoạch.
> **Kế thừa từ:** `project-docs/06-adjust-and-upgrade-with-jev.md` (kiến trúc Jev) + `tasks/phase-14-6-ai-agent-enhancements.md` §2 (Track B chưa làm) + `03-roadmap.md` §14.7.
> **Mục tiêu:** Hợp nhất năng lực Autonomous AI Agent (kế thừa **Track B của 14.6**) với **Jev (System One, non-generative, TypeSafe qua OpenRouter)** theo kiến trúc **Hybrid AI: Jev = System 1 (quyết định có cấu trúc siêu tốc) + LLM = System 2 (ngôn ngữ & giải thích sâu)**.
> **Chia Track:** Track A = nền tảng + Jev + Observer Auto-Healing (§A1–§A5) · Track B = personas, ReAct mở rộng, RAG pgvector (§B1–§B3).
>
> **📊 SỐ ĐO THẬT Track A (xem nhật ký §8 để biết các lệch so với kế hoạch):**
> backend `dotnet build --no-incremental` **0 warning / 0 error** ·
> `dotnet test` **Failed 0 / Passed 912 / Skipped 0** (baseline 665 ⇒ **+247**) ·
> `dotnet ef migrations list` **11 → 12** (`Phase147AgentPlatform`) · `has-pending-model-changes` **sạch** ·
> frontend `npm test` **657 / 110 file** (không đổi — Track A không chạm frontend) ·
> `oxlint` **0/0** · `npx tsc -b` exit **0** · `npm run build` **OK** ·
> cổng CI: `ci-backend.yml` `665 → 912`; `ci-web.yml` giữ `657`.

---

## 0. Nguyên tắc bất biến (vi phạm = phải dừng lại và báo cáo)

1. **Accountability Layer giữ nguyên 100%.** Mọi tác động ghi dữ liệu đi qua `Pending` → `Approve` → `Undo` bằng `IAiActionService` + một `IAiActionApplier` riêng. Jev **chỉ đề xuất**; tuyệt đối không ghi thẳng.
2. **`Ai → Board` là quan hệ MỘT CHIỀU.** Board **không bao giờ** được `using TeamNexus.Modules.Ai.*`. Mọi thứ Board cần từ Ai phải là **port khai báo ở Board** + implementation đăng ký trong `AddAiModule` (chạy **sau** `AddBoardModule` trong `Program.cs` nên thắng resolve). Tiền lệ: `IActivityLogWriter`, `IAiAgentResolver`, `INotificationWriter`, `IEmailGateway`, `IDailyDigestService`.
3. **Jev KHÔNG BAO GIỜ trả về ID.** Mọi câu hỏi `choice` dùng **nhãn do server cấp phát** (`cand-1`, `Backend`, …); server giữ bảng ánh xạ nhãn → `Guid` **trong bộ nhớ của request** rồi tự ánh xạ ngược. `Guid` chỉ được xuất hiện ở field có kiểu `Guid` trong response. Đây là cơ chế chống rò rỉ GUID mạnh hơn hẳn cách đang dùng cho LLM (Phase 14.6 §A2 phải viết bộ lọc cưỡng chế vì LLM **có thể** sinh GUID).
4. **Không nới bất kỳ guardrail nào đang có** (`Agent:MaxToolCalls 15`, `RunTimeoutSeconds 300`, `MaxRunTokens 50000`, `MaxRunLlmCalls 20`, các trần `AiChat`/`AgentChat`). Tool mới tiêu cùng ngân sách đó.
5. **Không thêm migration nào ngoài 2 migration của giai đoạn này.** `dotnet ef migrations has-pending-model-changes` phải trả "No changes have been made to the model since the last migration."

---

## 1. Sự thật bên ngoài đã xác minh bằng tài liệu gốc

Đã tra cứu và đối chiếu ba nguồn:
[Jev trên OpenRouter](https://openrouter.ai/docs/guides/community/jev) ·
[OpenAPI `POST /api/alpha/decisions`](https://openrouter.ai/docs/api/api-reference/alphadecisions/submit-a-decisions-questions-and-answers-request) ·
[Decisions API schematic](https://portkey.ai/docs/aigw/product/ai-gateway/decisions-api).

### 1.1. Hệ quả thiết kế bắt buộc

- **Jev KHÔNG đi qua `/chat/completions`.** Nó có API "Decisions" riêng (`state` + `questions` → `answers` có kiểu). ⇒ `DeepSeekAiProvider` hiện tại (`IAiProvider` / `IAiToolCallingProvider` / `IAiStreamingProvider`, đều `POST {BaseUrl}/chat/completions`) **không thể tái dùng**, kể cả chỉ đổi `BaseUrl`. Cần client C# mới. Đây là lý do `Jev:BaseUrl` **phải** tách khỏi `DeepSeek:BaseUrl`.
- Bề mặt đã chốt: `POST https://openrouter.ai/api/alpha/decisions`. Model `typesafe/jev-1.13` (alias `~typesafe/jev-latest`), **context 32 000 token**, **output token MIỄN PHÍ** (chỉ tính tiền input token).
- **Request:** `{ model, state, questions, provider?, session_id?, trace?, user? }`.
- **Response:** `{ answers, model, id, provider, usage: { input_tokens, output_tokens, cost } }`.
- Ba primitive và shape câu trả lời **chính xác**:

| `type` | `criteria` là gì | Shape câu trả lời |
|---|---|---|
| `noul` | object `{ "true": mô tả, "false": mô tả }` | `{ type: "noul", noul: <0–1> }` |
| `choice` | object `{ label: mô tả }` — **object, không phải array** | `{ type: "choice", choice, probabilities: {label: number}, confidence }` |
| `score` | **array mô tả theo thứ tự tăng dần** | `{ type: "score", score, legend, probabilities, confidence }` |

- `state` nhận `string | object | array`.

> Hợp đồng dây chính xác (đã đối chiếu OpenAPI + ví dụ chính thức):
> ```jsonc
> // request — POST https://openrouter.ai/api/alpha/decisions
> {
>   "model": "typesafe/jev-1.13",
>   "state": { "title": "…", "description": "…" },      // string | object | array
>   "questions": {
>     "urgency": {
>       "type": "score",
>       "instructions": "Mức độ gấp của thẻ này?",
>       "criteria": ["Chờ được bản sau", "Nên sửa trong tuần", "Đang chặn doanh thu"]
>     },
>     "team": {
>       "type": "choice",
>       "instructions": "Miền kỹ thuật nào phụ trách?",
>       "criteria": { "Backend": "API, DB, service", "Frontend": "UI, rendering", "DevOps": "CI, hạ tầng" }
>     },
>     "is_bug": {
>       "type": "noul",
>       "instructions": "Đây có phải báo cáo lỗi không?",
>       "criteria": { "true": "Mô tả hành vi hỏng", "false": "Câu hỏi hoặc yêu cầu tính năng" }
>     }
>   }
> }
> // response 200
> {
>   "answers": {
>     "team":   { "type": "choice", "choice": "Backend", "confidence": 0.75,
>                 "probabilities": { "Backend": 0.84, "Frontend": 0.16, "DevOps": 0 } },
>     "urgency":{ "type": "score", "score": 1.99, "confidence": 0.99,
>                 "legend": { "0": "…", "1": "…", "2": "…" },
>                 "probabilities": { "0": 0, "1": 0.01, "2": 0.99 } },
>     "is_bug": { "type": "noul", "noul": 0.96 }
>   },
>   "model": "typesafe/jev-1.13-20260917", "id": "gen-dec-…", "provider": "TypeSafe",
>   "usage": { "input_tokens": 476, "output_tokens": 70, "cost": 0.000019992 }
> }
> ```

### 1.2. ⚠️ Rủi ro đã nhận diện & cách giảm thiểu

| # | Rủi ro | Giảm thiểu (bắt buộc hiện thực) |
|---|---|---|
| **R1** | Đường dẫn nằm dưới **`/api/alpha/`** — OpenRouter gắn nhãn **alpha**, có thể đổi không báo trước | Cô lập URL vào `Jev:BaseUrl` + `Jev:DecisionsPath` ⇒ đổi bằng **1 dòng env**, không rebuild. Mọi thất bại map sang `JevDecisionException` (→ **502**) giống tiền lệ `AiProviderException`. Mọi caller **phải** graceful degrade. |
| **R2** | `postgres:18` **không** có pgvector | Đã xác minh bằng Docker rằng **`pgvector/pgvector:pg18` có** `vector` trong `/usr/share/postgresql/18/extension/`. Đổi image CI (xem §3.3). |
| **R3** | Contract `/api/v1/embeddings` của OpenRouter cho model cụ thể **chưa được xác minh** | Port `IEmbeddingProvider` tách biệt + section `Embeddings` riêng; `Embeddings:Enabled=false` mặc định; đổi `BaseUrl`+`Model` là xong nếu cần provider khác (**0 dòng code**). |
| **R4** | Đổi index agent (bỏ ràng buộc "đúng 1 agent/workspace") có thể chạm test Phase 7 | Migration **backfill** `persona_key = 'general'`; toàn bộ test Phase 7 phải xanh **không sửa assertion nào**. Nếu buộc phải sửa ⇒ ghi vào §8 như **lệch có chủ ý** kèm lý do. |
| **R5** | Triage là đường nóng, DoD đòi < 300 ms | Đo thật và **ghi số thật**. Nếu vượt ⇒ giảm `Jev:MaxStateChars`/`Jev:MaxCandidates`; **không** âm thầm hạ chuẩn DoD. |

---

## 2. Tổng quan 2 Track

| Track | Phạm vi | Trọng tâm | Tác động schema |
|---|---|---|---|
| **Track A — Nền tảng + Jev (System 1)** | `Ai` module + `Board` port/service + FE | Client Jev, taxonomy, hồ sơ kỹ năng, confidence branching, triage, assignee dispatch, Observer Phần A + B | **1 migration** (`Phase147AgentPlatform`) |
| **Track B — Autonomous Platform** | Multi-Agent Personas + tool-calling + RAG | Nhiều AI agent row theo persona, 3 tool mới, pgvector RAG | **1 migration** (`Phase147KnowledgeBase`) |

**Thứ tự thi hành:** Track A trước (§A1→§A5). Track A xong là đã có Jev chạy thật + Observer auto-healing. Track B tiếp theo, với `Jev:RagEnabled=false` và `Embeddings:Enabled=false` **mặc định** ⇒ Track B **không thể** ảnh hưởng Track A đã deploy.

---

## 3. Thay đổi Schema (2 migration — chốt)

> ⚠️ **Lệch so với Giai đoạn 14.6 Track A (0 migration) và Giai đoạn 14 (0 migration).** Giai đoạn 14.7 **có 2 migration** vì (a) hồ sơ kỹ năng/taxonomy/persona cần cột + bảng thật, (b) RAG pgvector cần extension + cột `vector` + index HNSW. Từ **11 → 13** migration.

### 3.1. Migration 12 — `Phase147AgentPlatform`

| Thay đổi | Chi tiết |
|---|---|
| `workspace_members.skills` | `jsonb NOT NULL DEFAULT '[]'::jsonb` — mảng chuỗi slug kỹ năng |
| `workspace_members.primary_role` | `varchar(50) NOT NULL DEFAULT 'Member'` |
| `workspace_members.capacity_weight` | `double precision NOT NULL DEFAULT 1.0`, CHECK `> 0` |
| `workspace_members.max_wip_limit` | `int NOT NULL DEFAULT 5`, CHECK `>= 0` |
| `workspace_members.persona_key` | `varchar(40) NULL`; CHECK `persona_key IS NULL OR persona_key IN ('general','scrum_master','tech_lead','qa_security')`; CHECK bổ sung `member_type = 'human'` ⇒ `persona_key IS NULL` |
| **Index thay thế** | **DROP** `uq_workspace_members_ai_agent` (đang cưỡng chế **đúng 1** agent/workspace) → **CREATE** `uq_workspace_members_ai_persona` UNIQUE `(workspace_id, COALESCE(persona_key, 'general'))` **WHERE** `member_type = 'ai_agent'` |
| Bảng `workspace_taxonomies` | `id uuid PK`, `workspace_id uuid NOT NULL REFERENCES workspaces ON DELETE CASCADE`, `kind varchar(20) NOT NULL` (`domain`\|`work_type`), `key varchar(50) NOT NULL`, `label varchar(120) NOT NULL`, `color_hex varchar(10) NULL`, `position int NOT NULL DEFAULT 0`, `created_at`/`updated_at`. UNIQUE `(workspace_id, kind, key)`. INDEX `(workspace_id, kind, position)` |

**Backfill dữ liệu cũ (trong chính migration):**
```sql
UPDATE workspace_members SET persona_key = 'general'
WHERE member_type = 'ai_agent' AND persona_key IS NULL;
```
⇒ `FindAgentUserIdAsync` hiện có (`WHERE member_type = 'ai_agent'`) **vẫn trỏ đúng row cũ**, hành vi Phase 7 không đổi.

**Vì sao `COALESCE(persona_key,'general')` trong index:** một partial unique index trên `(workspace_id, persona_key)` sẽ **không** chặn được hai row cùng `persona_key = NULL` (PostgreSQL coi `NULL` khác nhau). Dùng `COALESCE` là cách duy nhất giữ được tính "mỗi persona tối đa 1 row" mà vẫn cho phép backfill dữ liệu cũ.

### 3.2. Migration 13 — `Phase147KnowledgeBase`

| Thay đổi | Chi tiết |
|---|---|
| Extension | `CREATE EXTENSION IF NOT EXISTS vector;` — đặt **đầu** migration, **trước** mọi cột `vector` |
| Bảng `workspace_knowledge_documents` | `id`, `workspace_id` (CASCADE), `title varchar(300)`, `source_url varchar(1000) NULL`, `content text`, `status varchar(20) NOT NULL` (`pending`\|`ready`\|`failed`), `error varchar(500) NULL`, `chunk_count int NOT NULL DEFAULT 0`, `created_by_user_id`, `created_at`/`updated_at`. INDEX `(workspace_id, created_at)` |
| Bảng `workspace_knowledge_chunks` | `id`, `document_id` (CASCADE), `workspace_id` (CASCADE, **denormalize** để truy vấn RAG không cần join), `chunk_index int NOT NULL`, `content text NOT NULL`, `embedding vector(1536) NULL`, `created_at`/`updated_at`. UNIQUE `(document_id, chunk_index)`. INDEX **HNSW** `USING hnsw (embedding vector_cosine_ops)` |

**Về số chiều vector (quyết định, ghi rõ lý do):** cột DB cố định **`vector(1536)`**, nhưng `Jev:RagEmbeddingDimension` là knob cấu hình; **khởi động kiểm tra** `RagEmbeddingDimension == 1536` và **fail-fast** (`ValidateOnStart`) nếu lệch. Lý do chọn cột **có** chiều thay vì `vector` không chiều: cột không chiều đẩy việc kiểm tra chiều sang tầng ứng dụng ⇒ dễ sinh lỗi im lặng khi đổi model. Đánh đổi: đổi model embedding sang số chiều khác cần một migration `ALTER COLUMN … TYPE vector(N)` + **re-embed toàn bộ** — chi phí này được ghi nhận có chủ ý.

### 3.3. CI & môi trường dev — thay đổi bắt buộc

- **`.github/workflows/ci-backend.yml`:** `image: postgres:18` → **`image: pgvector/pgvector:pg18`**. Giữ nguyên `POSTGRES_PASSWORD`, `ports`, `options` (healthcheck) và `TEAMNEXUS_TEST_DB` — image này dùng cùng ENV/entrypoint nên không cần đổi gì khác.
  - Đã xác minh bằng `docker run --rm pgvector/pgvector:pg18 sh -c "ls /usr/share/postgresql/18/extension/ | grep -i vector"` ⇒ có `vector--*.sql`.
- **`tests/TeamNexus.Api.Tests/Infrastructure/DatabaseFixture.cs`:** thêm **preflight** `SELECT 1 FROM pg_extension WHERE extname = 'vector'`; nếu thiếu ⇒ chỉ **skip nhóm test RAG** kèm thông điệp hướng dẫn (`docker run … pgvector/pgvector:pg18`), các nhóm khác **vẫn chạy**. Giữ đúng tinh thần D4 (không có DB ⇒ skip) mà không biến một máy thiếu extension thành "đỏ hàng loạt" hoặc — nguy hiểm hơn — thành "xanh giả".
- `project-docs/02-tech-stack-decisions.md` §3: ghi image mới + lệnh Docker local cho dev.

---

## 4. TRACK A — Nền tảng + Jev (System 1) + Observer Auto-Healing

### §A1. Client Jev — `src/Modules/Ai/TeamNexus.Modules.Ai/Services/Jev/`

| File (mới) | Nội dung & quyết định |
|---|---|
| `JevOptions.cs` | `SectionName = "Jev"`. `ApiKey` (secret, rỗng ⇒ fake), `BaseUrl = "https://openrouter.ai"`, `DecisionsPath = "/api/alpha/decisions"`, `Model = "typesafe/jev-1.13"`, `TimeoutSeconds = 20`, `Enabled = true`, `HighConfidenceThreshold = 0.85`, `MediumConfidenceThreshold = 0.50`, `MaxStateChars = 12000`, `MaxCandidates = 12`, `MaxQuestionsPerRequest = 6`. Derived: `HasApiKey`, `DecisionsUri`, `Effective` (mọi giá trị **clamp**; **cưỡng chế `High > Medium`**). **Không** có `Temperature`/`MaxTokens` — Jev không sinh text, thêm vào chỉ gây hiểu nhầm. **Không** `ValidateOnStart` cho `ApiKey` (rỗng là cấu hình dev hợp lệ — tiền lệ `DeepSeekOptions`). |
| `JevDecisionModels.cs` | `JevQuestionType` (`Noul`\|`Choice`\|`Score`), `JevQuestion`, `JevDecisionRequest`, `JevNoulAnswer`, `JevChoiceAnswer`, `JevScoreAnswer`, `JevUsage(InputTokens, OutputTokens, CostUsd)` — **`CostUsd` là `double?`, KHÔNG phải `int?`** (giá trị thật `0.000019992`), `JevDecisionResponse`, **`JevDecisionException : BoardModuleException`** (map → **502**, tiền lệ `AiProviderException`). |
| `JevWireModels.cs` | Record **riêng tư** cho shape dây: `JevDecisionsBody(Model, State, Questions, Provider?, SessionId?)`, `JevQuestionWire(Type, Instructions, Criteria)`, `JevDecisionsResponse(Answers, Model, Id, Provider, Usage)`, `JevErrorEnvelope(Error{Code, Message})`. Dùng `JsonSerializerDefaults.Web` + bỏ null (`DefaultIgnoreCondition = WhenWritingNull`) để body gọn. |
| `IJevDecisionProvider.cs` + `JevDecisionHttpProvider.cs` | `Task<JevDecisionResponse> DecideAsync(JevDecisionRequest, CancellationToken)`. **Một** `POST` tới `JevOptions.DecisionsUri`; header `Authorization: Bearer {ApiKey}`, `Content-Type: application/json`, `Accept: application/json`. **KHÔNG retry** (giữ nguyên chủ trương "no retry" của `DeepSeekAiProvider` — Phase 3 §2.2). Mọi lỗi transport / HTTP ≠ 2xx / JSON hỏng ⇒ `JevDecisionException`; thân lỗi cắt ≤ **500** ký tự; snippet parse lỗi ≤ **200** ký tự. **API key không bao giờ vào log hay message ngoại lệ** (có test khẳng định điều này). Đọc `usage.cost` vào `JevUsage.CostUsd` để phục vụ luận điểm "tiết kiệm chi phí" bằng **số đo thật**, không phải ước lượng. |
| `FakeJevDecisionProvider.cs` | Tất định, **0 mạng, 0 token**. Chọn kịch bản bằng marker trong `state`: mặc định ⇒ `choice` tự tin cao; `FAKE:JEV_MED` ⇒ confidence `0.68` + 3 xác suất; `FAKE:JEV_LOW` ⇒ confidence `0.30`; `FAKE:JEV_ERROR` ⇒ ném `JevDecisionException`; `FAKE:JEV_SLOW` ⇒ delay (test wall-clock). **Luôn đọc lại chính các nhãn `criteria` mà caller gửi** để câu trả lời fake không bao giờ lệch khỏi hợp lệ — cùng thủ thuật `FakeAiProvider.BuildObserverFindings` dùng cho Observer (nhờ đó toàn bộ pipeline verify được offline với 0 token). |
| `IJevDecisionService.cs` + `JevDecisionService.cs` | Port nghiệp vụ (System 1). **Không** phụ thuộc EF Core, **không** truy cập entity Board/Task: input là record thuần do caller dựng sẵn (giữ đúng bất biến §0.2). |
| `JevDecisionValidator.cs` | Hàm **thuần**, có test riêng (xem §7). |
| `ConfidenceRouting.cs` | Hàm **thuần**: `confidence` → `RoutingAdvice`. **Cưỡng chế nằm ở đây, KHÔNG tin provider.** |

**Hợp đồng `IJevDecisionService`** (khớp `06-adjust-and-upgrade-with-jev.md` §5.2, có bổ sung cần thiết):

```csharp
namespace TeamNexus.Modules.Ai.Services.Jev;

public interface IJevDecisionService
{
    /// <summary>Triage: Domain + WorkType + Priority + cờ security, một lượt (single-pass).</summary>
    Task<TaskTriageResult> TriageTaskAsync(TaskTriageContext ctx, CancellationToken ct = default);

    /// <summary>Gợi ý người phụ trách theo kỹ năng + tải thực tế.</summary>
    Task<AssigneeRecommendationResult> RecommendAssigneeAsync(
        AssigneeDispatchContext ctx, CancellationToken ct = default);

    /// <summary>Phần A của Observer: mức độ nghiêm trọng + cờ khẩn + rủi ro domino.</summary>
    Task<ObserverRiskAssessmentResult> AssessObserverSignalsAsync(
        ObserverRiskContext ctx, CancellationToken ct = default);

    /// <summary>Phần B của Observer: chọn kịch bản phục hồi.</summary>
    Task<RecoveryActionDecisionResult> DecideRecoveryActionAsync(
        RecoveryActionContext ctx, CancellationToken ct = default);
}
```

**Record kết quả** (khớp tài liệu §5.2; mọi record đều mang `ConfidenceScore` **và** `RoutingAdvice`):

```csharp
public sealed record TaskTriageResult(
    string? SuggestedDomain, string? SuggestedWorkType, string? SuggestedPriority,
    bool NeedsSecurityReview, double ConfidenceScore, string RoutingAdvice);

public sealed record CandidateScore(
    Guid UserId, string DisplayName, double Probability, string? Reason);

public sealed record AssigneeRecommendationResult(
    Guid? RecommendedUserId, double ConfidenceScore,
    IReadOnlyList<CandidateScore> CandidateRankings, string RoutingAdvice);

public sealed record ObserverRiskAssessmentResult(
    string EvaluatedSeverity, bool RequireImmediateAlert, bool HasDominoRisk,
    double ConfidenceScore, string RoutingAdvice);

public sealed record RecoveryActionDecisionResult(
    string? ActionType, Guid? TargetTaskId, Guid? TargetUserId,
    double ConfidenceScore, string RoutingAdvice);
```

**Logic rẽ nhánh theo confidence — CƯỠNG CHẾ TRONG CODE:**

| `confidence` | `RoutingAdvice` | Hành vi bắt buộc |
|---|---|---|
| `≥ HighConfidenceThreshold` (0.85) | `DirectAssign` | Đúng **1** đề xuất; cho phép auto-fill + 1-click Accept |
| `[Medium, High)` (0.50–0.85) | `OfferChoices` | Top **2–3** lựa chọn kèm % xác suất, **không** auto-fill |
| `< MediumConfidenceThreshold` (0.50) | `ManualRequired` / `AwaitingClarification` | **Mọi giá trị đề xuất bị set `null`**, ranking rỗng |

Điểm quan trọng: nhánh được quyết định **sau** khi có câu trả lời, bằng code của ta — nên dù provider (thật hay fake) trả confidence bất thường, hệ thống vẫn không bao giờ "tin mù". Có test ở **đúng biên** 0.85 và 0.50.

**`JevDecisionValidator` (hàm thuần, có test):**
- Mọi nhãn `choice` phải thuộc **tập nhãn server đã gửi** (đối chiếu case-insensitive) — nhãn lạ ⇒ loại.
- `score` phải ánh xạ được vào thang hợp lệ (theo `legend` server cấp, không theo số provider trả).
- Xác suất phải trong `[0, 1]`; tổng xác suất của `choice` phải trong `[0.9, 1.1]`.
- **Bất kỳ chuỗi nào khớp mẫu GUID ⇒ loại bỏ** (phòng thủ theo chiều sâu, dù thiết kế đã loại bỏ khả năng này).
- Vi phạm ⇒ trả **kết quả rỗng + `ManualRequired`**, **không** ném 500. Đây là khác biệt có chủ ý so với Smart Setup (vốn retry rồi 400): Jev là *gợi ý*, không phải *hợp đồng dữ liệu*.

**Đăng ký DI trong `AiModule.AddAiModule`** (theo đúng khuôn `HasApiKey` switch đang dùng cho DeepSeek/Tavily/Resend):
```csharp
services.AddOptions<JevOptions>().Bind(configuration.GetSection(JevOptions.SectionName));

services.AddHttpClient(JevHttpClientName, (provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<JevOptions>>().Value;
    client.Timeout = options.Timeout;
});

// Ba tầng switch, tách biệt hoàn toàn khỏi DeepSeek: thiếu Jev:ApiKey ⇒ fake (offline, 0 token).
services.AddSingleton<IJevDecisionProvider>(provider =>
{
    var options = provider.GetRequiredService<IOptions<JevOptions>>().Value;
    return options.HasApiKey
        ? ActivatorUtilities.CreateInstance<JevDecisionHttpProvider>(provider)
        : ActivatorUtilities.CreateInstance<FakeJevDecisionProvider>(provider);
});

services.AddSingleton<IJevDecisionService, JevDecisionService>();
```
Và **một dòng log** trong `LogResolvedConfiguration` (không chứa secret): `Jev enabled=…, provider=…, model=…, host=…, high=…, medium=…, observerRouting=…, recoveryPlan=…`.

---

### §A2. Trụ cột 1 — Taxonomy chuẩn hóa & Hồ sơ kỹ năng

**`TaxonomyDefaults.cs`** (hàm thuần, có test):
- Domain: `Backend`, `Frontend`, `DevOps`, `Database`, `Mobile`, `UI/UX`, `QA/Testing`.
- Work type: `Feature`, `Bug`, `Refactor`, `TechDebt`, `Security`, `Documentation`.
- `PrimaryRoles`: `Tech Lead`, `Backend Dev`, `Frontend Dev`, `QA Engineer`, `Scrum Master`, `DevOps`, `Member`.

**`TaxonomyService` (`Board/Services/`)** + port `ITaxonomyService`:
- `GetAsync(workspaceId, userId, ct)` — Member+.
- `ReplaceAsync(workspaceId, items, userId, ct)` — Manager+.
- **Seed mặc định LAZY + IDEMPOTENT** khi workspace chưa có row nào: đọc trước, nếu rỗng thì chèn; dựa vào UNIQUE `(workspace_id, kind, key)` + bắt `DbUpdateException`/`ExecuteUpdate` để hai request đồng thời **không nhân đôi** (đúng kỹ thuật race-guard của `WorkspaceAiAgentResolver.EnsureClarificationColumnAsync`).
- Validate: `key`/`label` không rỗng, độ dài theo cột, `color_hex` khớp `^#[0-9A-Fa-f]{6}$` (hoặc null), `kind` thuộc `domain|work_type`.

**Endpoint:**
| Method | Route | Quyền |
|---|---|---|
| `GET` | `/api/workspaces/{id}/taxonomies` | Member+ |
| `PUT` | `/api/workspaces/{id}/taxonomies` | Manager+ |

**`WorkspaceMemberResponse` — CHỈ APPEND** (đúng luật append-only của Phase 10 §4.2 / Phase 11):
```csharp
public sealed record WorkspaceMemberResponse(
    Guid UserId, string DisplayName, string Role, string? AvatarUrl, string MemberType,
    string? Email, DateTimeOffset JoinedAt, bool IsOwner,      // ← 8 field cũ: TÊN + THỨ TỰ không đổi
    IReadOnlyList<string> Skills, string PrimaryRole,           // ← append từ đây
    double CapacityWeight, int MaxWipLimit, string? PersonaKey);
```
> **Vì sao không đổi 8 field đầu:** `useWorkspaceMembers`, dropdown assignee (`KanbanColumn`, `TaskDetailModal`, Smart Setup), và `MembersPage` đều parse chúng.

**Endpoint mới — cập nhật hồ sơ kỹ năng:**
`PUT /api/workspaces/{id}/members/{memberUserId}/profile` — **Admin/Owner**, **cùng gate** với đổi role (`RequireAdminAsync` + `GuardTarget`) ⇒ **từ chối sửa AI Agent** với đúng thông điệp hiện có. Validate:
- `skills`: slug khớp `^[a-z0-9][a-z0-9+._-]{0,39}$`, tối đa **20** kỹ năng, loại trùng.
- `primaryRole`: thuộc `TaxonomyDefaults.PrimaryRoles` (không phân biệt hoa/thường, trả về canonical).
- `capacityWeight ∈ [0.1, 3.0]`; `maxWipLimit ∈ [0, 50]`.
- Ghi `activity_logs` action mới **`MemberSkillsChanged`**.
- **Lưu ý bẫy Phase 10 BUG-1:** dùng `Enum.IsDefined` + helper chặn chuỗi số; **không** dùng `Enum.TryParse` trần.

---

### §A3. Kịch bản 1 — Auto-Triage & Smart Priority

**Endpoint:** `POST /api/boards/{boardId}/tasks/triage` — **Member+**.
- `Jev:Enabled = false` ⇒ **503** (tiền lệ `Reports:Enabled`/`Agent:Enabled`).
- `Jev:ApiKey` rỗng ⇒ dùng `FakeJevDecisionProvider` ⇒ **200** (KHÔNG 503 — fresh clone/CI phải dùng được).
- Body `{ title, description? }`; validate độ dài như `TaskService` (title 1–200, description ≤ `SmartSetupService.MaxTaskDescriptionLength`).
- **KHÔNG ghi gì.** (Không `ai_action_logs`, không `notifications`, không `activity_logs`.)

**Response:**
```jsonc
{ "suggestedDomain": "Backend" | null, "suggestedWorkType": "Bug" | null,
  "suggestedPriority": "Urgent" | null, "needsSecurityReview": false,
  "confidenceScore": 0.92, "routingAdvice": "DirectAssign",
  "candidates": [ { "label": "Backend", "probability": 0.92 } ],   // có khi OfferChoices
  "degraded": false, "model": "typesafe/jev-1.13", "latencyMs": 143 }
```
- Guardrail chi phí: cắt `title + description` theo `Jev:MaxStateChars` **TRƯỚC** khi gọi provider. `state` là object `{ title, description }` — **không** nhét thêm gì (Jev chỉ quyết định trên cái được đưa).
- **Graceful degradation — KHÁC Smart Setup có chủ ý:** provider lỗi/timeout ⇒ **200 với `degraded: true`** và mọi trường đề xuất `null` + warning log. Lý do: triage là **gợi ý tuỳ chọn**, người dùng vẫn tạo task bằng tay được; trả 502 sẽ làm hỏng một luồng không hề phụ thuộc AI. (Smart Setup trả 502 vì kết quả *là* thứ người dùng yêu cầu.)
- `latencyMs` được trả về **để chứng minh DoD < 300 ms bằng số đo thật**, không phải tuyên bố.

**Luồng kích hoạt:** blur input tiêu đề/mô tả trong `TaskDetailModal`/Quick Create. (Issue từ GitHub Webhook thuộc **Phase 15** — không làm ở đây.)

**Frontend:**
- `useJevTriage` — debounce **400 ms**, huỷ request cũ bằng `AbortController`, chỉ gọi khi title ≥ **3** ký tự, **không** gọi khi title rỗng.
- `JevTriagePanel` — hiển thị theo **đúng 3 nhánh**:
  - `DirectAssign` ⇒ badge xanh **"AI tự tin 92%"** + nút **"Áp dụng"** (1 click điền cả 3 field).
  - `OfferChoices` ⇒ `Radio`/`Select`: **"Gợi ý: Backend (84%), Frontend (16%)"** cho Manager chọn.
  - `ManualRequired` ⇒ chữ **"Cần bạn tự chọn"**, **KHÔNG** auto-fill bất cứ thứ gì (có test khẳng định điều này).
  - `degraded === true` ⇒ `Alert` nhỏ **"Gợi ý AI tạm không khả dụng"**; form vẫn dùng bình thường.

---

### §A4. Kịch bản 2 — Smart Assignee Dispatching

**Endpoint:** `POST /api/boards/{boardId}/tasks/{taskId}/assignee-suggestions` — **Member+**; 404 khi thẻ/bảng không khớp (`task.BoardId != boardId` ⇒ 400 theo tiền lệ `RequestTaskScopedAsync`).

**`AssigneeDispatchContext` server dựng:**
- Task: `title`, `description` (đã cắt theo `Jev:MaxStateChars`).
- Tối đa `Jev:MaxCandidates` thành viên (`human` + `ai_agent`, **loại** thành viên đã rời workspace), mỗi người: `DisplayName`, `skills`, `primaryRole`, `capacityWeight`, `maxWipLimit`, `openTaskCount`, `overdueCount`, `inProgressCount`.
- **TUYỆT ĐỐI KHÔNG gửi `email`** (dữ liệu cá nhân không cần cho quyết định) — có test khẳng định.

**Cơ chế chống rò rỉ ID (điểm cốt lõi của §0.3):**
- Nhãn `criteria` là **`cand-1` … `cand-N`**; server giữ `Dictionary<string, Guid>` ánh xạ nhãn → `UserId` **chỉ trong bộ nhớ của request** (biến local, không cache, không log).
- Response chỉ chứa `displayName` + `probability` + `reason`; `userId` xuất hiện ở field có kiểu `Guid`.

**Cưỡng chế ngưỡng TRONG CODE** (kể cả khi fake trả confidence cao bất thường): `confidence < 0.50` ⇒ `candidateRankings = []` **và** `recommendedUserId = null`.

**KHÔNG** đi qua Accountability Layer — vì **không ghi gì**. Việc áp dụng gợi ý là thao tác `PATCH` task **hiện có** của người dùng.

**Frontend:** nút **"Gợi ý người làm"** trong `TaskDetailModal`; 3 nhánh hiển thị như §A3; tooltip lý do lấy từ `CandidateScore.Reason` (ví dụ *"Phù hợp kỹ năng .NET & tải hiện tại thấp"*).

---

### §A5. Kịch bản 3 — AI Observer Toàn diện (Phần A + B)

#### Phần A — Đánh giá Mức độ Nghiêm trọng (Risk Severity & Anomaly Filtering)

- `ObserverService` **đổi vai trò của LLM**: **Jev chấm `severity` + cờ khẩn + rủi ro domino**, rồi **chỉ khi** có finding đạt `MinSeverityToNotify` mới gọi **System 2** để *viết giải thích / lời khuyên*. Đúng tinh thần "phân loại bằng Jev, giải thích bằng LLM".
- **Cờ rollback:** `Jev:ObserverSeverityRouting = true` bật đường mới; `false` giữ **NGUYÊN** đường cũ (`IAiProvider` + `ObserverFindingValidator`) ⇒ quay lại Phase 14 **bằng 1 dòng config**, không cần deploy lại code. Đây là điều kiện để thay đổi này an toàn trên production.
- Jev trả về: `score` cho `severity` (`Low/Medium/High/Critical`) + `noul` cho `RequireImmediateEscalation` + `noul` cho `SprintDelayDominoRisk` (đúng 3 câu hỏi như tài liệu §3.3 Phần A).
- **KHÔNG nới bất kỳ kiểm tra nào hiện có.** Giá trị severity do Jev chấm **vẫn** phải qua `ObserverFindingValidator`: Jev chấm severity của *cả tập tín hiệu*, validator vẫn là thứ quyết định finding nào được ghi. `NotificationTypes.All` (5 loại) vẫn là **whitelist chống ảo giác**.
- `ai_observer_runs.summary` ghi thêm: `jevUsed`, `jevConfidence`, `jevInputTokens`, `jevCostUsd` — bên cạnh `signalsPreserved`/`signalsDroppedBeforePrompt` đã có từ Phase 14.
- **Lợi ích định lượng:** triệt tiêu hoàn toàn nỗi lo LLM rò rỉ GUID/bịa task ID trong khâu **đánh giá**, và chi phí quét nền giảm mạnh (output token miễn phí + chỉ tính input token).

#### Phần B — Định tuyến Kịch bản Phục hồi Chủ động (Proactive Auto-Healing Routing)

- **`Jev:AutoRecoveryPlanEnabled = false` mặc định** (tiền lệ `Digest:Enabled`) — tính năng này **sinh** một đề xuất tác động dữ liệu, nên bật phải là cài đặt tường minh.
- Khi bật **và** một tín hiệu đạt `High`/`Critical`: gọi `DecideRecoveryActionAsync` với `criteria` = `ReassignTask | SplitSubtasks | ExtendDeadline | EscalateManager` (`choice`).
- **`AiActionTypes` thêm đúng MỘT action type: `RecoveryPlan`**, với `kind` nằm **trong payload** (`after_snapshot.kind`). Lý do **không** tạo 4 action type riêng: `ai_action_logs.action` là text tự do nhưng lịch sử AI và bộ lọc `?action=` trở nên phân mảnh; một action type + `kind` giữ được một luồng đọc duy nhất.
- **`IAiActionService.RequestRecoveryPlanAsync(Guid workspaceId, string afterSnapshotJson, string basisJson, Guid userId, CancellationToken)`** — theo **đúng khuôn** `RequestCreateBoardFromTemplateAsync`:
  - Scope **`entity_type = 'Workspace'`** (vì một tín hiệu có thể trải nhiều board; `ai_action_logs` không có `workspace_id`, xem A1.1 Phase 14.5).
  - `RequireManagerAsync(workspaceId, userId, ct)`; workspace không tồn tại ⇒ 404.
  - Chỉ ghi **MỘT** row `Pending`. Không ghi gì khác.
- **`RecoveryPlanApplier : IAiActionApplier`** (`ActionType = "RecoveryPlan"`) — file mới trong `Services/Appliers/`:

| `kind` | `ApplyAsync` | `UndoAsync` |
|---|---|---|
| `ReassignTask` | Đổi `tasks.assignee_id`; lưu `beforeAssigneeId` + `afterAssigneeId` vào `applied_snapshot` | Khôi phục `assignee_id` cũ |
| `ExtendDeadline` | Đổi `tasks.due_date` (đi qua `ToUtc(...)` — bẫy **BUG-2** Phase 10) | Khôi phục `due_date` cũ |
| `SplitSubtasks` | **UỶ QUYỀN** cho `CreateSubtasksApplier` đang có — **không** viết lại logic | Uỷ quyền undo của `CreateSubtasksApplier` (soft-delete thẻ đã tạo) |
| `EscalateManager` | **KHÔNG ghi gì**; trả `Warnings = ["Cần họp khẩn — không có thay đổi dữ liệu nào được áp dụng."]` | **No-op thành công** (không có gì để hoàn tác) |

  > **`EscalateManager` là ca đặc biệt BẮT BUỘC có test.** Nếu không, nó trở thành "đường ghi dữ liệu rỗng" mà không ai phát hiện: Manager bấm duyệt, thấy thành công, nhưng không có gì xảy ra và không có cảnh báo.

  > **⛔ KHÔNG mở transaction riêng trong applier.** `AiActionService.ApproveAsync` **đã** mở transaction; đây chính là bẫy **P2** của Phase 14.5 (`MoveTaskAsync` trả 500 vì `InvalidOperationException`). Mọi thay đổi nằm trong **một** `SaveChangesAsync` ⇒ nguyên tử không đổi.
- **Thông báo & UI:** `NotificationService` nhận thêm một finding family cho recovery plan; nút **"Phê duyệt giải pháp"** 1 click trong Notification Center / `AiActionHistoryDrawer`, kèm nút `Undo`.
- **Không cần endpoint mới:** `GET /api/workspaces/{id}/ai-actions?status=Pending&action=RecoveryPlan` đã hoạt động nhờ **A1.1 của Phase 14.5**.

---

## 5. TRACK B — Multi-Agent Personas, ReAct mở rộng, RAG pgvector

### §B1. Đội ngũ AI Chuyên gia (nhiều agent row thật)

**`PersonaCatalog.cs`** (hàm thuần, có test):

| `persona_key` | `DisplayName` | Skill mặc định |
|---|---|---|
| `general` | `TeamNexus Agent` | `[]` (**backward-compat** với Phase 7) |
| `scrum_master` | `AI Scrum Master` | `["user-story","acceptance-criteria","sprint-planning"]` |
| `tech_lead` | `AI Tech Lead` | `["architecture","code-review","api-design"]` |
| `qa_security` | `AI QA & Security` | `["test-cases","security-audit","threat-modeling"]` |

**Mở rộng port Board `IAiAgentResolver` — thêm ĐÚNG MỘT method, giữ nguyên 3 method cũ:**
```csharp
/// <summary>Row agent của một persona, tạo lazily. personaKey phải thuộc PersonaCatalog.</summary>
Task<Guid> EnsurePersonaAgentAsync(Guid workspaceId, string personaKey, CancellationToken ct = default);
```
`NullAiAgentResolver` cập nhật tương ứng (ném `NotSupportedException`, giống hai method write-ish khác — **không** trả `Guid.Empty`).

**`WorkspaceAiAgentResolver`:**
- `EnsureAgentAsync` **giữ nguyên hành vi cũ** ⇒ trỏ `persona_key = 'general'`. **Không có đường gọi nào của Phase 7 đổi ý nghĩa.**
- `EnsurePersonaAgentAsync` dùng **CÙNG** kỹ thuật race-guard hiện có (transaction + bắt `DbUpdateException` + re-read người thắng), chỉ khác: khoá `(workspace_id, persona_key)`, `UserName = $"ai-agent-{personaKey}-{workspaceId:N}"`. `ApplicationUser` vẫn **không thể đăng nhập** (no password hash, no email, lockout + 2FA, no `user_logins`).
- **`IsAiAgentAsync` KHÔNG ĐỔI** — vẫn chỉ kiểm `member_type = 'ai_agent'`. Hệ quả quan trọng: mọi persona đều được nhận diện là agent trong `TaskService`, `CommentService`, `BoardService`, `TaskSearchService`, `TaskReadHelpers`, `AgentRunOrchestrator` **mà không phải sửa file nào của các module đó**. Đây chính là lý do việc nới index ở §3.1 lại rẻ như vậy.

**`RequestAgentOutputAsync` — phải rà và siết:**
- Assert hiện tại (`task.AssigneeId == agentUserId` + `MemberType == AiAgent` của **chính user đó**) vẫn đúng khi task gán cho **một** persona.
- **Bổ sung:** thông điệp lỗi phải nêu **rõ persona** của assignee để người vận hành không phải đoán.
- **Test bắt buộc:** gán task cho `qa_security`, chạy orchestrator cho `qa_security` ⇒ `Pending` tạo được; chạy cho `tech_lead` ⇒ **403**.

**Persona trong Agent Chat:**
- Query param **`?persona=`** (whitelist `PersonaCatalog`), mặc định `general` ⇒ hành vi cũ y nguyên. `AgentChatOptions.DefaultPersona` mới.
- `AgentChatPrompts.BuildSystemPrompt(contextBlock, personaKey)` **chỉ chèn MỘT khối `VAI TRÒ:` ngay đầu**.
  > ⛔ **`AgentChatPrompts.Marker` (`"AI Agent Chat của TeamNexus"`) PHẢI GIỮ NGUYÊN trong thân prompt.** Marker này là **bộ chọn nhánh** của `FakeAiProvider.StreamAsync`. Đổi nó sẽ làm test offline kiểm **sai nhánh** — đúng loại lỗi mà `phase-14-6-ai-agent-enhancements.md` §4.3 đã cảnh báo (lỗi xanh/đỏ giả). Chỉ được **append**, không được thay.
- `?persona=` **không** đổi shape SSE: vẫn `meta` → `delta`* → `done` | `error`, vẫn `done.answer == Σ delta` (bất biến Phase 14.5 **P1**).
- UI: `Select` persona ở header `AgentChatPanel`, gửi theo **từng lượt**; nhãn persona hiển thị **trên message của lượt đó** (không suy diễn ngược cho lịch sử cũ — persona chưa được lưu vào `agent_chat_messages` và giai đoạn này **không** thêm cột, để tránh migration thứ ba).
- `useAgentRuns` + `TaskCard` hiển thị **tên persona** thay vì `"TeamNexus Agent"` chung chung.

### §B2. Mở rộng Tool-calling (ReAct Pattern)

Thêm **3 tool** theo đúng khuôn `IAgentTool` + `AgentToolRegistry` + `AgentToolDefinitions`:

| Tool | Chức năng | Ghi chú hiện thực |
|---|---|---|
| `InspectMemberWorkload` | Khối lượng hiện tại của thành viên (open/overdue/in-progress, `maxWipLimit`, `capacityWeight`) | **TÁI DÙNG** truy vấn của §A4 — không viết lại, để hai đường không thể lệch số |
| `MatchSkills` | Truy vấn `workspace_members.skills` theo slug, xếp hạng mức khớp | **Không** gọi Jev ⇒ test được offline trong suite thuần |
| `ValidateAcceptanceCriteria` | Kiểm tra output nháp có thỏa mô tả task không | Gọi `IJevDecisionService` với câu hỏi `noul`. Khi Jev tắt ⇒ trả `"Không xác minh được (Jev đang tắt)"` — **KHÔNG** ném lỗi, để run không chết vì một tool tuỳ chọn |

- `AgentToolDefinitions` + prompt `AgentPrompts` cập nhật liệt kê tool mới.
- **Guardrail hiện có KHÔNG ĐỔI:** `MaxToolCalls 15` / `RunTimeoutSeconds 300` / `MaxRunTokens 50000` / `MaxRunLlmCalls 20`. Tool mới tiêu **cùng** ngân sách đó — có test khẳng định thêm tool **không** nới trần.
- Tool thứ 4 đã có sẵn từ 14.6 §B3 (`SearchProjectContext`) **không** làm vì `SearchSystemData` (Phase 7) đã phủ chức năng — ghi vào "nợ kỹ thuật có chủ ý" thay vì thêm tool trùng.

### §B3. Bộ nhớ Dự án Ngữ nghĩa (Workspace RAG với pgvector)

- **`Jev:RagEnabled = false` mặc định.**
- **Port Board `IWorkspaceKnowledgeStore`** — chỉ khai báo **hợp đồng dữ liệu**, **không** kéo Ai vào Board:
  ```csharp
  Task<IReadOnlyList<KnowledgeChunk>> GetTopChunksAsync(
      Guid workspaceId, ReadOnlyMemory<float> queryVector, bool onlyReady,
      int maxDocuments, CancellationToken ct = default);
  ```
  Giữ bất biến §0.2 (`Board → Ai` bị cấm), giống tiền lệ `IDailyDigestService`.
- **`KnowledgeBaseService` (phía Ai):**
  - `CreateDocumentAsync` (Manager+) ⇒ chunk theo đoạn/dòng, trần `Jev:RagMaxChunksPerDocument` (**200**) và `Jev:RagChunkChars` (**1200**), ghi `status = 'pending'`.
  - `RefreshEmbeddingsRunner` — **scoped**, **gọi trực tiếp được từ test** (tiền lệ `DailyDigestRunner` / `AgentRunReaperRunner` / `AgentChatRetentionRunner`), chạy theo batch cho chunk `embedding IS NULL`, cập nhật `status` (`ready`/`failed` + `error`). **Hosted service chỉ quyết định KHI NÀO gọi runner** — để test không phải chờ timer.
  - Truy vấn: `ORDER BY embedding <=> @queryVector LIMIT @k` (cosine). **Chỉ chạy khi tài liệu `status = 'ready'`**; nếu không, RAG trả **rỗng im lặng** (không ném).
- **Port embedding `IEmbeddingProvider`** + `OpenRouterEmbeddingProvider` (OpenAI-compatible `POST /api/v1/embeddings`, `IHttpClientFactory` client riêng `"Embeddings"`).
  Cấu hình dưới section **`Embeddings`** — **TÁCH khỏi `Jev`** vì (a) endpoint khác, (b) provider có thể không phải OpenRouter:
  `Enabled = false`, `ApiKey`, `BaseUrl = "https://openrouter.ai/api/v1"`, `Model`, `Dimensions = 1536`, `TimeoutSeconds`.
- **Giảm thiểu R3:** (1) `Embeddings:Enabled=false` mặc định; (2) provider lỗi ⇒ chunk ở lại `pending` + ghi `error`, **không** làm hỏng request người dùng; (3) `FakeEmbeddingProvider` sinh vector **tất định** từ hash chuỗi (cùng chuỗi ⇒ cùng vector) để test RAG tất định offline; (4) nếu OpenRouter không phục vụ `/embeddings`, chỉ cần đổi `Embeddings:BaseUrl` + `Model` — **0 dòng code**.
- **Endpoints:**
  | Method | Route | Quyền | Ghi chú |
  |---|---|---|---|
  | `GET` | `/api/workspaces/{id}/knowledge/documents` | Member+ | RAG off ⇒ `[]` + `enabled: false` |
  | `POST` | `/api/workspaces/{id}/knowledge/documents` | Manager+ | ⇒ **`Pending`** action `CreateKnowledgeDocument` |
  | `DELETE` | `/api/workspaces/{id}/knowledge/documents/{docId}` | Manager+ | ⇒ **`Pending`** action |
- **`CreateKnowledgeDocumentApplier : IAiActionApplier`** (`ActionType = "CreateKnowledgeDocument"`): Approve mới ghi row; Undo = **soft-delete tài liệu** (`status = 'deleted'` — dùng `status` hiện có, **không** thêm cột `deleted_at` để tránh migration thứ ba).
  > **Đây là đường GHI DỮ LIỆU ⇒ BẮT BUỘC đi Accountability Layer.** Không được "tiện tay" ghi thẳng vì nó "chỉ là tài liệu".
- **RAG vào prompt:** chèn khối `KIẾN THỨC DỰ ÁN` vào `AgentChatPrompts.BuildContextBlock` **TRƯỚC** khối digest, trần `Jev:RagMaxContextChars = 3000`. Cắt bằng **hàm thuần theo dòng nguyên vẹn** — cùng thủ thuật `MaxDigestChars` của Phase 14.6 §A4. **KHÔNG chứa GUID.**

---

## 6. Hợp đồng API mới (Flutter Phase 16 dùng lại nguyên)

| Method | Route | Quyền | Ghi chú |
|---|---|---|---|
| `GET` | `/api/workspaces/{id}/taxonomies` | Member+ | Seed lazy |
| `PUT` | `/api/workspaces/{id}/taxonomies` | Manager+ | Thay cả tập |
| `PUT` | `/api/workspaces/{id}/members/{memberUserId}/profile` | Admin/Owner | **Từ chối AI Agent** |
| `POST` | `/api/boards/{boardId}/tasks/triage` | Member+ | **Không ghi gì**; 503 khi tắt; `degraded` khi lỗi provider |
| `POST` | `/api/boards/{boardId}/tasks/{taskId}/assignee-suggestions` | Member+ | **Không ghi gì** |
| `GET` | `/api/workspaces/{id}/knowledge/documents` | Member+ | RAG off ⇒ `[]` + `enabled:false` |
| `POST` | `/api/workspaces/{id}/knowledge/documents` | Manager+ | 201 `Pending` `CreateKnowledgeDocument` |
| `DELETE` | `/api/workspaces/{id}/knowledge/documents/{docId}` | Manager+ | 201 `Pending` action |
| `?persona=` | `/api/boards/{id}/agent-chat/stream` | Member+ | **CHỈ append query param** — shape SSE và `meta` **KHÔNG đổi** |

**KHÔNG ĐỔI (đã verify, không được chạm):** `TaskResponse`, `BoardResponse`, `ColumnResponse`, `CommentResponse`, `NotificationResponse`, `ReportSummaryResponse`, `AiActionLogResponse`, `AiActionLogDetailResponse`, payload SignalR.
**Chỉ APPEND CUỐI:** `WorkspaceMemberResponse` (§A2), `DashboardResponse` (không chạm ở giai đoạn này).
**Query param mới là additive:** vắng `?persona=` ⇒ hành vi **y hệt** trước.

---

## 7. Ma trận Kiểm thử

### 7.1. Backend — file mới trong `tests/TeamNexus.Api.Tests/`

| File | Nội dung bắt buộc |
|---|---|
| `Pure/JevDecisionModelsTests.cs` | Ngưỡng confidence: `≥0.85` ⇒ `DirectAssign` + đúng 1; `[0.5,0.85)` ⇒ `OfferChoices` + ≤3; `<0.5` ⇒ **mọi đề xuất `null`**. Test ở **ĐÚNG BIÊN** `0.85` và `0.50`. `High > Medium` bị cưỡng chế khi cấu hình ngược. Clamp mọi knob. |
| `Pure/JevDecisionValidatorTests.cs` | Nhãn lạ ⇒ loại; xác suất ngoài `[0,1]` ⇒ loại; tổng xác suất lệch ⇒ loại; **chuỗi khớp mẫu GUID bị lọc**; **mọi đầu vào lỗi ⇒ kết quả rỗng + `ManualRequired`, KHÔNG ném 500**. |
| `Pure/FakeJevDecisionProviderTests.cs` | Mọi kịch bản `FAKE:JEV_*` trả đúng shape; fake **chỉ** trả nhãn có trong `criteria`; tất định qua 2 lần chạy liên tiếp. |
| `Pure/ConfidenceBranchingTests.cs` | Bảng chân trị **3 nhánh × 4 use case** (triage, assignee, severity, recovery action). |
| `Pure/TaxonomyDefaultsTests.cs` | Seed mặc định; slug/role whitelist; validate profile (20 kỹ năng, `capacityWeight [0.1,3.0]`, `maxWipLimit [0,50]`); `color_hex` regex. |
| `Pure/PersonaCatalogTests.cs` | 4 persona có display name + skill mặc định; persona lạ ⇒ từ chối; `general` có skill rỗng (backward-compat). |
| `Pure/RecoveryPlanPayloadTests.cs` | Parse `after_snapshot` cho **cả 4** `kind`; `EscalateManager` ⇒ danh sách thay đổi **rỗng**; round-trip jsonb. |
| `Pure/KnowledgeChunkingTests.cs` | Tôn trọng **cả hai** trần; **KHÔNG** cắt giữa surrogate pair (tái dùng bài học `FakeAiProvider.SplitIntoChunks`); tất định. |
| `Pure/FakeEmbeddingProviderTests.cs` | Cùng chuỗi ⇒ cùng vector; đúng `Dimensions`; chuỗi rỗng **không** ném. |
| `Integration/Phase147JevApiTests.cs` | Triage 200/503/shape; **`degraded ⇒ 200 + degraded:true`** khi provider ném; assignee 3 nhánh; **`userId` không rò sang field văn bản**; **API key không xuất hiện trong log/message ngoại lệ**. |
| `Integration/Phase147PersonaTests.cs` | 4 persona tạo được cho **cùng** workspace (index mới chạy đúng); tạo trùng persona **không** nhân đôi; `persona_key` của row cũ = `'general'`; `IsAiAgentAsync` **true cho mọi persona**; **403 khi agent chạy task của persona khác**; chat `?persona=` **không** đổi shape SSE. |
| `Integration/Phase147RecoveryPlanTests.cs` | Observer sinh **đúng 1** row `Pending`; Approve đổi `assignee_id`/`due_date`; Undo khôi phục **đúng** giá trị trước; `EscalateManager` ⇒ **0 thay đổi dữ liệu** + có warning; Approve hai lần ⇒ **409**; **KHÔNG** transaction lồng nhau (hồi quy bẫy **P2** Phase 14.5). |
| `Integration/Phase147KnowledgeTests.cs` | CRUD ⇒ `Pending` `CreateKnowledgeDocument`; **Approve mới** tạo row; Undo soft-delete; RAG off ⇒ `[]`; thiếu extension `vector` ⇒ **skip có thông điệp**; truy vấn cosine trả đúng chunk gần nhất bằng fake embedding. |
| `Integration/Phase147TaxonomyApiTests.cs` | Seed lazy; PUT Manager+ / **403** Member; validate `color_hex`. |
| `Integration/Phase147MemberSkillsApiTests.cs` | PUT profile Admin-only (403 Manager); **400 khi sửa AI Agent**; slug sai ⇒ 400; ghi `activity_logs`. |
| `Infrastructure/ScriptedJevProvider.cs` | `IJevDecisionProvider` kịch bản hoá (tiền lệ `ScriptedAiProvider`): chỉ định từng answer/confidence, **đếm số lần gọi** (0 lần chứng minh guard chặn **trước** provider), ném lỗi được. |
| `Infrastructure/TeamNexusApiFactory.cs` (sửa) | Thêm `ScriptedJev` + `JevEnabled` + `RagEnabled` override qua `ConfigureTestServices` (chạy **sau** đăng ký của app nên thắng resolve). |

### 7.2. Frontend (Vitest + React Testing Library)

`useJevTriage.test.ts` (debounce 400 ms, huỷ request cũ, `degraded`) ·
`JevTriagePanel.test.tsx` (**3 nhánh hiển thị**; khẳng định **KHÔNG** auto-fill ở nhánh thấp; `degraded` ⇒ `Alert`) ·
`AssigneeSuggestion.test.tsx` (3 nhánh + tooltip lý do) ·
`AgentChatPanel.persona.test.tsx` (đổi persona ⇒ `?persona=` trong URL; **frame SSE không đổi**) ·
`RecoveryPlanCard.test.tsx` (Approve/Reject/Undo; `EscalateManager` hiện cảnh báo) ·
`KnowledgeBasePage.test.tsx` (upload, trạng thái `pending`/`ready`/`failed`, xoá) ·
`WorkspaceSettings.taxonomy.test.ts` + `MemberSkillsForm.test.tsx` ·
`confidenceHelpers.test.ts`.

### 7.3. Cổng chốt — phải chạy THẬT và ghi SỐ ĐO THẬT

| Cổng | Ngưỡng |
|---|---|
| `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` | **0 warning / 0 error** |
| `dotnet test` (PostgreSQL thật) | **Failed 0 / Skipped 0**, total mới (baseline **665** ⇒ ước tính **+55…+70**) |
| `dotnet ef migrations list` | **13** |
| `has-pending-model-changes` | **sạch** |
| `npm test` | **Failed 0 / Skipped 0** (baseline **657 / 110 file** ⇒ ước tính **+35…+45 / +8 file**) |
| `npm run lint` | **0 warning / 0 error** |
| `npx tsc -b` | **exit 0** |
| `npm run build` | **OK** |
| `ci-backend.yml` / `ci-web.yml` | Nâng số **bằng số đo thật**, không ước lượng |
| **Chạy một lượt KHÔNG-Docker** | Xác minh cơ chế skip mới hoạt động và **KHÔNG** làm xanh giả |

---

## 8. Nhật ký lệch kế hoạch (điền khi thi hành — để không bị coi là sai)

> Mục này **để trống** khi bắt đầu. Điền mọi phát sinh/lệch trong lúc làm, theo đúng khuôn `phase-14-6-ai-agent-enhancements.md` §4.

| # | Phát sinh | Cách xử lý | Lý do |
|---|---|---|---|
| **L1** | 2 migration (khác 14/14.6 Track A: 0 migration) | `Phase147AgentPlatform` + `Phase147KnowledgeBase` | Hồ sơ kỹ năng/taxonomy/persona cần cột + bảng thật; RAG pgvector cần extension + cột `vector` + index HNSW |
| **L2** | Đổi image CI `postgres:18` → `pgvector/pgvector:pg18` | §3.3 | Extension `vector` không có trong image chính thức; đã xác minh image pgvector có |
| **L3** | **Nới index** `uq_workspace_members_ai_agent` (đúng 1 agent/workspace) | §3.1 | Yêu cầu Multi-Agent Personas với **row agent riêng** cho mỗi persona. Backfill `'general'` giữ nguyên hành vi Phase 7 |
| **L4** | `EscalateManager` là no-op có chủ ý | §A5 | Không thể tự giải quyết ⇒ phải họp khẩn; "thành công im lặng" là hành vi **có chủ ý** và **có test** |
| **L5** | Bỏ tool `SearchProjectContext` (14.6 §B3) | §B2 | `SearchSystemData` (Phase 7) đã phủ chức năng — ghi thành **nợ kỹ thuật có chủ ý**, không thêm tool trùng |
| **L6** | Persona **không** được lưu vào `agent_chat_messages` | §B1 | Tránh migration thứ ba; transcript cũ không suy diễn ngược persona |

---

## 9. Tài liệu & Cấu hình

### 9.1. Cấu hình mới trong `src/TeamNexus.Api/appsettings.json`

Ba section, kèm comment lý do như tiền lệ:

- **`"Jev"`**: `Enabled`, `ApiKey` (secret), `BaseUrl = "https://openrouter.ai"`, `DecisionsPath = "/api/alpha/decisions"`, `Model = "typesafe/jev-1.13"`, `TimeoutSeconds = 20`, `HighConfidenceThreshold = 0.85`, `MediumConfidenceThreshold = 0.50`, `MaxStateChars = 12000`, `MaxCandidates = 12`, `MaxQuestionsPerRequest = 6`, `ObserverSeverityRouting = true`, `AutoRecoveryPlanEnabled = false`, `RagEnabled = false`, `RagChunkChars = 1200`, `RagMaxChunksPerDocument = 200`, `RagMaxContextChars = 3000`, `RagEmbeddingDimension = 1536`.
- **`"Embeddings"`**: `Enabled = false`, `ApiKey`, `BaseUrl = "https://openrouter.ai/api/v1"`, `Model`, `Dimensions = 1536`, `TimeoutSeconds`.
- **`"AgentChat"`**: thêm `DefaultPersona = "general"`.

Bật/tắt an toàn: `ObserverSeverityRouting = false` ⇒ quay lại Phase 14. `Jev:Enabled = false` ⇒ 503 ở triage/assignee. `AutoRecoveryPlanEnabled`/`RagEnabled`/`Embeddings:Enabled = false` mặc định.

### 9.2. Cấu hình secret

```powershell
# Local (User Secrets) — Jev dùng CHUNG OpenRouter API key, không có key TypeSafe riêng
dotnet user-secrets set --project src/TeamNexus.Api "Jev:ApiKey" "sk-or-v1-..."
dotnet user-secrets set --project src/TeamNexus.Api "Embeddings:ApiKey" "sk-or-v1-..."

# Production (Render) — biến môi trường
# Jev__ApiKey, Jev__Enabled=true, Jev__ObserverSeverityRouting=true
# Embeddings__ApiKey, Embeddings__Enabled=true
```

### 9.3. Cập nhật tài liệu `project-docs/`

| File | Thay đổi |
|---|---|
| `tasks/phase-14-7-autonomous-agent-jev.md` | **Tài liệu này** — nguồn chia task chính |
| `03-roadmap.md` §14.7 | Tick DoD, ghi **3 lệch** (2 migration, đổi index agent, đổi image CI) + số đo thật |
| `01-system-specification.md` §16 | Mục mới: Hybrid AI, ngưỡng confidence, Recovery Plan, persona, RAG |
| `02-tech-stack-decisions.md` §2.10 + §3 | Client Jev + `POST /api/alpha/decisions` + rủi ro alpha + image `pgvector/pgvector:pg18` |
| `06-adjust-and-upgrade-with-jev.md` §5.1/§5.2 | Cập nhật theo hợp đồng **thật** đã hiện thực |
| `tasks/phase-14-6-ai-agent-enhancements.md` §2 | Đánh dấu Track B **đã hợp nhất vào 14.7** |
| `report/phase-14-7-autonomous-agent-jev-test-report.md` | Báo cáo nghiệm thu (**tạo sau** khi có số đo thật) |

---

## 10. Giá trị học thuật & điểm cộng IT got Talent (đã có bằng chứng đo được)

1. **Khắc phục triệt để điểm yếu của "Chatbot AI đại trà":** dùng **đúng mô hình cho đúng bài toán** — không dùng cỗ máy sinh token cồng kềnh để phân loại.
2. **Hiện thực hóa System 1 / System 2 của Kahneman trong kiến trúc phần mềm:** Jev phản xạ để định tuyến/lọc; LLM chỉ suy ngẫm sâu khi cần.
3. **Số đo định lượng kiểm chứng được:** `latencyMs` trả về từ API (mục tiêu < 300 ms), `usage.cost` **thật** từng lượt (`JevDecisionValidator` + `ai_observer_runs.summary`), **0 % lỗi ảo giác cấu trúc** (đầu ra là typed data, không phải JSON sinh ra để rồi phải parse).
4. **Tính khả thi và sẵn sàng Production:** `Graceful Degradation` + tương thích ngược + mọi cờ rollback bằng config, hệ thống vẫn chạy ổn định trên Internet thật.

---

## 11. NHẬT KÝ THI HÀNH TRACK A (§A1–§A5) — phát sinh & lệch kế hoạch

> **Số ĐO THẬT:** backend **912 / 0 / 0** (baseline 665 ⇒ **+247**) · `migrations list` = **12** (mới: `20261014163751_Phase147AgentPlatform`) · `has-pending-model-changes` = **sạch** · `dotnet build --no-incremental` **0/0** · FE **657 / 110 file** (không đổi) · lint **0/0** · `tsc -b` exit **0** · `npm run build` **OK**.

### 11.1. ✅ Đã đạt DoD của Track A

| # | Nội dung | Trạng thái |
|---|---|---|
| 1 | `IJevDecisionService` + `FakeJevDecisionProvider` chạy thật, kiểm thử độc lập, **0 token** | ✅ |
| 2 | Schema: `workspace_taxonomies` + `skills`/`primary_role`/`capacity_weight`/`max_wip_limit` (migration sạch) | ✅ |
| 3 | Triage tự động gợi ý phân loại + độ khẩn cấp | ✅ (đo `latencyMs` trả về trong response) |
| 4 | Gợi ý assignee tuân thủ **nghiêm ngặt** rẽ nhánh theo confidence | ✅ (3 nhánh có test ở **đúng biên**) |
| 5 | Observer sinh Recovery Plan `Pending`; Approve/Reject/Undo thành công | ✅ |
| 6 | Test suite backend + frontend tiếp tục pass 100 %, không vi phạm Accountability Layer | ✅ **912/0/0** + **657/0/0** |

### 11.2. 🐞 5 BUG THẬT bắt được trong lúc hiện thực (giá trị lớn nhất của giai đoạn)

| # | Bug | Vì sao test bắt được & cách sửa |
|---|---|---|
| **P1** | **Deadlock `40P01` khi seed taxonomy lần đầu đồng thời.** Hai request cùng seed 13 row ⇒ hai transaction khoá index theo thứ tự khác nhau ⇒ PostgreSQL kill một cái ⇒ **500**. | Đây là ca **bình thường**, không phải biên: lần đầu mở workspace rất dễ có 2 người/2 tab. Bản đầu dùng "13 lệnh `Add` có theo dõi + bắt `23505`" — **sai**, vì deadlock **không phải** unique violation nên không guard nào bắt được. Sửa: **một lệnh `INSERT … ON CONFLICT (workspace_id, kind, key) DO NOTHING` cho mỗi kind** ⇒ không thể deadlock, không cần savepoint, không bẩn ChangeTracker. Unique index vẫn là thứ bảo đảm tính đúng. |
| **P2** | **Jev directive làm Observer ngừng cảnh báo.** Nhét directive vào **user prompt** ⇒ `FakeAiProvider` parse `marker + JSON` bằng `JsonDocument.Parse` ⇒ **throw** ⇒ trả **0 finding** ⇒ `NotifyManagersAsync` không chạy ⇒ **không còn alert nào**, mà endpoint vẫn 200. | Đúng loại "xanh giả": tính năng chết im lặng. Sửa: chuyển directive sang **system prompt** (không chứa cấu trúc nào bị máy đọc). Khoá bằng test `BuildRequest_PutsTheDirectiveInTheSystemPromptAndLeavesTheUserPromptAsPureJson` — test này **parse lại** đúng chuỗi JSON của user prompt. |
| **P3** | **`applied_snapshot` của RecoveryPlan `SplitSubtasks` mất `createdTaskIds`.** `AiActionService` chỉ lưu kết quả **ngoài**; `RecoveryPlanApplier` khi uỷ quyền cho `CreateSubtasksApplier` đã **nuốt** id mà delegate trả về ⇒ **Undo xoá 0 thẻ** và báo thành công. | Undo im lặng không làm gì là kiểu hỏng tệ nhất ở Accountability Layer. Sửa: **mang kết quả delegate lên** qua `result with { … }`. Thêm nữa `UndoAsync` phải **thu hẹp context về board** (`ctx with { BoardId = … }`) vì action ngoài là workspace-scoped, còn `CreateSubtasksApplier.UndoAsync` gọi `RequireBoardId()` ⇒ nếu không sẽ thành action đã duyệt mà **vĩnh viễn không thể hoàn tác**. Có test end-to-end cho cả approve **và** undo. |
| **P4** | **Observer gửi `workspaceId` cho Jev** trong trường `WorkspaceName` (tôi tự viết ra khi dựng context). | Vi phạm chính bất biến §0.3 mà giai đoạn này tuyên bố. Tự phát hiện khi rà lại; sửa thành `LoadWorkspaceNameAsync` (tên hiển thị) và ghi rõ trong XML doc để không tái diễn. |
| **P5** | **`TestScenario.AsUserAsync` ghi cookie vào **jar dùng chung của scenario**.** Dựng 2 client trước rồi mới gọi ⇒ lời gọi thứ hai **ghi đè** token của người thứ nhất ⇒ client A hành động như user B ⇒ **404** trông y như bug phân quyền. | Không phải bug production (đúng như thiết kế của harness) nhưng là **bẫy tốn thời gian nhất** khi viết test nhiều người dùng. Ghi lại vào chú thích test: gọi `AsUserAsync` **tuần tự** quanh mỗi request. |
| **P6** | **Fixture test tự viết `"tasks": null`.** `OverwriteKindAsync` chép mảng `tasks` qua `Dictionary<string, object?>` ⇒ boxed `JsonElement` **không** sống sót khi serialize lại ⇒ applier từ chối đúng (400) nhưng trông như **bug applier**. | Bài học: fixture cũng là code phải kiểm. Sửa: dựng payload **từng trường một**; và test **tự parse lại** payload trước khi approve, để lỗi fixture không giả dạng lỗi sản phẩm. |

### 11.3. Lệch kế hoạch có chủ ý

| # | Lệch | Lý do |
|---|---|---|
| **L1** | **1 migration** (`Phase147AgentPlatform`) thay vì 2 như §3 dự định | §3.2 (`Phase147KnowledgeBase` + pgvector) thuộc **Track B §B3**, và Track B chưa thi hành. Tách đúng theo ranh giới Track đã chốt. |
| **L2** | **KHÔNG đổi image CI** thành `pgvector/pgvector:pg18` | Chỉ cần khi có cột `vector`. Track A không có ⇒ thay đổi này để **lại cho §B3** cùng migration của nó. Ghi ở đây để không bị coi là bỏ sót. |
| **L3** | **KHÔNG nới index `uq_workspace_members_ai_agent`** | Thuộc **Track B §B1** (nhiều agent row theo persona). Track A giữ nguyên "đúng 1 agent/workspace" như Phase 7 — và **toàn bộ test Phase 7 xanh mà không sửa assertion nào**. |
| **L4** | **Sửa ĐÚNG 1 assertion của test cũ** — `WorkspaceMemberApiTests.GetMembers_KeepsTheFiveOriginalFieldsAndAppendsThePhase11Ones` | §A2 **append** 4 field vào `WorkspaceMemberResponse`, mà test đó khẳng định **danh sách key chính xác**. Đây là ngoại lệ **duy nhất** của Track A và là hệ quả trực tiếp của luật append-only đã có từ Phase 10 §4.2. Không có assertion nào khác bị sửa. |
| **L5** | `EscalateManager` **không** ghi gì và Undo là no-op thành công | Là hành vi **có chủ ý** (không thể tự giải quyết ⇒ phải họp khẩn). Có **2 test** riêng: một khẳng định **0 thay đổi dữ liệu**, một khẳng định Undo **thành công** (nếu Undo lỗi thì action đã duyệt sẽ kẹt vĩnh viễn trong lịch sử). |
| **L6** | `RecoveryPlan` là **1** action type với `kind` trong payload, không phải 4 | Giữ lịch sử AI + bộ lọc `?action=` + UI "duyệt đề xuất" là **một luồng đọc**. |

### 11.4. Ghi chú thiết kế phải đọc trước khi "sửa lại cho đúng ý"

1. **§A5 Phần A KHÔNG thay LLM — nó chèn trước LLM.** Jev chấm severity rồi (a) **chặn** lời gọi LLM khi verdict tự tin là dưới ngưỡng cảnh báo (đây là phần tiết kiệm token), (b) **truyền verdict xuống** system prompt để LLM giải thích đúng mức. `ObserverFindingValidator` **vẫn** là thứ quyết định finding nào được ghi — Jev không bypass nó.
2. **Ngưỡng confidence bị cưỡng chế trong code, không tin provider.** Ngoài nhánh `DirectAssign` **mọi** trường đề xuất bị set `null`, kể cả khi fake trả 0.99. Có test ở **đúng biên** 0.85 / 0.50 + `NaN`/`∞`.
3. **`AutoRecoveryPlanEnabled = false` mặc định** — giống `Digest:Enabled`. Đây là tính năng **duy nhất** của Jev *sinh* đề xuất tác động dữ liệu, nên bật phải là quyết định tường minh. Có test khẳng định mặc định **không** tạo gì.
4. **Requester của một Recovery Plan là một Manager thật**, không phải "system". `ai_action_logs.requested_by_user_id` là FK **bắt buộc** tới `users` và không có system principal; gán bừa một user ảo sẽ làm sai lệch vết kiểm toán. Không có Admin/Manager ⇒ **bỏ qua** plan (log lại) chứ không fail scan.
5. **`RecoveryPlanApplier` KHÔNG mở transaction.** `ApproveAsync` đã mở sẵn — mở lồng nhau chính là bẫy **P2 của Phase 14.5** (duyệt `MoveTaskToColumn` từng trả 500). Mọi thay đổi nằm trong **một** `SaveChangesAsync`.
6. **`Jev:ObserverSeverityRouting` là cờ rollback riêng**, không dùng chung `Jev:Enabled`: Observer chạy theo timer, nên một công tắc Jev toàn cục **không được** làm mỗi tick ném lỗi. `AssessObserverSignalsAsync` vì vậy **cố ý không** gọi `EnsureEnabled()`.
7. **`AgentChat:DefaultPersona` chưa được dùng** — persona là **Track B §B1**. Field đã thêm vào `appsettings.json` theo §9.1 nhưng chưa có code đọc; đừng tưởng nó đang hoạt động.
8. **§A3/§A4 chưa có UI.** Endpoint + test đã xong và đã verify; phần frontend (pill 3 nhánh, nút "Gợi ý người làm") nằm trong §A3/§A4 của kế hoạch và **chưa** thi hành — nên `ci-web.yml` giữ nguyên 657.

### 11.5. Việc còn lại của Track A (để không bị coi là "xong hết")

- **Frontend §A3/§A4**: `useJevTriage` (debounce 400 ms + `AbortController`), `JevTriagePanel` 3 nhánh, nút "Gợi ý người làm" trong `TaskDetailModal`, `confidenceHelpers`.
- **Frontend §A5**: `RecoveryPlanCard` trong Notification Center / `AiActionHistoryDrawer`.
- **Frontend §A2**: màn hình Taxonomy trong Workspace Settings + form hồ sơ kỹ năng thành viên.
- **Đo `latencyMs` thật** với `Jev:ApiKey` thật để chốt DoD "< 300 ms" bằng số đo, không phải mục tiêu.

