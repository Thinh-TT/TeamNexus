import { httpClient } from '../../../shared/api'
import type {
  AssigneeSuggestionResponse,
  TaskTriageRequest,
  TaskTriageResponse,
  UpdateMemberProfileRequest,
  UpdateTaxonomyRequest,
  WorkspaceTaxonomyResponse,
} from '../types/jev.types'

export const jevApi = {
  /**
   * Calls POST /api/boards/{boardId}/tasks/triage to get AI suggestions for domain, workType, priority.
   */
  triageTask: async (
    boardId: string,
    data: TaskTriageRequest,
    signal?: AbortSignal
  ): Promise<TaskTriageResponse> => {
    const res = await httpClient.post<TaskTriageResponse>(
      `/boards/${boardId}/tasks/triage`,
      data,
      { signal }
    )
    return res.data
  },

  /**
   * Calls POST /api/boards/{boardId}/tasks/{taskId}/assignee-suggestions.
   */
  suggestAssignees: async (
    boardId: string,
    taskId: string,
    signal?: AbortSignal
  ): Promise<AssigneeSuggestionResponse> => {
    const res = await httpClient.post<AssigneeSuggestionResponse>(
      `/boards/${boardId}/tasks/${taskId}/assignee-suggestions`,
      {},
      { signal }
    )
    return res.data
  },

  /**
   * Calls GET /api/workspaces/{workspaceId}/taxonomies.
   */
  getTaxonomies: async (workspaceId: string): Promise<WorkspaceTaxonomyResponse> => {
    const res = await httpClient.get<WorkspaceTaxonomyResponse>(
      `/workspaces/${workspaceId}/taxonomies`
    )
    return res.data
  },

  /**
   * Calls PUT /api/workspaces/{workspaceId}/taxonomies.
   */
  replaceTaxonomies: async (
    workspaceId: string,
    data: UpdateTaxonomyRequest
  ): Promise<WorkspaceTaxonomyResponse> => {
    const res = await httpClient.put<WorkspaceTaxonomyResponse>(
      `/workspaces/${workspaceId}/taxonomies`,
      data
    )
    return res.data
  },

  /**
   * Calls PUT /api/workspaces/{workspaceId}/members/{memberUserId}/profile.
   */
  updateMemberProfile: async (
    workspaceId: string,
    memberUserId: string,
    data: UpdateMemberProfileRequest
  ): Promise<void> => {
    await httpClient.put(
      `/workspaces/${workspaceId}/members/${memberUserId}/profile`,
      data
    )
  },
}
