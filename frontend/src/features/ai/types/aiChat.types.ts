export type AiChatRole = 'user' | 'assistant'

export interface AiChatMessage {
  id: string
  role: AiChatRole
  content: string
  createdAt: string
}

export interface AiChatRequestMessage {
  role: AiChatRole
  content: string
}

export interface AiChatMeta {
  taskId: string
  boardId: string
  workspaceId: string
  taskTitle: string
  historyMessages: number
  model: string
}

export interface AiChatDelta {
  text: string
}

export interface AiChatDone {
  answer: string
  promptTokens: number
  completionTokens: number
}

export interface AiChatStreamError {
  error: string
}

export type AiChatStreamEvent =
  | { event: 'meta'; data: AiChatMeta }
  | { event: 'delta'; data: AiChatDelta }
  | { event: 'done'; data: AiChatDone }
  | { event: 'error'; data: AiChatStreamError }

export interface SaveAiChatMessageRequest {
  content: string
}
