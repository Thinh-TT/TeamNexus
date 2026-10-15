/**
 * Phase 14.7 §B — the workspace's semantic memory ("kho tri thức").
 *
 * <p>The list endpoint is Member+, while adding and removing are Manager+ AND go through an AI action log:
 * a document is only written once a Manager approves the action. Nothing in this type describes a document
 * that has been created — the create call answers with an action, not with a row.</p>
 */

export type KnowledgeDocumentStatus = 'pending' | 'ready' | 'failed'

export interface KnowledgeDocument {
  id: string
  title: string
  sourceUrl: string | null
  status: KnowledgeDocumentStatus
  chunkCount: number
  /** Present only for a `failed` document; the pipeline's own reason, shown as-is. */
  error: string | null
  createdAt: string
  updatedAt: string
}

export interface KnowledgeDocumentListResponse {
  /**
   * `false` means `Jev:RagEnabled` is off on the server: the list is empty because the feature does not
   * exist right now, NOT because the workspace has no documents. The UI must say so instead of showing an
   * empty table, which would be a lie the user cannot act on.
   */
  enabled: boolean
  items: KnowledgeDocument[]
}

export interface CreateKnowledgeDocumentRequest {
  title: string
  sourceUrl: string | null
  content: string
}

/** The status labels, in the words the UI shows. Kept next to the union so a new status cannot be added
 * without deciding how it reads. */
export const KNOWLEDGE_STATUS_LABELS: Record<KnowledgeDocumentStatus, string> = {
  pending: 'Đang xử lý',
  ready: 'Đã lập chỉ mục',
  failed: 'Lỗi',
}

export const KNOWLEDGE_STATUS_COLORS: Record<KnowledgeDocumentStatus, string> = {
  pending: 'processing',
  ready: 'success',
  failed: 'error',
}

/**
 * The server's own caps. The form enforces them locally so a user cannot build a request that is guaranteed
 * to come back as a 400.
 */
export const KNOWLEDGE_LIMITS = {
  title: 300,
  sourceUrl: 1000,
  content: 400000,
} as const
