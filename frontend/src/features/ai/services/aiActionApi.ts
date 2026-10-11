import { httpClient } from '../../../shared/api'
import type {
  AiActionLog,
  AiActionLogDetail,
  AiActionStatus,
  ConfirmSmartSetupRequest,
  RejectAiActionRequest,
} from '../types/aiAction.types'

export const aiActionApi = {
  /**
   * Calls POST /api/boards/{boardId}/smart-setup/confirm to create a pending AI action log.
   * X-XSRF-TOKEN is automatically handled by httpClient.
   */
  confirmSmartSetup: async (
    boardId: string,
    payload: ConfirmSmartSetupRequest
  ): Promise<AiActionLog> => {
    const res = await httpClient.post<AiActionLog>(
      `/boards/${boardId}/smart-setup/confirm`,
      payload
    )
    return res.data
  },

  /**
   * Calls GET /api/boards/{boardId}/ai-actions to list AI action logs for a board.
   */
  listAiActions: async (
    boardId: string,
    params?: { status?: AiActionStatus; take?: number }
  ): Promise<AiActionLog[]> => {
    const res = await httpClient.get<AiActionLog[]>(
      `/boards/${boardId}/ai-actions`,
      { params }
    )
    return res.data
  },

  /**
   * Calls GET /api/workspaces/{workspaceId}/ai-actions (Phase 14.5 A1.1).
   *
   * A separate call from the board-scoped list because the two cannot answer the same question: a log that
   * CREATES a board is stored as `entity_type = 'Workspace'` and can never appear under a board. This is
   * what makes a board-template proposal reviewable before (and without) opening the board it created.
   */
  listWorkspaceAiActions: async (
    workspaceId: string,
    params?: { status?: AiActionStatus; entityType?: string; action?: string; take?: number }
  ): Promise<AiActionLog[]> => {
    const res = await httpClient.get<AiActionLog[]>(
      `/workspaces/${workspaceId}/ai-actions`,
      { params }
    )
    return res.data
  },

  /**
   * Calls GET /api/ai-actions/{logId} to get detailed snapshots of an AI action.
   */
  getAiAction: async (logId: string): Promise<AiActionLogDetail> => {
    const res = await httpClient.get<AiActionLogDetail>(`/ai-actions/${logId}`)
    return res.data
  },

  /**
   * Calls POST /api/ai-actions/{logId}/approve to apply an approved AI action.
   */
  approveAiAction: async (logId: string): Promise<AiActionLogDetail> => {
    const res = await httpClient.post<AiActionLogDetail>(
      `/ai-actions/${logId}/approve`
    )
    return res.data
  },

  /**
   * Calls POST /api/ai-actions/{logId}/reject to reject a pending AI action.
   */
  rejectAiAction: async (
    logId: string,
    note?: string | null
  ): Promise<AiActionLogDetail> => {
    const body: RejectAiActionRequest = note !== undefined && note !== null ? { note } : {}
    const res = await httpClient.post<AiActionLogDetail>(
      `/ai-actions/${logId}/reject`,
      body
    )
    return res.data
  },

  /**
   * Calls POST /api/ai-actions/{logId}/undo to undo an approved AI action.
   */
  undoAiAction: async (logId: string): Promise<AiActionLogDetail> => {
    const res = await httpClient.post<AiActionLogDetail>(
      `/ai-actions/${logId}/undo`
    )
    return res.data
  },
}
