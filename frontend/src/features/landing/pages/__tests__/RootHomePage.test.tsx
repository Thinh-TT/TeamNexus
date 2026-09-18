import React from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RootHomePage } from '../RootHomePage'
import { useAuth } from '../../../auth/hooks/useAuth'

vi.mock('../../../auth/hooks/useAuth', () => ({
  useAuth: vi.fn(),
}))

vi.mock('../../../auth/pages/DashboardPage', () => ({
  DashboardPage: () => <div data-testid="mock-dashboard-page">Dashboard Authenticated</div>,
}))

vi.mock('../LandingPage', () => ({
  LandingPage: () => <div data-testid="mock-landing-page">Landing Public Page</div>,
}))

describe('RootHomePage Router Coordinator', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders loading indicator when auth is loading', () => {
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isLoading: true,
      isAuthenticated: false,
      checkAuth: vi.fn(),
      loginWithProvider: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter>
        <RootHomePage />
      </MemoryRouter>
    )

    expect(screen.getByText('Đang khởi động TeamNexus...')).toBeInTheDocument()
  })

  it('renders LandingPage when user is unauthenticated', () => {
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isLoading: false,
      isAuthenticated: false,
      checkAuth: vi.fn(),
      loginWithProvider: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter>
        <RootHomePage />
      </MemoryRouter>
    )

    expect(screen.getByTestId('mock-landing-page')).toBeInTheDocument()
    expect(screen.queryByTestId('mock-dashboard-page')).not.toBeInTheDocument()
  })

  it('renders DashboardPage when user is authenticated', () => {
    vi.mocked(useAuth).mockReturnValue({
      user: { id: 'u-1', displayName: 'Thịnh', email: 'thinh@test.com', roles: [] },
      isLoading: false,
      isAuthenticated: true,
      checkAuth: vi.fn(),
      loginWithProvider: vi.fn(),
      logout: vi.fn(),
    })

    render(
      <MemoryRouter>
        <RootHomePage />
      </MemoryRouter>
    )

    expect(screen.getByTestId('mock-dashboard-page')).toBeInTheDocument()
    expect(screen.queryByTestId('mock-landing-page')).not.toBeInTheDocument()
  })
})
