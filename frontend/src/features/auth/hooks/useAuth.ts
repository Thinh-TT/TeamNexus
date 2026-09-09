import { useAuthStore } from '../store/useAuthStore'

export function useAuth() {
  const user = useAuthStore((state) => state.user)
  const isLoading = useAuthStore((state) => state.isLoading)
  const isAuthenticated = useAuthStore((state) => state.isAuthenticated)
  const checkAuth = useAuthStore((state) => state.checkAuth)
  const loginWithProvider = useAuthStore((state) => state.loginWithProvider)
  const logout = useAuthStore((state) => state.logout)

  return {
    user,
    isLoading,
    isAuthenticated,
    checkAuth,
    loginWithProvider,
    logout,
  }
}
