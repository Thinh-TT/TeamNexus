import { httpClient } from '../../../shared/api'
import type {
  AttachLabelRequest,
  BoardResponse,
  ColumnResponse,
  CommentResponse,
  CreateBoardRequest,
  CreateColumnRequest,
  CreateCommentRequest,
  CreateLabelRequest,
  CreateTaskRequest,
  LabelResponse,
  MoveTaskRequest,
  ReorderColumnsRequest,
  TaskResponse,
  UpdateBoardRequest,
  UpdateColumnRequest,
  UpdateCommentRequest,
  UpdateTaskRequest,
} from '../types/board.types'

export const boardApi = {
  // ---- Boards ----
  getBoards: async (workspaceId: string): Promise<BoardResponse[]> => {
    const res = await httpClient.get<BoardResponse[]>(`/workspaces/${workspaceId}/boards`)
    return res.data
  },

  getBoard: async (workspaceId: string, boardId: string): Promise<BoardResponse> => {
    const res = await httpClient.get<BoardResponse>(`/workspaces/${workspaceId}/boards/${boardId}`)
    return res.data
  },

  createBoard: async (workspaceId: string, data: CreateBoardRequest): Promise<BoardResponse> => {
    const res = await httpClient.post<BoardResponse>(`/workspaces/${workspaceId}/boards`, data)
    return res.data
  },

  updateBoard: async (
    workspaceId: string,
    boardId: string,
    data: UpdateBoardRequest
  ): Promise<BoardResponse> => {
    const res = await httpClient.put<BoardResponse>(
      `/workspaces/${workspaceId}/boards/${boardId}`,
      data
    )
    return res.data
  },

  deleteBoard: async (workspaceId: string, boardId: string): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}/boards/${boardId}`)
  },

  // ---- Columns ----
  getColumns: async (boardId: string): Promise<ColumnResponse[]> => {
    const res = await httpClient.get<ColumnResponse[]>(`/boards/${boardId}/columns`)
    return res.data
  },

  createColumn: async (boardId: string, data: CreateColumnRequest): Promise<ColumnResponse> => {
    const res = await httpClient.post<ColumnResponse>(`/boards/${boardId}/columns`, data)
    return res.data
  },

  updateColumn: async (
    boardId: string,
    columnId: string,
    data: UpdateColumnRequest
  ): Promise<ColumnResponse> => {
    const res = await httpClient.put<ColumnResponse>(`/boards/${boardId}/columns/${columnId}`, data)
    return res.data
  },

  reorderColumns: async (boardId: string, data: ReorderColumnsRequest): Promise<void> => {
    await httpClient.put(`/boards/${boardId}/columns/reorder`, data)
  },

  deleteColumn: async (boardId: string, columnId: string): Promise<void> => {
    await httpClient.delete(`/boards/${boardId}/columns/${columnId}`)
  },

  // ---- Tasks ----
  getTasks: async (boardId: string, columnId?: string): Promise<TaskResponse[]> => {
    const params = columnId ? { columnId } : undefined
    const res = await httpClient.get<TaskResponse[]>(`/boards/${boardId}/tasks`, { params })
    return res.data
  },

  getTask: async (boardId: string, taskId: string): Promise<TaskResponse> => {
    const res = await httpClient.get<TaskResponse>(`/boards/${boardId}/tasks/${taskId}`)
    return res.data
  },

  createTask: async (boardId: string, data: CreateTaskRequest): Promise<TaskResponse> => {
    const res = await httpClient.post<TaskResponse>(`/boards/${boardId}/tasks`, data)
    return res.data
  },

  updateTask: async (
    boardId: string,
    taskId: string,
    data: UpdateTaskRequest
  ): Promise<TaskResponse> => {
    const res = await httpClient.put<TaskResponse>(`/boards/${boardId}/tasks/${taskId}`, data)
    return res.data
  },

  moveTask: async (
    boardId: string,
    taskId: string,
    data: MoveTaskRequest
  ): Promise<TaskResponse> => {
    const res = await httpClient.put<TaskResponse>(`/boards/${boardId}/tasks/${taskId}/move`, data)
    return res.data
  },

  deleteTask: async (boardId: string, taskId: string): Promise<void> => {
    await httpClient.delete(`/boards/${boardId}/tasks/${taskId}`)
  },

  // ---- Labels ----
  getLabels: async (workspaceId: string): Promise<LabelResponse[]> => {
    const res = await httpClient.get<LabelResponse[]>(`/workspaces/${workspaceId}/labels`)
    return res.data
  },

  createLabel: async (workspaceId: string, data: CreateLabelRequest): Promise<LabelResponse> => {
    const res = await httpClient.post<LabelResponse>(`/workspaces/${workspaceId}/labels`, data)
    return res.data
  },

  deleteLabel: async (workspaceId: string, labelId: string): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}/labels/${labelId}`)
  },

  attachLabel: async (taskId: string, labelId: string): Promise<void> => {
    const data: AttachLabelRequest = { labelId }
    await httpClient.post(`/tasks/${taskId}/labels`, data)
  },

  detachLabel: async (taskId: string, labelId: string): Promise<void> => {
    await httpClient.delete(`/tasks/${taskId}/labels/${labelId}`)
  },

  // ---- Comments ----
  getComments: async (taskId: string): Promise<CommentResponse[]> => {
    const res = await httpClient.get<CommentResponse[]>(`/tasks/${taskId}/comments`)
    return res.data
  },

  createComment: async (taskId: string, data: CreateCommentRequest): Promise<CommentResponse> => {
    const res = await httpClient.post<CommentResponse>(`/tasks/${taskId}/comments`, data)
    return res.data
  },

  updateComment: async (
    taskId: string,
    commentId: string,
    data: UpdateCommentRequest
  ): Promise<CommentResponse> => {
    const res = await httpClient.put<CommentResponse>(
      `/tasks/${taskId}/comments/${commentId}`,
      data
    )
    return res.data
  },

  deleteComment: async (taskId: string, commentId: string): Promise<void> => {
    await httpClient.delete(`/tasks/${taskId}/comments/${commentId}`)
  },
}
