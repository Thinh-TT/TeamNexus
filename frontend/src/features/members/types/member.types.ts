export type WorkspaceRole = 'Admin' | 'Manager' | 'Member'

export interface WorkspaceMemberResponse {
  userId: string
  displayName: string
  role: WorkspaceRole
  avatarUrl: string | null
  memberType: 'human' | 'ai_agent'
  email: string | null
  joinedAt: string
  isOwner: boolean
}

export type InvitationStatus = 'Pending' | 'Accepted' | 'Cancelled' | 'Expired'

export interface InvitationResponse {
  id: string
  invitedEmail: string
  invitedRole: WorkspaceRole
  status: InvitationStatus
  invitedByName: string
  expiresAt: string
  createdAt: string
  acceptedByUserId: string | null
  acceptedAt: string | null
  emailSent: boolean
}

export interface InvitationPreviewResponse {
  workspaceId: string
  workspaceName: string
  invitedEmail: string
  invitedRole: WorkspaceRole | string
  invitedByName: string
  expiresAt: string
  status: InvitationStatus | string
}

export interface CreateInvitationRequest {
  email: string
  role?: WorkspaceRole
}

export interface QuickEmailRequest {
  subject: string
  body: string
  recipientUserIds: string[]
}

export interface QuickEmailResult {
  requested: number
  sent: number
  failed: number
  errors: string[]
}
