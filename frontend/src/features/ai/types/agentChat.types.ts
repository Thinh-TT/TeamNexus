import type { AiActionLog } from './aiAction.types'
import type { AiChatRequestMessage, AiChatRole } from './aiChat.types'

/**
 * The three write actions the board chat may propose (Phase 14.5 §B). The union is closed on purpose:
 * the server rejects anything else, so a typo here is a compile error rather than a runtime 400.
 */
export type AgentChatProposalKind = 'CreateSubtasks' | 'UpdateTaskFields' | 'MoveTaskToColumn'

/** One task the user dragged into the panel, as the chat service needs it. */
export interface AgentChatAttachment {
  taskId: string
  /** Display only — the server reads the live title, never this copy. */
  title: string
}

/** One task as the request body carries it: the id is the only thing that crosses the wire. */
export interface AgentChatAttachmentRequest {
  taskId: string
}

/** A stored turn as the API returns it. `id` is the database row, so reloads keep stable keys. */
export interface AgentChatStoredMessage {
  id: string
  role: AiChatRole
  content: string
  attachments: AgentChatAttachment[]
  createdAt: string
}

/** A turn held in component state (a stored row, or one just produced by streaming). */
export interface AgentChatMessage {
  id: string
  role: AiChatRole
  content: string
  createdAt: string
}

export interface AgentChatMeta {
  taskId: string
  boardId: string
  workspaceId: string
  taskTitle: string
  historyMessages: number
  model: string
}

export interface AgentChatSession {
  boardId: string
  hasMessages: boolean
  model: string
}

export interface AgentChatSendRequest {
  messages: AiChatRequestMessage[]
  attachedTaskIds: AgentChatAttachmentRequest[]
}

/**
 * A validated proposal, as the server echoed it back inside the answer. The client only ever PREVIEWS
 * this — it never builds one — so the fields are deliberately loose (nullable) rather than a mirror of
 * the server's discriminated payload.
 */
export interface AgentChatProposal {
  kind: AgentChatProposalKind
  summary: string
  payload: {
    taskId?: string | null
    columnId?: string | null
    title?: string | null
    description?: string | null
    priority?: string | null
    dueDate?: string | null
  }
}

export interface AgentChatProposalEnvelope {
  proposal: AgentChatProposal
}

export interface SaveAgentChatAnswerRequest {
  content: string
  taskId: string | null
}

export interface SubmitAgentChatProposalRequest {
  proposalJson: string
  attachedTaskIds: AgentChatAttachmentRequest[]
}

/** The label shown on the action card and in the history drawer. */
export const AGENT_CHAT_PROPOSAL_LABELS: Record<AgentChatProposalKind, string> = {
  CreateSubtasks: 'Tạo thẻ mới',
  UpdateTaskFields: 'Cập nhật thẻ',
  MoveTaskToColumn: 'Di chuyển thẻ',
}

export type { AiActionLog }
