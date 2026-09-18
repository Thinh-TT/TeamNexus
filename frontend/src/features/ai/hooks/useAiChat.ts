import { useCallback, useEffect, useRef, useState } from 'react'
import { httpClient } from '../../../shared/api/httpClient'
import type {
  AiChatMessage,
  AiChatMeta,
} from '../types/aiChat.types'
import type { AiActionLog } from '../types/aiAction.types'
import { canSaveAnswer, toRequestMessages, trimHistory } from '../utils/aiChatHistory'
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
              setStreamingAnswer(streamingAccumulatorRef.current)
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
            content: answerText,
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
