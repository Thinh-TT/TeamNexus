import type {
  NotificationSeverity,
  NotificationType,
} from '../types/notification.types'

export const getSeverityTagColor = (
  severity?: NotificationSeverity | string
): string => {
  switch (severity?.toLowerCase()) {
    case 'critical':
      return 'error'
    case 'high':
      return 'warning'
    case 'medium':
      return 'processing'
    case 'low':
    default:
      return 'default'
  }
}

export const getTypeText = (type: NotificationType | string): string => {
  switch (type) {
    case 'OverdueTask':
      return 'Quá hạn'
    case 'StalledTask':
      return 'Đình trệ'
    case 'Overload':
      return 'Quá tải'
    case 'Bottleneck':
      return 'Nghẽn việc'
    case 'AgentRunFailed':
      return 'Agent thất bại (AgentRunFailed)'
    case 'AgentAwaitingClarification':
      return 'Agent chờ làm rõ (AgentAwaitingClarification)'
    case 'AgentOutputPending':
      return 'Agent chờ duyệt kết quả (AgentOutputPending)'
    case 'TaskAssigned':
      return 'Được giao thẻ'
    case 'CommentOnTask':
      return 'Bình luận mới'
    case 'WorkspaceInvitation':
      return 'Lời mời workspace'
    default:
      return type
  }
}
