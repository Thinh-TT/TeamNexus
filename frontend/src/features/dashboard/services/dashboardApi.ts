import { httpClient } from '../../../shared/api/httpClient'
import type { DashboardResponse } from '../types/dashboard.types'

export interface GetDashboardParams {
  days?: number
  take?: number
}

export const dashboardApi = {
  /**
   * Lấy dữ liệu tổng quan workspace
   * GET /api/workspaces/{workspaceId}/dashboard?days=&take=
   */
  get: async (workspaceId: string, params?: GetDashboardParams): Promise<DashboardResponse> => {
    const query = new URLSearchParams()
    if (params) {
      if (params.days !== undefined) {
        query.append('days', String(params.days))
      }
      if (params.take !== undefined) {
        query.append('take', String(params.take))
      }
    }
    const queryString = query.toString()
    const url = queryString
      ? `/workspaces/${workspaceId}/dashboard?${queryString}`
      : `/workspaces/${workspaceId}/dashboard`
    const res = await httpClient.get<DashboardResponse>(url)
    return res.data
  },
}
