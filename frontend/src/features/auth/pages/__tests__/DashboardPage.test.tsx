import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DashboardPage } from '../DashboardPage'
import { workspaceApi } from '../../../workspace/services/workspaceApi'
import { useAuth } from '../../hooks/useAuth'

vi.mock('../../hooks/useAuth', () => ({
  useAuth: vi.fn(),
}))

vi.mock('../../../workspace/services/workspaceApi', () => ({
  workspaceApi: {
    list: vi.fn(),
  },
}))

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: () => <header data-testid="app-header" />,
}))

describe('DashboardPage (Root entry /)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useAuth).mockReturnValue({
      user: {
        id: 'u-1',
        displayName: 'Trần Thinh',
        email: 'thinh@example.com',
        roles: ['Admin'],
      },
      isLoading: false,
      isAuthenticated: true,
      checkAuth: vi.fn(),
      loginWithProvider: vi.fn(),
      logout: vi.fn(),
    })
  })

  it('calls workspaceApi.list on mount and displays workspaces', async () => {
    vi.mocked(workspaceApi.list).mockResolvedValueOnce([
      {
        id: 'ws-101',
        name: 'Dự Án Alpha',
        description: 'Mô tả dự án alpha',
        role: 'Admin',
        ownerId: 'u-1',
        isOwner: true,
        createdAt: '2026-09-01T00:00:00Z',
      },
    ])

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>
    )

    await waitFor(() => {
      expect(workspaceApi.list).toHaveBeenCalled()
    })

    expect(screen.getByText('Trần Thinh')).toBeInTheDocument()
    expect(screen.getByText('Dự Án Alpha')).toBeInTheDocument()
    expect(screen.getByText('Mô tả dự án alpha')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tổng quan/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Bảng Kanban/i })).toBeInTheDocument()

    // Does NOT have Phase 1 test RBAC ping buttons
    expect(screen.queryByText(/Test Manager Access/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Test Admin Access/i)).not.toBeInTheDocument()
  })
})
