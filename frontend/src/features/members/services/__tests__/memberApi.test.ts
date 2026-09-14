import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { memberApi } from '../memberApi'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('memberApi service', () => {
  const wsId = 'ws-100'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('listMembers calls GET /workspaces/{workspaceId}/members', async () => {
    const mockMembers = [{ userId: 'u-1', displayName: 'Thinh', role: 'Admin' }]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockMembers })

    const result = await memberApi.listMembers(wsId)

    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/members`)
    expect(result).toEqual(mockMembers)
  })

  it('updateMemberRole calls PUT /workspaces/{workspaceId}/members/{memberUserId}/role with { role }', async () => {
    vi.mocked(httpClient.put).mockResolvedValueOnce({ data: null })

    await memberApi.updateMemberRole(wsId, 'u-2', 'Manager')

    expect(httpClient.put).toHaveBeenCalledWith(
      `/workspaces/${wsId}/members/u-2/role`,
      { role: 'Manager' }
    )
  })

  it('removeMember calls DELETE /workspaces/{workspaceId}/members/{memberUserId}', async () => {
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: null })

    await memberApi.removeMember(wsId, 'u-2')

    expect(httpClient.delete).toHaveBeenCalledWith(
      `/workspaces/${wsId}/members/u-2`
    )
  })

  it('listInvitations calls GET /workspaces/{workspaceId}/invitations', async () => {
    const mockInvitations = [{ id: 'inv-1', invitedEmail: 'a@b.com' }]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockInvitations })

    const result = await memberApi.listInvitations(wsId)

    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/invitations`
    )
    expect(result).toEqual(mockInvitations)
  })

  it('createInvitation sends email and role when role is provided', async () => {
    const mockCreated = { id: 'inv-1', invitedEmail: 'test@b.com', emailSent: true }
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockCreated })

    const res = await memberApi.createInvitation(wsId, {
      email: 'test@b.com',
      role: 'Manager',
    })

    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/invitations`,
      { email: 'test@b.com', role: 'Manager' }
    )
    expect(res).toEqual(mockCreated)
  })

  it('createInvitation omits role key when role is undefined', async () => {
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { id: 'inv-2' } })

    await memberApi.createInvitation(wsId, {
      email: 'norole@b.com',
    })

    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/invitations`,
      { email: 'norole@b.com' }
    )
  })

  it('cancelInvitation calls DELETE /workspaces/{workspaceId}/invitations/{invitationId}', async () => {
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: null })

    await memberApi.cancelInvitation(wsId, 'inv-99')

    expect(httpClient.delete).toHaveBeenCalledWith(
      `/workspaces/${wsId}/invitations/inv-99`
    )
  })

  it('sendQuickEmail calls POST /workspaces/{workspaceId}/quick-email and propagates 403 error', async () => {
    const error403 = { response: { status: 403, data: { error: 'Forbidden' } } }
    vi.mocked(httpClient.post).mockRejectedValueOnce(error403)

    const payload = {
      subject: 'Thông báo',
      body: 'Nội dung',
      recipientUserIds: ['u-1'],
    }

    await expect(memberApi.sendQuickEmail(wsId, payload)).rejects.toEqual(error403)
    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/quick-email`,
      payload
    )
  })
})
