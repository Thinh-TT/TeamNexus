import { httpClient } from '../../../shared/api'
import { saveBlob } from '../../reporting/utils/reportDownload'
import type {
  AgentRunDetailResponse,
  AgentRunResponse,
  AttachmentResponse,
} from '../types/agentRun.types'

export const agentApi = {
  /**
   * Triggers a new AI agent run for a task.
   * Calls POST /api/tasks/{taskId}/agent-runs
   * Returns 202 Accepted with AgentRunResponse (status: "Running").
   * Requires Manager or Admin role.
   */
  startRun: async (taskId: string): Promise<AgentRunResponse> => {
    const res = await httpClient.post<AgentRunResponse>(`/tasks/${taskId}/agent-runs`)
    return res.data
  },

  /**
   * Re-runs an agent run that is awaiting clarification after the user commented an answer.
   * Calls POST /api/tasks/{taskId}/agent-runs/{runId}/rerun
   * Returns 202 Accepted with a new AgentRunResponse.
   * Requires Manager or Admin role.
   */
  rerun: async (taskId: string, runId: string): Promise<AgentRunResponse> => {
    const res = await httpClient.post<AgentRunResponse>(`/tasks/${taskId}/agent-runs/${runId}/rerun`)
    return res.data
  },

  /**
   * Lists agent runs for a specific task.
   * Calls GET /api/tasks/{taskId}/agent-runs?take={take}
   * Sorted descending by startedAt.
   */
  listRuns: async (taskId: string, take = 20): Promise<AgentRunResponse[]> => {
    const res = await httpClient.get<AgentRunResponse[]>(`/tasks/${taskId}/agent-runs`, {
      params: { take },
    })
    return res.data
  },

  /**
   * Retrieves full details for an agent run, including tool call trace and resolution comments.
   * Calls GET /api/agent-runs/{runId}
   */
  getRunDetail: async (runId: string): Promise<AgentRunDetailResponse> => {
    const res = await httpClient.get<AgentRunDetailResponse>(`/agent-runs/${runId}`)
    return res.data
  },

  /**
   * Cancels a currently running agent run.
   * Calls POST /api/agent-runs/{runId}/cancel
   * Returns 200 with updated AgentRunDetailResponse (status: "Failed", stopReason: "Cancelled").
   * Requires Manager or Admin role.
   */
  cancelRun: async (runId: string): Promise<AgentRunDetailResponse> => {
    const res = await httpClient.post<AgentRunDetailResponse>(`/agent-runs/${runId}/cancel`)
    return res.data
  },

  /**
   * Lists all attachments created for a task.
   * Calls GET /api/tasks/{taskId}/attachments
   */
  listAttachments: async (taskId: string): Promise<AttachmentResponse[]> => {
    const res = await httpClient.get<AttachmentResponse[]>(`/tasks/${taskId}/attachments`)
    return res.data
  },

  /**
   * Downloads an attachment file as a blob and prompts save in the browser.
   * Calls GET /api/tasks/{taskId}/attachments/{attachmentId}/download
   */
  downloadAttachment: async (
    taskId: string,
    attachmentId: string,
    fileName?: string
  ): Promise<void> => {
    const res = await httpClient.get<Blob>(
      `/tasks/${taskId}/attachments/${attachmentId}/download`,
      { responseType: 'blob' }
    )
    saveBlob(res.data, fileName ?? 'attachment')
  },
}
