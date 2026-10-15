import { useCallback, useEffect, useRef, useState } from 'react'
import { MAX_ATTACHED_TASKS, attachTask, detachTask, toRequestIds } from '../utils/agentChatAttachments'
import { parseProposal, splitProposal } from '../utils/agentChatProposal'
import { redactAnswerProse, redactIdentifiers } from '../utils/redactIdentifiers'
import { agentChatApi } from '../services/agentChatApi'
import { trimHistory, toRequestMessages } from '../utils/aiChatHistory'
import type { AiActionLog } from '../types/aiAction.types'
import type {
  AgentChatAttachment,
  AgentChatMessage,
  AgentChatPersona,
  AgentChatProposal,
  AgentChatStoredMessage,
} from '../types/agentChat.types'

export type AgentChatStatus = 'idle' | 'loading' | 'streaming' | 'saving' | 'confirming' | 'error'

export interface UseAgentChatReturn {
  messages: AgentChatMessage[]
  streamingAnswer: string
  attachments: AgentChatAttachment[]
  proposal: AgentChatProposal | null
  proposalJson: string | null
  model: string | null
  /** Phase 14.7 §B: the server's persona catalogue, empty when the server sent none. */
  personas: AgentChatPersona[]
  /** The persona the NEXT turn will be answered as; `null` means "let the server decide". */
  persona: string | null
  setPersona: (key: string | null) => void
  status: AgentChatStatus
  error: string | null
  httpStatus: number | null
  ask: (content: string) => Promise<void>
  cancel: () => void
  attach: (task: AgentChatAttachment) => void
  detach: (taskId: string) => void
  clearAttachments: () => void
  saveAnswer: (content?: string, taskId?: string | null) => Promise<AiActionLog | null>
  submitProposal: () => Promise<AiActionLog | null>
  clearError: () => void
}

/**
 * State for one board's AI Agent Chat (Phase 14.5 §B).
 *
 * <p><b>Unlike the task chat, the transcript comes from the server.</b> The board conversation is
 * persisted (`agent_chat_messages`), so the panel loads it on open and a reload does not lose history —
 * that is the whole reason this phase added a table. The client still REPLAYS the conversation on every
 * turn, which keeps one request shape for both chats and lets the server judge a request on its own.</p>
 *
 * <p>Follows the repo's hook convention: `useState` + `useEffect`, no react-query.</p>
 */
export function useAgentChat(boardId: string | undefined): UseAgentChatReturn {
  const [messages, setMessages] = useState<AgentChatMessage[]>([])
  const [streamingAnswer, setStreamingAnswer] = useState('')
  const [attachments, setAttachments] = useState<AgentChatAttachment[]>([])
  const [proposal, setProposal] = useState<AgentChatProposal | null>(null)
  const [proposalJson, setProposalJson] = useState<string | null>(null)
  const [model, setModel] = useState<string | null>(null)
  const [personas, setPersonas] = useState<AgentChatPersona[]>([])
  const [persona, setPersonaState] = useState<string | null>(null)
  const [status, setStatus] = useState<AgentChatStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const abortControllerRef = useRef<AbortController | null>(null)
  const accumulatorRef = useRef('')
  const messagesRef = useRef<AgentChatMessage[]>([])
  const attachmentsRef = useRef<AgentChatAttachment[]>([])
  // The persona is read INSIDE `ask` when the request is built. A ref keeps that read current without
  // putting `persona` into `ask`'s dependency list, which would rebuild the callback on every keystroke of
  // a Select — and, worse, could let a turn that is already streaming see a persona chosen after it began.
  const personaRef = useRef<string | null>(null)

  const setPersona = useCallback((key: string | null) => {
    // A blank key is not a persona: it must mean "server default", never `?persona=`.
    const next = key && key.trim() ? key : null
    personaRef.current = next
    setPersonaState(next)
  }, [])

  useEffect(() => {
    messagesRef.current = messages
  }, [messages])

  useEffect(() => {
    attachmentsRef.current = attachments
  }, [attachments])

  // Load the stored conversation when the panel binds to a board. Re-running on a board change is the
  // point: two boards must never show each other's history.
  useEffect(() => {
    let ignore = false

    // No board bound (the panel was mounted without one): nothing to load, and the local state has nothing
    // to reset because it starts empty and is only ever filled from the board it is bound to. The previous
    // version cleared the lists here, which meant a synchronous setState inside an effect — a second render
    // before any work happened.
    if (!boardId) {
      return () => {
        ignore = true
      }
    }

    // The initial "loading" state is set inside the async body rather than synchronously in the effect:
    // a sync setState inside an effect starts another render before the fetch even begins, which the repo's
    // lint rule (correctly) rejects. Awaiting first keeps the render count honest.
    const run = async () => {
      setStatus('loading')
      setError(null)

      try {
        const [session, stored] = await Promise.all([
          agentChatApi.getSession(boardId),
          agentChatApi.listMessages(boardId, { take: 50 }),
        ])

        if (ignore) {
          return
        }

        setModel(session.model)

        // Phase 14.7 §B: the catalogue and its default arrive with the session, so the picker is populated
        // before the first question. `defaultPersona` is only adopted when the catalogue actually offers it —
        // sending back a key the server would reject is a 400 the user never asked for.
        const catalogue = session.personas ?? []
        setPersonas(catalogue)
        const preferred = session.defaultPersona ?? null
        const nextPersona = preferred && catalogue.some((p) => p.key === preferred) ? preferred : null
        personaRef.current = nextPersona
        setPersonaState(nextPersona)

        setMessages(stored.map(toLocalMessage))
        setStatus('idle')
      } catch (err: unknown) {
        if (ignore) {
          return
        }

        // A failed load must not block the chat: the panel stays usable and shows what went wrong. Note the
        // transcript is the SERVER's, so this failure is a real degradation rather than a cosmetic one.
        setError(messageOf(err))
        setStatus('error')
      }
    }

    void run()

    return () => {
      ignore = true
    }
  }, [boardId])

  useEffect(() => {
    return () => {
      abortControllerRef.current?.abort()
    }
  }, [])

  const clearError = useCallback(() => {
    setError(null)
    setHttpStatus(null)
    setStatus((current) => (current === 'error' ? 'idle' : current))
  }, [])

  const cancel = useCallback(() => {
    abortControllerRef.current?.abort()
    abortControllerRef.current = null
    accumulatorRef.current = ''
    setStreamingAnswer('')
    setStatus('idle')
  }, [])

  const attach = useCallback((task: AgentChatAttachment) => {
    setAttachments((current) => {
      const next = attachTask(current, task)
      attachmentsRef.current = next
      return next
    })
  }, [])

  const detach = useCallback((taskId: string) => {
    setAttachments((current) => {
      const next = detachTask(current, taskId)
      attachmentsRef.current = next
      return next
    })
  }, [])

  const clearAttachments = useCallback(() => {
    attachmentsRef.current = []
    setAttachments([])
  }, [])

  const ask = useCallback(
    async (content: string) => {
      if (!boardId || !content.trim() || status === 'streaming') {
        return
      }

      setError(null)
      setHttpStatus(null)
      setProposal(null)
      setProposalJson(null)

      const userMessage: AgentChatMessage = {
        id: newId(),
        role: 'user',
        content: content.trim(),
        createdAt: new Date().toISOString(),
      }

      const nextMessages = [...messagesRef.current, userMessage]
      messagesRef.current = nextMessages
      setMessages(nextMessages)
      setStreamingAnswer('')
      accumulatorRef.current = ''
      setStatus('streaming')

      const controller = new AbortController()
      abortControllerRef.current = controller

      // Captured ONCE, at request time. If the user switches persona while the answer streams, the turn that
      // lands in the transcript must still be labelled with the role that actually produced it.
      const turnPersona = personaRef.current

      // Trimmed to 10 turns/8000 chars — the BOARD chat's server cap is 24/24000, so this is comfortably
      // inside it. Sending less than the limit is always preferable to being refused with a 400, and the
      // shared helper keeps the two chats from drifting on how history is bounded.
      const requestMessages = toRequestMessages(trimHistory(nextMessages, 10))

      try {
        let finalAnswer: string | null = null

        await agentChatApi.stream(
          boardId,
          {
            messages: requestMessages,
            attachedTaskIds: toRequestIds(attachmentsRef.current),
          },
          {
            signal: controller.signal,
            persona: turnPersona,
            onEvent: ({ event, data }) => {
              if (event === 'delta' && data?.text) {
                accumulatorRef.current += data.text
                // Phase 14.6 §A2: the DISPLAY is redacted from the raw accumulator, never from one delta —
                // a Guid split across two frames is only recognisable once both halves have arrived. The
                // proposal block is stripped by `redactAnswerProse`, so a real `taskId` inside it survives
                // and the "Gửi đề xuất" button still sends exactly what the model produced.
                setStreamingAnswer(redactAnswerProse(accumulatorRef.current))
              } else if (event === 'done' && data?.answer) {
                // The full answer is authoritative: if a delta frame was lost, this still has everything.
                finalAnswer = data.answer
              }
            },
          }
        )

        if (controller.signal.aborted) {
          return
        }

        const answer = finalAnswer ?? accumulatorRef.current
        const { visibleText, jsonBlock } = splitProposal(answer)

        // Phase 14.6 §A2: the turn that becomes the transcript is the redacted prose. The server redacts
        // again before storing it (a reload must not resurrect what was hidden) — doing it here too is what
        // keeps "Lưu thành bình luận" from posting an identifier the user never saw.
        const proseText = redactIdentifiers(visibleText)

        if (proseText) {
          const assistant: AgentChatMessage = {
            id: newId(),
            role: 'assistant',
            content: proseText,
            createdAt: new Date().toISOString(),
            // Phase 14.7 §B: only a locally produced turn carries a persona. A reload gets its turns from
            // the server, which does not record one, so old answers stay unlabelled.
            personaKey: turnPersona,
          }

          const withAnswer = [...messagesRef.current, assistant]
          messagesRef.current = withAnswer
          setMessages(withAnswer)
        }

        // The card is offered only when the block actually parses into a proposal the server could accept,
        // so the UI never shows a "Gửi đề xuất" button that is guaranteed to be refused.
        setProposal(parseProposal(jsonBlock) as AgentChatProposal | null)
        setProposalJson(jsonBlock)

        setStreamingAnswer('')
        accumulatorRef.current = ''
        setStatus('idle')
      } catch (err: unknown) {
        if (controller.signal.aborted) {
          return
        }

        const streamError = err as { message?: string; status?: number }
        setError(streamError.message || 'Không thể kết nối đến dịch vụ AI')
        setHttpStatus(streamError.status ?? null)
        // The partial answer stays visible: throwing away text the user already read would be worse than
        // showing an alert next to it.
        setStatus('error')
      } finally {
        abortControllerRef.current = null
      }
    },
    [boardId, status]
  )

  const saveAnswer = useCallback(
    async (content?: string, taskId?: string | null): Promise<AiActionLog | null> => {
      if (!boardId) {
        return null
      }

      const lastAssistant = [...messagesRef.current].reverse().find((m) => m.role === 'assistant')
      const contentToSave = content ?? lastAssistant?.content ?? streamingAnswer

      if (!contentToSave || contentToSave.length === 0 || contentToSave.length > 2000) {
        setError('Nội dung câu trả lời không hợp lệ hoặc vượt quá 2000 ký tự')
        setStatus('error')
        return null
      }

      // The comment needs a task. Defaulting to the FIRST attached card is the common case (one card
      // dragged in) and it is what the server expects when `taskId` is omitted — but the UI passes it
      // explicitly so the choice is visible rather than implicit.
      const targetTaskId = taskId ?? attachmentsRef.current[0]?.taskId ?? null

      if (!targetTaskId) {
        setError('Hãy gắn ít nhất một thẻ vào cuộc trò chuyện trước khi lưu câu trả lời.')
        setStatus('error')
        return null
      }

      setStatus('saving')
      setError(null)

      try {
        const log = await agentChatApi.saveAnswer(boardId, {
          content: contentToSave,
          taskId: targetTaskId,
        })
        setStatus('idle')
        return log
      } catch (err: unknown) {
        const httpErr = err as { response?: { data?: { error?: string }; status?: number }; message?: string }
        setError(
          httpErr?.response?.data?.error ||
            httpErr?.message ||
            'Không thể lưu câu trả lời thành bình luận'
        )
        setHttpStatus(httpErr?.response?.status ?? null)
        setStatus('error')
        return null
      }
    },
    [boardId, streamingAnswer]
  )

  const submitProposal = useCallback(async (): Promise<AiActionLog | null> => {
    if (!boardId || !proposalJson) {
      return null
    }

    setStatus('confirming')
    setError(null)

    try {
      const log = await agentChatApi.submitProposal(boardId, {
        proposalJson,
        attachedTaskIds: toRequestIds(attachmentsRef.current),
      })

      // Cleared only on success: an error keeps the card so the user can retry rather than re-asking the
      // model (which would cost tokens and might produce a different proposal).
      setProposal(null)
      setProposalJson(null)
      setStatus('idle')
      return log
    } catch (err: unknown) {
      const httpErr = err as { response?: { data?: { error?: string }; status?: number }; message?: string }
      setError(httpErr?.response?.data?.error || httpErr?.message || 'Không thể gửi đề xuất để duyệt')
      setHttpStatus(httpErr?.response?.status ?? null)
      setStatus('error')
      return null
    }
  }, [boardId, proposalJson])

  return {
    messages,
    streamingAnswer,
    attachments,
    proposal,
    proposalJson,
    model,
    personas,
    persona,
    setPersona,
    status,
    error,
    httpStatus,
    ask,
    cancel,
    attach,
    detach,
    clearAttachments,
    saveAnswer,
    submitProposal,
    clearError,
  }
}

/** The cap the UI enforces before a user can build a request the server would refuse. */
export { MAX_ATTACHED_TASKS }

function toLocalMessage(stored: AgentChatStoredMessage): AgentChatMessage {
  return {
    id: stored.id,
    role: stored.role,
    // Assistant prose only, and again on the way IN: turns written before §A2 may still hold an identifier,
    // and re-reading them must not put it back on screen. A user's own words are never rewritten.
    content: stored.role === 'assistant' ? redactIdentifiers(stored.content) : stored.content,
    createdAt: stored.createdAt,
  }
}

function newId(): string {
  return typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : `msg-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`
}

function messageOf(err: unknown): string {
  const httpErr = err as { response?: { data?: { error?: string } }; message?: string }
  return httpErr?.response?.data?.error || httpErr?.message || 'Không thể tải hội thoại của bảng.'
}
