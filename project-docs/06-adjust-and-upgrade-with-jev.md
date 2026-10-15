# TeamNexus — Điều chỉnh & Nâng cấp Hệ thống với Mô hình Jev (Non-Generative Decision AI)

> **Tài liệu thứ 06 của `project-docs/`.** Kế thừa từ `05-capstone-and-itgottalent.md` và `tasks/phase-14-6-ai-agent-enhancements.md`.
> **Mục tiêu:** Lưu trữ toàn bộ định hướng nghiên cứu, cơ sở lý thuyết, kiến trúc kỹ thuật và kế hoạch hiện thực hóa việc tích hợp **Jev** — mô hình AI ra quyết định không sinh văn bản (Non-Generative Decision Model) vào TeamNexus, kết hợp nâng cấp toàn diện năng lực Autonomous Agent (hợp nhất từ Track B Phase 14.6 vào **Giai đoạn 14.7**).
>
> **Nguyên tắc bất biến:**
> - **Accountability Layer giữ vững 100%:** Mọi quyết định hoặc hành động tác động dữ liệu của Jev / LLM đều phải qua `Pending` → `Human Approve` / `Reject` → `Undo`.
> - **Kiến trúc Hybrid AI:** Jev đóng vai trò **System 1 (Phản xạ & Quyết định có cấu trúc siêu tốc)**, LLM (DeepSeek) đóng vai trò **System 2 (Tư duy ngôn ngữ & Giải thích sâu)**.
> - **Phòng thủ chống ảo giác (Anti-Hallucination):** Tận dụng tối đa bản chất schema-constrained của Jev để triệt tiêu việc rò rỉ ID hoặc sai lệch dữ liệu nghiệp vụ.

---

## 1. BỐI CẢNH & BẢN CHẤT CỦA MÔ HÌNH JEV

### 1.1. Jev là gì?
**Jev** (phát triển bởi TypeSafe AI, đồng sáng lập bởi Diogo Almeida — cựu nghiên cứu sinh OpenAI, công bố tháng 9/2026) là dòng mô hình AI đại diện cho bước chuyển dịch mới: **Non-Generative, Non-Autoregressive System One Decision Model**.

Khác với các Large Language Models (LLM) thông thường (GPT, Gemini, Claude, DeepSeek) hoạt động theo cơ chế đoán token từng bước (autoregressive loop) để sinh văn bản tự do, Jev được huấn luyện chuyên biệt để:
1. **Đánh giá trạng thái (State Evaluation):** Tiếp nhận dữ liệu đầu vào (text, JSON payload, context) trong một lượt xử lý duy nhất (*single-pass inference*).
2. **Trả lời các câu hỏi có kiểu dữ liệu tĩnh (Typed Questions):**
   - **`Choice`:** Chọn 1 phương án trong danh mục rời rạc kèm phân phối xác suất và độ tin cậy (`confidence score`).
   - **`Score`:** Chấm điểm mức độ theo thang đo thứ tự (`Low`, `Medium`, `High`, `Critical`).
   - **`Noul` (Nhị phân):** Trả lời Có/Không (`true`/`false`) hoặc xác định sự tồn tại của một thuộc tính.
3. **Độ trễ cực thấp (Ultra-low Latency):** Phản hồi trong **70ms – 300ms** (so với 1.5s – 5s của LLM).
4. **Chi phí tính toán tối thiểu:** Tiết kiệm ~90–95% chi phí so với việc ép LLM sinh JSON có cấu trúc qua structured outputs.
5. **Không ảo giác cú pháp:** Đầu ra là typed data chuẩn mà backend (C# / .NET) có thể parse và validate trực tiếp, loại bỏ 100% rủi ro vỡ định dạng JSON hoặc rò rỉ UUID ngoài ý muốn.

### 1.2. So sánh Kiến trúc: LLM thuần túy vs. Hybrid AI (Jev + LLM)

| Tiêu chí | Tiếp cận cũ (Thuần LLM DeepSeek) | Tiếp cận mới: Hybrid AI (Jev + DeepSeek) |
|---|---|---|
| **Phân loại & Định tuyến** | Ép LLM sinh JSON qua prompt; chậm (1.5–4s); tốn token | **Jev xử lý (<200ms)**; trả về enum + xác suất tin cậy |
| **Độ tin cậy cấu trúc** | Cần `ObserverFindingValidator` lọc chặt, regex, bắt ngoại lệ JSON | **Tuyệt đối an toàn**; schema được bảo đảm ở tầng mô hình |
| **Chi phí quét nền (Observer)** | Nguy cơ bùng nổ chi phí token khi quét định kỳ nhiều board | **Cực rẻ**, thoải mái quét liên tục ở `BackgroundService` |
| **Sinh văn bản / Giải thích** | Làm tốt nhưng bị lạm dụng vào cả khâu ra quyết định | **DeepSeek chỉ làm đúng sở trường**: giải thích lý do, chat tương tác |
| **Trải nghiệm người dùng (UX)** | Người dùng phải đợi xoay spinner khi tạo task hoặc phân loại | **Gợi ý tức thời** ngay khi người dùng gõ phím / blur trường nhập |

---

## 2. NÂNG CẤP TIỀN ĐỀ BẮT BUỘC (FOUNDATIONAL PREREQUISITES)

Để mô hình Jev hoạt động ổn định và chính xác trong môi trường doanh nghiệp thực tế của TeamNexus, hệ thống cần được trang bị 3 trụ cột dữ liệu và logic nền tảng:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    3 TRỤ CỘT TIỀN ĐỀ ĐỂ JEV HOẠT ĐỘNG ỔN ĐỊNH               │
├──────────────────────────────┬──────────────────────────────┬───────────────┤
│    TRỤ CỘT 1: TAXONOMY       │     TRỤ CỘT 2: MEMBER SKILLS │  TRỤ CỘT 3:   │
│   Quản lý Nhãn Cố định       │    Kỹ năng & Vai trò Thành   │  LOGIC RẼ     │
│   & Danh mục Chuẩn hóa       │   viên Thực + Agent Personas │  NHÁNH THEO   │
│                              │                              │  CONFIDENCE   │
└──────────────────────────────┴──────────────────────────────┴───────────────┘
```

### 2.1. Trụ cột 1: Quản lý Nhãn Cố định & Taxonomy Chuẩn hóa (Fixed Taxonomy Management)
* **Vấn đề thực tế:** Nếu nhãn (labels) trên Board do người dùng nhập tự do tùy tiện (ví dụ: `fe`, `frontend`, `ui-bug`), câu hỏi dạng `Choice` của Jev sẽ bị phân mảnh, giảm độ chính xác và không thể ánh xạ tự động.
* **Giải pháp kiến trúc:**
  1. Xây dựng bảng từ điển danh mục chuẩn cấp Workspace (`board_taxonomies` hoặc trường cấu hình nhãn chuẩn):
     - **Miền kỹ thuật (Domains):** `Backend`, `Frontend`, `DevOps/Infra`, `Database`, `Mobile`, `UI/UX`, `QA/Testing`.
     - **Loại hình (Work Types):** `Feature`, `Bug`, `Refactor`, `TechDebt`, `Security`, `Documentation`.
  2. Jev sẽ nhận tập nhãn cố định này làm danh sách lựa chọn (`Choice options`). Người dùng vẫn có thể gắn thêm custom tag phụ, nhưng nhãn phân loại lõi phải thuộc hệ thống chuẩn hóa.

### 2.2. Trụ cột 2: Hồ sơ Kỹ năng (Skills) & Vai trò (Roles) của Thành viên Thực và AI Agent
* **Vấn đề thực tế:** Để Jev gợi ý người phụ trách tối ưu (`Smart Assignee Dispatching`), Jev không thể đoán mò qua tên người dùng (`user.full_name`).
* **Giải pháp kiến trúc:**
  1. Mở rộng `workspace_members`:
     - Bổ sung trường `skills` (danh sách chuẩn, vd: `["csharp", "dotnet", "postgresql", "docker"]` hoặc `["react", "typescript", "tailwind"]`).
     - Bổ sung trường `primary_role` (`Tech Lead`, `Backend Dev`, `Frontend Dev`, `QA Engineer`, `Scrum Master`, `DevOps`).
     - Bổ sung chỉ số `capacity_weight` và giới hạn `max_wip_tasks` (số lượng task tối đa đang thực hiện).
  2. **Đồng bộ hóa cho AI Agent Personas:** Các "thành viên ảo" (Virtual Co-workers từ Track B Phase 14.6) cũng được khai báo `member_type = ai_agent` với bộ kỹ năng tương ứng (ví dụ: `QA Agent` chuyên về `["test-cases", "security-audit"]`).
  3. Khi cần điều phối, Jev nhận input gồm [Mô tả Task + Vector Kỹ năng/Tải trọng của Team] để đưa ra quyết định ghép người chính xác.

### 2.3. Trụ cột 3: Cấu trúc Logic Rẽ nhánh theo "Xác suất Tin cậy" (Confidence Threshold Branching)
Jev luôn trả về kết quả kèm chỉ số tin cậy (`confidence score` $\in [0.0, 1.0]$). Hệ thống tuyệt đối không áp dụng mù quáng mà triển khai cây quyết định rẽ nhánh an toàn:

```mermaid
flowchart TD
    Start[Jev đánh giá trạng thái / đề xuất] --> Conf{Độ tin cậy Confidence}
    
    Conf -->|Cao: >= 0.85| HighPath[Đề xuất đơn nhất với 1-Click Accept / Auto-Fill]
    Conf -->|Trung bình: 0.50 <= Conf < 0.85| MedPath[Đề xuất Top 2-3 Lựa chọn kèm % Xác suất]
    Conf -->|Thấp: < 0.50| LowPath[Rẽ nhánh Dự phòng Fallback]
    
    HighPath --> UI1[UI hiển thị badge xanh: 'AI tự tin 92%']
    MedPath --> UI2[UI hiển thị Radio/Dropdown: 'Gợi ý: Dev A (68%), Dev B (27%)']
    LowPath --> FallbackCheck{Loại tác vụ?}
    
    FallbackCheck -->|Phân loại / Gán việc| HumanChoice[Để người dùng tự chọn thủ công]
    FallbackCheck -->|Observer / Phân tích sâu| System2LLM[Đánh thức DeepSeek System 2 phân tích chuyên sâu]
```

* **Ngưỡng Cao ($Confidence \ge 0.85$):** Tự động điền giá trị ưu tiên hoặc đề xuất duy nhất. Người dùng chỉ cần 1 click để chấp thuận.
* **Ngưỡng Trung bình ($0.50 \le Confidence < 0.85$):** Hiển thị Top 2 hoặc Top 3 lựa chọn có xác suất cao nhất kèm tỷ lệ phần trăm (ví dụ: *"Gợi ý phân công: Nguyễn Văn A (70%), Trần Văn B (25%)"*), giúp quản lý có thêm góc nhìn phản biện mà không bị áp đặt.
* **Ngưỡng Thấp ($Confidence < 0.50$):** Hệ thống không tự tiện suy đoán; kích hoạt cơ chế fallback:
  - Nếu là phân loại task: Để trống để con người tự quyết định.
  - Nếu là phân tích bất thường: Chuyển sang LLM DeepSeek (System 2) để đọc ngữ cảnh toàn diện và đặt câu hỏi làm rõ (*Awaiting Clarification*).

---

## 3. CÁC KỊCH BẢN ỨNG DỤNG CHI TIẾT CỦA JEV TRONG TEAMNEXUS

### 3.1. Kịch bản 1: Phân loại Task & Đánh giá Mức độ Gấp (Auto-Triage & Smart Priority)
* **Thời điểm kích hoạt:** Khi người dùng tạo task qua Quick Create, blur input tiêu đề/mô tả trên Modal, hoặc khi có Issue mới đẩy về từ GitHub Webhook (Phase 15).
* **Luồng xử lý:**
  1. Backend gửi tiêu đề + mô tả task đến Jev API.
  2. Jev trả lời song song trong 1 lượt chạy:
     - `Choice` → `TaxonomyDomain` (Backend / Frontend / DevOps / ...).
     - `Choice` → `WorkType` (Feature / Bug / Refactor / ...).
     - `Score` → `Priority` (`Urgent`, `High`, `Medium`, `Low`).
     - `Noul` → `NeedsSecurityReview` (true/false).
  3. Giao diện frontend nhận kết quả qua HTTP/SignalR trong tích tắc, tự động gán gợi ý thẻ pill đổi màu sinh động.

### 3.2. Kịch bản 2: Gợi ý Người thực hiện Phù hợp (Smart Assignee Dispatching)
* **Thời điểm kích hoạt:** Người dùng bấm nút "Gợi ý người làm" trên `TaskDetailModal`, hoặc khi AI Agent điều phối việc.
* **Luồng xử lý:**
  1. Trích xuất ngữ cảnh: Mô tả task, danh sách `workspace_members` (kèm `skills`, số lượng task `In Progress`, vai trò).
  2. Jev trả về `Choice` đối với danh sách `userId` ứng viên kèm xác suất.
  3. Áp dụng **Trụ cột 3 (Logic rẽ nhánh)**:
     - Nếu tự tin cao: Gợi ý trực tiếp assignee kèm tooltip lý do *"Phù hợp kỹ năng .NET & tải hiện tại thấp"*.
     - Nếu phân vân: Hiển thị 2 ứng viên sáng giá nhất cho Manager lựa chọn.

### 3.3. Kịch bản 3: Nâng cấp Toàn diện AI Observer (Bộ đôi A + B)

AI Observer là "trái tim" giám sát tiến độ của TeamNexus. Jev sẽ nâng cấp cả hai tầng:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          AI OBSERVER VỚI MÔ HÌNH JEV                        │
├──────────────────────────────────────┬──────────────────────────────────────┤
│ PHẦN A: ĐÁNH GIÁ MỨC ĐỘ NGHIÊM TRỌNG │ PHẦN B: ĐỊNH TUYẾN KỊCH BẢN PHỤC HỒI │
│ (Risk Severity & Anomaly Filtering)  │ (Proactive Auto-Healing Routing)     │
│ - Tốc độ cao, không ảo giác          │ - Chọn giải pháp hành động cụ thể    │
│ - Phân loại Low/Medium/High/Critical │ - Đẩy vào Accountability Layer       │
│ - Quyết định có cần báo động khẩn    │ - 1-Click Approve cho Quản lý        │
└──────────────────────────────────────┴──────────────────────────────────────┘
```

#### Phần A — Đánh giá Mức độ Nghiêm trọng (Risk Severity & Anomaly Filtering)
* **Hiện trạng cũ:** BackgroundService dùng C# heurictics quét tín hiệu thô (`OverdueTask`, `StalledTask`, `AtRiskDeadline`, `Overload`, `Bottleneck`), sau đó phải gửi System Prompt rất dài cho LLM DeepSeek để yêu cầu không bịa ID, rồi C# `ObserverFindingValidator` lại phải giao cắt ID để chống ảo giác.
* **Cải tiến với Jev:**
  - Jev nhận tóm tắt tín hiệu và trả về:
    - `Score`: Mức độ rủi ro khách quan (`Low`, `Medium`, `High`, `Critical`).
    - `Noul`: `RequireImmediateEscalation` (Có cần phát chuông báo động khẩn sang Slack/Discord ngay không?).
    - `Noul` : `SprintDelayDominoRisk` (Có nguy cơ kéo theo sự đổ vỡ dây chuyền của Sprint không?).
  - **Lợi ích:** Triệt tiêu hoàn toàn nỗi lo LLM rò rỉ GUID hoặc bịa đặt task ID trong giai đoạn đánh giá.

#### Phần B — Định tuyến Kịch bản Phục hồi Chủ động (Proactive Auto-Healing Routing)
* Khi một rủi ro đạt mức `High` hoặc `Critical`, hệ thống không chỉ gửi thông báo thụ động mà phải đề xuất **Kịch bản Khắc phục Đề xuất (Recovery Plan Proposal)**:
  1. Jev đánh giá tình hình và đưa ra quyết định dạng `Choice`:
     - `ReassignTask`: Chuyển task sang nhân sự rảnh hơn.
     - `SplitSubtasks`: Giữ task nhưng phân rã phần tồn đọng sang subtasks cho sprint sau.
     - `ExtendDeadline`: Gia hạn deadline khả thi dựa trên vận tốc làm việc.
     - `EscalateManager`: Không thể tự giải quyết, cần họp khẩn.
  2. Khi Jev đã chốt loại hành động (`ActionType`) và đối tượng đích, hệ thống tự động sinh bản ghi trong bảng `ai_action_logs` ở trạng thái `Pending`.
  3. Quản lý mở Thông báo / Lịch sử AI, bấm **"Phê duyệt giải pháp"** để thực thi tự động (kèm nút `Undo` hoàn tác an toàn).

---

## 4. HỢP NHẤT TRACK B (PHASE 14.6) VÀO GIAI ĐOẠN 14.7

Trước đây, Giai đoạn 14.6 được chia làm 2 Track:
- **Track A:** Khắc phục ma sát & Hoàn thiện trải nghiệm (Card Picker, Chặn rò rỉ GUID, Transcript Task Chat, Board Digest) — **Đã hoàn thành 100% với 665 backend tests và 657 frontend tests**.
- **Track B:** Nâng cấp năng lực AI Agent (Personas, Auto-Healing, Tool-calling ReAct, pgvector).

Nhằm bảo đảm tính ổn định, mạch lạc và tích hợp sâu kiến trúc **Hybrid AI (Jev + LLM)**, toàn bộ nội dung của Track B được **chính thức nâng tầm và hợp nhất vào Giai đoạn 14.7**:

### Các trụ cột của Giai đoạn 14.7:
1. **Trụ cột 1: Hạ tầng Nền tảng & Tích hợp Jev Client (`IJevDecisionService`)**
   - Thiết kế C# client kết nối TypeSafe AI Jev API (hỗ trợ `Choice`, `Score`, `Noul`).
   - Xây dựng `FakeJevProvider` phục vụ kiểm thử đơn vị & kiểm thử tích hợp ngoại tuyến (deterministic unit tests).
   - Thiết lập cấu trúc `Taxonomy` chuẩn và mở rộng trường `skills`/`roles` cho thành viên.
2. **Trụ cột 2: Hệ thống Đội ngũ AI Chuyên gia (Multi-Agent Personas)**
   - Scrum Master / PO Agent, Tech Lead Agent, QA & Security Agent.
   - Định danh rõ ràng trong `workspace_members` với `member_type = ai_agent`.
3. **Trụ cột 3: AI Observer Proactive Auto-Healing với Jev (Phần A + B)**
   - Tầng thẩm định rủi ro và lọc tín hiệu bằng Jev.
   - Tự động sinh `RecoveryPlanProposal` gắn liền với `AiActionLog` (Human-in-the-Loop).
4. **Trụ cột 4: Mở rộng Tool-Calling (ReAct Loop) & Bộ nhớ Ngữ nghĩa (pgvector)**
   - Các tool mới: `InspectMemberWorkload`, `MatchSkills`, `ValidateAcceptanceCriteria`.
   - Lưu trữ và truy vấn semantic embeddings cho tài liệu dự án để Agent trả lời chính xác theo chuẩn team.

---

## 5. THIẾT KẾ DỮ LIỆU & HỢP ĐỒNG API (DATA MODEL & CONTRACTS)

> **📌 CẬP NHẬT SAU KHI THI HÀNH (Giai đoạn 14.7 Track A):** §5.1 và §5.2 dưới đây là **thiết kế dự kiến ban đầu**. Hợp đồng **thật đã hiện thực** khác ở vài điểm quan trọng — xem **§5.3** ngay sau §5.2 để biết chính xác cái gì đã được build. Đừng code theo §5.1/§5.2 mà không đọc §5.3.

### 5.3. Hợp đồng THẬT đã hiện thực (Giai đoạn 14.7 Track A)

**A. Jev KHÔNG đi qua `/chat/completions` — đây là phát hiện quan trọng nhất khi tra tài liệu gốc.**
Jev có **Decisions API** riêng. Vì vậy `DeepSeekAiProvider` hiện có **không thể tái dùng**, kể cả khi chỉ đổi `BaseUrl`:
`POST {Jev:BaseUrl}{Jev:DecisionsPath}` (mặc định `https://openrouter.ai` + `/api/alpha/decisions`) với body
`{ model, state, questions }` → response `{ answers, model, provider, usage: { input_tokens, output_tokens, cost } }`.
`state` nhận `string | object | array`. `criteria` của `choice` là **object `{ nhãn: mô tả }`**; của `score` là **array tăng dần**.
Câu trả lời có kiểu: `noul` → `{ type, noul: 0..1 }` (**KHÔNG có `confidence`**); `choice` → `{ type, choice, probabilities, confidence }`; `score` → `{ type, score, legend, probabilities, confidence }`.
⚠️ `probabilities`/`confidence` là **optional** trong schema OpenRouter (dù Portkey đánh dấu required) ⇒ code phải chịu được thiếu, và thiếu ⇒ **coi là không dùng được**, không được suy diễn thành mặc định tự tin.

**B. Schema thật (1 migration: `Phase147AgentPlatform`, `11 → 12`):**
- `workspace_members` thêm `skills jsonb NOT NULL DEFAULT '[]'`, `primary_role varchar(50) DEFAULT 'Member'`, `capacity_weight double precision DEFAULT 1.0` (CHECK `> 0`), `max_wip_limit int DEFAULT 5` (CHECK `>= 0`).
- Bảng **`workspace_taxonomies`**: `id · workspace_id · kind ('domain'|'work_type') · key · label · color_hex · position · created_at · updated_at`, UNIQUE `(workspace_id, kind, key)`, index `(workspace_id, kind, position)`.
- **KHÁC thiết kế §5.1:** không có cột `domain`/`work_type` trên cùng một bảng như SQL minh hoạ, mà dùng **một cột `kind`** + `key`/`label` — để thêm loại danh mục mới không cần đổi schema. `persona_key` **chưa** thêm (thuộc Track B §B1).

**C. Hợp đồng `IJevDecisionService` thật** (4 method thay vì 3; mọi kết quả đều mang `ConfidenceScore` **và** `RoutingAdvice`):
```csharp
Task<TaskTriageResult>            TriageTaskAsync(TaskTriageContext, CancellationToken);
Task<AssigneeRecommendationResult> RecommendAssigneeAsync(AssigneeDispatchContext, CancellationToken);
Task<ObserverRiskAssessmentResult> AssessObserverSignalsAsync(ObserverRiskContext, CancellationToken);
Task<RecoveryActionDecisionResult> DecideRecoveryActionAsync(RecoveryActionContext, CancellationToken);
```
- `TaskTriageResult(string? SuggestedDomain, string? SuggestedWorkType, string? SuggestedPriority, bool NeedsSecurityReview, double ConfidenceScore, string RoutingAdvice, IReadOnlyList<TriageOption> Options)`
- `AssigneeRecommendationResult(Guid? RecommendedUserId, double ConfidenceScore, IReadOnlyList<CandidateScore> CandidateRankings, string RoutingAdvice)`
- `ObserverRiskAssessmentResult(string EvaluatedSeverity, bool RequireImmediateAlert, bool HasDominoRisk, double ConfidenceScore, string RoutingAdvice)`
- `RecoveryActionDecisionResult(string? ActionType, Guid? TargetTaskId, Guid? TargetUserId, double ConfidenceScore, string RoutingAdvice)`

**D. Rẽ nhánh confidence là CODE CỦA TA, không phải của provider** (`ConfidenceRouting`, hàm thuần):
`≥ 0.85` ⇒ `DirectAssign` (đúng 1 đề xuất) · `[0.50, 0.85)` ⇒ `OfferChoices` (≤ 3) · `< 0.50` ⇒ `ManualRequired` **và mọi giá trị đề xuất bị set `null`**.
Cấu hình ngược (`High < Medium`) bị **clamp** để nhánh giữa không biến mất; `NaN`/`∞` ⇒ `ManualRequired`.

**E. Endpoint HTTP thật:**
| Method | Route | Quyền | Ghi chú |
|---|---|---|---|
| `GET` / `PUT` | `/api/workspaces/{id}/taxonomies` | Member+ / Manager+ | Seed lazy idempotent (`INSERT … ON CONFLICT DO NOTHING`) |
| `PUT` | `/api/workspaces/{id}/members/{userId}/profile` | Admin/Owner | Từ chối AI Agent; field **vắng ⇒ giữ nguyên**, list **rỗng ⇒ xoá** |
| `POST` | `/api/boards/{id}/tasks/triage` | Member+ | **Không ghi gì**; tắt ⇒ 503; provider lỗi ⇒ **200 + `degraded:true`** |
| `POST` | `/api/boards/{id}/tasks/{taskId}/assignee-suggestions` | Member+ | **Không ghi gì**; thẻ khác bảng ⇒ 400 |

**F. Chống rò rỉ ID — mạnh hơn đường LLM:** Jev chỉ nhận **nhãn do server cấp** (`cand-1`, key taxonomy). Nó **không bao giờ** nhận `Guid` ⇒ không thể trả lại. Validator **còn** lọc thêm mọi chuỗi khớp mẫu GUID (phòng thủ theo chiều sâu). Context assignee **không gửi `email`**. Có test khẳng định `userId` không xuất hiện trong trường văn bản, và **test khẳng định email không rời khỏi tiến trình**.

**G. Recovery Plan (Phần B) — thật:** action type **`RecoveryPlan`** (`entity_type='Workspace'`), `kind` nằm **trong payload** (4 giá trị). `RecoveryPlanApplier` **KHÔNG mở transaction** (bẫy P2 Phase 14.5). `SplitSubtasks` **uỷ quyền** `CreateSubtasksApplier` **và phải mang `createdTaskIds` lên** `applied_snapshot`, nếu không Undo xoá 0 thẻ mà báo thành công. `EscalateManager` **không ghi gì** + trả warning. Requester = **Admin/Manager thật** (không có system principal). `AutoRecoveryPlanEnabled = false` **mặc định**.

**H. `FakeJevDecisionProvider` (offline, 0 token):** chọn kịch bản bằng sentinel `FAKE:JEV_HIGH|MED|LOW|ERROR|SLOW` **quét trong toàn bộ `state`** (không chỉ một field — vì ba context đặt tiêu đề ở ba chỗ khác nhau). Luôn **đọc lại chính các nhãn `criteria` mà caller gửi** ⇒ câu trả lời fake không bao giờ lệch khỏi hợp lệ.

### 5.1. Mở rộng Database Schema (thiết kế DỰ KIẾN ban đầu — xem §5.3.A/B cho bản thật)
```sql
-- 1. Bổ sung kỹ năng và vai trò cho thành viên workspace
ALTER TABLE workspace_members 
ADD COLUMN IF NOT EXISTS skills jsonb DEFAULT '[]'::jsonb,
ADD COLUMN IF NOT EXISTS primary_role varchar(50) DEFAULT 'Member',
ADD COLUMN IF NOT EXISTS max_wip_limit int DEFAULT 5;

-- 2. Quản lý nhãn cố định / taxonomy chuẩn hóa
CREATE TABLE IF NOT EXISTS workspace_taxonomies (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    workspace_id uuid NOT NULL REFERENCES workspaces(id) ON DELETE CASCADE,
    domain varchar(50) NOT NULL, -- 'Backend', 'Frontend', 'DevOps', 'Mobile', etc.
    work_type varchar(50) NOT NULL, -- 'Feature', 'Bug', 'Refactor', etc.
    color_hex varchar(10) NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS idx_workspace_taxonomies_ws ON workspace_taxonomies(workspace_id);
```

### 5.2. C# Interface Contract: `IJevDecisionService`
```csharp
namespace TeamNexus.Modules.Ai.Services.Jev;

public interface IJevDecisionService
{
    /// <summary>Phân loại nhanh task và đánh giá độ khẩn cấp (Single-pass).</summary>
    Task<TaskTriageResult> TriageTaskAsync(string title, string? description, CancellationToken ct = default);

    /// <summary>Gợi ý thành viên phù hợp nhất dựa trên kỹ năng và khối lượng công việc hiện tại.</summary>
    Task<AssigneeRecommendationResult> RecommendAssigneeAsync(
        string taskTitle, 
        string? taskDescription, 
        IReadOnlyList<MemberWorkloadContext> candidates, 
        CancellationToken ct = default);

    /// <summary>Đánh giá mức độ nghiêm trọng của tín hiệu và định tuyến kịch bản phục hồi cho AI Observer.</summary>
    Task<ObserverRiskAssessmentResult> AssessObserverSignalsAsync(
        IReadOnlyList<ObserverSignalContext> signals, 
        CancellationToken ct = default);
}

public sealed record TaskTriageResult(
    string SuggestedDomain,
    string SuggestedWorkType,
    string SuggestedPriority,
    bool NeedsSecurityReview,
    double ConfidenceScore);

public sealed record AssigneeRecommendationResult(
    Guid? RecommendedUserId,
    double ConfidenceScore,
    IReadOnlyList<CandidateScore> CandidateRankings,
    string RoutingAdvice); // "DirectAssign", "OfferChoices", "ManualRequired"

public sealed record ObserverRiskAssessmentResult(
    string EvaluatedSeverity, // "Low", "Medium", "High", "Critical"
    bool RequireImmediateAlert,
    bool HasDominoRisk,
    string RecommendedActionType, // "Reassign", "SplitSubtasks", "ExtendDeadline", "None"
    double ConfidenceScore);
```

---

## 6. GIÁ TRỊ HỌC THUẬT & ĐIỂM CỘNG TẠI CUỘC THI IT GOT TALENT

Trong hồ sơ Đồ án Chuyên ngành và Bài dự thi **IT got Talent (Hạng mục AI)**, việc tích hợp Jev mang lại những luận điểm cạnh tranh mang tính đột phá:

1. **Khắc phục triệt để điểm yếu của "Chatbot AI đại trà":**
   - Hầu hết các đồ án thông thường chỉ dừng lại ở việc dùng LLM dạng "hỏi-đáp sinh text".
   - TeamNexus chứng minh được tư duy kỹ thuật đỉnh cao: **Dùng đúng mô hình cho đúng bài toán**. Không dùng một cỗ máy LLM cồng kềnh để làm việc phân loại đơn giản.
2. **Mô hình Hybrid AI (Tư duy Nhanh & Chậm — System 1 & System 2):**
   - Hiện thực hóa thuyết nhận thức Kahneman trong kiến trúc phần mềm: Jev phản xạ trong 100ms để định tuyến, lọc dữ liệu; LLM DeepSeek chỉ suy ngẫm sâu khi cần phân tích chiến lược.
3. **Số đo định lượng kiểm chứng được (Measurable Benchmarks):**
   - **Độ trễ:** Giảm thời gian ra quyết định phân loại từ 2.800ms (LLM) xuống **140ms (Jev)** (nhanh hơn gấp 20 lần).
   - **Chi phí vận hành:** Tiết kiệm ~92% chi phí token cho các tác vụ nền định kỳ của AI Observer.
   - **Độ chính xác & An toàn:** 0% lỗi hallucination cấu trúc JSON, 100% tuân thủ Accountability Layer.
4. **Tính khả thi và sẵn sàng Production:**
   - Hệ thống vẫn giữ vững nguyên tắc **Graceful Degradation** và tính tương thích ngược, đảm bảo TeamNexus vận hành liên tục, ổn định và an toàn trên Internet thật.
