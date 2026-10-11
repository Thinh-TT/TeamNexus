import type { AgentChatAttachment } from '../types/agentChat.types'

/**
 * Attachment list maths for the board chat (Phase 14.5 §B) — pure, so the cap and the toggle are
 * verifiable without rendering anything.
 */

/**
 * How many tasks one question may attach. The server clamps its own
 * `AgentChat:MaxAttachedTasks` to 1..20 and the default is 8, so this constant exists to stop a user
 * from building a request the server would answer with a 400 — not as a second source of truth.
 */
export const MAX_ATTACHED_TASKS = 8

/** True when another task may be attached. */
export function canAttach(list: AgentChatAttachment[], max = MAX_ATTACHED_TASKS): boolean {
  return list.length < max
}

/**
 * Adds a task, ignoring a duplicate. Returns the SAME array reference when nothing changed, so a caller
 * can skip a state update — and, more importantly, so a duplicate drag cannot silently reorder the list.
 */
export function attachTask(
  list: AgentChatAttachment[],
  task: AgentChatAttachment,
  max = MAX_ATTACHED_TASKS
): AgentChatAttachment[] {
  if (list.some((item) => item.taskId === task.taskId) || !canAttach(list, max)) {
    return list
  }

  return [...list, task]
}

/** Removes a task by id. Same "unchanged reference" rule as {@link attachTask}. */
export function detachTask(list: AgentChatAttachment[], taskId: string): AgentChatAttachment[] {
  if (!list.some((item) => item.taskId === taskId)) {
    return list
  }

  return list.filter((item) => item.taskId !== taskId)
}

/** The request shape: only the ids cross the wire. */
export function toRequestIds(list: AgentChatAttachment[]): { taskId: string }[] {
  return list.map((item) => ({ taskId: item.taskId }))
}

/**
 * Human-readable reason a drag was refused, or null when it is allowed. Exists so the panel can say WHY
 * rather than silently ignoring the drop — a card that vanishes into nothing reads like a bug.
 */
export function attachRefusalReason(
  list: AgentChatAttachment[],
  taskId: string,
  max = MAX_ATTACHED_TASKS
): string | null {
  if (list.some((item) => item.taskId === taskId)) {
    return 'Thẻ này đã được gắn vào cuộc trò chuyện.'
  }

  if (!canAttach(list, max)) {
    return `Chỉ gắn được tối đa ${max} thẻ cho một lượt hỏi.`
  }

  return null
}
