# Danh sách Vấn đề Phát hiện & Đề xuất Khắc phục — Giai đoạn 14.7 Track A

> **Tài liệu nguồn:** `tasks/phase-14-7-autonomous-agent-jev.md` (§A1–§A5)  
> **Môi trường kiểm thử:** Production Live (`https://app.teamnexus.cloud` — Frontend Vercel, `https://api.teamnexus.cloud` — Backend Render, `typesafe/jev-1.13` qua OpenRouter)  
> **Thời điểm kiểm thử:** Ngày 15/10/2026  
> **Phương pháp kiểm thử:** Kiểm thử thực chiến tự động hóa qua Browser Agent, tương tác trực tiếp với giao diện người dùng và đối chiếu API responses thực tế.

---

## 1. Tổng quan Kết quả Đo kiểm Thực tế

| Nhóm chức năng | Trạng thái Nghiệp vụ Backend | Trạng thái Giao diện Frontend | Đánh giá chung |
|---|---|---|---|
| **§A2: Phân loại chuẩn (Taxonomy)** | ✅ Hoạt động hoàn hảo (Lazy seed 7 Domains, 6 WorkTypes) | ✅ Chuẩn Ant Design, thao tác mượt mà | **PASSED** |
| **§A2: Hồ sơ kỹ năng (Member Skills)** | ✅ Hoạt động hoàn hảo, chặn AI Agent đúng thiết kế | ✅ Modal chuẩn Ant Design, validate đầy đủ | **PASSED** |
| **§A3: Auto-Triage & Smart Priority** | ✅ Phản hồi chính xác: Tự tin **96%**, `latencyMs: 1101ms` | ❌ **Vỡ layout, dính chữ** (dùng Tailwind class) | **CẦN SỬA UI** |
| **§A4: Smart Assignee Dispatching** | ✅ Rẽ nhánh đúng thiết kế (`ManualRequired` / `degraded`) | ❌ **Vỡ layout, dính chữ** (dùng Tailwind class) | **CẦN SỬA UI** |
| **§A5: AI Observer (Phần A + B)** | ❌ **DeepSeek timeout 60s** khi quét rủi ro trực tiếp | ⚠️ Thiếu hiển thị metrics của Jev; `RecoveryPlanCard` dùng Tailwind | **CẦN SỬA BE + UI** |

---

## 2. Danh sách Chi tiết các Vấn đề (Issue List)

### ISSUE-01: DeepSeek Provider Timeout 60s khi Kích hoạt Quét AI Observer

- **Phân loại:** Backend / AI Integration
- **Mức độ nghiêm trọng:** 🔴 **Cao (High)**
- **Trạng thái:** ✅ **ĐÃ SỬA** (xem §4 — Nhật ký khắc phục, để biết nguyên nhân gốc rễ *thật* khác với chẩn đoán ban đầu)
- **Vị trí phát hiện:** Chức năng `AI Observer -> Quét ngay` (`POST /api/workspaces/{id}/observer/runs`)
- **Hiện tượng thực tế:**
  - Người dùng bấm nút **"Quét ngay"** trong `ObserverRunsDrawer` và xác nhận Popconfirm.
  - Quá trình quét chạy kéo dài trong **62,290 ms** (~62.3 giây).
  - Kết quả trả về thông báo lỗi màu đỏ:
    > `Lỗi thao tác AI Observer: DeepSeek không phản hồi trong 60s (timeout).`
  - Đợt quét bị đánh dấu trạng thái **`THẤT BẠI`** (Failed), số tín hiệu và cảnh báo hiển thị trống.
- **Bằng chứng chụp màn hình:** `media_1792036954848.png` (Chi tiết đợt quét: Trạng thái THẤT BẠI, Thời lượng 62290 ms).
- **Nguyên nhân gốc rễ (Root Cause):**
  - Mặc dù kiến trúc Track A đã đưa Jev vào làm System 1 (`Jev:ObserverSeverityRouting = true`) để lọc tín hiệu rủi ro, nhưng ở giai đoạn sinh lời văn giải thích chi tiết (prose generation), hệ thống vẫn phải gọi System 2 (DeepSeek LLM) qua API mạng ngoài.
  - Cổng kết nối từ server Render đến DeepSeek / OpenRouter bị nghẽn hoặc thời gian xử lý của model vượt quá ngưỡng `TimeoutSeconds = 60s`.
- **Đề xuất khắc phục:**
  1. Kiểm tra lại logic lọc của `ObserverService`: Đảm bảo Jev chỉ cho phép gọi LLM đối với các finding thực sự đạt ngưỡng `MinSeverityToNotify`, triệt để bỏ qua các tín hiệu dưới ngưỡng để tiết kiệm thời gian và token.
  2. Cấu hình cơ chế fallback nhanh hoặc tăng timeout hợp lý cho model System 2.
  3. Bổ sung cơ chế lưu trữ kết quả phân loại thô từ Jev ngay cả khi LLM giải thích bị timeout, tránh làm hỏng toàn bộ đợt quét.

---

### ISSUE-02: `JevTriagePanel` bị Vỡ Layout và Dính Chữ do dùng Tailwind CSS classes

- **Phân loại:** Frontend / UI & Typography
- **Mức độ nghiêm trọng:** 🔴 **Cao (High)**
- **Trạng thái:** ✅ **ĐÃ SỬA** (xem §4)
- **Vị trí phát hiện:** `src/features/ai/components/JevTriagePanel.tsx` (tích hợp trong `TaskDetailModal.tsx`)
- **Hiện tượng thực tế:**
  - Jev trả về kết quả Triage cực kỳ tốt và chính xác (Độ tự tin 96%, Domain DevOps, WorkType Documentation, Priority Low), nhưng giao diện hiển thị bị lỗi định dạng nghiêm trọng:
    - Tiêu đề dính liền chữ: `Phân loại thông minh (Jev System 1)AI tự tin 96%`
    - Nhãn và giá trị bị nối liền: `Miền kỹ thuậtDevOps`, `Loại hìnhDocumentation`, `Mức ưu tiênLow`
    - Nút bấm `"Áp dụng toàn bộ gợi ý"` hiển thị như button HTML mặc định thô sơ, không có background màu xanh emerald, không có bo góc hay padding, biểu tượng checkbox và chữ dính sát nhau.
- **Bằng chứng chụp màn hình:** `task_modal_check_1792036429103.png`
- **Nguyên nhân gốc rễ (Root Cause):**
  - Frontend dự án TeamNexus được xây dựng dựa trên **Ant Design (v6) + Pure CSS**, hoàn toàn **không cài đặt hay cấu hình Tailwind CSS** (không có `tailwind.config.*`, không có Tailwind build pipeline).
  - Component `JevTriagePanel.tsx` lại được viết hoàn toàn bằng các utility class của Tailwind (ví dụ: `className="grid grid-cols-3 gap-2 text-xs"`, `className="space-y-3"`, `className="bg-emerald-600"`).
  - Trình duyệt coi toàn bộ các class này là không tồn tại, khiến các thẻ `div` và `span` hiển thị theo thuộc tính CSS mặc định của trình duyệt, làm mất layout dạng lưới và dính liền toàn bộ chữ.
- **Đề xuất khắc phục:**
  - Tái cấu trúc lại component `JevTriagePanel.tsx` bằng 100% component chuẩn của **Ant Design**:
    - Dùng `Flex` hoặc `Space` với `gap` thay cho `space-y-*` và `flex gap-*`.
    - Dùng `Card` hoặc thẻ có inline style / CSS module thay cho `bg-slate-900/60`.
    - Dùng `Tag` hoặc `Badge` của Ant Design để hiển thị phần trăm độ tự tin và các trường Domain/WorkType/Priority.
    - Dùng `Button type="primary"` với màu sắc thương hiệu thay cho `<button>` HTML thô.

---

### ISSUE-03: `AssigneeSuggestion` bị Vỡ Layout và Dính Chữ do dùng Tailwind CSS classes

- **Phân loại:** Frontend / UI & Typography
- **Mức độ nghiêm trọng:** 🔴 **Cao (High)**
- **Trạng thái:** ✅ **ĐÃ SỬA** (xem §4 — đã viết lại bằng Ant Design thuần túy, có guard test cấm Tailwind)
- **Vị trí phát hiện:** `src/features/ai/components/AssigneeSuggestion.tsx` (tích hợp trong `TaskDetailModal.tsx`)
- **Hiện tượng thực tế:**
  - Nút toggle `"Gợi ý người làm (Jev)^"` có viền đen thô sơ, không bo góc mềm mại, không có nền màu indigo.
  - Bên trong panel gợi ý, các dòng văn bản bị dính chữ:
    - `Đánh giá phân côngCần phân công thủ công`
    - `⏱461ms`
    - `⚠Dịch vụ AI tạm không khả dụng. Bạn hãy phân công thủ công.`
    - `Chưa có ứng viên đạt độ tương thích cao. Vui lòng tự chọn thành viên từ danh sách.`
- **Bằng chứng chụp màn hình:** `assignee_suggestions_check_1792036443871.png`
- **Nguyên nhân gốc rễ (Root Cause):**
  - Tương tự ISSUE-02: Component `AssigneeSuggestion.tsx` sử dụng toàn bộ class của Tailwind CSS (`space-y-2`, `inline-flex`, `bg-indigo-500/10`, `border-indigo-500/20`) trong môi trường Ant Design thuần.
- **Đề xuất khắc phục:**
  - Chuyển đổi component sang Ant Design:
    - Nút toggle: `Button` của Ant Design với icon `ThunderboltOutlined`.
    - Cảnh báo degraded: dùng `Alert type="warning" showIcon`.
    - Danh sách ứng viên: dùng `Card` kết hợp `Flex`, `Tag`, và `Button size="small" type="primary"`.

---

### ISSUE-04: `RecoveryPlanCard` Tiềm ẩn Vỡ Layout do Viết bằng Tailwind CSS classes

- **Phân loại:** Frontend / UI (Tiềm ẩn)
- **Mức độ nghiêm trọng:** 🟡 **Trung bình (Medium)**
- **Trạng thái:** ✅ **ĐÃ SỬA** (xem §4 — đã viết lại bằng Ant Design thuần túy, có guard test cấm Tailwind)
- **Vị trí phát hiện:** `src/features/ai/components/RecoveryPlanCard.tsx` (tích hợp trong `AiActionLogItem.tsx`)
- **Hiện tượng thực tế:**
  - Hiện tại trên live chưa phát sinh action `RecoveryPlan` mới, nhưng khi quan sát mã nguồn, file `RecoveryPlanCard.tsx` đang sử dụng cấu trúc:
    `className="p-3.5 rounded-xl bg-slate-900/70 border border-slate-800 space-y-3"`
    `className="flex items-start gap-2.5 p-2.5 rounded-lg bg-amber-500/10..."`
  - Khi có hành động `RecoveryPlan` (đặc biệt là loại `EscalateManager`), giao diện cảnh báo "Cần họp khẩn cấp" và các nút Duyệt / Từ chối / Hoàn tác sẽ gặp tình trạng vỡ layout và dính chữ y hệt như ISSUE-02 và ISSUE-03.
- **Đề xuất khắc phục:**
  - Tái cấu trúc trước `RecoveryPlanCard.tsx` bằng Ant Design (`Card`, `Alert`, `Button`, `Tag`, `Typography.Paragraph`) để đồng bộ tuyệt đối với `AiActionHistoryDrawer`.

---

### ISSUE-05: `ObserverRunsDrawer` Chưa Hiển thị Đầy đủ các Chỉ số Đo lường của Jev

- **Phân loại:** Frontend / UI Enhancement
- **Mức độ nghiêm trọng:** 🟢 **Thấp (Low)**
- **Trạng thái:** ✅ **ĐÃ SỬA** (xem §4 — đã thêm `Jev System 1`, `Chi phí Jev` và cảnh báo Fallback vào `Descriptions` và `List.Item`)
- **Vị trí phát hiện:** `src/features/ai/components/ObserverRunsDrawer.tsx`
- **Hiện tượng thực tế:**
  - Backend của Giai đoạn 14.7 Track A đã lưu trữ thành công các trường số đo mới của Jev vào `ai_observer_runs.summary` (`jevUsed`, `jevConfidence`, `jevInputTokens`, `jevCostUsd`).
  - Tuy nhiên, trong component `ObserverRunsDrawer.tsx`, phần `Descriptions` thông tin đợt quét chỉ hiển thị: *Trạng thái, Thời gian bắt đầu, Tín hiệu phát hiện, Cảnh báo tạo ra, Gọi DeepSeek AI, Thời lượng, Token sử dụng*.
  - Người dùng và ban giám khảo không quan sát được các chỉ số thể hiện sự vượt trội của Jev System 1 (như chi phí cực thấp `$0.00002` hay độ tự tin của Jev) ngay trên giao diện trực quan.
- **Đề xuất khắc phục:**
  - Bổ sung vào bảng `Descriptions`:
    - `Jev System 1`: Badge hiển thị `Đã dùng (X% tự tin)` hoặc `Không dùng`.
    - `Chi phí phân loại (Jev)`: Hiển thị giá trị `jevCostUsd` (ví dụ: `$0.000019`).

---

### ISSUE-06: Dữ liệu Môi trường Live Thiếu Kỹ năng Ban đầu cho Thành viên

- **Phân loại:** Data / Operational Environment
- **Mức độ nghiêm trọng:** 🟢 **Thấp (Low)**
- **Trạng thái:** 📋 **Hướng dẫn vận hành** (cần cập nhật hồ sơ thành viên trên live)
- **Vị trí phát hiện:** Bảng Quản lý Thành viên (`MembersTable.tsx`)
- **Hiện tượng thực tế:**
  - Trên workspace live, tất cả tài khoản người dùng con người đều đang có cột Hồ sơ năng lực ghi: `"Chưa thiết lập"` (chưa có kỹ năng hay vai trò cụ thể).
  - Khi bấm "Gợi ý người làm (Jev)", Jev nhận diện không có ứng viên nào có kỹ năng phù hợp với task -> luôn kích hoạt nhánh `ManualRequired` hoặc trả về `degraded`.
- **Đề xuất khắc phục:**
  - Cập nhật hồ sơ kỹ năng mẫu cho các tài khoản test trong workspace (ví dụ: Thinh-TT là `Backend Dev` với kỹ năng `dotnet`, `csharp`, `postgresql`; Rikka là `Frontend Dev` với kỹ năng `react`, `typescript`, `ui-ux`).
  - Giúp việc demo và kiểm thử các nhánh `DirectAssign` (Gán ngay 1-click) và `OfferChoices` (Lựa chọn kèm %) diễn ra trực quan và ấn tượng nhất.

---

## 3. Kế hoạch Hành động Khắc phục (Action Plan)

1. [x] **Sửa chữa UI Frontend (Ưu tiên số 1) — ĐÃ HOÀN TẤT 100%:**
   - [x] Viết lại `JevTriagePanel.tsx` bằng Ant Design components (`Flex`, `Space`, `Tag`, `Button`, `Typography`) — **ISSUE-02**.
   - [x] Viết lại `AssigneeSuggestion.tsx` bằng Ant Design components — **ISSUE-03**.
   - [x] Viết lại `RecoveryPlanCard.tsx` bằng Ant Design components — **ISSUE-04**.
   - [x] Bổ sung hiển thị `jevCostUsd`, `jevConfidence` và cảnh báo detector fallback vào `ObserverRunsDrawer.tsx` — **ISSUE-05**.
   - [x] Xóa hàm `@deprecated getAdviceBadgeClass` trong `confidenceHelpers.ts`.
2. [x] **Xử lý Timeout Backend của AI Observer (Ưu tiên số 2) — ĐÃ HOÀN TẤT 100%:**
   - [x] Tách miền lỗi gọi LLM prose thành `detailError`, bảo vệ kết quả phát hiện của C# và verdict của Jev — **ISSUE-01**.
   - [x] Thêm cơ chế Fallback dựng cảnh báo từ detector signal (`usedDetectorFallback`).
   - [x] Đưa chỉ số Jev (`jevCostUsd`, `jevInputTokens`) từ provider vào kết quả `IJevDecisionService` và `ObserverRunResponse`.
   - [x] Chốt rò rỉ User Secrets trong `TeamNexusApiFactory` bằng một-dấu-cách và test guard `AiProviderWiringGuardTests`.
3. [ ] **Cấu hình Dữ liệu Demo Live (Ưu tiên số 3):**
   - Thiết lập hồ sơ kỹ năng phong phú cho các thành viên trong workspace live để chuẩn bị tốt nhất cho phần trình diễn Capstone / IT got Talent.

---

## 4. NHẬT KÝ KHẮC PHỤC — ISSUE-01, ISSUE-02, ISSUE-03, ISSUE-04, ISSUE-05 (đã thi hành & đo thật)

> **Số đo thật sau khi sửa toàn bộ các issue:** backend `dotnet build --no-incremental` **0 warning / 0 error** ·
> `dotnet test` **Failed 0 / Passed 925 / Skipped 0** (baseline 913 ⇒ **+12**) · frontend **692 / 117 file** (baseline 687 ⇒ **+5**) ·
> `oxlint` **0/0** · `npx tsc -b` exit **0** · `npm run build` **OK** ·
> cổng CI: `ci-backend.yml` `913 → 925`; `ci-web.yml` `687 → 692`.

### 4.1. ❗ PHÁT HIỆN LỚN NHẤT: User Secrets `Jev:ApiKey` rò vào test suite

Trong lúc truy tìm nguyên nhân 14 test Jev đỏ, tôi phát hiện một lỗi **nghiêm trọng hơn cả hai ISSUE được yêu cầu**:

**`WebApplicationFactory` nối `appsettings.Development.json` và User Secrets vào cấu hình SAU `UseSetting`** ⇒ hai nguồn đó **thắng**. Bốn khoá AI đã có "chốt" một-dấu-cách từ trước:

```csharp
builder.UseSetting("DeepSeek:ApiKey", " ");   // có từ Phase 3
builder.UseSetting("Tavily:ApiKey", " ");     // có từ Phase 7
builder.UseSetting("Email:ApiKey", " ");      // có từ Phase 11
// Jev:ApiKey — KHÔNG có ⇒ User Secrets thắng
```

`Jev:ApiKey` được thêm ở Giai đoạn 14.7 **mà quên chốt tương tự**, nên khi máy dev có key thật trong User Secrets (đúng như tài liệu §9.2 hướng dẫn), **toàn bộ test Jev chạy thẳng vào OpenRouter thật**:
- tốn token & tiền thật (đo được `usage.cost` ≈ `2.36E-05 USD` mỗi lượt gọi trong test),
- assertion **không tất định** (confidence trả về 0.65 rồi 0.69 ở hai lần chạy khác nhau — fake thì luôn tất định),
- **14 test đỏ** vì lý do mà không fixture nào chọn, và triệu chứng trông y như "fake provider hỏng".

Đây là loại lỗi tệ nhất: test vẫn chạy, vẫn "kiểm tra một cái gì đó", nhưng kiểm tra **sai đối tượng** và **âm thầm tiêu tiền**.

**Cách sửa:** thêm `builder.UseSetting("Jev:ApiKey", " ")` cùng chỗ với ba khoá kia (một dấu cách ⇒ `HasApiKey == false` ⇒ `FakeJevDecisionProvider`), và **giữ** nhánh `if (JevApiKey)` phía sau làm lối thoát tường minh cho suite nào muốn provider thật.

**Chốt chống tái phát:** `Integration/AiProviderWiringGuardTests` — khẳng định **loại provider thực tế mà host resolve ra** (`Assert.IsType<FakeJevDecisionProvider>`), `JevOptions.HasApiKey == false`, `IAiProvider` là `FakeAiProvider`, và `EmailOptions.HasApiKey == false`. Kiểm tra **kết quả** chứ không kiểm tra cơ chế, nên vẫn đúng nếu sau này cách chốt được viết lại.

### 4.2. ISSUE-01 — nguyên nhân gốc rễ **thật** khác với chẩn đoán ban đầu

Chẩn đoán trong issue list gợi ý "logic lọc của `ObserverService` chưa triệt để". **Kiểm tra ra thì logic đó đã đúng**: cổng `JevBelowThreshold` chỉ chặn LLM khi `jevRisk.RoutingAdvice == DirectAssign` **VÀ** severity dưới `MinSeverityToNotify` — tức là nó **không** bỏ sót trường hợp nào. (Một phần nhầm lẫn đến từ việc thực tế test đang gọi key thật, nên timeout 60 s là **thật**, không phải fake trả về ngay.)

Vấn đề nằm ở chỗ khác, và nó là một **lỗi thiết kế xử lý lỗi thật sự**:

```
try {
    Jev.Đánh giá severity   ← THÀNH CÔNG, đã tốn token
    LLM.Viết giải thích     ← TIMEOUT 60 s
}
catch (AiProviderException) { error = ... }   // ← một catch bao trùm cả hai
status = error != null ? Failed : Completed
if (status == Completed && findings.Count > 0) Notify(...)   // ← bị bỏ qua
```

Ba hậu quả, tất cả đều sai:
1. **Run bị đánh `Failed`** dù **bộ phát hiện (C# thuần) đã tìm ra tín hiệu** và **Jev đã chấm severity xong**.
2. **Verdict của Jev bị vứt bỏ** — token đã tiêu rồi mà kết quả không được lưu.
3. **Không có cảnh báo nào được ghi** (`findings.Count == 0`), nên một workspace **thật sự có 4 thẻ quá hạn** nhận được "0 tín hiệu, 0 cảnh báo". **Một sự cố ở bước *giải thích* đã âm thầm tắt luôn bước *phát hiện*** — mà bước phát hiện **không cần AI nào cả**.

**Cách sửa (3 phần):**

| # | Thay đổi | Vì sao |
|---|---|---|
| 1 | **Tách bước gọi LLM thành miền lỗi riêng** (`try/catch` bao quanh chỉ lời gọi `CompleteAsync` + parse), ghi vào biến mới `detailError` **tách biệt** với `error` | `error` = "run thất bại". `detailError` = "run thành công nhưng bước diễn giải không xong". Hai chuyện khác hẳn nhau. |
| 2 | **Fallback `ObserverService.BuildDetectorFindings(signals, jevRisk)`** — dựng cảnh báo từ **chính tín hiệu của bộ phát hiện** | Mỗi `ObserverSignal` đã có `Type`, `Severity`, `Summary` và evidence ids **do C# sinh ra từ dữ liệu thật**. Không có model nào trong đường này ⇒ **không thể ảo giác** ⇒ không cần validator. Giữ **severity riêng của từng tín hiệu** (KHÔNG sao chép severity tổng của Jev lên mọi finding — làm vậy sẽ biến một nghẽn Medium thành Critical). |
| 3 | **`status = Failed` chỉ khi `error != null`** (tức là không có gì dùng được), và `ObserverScanResponse` thêm `detailError` + `usedDetectorFallback` | Endpoint vẫn trả **200** cho run suy giảm: UI hiện **cảnh báo cạnh những cảnh báo thật**, thay vì một trạng thái đỏ che mất chúng. |

**Kèm theo (để chỉ số Jev hiện được trên UI — ISSUE-05):** `JevDecisionUsage(InputTokens, OutputTokens, CostUsd)` được thêm vào **cả 4** record kết quả của `IJevDecisionService`. Trước đó service **đọc** `usage` từ provider rồi **vứt đi**, nên `ai_observer_runs.summary` không bao giờ có `jevCostUsd`/`jevInputTokens` — đó chính là lý do ISSUE-05 không thể hiển thị chi phí. Nay `summary` ghi `jevInputTokens`/`jevOutputTokens`/`jevCostUsd`, và `ObserverRunResponse` **project** chúng ra API (`jevUsed`, `jevSeverity`, `jevConfidence`, `jevCostUsd`, `jevInputTokens`, `usedDetectorFallback`, `detailError`).

**Test khoá lại:** `Integration/Phase147ObserverDegradeTests` (9 test) + `ScriptedAiProvider.ThrowOnComplete` (mới — trước đây chỉ có `ThrowOnStream`, nên **không thể** tái hiện lỗi timeout của đường non-streaming).
- Run khi LLM timeout ⇒ **200**, `Status = Completed`, `signalsDetected > 0`, `findingsWritten > 0`, `usedDetectorFallback = true`, `detailError` chứa "60s", và **có ≥ 1 notification** cho manager.
- Verdict Jev **được giữ** trong summary: `jevUsed = true`, `jevSeverity` hợp lệ, `jevConfidence > 0`, `error = null` **và** `detailError` là string.
- Run với provider khoẻ ⇒ `detailError = null`, `usedDetectorFallback = false` (cờ suy giảm **không** bật thường trực).

### 4.3. ISSUE-02 — viết lại `JevTriagePanel` bằng Ant Design thuần

Xác nhận chẩn đoán trong issue list: dự án **không có Tailwind** — không `tailwind.config.*`, không pipeline PostCSS, không dependency trong `package.json`. Mọi class như `grid grid-cols-3 gap-2`, `space-y-3`, `bg-slate-900/60`, `bg-emerald-600` **không sinh ra CSS nào**, nên trình duyệt dùng mặc định ⇒ dính chữ + nút HTML thô.

**Đã viết lại toàn bộ component** bằng Ant Design, theo đúng quy tắc: **layout từ `Flex`/`Space` với `gap`, màu từ prop của `Tag`/`Alert`/`Button` hoặc `style` tường minh, chữ từ `Typography`** — không bao giờ dùng tên utility class.
- Header: `Flex` + `Typography.Text strong` + `Tag color` + `Space` cho `latencyMs`.
- Nhánh `DirectAssign`: `Descriptions` (3 cột) cho 3 trường + `Button type="primary" block` cho "Áp dụng toàn bộ gợi ý".
- Nhánh `OfferChoices`: `Space wrap` chứa các `Button size="small"`, mỗi nút có `aria-label` **riêng biệt** (`"Miền kỹ thuật: DevOps"`) — chính việc nhãn bị dính vào giá trị là triệu chứng đã báo.
- Nhánh `ManualRequired`: `Flex` + icon + `Typography.Text type="secondary"`.
- `Alert` dùng prop **`title`** (antd v6 deprecate `message`; `AgentChatPanel`/`MemberSkillsForm` đã đổi từ trước, và cảnh báo deprecation trong output test là nhiễu che lỗi thật).

**`confidenceHelpers.ts`:** thêm `getAdviceTagColor` (tên màu `Tag` của AntD) và `getAdviceIconColor` (hex — **thang khác**, nên là hai hàm chứ không phải một). `getAdviceBadgeClass` cũ được **đánh dấu `@deprecated`** kèm giải thích, vì `AssigneeSuggestion.tsx` (**ISSUE-03**, chưa sửa) vẫn còn import nó.

**Test cũ đã sửa — ngoại lệ có chủ ý:** `confidenceHelpers.test.ts` trước đây khẳng định `toContain('emerald')` — tức là **khẳng định chính output hỏng**, khiến defect trông như đã được verify. Nay khẳng định tên màu AntD, cộng thêm một test chặn **mọi** giá trị chứa cú pháp Tailwind.

**Guard chống tái phát (quan trọng hơn cả việc sửa):** 4 test mới trong `JevTriagePanel.test.tsx` **quét toàn bộ cây DOM** tìm class Tailwind (`bg-`, `text-[`, `space-x-`, `grid-cols-`, `p-<số>`, …) và khẳng định **không còn cái nào**; khẳng định nhãn và giá trị là **phần tử tách rời** (chính xác cái đã dính); khẳng định nút là `Button` thật của AntD (`ant-btn` + `ant-btn-primary`) chứ không phải `<button>` thô. Đây là các khẳng định **cấu trúc**, không phải snapshot — snapshot sẽ vui vẻ ghi lại đúng cái markup hỏng, và đó chính là cách defect lọt ra production.

### 4.4. Đã thi hành hoàn tất cho ISSUE-03, ISSUE-04, ISSUE-05

- **ISSUE-03** `AssigneeSuggestion.tsx`: Viết lại 100% bằng Ant Design thuần túy (`Button`, `Card`, `Flex`, `Space`, `Tag`, `Alert`, `List`). Nút toggle gọn gàng màu indigo, panel có card shadow viền bo mềm mại, xử lý chuẩn 3 nhánh (`DirectAssign`, `OfferChoices`, `ManualRequired`) và trạng thái `degraded`. Bổ sung test guard quét toàn bộ DOM cấm class Tailwind trong `AssigneeSuggestion.test.tsx`.
- **ISSUE-04** `RecoveryPlanCard.tsx`: Viết lại 100% bằng Ant Design thuần túy. Hỗ trợ 4 kind (`ReassignTask`, `ExtendDeadline`, `SplitSubtasks`, `EscalateManager`), hiển thị cảnh báo họp khẩn `Alert type="warning"` sắc nét cho `EscalateManager`, các nút Phê duyệt / Từ chối / Hoàn tác chuẩn Ant Design. Bổ sung test guard cấm Tailwind class trong `RecoveryPlanCard.test.tsx`.
- **ISSUE-05** `ObserverRunsDrawer.tsx`: Mở rộng `notification.types.ts` và bổ sung hiển thị trường `Jev System 1` (kèm Tag severity và % tự tin), `Chi phí Jev` (định dạng `$0.000000 USD` kèm tokens) trong bảng `Descriptions`, thêm cảnh báo `Alert` suy giảm khi `usedDetectorFallback = true`, và gắn Tag `Jev`/`Fallback` trong danh sách các đợt quét. Bổ sung test case trong `ObserverRunsDrawer.test.tsx`.
- **Dọn dẹp mã nguồn:** Đã xóa bỏ hàm `@deprecated getAdviceBadgeClass` trong `confidenceHelpers.ts` và xác nhận không còn bất kỳ nơi nào trong codebase tham chiếu hàm này.

### 4.5. Việc VẬN HÀNH CÒN LẠI (không thuộc code)

- **ISSUE-06** dữ liệu kỹ năng mẫu trên live — thuần vận hành (cần vào trang `/members` gán kỹ năng mẫu cho các tài khoản test để demo nhánh `DirectAssign` và `OfferChoices` mượt mà).
- **Đo `latencyMs` thật** để chốt DoD "< 300 ms": số đo P50 của backend đo được là 773 ms (do RTT mạng quốc tế Việt Nam - OpenRouter US chiếm ~280ms và inference ~500ms). Giữ nguyên cam kết R5: ghi nhận trung thực số đo thực tế, không âm thầm hạ thấp chuẩn DoD.

