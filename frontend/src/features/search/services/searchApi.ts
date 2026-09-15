import { httpClient } from '../../../shared/api/httpClient'
import type { TaskSearchFilters, TaskSearchResponse } from '../types/search.types'

export const searchApi = {
  /**
   * Tìm kiếm task trong workspace
   * GET /api/workspaces/{workspaceId}/tasks/search
   */
  searchTasks: async (
    workspaceId: string,
    filters?: TaskSearchFilters
  ): Promise<TaskSearchResponse> => {
    const query = new URLSearchParams()

    if (filters) {
      if (filters.q !== undefined && filters.q.trim() !== '') {
        query.append('q', filters.q.trim())
      }
      if (filters.boardId !== undefined && filters.boardId !== '') {
        query.append('boardId', filters.boardId)
      }
      if (filters.unassigned) {
        query.append('unassigned', 'true')
      } else if (filters.assigneeId !== undefined && filters.assigneeId !== '') {
        query.append('assigneeId', filters.assigneeId)
      }
      if (filters.labelIds && filters.labelIds.length > 0) {
        query.append('labelIds', filters.labelIds.join(','))
      }
      if (filters.priority) {
        query.append('priority', filters.priority)
      }
      if (filters.dueFrom !== undefined && filters.dueFrom !== '') {
        query.append('dueFrom', filters.dueFrom)
      }
      if (filters.dueTo !== undefined && filters.dueTo !== '') {
        query.append('dueTo', filters.dueTo)
      }
      if (filters.overdue) {
        query.append('overdue', 'true')
      }
      if (typeof filters.includeDone === 'boolean') {
        query.append('includeDone', filters.includeDone ? 'true' : 'false')
      }
      if (typeof filters.take === 'number') {
        query.append('take', String(filters.take))
      }
      if (filters.cursor !== undefined && filters.cursor !== '') {
        query.append('cursor', filters.cursor)
      }
    }

    const queryString = query.toString()
    const url = queryString
      ? `/workspaces/${workspaceId}/tasks/search?${queryString}`
      : `/workspaces/${workspaceId}/tasks/search`

    const res = await httpClient.get<TaskSearchResponse>(url)
    return res.data
  },
}
