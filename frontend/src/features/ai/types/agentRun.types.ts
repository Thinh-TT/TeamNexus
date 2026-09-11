export type MemberType = 'human' | 'ai_agent'

export type AgentRunStatus =
  | 'Running'
  | 'AwaitingClarification'
  | 'AwaitingApproval'
  | 'Completed'
  | 'Failed'

export type AgentStopReason =
  | 'DraftProduced'
  | 'QuestionAsked'
  | 'ToolLimit'
  | 'TimeLimit'
  | 'TokenBudget'
  | 'ProviderError'
  | 'Cancelled'
  | 'TaskChanged'
  | 'InternalError'

export interface AgentRunResponse {
  id: string
  taskId: string
  boardId: string
  agentUserId: string
  agentDisplayName: string
  triggeredByUserId: string
  triggeredByName: string
  status: AgentRunStatus
  stopReason: AgentStopReason | null
  clarificationQuestion: string | null
  aiActionLogId: string | null
  outputKind: 'Comment' | 'Attachment' | null
  error: string | null
  toolCallCount: number
  llmCallCount: number
  promptTokens: number
  completionTokens: number
  totalTokens: number
  startedAt: string
  finishedAt: string | null
  traceTruncated: boolean
}

export interface AgentToolTraceEntryResponse {
  name: string
  arguments: string | null
  resultSummary: string | null
  isError: boolean
  at: string
  durationMs: number
}

export interface AgentRunDetailResponse {
  run: AgentRunResponse
  toolCallTrace: AgentToolTraceEntryResponse[]
  previousRunId: string | null
  previousQuestion: string | null
  resolutionCommentContent: string | null
}

export interface AttachmentResponse {
  id: string
  taskId: string
  fileName: string
  contentType: string
  sizeBytes: number
  createdByUserId: string
  createdByName: string
  sourceRunId: string | null
  createdAt: string
}

export interface AgentRunProgressEvent {
  runId: string
  taskId: string
  boardId: string
  status: AgentRunStatus
  stopReason: AgentStopReason | null
  toolCallCount: number
  totalTokens: number
  clarificationQuestion: string | null
}
