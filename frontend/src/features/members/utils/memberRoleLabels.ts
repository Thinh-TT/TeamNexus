import type { WorkspaceMemberResponse } from '../types/member.types'

export const getRoleLabel = (role?: string | null): string => {
  switch (role) {
    case 'Admin':
      return 'Quản trị viên'
    case 'Manager':
      return 'Quản lý'
    case 'Member':
      return 'Thành viên'
    default:
      return role ?? 'Thành viên'
  }
}

export const getRoleTagColor = (role?: string | null): string => {
  switch (role) {
    case 'Admin':
      return 'red'
    case 'Manager':
      return 'gold'
    case 'Member':
      return 'blue'
    default:
      return 'default'
  }
}

export const getMemberTypeLabel = (type?: string | null): string => {
  if (type === 'ai_agent') {
    return 'AI Agent'
  }
  return 'Người dùng'
}

export interface MemberActionPermission {
  disabled: boolean
  reason?: string
}

export const isMemberActionDisabled = (
  member: WorkspaceMemberResponse,
  currentUserId?: string
): MemberActionPermission => {
  if (member.isOwner) {
    return {
      disabled: true,
      reason: 'Không thể thao tác trên Chủ sở hữu workspace',
    }
  }

  if (currentUserId && member.userId === currentUserId) {
    return {
      disabled: true,
      reason: 'Không thể tự thay đổi vai trò hoặc xoá chính mình',
    }
  }

  if (member.memberType === 'ai_agent') {
    return {
      disabled: true,
      reason: 'Không thể thao tác trên AI Agent',
    }
  }

  return {
    disabled: false,
  }
}
