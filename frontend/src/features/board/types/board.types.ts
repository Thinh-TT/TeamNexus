export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export interface LabelResponse {
  id: string
  workspaceId: string
  name: string
  color: string
  createdAt: string
}

export type MemberType = 'human' | 'ai_agent'

export interface WorkspaceMemberResponse {
  userId: string
  displayName: string
  role: 'Admin' | 'Manager' | 'Member'
  avatarUrl: string | null
  memberType: MemberType
}

export interface TaskResponse {
  id: string
  boardId: string
  columnId: string
  title: string
  description: string | null
  position: number
  assigneeId: string | null
  assigneeName: string | null
  dueDate: string | null
  priority: TaskPriority | null
  createdAt: string
  updatedAt: string
  completedAt: string | null
  labels: LabelResponse[]
  commentCount: number
  assigneeIsAiAgent: boolean
  activeAgentRunId: string | null
}

export interface ColumnResponse {
  id: string
  boardId: string
  name: string
  position: number
  isDone: boolean
  isClarification: boolean
  createdAt: string
  updatedAt: string
  tasks: TaskResponse[]
}

export interface BoardResponse {
  id: string
  workspaceId: string
  name: string
  description: string | null
  createdAt: string
  updatedAt: string
  columns: ColumnResponse[]
}

export interface CommentResponse {
  id: string
  taskId: string
  authorId: string
  authorName: string
  content: string
  createdAt: string
  updatedAt: string
}

// ---- Requests ----

export interface CreateBoardRequest {
  name: string
  description?: string | null
}

export interface UpdateBoardRequest {
  name: string
  description?: string | null
}

export interface CreateColumnRequest {
  name: string
  isDone?: boolean
  isClarification?: boolean
}

export interface UpdateColumnRequest {
  name?: string | null
  isDone?: boolean | null
  isClarification?: boolean | null
}

export interface ColumnPositionItem {
  id: string
  position: number
}

export interface ReorderColumnsRequest {
  items: ColumnPositionItem[]
}

export interface CreateTaskRequest {
  columnId: string
  title: string
  description?: string | null
  assigneeId?: string | null
  dueDate?: string | null
  priority?: TaskPriority | null
}

export interface UpdateTaskRequest {
  title: string
  description?: string | null
  assigneeId?: string | null
  dueDate?: string | null
  priority?: TaskPriority | null
}

export interface MoveTaskRequest {
  columnId: string
  position: number
}

export interface CreateLabelRequest {
  name: string
  color: string
}

export interface AttachLabelRequest {
  labelId: string
}

export interface CreateCommentRequest {
  content: string
  mentionUserIds?: string[]
}

export interface UpdateCommentRequest {
  content: string
}

// ---- Real-time Event Payloads ----

export interface TaskMovedEventPayload {
  taskId: string
  fromColumnId: string
  toColumnId: string
  position: number
}

export interface CommentDeletedEventPayload {
  commentId: string
  taskId: string
}

export type HubConnectionStatus = 'connected' | 'connecting' | 'reconnecting' | 'disconnected'
