export type NotificationType = 'OverdueTask' | 'StalledTask' | 'Overload' | 'Bottleneck'
export type NotificationSeverity = 'Low' | 'Medium' | 'High' | 'Critical'
export type ObserverRunStatus = 'Running' | 'Completed' | 'Skipped' | 'Failed'

export interface NotificationPayload {
  runId?: string
  boardId?: string | null
  taskIds?: string[]
  userIds?: string[]
  severity?: NotificationSeverity
  model?: string | null
  tokens?: number | null
  promptTokens?: number | null
  completionTokens?: number | null
  [key: string]: unknown
}

export interface NotificationResponse {
  id: string
  workspaceId: string
  type: NotificationType | string
  title: string
  message: string
  payload: NotificationPayload | null
  isRead: boolean
  createdAt: string
  readAt: string | null
}

export interface NotificationListResponse {
  unreadCount: number
  items: NotificationResponse[]
}

export interface ObserverRunResponse {
  id: string
  workspaceId: string
  status: ObserverRunStatus
  startedAt: string
  finishedAt: string | null
  signalsDetected: number
  findingsWritten: number
  notificationsCreated: number
  aiCalled: boolean
}

export interface ObserverRunFinding {
  type: string
  severity: string
  title: string
  message: string
  taskIds: string[]
  userIds: string[]
}

export interface ObserverRunSummary {
  runId?: string
  aiCalled?: boolean
  model?: string | null
  signalsDetected?: number
  signalsByType?: Record<string, number>
  truncatedSignals?: number
  truncatedByPrompt?: number
  findingsWritten?: number
  notificationsCreated?: number
  promptTokens?: number | null
  completionTokens?: number | null
  durationMs?: number
  skippedReason?: string | null
  error?: string | null
  findings?: ObserverRunFinding[]
  [key: string]: unknown
}

export interface ObserverRunDetailResponse extends ObserverRunResponse {
  summary: ObserverRunSummary | null
  findings: ObserverRunFinding[]
}

export interface ObserverScanResponse {
  runId: string
  workspaceId: string
  status: ObserverRunStatus
  signalsDetected: number
  findingsWritten: number
  notificationsCreated: number
  aiCalled: boolean
}
