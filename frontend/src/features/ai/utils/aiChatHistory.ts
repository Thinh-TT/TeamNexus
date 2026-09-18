import type { AiChatMessage, AiChatRequestMessage } from '../types/aiChat.types'

/**
 * Trims conversation history to retain only the most recent N messages,
 * ensuring client requests never breach server bounds (12 messages / 8000 chars).
 */
export function trimHistory(
  messages: AiChatMessage[],
  maxMessages = 10
): AiChatMessage[] {
  if (messages.length <= maxMessages) {
    return [...messages]
  }
  return messages.slice(messages.length - maxMessages)
}

/**
 * Maps frontend AiChatMessage list to request payload format { role, content }.
 */
export function toRequestMessages(
  messages: AiChatMessage[]
): AiChatRequestMessage[] {
  return messages.map((m) => ({
    role: m.role,
    content: m.content,
  }))
}

/**
 * Validates whether an assistant answer can be saved as a comment.
 * Task comments limit is 2000 characters.
 */
export function canSaveAnswer(text: string, max = 2000): boolean {
  if (!text || typeof text !== 'string') return false
  const trimmed = text.trim()
  return trimmed.length > 0 && text.length <= max
}
