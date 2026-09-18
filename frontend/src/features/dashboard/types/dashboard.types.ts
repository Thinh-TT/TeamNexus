export interface DashboardTaskItem {
  id: string
  boardId: string
  columnId: string
  title: string
  boardName: string
  columnName: string
  dueDate: string | null
  priority: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  createdAt: string
  assigneeId: string | null
  isDone: boolean
  overdueByDays: number | null
}

export interface DashboardTaskBucket {
  count: number
  items: DashboardTaskItem[]
}

export interface DashboardBoardColumnCount {
  columnId: string
  name: string
  isDone: boolean
  count: number
}

export interface DashboardBoardSummary {
  boardId: string
  name: string
  total: number
  done: number
  open: number
  overdue: number
  columns: DashboardBoardColumnCount[]
}

export interface DashboardActivityItem {
  id: string
  boardId: string | null
  entityType: string
  action: string
  authorName: string | null
  createdAt: string
  payload: null
}

export interface DashboardSummary {
  totalTasks: number
  doneTasks: number
  openTasks: number
  overdueTasks: number
  myOpenTasks: number
}

export interface DashboardResponse {
  workspaceId: string
  workspaceName: string
  utcNow: string
  dueSoonDays: number
  myTasks: {
    overdue: DashboardTaskBucket
    dueSoon: DashboardTaskBucket
    recentlyAssigned: DashboardTaskBucket
  }
  boards: DashboardBoardSummary[]
  boardsTruncated: boolean
  recentActivities: DashboardActivityItem[]
  summary: DashboardSummary
  health?: DashboardProjectHealth | null
}

export interface DashboardProjectHealth {
  score: number
  band: 'Tốt' | 'Cần chú ý' | 'Rủi ro' | 'Nghiêm trọng' | string
  components: {
    overdue?: number
    atRisk?: number
    stalled?: number
    aging?: number
    load?: number
    [key: string]: number | undefined
  }
  reasons: string[]
}
