export type WorkspaceRole = 'Admin' | 'Manager' | 'Member'

export interface WorkspaceSummary {
  id: string
  name: string
  description: string | null
  role: string
  ownerId: string
  isOwner: boolean
}

export interface WorkspaceDetail {
  id: string
  name: string
  description: string | null
  createdAt: string
  updatedAt: string
  ownerId: string
  ownerDisplayName: string
  memberCount: number
  boardCount: number
  currentUserRole: string
}

export interface UpdateWorkspaceRequest {
  name: string
  description: string | null
}

export interface TransferOwnershipRequest {
  newOwnerId: string
}

export interface WorkspaceActivityItem {
  id: string
  boardId: string | null
  userId: string | null
  userDisplayName: string | null
  entityType: string
  entityId: string | null
  action: string
  payload: Record<string, unknown> | null
  createdAt: string
}

export interface WorkspaceActivityPage {
  items: WorkspaceActivityItem[]
  nextCursor: string | null
  hasMore: boolean
}

export interface GetWorkspaceActivityParams {
  boardId?: string
  entityType?: string
  action?: string
  take?: number
  before?: string
}
