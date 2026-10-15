import { useCallback, useEffect, useState } from 'react'
import { knowledgeApi } from '../services/knowledgeApi'
import type { AiActionLog } from '../../ai/types/aiAction.types'
import type {
  CreateKnowledgeDocumentRequest,
  KnowledgeDocument,
} from '../types/knowledge.types'

export interface UseKnowledgeDocumentsReturn {
  documents: KnowledgeDocument[]
  /** `false` when `Jev:RagEnabled` is off — see `KnowledgeDocumentListResponse.enabled`. */
  enabled: boolean
  loading: boolean
  error: string | null
  refetch: () => Promise<void>
  /** Resolves with the PENDING action, or `null` when the server refused (the reason is in `error`). */
  create: (request: CreateKnowledgeDocumentRequest) => Promise<AiActionLog | null>
  remove: (documentId: string) => Promise<AiActionLog | null>
  clearError: () => void
}

/**
 * State for one workspace's knowledge documents (Phase 14.7 §B).
 *
 * <p>Follows the repo's hook convention: `useState` + `useEffect` + `useCallback`, no react-query.</p>
 *
 * <p><b>Writes are requests, not results.</b> `create` and `remove` hand back the AI action the server
 * queued; the document list is deliberately NOT updated from them (no optimistic row, no refetch), because
 * nothing has been written yet and a row that appears before a Manager approves would be a lie.</p>
 */
export function useKnowledgeDocuments(
  workspaceId: string | undefined
): UseKnowledgeDocumentsReturn {
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([])
  // Optimistic default while the first load is in flight: showing "the feature is off" before the server has
  // said so would flash an alarming message on every visit.
  const [enabled, setEnabled] = useState(true)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const refetch = useCallback(async () => {
    if (!workspaceId) {
      return
    }

    setLoading(true)
    setError(null)

    try {
      const res = await knowledgeApi.getDocuments(workspaceId)
      setEnabled(res.enabled)
      // When the feature is off the server sends no items; keeping any previous rows on screen would show
      // documents for a feature that is no longer running.
      setDocuments(res.enabled ? res.items : [])
    } catch (err: unknown) {
      setError(errorMessageOf(err, 'Không thể tải danh sách tài liệu tri thức.'))
    } finally {
      setLoading(false)
    }
  }, [workspaceId])

  useEffect(() => {
    // Deferred into a microtask for the same reason the siblings in this repo do it: `refetch` sets `loading`
    // synchronously, and a sync setState inside an effect starts another render before the fetch even begins.
    Promise.resolve().then(() => {
      void refetch()
    })
  }, [refetch])

  const create = useCallback(
    async (request: CreateKnowledgeDocumentRequest): Promise<AiActionLog | null> => {
      if (!workspaceId) {
        return null
      }

      setError(null)

      try {
        return await knowledgeApi.createDocument(workspaceId, request)
      } catch (err: unknown) {
        setError(errorMessageOf(err, 'Không thể gửi yêu cầu thêm tài liệu.'))
        return null
      }
    },
    [workspaceId]
  )

  const remove = useCallback(
    async (documentId: string): Promise<AiActionLog | null> => {
      if (!workspaceId) {
        return null
      }

      setError(null)

      try {
        return await knowledgeApi.deleteDocument(workspaceId, documentId)
      } catch (err: unknown) {
        setError(errorMessageOf(err, 'Không thể gửi yêu cầu xoá tài liệu.'))
        return null
      }
    },
    [workspaceId]
  )

  const clearError = useCallback(() => setError(null), [])

  return { documents, enabled, loading, error, refetch, create, remove, clearError }
}

/**
 * The server's `{ error }` message, VERBATIM.
 *
 * <p>A 400 from this API is already written for the user ("Bộ nhớ ngữ nghĩa đang tắt…", "Tiêu đề không được
 * để trống…"). Replacing it with a generic sentence would throw away the only part that says which rule was
 * broken, so the fallback is used only when the server sent no message at all.</p>
 */
function errorMessageOf(err: unknown, fallback: string): string {
  const httpErr = err as { response?: { data?: { error?: string } }; message?: string }
  return httpErr?.response?.data?.error || httpErr?.message || fallback
}
