import type { WorkspaceRole } from '../../members/types/member.types'

export interface UserProfileResponse {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  createdAt: string
}

export interface UpdateProfileRequest {
  displayName: string
  avatarUrl?: string | null
}

export interface MyWorkspaceResponse {
  id: string
  name: string
  description: string | null
  role: WorkspaceRole
  ownerId: string
  isOwner: boolean
}
