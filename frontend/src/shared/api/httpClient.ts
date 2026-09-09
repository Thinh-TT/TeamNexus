import axios from 'axios'

/**
 * Base Axios instance shared across all features.
 *
 * - Base URL defaults to `/api` (Vite dev proxy → backend). Override with
 *   `VITE_API_BASE_URL` if the API is served from another origin in dev.
 * - `withCredentials` is mandatory for cookie-based auth: access/refresh tokens
 *   travel in HttpOnly cookies, so the browser must attach them on every request.
 */
export const httpClient = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  withCredentials: true,
  headers: { 'Content-Type': 'application/json' },
})
