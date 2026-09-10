import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useReportSummary } from '../useReportSummary'
import { reportingApi } from '../../services/reportingApi'
import type { ReportSummaryResponse } from '../../types/reporting.types'

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    getReportSummary: vi.fn(),
  },
}))

const mockReportSummary: ReportSummaryResponse = {
  workspaceId: 'ws-1',
  workspaceName: 'Workspace 1',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-08-01T00:00:00Z', to: '2026-09-01T00:00:00Z', days: 30, clamped: false },
  generatedAt: '2026-09-01T00:00:00Z',
  progress: { total: 10, done: 6, open: 4, overdue: 1, donePercent: 60 },
  performance: {
    createdInRange: 8,
    completedInRange: 5,
    avgCompletionHours: 48,
    avgLeadTimeHours: 72,
    onTimeRate: 80,
    overdueRate: 20,
    throughputPerWeek: 1.2,
    completedAtMissing: 0,
  },
  byBoard: [],
  byAssignee: [],
  activity: { totalActions: 15, byAction: [], activeUsers: 3, actionsPerDay: 0.5 },
  health: { runsScanned: 0, signalsByType: [], findingsBySeverity: [] },
  metricDefinitions: {},
  truncated: { rowCapReached: false, maxRows: 5000 },
}

describe('useReportSummary', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches and sets report data on mount', async () => {
    vi.mocked(reportingApi.getReportSummary).mockResolvedValueOnce(mockReportSummary)

    const { result } = renderHook(() => useReportSummary('ws-1'))

    expect(result.current.status).toBe('loading')

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    expect(result.current.report).toEqual(mockReportSummary)
    expect(result.current.error).toBeNull()
    expect(reportingApi.getReportSummary).toHaveBeenCalledWith('ws-1', {})
  })

  it('sets error and status error when API fails', async () => {
    vi.mocked(reportingApi.getReportSummary).mockRejectedValueOnce({
      response: {
        status: 403,
        data: { error: 'Requires Manager or Admin role in this workspace.' },
      },
    })

    const { result } = renderHook(() => useReportSummary('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('error')
    })

    expect(result.current.error).toBe('Requires Manager or Admin role in this workspace.')
    expect(result.current.httpStatus).toBe(403)
    expect(result.current.report).toBeNull()
  })

  it('updates board filter with setBoard and refetches with new param', async () => {
    vi.mocked(reportingApi.getReportSummary).mockResolvedValue(mockReportSummary)

    const { result } = renderHook(() => useReportSummary('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    act(() => {
      result.current.setBoard('board-123')
    })

    await waitFor(() => {
      expect(reportingApi.getReportSummary).toHaveBeenCalledWith('ws-1', {
        boardId: 'board-123',
      })
    })
  })

  it('updates date range with setRange and refetches', async () => {
    vi.mocked(reportingApi.getReportSummary).mockResolvedValue(mockReportSummary)

    const { result } = renderHook(() => useReportSummary('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    act(() => {
      result.current.setRange('2026-07-01T00:00:00Z', '2026-08-01T00:00:00Z')
    })

    await waitFor(() => {
      expect(reportingApi.getReportSummary).toHaveBeenCalledWith('ws-1', {
        from: '2026-07-01T00:00:00Z',
        to: '2026-08-01T00:00:00Z',
      })
    })
  })

  it('calls reload with override params', async () => {
    vi.mocked(reportingApi.getReportSummary).mockResolvedValue(mockReportSummary)

    const { result } = renderHook(() => useReportSummary('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    await act(async () => {
      await result.current.reload({ boardId: 'b-99' })
    })

    expect(reportingApi.getReportSummary).toHaveBeenCalledWith('ws-1', {
      boardId: 'b-99',
    })
  })
})
