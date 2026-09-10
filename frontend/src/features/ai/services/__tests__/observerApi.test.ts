import { afterEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { observerApi } from '../observerApi'
import type {
  ObserverRunDetailResponse,
  ObserverRunResponse,
  ObserverScanResponse,
} from '../../types/notification.types'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('observerApi', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('triggerObserverScan calls POST /workspaces/{wsId}/observer/scan', async () => {
    const mockScan: ObserverScanResponse = {
      runId: 'run-1',
      workspaceId: 'ws-1',
      status: 'Completed',
      signalsDetected: 3,
      findingsWritten: 2,
      notificationsCreated: 2,
      aiCalled: true,
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockScan })

    const res = await observerApi.triggerObserverScan('ws-1')

    expect(httpClient.post).toHaveBeenCalledWith('/workspaces/ws-1/observer/scan')
    expect(res).toEqual(mockScan)
  })

  it('listObserverRuns calls GET /workspaces/{wsId}/observer/runs with take param', async () => {
    const mockRuns: ObserverRunResponse[] = [
      {
        id: 'run-1',
        workspaceId: 'ws-1',
        status: 'Completed',
        startedAt: '2026-09-10T12:00:00Z',
        finishedAt: '2026-09-10T12:00:05Z',
        signalsDetected: 2,
        findingsWritten: 2,
        notificationsCreated: 1,
        aiCalled: true,
      },
    ]

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockRuns })

    const res = await observerApi.listObserverRuns('ws-1', 20)

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/observer/runs', {
      params: { take: 20 },
    })
    expect(res).toEqual(mockRuns)
  })

  it('getObserverRun calls GET /observer/runs/{runId}', async () => {
    const mockDetail: ObserverRunDetailResponse = {
      id: 'run-1',
      workspaceId: 'ws-1',
      status: 'Completed',
      startedAt: '2026-09-10T12:00:00Z',
      finishedAt: '2026-09-10T12:00:05Z',
      signalsDetected: 1,
      findingsWritten: 1,
      notificationsCreated: 1,
      aiCalled: true,
      summary: {
        durationMs: 1200,
        promptTokens: 100,
        completionTokens: 50,
      },
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

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockDetail })

    const res = await observerApi.getObserverRun('run-1')

    expect(httpClient.get).toHaveBeenCalledWith('/observer/runs/run-1')
    expect(res).toEqual(mockDetail)
  })
})
