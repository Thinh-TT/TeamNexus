# TeamNexus Web (frontend)

React + TypeScript + Vite + Ant Design — web client của TeamNexus.

## Cấu trúc

```
src/
  app/            # Router, providers (React Query, ConfigProvider), global state – Phase 1 §4
  features/auth/  # Auth: trang login, useAuth, ProtectedRoute, auth store – Phase 1 §4
  shared/
    api/          # Axios instance (withCredentials) – Phase 1 §1
```

## Dev

```bash
npm install
npm run dev        # http://localhost:5173 — proxy /api → http://localhost:5000
npm run build      # tsc -b && vite build
npm run lint       # oxlint
```

Xem `.env.example` để đổi địa chỉ backend (VITE_DEV_API_TARGET) / base URL (VITE_API_BASE_URL).
