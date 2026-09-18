import { httpClient } from '../../../shared/api/httpClient'
import type { AiActionLog } from '../types/aiAction.types'
import type {
  BoardTemplateProposal,
  BoardTemplateRequest,
  ConfirmBoardTemplateRequest,
} from '../types/boardTemplate.types'

export const boardTemplateApi = {
  generateTemplate: async (
    workspaceId: string,
    description: string
  ): Promise<BoardTemplateProposal> => {
    const payload: BoardTemplateRequest = { description }
    const response = await httpClient.post<BoardTemplateProposal>(
      `/workspaces/${workspaceId}/smart-setup/template`,
      payload
    )
    return response.data
  },

  confirmTemplate: async (
    workspaceId: string,
    proposal: BoardTemplateProposal
  ): Promise<AiActionLog> => {
    const payload: ConfirmBoardTemplateRequest = { proposal }
    const response = await httpClient.post<AiActionLog>(
      `/workspaces/${workspaceId}/smart-setup/template/confirm`,
      payload
    )
    return response.data
  },
}
