import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { profileApi } from '../profileApi'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('profileApi service', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getProfile calls GET /users/me', async () => {
    const mockProfile = {
      id: 'u-1',
      email: 'me@example.com',
      displayName: 'Nguyen Van A',
      avatarUrl: null,
      createdAt: '2026-09-01T00:00:00Z',
    }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockProfile })

    const res = await profileApi.getProfile()

    expect(httpClient.get).toHaveBeenCalledWith('/users/me')
    expect(res).toEqual(mockProfile)
  })

  it('updateProfile calls PUT /users/me with payload', async () => {
    const payload = {
      displayName: 'Nguyen Van B',
      avatarUrl: 'https://example.com/b.png',
    }
    vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { ...payload, id: 'u-1', email: 'me@example.com', createdAt: '' } })

    await profileApi.updateProfile(payload)

    expect(httpClient.put).toHaveBeenCalledWith('/users/me', payload)
  })

  it('listMyWorkspaces calls GET /users/me/workspaces', async () => {
    const mockWorkspaces = [
      { id: 'ws-1', name: 'Workspace A', role: 'Admin', isOwner: true },
    ]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockWorkspaces })

    const res = await profileApi.listMyWorkspaces()

    expect(httpClient.get).toHaveBeenCalledWith('/users/me/workspaces')
    expect(res).toEqual(mockWorkspaces)
  })

  it('leaveWorkspace calls DELETE /users/me/workspaces/{workspaceId}', async () => {
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: null })

    await profileApi.leaveWorkspace('ws-123')

    expect(httpClient.delete).toHaveBeenCalledWith('/users/me/workspaces/ws-123')
  })
})
