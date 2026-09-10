import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { observerApi } from '../../services/observerApi'
import { useObserverRuns } from '../useObserverRuns'
import type {
  ObserverRunDetailResponse,
  ObserverRunResponse,
  ObserverScanResponse,
} from '../../types/notification.types'

vi.mock('../../services/observerApi', () => ({
  observerApi: {
    triggerObserverScan: vi.fn(),
    listObserverRuns: vi.fn(),
    getObserverRun: vi.fn(),
  },
}))

describe('useObserverRuns', () => {
  const mockRun: ObserverRunResponse = {
    id: 'run-1',
    workspaceId: 'ws-1',
    status: 'Completed',
    startedAt: '2026-09-10T12:00:00Z',
    finishedAt: '2026-09-10T12:00:05Z',
    signalsDetected: 2,
    findingsWritten: 2,
    notificationsCreated: 1,
    aiCalled: true,
  }

  const mockRunDetail: ObserverRunDetailResponse = {
    ...mockRun,
    summary: { durationMs: 1500, promptTokens: 50, completionTokens: 20 },
    findings: [
      {
        type: 'OverdueTask',
        severity: 'High',
        title: 'Task trễ hạn',
        message: 'Task A trễ hạn 5 ngày',
        taskIds: ['task-1'],
        userIds: ['user-1'],
      },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.clearAllMocks()
  })

  it('initializes with default state', () => {
    const { result } = renderHook(() => useObserverRuns('ws-1'))

    expect(result.current.runs).toEqual([])
    expect(result.current.selectedRun).toBeNull()
    expect(result.current.status).toBe('idle')
    expect(result.current.error).toBeNull()
    expect(result.current.httpStatus).toBeNull()
  })

  it('reload fetches runs list and updates state', async () => {
    vi.mocked(observerApi.listObserverRuns).mockResolvedValueOnce([mockRun])

    const { result } = renderHook(() => useObserverRuns('ws-1'))

    await act(async () => {
      await result.current.reload()
    })

    expect(observerApi.listObserverRuns).toHaveBeenCalledWith('ws-1', 30)
    expect(result.current.runs).toEqual([mockRun])
    expect(result.current.status).toBe('idle')
  })

  it('openRun and closeRun update selectedRun state', async () => {
    vi.mocked(observerApi.getObserverRun).mockResolvedValueOnce(mockRunDetail)

    const { result } = renderHook(() => useObserverRuns('ws-1'))

    await act(async () => {
      await result.current.openRun('run-1')
    })

    expect(observerApi.getObserverRun).toHaveBeenCalledWith('run-1')
    expect(result.current.selectedRun).toEqual(mockRunDetail)

    act(() => {
      result.current.closeRun()
    })

    expect(result.current.selectedRun).toBeNull()
  })

  it('triggerScan triggers scan, reloads runs, and executes onScanFinished callback', async () => {
    const scanRes: ObserverScanResponse = {
      runId: 'run-2',
      workspaceId: 'ws-1',
      status: 'Completed',
      signalsDetected: 1,
      findingsWritten: 1,
      notificationsCreated: 1,
      aiCalled: true,
    }
    vi.mocked(observerApi.triggerObserverScan).mockResolvedValueOnce(scanRes)
    vi.mocked(observerApi.listObserverRuns).mockResolvedValueOnce([mockRun])

    const onScanFinished = vi.fn()
    const { result } = renderHook(() => useObserverRuns('ws-1'))

    await act(async () => {
      await result.current.triggerScan(onScanFinished)
    })

    expect(observerApi.triggerObserverScan).toHaveBeenCalledWith('ws-1')
    expect(observerApi.listObserverRuns).toHaveBeenCalledWith('ws-1', 30)
    expect(onScanFinished).toHaveBeenCalledTimes(1)
    expect(result.current.status).toBe('idle')
  })

  it('handles error when scan fails', async () => {
    const errorObj = {
      response: {
        status: 502,
        data: { error: 'DeepSeek AI tạm thời không phản hồi.' },
      },
    }
    vi.mocked(observerApi.triggerObserverScan).mockRejectedValueOnce(errorObj)

    const { result } = renderHook(() => useObserverRuns('ws-1'))

    await act(async () => {
      await result.current.triggerScan()
    })

    expect(result.current.status).toBe('idle')
    expect(result.current.httpStatus).toBe(502)
    expect(result.current.error).toBe('DeepSeek AI tạm thời không phản hồi.')
  })
})
