import type { WorkspaceActivityItem } from '../types/workspace.types'

export const KNOWN_ACTIVITY_ACTIONS: Record<string, string> = {
  TaskCreated: 'đã tạo thẻ',
  TaskUpdated: 'đã cập nhật thẻ',
  TaskMoved: 'đã chuyển thẻ',
  TaskCompleted: 'đã hoàn thành thẻ',
  TaskDeleted: 'đã xoá thẻ',
  CommentAdded: 'đã bình luận',
  WorkspaceUpdated: 'đã cập nhật workspace',
  WorkspaceOwnerTransferred: 'đã chuyển quyền sở hữu',
  WorkspaceDeleted: 'đã xoá workspace',
}

/**
 * Lấy nhãn tiếng Việt cho một action.
 * Nếu action không nằm trong danh sách đã biết, fallback trả về nguyên văn chuỗi action (không ném).
 */
export function getActivityActionLabel(action: string): string {
  if (!action) return ''
  return KNOWN_ACTIVITY_ACTIONS[action] || action
}

/**
 * Lấy tên hiển thị của người thực hiện.
 * Nếu userId hoặc userDisplayName là null -> trả về "Hệ thống".
 */
export function getActivityActorName(item: Pick<WorkspaceActivityItem, 'userId' | 'userDisplayName'>): string {
  if (!item.userId || !item.userDisplayName) {
    return 'Hệ thống'
  }
  return item.userDisplayName
}

/**
 * Lấy thông tin bổ sung ngắn gọn từ payload của sự kiện (nếu có).
 * Chịu được payload null/undefined hoặc schema lạ mà không crash.
 */
export function getActivityPayloadSummary(item: WorkspaceActivityItem): string | null {
  if (!item.payload) return null

  try {
    const p = item.payload
    if (item.action === 'TaskMoved') {
      const fromName = p.fromColumnName as string | undefined
      const toName = p.toColumnName as string | undefined
      if (fromName && toName) {
        return `từ "${fromName}" sang "${toName}"`
      }
    }

    if (item.action === 'TaskUpdated') {
      const parts: string[] = []
      if (p.priority) parts.push(`ưu tiên ${p.priority}`)
      if (p.dueDate) parts.push('hạn chót')
      if (p.descriptionChanged) parts.push('mô tả')
      if (parts.length > 0) {
        return parts.join(', ')
      }
    }

    if (item.action === 'WorkspaceUpdated' && p.nameChanged) {
      return 'đã đổi tên'
    }
  } catch {
    return null
  }

  return null
}
