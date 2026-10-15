import { httpClient } from '../../../shared/api'
import type { AiActionLog } from '../../ai/types/aiAction.types'
import type {
  CreateKnowledgeDocumentRequest,
  KnowledgeDocumentListResponse,
} from '../types/knowledge.types'

/**
 * HTTP surface of the workspace's semantic memory (Phase 14.7 §B).
 *
 * <p>Note what the two write calls return: an AI action log, never a document. A Manager still has to
 * approve the action, so treating either answer as "done" would be wrong.</p>
 */
export const knowledgeApi = {
  /**
   * `GET /api/workspaces/{workspaceId}/knowledge/documents` — Member+.
   */
  getDocuments: async (workspaceId: string): Promise<KnowledgeDocumentListResponse> => {
    const res = await httpClient.get<KnowledgeDocumentListResponse>(
      `/workspaces/${workspaceId}/knowledge/documents`
    )
    return res.data
  },

  /**
   * `POST /api/workspaces/{workspaceId}/knowledge/documents` — Manager+.
   *
   * <p>Answers 201 with the PENDING action, not with the document. A 400 carries a ready-to-show Vietnamese
   * `{ error }` (empty/oversized fields, or RAG switched off), which the caller surfaces verbatim.</p>
   */
  createDocument: async (
    workspaceId: string,
    request: CreateKnowledgeDocumentRequest
  ): Promise<AiActionLog> => {
    const res = await httpClient.post<AiActionLog>(
      `/workspaces/${workspaceId}/knowledge/documents`,
      request
    )
    return res.data
  },

  /**
   * `DELETE /api/workspaces/{workspaceId}/knowledge/documents/{documentId}` — Manager+.
   *
   * <p>Also answers with a pending action: the document is still there until a Manager approves.</p>
   */
  deleteDocument: async (workspaceId: string, documentId: string): Promise<AiActionLog> => {
    const res = await httpClient.delete<AiActionLog>(
      `/workspaces/${workspaceId}/knowledge/documents/${documentId}`
    )
    return res.data
  },
}
