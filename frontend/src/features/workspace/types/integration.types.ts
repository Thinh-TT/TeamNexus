export type WebhookProvider = 'slack' | 'discord'

export type WebhookDeliveryStatus = 'Pending' | 'Sent' | 'Failed'

export type GitHubDeliveryStatus = 'Processed' | 'Ignored' | 'Failed'

export type WebhookEventKey =
  | 'ObserverAlert'
  | 'AgentActionPending'
  | 'ClarificationNeeded'
  | 'TaskAssigned'
  | 'CommentMentioned'
  | 'MemberJoined'

export interface WebhookEventOption {
  key: WebhookEventKey
  label: string
  description: string
  isDefault: boolean
}

/**
 * Canonical outbound webhook subscription keys matching backend `WebhookEventMatcher.cs`.
 */
export const WEBHOOK_EVENT_CATALOG: WebhookEventOption[] = [
  {
    key: 'ObserverAlert',
    label: 'Cảnh báo rủi ro AI Observer',
    description: 'Quá hạn, tắc nghẽn, quá tải thành viên hoặc nguy cơ trễ hạn',
    isDefault: true,
  },
  {
    key: 'AgentActionPending',
    label: 'Đề xuất / Cảnh báo AI Agent',
    description: 'Agent tạo đề xuất chờ duyệt, Recovery Plan hoặc run thất bại',
    isDefault: true,
  },
  {
    key: 'ClarificationNeeded',
    label: 'Thẻ cần làm rõ',
    description: 'AI Agent tạm dừng và đặt câu hỏi làm rõ thông tin thẻ',
    isDefault: true,
  },
  {
    key: 'TaskAssigned',
    label: 'Giao việc mới',
    description: 'Thông báo khi thẻ được phân công cho thành viên',
    isDefault: false,
  },
  {
    key: 'CommentMentioned',
    label: 'Nhắc tên / Bình luận công việc',
    description: 'Khi có bình luận mới hoặc nhắc tên @tên trong thẻ',
    isDefault: false,
  },
  {
    key: 'MemberJoined',
    label: 'Thành viên mới tham gia',
    description: 'Khi thành viên chấp nhận lời mời vào không gian làm việc',
    isDefault: false,
  },
]

export const DEFAULT_WEBHOOK_EVENTS: WebhookEventKey[] = WEBHOOK_EVENT_CATALOG.filter(
  (item) => item.isDefault
).map((item) => item.key)

export interface WorkspaceWebhookStatsResponse {
  sentLast24h: number
  failedLast24h: number
  pendingCount: number
}

export interface WorkspaceWebhookEndpointResponse {
  id: string
  workspaceId: string
  name: string
  provider: WebhookProvider | string
  maskedTargetUrl: string
  subscribedEvents: string[]
  subscribedEventsCsv: string
  isActive: boolean
  failureCount: number
  lastSuccessAt: string | null
  lastFailureAt: string | null
  lastError: string | null
  createdByUserId: string
  createdAt: string
  updatedAt: string
}

export interface WorkspaceWebhookListResponse {
  stats: WorkspaceWebhookStatsResponse
  endpoints: WorkspaceWebhookEndpointResponse[]
}

export interface CreateWorkspaceWebhookRequest {
  name: string
  provider: WebhookProvider
  targetUrl: string
  subscribedEvents?: string[]
}

export interface UpdateWorkspaceWebhookRequest {
  name?: string
  targetUrl?: string
  subscribedEvents?: string[]
  isActive?: boolean
}

export interface WebhookTestResultResponse {
  success: boolean
  deliveryId: string
  status: WebhookDeliveryStatus | string
  httpStatus: number | null
  error: string | null
  testedAt: string
}

export interface WebhookDeliveryItemResponse {
  id: string
  endpointId: string
  eventType: string
  status: WebhookDeliveryStatus | string
  attemptCount: number
  maxAttempts: number
  lastHttpStatus: number | null
  lastError: string | null
  nextAttemptAt: string
  sentAt: string | null
  createdAt: string
}

export interface WorkspaceGitHubIntegrationResponse {
  id: string
  workspaceId: string
  webhookToken: string
  webhookPayloadUrl: string
  secretHint: string
  isEnabled: boolean
  defaultBoardId: string | null
  issueLabelFilter: string | null
  autoMoveOnPr: boolean
  autoCreateTasksFromIssues: boolean
  syncIssueComments: boolean
  createdAt: string
  updatedAt: string
  webhookSecretPlaintext?: string | null
}

export interface UpsertWorkspaceGitHubIntegrationRequest {
  isEnabled?: boolean
  defaultBoardId?: string | null
  issueLabelFilter?: string | null
  autoMoveOnPr?: boolean
  autoCreateTasksFromIssues?: boolean
  syncIssueComments?: boolean
}

export interface RotateGitHubSecretResponse {
  secretHint: string
  webhookSecretPlaintext: string
}

export interface GitHubWebhookDeliveryResponse {
  id: string
  deliveryId: string
  eventName: string
  action: string | null
  repositoryFullName: string | null
  status: GitHubDeliveryStatus | string
  summary: string | null
  errorMessage: string | null
  receivedAt: string
}
