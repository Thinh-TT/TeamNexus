# TeamNexus – Định hướng Đồ án Chuyên ngành & Cuộc thi IT got Talent (Hạng mục AI)

> **Tài liệu thứ 05 của `project-docs/`.** Bốn tài liệu trước (`01-system-specification.md`, `02-tech-stack-decisions.md`, `03-roadmap.md`, `04-database-design.md`) mô tả **sản phẩm và cách xây dựng**. Tài liệu này mô tả **sự chuyển hướng**: TeamNexus từ một **dự án cá nhân phục vụ CV/portfolio** nay được **kế thừa và nâng cấp thành Đồ án Chuyên ngành**, đồng thời trở thành **bài dự thi IT got Talent – hạng mục AI** của Khoa.
>
> **Cách đọc:** đây là **khung định hướng**, không phải tài liệu chia task. Mỗi giai đoạn sẽ được làm rõ chi tiết (checklist D1–Dn, ca biên, bằng chứng, hợp đồng API) trong **phiên riêng** khi bắt đầu — theo đúng tiền lệ `tasks/phase-*.md` + `report/phase-*-test-report.md`.
>
> **Trạng thái tài liệu:** 🟡 Bản khung — sẽ tinh chỉnh sau khi chốt thể lệ cuộc thi và lịch nộp của Khoa.
>
> **Liên quan:** `03-roadmap.md` (nguồn sự thật về tiến độ) · `tasks/phase-14-ai-advanced.md` · `report/phase-14-ai-advanced-test-report.md`.

---

## 1. BỐI CẢNH: VÌ SAO CÓ SỰ CHUYỂN HƯỚNG NÀY

| | **Trước đây (dự án cá nhân)** | **Từ phiên này (đồ án chuyên ngành + cuộc thi)** |
|---|---|---|
| **Mục tiêu** | Portfolio/CV cá nhân, chứng minh năng lực full-stack | Đồ án chuyên ngành có hội đồng nghiệm thu **+** bài dự thi IT got Talent (hạng mục AI) |
| **Động lực hoàn thiện** | "Đủ tốt để đưa vào CV" | "Đủ chặt để bảo vệ trước hội đồng" và "đủ khác biệt để thắng hạng mục AI" |
| **Tiêu chí đánh giá** | Tính năng chạy được, deploy thật | + Tính **học thuật** (bài toán, phương pháp, đo đạc), **đóng góp mới**, **khả năng bảo vệ**, **tính tái lập** |
| **Bằng chứng bắt buộc** | Test xanh, link production | + Báo cáo nghiệm thu, số đo hiệu năng, demo kịch bản, slide, tài liệu kiến trúc |
| **Phạm vi** | Web + AI agent | + **Hệ sinh thái mở** (P15) và **Mobile đa nền tảng** (P16) — hai hướng mở rộng của kỳ đồ án |
| **Ràng buộc thời gian** | Không có deadline cứng | **Mốc cứng**: nộp đồ án, nghiệm thu, vòng thi cuộc thi |

**Điểm mấu chốt:** đây **không phải** làm lại từ đầu. Nền tảng đã chạy production thật là **lợi thế cạnh tranh lớn nhất** — phần lớn đồ án cùng kỳ dừng ở mức "chạy được trên máy local". Rủi ro lớn nhất của hướng đi này **không phải kỹ thuật**, mà là **minh bạch học thuật**: phải phân định rõ **đâu là nền tảng kế thừa** và **đâu là đóng góp mới trong kỳ đồ án** (mục 3).

---

## 2. THÔNG TIN CHUNG

* **Tên dự án:** **TeamNexus**
* **Hình thức thực hiện:** **Cá nhân** (Full-stack Software Engineering — backend, web, mobile, hạ tầng, tài liệu, kiểm thử).
* **Lĩnh vực:** Công nghệ Phần mềm — **Hệ thống phân tán**, **Đa nền tảng Web & Mobile**, **Ứng dụng Trí tuệ Nhân tạo**.
* **Loại hình:** Đồ án Chuyên ngành **+** Bài dự thi **IT got Talent – hạng mục AI**
* **Bối cảnh thực hiện:** Kế thừa và nâng cấp từ dự án cá nhân nghiên cứu độc lập trước đó. Trọng tâm kỳ đồ án là **tối ưu hóa hệ thống cốt lõi hiện có** và **mở rộng 2 hướng phát triển đột phá mới**:
  1. **Giai đoạn 15 — Tích hợp Hệ sinh thái Công cụ bên ngoài** (nhóm **mở rộng hệ thống**).
  2. **Giai đoạn 16 — Ứng dụng Di động Đa nền tảng với Flutter** (nhóm **mở rộng nền tảng**).
* **Môi trường triển khai thực tế (Production) — đã hoạt động:**
  * **Web Client:** [https://app.teamnexus.cloud](https://app.teamnexus.cloud) *(Vercel)*
  * **API Service:** [https://api.teamnexus.cloud](https://api.teamnexus.cloud) *(Render + Docker, .NET 10)*
  * **Database:** Neon Serverless PostgreSQL (10/10 EF Core migration)
  * **Domain & Security:** `teamnexus.cloud` — Cloudflare DNS, SSL Full (Strict), chống DDoS
  * **Email giao dịch:** `TeamNexus <noreply@teamnexus.cloud>` (SPF/DKIM/DMARC qua Cloudflare + Resend)

### 2.1. Trạng thái nền tảng tại thời điểm chuyển hướng (số đo thật)

| Hạng mục | Số đo thực tế | Ý nghĩa với hội đồng / giám khảo |
|---|---|---|
| Giai đoạn hoàn thành | **1 → 14** ✅ (15, 16 chưa bắt đầu) | Nền tảng đã đủ chín để mở rộng, không phải "ý tưởng trên giấy" |
| Backend test | **480 PASS / 0 fail / 0 skip** | Chất lượng được kiểm chứng bằng test tự động, không chỉ demo tay |
| Frontend test | **508 PASS / 0 fail (86 test file)** | |
| Migration EF Core | **10** (mới nhất `Phase13DailyDigest`), `has-pending-model-changes` **sạch** | Schema có kiểm soát, không trôi dạt |
| Chất lượng build | `dotnet build` **0 warning / 0 error** · `oxlint` **0/0** · `tsc -b` **exit 0** | Kỷ luật mã nguồn |
| CI/CD | 2 workflow **xanh** trên `main`, có **cổng ngưỡng test** (480 / 508) | Không thể "âm thầm làm rớt test" |
| Triển khai | Web + API + DB + domain riêng, **đang chạy Internet thật** | Bằng chứng DevOps/Cloud/SSL/Domain — điểm cộng hiếm |

> **Ghi chú học thuật:** các con số trên là **nền tảng kế thừa**. Chúng được dùng làm **baseline** — mọi giai đoạn của kỳ đồ án phải chứng minh **tăng trưởng so với baseline này** (số test, số migration, số endpoint, số đo hiệu năng).

---

## 3. PHÂN ĐỊNH KHỐI LƯỢNG: NỀN TẢNG KẾ THỪA vs. ĐÓNG GÓP TRONG KỲ ĐỒ ÁN

Đây là **mục quan trọng nhất** của tài liệu — trả lời trực tiếp câu hỏi hội đồng chắc chắn sẽ hỏi: *"Phần nào là của em trong kỳ này?"*

```
┌───────────────────────────────────────────────────┬───────────────────────────────────────────────────┐
│   NỀN TẢNG KẾ THỪA (Giai đoạn 1–13, đã xong)      │   ĐÓNG GÓP TRONG KỲ ĐỒ ÁN (Giai đoạn 14–16)       │
│   → Trình bày như ĐIỀU KIỆN XUẤT PHÁT, không       │   → Trình bày như KẾT QUẢ NGHIÊN CỨU              │
│     tính là đóng góp mới                          │                                                   │
├───────────────────────────────────────────────────┼───────────────────────────────────────────────────┤
│ • Modular Monolith .NET 9/10 + React 19 + Vite    │ [14] NÂNG CẤP HỆ THỐNG CỐT LÕI — ✅ HOÀN THÀNH    │
│ • 10 migration PostgreSQL, schema có soft delete  │   • AI Task Chat tương tác thời gian thực qua SSE │
│ • Identity + OAuth Google/GitHub + RBAC 2 lớp     │     (hợp đồng khung meta → delta* → done|error)   │
│ • Kanban real-time (@dnd-kit + SignalR Hub)       │   • Tín hiệu dự báo rủi ro `AtRiskDeadline` +     │
│ • AI Smart Setup (DeepSeek, JSON schema)          │     severity Critical/High/Medium                 │
│ • Accountability Layer (Pending→Approve/Undo)     │   • Project Health Score (5 thành phần = 100)     │
│ • AI Observer (BackgroundService + notifications) │   • AI Board Template (đề xuất → duyệt → board)   │
│ • AI Agent Executor + vòng lặp tool-calling,      │                                                   │
│   guardrail 15 tool-call / 5 phút / 50k token     │ [15] MỞ RỘNG 1 — HỆ SINH THÁI MỞ (P15) 🟡 CHƯA   │
│ • Báo cáo PDF/Excel on-demand (QuestPDF/ClosedXML)│   • Slack / Discord Webhook Notifications         │
│ • Member/Profile + Email (Resend) + Notification  │   • GitHub Webhook: PR merged → task Done         │
│   Center                                          │   • Bảo mật: HMAC-SHA256 + mã hóa PAT            │
│ • Dashboard, Search keyset, @mention, Calendar,   │   • Graceful Degradation khi chưa cấu hình        │
│   Burndown/Velocity, Daily Email Digest           │                                                   │
│ • Deploy thật: Vercel + Render + Neon + Cloudflare│ [16] MỞ RỘNG 2 — MOBILE FLUTTER (P16) 🟡 CHƯA    │
│ • CI/CD GitHub Actions + 988 test tự động         │   • App Flutter (iOS/Android) dùng chung API      │
│                                                   │   • Mobile OAuth flow + flutter_secure_storage    │
│                                                   │   • SignalR real-time qua `signalr_netcore`       │
│                                                   │   • Kanban kéo-thả tối ưu touch & swipe           │
└───────────────────────────────────────────────────┴───────────────────────────────────────────────────┘
```

> **Nguyên tắc trình bày:** phần kế thừa được nêu **ngắn gọn, có số đo, có bằng chứng** — đủ để chứng minh năng lực và độ chín của hệ thống, **không** chiếm thời lượng trình bày chính. Toàn bộ "đất diễn" dành cho **hai hướng mở rộng P15 + P16** (mục 6.6, 6.7), vì đó là phần **duy nhất** hội đồng và giám khảo chưa từng thấy.

---

## 4. TÊN ĐỀ TÀI

**Tiếng Việt:**
> **"Hệ thống quản lý công việc đa nền tảng tích hợp AI Agent điều phối và thực thi (TeamNexus)"**

**Tiếng Anh:**
> **"TeamNexus: A Cross-Platform Workspace Orchestration Platform with an Accountable Autonomous AI Agent"**

*Ghi chú:* tên đề tài nhấn vào **3 trụ**: *đa nền tảng* (Web + Mobile), *AI Agent điều phối & thực thi* (không phải chatbot gợi ý), và *hệ sinh thái mở* (webhook). Cụm **"điều phối" (orchestration)** là từ khoá định vị: hệ thống không chỉ ghi việc, mà **điều phối** ai/cái gì làm việc gì.

---

## 5. Ý TƯỞNG & TÍNH CẤP THIẾT

### 5.1. Đặt vấn đề (Problem Statement)

* **Công cụ hiện tại là "máy ghi chép thụ động":** Trello/Jira/Asana chờ con người nhập liệu và bẻ nhỏ task; nhà quản lý phải tự rà soát để phát hiện chậm trễ ⇒ **phát hiện muộn, phụ thuộc trí nhớ con người**.
* **Cô lập thông tin (siloed environment):** công việc của đội kỹ thuật phân tán trên nhiều nền tảng — thảo luận trên Slack/Discord, mã nguồn trên GitHub, task trên bảng Kanban. **Không có liên kết tự động** ⇒ trạng thái task lệch pha với thực tế phát triển phần mềm (PR đã merge nhưng thẻ vẫn "Đang làm").
* **Hạn chế về tính cơ động (mobility):** thành viên và quản lý cần theo dõi tiến độ, nhận cảnh báo khẩn và **duyệt đề xuất của AI** ngay trên điện thoại, không phải mở máy tính.
* **Thách thức an toàn khi đưa AI vào hệ thống:** AI tự động hoá có thể **ghi sai dữ liệu**, và nếu không có cơ chế kiểm duyệt (Human-in-the-Loop) cùng khả năng hoàn tác (Undo) thì **không thể tin dùng trong môi trường doanh nghiệp**.
* **Khoảng trống học thuật:** đa số nghiên cứu/sản phẩm AI cho quản lý công việc chỉ dừng ở *gợi ý* hoặc *chat*. Việc đưa một **tác nhân tự chủ thực sự hành động** vào hệ thống nhiều người dùng, **có ràng buộc ngân sách**, **có giải trình** và **có thể hoàn tác** vẫn là bài toán mở ở quy mô ứng dụng thực tế.

### 5.2. Ý tưởng cốt lõi (Core Idea)

**TeamNexus** được định vị là **Nền tảng Điều phối Không gian Làm việc Toàn diện (Unified Workspace Orchestration Platform)**:

1. **AI là "Thành viên ảo" (Virtual Co-worker), không phải chatbot:** AI Agent có định danh chính thức, nằm trong danh sách thành viên, **nhận việc trực tiếp trên Kanban**, tự tìm ngữ cảnh nội bộ, tra cứu Internet (Tavily), và **biết hỏi lại** khi đề bài thiếu thông tin (*Awaiting Clarification*).
2. **Quan sát chủ động thay vì chờ báo cáo:** tiến trình nền (**AI Observer**) phân tích dòng chảy công việc để **dự báo** nguy cơ trễ hạn (`AtRiskDeadline`) và tính **điểm Sức khỏe Dự án** (thang 100) từ 5 tham số khách quan, tất định và **kiểm chứng được**.
3. **Lớp Giải trình Minh bạch (AI Accountability Layer):** **mọi** hành động ghi dữ liệu của AI bắt buộc đi qua `Pending` → `Approved`/`Rejected` và **hoàn tác được** (`Undo`). Đây là **bất biến kiến trúc**, không phải tính năng tuỳ chọn.
4. **Hệ sinh thái mở (Open Ecosystem):** đồng bộ hai chiều với GitHub (PR merge → tự động chuyển task sang cột hoàn thành) và phát cảnh báo tức thời sang Slack/Discord.
5. **Trải nghiệm đa nền tảng liền mạch:** cùng một trạng thái Kanban, cùng một tài khoản, cùng một API — đồng bộ thời gian thực qua SignalR trên **cả Web (React) và Mobile (Flutter)**.

### 5.3. Câu hỏi nghiên cứu (Research Questions) — dùng cho phần bảo vệ

| # | Câu hỏi | Cách trả lời trong đồ án |
|---|---|---|
| **RQ1** | Làm sao để một LLM Agent **hành động được** trong hệ thống nhiều người dùng mà **không phá vỡ tính toàn vẹn dữ liệu**? | Accountability Layer: mọi ghi dữ liệu qua `Pending`; đo tỷ lệ đề xuất AI được duyệt |
| **RQ2** | Làm sao **kiểm soát chi phí và vòng lặp vô hạn** của agent? | Guardrail định lượng (15 tool-call / 5 phút / 50.000 token) + `agent_runs` trace; đo token/lượt chạy |
| **RQ3** | Làm sao **dự báo rủi ro trễ hạn** một cách tất định và giải thích được? | `DeadlineRiskRules` là **một nguồn duy nhất** dùng chung cho cả cảnh báo và gauge ⇒ không thể lệch số |
| **RQ4** | Điểm Sức khỏe Dự án có **thực sự phản ánh** tình trạng dự án? | 5 thành phần trọng số **cộng đúng 100** ⇒ điểm 0 chạm tới được; kiểm chứng trên dữ liệu thật |
| **RQ5** | Đồng bộ trạng thái giữa **Web và Mobile** có giữ được tính nhất quán? | Cùng SignalR Hub + cùng endpoint; test real-time trên cả hai client |
| **RQ6** | Tích hợp bên thứ ba có **an toàn** và **không làm hỏng** hệ thống khi thiếu? | HMAC-SHA256 + mã hóa PAT + **Graceful Degradation** (không cấu hình ⇒ hệ thống vẫn nguyên vẹn) |

---

## 6. TỔNG HỢP TÍNH NĂNG CỐT LÕI & CÁC HƯỚNG MỞ RỘNG MỚI

### 6.1. Phân hệ Quản trị, Xác thực & Phân quyền (Auth & Workspace RBAC) — *kế thừa*

* **Xác thực đa phương thức:** OAuth 2.0 (Google, GitHub), phiên làm việc bằng JWT + Refresh Token trong **HttpOnly Cookie**, có rotate refresh token.
* **Phân quyền 2 lớp:**
  * *Identity Role toàn cục* (`Admin` / `User`): quản trị cấp nền tảng. Mọi user OAuth mặc định là `User`.
  * *Workspace Role* (`Admin` / `Manager` / `Member`): lớp phân quyền **thực sự** cho mọi nghiệp vụ trong workspace.
* **Quản lý thành viên:** mời qua email (Resend) với token băm **SHA-256**; phân loại tường minh **`human`** vs **`ai_agent`** qua `workspace_members.member_type` (không suy đoán qua tên hiển thị).
* **Điểm mở rộng cho P16:** cơ chế cấp phát token an toàn cho **Mobile Client** (Bearer Token) — thiết kế đã sẵn sàng từ Giai đoạn 1.

### 6.2. Phân hệ Kanban Thời gian thực & Trực quan hóa — *kế thừa*

* **Real-time Kanban:** kéo-thả `@dnd-kit` (Web), đồng bộ tức thời đa người dùng qua **SignalR Hub & Groups** theo `boardId`, có auto-reconnect.
* **Thẻ tác vụ nâng cao:** độ ưu tiên (Urgent/High/Medium/Low), hạn chót, mô tả Markdown (renderer tự viết, **không** `dangerouslySetInnerHTML`), nhãn, tệp đính kèm.
* **Chế độ Lịch biểu (Calendar View):** thẻ xếp theo **hạn chót tại múi giờ địa phương**, ô có thẻ trễ hạn báo đỏ, gộp `+N`.
* **Tìm kiếm nâng cao:** xuyên board, đa tiêu chí, phân trang **keyset** trên `(updated_at, id)` — tối ưu tài nguyên CSDL so với OFFSET.
* **Bình luận & @mention chống mạo danh:** server **nhận `mentionUserIds` tường minh**, **không parse `@tên`** từ text; chỉ chấp nhận thành viên `human` của workspace.

### 6.3. Phân hệ Trí tuệ Nhân tạo (Agentic AI & Orchestration) — *kế thừa + nâng cấp Giai đoạn 14*

* **AI Smart Setup & AI Board Template:** từ mô tả của trưởng nhóm ⇒ đề xuất **board + 2–6 cột (pin đúng 1 cột `isDone`) + 5–10 task**, luôn qua bước duyệt.
* **AI Agent Executor (thành viên ảo thực thi):**
  * Được giao việc như nhân sự thật, qua **đúng UI assignee hiện có**.
  * Vòng lặp **tool-calling**: `SearchSystemData` · `WebSearch` (Tavily) · `DraftOutput` · `RequestClarification`.
  * **Guardrails:** tối đa **15 tool-call**, timeout **5 phút**, ngân sách **~50.000 token**/lượt chạy; vượt ngưỡng ⇒ dừng, log `BudgetExceeded`, thông báo trưởng nhóm.
  * Theo dõi tiến trình qua bảng `agent_runs` + broadcast SignalR real-time.
* **AI Task Chat qua SSE (Giai đoạn 14):** `POST /api/tasks/{id}/ai-chat/stream` — khung `meta` → `delta`* → `done` | `error`; guard `MaxHistoryMessages`/`MaxHistoryChars` chặn **trước khi gọi provider** (0 token khi vượt ngưỡng); lưu câu trả lời thành bình luận ⇒ đi qua `Pending` `PostComment`.
* **AI Observer & cảnh báo sớm:** tín hiệu **`AtRiskDeadline`** (chưa done, còn < 20% cửa sổ, 0 update trong 48 h, sàn cửa sổ ≥ 2 ngày) + severity `Critical ≤ 24h` / `High ≤ 72h` / `Medium`; chống "tín hiệu bị ăn mất" bằng `signalsPreserved`/`signalsDroppedBeforePrompt`.
* **Chỉ số Sức khỏe Dự án:** `ProjectHealth.Compute` (hàm thuần, 0–100, 4 band) theo 5 tiêu chí: tỷ lệ quá hạn (40) · tỷ lệ rủi ro (20) · thời gian đình trệ (10) · tuổi thọ task mở (15) · mức độ quá tải nhân sự (15) — **tổng trọng số đúng 100**.

### 6.4. Lớp Giải trình Minh bạch (AI Accountability Layer) — *kế thừa, là bất biến kiến trúc*

* **Human-in-the-Loop:** mọi đề xuất/tác vụ ghi dữ liệu của AI đi qua `AiActionLog` ở trạng thái `Pending` **trước khi** chạm dữ liệu nghiệp vụ.
* **Phê duyệt & Hoàn tác:** `Approve` · `Reject` · `Undo` (khôi phục trạng thái ban đầu, ví dụ Undo board template = soft-delete board).
* **Điểm nhấn học thuật:** đây là **cơ chế**, không phải tính năng — kể cả AI Board Template (Giai đoạn 14) và bình luận lưu từ AI Chat cũng đi đúng một cửa duy nhất.

### 6.5. Phân hệ Báo cáo & Thông báo Chủ động — *kế thừa*

* **Biểu đồ tiến độ:** chuỗi thời gian (Progress Series) cho **Burndown & Velocity** — mode `date` (≤ 60 ngày) / `week` (mốc Thứ Hai), tôn trọng `tzOffsetMinutes`.
* **Xuất báo cáo on-demand:** PDF (QuestPDF) + Excel (ClosedXML), sinh trực tiếp từ **RAM stream**, không lưu file tĩnh trên server.
* **Daily Email Digest:** tóm tắt "việc của tôi" mỗi sáng, **chống trùng ở tầng DB** (idempotency key trong `email_messages`) ⇒ host free-tier ngủ/thức vẫn tối đa **1 email/người/ngày**.

---

### 6.6. HƯỚNG MỞ RỘNG MỚI 1 — TÍCH HỢP HỆ SINH THÁI BÊN NGOÀI (GIAI ĐOẠN 15) 🟡 *trọng tâm đồ án*

> **Vì sao thuộc hạng mục AI:** phần này biến AI Observer từ "cảnh báo trong app" thành **tác nhân tác động ra hệ sinh thái thật** (Slack/Discord/GitHub). Giá trị AI tăng theo **mức độ hành động được**, không theo số tính năng.

1. **Thông báo Đa kênh (Slack & Discord):**
   * Thiết kế theo **Adapter Pattern** — `SlackNotificationGateway : IExternalNotificationGateway` (đúng tiền lệ `EmailGateway` đã có).
   * Cấu hình Webhook URL **linh hoạt theo từng Workspace**.
   * Tự động đẩy thông báo khi: AI Observer phát hiện rủi ro `AtRiskDeadline`, task quá hạn, hoặc thành viên mới gia nhập.
2. **Tích hợp Tự động hóa với GitHub:**
   * Gắn liên kết GitHub Issue / Pull Request trực tiếp trên thẻ task.
   * Webhook endpoint `POST /api/webhooks/github` lắng nghe sự kiện GitHub.
   * Khi PR liên kết được **merge** ⇒ hệ thống **tự động** chuyển thẻ task sang cột `is_done`. Đây là "vòng lặp khép kín" giữa **quản lý công việc** và **thực tế mã nguồn**.
3. **Bảo mật tích hợp bên thứ ba:**
   * Xác thực chữ ký số webhook bằng **HMAC-SHA256** (GitHub Webhook Secret).
   * **Mã hóa Personal Access Token (PAT)** trước khi lưu CSDL.
   * **Graceful Degradation:** workspace chưa cấu hình tích hợp ⇒ mọi tính năng lõi hoạt động **y hệt**, không lỗi, không chặn.

**Kết quả đầu ra (deliverables):**
- Notification gateway hoạt động thật trên Slack/Discord (có bằng chứng ảnh/message ID).
- Task tự động chuyển trạng thái từ sự kiện GitHub PR merge (có log webhook + test).
- Test: xác thực HMAC hợp lệ/không hợp lệ · chống replay · PAT mã hóa/giải mã · degradation.

---

### 6.7. HƯỚNG MỞ RỘNG MỚI 2 — ỨNG DỤNG DI ĐỘNG ĐA NỀN TẢNG (FLUTTER – GIAI ĐOẠN 16) 🟡 *trọng tâm đồ án*

> **Vì sao là "đột phá":** không phải viết lại app, mà chứng minh **kiến trúc API đủ sạch để tái sử dụng 100%** cho một nền tảng mới, kèm bài toán khó thật: OAuth flow trên mobile, lưu token an toàn ở KeyStore/Keychain, và **SignalR real-time trên Dart**.

1. **Ứng dụng Flutter (iOS & Android) từ một codebase:**
   * Flutter 3.x / Dart 3.x, giao diện Material 3.
   * Kết nối trực tiếp và **tái sử dụng toàn bộ** hệ sinh thái RESTful API hiện hữu của backend ASP.NET Core.
2. **Đồng bộ dữ liệu thời gian thực:**
   * Tích hợp `signalr_netcore` để kết nối `BoardHub`.
   * Cập nhật tức thời trạng thái Kanban, bình luận mới, trạng thái AI Agent đang chạy — **không cần tải lại**.
3. **Xác thực & Phiên làm việc trên di động:**
   * Luồng Mobile OAuth/JWT an toàn; Refresh Token mã hóa trong `flutter_secure_storage` (**KeyStore** trên Android / **Keychain** trên iOS).
4. **Trải nghiệm người dùng di động (Mobile UX):**
   * Kanban tối ưu thao tác **chạm – vuốt** (touch & swipe), không sao chép nguyên layout desktop.
   * Dashboard cá nhân ("Task của tôi"), chi tiết task, gauge Sức khỏe Dự án, nhận cảnh báo khẩn từ AI Observer.
   * **Màn hình duyệt đề xuất AI** (Approve/Reject/Undo) — trả lời trực tiếp RQ1: kiểm duyệt AI **không bị trói vào máy tính**.

**Kết quả đầu ra (deliverables):**
- App Flutter đăng nhập thành công + kết nối SignalR với server.
- Kanban kéo-thả hoạt động, đồng bộ real-time với Web.
- Bản build **APK** (và bản chạy iOS simulator) kèm bằng chứng trên thiết bị thật.

---

## 7. TỔNG HỢP CÔNG NGHỆ SỬ DỤNG (TECH STACK)

> Nguồn chi tiết & lý do chọn: `02-tech-stack-decisions.md`. Bảng dưới đây nhấn vào **phần nào thuộc kỳ đồ án**.

| Tầng / Phân hệ | Công nghệ | Chi tiết kỹ thuật | Vai trò & Ghi chú |
|---|---|---|---|
| **Backend Framework** | **ASP.NET Core (.NET 9/10)** | C# 13, .NET 10 LTS | Modular Monolith: `Auth`, `Board`, `Ai`, `Reporting`, `Integration` |
| **Real-time Engine** | **ASP.NET Core SignalR** | WebSocket / SSE | Động cơ real-time **dùng chung cho Web và Mobile** |
| **Auth & RBAC** | **Identity + JWT** | OAuth 2.0 (Google, GitHub) | HttpOnly Cookie (Web) & Bearer Token (Mobile) |
| **Background Processing** | **.NET `BackgroundService`** | `IHostedService` | AI Observer, quét rủi ro, Email Digest |
| **CSDL** | **PostgreSQL (Neon Serverless)** | PostgreSQL 16+ | Tối ưu `jsonb` cho payload động & trace tool-calling |
| **ORM** | **Entity Framework Core** | Npgsql provider | Code-First, Keyset Pagination, Global Query Filter (soft delete) |
| **Web Frontend** | **React 19 + TypeScript + Vite** | Ant Design v5, `@dnd-kit` | SPA tốc độ cao |
| **Mobile (MỚI – P16)** | **Flutter & Dart** | Flutter 3.x, Dart 3.x | **App iOS/Android một codebase, Material 3, kéo-thả mượt** |
| **Mobile Real-time (MỚI – P16)** | **`signalr_netcore`** | SignalR Client for Dart | **WebSocket real-time giữa Mobile và `BoardHub`** |
| **Secure Storage (MỚI – P16)** | **`flutter_secure_storage`** | KeyStore / Keychain | **Lưu Refresh Token mã hóa trên thiết bị** |
| **Tích hợp ngoài (MỚI – P15)** | **Webhooks & REST API** | Slack/Discord, GitHub API | **Tự động hoá thông báo + đồng bộ trạng thái từ PR merge** |
| **Bảo mật tích hợp (MỚI – P15)** | **HMAC-SHA256 + mã hóa PAT** | Data Protection API | **Xác thực chữ ký webhook, bảo vệ token bên thứ ba** |
| **AI / LLM** | **DeepSeek API** | OpenAI-compatible, Function/Tool Calling | Suy luận mạnh, chi phí tối ưu |
| **Web Intelligence** | **Tavily Search API** | REST client | Tool `WebSearch` cho AI Agent |
| **Email giao dịch** | **Resend API + MailKit** | Cloudflare SPF/DKIM/DMARC | Mời thành viên + Daily Digest |
| **Xuất báo cáo** | **QuestPDF & ClosedXML** | RAM stream | PDF + Excel on-demand |
| **Bảo mật & tên miền** | **Cloudflare** | DNS, SSL Full (Strict) | Bảo vệ API & webhook endpoint |
| **Hạ tầng** | **Vercel · Render (Docker) · Neon** | Cloud free tier | Web / API / DB |
| **Kiểm thử** | **xUnit · Vitest · Flutter Test** | Unit & Integration | Baseline **480 backend / 508 frontend** |

---

## 8. KẾ HOẠCH TRIỂN KHAI TRONG KỲ ĐỒ ÁN

> Lịch dưới đây theo **16 tuần** làm mốc tham chiếu. Sẽ được **hiệu chỉnh theo mốc cứng của Khoa và thể lệ cuộc thi** ngay khi có thông tin
> **Nguyên tắc:** mỗi phân đoạn có **Deliverable kiểm chứng được** — không có phân đoạn nào kết thúc bằng "đã viết code".

| STT | Phân đoạn công việc | Nội dung chi tiết | Kết quả đầu ra (Deliverables) | Tiến độ |
|:-:|---|---|---|:-:|
| **1** | **Chốt phạm vi học thuật & Đóng băng nền tảng kế thừa** | • Điền mục 2.2 (thể lệ, mốc, biểu mẫu).<br>• Kiểm toán baseline: xác nhận 480/508 test, 10 migration, 0 warning.<br>• Viết **chương "Nền tảng kế thừa"** – phân định rõ ranh giới đóng góp.<br>• Chốt RQ1–RQ6 và phương pháp đo. | Đề cương được duyệt · Báo cáo kiểm toán baseline · Chương 2 bản thảo · Bộ RQ + chỉ số đo | Tuần 1–2 |
| **2** | **Gia cố & Tối ưu hệ thống cốt lõi (Giai đoạn 14 – đóng gói học thuật)** | • Rà soát & bổ sung test cho AI Task Chat SSE, `AtRiskDeadline`, Project Health Score, AI Board Template.<br>• **Đo đạc định lượng**: độ trễ SSE, token/lượt agent, độ chính xác cảnh báo rủi ro trên dữ liệu thật.<br>• Tinh chỉnh guardrail dựa trên số đo. | Backend **> 480** test · Frontend **> 508** test · Bảng số đo hiệu năng AI · Báo cáo nghiệm thu Giai đoạn 14 (đã có, bổ sung phần đo) | Tuần 3–5 |
| **3** | **Mở rộng 1 — Tích hợp Hệ sinh thái bên ngoài (Giai đoạn 15)** | • Hiện thực `SlackNotificationGateway` / `DiscordNotificationGateway` (**Adapter Pattern**).<br>• Webhook `POST /api/webhooks/github` + xác minh **HMAC-SHA256**.<br>• PR merged ⇒ tự động chuyển task sang `is_done`.<br>• Mã hóa PAT trong CSDL · Chip "PR #123" trên `TaskCard`.<br>• Kiểm thử **graceful degradation**. | Gateway hoạt động thật trên Slack/Discord · Task tự cập nhật từ GitHub (kèm log webhook) · Bộ test HMAC/replay/PAT · `tasks/phase-15-*.md` + `report/phase-15-*-test-report.md` | Tuần 6–8 |
| **4** | **Mở rộng 2 — Kiến trúc & Nền tảng Mobile (Giai đoạn 16 – Phần 1)** | • Khởi tạo project Flutter, State Management, Theme Material 3.<br>• Luồng đăng nhập Mobile + `flutter_secure_storage`.<br>• Tích hợp `signalr_netcore` kết nối `BoardHub`.<br>• Lớp API client dùng chung contract với Web. | App Flutter đăng nhập thành công · Kết nối SignalR ổn định (có auto-reconnect) · Flutter Test cho tầng auth/network | Tuần 9–11 |
| **5** | **Mở rộng 2 — Nghiệp vụ & UX Mobile (Giai đoạn 16 – Phần 2)** | • Kanban kéo-thả tối ưu touch & swipe.<br>• Chi tiết task, Dashboard "Task của tôi", gauge Sức khỏe Dự án.<br>• **Màn hình duyệt đề xuất AI** (Approve/Reject/Undo).<br>• Nhận cảnh báo khẩn từ AI Observer (push/in-app).<br>• Kiểm thử trên **thiết bị thật**. | APK + bản chạy iOS simulator · Kanban real-time đồng bộ với Web · Báo cáo kiểm thử thiết bị thật | Tuần 12–14 |
| **6** | **Kiểm thử toàn diện · Đóng gói hồ sơ · Luyện demo** | • Integration Test (backend), Vitest (web), Flutter Test (mobile).<br>• Đo hiệu năng, bảo mật, khả năng chịu tải.<br>• Hoàn thiện **quyển báo cáo đồ án** + **slide** + **kịch bản demo**.<br>• **Diễn tập phản biện**: trả lời RQ1–RQ6, giải thích ranh giới kế thừa/đóng góp. | Bộ mã nguồn hoàn chỉnh · APK/iOS build · Quyển báo cáo · Slide · Kịch bản demo · Bộ câu hỏi–đáp phản biện | Tuần 15–16 |

### 8.1. Cổng chất lượng phải giữ ở mọi phân đoạn (Definition of Done)

Kế thừa kỷ luật đã có từ Giai đoạn 10–14 — **không** hạ chuẩn vì áp lực thi cử:

- [ ] `dotnet build TeamNexus.sln -m:1 -nr:false --no-incremental` = **0 warning / 0 error**
- [ ] Backend test **không giảm** so với baseline (hiện **480**), và **cổng ngưỡng CI được nâng** tương ứng
- [ ] `dotnet ef migrations list` **đúng số kỳ vọng**; `has-pending-model-changes` **sạch**
- [ ] Frontend: `npm test` không giảm (hiện **508**), `npm run lint` **0/0**, `npx tsc -b` exit 0, `npm run build` OK
- [ ] Mọi hành động ghi dữ liệu của AI **đi qua Accountability Layer** (bất biến, không có ngoại lệ)
- [ ] Không có secret nào bị commit; cấu hình qua User Secrets / biến môi trường
- [ ] Tài liệu: cập nhật `01`–`04` + tài liệu `tasks/`/`report/` của giai đoạn đó

---

## 9. ĐIỂM NỔI BẬT & ĐÓNG GÓP CỦA ĐỀ TÀI

1. **Khối lượng có tính nâng cấp, không làm lại việc tầm thường:**
   Kế thừa thành quả nghiên cứu trước để **tiến xa hơn**, tập trung vào hai bài toán lớn của kỹ thuật phần mềm hiện đại: **tích hợp hệ sinh thái mở (Webhooks/DevOps)** và **trải nghiệm đa nền tảng (Web + Mobile real-time)**.
2. **AI đột phá nhưng an toàn (Agentic AI with Accountability):**
   Không phải chatbot gợi ý chung chung, mà là **"thành viên ảo" có khả năng hành động**, biết tra cứu, biết **tự hỏi lại khi thiếu thông tin**, đặt dưới **kiểm duyệt nghiêm ngặt** của con người và **hoàn tác được** — kèm **guardrail định lượng** để chi phí không vượt kiểm soát.
3. **Thực chứng triển khai thực tế (Production-Ready):**
   Web + API + DB + domain riêng **đang chạy trên Internet thật** ([https://app.teamnexus.cloud](https://app.teamnexus.cloud)), có **CI/CD** và **988 test tự động** — chứng minh năng lực vận hành (DevOps, Cloud, SSL, Domain) chứ không chỉ năng lực lập trình.
4. **Hồ sơ kỹ thuật chuẩn mực & tái lập được:**
   Kiến trúc Modular Monolith phân lớp rõ ràng, tài liệu thiết kế CSDL đầy đủ, quy trình phát triển có kỷ luật (mỗi giai đoạn có tài liệu chia task **và** báo cáo nghiệm thu đo thật).
5. **Đóng góp có thể kiểm chứng, không chỉ tuyên bố (dành cho hạng mục AI):**
   Các thành phần "trí tuệ" của hệ thống đều là **hàm thuần, tất định, có test** — `DeadlineRiskRules` (một nguồn duy nhất cho cảnh báo và gauge), `ProjectHealth.Compute` (5 trọng số **cộng đúng 100**), `ReportAggregator.BuildProgressSeries`. Đây là điểm khác biệt so với các sản phẩm "gọi API LLM rồi hiển thị kết quả".

---

## 10. RỦI RO & BIỆN PHÁP (RISK REGISTER)

| # | Rủi ro | Mức độ | Biện pháp giảm thiểu |
|:-:|---|:-:|---|
| **R1** | **Bị đánh giá là "dùng lại đồ án/đồ án cũ"** | 🔴 Cao | Mục 3 (bảng phân định) + chương riêng "Nền tảng kế thừa" trong quyển; nêu rõ baseline có số đo; đóng góp mới = **P15 + P16 + đóng gói học thuật P14** |
| **R2** | **Ôm đồm 2 hướng mở rộng (P15 + P16) trong một kỳ** | 🔴 Cao | Cắt theo chiều dọc: P15 xong trước (tuần 6–8), P16 chia 2 phần có mốc rõ; **bản Web luôn là bản demo dự phòng**; nếu trễ ⇒ P16 rút về "Kanban + Auth + real-time" và ghi rõ phạm vi đã cắt |
| **R3** | **Chi phí API LLM/tìm kiếm vượt kiểm soát** | 🟡 Vừa | Guardrail đã có (15 tool-call / 5 phút / 50k token); `MaxHistoryChars` chặn **trước** khi gọi provider; theo dõi `tokens_used` trong `agent_runs` |
| **R4** | **Hạ tầng free-tier ngủ (cold start ~30–60 s)** làm hỏng buổi demo | 🟡 Vừa | "Làm nóng" server trước khi demo; frontend đã có auto-reconnect + thông báo tiếng Việt; **quay video demo dự phòng** |
| **R5** | **Rò rỉ secret** (OAuth secret, PAT GitHub, API key AI) | 🟡 Vừa | User Secrets ở local, biến môi trường ở production; mã hóa PAT trong DB; HMAC-SHA256 cho webhook; **rà soát trước mỗi lần nộp** |
| **R6** | **Vỡ tính nhất quán Web ↔ Mobile** khi thêm P16 | 🟡 Vừa | Dùng **cùng endpoint, cùng SignalR Hub, cùng vocabulary trạng thái**; kiểm thử đồng thời hai client; backend đã xử lý sẵn `dueDate` offset & priority enum (BUG-1/BUG-2 Giai đoạn 10) |
| **R7** | **Thiếu thời gian viết báo cáo/slide** (dồn vào tuần cuối) | 🟡 Vừa | Viết báo cáo **cuốn chiếu theo từng giai đoạn** (mỗi `report/phase-*` là một phần chương); chốt slide trước tuần 15 |
| **R8** | **Không nắm rõ thể lệ cuộc thi** ⇒ chuẩn bị sai trọng tâm | 🟡 Vừa | Chốt mục 2.2 **trước tuần 2**; nếu thể lệ chấm nặng phần trình bày ⇒ dồn thời gian vào kịch bản demo & số đo |

---

> **Kết luận định hướng:** TeamNexus bước vào kỳ đồ án với một lợi thế hiếm — **một hệ thống đã chạy thật, đã được kiểm thử tự động và đã có hạ tầng hoàn chỉnh**. Vì vậy toàn bộ năng lượng của kỳ đồ án được dồn vào **hai hướng mở rộng P15 + P16** và vào **việc chứng minh đóng góp bằng số đo**, thay vì dành cho những phần nền tảng đã xong. Ranh giới giữa **kế thừa** và **đóng góp mới** được ghi rõ ngay trong tài liệu này (mục 3) để bảo vệ tính minh bạch học thuật trước hội đồng.
