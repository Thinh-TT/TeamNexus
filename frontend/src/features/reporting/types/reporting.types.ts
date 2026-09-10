export type ReportScopeType = 'workspace' | 'board'
export type ReportFormat = 'pdf' | 'excel'

export interface ReportPeriodResponse {
  from: string
  to: string
  days: number
  clamped: boolean
}

export interface ReportScopeResponse {
  type: ReportScopeType
  boardId: string | null
  boardName: string | null
}

export interface ReportProgressResponse {
  total: number
  done: number
  open: number
  overdue: number
  donePercent: number
}

export interface ReportPerformanceResponse {
  createdInRange: number
  completedInRange: number
  avgCompletionHours: number | null
  avgLeadTimeHours: number | null
  onTimeRate: number | null
  overdueRate: number
  throughputPerWeek: number
  completedAtMissing: number
}

export interface ReportBoardRowResponse {
  boardId: string
  boardName: string
  total: number
  done: number
  open: number
  overdue: number
  completedInRange: number
  avgCompletionHours: number | null
  onTimeRate: number | null
}

export interface ReportAssigneeRowResponse {
  assigneeId: string | null
  assigneeName: string
  total: number
  done: number
  open: number
  overdue: number
  completedInRange: number
  avgCompletionHours: number | null
  onTimeRate: number | null
}

export interface ReportActionRowResponse {
  action: string
  count: number
}

export interface ReportActivityResponse {
  totalActions: number
  byAction: ReportActionRowResponse[]
  activeUsers: number
  actionsPerDay: number
}

export interface ReportSeverityRowResponse {
  severity: string
  count: number
}

export interface ReportSignalTypeRowResponse {
  type: string
  count: number
}

export interface ReportHealthResponse {
  runsScanned: number
  signalsByType: ReportSignalTypeRowResponse[]
  findingsBySeverity: ReportSeverityRowResponse[]
}

export interface ReportTruncationResponse {
  rowCapReached: boolean
  maxRows: number
}

export interface ReportSummaryResponse {
  workspaceId: string
  workspaceName: string
  scope: ReportScopeResponse
  period: ReportPeriodResponse
  generatedAt: string
  progress: ReportProgressResponse
  performance: ReportPerformanceResponse
  byBoard: ReportBoardRowResponse[]
  byAssignee: ReportAssigneeRowResponse[]
  activity: ReportActivityResponse
  health: ReportHealthResponse
  metricDefinitions: Record<string, string>
  truncated: ReportTruncationResponse
}

// Aliases for convenience
export type ReportSummary = ReportSummaryResponse
export type ReportPeriod = ReportPeriodResponse
export type ReportScope = ReportScopeResponse
export type ReportProgress = ReportProgressResponse
export type ReportPerformance = ReportPerformanceResponse
export type ReportBoardRow = ReportBoardRowResponse
export type ReportAssigneeRow = ReportAssigneeRowResponse
export type ReportActionRow = ReportActionRowResponse
export type ReportActivity = ReportActivityResponse
export type ReportHealth = ReportHealthResponse

export interface ReportBoardOptionResponse {
  id: string
  name: string
  taskCount: number
  isDoneColumns: number
}
export type ReportBoardOption = ReportBoardOptionResponse

export interface ReportExportResult {
  blob: Blob
  fileName: string
  rowCapReached: boolean
}

export interface GetReportSummaryParams {
  boardId?: string
  from?: string
  to?: string
}

export interface DownloadReportParams {
  format: ReportFormat
  boardId?: string
  from?: string
  to?: string
}
