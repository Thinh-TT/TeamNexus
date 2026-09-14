import { httpClient } from '../../../shared/api'
import type { InvitationPreviewResponse } from '../../members/types/member.types'

export interface AcceptInvitationResult {
  workspaceId: string
  role: string
  alreadyMember: boolean
}

export const invitationApi = {
  /**
   * GET /api/invitations/preview?token=...
   * Endpoint công khai không yêu cầu xác thực
   */
  previewInvitation: async (
    token: string
  ): Promise<InvitationPreviewResponse> => {
    const res = await httpClient.get<InvitationPreviewResponse>(
      '/invitations/preview',
      {
        params: { token },
      }
    )
    return res.data
  },

  /**
   * POST /api/invitations/accept
   * Yêu cầu người dùng đã đăng nhập
   */
  acceptInvitation: async (
    token: string
  ): Promise<AcceptInvitationResult> => {
    const res = await httpClient.post<AcceptInvitationResult>(
      '/invitations/accept',
      { token }
    )
    return res.data
  },
}
