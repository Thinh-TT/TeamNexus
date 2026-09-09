# HTTP client

Base Axios instance for all API calls.

- `baseURL`: `import.meta.env.VITE_API_BASE_URL` (default `/api`). In dev, `/api` is
  proxied by the Vite dev server to the backend (see `vite.config.ts`).
- `withCredentials: true` is **required** for cookie-based auth (Phase 1 §3/§4):
  the browser must send/receive the HttpOnly `access_token` / `refresh_token`
  cookies with every request.
