import React from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceDashboardPage } from '../WorkspaceDashboardPage'
import { dashboardApi } from '../../services/dashboardApi'
import type { DashboardResponse } from '../../types/dashboard.types'

vi.mock('../../services/dashboardApi', () => ({
  dashboardApi: {
    get: vi.fn(),
  },
}))

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn().mockResolvedValue({ unreadCount: 0, items: [] }),
  },
}))

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: ({ children }: { children?: React.ReactNode }) => <header data-testid="app-header">{children}</header>,
}))

describe('WorkspaceDashboardPage', () => {
  const wsId = '11111111-1111-1111-1111-111111111111'

  const mockDashboard: DashboardResponse = {
    workspaceId: wsId,
    workspaceName: 'Nexus Workspace',
    utcNow: '2026-09-15T12:00:00Z',
    dueSoonDays: 3,
    myTasks: {
      overdue: { count: 0, items: [] },
      dueSoon: { count: 0, items: [] },
      recentlyAssigned: { count: 0, items: [] },
    },
    boards: [],
    boardsTruncated: false,
    recentActivities: [],
    summary: {
      totalTasks: 20,
      doneTasks: 8,
      openTasks: 12,
      overdueTasks: 2,
      myOpenTasks: 5,
    },
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders all 4 panels and summary statistics on success', async () => {
    vi.mocked(dashboardApi.get).mockResolvedValueOnce(mockDashboard)

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/dashboard`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/dashboard" element={<WorkspaceDashboardPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText(/Tổng Quan Workspace: Nexus Workspace/i)).toBeInTheDocument()
    expect(screen.getByText('Task của tôi')).toBeInTheDocument()
    expect(screen.getByText('Cảnh báo AI Observer')).toBeInTheDocument()
    expect(screen.getByText('Tóm tắt bảng Kanban')).toBeInTheDocument()
    expect(screen.getByText('Hoạt động gần đây')).toBeInTheDocument()
    expect(screen.getByText('20')).toBeInTheDocument()
  })

  it('renders 404 result when user is not a member of the workspace', async () => {
    vi.mocked(dashboardApi.get).mockRejectedValueOnce({
      response: {
        status: 404,
        data: { error: 'Workspace không tồn tại' },
      },
    })

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/dashboard`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/dashboard" element={<WorkspaceDashboardPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText('404')).toBeInTheDocument()
    expect(
      screen.getByText(/Không tìm thấy workspace hoặc bạn không phải là thành viên/i)
    ).toBeInTheDocument()
  })

  it('renders project health gauge when health data is present', async () => {
    const dashboardWithHealth: DashboardResponse = {
      ...mockDashboard,
      health: {
        score: 82,
        band: 'Tốt',
        components: { overdue: 5, atRisk: 5 },
        reasons: [],
      },
    }
    vi.mocked(dashboardApi.get).mockResolvedValueOnce(dashboardWithHealth)

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/dashboard`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/dashboard" element={<WorkspaceDashboardPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByTestId('project-health-gauge')).toBeInTheDocument()
    expect(screen.getByText('Tốt')).toBeInTheDocument()
    expect(screen.getByText('82')).toBeInTheDocument()
    // Verify 5 KPI stats are still intact
    expect(screen.getByText('20')).toBeInTheDocument()
    expect(screen.getByText('Tổng số thẻ')).toBeInTheDocument()
  })

  it('renders empty health state when health is null', async () => {
    const dashboardWithoutHealth: DashboardResponse = {
      ...mockDashboard,
      health: null,
    }
    vi.mocked(dashboardApi.get).mockResolvedValueOnce(dashboardWithoutHealth)

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/dashboard`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/dashboard" element={<WorkspaceDashboardPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByTestId('project-health-gauge')).toBeInTheDocument()
    expect(screen.getByText('Chưa đủ dữ liệu để tính sức khỏe dự án')).toBeInTheDocument()
  })
})
