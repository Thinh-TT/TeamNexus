import type { TaskResponse } from '../../board/types/board.types'

export interface TaskSearchItem {
  task: TaskResponse
  boardName: string
  columnName: string
  isDoneColumn: boolean
}

export interface TaskSearchResponse {
  items: TaskSearchItem[]
  nextCursor: string | null
  hasMore: boolean
  hasQuery: boolean
}

export interface TaskSearchFilters {
  q?: string
  boardId?: string
  assigneeId?: string
  unassigned?: boolean
  labelIds?: string[]
  priority?: 'Low' | 'Medium' | 'High' | 'Urgent'
  dueFrom?: string
  dueTo?: string
  overdue?: boolean
  includeDone?: boolean
  take?: number
  cursor?: string
}
