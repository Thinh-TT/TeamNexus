import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
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
    create: vi.fn(),
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

  it('creates a workspace from the modal and navigates to its boards page', async () => {
    vi.mocked(workspaceApi.list).mockResolvedValueOnce([])
    vi.mocked(workspaceApi.create).mockResolvedValueOnce({
      id: 'ws-new',
      name: 'Dự án Beta',
      description: null,
      role: 'Admin',
      ownerId: 'u-1',
      isOwner: true,
    })

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route
            path="/workspaces/:workspaceId/boards"
            element={<div data-testid="boards-page" />}
          />
        </Routes>
      </MemoryRouter>
    )

    await waitFor(() => {
      expect(workspaceApi.list).toHaveBeenCalled()
    })

    fireEvent.click(screen.getByTestId('create-workspace-btn'))
    fireEvent.change(screen.getByTestId('workspace-name-input'), {
      target: { value: 'Dự án Beta' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => {
      expect(workspaceApi.create).toHaveBeenCalledWith({ name: 'Dự án Beta', description: null })
    })
    await waitFor(() => {
      expect(screen.getByTestId('boards-page')).toBeInTheDocument()
    })
  })

  it('keeps the modal open and shows the API error when creation fails', async () => {
    vi.mocked(workspaceApi.list).mockResolvedValueOnce([])
    vi.mocked(workspaceApi.create).mockRejectedValueOnce({
      response: { data: { error: 'Tên không hợp lệ' } },
    })

    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>
    )

    await waitFor(() => {
      expect(workspaceApi.list).toHaveBeenCalled()
    })

    fireEvent.click(screen.getByTestId('create-workspace-btn'))
    fireEvent.change(screen.getByTestId('workspace-name-input'), {
      target: { value: 'Dự án lỗi' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => {
      expect(workspaceApi.create).toHaveBeenCalled()
    })
    // Modal must stay open for correction (the title remains on screen).
    expect(screen.getByText('Tạo không gian làm việc mới')).toBeInTheDocument()
  })
})
