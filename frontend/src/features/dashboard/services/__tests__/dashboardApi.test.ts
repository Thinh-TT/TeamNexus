import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { dashboardApi } from '../dashboardApi'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

describe('dashboardApi service', () => {
  const wsId = '11111111-1111-1111-1111-111111111111'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls GET /workspaces/{id}/dashboard without params', async () => {
    const mockData = { workspaceId: wsId, workspaceName: 'Test WS' }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const result = await dashboardApi.get(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/dashboard`)
    expect(result).toEqual(mockData)
  })

  it('calls GET /workspaces/{id}/dashboard with days and take query parameters', async () => {
    const mockData = { workspaceId: wsId, workspaceName: 'Test WS' }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const result = await dashboardApi.get(wsId, { days: 7, take: 20 })
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/dashboard?days=7&take=20`)
    expect(result).toEqual(mockData)
  })

  it('rejects with error when server returns 404', async () => {
    const errorResponse = { response: { status: 404, data: { error: 'Workspace không tồn tại' } } }
    vi.mocked(httpClient.get).mockRejectedValueOnce(errorResponse)

    await expect(dashboardApi.get(wsId)).rejects.toEqual(errorResponse)
  })
})
