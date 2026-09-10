import { httpClient } from '../../../shared/api'
import type {
  SmartSetupProposal,
  SmartSetupRequest,
  WorkspaceMember,
} from '../types/smartSetup.types'

export const smartSetupApi = {
  /**
   * Calls POST /api/boards/{boardId}/smart-setup to generate task proposals via AI.
   * X-XSRF-TOKEN is automatically handled by httpClient.
   */
  generateSmartSetup: async (
    boardId: string,
    data: SmartSetupRequest
  ): Promise<SmartSetupProposal> => {
    const res = await httpClient.post<SmartSetupProposal>(
      `/boards/${boardId}/smart-setup`,
      data
    )
    return res.data
  },

  /**
   * Calls GET /api/workspaces/{workspaceId}/members to populate assignee dropdown.
   */
  getWorkspaceMembers: async (workspaceId: string): Promise<WorkspaceMember[]> => {
    const res = await httpClient.get<WorkspaceMember[]>(
      `/workspaces/${workspaceId}/members`
    )
    return res.data
  },
}
