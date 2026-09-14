import { httpClient } from '../../../shared/api'
import type {
  MyWorkspaceResponse,
  UpdateProfileRequest,
  UserProfileResponse,
} from '../types/profile.types'

export const profileApi = {
  /**
   * GET /api/users/me
   */
  getProfile: async (): Promise<UserProfileResponse> => {
    const res = await httpClient.get<UserProfileResponse>('/users/me')
    return res.data
  },

  /**
   * PUT /api/users/me
   */
  updateProfile: async (
    data: UpdateProfileRequest
  ): Promise<UserProfileResponse> => {
    const res = await httpClient.put<UserProfileResponse>('/users/me', data)
    return res.data
  },

  /**
   * GET /api/users/me/workspaces
   */
  listMyWorkspaces: async (): Promise<MyWorkspaceResponse[]> => {
    const res = await httpClient.get<MyWorkspaceResponse[]>(
      '/users/me/workspaces'
    )
    return res.data
  },

  /**
   * DELETE /api/users/me/workspaces/{workspaceId}
   */
  leaveWorkspace: async (workspaceId: string): Promise<void> => {
    await httpClient.delete(`/users/me/workspaces/${workspaceId}`)
  },
}
