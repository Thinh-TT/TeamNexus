import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { message } from 'antd'
import { useDashboard } from '../useDashboard'
import { dashboardApi } from '../../services/dashboardApi'
import type { DashboardResponse } from '../../types/dashboard.types'

vi.mock('../../services/dashboardApi', () => ({
  dashboardApi: {
    get: vi.fn(),
  },
}))

vi.mock('antd', async () => {
  const actual = await vi.importActual('antd')
  return {
    ...actual,
    message: {
      success: vi.fn(),
      error: vi.fn(),
    },
  }
})

describe('useDashboard hook', () => {
  const wsId = '11111111-1111-1111-1111-111111111111'
  const mockDashboard: DashboardResponse = {
    workspaceId: wsId,
    workspaceName: 'Test Workspace',
    utcNow: '2026-09-15T12:00:00Z',
    dueSoonDays: 3,
    myTasks: {
      overdue: { count: 1, items: [] },
      dueSoon: { count: 2, items: [] },
      recentlyAssigned: { count: 3, items: [] },
    },
    boards: [],
    boardsTruncated: false,
    recentActivities: [],
    summary: {
      totalTasks: 10,
      doneTasks: 4,
      openTasks: 6,
      overdueTasks: 1,
      myOpenTasks: 5,
    },
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches dashboard data successfully on mount', async () => {
    vi.mocked(dashboardApi.get).mockResolvedValueOnce(mockDashboard)

    const { result } = renderHook(() => useDashboard(wsId))

    expect(result.current.status).toBe('loading')

    await act(async () => {
      // wait for effect
    })

    expect(result.current.status).toBe('idle')
    expect(result.current.dashboard).toEqual(mockDashboard)
    expect(result.current.error).toBeNull()
  })

  it('handles 404 error and displays message.error', async () => {
    vi.mocked(dashboardApi.get).mockRejectedValueOnce({
      response: {
        status: 404,
        data: { error: 'Không tìm thấy workspace' },
      },
    })

    const { result } = renderHook(() => useDashboard(wsId))

    await act(async () => {
      // wait for effect
    })

    expect(result.current.status).toBe('error')
    expect(result.current.httpStatus).toBe(404)
    expect(result.current.error).toBe('Không tìm thấy workspace')
    expect(message.error).toHaveBeenCalledWith('Không tìm thấy workspace')
  })

  it('reload re-triggers fetch', async () => {
    vi.mocked(dashboardApi.get).mockResolvedValue(mockDashboard)

    const { result } = renderHook(() => useDashboard(wsId))

    await act(async () => {})
    expect(dashboardApi.get).toHaveBeenCalledTimes(1)

    await act(async () => {
      await result.current.reload()
    })

    expect(dashboardApi.get).toHaveBeenCalledTimes(2)
  })
})
