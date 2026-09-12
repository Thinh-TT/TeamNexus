# Bàn giao §2b — Test Frontend bổ sung + §3 (3 vá mã nguồn) — Giai đoạn 8

> **Người nhận:** Antigravity (agent/đội thực thi frontend).
> **Người giao:** phiên làm backend §2 (**đã xong và verify: 170/170 test PASS** trên PostgreSQL 18 thật).
> **Tham chiếu bắt buộc:** `Project-Documents/tasks/phase-8-completion-test-deploy.md` §2b, §3, §6.2 · §0.2 (D7/D8/D9) ·
> `Project-Documents/03-roadmap.md` (Giai đoạn 8) · `Project-Documents/report/phase-8-completion-test-deploy-report.md` (sẽ tạo ở §9).
>
> **Phạm vi:** CHỈ `frontend/` + list đóng 3 file backend ở §3.1. **Không** sửa `Program.cs`, **không** sinh migration,
> **không** đổi hợp đồng REST/DTO, **không** thêm route.
>
> **Thứ tự thi hành bắt buộc:** **§2b trước, §3 sau** — vì §2b test đúng hai hàm mà §3.2/§3.3 tạo ra.
> Cụ thể: (1) tạo `hubUrl.ts` + `reconnectPolicy.ts` **kèm** test; (2) nối vào `useBoardHub` + `BoardView` **kèm** test;
> (3) chạy full DoD ở §5 của note này.
>
> ⚠️ **§3.1 (vá cookie backend) ĐÃ ĐƯỢC LÀM XONG ở phiên backend** — xem §3.1 dưới đây để biết chính xác đã đổi gì.
> **Bạn KHÔNG cần làm lại**, và **không được sửa** các file đó; chỉ cần chạy lại `dotnet test` ở bước DoD để xác nhận vẫn **172/172**.

---

## 1. Trạng thái bàn giao & baseline

| Mục | Trạng thái |
|---|---|
| §2 Test backend (172 test, fixture PostgreSQL thật) | ✅ **XONG** — `dotnet test` = **172 passed / 0 failed / 0 skipped**; `dotnet build TeamNexus.sln` = **0 warning / 0 error**; `migrations list` = **6** (không đổi schema) |
| **§2b Test frontend bổ sung** | ✅ **XONG** — 37 test files / 206 tests PASS (+19 test mới), `lint` 0/0, `tsc -b` exit 0, `build` OK |
| **§3.1 Vá cookie backend (cross-site)** | ✅ **XONG ở phiên backend** — `AuthOptions` + `TokenCookieService` + antiforgery + `appsettings.json`; 2 test mới chứng minh cờ `SameSite` đi theo cấu hình. **Không sửa lại.** |
| **§3.2 + §3.3 (frontend)** | ✅ **XONG** — `hubUrl.ts` (cross-site base), `reconnectPolicy.ts` (infinite retry + cold-start timer), `useBoardHub.ts`, `BoardView.tsx` |
| §4 CI/CD · §5 deploy · §6.2 UX cold-start · §7 rà soát UI/UX | ⬜ Sau khi §2b xong |

**Baseline frontend ĐÃ ĐO LẠI (2026-09, trên nhánh `feat/phase8-completion-test-deploy`):**

```
npm run lint   → 0 warning / 0 error
npx tsc -b     → exit 0
npm run build  → OK
npm test       → Test Files 35 passed (35) · Tests 187 passed (187)
```

> ⚠️ **Điều kiện "xong" của §2b: `npm test` phải ra > 187 test** (số cụ thể bên dưới dự kiến **+12 → 199**), và
> `lint` = 0/0, `tsc -b` = exit 0, `build` = OK. Đây là DoD cứng, không phải "cố gắng".

**Không được làm (để không phá thứ đã verify):**

- ❌ Không đổi `httpClient.ts` (CSRF + refresh rotation đã verify 170 test backend phía sau nó).
- ❌ Không đổi `features/reporting/utils/reportDownload.ts` (pattern tải blob đã verify).
- ❌ Không thêm thư viện UI/tiện ích mới — `@microsoft/signalr` đã có sẵn.
- ❌ Không đổi shape của `HubConnectionStatus` (`'connected' | 'connecting' | 'reconnecting' | 'disconnected'`) — state này đã có UI + test.
- ❌ Không tự đặt header CSRF (đã tự động) và không dùng `axios` trong 2 hàm thuần (chúng phải thuần, không I/O).

---

## 2. §2b — Việc phải làm

### 2.1 Tạo `frontend/src/features/board/utils/hubUrl.ts`

**Vì sao (bằng chứng trong repo):** `useBoardHub.ts` dòng 50 đang hard-code `.withUrl('/hubs/board')`. Ở production FE (Vercel) và API
(Render) khác origin, URL tương đối sẽ khiến WebSocket gọi vào **chính Vercel** ⇒ real-time chết. `httpClient.ts` đã đọc
`VITE_API_BASE_URL` nhưng hook SignalR thì không ⇒ đây là lỗ (Phase 8 §0.1 **B14**, quyết định **D8**).

```ts
/**
 * Ghép đường dẫn SignalR hub với API base.
 * - base rỗng/undefined/relative ('/api')  ⇒ giữ đường dẫn tương đối '/hubs/board' (Vite dev proxy, hành vi cũ).
 * - base tuyệt đối ('https://api.x.com/api') ⇒ 'https://api.x.com/hubs/board' (bỏ path của base).
 * - base có dấu '/' cuối ⇒ không sinh '//'.
 * Hàm THUẦN: không đọc import.meta.env, không I/O ⇒ truyền base vào từ chỗ gọi.
 */
export function resolveHubUrl(apiBaseUrl?: string, hubPath = '/hubs/board'): string
```

**Quy tắc chốt (đúng 5 ca test ở §2.3):**

| Input `apiBaseUrl` | Output |
|---|---|
| `undefined` | `/hubs/board` |
| `''` | `/hubs/board` |
| `'/api'` (bắt đầu bằng `/`) | `/hubs/board` |
| `'https://api.example.com/api'` | `https://api.example.com/hubs/board` |
| `'https://api.example.com/api/'` | `https://api.example.com/hubs/board` |
| `'https://api.example.com'` | `https://api.example.com/hubs/board` |
| chuỗi rác (`':::'`) | xử lý **xác định**, không ném exception (fallback về `hubPath`) |

### 2.2 Tạo `frontend/src/features/board/utils/reconnectPolicy.ts`

**Vì sao (bằng chứng):** `useBoardHub.ts` dòng 53 đang dùng `withAutomaticReconnect([0, 2000, 5000, 10000, 30000])` — **5 lần thử
trong ~47 giây rồi bỏ cuộc vĩnh viễn** (`onclose` ⇒ `disconnected`, không bao giờ thử lại). Bản free-tier của Render ngủ sau
15 phút và cần **~60 giây** để thức ⇒ hệ thống hiện tại **thua cold-start một cách xác định** (Phase 8 §0.1 **B7**, quyết định **D9**).

```ts
/** Backoff có TRẦN, không bao giờ bỏ cuộc: sau khi đạt trần thì giữ nguyên trần mãi. */
export const RECONNECT_MAX_DELAY_MS = 30_000

/** Số ms chờ trước lần thử lại thứ `previousRetryCount` (0-based). */
export function nextRetryDelay(previousRetryCount: number): number

/** True khi đã thử đủ nhiều để coi là "đang chờ máy chủ thức dậy" (dùng cho thông điệp UX). */
export function isColdStartLikely(previousRetryCount: number): boolean
```

**Bảng chốt:**

| `previousRetryCount` | `nextRetryDelay` | Ghi chú |
|---|---|---|
| `0` | `0` | thử lại ngay |
| `1` | `2000` | |
| `2` | `5000` | |
| `3` | `10000` | |
| `4` | `30000` | chạm trần |
| `5`, `6`, `100` | **`30000`** | **giữ trần mãi — TUYỆT ĐỐI không trả `null`/`undefined`** |
| `-1`, `NaN`, `Number.MAX_SAFE_INTEGER` | giá trị hợp lệ (≥ 0, hữu hạn) | không được ném, không trả `NaN` |

`isColdStartLikely(n)` ⇒ `true` từ `n >= 3` (đã thử lại ≥ 3 lần ≈ quá 7 giây ⇒ gần như chắc chắn là spin-up của free-tier).

### 2.3 Test phải viết (đây chính là §2b — checklist gốc của task doc)

**`utils/__tests__/hubUrl.test.ts`** — tối thiểu 7 case:
- [ ] `undefined` ⇒ `/hubs/board`
- [ ] `''` ⇒ `/hubs/board`
- [ ] `'/api'` ⇒ `/hubs/board`
- [ ] `'https://api.example.com/api'` ⇒ `https://api.example.com/hubs/board`
- [ ] `'https://api.example.com/api/'` ⇒ `https://api.example.com/hubs/board` (**không** có `//`)
- [ ] `'https://api.example.com'` ⇒ `https://api.example.com/hubs/board`
- [ ] chuỗi không hợp lệ ⇒ trả `hubPath`, **không** ném

**`utils/__tests__/reconnectPolicy.test.ts`** — tối thiểu 4 case:
- [ ] dãy `0,1,2,3,4` ⇒ `0, 2000, 5000, 10000, 30000`
- [ ] `5, 6, 100` ⇒ `30000` (không bao giờ `null`/bỏ cuộc)
- [ ] đầu vào bất thường (`-1`, `NaN`, `Number.MAX_SAFE_INTEGER`) ⇒ số hữu hạn, `>= 0`
- [ ] `isColdStartLikely(0|1|2)` = `false`; `isColdStartLikely(3|10)` = `true`

**`hooks/__tests__/useBoardHub.test.ts`** (sửa file có sẵn, giữ test cũ xanh):
- [ ] `withAutomaticReconnect` được gọi với **object** `{ nextRetryDelayInMilliseconds }`, **không** còn mảng
- [ ] URL dùng `resolveHubUrl(...)` — mock `import.meta.env.VITE_API_BASE_URL` và assert URL kết nối
- [ ] `onreconnected` ⇒ gọi `refetch` **đúng một lần** (spy) **và** `invoke('JoinBoard', boardId)`
- [ ] `reconnect()` thủ công ⇒ gọi `start()` lại
- [ ] `onclose` ⇒ `connectionStatus = 'disconnected'`

**`components/__tests__/BoardView.test.tsx`** (sửa file có sẵn):
- [ ] trạng thái `disconnected` ⇒ render nút **"Kết nối lại"**; bấm ⇒ gọi `reconnect`
- [ ] trạng thái `reconnecting` + đã vượt mốc cold-start ⇒ hiện thông điệp cold-start (dùng `vi.useFakeTimers()`)
- [ ] giữ nguyên 4 nhánh render cũ (không phá test đang có)

### 2.4 §3.2 + §3.3 — nối 2 hàm vào code thật

**`hooks/useBoardHub.ts`:**

```ts
// chữ ký mới (tham số 2 TUỲ CHỌN để không phá 2 chỗ gọi hiện có)
export const useBoardHub = (boardId: string | undefined, refetch?: () => void | Promise<void>) => { … }
```

- [ ] Thay `.withUrl('/hubs/board', { withCredentials: true })` bằng
      `.withUrl(resolveHubUrl(import.meta.env.VITE_API_BASE_URL), { withCredentials: true })`.
- [ ] Thay `.withAutomaticReconnect([...])` bằng
      `.withAutomaticReconnect({ nextRetryDelayInMilliseconds: ({ previousRetryCount }) => nextRetryDelay(previousRetryCount) })`.
- [ ] Thêm `serverTimeoutInMilliseconds` / `keepAliveIntervalInMilliseconds` **dưới dạng hằng số có tên** ở đầu file
      (gợi ý: `keepAliveIntervalInMilliseconds: 15_000`, `serverTimeoutInMilliseconds: 60_000`). **Không** magic number rải rác.
- [ ] Trong `onreconnected`: giữ `setConnectionStatus('connected')` + `invoke('JoinBoard', boardId)` **rồi** gọi `await refetch?.()`
      (bọc `try/catch`, log có ngữ cảnh — không được để lỗi refetch làm sập callback).
- [ ] Expose thêm `reconnect()` trong object trả về: nếu state là `Disconnected` (không phải `Connecting`/`Connected`) thì `start()` lại
      rồi `JoinBoard`. Guard để bấm nhanh nhiều lần không tạo nhiều kết nối song song.
- [ ] **Không** đổi tên event đang `connection.on(...)` (backend Phase 2–7 đã verify) và **không** đổi kiến trúc 1-hub-1-group.

**`hooks/useBoard.ts`:**

- [ ] `useBoardHub(boardId)` ⇒ `useBoardHub(boardId, fetchBoardData)`. `fetchBoardData` **đã** được expose qua `refetch` (dòng ~341)
      nên **không** cần thêm API mới.

**`components/BoardView.tsx` — trong `renderConnectionStatus` (dòng 66–106):**

- [ ] Nhánh `disconnected`: thêm nút **"Kết nối lại"** (Ant Design `Button size="small"`, không thêm `Tag` mới) gọi `reconnect()`.
- [ ] Nhánh `reconnecting`: giữ tooltip hiện có cho ~5 giây đầu; sau mốc cold-start đổi thông điệp thành:
      *"Máy chủ đang khởi động lại (bản miễn phí tự ngủ khi rảnh). Quá trình có thể mất khoảng 1 phút — dữ liệu của bạn vẫn an toàn."*
      (dùng `isColdStartLikely` + timer, không hard-code chuỗi ở nhiều nơi).
- [ ] `renderConnectionStatus` hiện là hàm `const` ngoài component ⇒ **truyền `reconnect` vào qua tham số** thay vì biến module,
      để giữ được tính "thuần" khi test.

**`frontend/.env.example`:**

- [ ] Bổ sung ví dụ `VITE_API_BASE_URL` cho production (đang chỉ có comment), kèm ghi chú **biến này là build-time ⇒ sửa xong phải redeploy**.

---

## 3. §3.1 — Vá cookie backend (cross-site) — ✅ **ĐÃ XONG Ở PHIÊN BACKEND**

> **Đây là blocker số 1 khi deploy tách domain.** FE (Vercel) và API (Render) là **khác site**; cookie `SameSite=Lax` sẽ **không**
> được gửi trên XHR cross-site ⇒ đăng nhập xong vẫn bị coi là chưa đăng nhập (vòng lặp login). Bằng chứng: `TokenCookieService.cs`
> và `DependencyInjection.cs` đều hard-code `SameSiteMode.Lax` (Phase 8 §0.1 **B6**, quyết định **D7**).
>
> **Trạng thái: đã thi hành + verify (172/172 test xanh). Đừng sửa lại.** Mục này giữ lại để bạn biết **hợp đồng đã đổi gì** — vì
> nó ảnh hưởng tới §5 (deploy) và tới cách bạn debug nếu real-time/CSRF có vấn đề trên production.

**Những gì đã thay đổi (backend, ngoài phạm vi của bạn):**

| File | Thay đổi |
|---|---|
| `Options/AuthOptions.cs` **(MỚI)** | Section `Auth`, một property `CookieSameSite` (mặc định `Lax`), có `Validate` + `ValidateOnStart` |
| `Services/TokenCookieService.cs` | 4 chỗ hard-code `SameSiteMode.Lax` ⇒ đọc `_authOptions.CookieSameSite` |
| `DependencyInjection.cs` | Bind `AuthOptions`; **antiforgery cookie dùng cùng giá trị** (quên chỗ này ⇒ 403 CSRF trên production) |
| `TeamNexus.Api/appsettings.json` | `"Auth": { "CookieSameSite": "Lax" }` |
| `AuthApiTests.cs` | +2 test: `AuthCookies_UseLaxByDefault`, `AuthCookies_FollowAuthCookieSameSiteConfiguration` |

**Hệ quả cho §5 (deploy) — ghi nhớ khi làm runbook:** env trên Render là **`Auth__CookieSameSite = None`**
(không phải `Auth__CookieCrossSite` hay tên nào khác). Local **không cần** đổi gì: mặc định `Lax` giữ nguyên hành vi dev.

**Điều bạn cần làm ở phần này:** chỉ chạy `dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj` ở bước DoD (§4, mục 5)
để xác nhận vẫn **172 passed / 0 failed**. Nếu số này khác ⇒ báo lại, **đừng** tự sửa backend.

---

## 4. Bằng chứng cần nộp

| # | Bằng chứng | Cách đo |
|---|---|---|
| 1 | `npm run lint` | 0 warning / 0 error |
| 2 | `npx tsc -b` | exit 0 |
| 3 | `npm run build` | OK |
| 4 | `npm test` | **Test Files ≥ 35 · Tests > 187** (dự kiến ~199) — **dán số thật** |
| 5 | `dotnet test tests/TeamNexus.Api.Tests/TeamNexus.Api.Tests.csproj` | vẫn **172 passed / 0 failed** (chứng minh §2b không hồi quy backend) |
| 6 | `dotnet build TeamNexus.sln -m:1 -nr:false` | **0 warning / 0 error** |
| 7 | `dotnet ef migrations list …` | vẫn **6** — §2b/§3 **không** sinh migration |
| 8 | Ảnh/ghi chú 1 lần đăng nhập local thật | chứng minh cookie vá không phá dev |

**Phải ghi vào báo cáo khi xong:** số test frontend mới (trước/sau), số test backend vẫn 170, và **bất kỳ phát hiện nào trái với note này**
(ví dụ `withAutomaticReconnect` cần chữ ký khác, hoặc `BoardView` không truyền được `reconnect` như mô tả) — ghi thẳng vào
`Project-Documents/report/phase-8-completion-test-deploy-report.md`, **đừng** tự ý đổi hợp đồng ở §2b/§3.

---

## 5. Sau khi §2b + §3 xong (việc của phiên này, không phải của bạn)

1. Cập nhật `tasks/phase-8-completion-test-deploy.md`: tick §2b, §3.1, §3.2, §3.3 + điền số test thật.
2. Cập nhật `README.md` mục Giai đoạn 8.
3. Làm tiếp **§4 (CI/CD)** → **§5 (deploy)** → **§6 (UX cold-start đã có sẵn từ §2b)** → **§7 (rà soát UI/UX)** → **§9 (báo cáo)**.

> ⚠️ **§6.2 (UX cold-start) về cơ bản đã được làm luôn trong §2b** (retry vô hạn + nút "Kết nối lại" + refetch + thông điệp tiếng Việt).
> Phần còn lại của §6 chỉ là **đo cold-start thật trên Render** (§6.5) — cần deploy trước.
