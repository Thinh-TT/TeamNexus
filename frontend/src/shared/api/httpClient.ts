import axios from 'axios'

function getCookie(name: string): string | null {
  if (typeof document === 'undefined') return null
  const value = `; ${document.cookie}`
  const parts = value.split(`; ${name}=`)
  if (parts.length === 2) {
    const cookieVal = parts.pop()?.split(';').shift()
    return cookieVal ? decodeURIComponent(cookieVal) : null
  }
  return null
}

let inMemoryXsrfToken: string | null = null

export function setXsrfToken(token: string | null) {
  inMemoryXsrfToken = token
}

export function getXsrfToken(): string | null {
  return inMemoryXsrfToken ?? getCookie('XSRF-TOKEN')
}

async function fetchXsrfToken(): Promise<string | null> {
  try {
    const res = await axios.get<{ token?: string }>(
      `${import.meta.env.VITE_API_BASE_URL ?? '/api'}/auth/antiforgery?json=true`,
      { withCredentials: true }
    )
    const token =
      res.data?.token ??
      (res.headers['x-xsrf-token'] as string | undefined) ??
      getCookie('XSRF-TOKEN')
    if (token) {
      inMemoryXsrfToken = token
    }
    return token ?? null
  } catch {
    return null
  }
}

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

// Request Interceptor: Attach X-XSRF-TOKEN for mutating operations
httpClient.interceptors.request.use(async (config) => {
  const method = config.method?.toUpperCase()
  if (method && ['POST', 'PUT', 'DELETE', 'PATCH'].includes(method)) {
    let xsrfToken = getXsrfToken()
    if (!xsrfToken && !config.url?.includes('/auth/antiforgery')) {
      xsrfToken = await fetchXsrfToken()
    }
    if (xsrfToken) {
      config.headers['X-XSRF-TOKEN'] = xsrfToken
    }
  }
  return config
})

// Response Interceptor: Handle 401 Unauthorized by attempting Refresh Token rotation
let isRefreshing = false
let failedQueue: Array<{
  resolve: (value?: unknown) => void
  reject: (reason?: unknown) => void
}> = []

const processQueue = (error: unknown) => {
  failedQueue.forEach((prom) => {
    if (error) {
      prom.reject(error)
    } else {
      prom.resolve()
    }
  })
  failedQueue = []
}

httpClient.interceptors.response.use(
  (response) => {
    const token =
      (response.data && typeof response.data === 'object' && 'token' in response.data
        ? (response.data as { token?: string }).token
        : undefined) ?? (response.headers['x-xsrf-token'] as string | undefined)
    if (token && typeof token === 'string') {
      inMemoryXsrfToken = token
    }
    return response
  },
  async (error) => {
    const originalRequest = error.config

    if (
      error.response?.status === 401 &&
      originalRequest &&
      !originalRequest._retry &&
      !originalRequest.url?.includes('/auth/refresh') &&
      !originalRequest.url?.includes('/auth/login')
    ) {
      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({ resolve, reject })
        })
          .then(() => httpClient(originalRequest))
          .catch((err) => Promise.reject(err))
      }

      originalRequest._retry = true
      isRefreshing = true

      try {
        await httpClient.post('/auth/refresh')
        processQueue(null)
        return httpClient(originalRequest)
      } catch (refreshError) {
        processQueue(refreshError)
        window.dispatchEvent(new CustomEvent('auth:unauthorized'))
        return Promise.reject(refreshError)
      } finally {
        isRefreshing = false
      }
    }

    if (
      error.response?.status === 403 &&
      typeof error.response?.data?.error === 'string' &&
      error.response.data.error.includes('CSRF') &&
      originalRequest &&
      !originalRequest._csrfRetry
    ) {
      originalRequest._csrfRetry = true
      try {
        const freshToken = await fetchXsrfToken()
        if (freshToken) {
          originalRequest.headers['X-XSRF-TOKEN'] = freshToken
        }
        return httpClient(originalRequest)
      } catch (csrfError) {
        return Promise.reject(csrfError)
      }
    }

    return Promise.reject(error)
  }
)

