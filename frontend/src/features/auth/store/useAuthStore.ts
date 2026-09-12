import { create } from 'zustand'
import { httpClient } from '../../../shared/api'

export interface User {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  roles: string[]
}

interface AuthState {
  user: User | null
  isLoading: boolean
  isAuthenticated: boolean
  checkAuth: () => Promise<void>
  loginWithProvider: (provider: 'google' | 'github') => void
  logout: () => Promise<void>
}

export const useAuthStore = create<AuthState>((set) => ({
  user: null,
  isLoading: true,
  isAuthenticated: false,

  checkAuth: async () => {
    set({ isLoading: true })
    try {
      const response = await httpClient.get<User>('/auth/me')
      set({
        user: response.data,
        isAuthenticated: true,
        isLoading: false,
      })
      try {
        await httpClient.get('/auth/antiforgery?json=true')
      } catch {
        // non-blocking
      }
    } catch {
      set({
        user: null,
        isAuthenticated: false,
        isLoading: false,
      })
    }
  },

  loginWithProvider: (provider: 'google' | 'github') => {
    const apiBase = import.meta.env.VITE_API_BASE_URL ?? '/api'
    window.location.href = `${apiBase}/auth/login/${provider}`
  },

  logout: async () => {
    try {
      await httpClient.post('/auth/logout')
    } catch {
      // Ignore logout errors if session already invalid
    } finally {
      set({
        user: null,
        isAuthenticated: false,
      })
    }
  },
}))

// Listen for global auth:unauthorized event from httpClient interceptor
if (typeof window !== 'undefined') {
  window.addEventListener('auth:unauthorized', () => {
    useAuthStore.setState({
      user: null,
      isAuthenticated: false,
      isLoading: false,
    })
  })
}
