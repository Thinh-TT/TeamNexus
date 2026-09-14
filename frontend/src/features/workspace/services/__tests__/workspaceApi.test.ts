import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { workspaceApi } from '../workspaceApi'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('workspaceApi service', () => {
  const wsId = 'ws-test-1'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('list: calls GET /workspaces', async () => {
    const mockData = [{ id: wsId, name: 'Workspace 1', role: 'Admin', ownerId: 'u-1', isOwner: true }]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const result = await workspaceApi.list()
    expect(httpClient.get).toHaveBeenCalledWith('/workspaces')
    expect(result).toEqual(mockData)
  })

  it('get: calls GET /workspaces/{id}', async () => {
    const mockDetail = { id: wsId, name: 'Workspace 1', memberCount: 5, boardCount: 2 }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockDetail })

    const result = await workspaceApi.get(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}`)
    expect(result).toEqual(mockDetail)
  })

  it('update: calls PUT /workspaces/{id} with body', async () => {
    vi.mocked(httpClient.put).mockResolvedValueOnce({ data: undefined })

    await workspaceApi.update(wsId, { name: 'Tên Mới', description: 'Mô tả mới' })
    expect(httpClient.put).toHaveBeenCalledWith(`/workspaces/${wsId}`, {
      name: 'Tên Mới',
      description: 'Mô tả mới',
    })
  })

  it('transferOwnership: calls PUT /workspaces/{id}/owner with body', async () => {
    vi.mocked(httpClient.put).mockResolvedValueOnce({ data: undefined })

    await workspaceApi.transferOwnership(wsId, { newOwnerId: 'user-new-owner' })
    expect(httpClient.put).toHaveBeenCalledWith(`/workspaces/${wsId}/owner`, {
      newOwnerId: 'user-new-owner',
    })
  })

  it('deleteWorkspace: calls DELETE /workspaces/{id}', async () => {
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: undefined })

    await workspaceApi.deleteWorkspace(wsId)
    expect(httpClient.delete).toHaveBeenCalledWith(`/workspaces/${wsId}`)
  })

  it('getActivity: calls GET /workspaces/{id}/activity without query string when params omitted', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: { items: [], nextCursor: null, hasMore: false } })

    await workspaceApi.getActivity(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/activity`)
  })

  it('getActivity: builds query string and omits undefined/empty parameters', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: { items: [], nextCursor: null, hasMore: false } })

    await workspaceApi.getActivity(wsId, {
      boardId: 'b-1',
      entityType: undefined,
      action: 'TaskCreated',
      take: 25,
      before: undefined,
    })

    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/activity?boardId=b-1&action=TaskCreated&take=25`
    )
  })
})
