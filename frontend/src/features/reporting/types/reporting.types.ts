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

// ---- chuỗi thời gian Burndown/Velocity (Giai đoạn 13 §4) -------------------
//
// Hợp đồng khớp 1-1 với `ReportProgressSeriesResponse` của backend
// (`GET /api/workspaces/{id}/reports/progress-series`). Các bất biến dưới đây **đã có test backend**
// chứng minh — client phải dựa vào chúng, không được "sửa" lại:
//   • `days` và `weeks` LOẠI TRỪ nhau: đúng một trong hai có dữ liệu.
//   • `openTasks` = số thẻ mở ở CUỐI ngày và CỐ Ý không trừ `completions` cùng ngày
//     ⇒ `openTasks − completions` là đường lý tưởng và luôn >= 0.
//   • Mọi mốc luôn đủ bucket (kể cả bucket toàn số 0) ⇒ biểu đồ không nhảy cột.

/** `date` = mỗi bucket một ngày; `week` = mỗi bucket một tuần (mốc Thứ Hai). */
export type ReportSeriesMode = 'date' | 'week'

/** Một ngày trong chuỗi. `date` là ngày **theo múi giờ đã chọn** (`YYYY-MM-DD`), không phải mốc UTC. */
export interface ReportDailyProgressPoint {
  date: string
  openTasks: number
  completions: number
  creations: number
}

/** Một tuần trong chuỗi; `weekStart` luôn là **Thứ Hai**. */
export interface ReportWeeklyProgressPoint {
  weekStart: string
  completions: number
  creations: number
  openAtEnd: number
}

export interface ReportVelocityResponse {
  avgCompletionsPerWeek: number
  completedInRange: number
  openAtEnd: number
}

export interface ReportSeriesTruncationResponse {
  bucketCapReached: boolean
  maxBuckets: number
}

export interface ReportProgressSeriesResponse {
  workspaceId: string
  scope: ReportScopeResponse
  period: ReportPeriodResponse
  mode: ReportSeriesMode
  /** 1 khi `mode = 'date'`, 7 khi `mode = 'week'`. */
  bucketDays: number
  /** Rỗng khi `mode = 'week'`. */
  days: ReportDailyProgressPoint[]
  /** Rỗng khi `mode = 'date'`. */
  weeks: ReportWeeklyProgressPoint[]
  velocity: ReportVelocityResponse
  metricDefinitions: Record<string, string>
  truncated: ReportSeriesTruncationResponse
  /** Giá trị **thực dùng sau clamp** — hiển thị nó để một tham số sai không âm thầm đổi số liệu. */
  tzOffsetMinutes: number
}

export interface ReportProgressSeriesParams {
  from?: string
  to?: string
  boardId?: string
  /** UTC + giá trị này, đơn vị phút (VN = `+420`). Xem `timeZoneOffsetMinutes()`. */
  tzOffsetMinutes?: number
}
