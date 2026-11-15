# TeamNexus Frontend — Ứng dụng Web SPA (Luminous Enterprise v4.2)

Giao diện Web của **TeamNexus** được xây dựng trên nền tảng **React 19.2**, **TypeScript 6.0**, **Vite 8.2** và **Ant Design 6.6** theo ngôn ngữ thiết kế **Luminous Enterprise v4.2** (tối ưu hóa hiển thị dấu tiếng Việt với phông chữ `Be Vietnam Pro` và huy hiệu kỹ thuật với `JetBrains Mono`).

---

## 1. Cấu trúc Thư mục (`src/`)

```text
frontend/src/
├── app/                        # Cấu hình định tuyến (React Router) và khung ứng dụng gốc
├── shared/                     # Hạ tầng dùng chung toàn ứng dụng
│   ├── api/                    # httpClient (Axios + tự động đính kèm X-XSRF-TOKEN & làm mới phiên 401)
│   ├── components/             # AppHeader, WorkspaceShell (Sidebar trái), NotificationBell, MarkdownView
│   └── utils/                  # Tiện ích định dạng thời gian, màu sắc và khử định danh (redactIdentifiers)
└── features/                   # 11 phân hệ tính năng theo miền nghiệp vụ (Feature-Sliced)
    ├── auth/                   # Trang đăng nhập OAuth (Google/GitHub), AuthGuard & quản lý phiên
    ├── landing/                # Trang giới thiệu sản phẩm cho người dùng chưa đăng nhập
    ├── workspace/              # Khởi tạo, chuyển đổi, cài đặt Workspace & lịch sử hoạt động
    ├── board/                  # Bảng Kanban thời gian thực (@dnd-kit + SignalR), Cột, Thẻ (TN-123), Lịch biểu
    ├── ai/                     # Trung tâm Giải trình AI (Approve/Reject/Undo), Task Chat & Board Agent Chat (SSE),
    │                           # Auto-Triage, Smart Assignee, Personas & Tích hợp ngoài (Slack/Discord/GitHub)
    ├── knowledge/              # Quản trị Cơ sở Tri thức Ngữ nghĩa RAG (pgvector documents)
    ├── dashboard/              # Tổng quan Workspace, Chỉ số Sức khỏe Dự án (0–100) & Thẻ của tôi
    ├── reporting/              # Biểu đồ Burndown/Velocity (Progress Series) & Xuất báo cáo PDF/Excel
    ├── search/                 # Tìm kiếm đa tiêu chí xuyên bảng (Keyset Pagination) & Command Palette (Ctrl+K)
    ├── members/                # Quản lý thành viên, phân quyền, hồ sơ kỹ năng (Skills/WIP) & lời mời Email
    └── profile/                # Hồ sơ cá nhân & tùy chọn nhận bản tin Daily Digest
```

---

## 2. Quy chuẩn Kỹ thuật & Giao diện Bất biến

1. **Hệ thống Thiết kế (Luminous Enterprise v4.2):**
   - Sử dụng các component **Ant Design v6 (`antd`)** kết hợp biến CSS `--tn-*` trong `src/index.css` và cấu hình `luminousTheme` trong `src/App.tsx`.
   - Không sử dụng Tailwind CSS và không dùng `dangerouslySetInnerHTML` (mô tả thẻ và tin nhắn AI được kết xuất qua trình phân tích Markdown an toàn).
2. **Truyền phát SSE & Đồng bộ SignalR:**
   - Các luồng hội thoại AI (`/ai-chat/stream`, `/agent-chat/stream`) sử dụng `fetch` + `ReadableStream` + `TextDecoder` kèm `getXsrfToken()` (`credentials: 'include'`), luôn kiểm tra `res.ok` trước khi đọc luồng.
   - Đồng bộ bảng Kanban thời gian thực qua `@microsoft/signalr` kết nối tới `/hubs/board` với cơ chế tự động kết nối lại (auto-reconnect).

---

## 3. Các Lệnh Phát triển & Kiểm định Chất lượng

Chạy các lệnh sau từ thư mục `frontend/`:

```bash
# 1. Khởi chạy máy chủ phát triển cục bộ (http://localhost:5173):
npm run dev

# 2. Kiểm tra quy tắc mã nguồn tĩnh (Oxlint — cổng chất lượng: 0 warnings, 0 errors):
npm run lint

# 3. Kiểm tra kiểu tĩnh TypeScript (cổng chất lượng: exit code 0):
npx tsc -b

# 4. Chạy toàn bộ 799 bài kiểm thử đơn vị & giao diện (Vitest + Testing Library):
npm test

# 5. Đóng gói bản dựng Production:
npm run build
```
