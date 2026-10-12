import { useCallback, useEffect, useRef, useState } from 'react'
import { httpClient } from '../../../shared/api/httpClient'
import type {
  AiChatMessage,
  AiChatMeta,
} from '../types/aiChat.types'
import type { AgentChatStoredMessage } from '../types/agentChat.types'
import type { AiActionLog } from '../types/aiAction.types'
import { canSaveAnswer, toRequestMessages, trimHistory } from '../utils/aiChatHistory'
import { redactIdentifiers } from '../utils/redactIdentifiers'
import { streamAiChat, type StreamHttpError } from '../utils/aiChatStream'

export type AiChatStatus = 'idle' | 'streaming' | 'saving' | 'error'

export interface UseAiChatReturn {
  messages: AiChatMessage[]
  streamingAnswer: string
  meta: AiChatMeta | null
  status: AiChatStatus
  error: string | null
  httpStatus: number | null
  ask: (content: string) => Promise<void>
  cancel: () => void
  saveAnswer: (customContent?: string) => Promise<AiActionLog | null>
  clearError: () => void
}

/** How many stored turns the task tab loads on open — the board panel's page size, for the same reason. */
const TRANSCRIPT_PAGE_SIZE = 50

/**
 * State for one task's AI chat (Phase 14 §2.2, transcript added in Phase 14.6 §A3).
 *
 * <p><b>What changed in 14.6.</b> The transcript used to live only in this hook's state, so the task tab lost
 * its conversation on every reload and no teammate could ever see it. It is now read from
 * <c>agent_chat_messages</c> on open (<c>task_id</c> instead of <c>board_id</c> — the same table the board
 * chat uses), and the server stores each exchange as it is answered.</p>
 */
export function useAiChat(taskId: string): UseAiChatReturn {
  const [messages, setMessages] = useState<AiChatMessage[]>([])
  const [streamingAnswer, setStreamingAnswer] = useState<string>('')
  const [meta, setMeta] = useState<AiChatMeta | null>(null)
  const [status, setStatus] = useState<AiChatStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const abortControllerRef = useRef<AbortController | null>(null)
  const streamingAccumulatorRef = useRef<string>('')
  const messagesRef = useRef<AiChatMessage[]>(messages)

  useEffect(() => {
    messagesRef.current = messages
  }, [messages])

  // Load the stored conversation for this task (Phase 14.6 §A3). Re-running on a task change is the point:
  // two cards must never show each other's history.
  useEffect(() => {
    if (!taskId) {
      return
    }

    let ignore = false

    const run = async () => {
      try {
        const res = await httpClient.get<AgentChatStoredMessage[]>(
          `/tasks/${taskId}/agent-chat/messages`,
          { params: { take: TRANSCRIPT_PAGE_SIZE } }
        )

        if (ignore) {
          return
        }

        // Never overwrite a turn the user has already produced while this was in flight: the history is
        // context, and a slow response must not eat a question someone just asked.
        if (messagesRef.current.length > 0) {
          return
        }

        const stored = (res.data ?? []).map(toLocalMessage)
        messagesRef.current = stored
        setMessages(stored)
      } catch (err: unknown) {
        // A failed load must not block the chat — the panel stays usable and simply starts empty. The
        // conversation is the SERVER's now, so this is a real degradation worth recording.
        if (!ignore) {
          console.warn('[useAiChat] Không tải được lịch sử hội thoại của thẻ', err)
        }
      }
    }

    void run()

    return () => {
      ignore = true
    }
  }, [taskId])

  useEffect(() => {
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
      }
    }
  }, [])

  const clearError = useCallback(() => {
    setError(null)
    setHttpStatus(null)
    if (status === 'error') {
      setStatus('idle')
    }
  }, [status])

  const cancel = useCallback(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort()
      abortControllerRef.current = null
    }
    streamingAccumulatorRef.current = ''
    setStreamingAnswer('')
    setStatus('idle')
  }, [])

  const ask = useCallback(
    async (content: string) => {
      if (!content || !content.trim() || status === 'streaming') {
        return
      }

      setError(null)
      setHttpStatus(null)

      const userMessage: AiChatMessage = {
        id: crypto.randomUUID ? crypto.randomUUID() : `msg-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
        role: 'user',
        content: content.trim(),
        createdAt: new Date().toISOString(),
      }

      const nextMessages = [...messagesRef.current, userMessage]
      setMessages(nextMessages)
      messagesRef.current = nextMessages

      setStreamingAnswer('')
      streamingAccumulatorRef.current = ''
      setStatus('streaming')

      const controller = new AbortController()
      abortControllerRef.current = controller

      const trimmedForRequest = trimHistory(nextMessages, 10)
      const requestPayload = toRequestMessages(trimmedForRequest)

      try {
        let finalAnswerFromDone: string | null = null

        await streamAiChat({
          taskId,
          messages: requestPayload,
          signal: controller.signal,
          onEvent: ({ event, data }) => {
            if (event === 'meta' && data) {
              setMeta(data)
            } else if (event === 'delta' && data?.text) {
              streamingAccumulatorRef.current += data.text
              // Phase 14.6 §A2: redact the accumulated text, never one delta — an identifier split across
              // two frames is only recognisable once both halves have arrived.
              setStreamingAnswer(redactIdentifiers(streamingAccumulatorRef.current))
            } else if (event === 'done' && data) {
              if (data.answer) {
                finalAnswerFromDone = data.answer
              }
            }
          },
        })

        if (controller.signal.aborted) {
          return
        }

        const answerText = finalAnswerFromDone || streamingAccumulatorRef.current
        if (answerText) {
          const assistantMessage: AiChatMessage = {
            id: crypto.randomUUID
              ? crypto.randomUUID()
              : `msg-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
            role: 'assistant',
            // Phase 14.6 §A2: the turn kept in the transcript is the redacted prose, so "Lưu thành bình
            // luận" can never post an identifier the user never saw. The server redacts again before it
            // stores the turn (a reload must not resurrect what was hidden).
            content: redactIdentifiers(answerText),
            createdAt: new Date().toISOString(),
          }
          setMessages((prev) => [...prev, assistantMessage])
        }

        setStreamingAnswer('')
        streamingAccumulatorRef.current = ''
        setStatus('idle')
      } catch (err: unknown) {
        if (controller.signal.aborted) {
          return
        }

        const streamError = err as StreamHttpError
        const errorMsg = streamError.message || 'Không thể kết nối đến dịch vụ AI'
        setError(errorMsg)
        setHttpStatus(streamError.status ?? null)
        setStatus('error')
        // Retain current streamingAnswer in UI as per D8 so partial text is visible
      } finally {
        abortControllerRef.current = null
      }
    },
    [status, taskId]
  )

  const saveAnswer = useCallback(
    async (customContent?: string): Promise<AiActionLog | null> => {
      let contentToSave = customContent
      if (!contentToSave) {
        // Default to the last assistant message
        const lastAssistant = [...messagesRef.current].reverse().find((m) => m.role === 'assistant')
        contentToSave = lastAssistant?.content || streamingAnswer
      }

      if (!canSaveAnswer(contentToSave)) {
        const errorMsg = 'Nội dung câu trả lời không hợp lệ hoặc vượt quá 2000 ký tự'
        setError(errorMsg)
        setStatus('error')
        return null
      }

      setStatus('saving')
      setError(null)
      try {
        const res = await httpClient.post<AiActionLog>(`/tasks/${taskId}/ai-chat/message`, {
          content: contentToSave,
        })
        setStatus('idle')
        return res.data
      } catch (err: any) {
        const errorMsg =
          err?.response?.data?.error ||
          err?.message ||
          'Không thể lưu câu trả lời thành bình luận'
        setError(errorMsg)
        setHttpStatus(err?.response?.status ?? null)
        setStatus('error')
        throw err
      }
    },
    [streamingAnswer, taskId]
  )

  return {
    messages,
    streamingAnswer,
    meta,
    status,
    error,
    httpStatus,
    ask,
    cancel,
    saveAnswer,
    clearError,
  }
}

/**
 * A stored turn as local component state.
 *
 * <p>Assistant prose is redacted on the way in for the same reason it is redacted on the way out: turns
 * written before §A2 may still hold an identifier, and merely reloading a conversation must not put it back
 * on screen. A user's own words are never rewritten.</p>
 */
function toLocalMessage(stored: AgentChatStoredMessage): AiChatMessage {
  return {
    id: stored.id,
    role: stored.role,
    content: stored.role === 'assistant' ? redactIdentifiers(stored.content) : stored.content,
    createdAt: stored.createdAt,
  }
}
