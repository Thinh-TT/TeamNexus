import type { SmartSetupTaskProposal } from './smartSetup.types'

export type AiActionStatus = 'Pending' | 'Approved' | 'Rejected' | 'Undone'
export type AiActionType = 'CreateSubtasks'

export interface ConfirmSmartSetupRequest {
  description: string
  summary: string | null
  tasks: SmartSetupTaskProposal[]
  columnId: string | null
}

export interface AiActionLog {
  id: string
  action: AiActionType | string
  entityType: 'Board' | string
  entityId: string | null
  status: AiActionStatus
  requestedByUserId: string
  requestedByName: string | null
  decidedByUserId: string | null
  decidedByName: string | null
  decidedAt: string | null
  decisionNote: string | null
  taskCount: number
  createdAt: string
  updatedAt: string
}

export interface AiActionAppliedSnapshot {
  entityType: string
  entityId: string | null
  createdTaskIds: string[]
  createdLabelIds: string[]
  warnings: string[]
  appliedAt: string
  undoWarnings?: string[]
}

export interface AiActionAfterSnapshot {
  description: string
  summary: string | null
  columnId: string | null
  tasks: SmartSetupTaskProposal[]
}

export interface AiActionBasis {
  boardId?: string
  boardName?: string
  workspaceId?: string
  descriptionLength?: number
  descriptionExcerpt?: string
  memberCount?: number
  columnCount?: number
  taskCount?: number
  requestedAt?: string
  [key: string]: unknown
}

export interface AiActionLogDetail extends AiActionLog {
  basis: AiActionBasis | null
  beforeSnapshot: unknown | null
  afterSnapshot: AiActionAfterSnapshot | null
  appliedSnapshot: AiActionAppliedSnapshot | null
  createdTaskIds: string[]
}

export interface RejectAiActionRequest {
  note?: string | null
}
