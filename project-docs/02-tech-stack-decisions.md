# TeamNexus – Quyết định Công nghệ & Lưu ý Phát triển (Tech Stack & ADRs)

> Tài liệu này ghi nhận các Quyết định Kiến trúc (Architectural Decision Records – ADRs), lựa chọn công nghệ và các nguyên tắc kỹ thuật cốt lõi của dự án **TeamNexus**.

---

## 1. Kiến trúc tổng thể

**Modular Monolith trên nền tảng ASP.NET Core (.NET 10)** — không tách microservices.

- Phân tách mã nguồn thành **4 module nghiệp vụ độc lập**: `Auth`, `Board` (Kanban & Workspace), `Ai` (Hybrid AI Jev/DeepSeek, Accountability Layer, Observer, Agent Executor, RAG, Email & External Integrations), và `Reporting`.
- **Chiều phụ thuộc nghiêm ngặt (`Ai → Board` một chiều):** Module `Board` tuyệt đối không tham chiếu `Ai`. Khi `Board` cần kích hoạt khả năng do `Ai` cài đặt (như ghi nhật ký hoạt động, gửi thông báo, gửi email, giải quyết danh tính AI Agent), `Board` khai báo giao diện cổng (Port) kèm bản cài đặt mặc định an toàn (`Null*`), và `AiModule` đăng ký ghi đè (Override) trong Dependency Injection. Khi `Ai` cần tác động ngược vào thực thể của `Board`, `Board` mở các cổng dịch vụ hẹp (`ITaskService`, `ITaskAutomation`).
- **Lý do lựa chọn:** Dễ dàng triển khai và vận hành trên hạ tầng đám mây chi phí thấp, loại bỏ độ trễ mạng nội bộ giữa các dịch vụ nhưng vẫn giữ ranh giới miền (Bounded Contexts) rõ ràng, sạch sẽ.

---

## 2. Tech Stack theo từng phân hệ

### 2.1. Xác thực & Phân quyền (Auth & Two-Layer RBAC)
- **Công nghệ:** ASP.NET Core Identity + OAuth 2.0 handlers tích hợp sẵn (Google / GitHub), cấp phát JWT Access Token + Refresh Token qua `HttpOnly Cookie`.
- **Không dùng:** Keycloak tự host — tránh tiêu tốn tài nguyên RAM/hạ tầng không cần thiết.
- **Mô hình phân quyền hai lớp:**
  - **Identity Role toàn cục (`Admin` / `User`):** Gắn vào JWT claim. `User` là mặc định cho mọi tài khoản đăng ký qua OAuth. `Admin` chỉ gán cho quản trị viên hệ thống (`SystemAdminPolicy`).
  - **Workspace Role (`Admin` / `Manager` / `Member`):** Lưu trong `workspace_members.role`, kiểm tra tại tầng dịch vụ (`IWorkspaceAccess`) cho mọi nghiệp vụ trong không gian làm việc. Một người dùng có thể là `Admin` ở workspace A nhưng là `Member` ở workspace B.
- **Lưu ý bảo mật:**
  - Cấu hình Cookie `SameSite=Lax/None` + `Secure` + `HttpOnly`.
  - Áp dụng bộ lọc chống CSRF (`AntiforgeryValidationEndpointFilter` kiểm tra header `X-XSRF-TOKEN`) trên toàn bộ các endpoint thay đổi dữ liệu dùng xác thực cookie.
  - Xoay vòng (rotate) Refresh Token mỗi lần sử dụng và thu hồi toàn bộ chuỗi nếu phát hiện sử dụng lại token đã thu hồi (chống replay).
  - Chặn hoàn toàn tài khoản hệ thống (`users.is_system = true`) và tài khoản thành viên ảo (`ai_agent`) khỏi luồng đăng nhập OAuth/JWT và làm mới token.

### 2.2. Bảng điều khiển Kanban Thời gian thực (Real-time Kanban)
- **Công nghệ:** ASP.NET Core SignalR (`BoardHub`).
- **Lưu ý kỹ thuật:**
  - Sử dụng SignalR Group theo từng bảng (`board-{boardId}`) và kiểm tra quyền thành viên workspace mỗi khi client gọi `JoinBoard`, đảm bảo cô lập dữ liệu tuyệt đối giữa các nhóm.
  - Giao diện Web áp dụng cập nhật lạc quan (Optimistic UI) khi kéo-thả và đồng bộ lại khi máy chủ xác nhận; xung đột chỉnh sửa đồng thời xử lý theo chiến lược *last-write-wins*.
  - Tích hợp cơ chế tự động kết nối lại (`withAutomaticReconnect`) và thông báo trạng thái kết nối thân thiện để thích ứng với hiện tượng ngủ (cold-start) trên hạ tầng cloud free-tier.

### 2.3. AI Smart Setup, AI Board Template & AI Observer (System 2 LLM)
- **Công nghệ:** DeepSeek API (chuẩn giao thức OpenAI-compatible), gọi qua `HttpClient` định danh riêng.
- **Trừu tượng hóa Provider:** Bọc toàn bộ giao tiếp LLM qua 3 giao diện cổng tách biệt theo mục đích:
  1. `IAiProvider` (`CompleteAsync`): Sinh kết quả đơn lượt kèm ép khuôn `json_object` (dùng cho Smart Setup, Board Template, Observer Summarizer).
  2. `IAiToolCallingProvider` (`ChatAsync`): Hỗ trợ vòng lặp gọi hàm nhiều bước (dùng cho AI Agent Executor).
  3. `IAiStreamingProvider` (`StreamAsync`): Truyền tải luồng văn bản theo thời gian thực qua Server-Sent Events (dùng cho Task Chat & Board Agent Chat).
- **Xử lý ngoại lệ & Chế độ Ngoại tuyến:** Khi khóa `DeepSeek:ApiKey` để trống, hệ thống tự động đăng ký `FakeAiProvider` (hoạt động tất định, không cần mạng, 0 tốn token) phục vụ phát triển cục bộ và kiểm thử tự động CI/CD.

### 2.4. Lớp Giải trình AI (Accountability Layer)
- **Công nghệ:** Bảng quan hệ `ai_action_logs` trong PostgreSQL kết hợp mẫu thiết kế Strategy (`IAiActionApplier` cho từng loại hành động).
- **Nguyên tắc bất biến:**
  - Mọi đề xuất ghi dữ liệu của AI phải đi qua `AiActionService`: khởi tạo ở trạng thái `Pending`, chỉ thực thi vào bảng nghiệp vụ khi Quản lý bấm `Approve` (sử dụng cơ chế Compare-And-Swap chống duyệt trùng), và hỗ trợ hoàn tác (`Undo`) dựa trên `before_snapshot` / `applied_snapshot`.
  - Quản lý giao dịch (Transaction) tập trung tại `AiActionService`: các lớp `IAiActionApplier` thực thi bên trong giao dịch sẵn có của `AiActionService` và tuyệt đối không mở giao dịch lồng nhau.

### 2.5. Xuất báo cáo Đa định dạng (PDF / Excel)
- **PDF:** QuestPDF (Community License — kết xuất đồ họa vector sắc nét, hỗ trợ đầy đủ tiếng Việt).
- **Excel:** ClosedXML (MIT License — tạo bảng tính `.xlsx` gồm nhiều trang kèm định dạng thanh dữ liệu, bộ lọc tự động và cố định dòng tiêu đề).
- **Lưu ý:** Khởi tạo tệp theo yêu cầu trực tiếp trong bộ nhớ RAM (`byte[]`), không lưu tệp vật lý trên ổ đĩa máy chủ.

### 2.6. Giao diện Web (Frontend SPA)
- **Công nghệ cốt lõi:** React 19.2 + TypeScript 6.0 + Vite 8.2.
- **Hệ thống Thiết kế & UI Kit:** Ant Design v6 (`antd`) kết hợp bộ quy chuẩn thiết kế **Luminous Enterprise v4.2** (quản lý qua biến CSS `--tn-*`, phông chữ *Be Vietnam Pro* tối ưu dấu tiếng Việt và *JetBrains Mono* cho các định danh kỹ thuật/số liệu). Không sử dụng Tailwind CSS và không dùng `dangerouslySetInnerHTML`.
- **Quản lý trạng thái & Kéo-thả:** Zustand cho trạng thái bảng Kanban/phiên đăng nhập, `@dnd-kit/core` + `@dnd-kit/sortable` cho thao tác kéo-thả thẻ trên bảng và kéo thẻ thả trực tiếp vào khung AI Agent Chat.
- **Giao tiếp API & SSE Streaming:** Axios (`httpClient` tự động đính kèm cookie và header `X-XSRF-TOKEN`) cho REST API; sử dụng `fetch` + `ReadableStream` + `TextDecoder` cho các luồng SSE `POST` (do `EventSource` mặc định của trình duyệt không hỗ trợ phương thức `POST` và header CSRF).

### 2.7. Ứng dụng Di động (Mobile — Giai đoạn 16)
- **Công nghệ:** Flutter 3.x / Dart 3.x, giao diện Material 3, tái sử dụng chung toàn bộ hệ thống REST API và hợp đồng DTO append-only với bản Web.
- **Lưu ý kỹ thuật:** Sử dụng `signalr_netcore` để đồng bộ thời gian thực với `BoardHub` và `flutter_secure_storage` (KeyStore / Keychain) để bảo vệ phiên đăng nhập trên thiết bị di động.

### 2.8. AI Agent Executor & Đội ngũ Chuyên gia (Multi-Agent Personas)
- **Định danh thành viên ảo:** Biểu diễn bằng bản ghi `users` kết hợp `workspace_members` có `member_type = 'ai_agent'` và `persona_key` (`general`, `scrum_master`, `tech_lead`, `qa_security`), khởi tạo tự động (lazy) cho từng workspace.
- **Bộ 7 công cụ ReAct (Whitelist):** `SearchSystemData`, `WebSearch` (qua **Tavily Search API**), `DraftOutput`, `RequestClarification`, `InspectMemberWorkload`, `MatchSkills`, và `ValidateAcceptanceCriteria`.
- **Theo dõi tiến trình & Guardrails:** Ghi nhật ký từng lượt chạy tại bảng append-only `agent_runs` (`tool_call_trace`, lượng token tiêu thụ, nguyên nhân dừng), phát sóng tiến độ qua SignalR (`AgentRunProgress`) và cưỡng chế giới hạn an toàn: tối đa **15 tool-call**, **20 lượt gọi LLM**, **5 phút** chạy và **~50.000 token** mỗi lượt. Có tiến trình `AgentRunReaper` tự động thu dọn các lượt chạy mồ côi khi máy chủ khởi động lại.

### 2.9. Dịch vụ Email Giao dịch & Daily Digest
- **Công nghệ:** [Resend](https://resend.com) REST API (tích hợp xác thực tên miền `teamnexus.cloud` qua Cloudflare DNS với đầy đủ SPF, DKIM, DMARC, gửi từ `TeamNexus <noreply@teamnexus.cloud>`). Khi thiếu khóa API, hệ thống tự động dùng `NullEmailSender` an toàn.
- **Phạm vi sử dụng:** Gửi email lời mời tham gia workspace, gửi thông báo nhanh từ Quản lý tới thành viên, và gửi bản tin tóm tắt công việc hàng ngày (**Daily Email Digest** — có cơ chế chống gửi trùng ở tầng CSDL theo khóa `(kind = 'DailyDigest', to_email, ngày UTC)` và mặc định tắt `Digest:Enabled = false` ở môi trường dev/CI).

### 2.10. Kiến trúc Hybrid AI: Mô hình Quyết định Jev (System 1) & Embeddings
- **Công nghệ System 1:** Mô hình quyết định không sinh văn bản **TypeSafe Jev (`typesafe/jev-1.13`)** gọi qua **OpenRouter Decisions API** (`POST {Jev:BaseUrl}{Jev:DecisionsPath}`, mặc định `/api/alpha/decisions`).
- **Đặc điểm kỹ thuật:**
  - Khác biệt hoàn toàn với `/chat/completions`: nhận `{ model, state, questions }` và trả về dữ liệu có kiểu tĩnh (`choice` kèm phân phối xác suất & `confidence`, `score` trên thang đo thứ tự, `noul` cho câu hỏi nhị phân). Được cài đặt qua client riêng `JevDecisionHttpProvider` (và `FakeJevDecisionProvider` khi chạy ngoại tuyến).
  - **Cưỡng chế rẽ nhánh trong mã nguồn (`ConfidenceRouting`):** `≥ 0.85` → `DirectAssign`; `0.50 .. < 0.85` → `OfferChoices`; `< 0.50` → `ManualRequired` (xóa trắng các trường gợi ý để tránh áp đặt khi độ tự tin thấp).
  - **Bảo mật định danh tuyệt đối:** Không bao giờ gửi địa chỉ email hay mã `Guid` thật cho Jev (ánh xạ ứng viên sang nhãn ẩn danh `cand-1`, `cand-2` do máy chủ quản lý).
- **Dịch vụ Nhúng Vector (`Embeddings`):** Tách cấu hình độc lập (`OpenRouterEmbeddingProvider` tương thích chuẩn OpenAI `POST /embeddings` + `FakeEmbeddingProvider` băm tất định SHA-256 khi ngoại tuyến). Cưỡng chế kiểm tra số chiều `Embeddings:Dimensions = 1536` ngay lúc khởi động (`ValidateOnStart`) để khớp tuyệt đối với cột `vector(1536)` trong CSDL.

### 2.11. Tìm kiếm Ngữ nghĩa Vector với `pgvector`
- **Công nghệ:** Extension `pgvector` trên PostgreSQL 18 kết hợp gói thư viện **`Pgvector.EntityFrameworkCore` (0.3.0)** trong `TeamNexus.Persistence` (đăng ký qua `npgsql => npgsql.UseVector()`).
- **Thiết kế lưu trữ & Truy vấn:**
  - Sử dụng cột cố định số chiều `vector(1536)` trên bảng `workspace_knowledge_chunks` để cơ sở dữ liệu chặn mọi vector sai số chiều.
  - Truy vấn độ tương đồng ngữ nghĩa bằng khoảng cách cosine (`.CosineDistance(...)` sinh toán tử `<=>`), đi kèm chỉ mục **HNSW** khai báo đúng họ toán tử **`vector_cosine_ops`**.
  - Quá trình cắt đoạn và nhúng vector được tách ra tiến trình nền (`KnowledgeEmbeddingRunner`), đảm bảo thao tác bấm phê duyệt tài liệu của Quản lý luôn phản hồi tức thời mà không phụ thuộc độ trễ mạng của nhà cung cấp embedding.

### 2.12. Tích hợp Bên ngoài: Data Protection, Transactional Outbox & Bảo mật Webhook
- **Giữ nguyên kiến trúc 4 Modules:** Toàn bộ dịch vụ tích hợp Slack, Discord và GitHub được đặt trong `TeamNexus.Modules.Ai` (`Services/Integrations`, `Endpoints`), giao tiếp một chiều với `Board` qua cổng `ITaskAutomation`.
- **Bảo vệ khóa bí mật (`ASP.NET Core Data Protection`):** Mã hóa bí mật GitHub Webhook at-rest với mục đích định danh riêng (`TeamNexus.Integrations.GitHub.WebhookSecret.v1`), chỉ trả plaintext duy nhất 1 lần khi khởi tạo hoặc xoay vòng khóa (`rotate-secret`). Vòng khóa (Key ring) được lưu trữ bền vững trong PostgreSQL tại bảng `data_protection_keys` (`PersistKeysToDbContext<TeamNexusDbContext>()`) để không bị mất khi container trên Render khởi động lại; hỗ trợ cấu hình chứng chỉ PFX (`CertPath`) để mã hóa vòng khóa trên môi trường VPS.
- **Transactional Outbox & Chống SSRF (Slack / Discord):**
  - `WebhookOutboxExternalAlertSink` cắm vào điểm nối `IExternalAlertSink` của `NotificationService` (an toàn, không làm gián đoạn giao dịch chính nếu kênh ngoài lỗi). Tiến trình `WebhookOutboxRunner` gửi tin bất đồng bộ với chiến lược thử lại `5s → 30s → 2m → 10m → 30m` và tự ngắt mạch sau 10 lỗi liên tiếp.
  - `WebhookUrlValidator` cưỡng chế giao thức `https://` (cổng 443), chỉ cho phép tên miền thuộc danh sách trắng (`hooks.slack.com/services/...`, `discord.com/api/webhooks/...`, `discordapp.com/api/webhooks/...`) và chặn toàn bộ địa chỉ IP trần, `localhost`, loopback, link-local cùng dải mạng riêng RFC1918.
- **Xác thực GitHub Webhook (`HMAC-SHA256`):** Endpoint công khai `POST /api/webhooks/github/{webhookToken:guid}` giới hạn kích thước body tối đa `256 KB` (`413`), xác thực chữ ký `X-Hub-Signature-256` trực tiếp trên mảng byte thô bằng hàm so sánh hằng thời gian `CryptographicOperations.FixedTimeEquals` (`401`), và chống phát lại dựa trên khóa duy nhất `(integration_id, delivery_id)`.

---

## 3. Hạ tầng Triển khai & Phân bổ Tên miền (`teamnexus.cloud`)

Toàn bộ hệ thống production được quản lý định danh và bảo mật tập trung qua **Cloudflare DNS & SSL**:

| Thành phần | Địa chỉ Production | Dịch vụ triển khai | Lưu ý kỹ thuật |
|---|---|---|---|
| **DNS & SSL** | `teamnexus.cloud` | Cloudflare (Free) | Quản lý DNS, SSL/TLS Full (Strict), bảo vệ DDoS và cấu hình bản ghi xác thực cho Resend |
| **Frontend Web** | `https://app.teamnexus.cloud` | Vercel (Free) | React 19 + Vite 8 + TypeScript 6, trỏ CNAME `cname.vercel-dns.com`. Biến môi trường: `VITE_API_BASE_URL=https://api.teamnexus.cloud/api` |
| **Backend API** | `https://api.teamnexus.cloud` | Render Web Service | Chạy Docker container (`Dockerfile`, cổng 10000). Cấu hình `Auth__CookieSameSite=None`, `Cors__AllowedOrigins__0=https://app.teamnexus.cloud`, `Integrations__PublicApiBaseUrl=https://api.teamnexus.cloud` |
| **Database** | Neon Cloud | Neon PostgreSQL 18 Serverless | Hỗ trợ `pgvector`, kết nối SSL Mode `Require`, quản lý qua 16 EF Core migrations |
| **Email Service** | `noreply@teamnexus.cloud` | Resend + Cloudflare DNS | Đã xác thực DKIM/SPF/DMARC, gửi email giao dịch và Daily Digest trực tiếp vào hộp thư thành viên |
| **File Export** | In-memory stream | Backend (QuestPDF / ClosedXML) | Kết xuất trực tiếp từ bộ nhớ RAM, không lưu trữ tệp tĩnh lâu dài trên đĩa |

---

## 4. CI/CD & Kiểm thử Tự động

- **CI/CD:** GitHub Actions (`.github/workflows/ci-backend.yml` và `ci-web.yml`) tự động kiểm tra biên dịch `0 Warning / 0 Error`, kiểm tra độ lệch mô hình EF Core (`has-pending-model-changes`) và thực thi toàn bộ bộ kiểm thử cùng cổng ngưỡng số lượng test tối thiểu.
- **Kiểm thử Backend:** xUnit (gồm nhóm `Pure` unit tests không phụ thuộc CSDL và nhóm `Integration` tests chạy trên PostgreSQL thật).
- **Kiểm thử Frontend:** Vitest + React Testing Library + kiểm tra tĩnh `oxlint` và `tsc -b`.
- **Lưu ý Image PostgreSQL cho CI & Máy phát triển cục bộ:**
  - Do tính năng Kho tri thức RAG sử dụng extension bên thứ ba `vector`, môi trường CI và Docker cục bộ bắt buộc sử dụng image **`pgvector/pgvector:pg18`** (thay vì `postgres:18` thuần):
    ```bash
    docker run --name teamnexus-pg -e POSTGRES_PASSWORD=postgres -p 5432:5432 -d pgvector/pgvector:pg18
    ```
  - `DatabaseFixture` tự động kiểm tra sự tồn tại của extension `vector` trước khi chạy migration để đưa ra thông báo hướng dẫn rõ ràng nếu môi trường cục bộ cài thiếu extension.

---

## 5. Nguyên tắc Chung khi Phát triển

1. **Ưu tiên sự đơn giản & Rõ ràng:** Tránh thiết kế thừa (*over-architecture*); tận dụng tối đa các thành phần chuẩn của .NET (`BackgroundService`, `SignalR`, `Identity`, `DataProtection`) trước khi cân nhắc thêm hạ tầng phụ trợ bên ngoài.
2. **Tuân thủ tuyệt đối Lớp Giải trình AI:** Không một tính năng AI nào được phép tự ý ghi đè dữ liệu nghiệp vụ mà không đi qua quy trình phê duyệt và lưu ảnh chụp hoàn tác của `AiActionService`.
3. **Kiểm soát chặt chẽ Chi phí & Bảo mật AI:** Luôn đặt ngưỡng giới hạn ký tự/token đầu vào trước khi gọi nhà cung cấp AI, tóm tắt dữ liệu nhật ký trước khi phân tích, và lọc bỏ mọi định danh nhạy cảm (UUID, email, khóa bí mật).
4. **Hợp đồng API Chỉ ghi nối (Append-Only DTOs):** Không đổi tên, xóa hoặc đảo thứ tự các trường đã công bố trên các DTO phản hồi để bảo toàn tính tương thích ngược với bộ kiểm thử và ứng dụng di động Flutter.
