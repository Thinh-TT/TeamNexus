import { httpClient } from '../../../shared/api'
import type {
  ObserverRunDetailResponse,
  ObserverRunResponse,
  ObserverScanResponse,
} from '../types/notification.types'

export const observerApi = {
  /**
   * POST /api/workspaces/{workspaceId}/observer/scan
   */
  triggerObserverScan: async (
    workspaceId: string
  ): Promise<ObserverScanResponse> => {
    const res = await httpClient.post<ObserverScanResponse>(
      `/workspaces/${workspaceId}/observer/scan`
    )
    return res.data
  },

  /**
   * GET /api/workspaces/{workspaceId}/observer/runs?take=20
   */
  listObserverRuns: async (
    workspaceId: string,
    take?: number
  ): Promise<ObserverRunResponse[]> => {
    const params: Record<string, number> = {}
    if (typeof take === 'number') {
      params.take = take
    }
    const res = await httpClient.get<ObserverRunResponse[]>(
      `/workspaces/${workspaceId}/observer/runs`,
      { params }
    )
    return res.data
  },

  /**
   * GET /api/observer/runs/{runId}
   */
  getObserverRun: async (
    runId: string
  ): Promise<ObserverRunDetailResponse> => {
    const res = await httpClient.get<ObserverRunDetailResponse>(
      `/observer/runs/${runId}`
    )
    return res.data
  },
}
