# Báo cáo Kiểm thử Giai đoạn 6 — Báo cáo & Xuất dữ liệu

> **Phạm vi:** backend Giai đoạn 6 (module `TeamNexus.Modules.Reporting`) — aggregation tiến độ/hiệu suất,
> xuất PDF (QuestPDF) + Excel (ClosedXML) on-demand, 3 endpoint HTTP. **Không** gồm frontend (§6 — bàn giao riêng).
>
> **Kết luận:** **264/264 check PASS** (B 76 · C 43 · D 34 · E 37 · F 74) trong 5 đợt harness độc lập ngoài workspace.
> Phát hiện & sửa **3 bug thật** trong sản phẩm + **1 lỗi hạ tầng** (làm hỏng `dotnet ef`).

---

## 1. Môi trường & phương pháp

| Thành phần | Chi tiết |
|---|---|
| Runtime | .NET SDK 10.0.201 (`net10.0`), ASP.NET Core + Kestrel |
| Database | PostgreSQL local, DB `TeamNexus` (connection string trong User Secrets) |
| Xác thực harness | JWT HS256 tự ký (`Jwt:SigningKey` trong User Secrets), gửi trong cookie `access_token` (`Issuer=TeamNexus`, `Audience=TeamNexus.Web`) |
| API | Boot thật bằng `dotnet run --project src/TeamNexus.Api --no-build --urls http://127.0.0.1:<cổng tạm>` (3 instance: mặc định / `Reports__Enabled=false` / `MaxExportRows=3`) |
| Đọc lại file | `UglyToad.PdfPig 0.1.16` (PDF) + `ClosedXML 0.105.1` (XLSX) — chỉ dùng trong harness (test-only) |
| Fixture | Seed bằng **raw SQL** (tránh `SaveChanges` tự stamp `created_at`), hard-delete trong `finally`, DB về baseline |
| Quy ước | Harness nằm ngoài workspace (`%TEMP%`), xoá sau khi chạy, **không** commit; AI **không** được gọi ⇒ không tốn token |

**Vì sao phải đọc lại file thay vì chỉ kiểm byte signature:** yêu cầu roadmap là PDF/Excel "đọc được/mở được" — chỉ
kiểm `%PDF-`/`PK` không chứng minh điều đó. Harness mở lại PDF bằng PdfPig (đọc được text tiếng Việt có dấu) và mở lại
XLSX bằng ClosedXML (đủ 5 sheet + số liệu khớp aggregator).

---

## 2. Tổng hợp kết quả

| Đợt | Nhóm | Nội dung | Check | Kết quả |
|---|---|---|---|---|
| §1 | **B** | Aggregation thuần: thresholds/range/clamp, overdue biên, `completed_at`/`is_done`, on-time, cap, sort, tất định, 1000 task | 76 | **76/76 PASS** |
| §4 | **C** | Renderer: PDF (magic, EOF, khổ trang, text tiếng Việt, cảnh báo cap) + Excel (5 sheet, autofilter, freeze pane, data bar, không `#REF!`) | 43 | **43/43 PASS** |
| §5 | **D** | HTTP 3 endpoint: shape DTO, scope, strict ISO-8601, export 2 định dạng, quyền/auth, 503, cap, OpenAPI tag | 34 | **34/34 PASS** |
| §3 | **E** | Loader read-only: quyền 403/404/503, số liệu khớp fixture, 8 truy vấn/request, bất biến 5 bảng, tất định, log | 37 | **37/37 PASS** |
| §7.1 | **A–F** | Chốt tổng hợp: schema/kiến trúc, hàm thuần, renderer, HTTP, bất biến, regression | 74 | **74/74 PASS** |

### Ma trận §7.1 (chốt cuối)

| Nhóm | Nội dung | Kết quả |
|---|---|---|
| **A** | 5 migration (không phát sinh), model snapshot zero-diff, Board/Ai/Persistence không bị sửa, csproj không thêm project reference, Board không tham chiếu Reporting, project có trong `.sln`, thứ tự đăng ký DI đúng, csproj pin đúng 2 package, module không có đường ghi DB | **9/9** |
| **B** | thresholds/`BuildRange` (mặc định 30d, `to` tương lai → now, clamp 365, `Days=ceil`, `from>to` không ném, clamp 0/âm ⇒ 1), `TryGetPdfPageSize` strict, `ReportFileName` (slug ASCII ≤ 40, tên file), `ReportLabels`, `ReportExportFormats`, aggregator (progress/performance/health/metricDefinitions, tất định, cap, rỗng không ném, 1000 task **1ms**) | **20/20** |
| **C** | PDF: `%PDF-`+`%%EOF`+> 8KB, PdfPig mở được, A4 595×842 / A3 842×1191 / `A5`→A4, text tiếng Việt có dấu đọc đúng, cột định nghĩa, cảnh báo cắt dòng; Excel: ZIP, mở lại đủ 5 sheet, số liệu khớp summary, autofilter+freeze+data bar, không cell lỗi, rỗng vẫn 5 sheet, `null`⇒`—`; `ReportExportService` Rows/metadata | **14/14** |
| **D** | summary Manager 200 (13 nhóm, camelCase), scope board, board lạ/đã xoá ⇒ 404, workspace lạ ⇒ 404, `from=bogus` ⇒ 400 `{error}`, `from>to` ⇒ 400, clamp 365, mặc định 30d, export PDF/Excel qua HTTP (contentType + disposition + content-length + bytes + mở lại được), 4 biến thể `format` sai ⇒ 400, boards + sort, Member 403 × 3, ngoài workspace 404, không cookie 401 × 3, `boardId=abc` ⇒ 400 ProblemDetails, `Enabled=false` ⇒ 503 × 3, hạ cap ⇒ header `true`, OpenAPI tag `Reports` + 3 route | **22/22** |
| **E** | 1 summary + 2 export ⇒ 5 bảng không đổi row; không tạo file trên disk; không sinh artifact lạ trong workspace | **3/3** |
| **F** | `dotnet build` 0 warning/0 error, health 200, log `Reporting module:` **đúng 1 dòng** (không secret), `Observer__Enabled=false`+`Reports__Enabled=false` boot sạch, `git diff` Board/Ai/Persistence rỗng, `Program.cs` chỉ +15 dòng | **6/6** |

### Số liệu đo thật

| Chỉ số | Giá trị |
|---|---|
| Migration sau Giai đoạn 6 | **5** (không phát sinh) |
| Model snapshot diff | **zero-diff** |
| `dotnet build TeamNexus.sln` | **0 warning / 0 error** |
| Aggregation 1000 task | **1 ms** |
| PDF (báo cáo 6 task) | **517 ms** nguội / **13 ms** sau warm-up · 61.925 B · **2 trang** |
| Excel (cùng báo cáo) | **860 ms** nguội / **21 ms** sau warm-up · 15.942 B · **5 sheet** |
| Bảng lớn (10 board + 50 người + 30 action) | PDF **36 ms** (4 trang) · Excel **68 ms** |
| Export qua HTTP | 61.868 B, `Content-Length` khớp, **không** redirect |
| Truy vấn / request summary | **8** (không N+1) |
| Log khởi động | đúng **1 dòng**, không chứa secret |
| DB sau harness | **baseline 0 row** fixture |

---

## 3. Bug thật đã bắt được & sửa

### 3.1 `activeUsers` đếm trùng người (nhóm B/§3)
`LoadActivityAsync` ban đầu cộng `count(distinct user_id)` **theo từng action**. Một người xuất hiện ở nhiều loại
hành động bị đếm nhiều lần ⇒ fixture cho kết quả **5** thay vì **2**. Đã sửa: `activeUsers` = số user khác nhau
**trong cả cửa sổ**, tính bằng một truy vấn `Distinct().Count()` riêng (không phụ thuộc khả năng dịch `Distinct`
trong `GroupBy` của provider).

### 3.2 Hai mốc thời gian khác nhau trong cùng một request (§3)
`Period.To` lấy `UtcNow` trong `ValidateRange`, còn `GeneratedAt` lấy `UtcNow` lần nữa trong `LoadSnapshotAsync`
⇒ báo cáo tự mâu thuẫn (lệch vài ms). Đã sửa: **một** `DateTimeOffset.UtcNow` cho cả request, dùng chung cho
`Period.To`, quy tắc quá hạn và `GeneratedAt` ⇒ hiện verify được `GeneratedAt == Period.To`.

### 3.3 Header `X-Report-Period` làm **mọi request export trả 500** (§5) — nghiêm trọng nhất
Nhãn kỳ của domain dùng **en dash `–` (U+2013)** cho bản in PDF/Excel; endpoint đưa nguyên nhãn đó vào header HTTP
⇒ Kestrel ném `Invalid non-ASCII or control character in header` ⇒ **cả 2 định dạng export đều 500** (harness phát
hiện qua D14/D16 fail với `InternalServerError`; body 500 ghi rõ `0x2013`). Đã sửa bằng `Ascii(...)`: en/em dash →
`-`, bỏ combining marks, ký tự ngoài ASCII in được → `-`. Bản có dấu vẫn nằm trong nội dung file.

### 3.4 Lỗi hạ tầng: đăng ký DI làm hỏng `dotnet ef` (§3)
Sau khi `AddReportingModule` đăng ký `IReportService` (phụ thuộc `IReportExportService` chưa tồn tại), host
design-time của `dotnet ef` (bật `ValidateOnBuild`) không validate được service graph ⇒ mọi lệnh `dotnet ef`
(**kể cả `migrations list`**) đổ vỡ. Đã thêm `NoReportExportService` (no-op throw `NotSupportedException`) làm
fallback **có điều kiện**; §4 sau đó bỏ hẳn footgun bằng cách để `AddReportingModule` tự đăng ký implementation
thật trước khi xét fallback (D21) ⇒ không còn phụ thuộc thứ tự đăng ký.

> **Bài học ghi lại:** 4 lỗi trên đều **không** lộ ra qua build — chỉ lộ khi (a) chạy harness thuần với fixture
> nhiều người/nhiều action, (b) gọi HTTP thật để header đi qua Kestrel, (c) chạy `dotnet ef` sau khi thêm DI.

---

## 4. Known gaps & giới hạn đã biết

1. **`?from=a&from=b`** (trùng tên query) ⇒ framework bind giá trị cuối; không xử lý riêng (chấp nhận).
2. **`?boardId=abc`** ⇒ **400 ProblemDetails** của framework (không có `{ error }`) — khác các lỗi domain khác.
   UI phải xử lý theo status, không đọc `.error`. Đã ghi trong contract §6.
3. **Render lần đầu ~0,5–0,9 s** (QuestPDF/ClosedXML warm-up + JIT). Chấp nhận được cho "generate on-demand";
   nếu cần, có thể warm-up ở lần boot hoặc giảm `MaxExportRows` trên free-tier.
4. **QuestPDF Community license** là self-declaration, set trong `ReportPdfRenderer` — không có key/activation.
5. **Frontend chưa có** (§6): chưa có `src/features/reporting/`, chưa có route `/workspaces/:id/reports`, chưa có
   nút "Báo cáo" ở `BoardListPage`/`BoardView`. Harness đã verify **backend**; UI cần verify riêng (§7.2).
6. **Chưa có project xUnit** (để Giai đoạn 7 — D17): các harness ngoài workspace là **điểm tựa** để chuyển thành
   test xUnit vì `ReportAggregator`/`ReportThresholds`/`ReportFileName`/`ReportLabels` đều là hàm `public static` thuần.
7. **Không có test cho PDF engine của QuestPDF** (không verify pixel-level); chỉ verify cấu trúc + text extract +
   tên file + số trang.

---

## 5. Điều kiện hoàn thành Giai đoạn 6 (map `03-roadmap.md`)

| Tiêu chí roadmap | Trạng thái | Bằng chứng |
|---|---|---|
| Tổng hợp được dữ liệu **tiến độ/hiệu suất** board | ✅ backend | §1 B13–B19 + §3 P12–P20 + §5 D1–D4 (progress/performance/byBoard/byAssignee/activity/health + `metricDefinitions`) |
| Xuất báo cáo ra **PDF** (QuestPDF) đúng định dạng, **đọc được** | ✅ backend | §4 + §7.1 C1–C6 (`%PDF-`+`%%EOF`, PdfPig mở lại, tiếng Việt có dấu, khổ A4/A3/Letter) |
| Xuất báo cáo ra **Excel** (ClosedXML) đúng định dạng, **mở được** | ✅ backend | §4 + §7.1 C7–C14 (ClosedXML round-trip, 5 sheet, data bar, không `#REF!`) |
| File **generate on-demand, không lưu trữ vĩnh viễn** | ✅ backend | §3 E + §7.1 E1–E3 (bytes RAM, `Results.File`, không file mới, 5 bảng không đổi) |
| UI gọi API thật & bộ test frontend hoàn tất | ⏳ **còn lại** | §6/§7.2 — bàn giao antigravity (xem mục "🔻 BÀN GIAO §6" và §7.2 trong file task) |

> 4 ô roadmap `03-roadmap.md` (dòng 67–70) **chưa tick** vì tiêu chí cuối còn phụ thuộc UI; tick khi §6 + §7.2 xong.

---

## 6. Phần UI — bổ sung sau §6

> **Chưa có.** Frontend (§6 + §7.2) được **bàn giao antigravity**; contract đầy đủ ở
> `Project-Documents/tasks/phase-6-reporting-export.md` mục "🔻 BÀN GIAO §6" và hướng dẫn verify ở §7.2 của file đó.
>
> **Baseline ghi trước khi bàn giao (2026-09-10):** `oxlint` **0 warn/0 err** · `tsc -b` **exit 0** ·
> `vitest run` **22 files / 122 tests PASS**.
>
> Sau khi antigravity xong, bổ sung vào đây: số test thật sau §7.2, ảnh chụp trang Báo cáo (panel số liệu + bộ lọc)
> và drawer xuất báo cáo (chọn PDF/Excel + trạng thái loading + lỗi), kèm kết quả `npm run build`.
> Lúc đó mới tick 4 ô Giai đoạn 6 trong `03-roadmap.md`.

---

## 7. Hướng dẫn tái lập

```powershell
# 1) Build + migration (không dùng --no-build cho dotnet ef)
dotnet build TeamNexus.sln
dotnet ef migrations list --project src/TeamNexus.Persistence --startup-project src/TeamNexus.Api   # phải = 5

# 2) Chạy app để thử tay
$env:Observer__Enabled = "false"            # tránh timer nền quét DB dev
dotnet run --project src/TeamNexus.Api      # http://localhost:5000  (Scalar: /scalar)
#   → GET /api/workspaces/{id}/reports/summary | /export?format=pdf|excel | /boards  (cần role Manager/Admin)

# 3) Harness backend (ngoài workspace, xoá sau khi chạy)
#    - console app net10.0 reference TeamNexus.Api/Microsoft.EntityFrameworkCore/ClosedXML/PdfPig
#    - đọc connection string + Jwt:SigningKey từ User Secrets, seed fixture raw SQL, hard-delete ở finally
#    - boot API bằng process thật 3 instance (mặc định / Enabled=false / MaxExportRows=3), JWT tự ký trong cookie
```

**Bẫy đã gặp (ghi lại để không mất thời gian):**
- `WebApplicationFactory<Program>` **không** dùng được từ harness ngoài solution: nó đi tìm `.sln` từ thư mục `bin`
  ⇒ `Solution root could not be located`. Giải pháp: boot API bằng process thật + poll `/api/health`.
- `Set-Content`/`Set-Location` của Windows PowerShell 5.1 ghi file theo codepage ANSI ⇒ **làm hỏng tiếng Việt**
  trong file `.cs`/`.sql`. Luôn dùng `UTF8Encoding($false)` khi ghi file có dấu.
- Fixture phải seed bằng **raw SQL**: `TeamNexusDbContext.SaveChanges` tự stamp `created_at`/`updated_at` cho mọi
  `IAuditableEntity` ⇒ seed qua EF làm mọi task trông "vừa tạo" và mọi metric theo cửa sổ sai.
