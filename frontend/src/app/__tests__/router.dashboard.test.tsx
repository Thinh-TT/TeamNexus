import React from 'react'
import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AppRouter } from '../router'
import { useAuth } from '../../features/auth/hooks/useAuth'

vi.mock('../../features/auth/hooks/useAuth', () => ({
  useAuth: vi.fn(),
}))

vi.mock('../../features/dashboard/pages/WorkspaceDashboardPage', () => ({
  WorkspaceDashboardPage: () => <div data-testid="workspace-dashboard-page">Dashboard Page Rendered</div>,
}))

describe('Router Dashboard Route', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders WorkspaceDashboardPage when authenticated and on dashboard route', () => {
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u-1', displayName: 'User 1', email: 'u1@test.com', roles: [] },
      isLoading: false,
      isAuthenticated: true,
      checkAuth: vi.fn(),
      loginWithProvider: vi.fn(),
      logout: vi.fn(),
    })

    window.history.pushState({}, 'Dashboard', '/workspaces/ws-test-123/dashboard')

    render(<AppRouter />)

    expect(screen.getByTestId('workspace-dashboard-page')).toBeInTheDocument()
  })
})
