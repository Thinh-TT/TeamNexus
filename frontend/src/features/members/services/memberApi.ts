import { httpClient } from '../../../shared/api'
import type {
  CreateInvitationRequest,
  InvitationResponse,
  QuickEmailRequest,
  QuickEmailResult,
  WorkspaceMemberResponse,
  WorkspaceRole,
} from '../types/member.types'

export const memberApi = {
  /**
   * GET /api/workspaces/{workspaceId}/members
   */
  listMembers: async (workspaceId: string): Promise<WorkspaceMemberResponse[]> => {
    const res = await httpClient.get<WorkspaceMemberResponse[]>(
      `/workspaces/${workspaceId}/members`
    )
    return res.data
  },

  /**
   * PUT /api/workspaces/{workspaceId}/members/{memberUserId}/role
   */
  updateMemberRole: async (
    workspaceId: string,
    memberUserId: string,
    role: WorkspaceRole
  ): Promise<void> => {
    await httpClient.put(
      `/workspaces/${workspaceId}/members/${memberUserId}/role`,
      { role }
    )
  },

  /**
   * DELETE /api/workspaces/{workspaceId}/members/{memberUserId}
   */
  removeMember: async (
    workspaceId: string,
    memberUserId: string
  ): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}/members/${memberUserId}`)
  },

  /**
   * GET /api/workspaces/{workspaceId}/invitations
   */
  listInvitations: async (
    workspaceId: string
  ): Promise<InvitationResponse[]> => {
    const res = await httpClient.get<InvitationResponse[]>(
      `/workspaces/${workspaceId}/invitations`
    )
    return res.data
  },

  /**
   * POST /api/workspaces/{workspaceId}/invitations
   */
  createInvitation: async (
    workspaceId: string,
    request: CreateInvitationRequest
  ): Promise<InvitationResponse> => {
    const payload: { email: string; role?: WorkspaceRole } = {
      email: request.email,
    }
    if (request.role !== undefined) {
      payload.role = request.role
    }

    const res = await httpClient.post<InvitationResponse>(
      `/workspaces/${workspaceId}/invitations`,
      payload
    )
    return res.data
  },

  /**
   * DELETE /api/workspaces/{workspaceId}/invitations/{invitationId}
   */
  cancelInvitation: async (
    workspaceId: string,
    invitationId: string
  ): Promise<void> => {
    await httpClient.delete(
      `/workspaces/${workspaceId}/invitations/${invitationId}`
    )
  },

  /**
   * POST /api/workspaces/{workspaceId}/quick-email
   */
  sendQuickEmail: async (
    workspaceId: string,
    request: QuickEmailRequest
  ): Promise<QuickEmailResult> => {
    const res = await httpClient.post<QuickEmailResult>(
      `/workspaces/${workspaceId}/quick-email`,
      request
    )
    return res.data
  },
}
