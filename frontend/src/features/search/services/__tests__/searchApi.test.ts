import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { searchApi } from '../searchApi'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

describe('searchApi service', () => {
  const wsId = '11111111-1111-1111-1111-111111111111'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls GET /workspaces/{id}/tasks/search without filters', async () => {
    const mockRes = { items: [], nextCursor: null, hasMore: false, hasQuery: false }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockRes })

    const result = await searchApi.searchTasks(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/tasks/search`)
    expect(result).toEqual(mockRes)
  })

  it('appends only non-empty filters with correct formats (CSV labelIds, string booleans)', async () => {
    const mockRes = { items: [], nextCursor: 'c-1', hasMore: true, hasQuery: true }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockRes })

    const result = await searchApi.searchTasks(wsId, {
      q: '  báo cáo ',
      boardId: 'b-1',
      unassigned: true,
      labelIds: ['l-1', 'l-2'],
      priority: 'Urgent',
      overdue: true,
      includeDone: false,
      take: 50,
      cursor: 'cur-123',
    })

    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/tasks/search?q=b%C3%A1o+c%C3%A1o&boardId=b-1&unassigned=true&labelIds=l-1%2Cl-2&priority=Urgent&overdue=true&includeDone=false&take=50&cursor=cur-123`
    )
    expect(result).toEqual(mockRes)
  })

  it('passes assigneeId when not unassigned', async () => {
    const mockRes = { items: [], nextCursor: null, hasMore: false, hasQuery: true }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockRes })

    await searchApi.searchTasks(wsId, {
      assigneeId: 'u-1',
      unassigned: false,
    })

    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/tasks/search?assigneeId=u-1`
    )
  })
})
