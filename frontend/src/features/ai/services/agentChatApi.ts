import { httpClient } from '../../../shared/api'
import type { AiActionLog } from '../types/aiAction.types'
import type {
  AgentChatSendRequest,
  AgentChatSession,
  AgentChatStoredMessage,
  SaveAgentChatAnswerRequest,
  SubmitAgentChatProposalRequest,
} from '../types/agentChat.types'
import { streamSse } from '../utils/sseStream'

/**
 * HTTP surface of the board-scoped AI Agent Chat (Phase 14.5 §B).
 *
 * <p>The streaming call deliberately does NOT go through `httpClient`: axios cannot hand back a
 * progressively-read body, and the whole point of the feature is that text arrives while it is being
 * produced. It uses the shared `streamSse` helper, which keeps the CSRF + cookie contract identical to
 * every other POST.</p>
 */
export const agentChatApi = {
  /** `GET /api/boards/{boardId}/agent-chat/session` — idempotent access probe + model name. */
  getSession: async (boardId: string): Promise<AgentChatSession> => {
    const res = await httpClient.get<AgentChatSession>(`/boards/${boardId}/agent-chat/session`)
    return res.data
  },

  /** `GET /api/boards/{boardId}/agent-chat/messages` — stored transcript, oldest first. */
  listMessages: async (
    boardId: string,
    params?: { take?: number; before?: string }
  ): Promise<AgentChatStoredMessage[]> => {
    const res = await httpClient.get<AgentChatStoredMessage[]>(
      `/boards/${boardId}/agent-chat/messages`,
      { params }
    )
    return res.data
  },

  /** Streams the answer (SSE frames, same contract as the task chat). */
  stream: async (
    boardId: string,
    request: AgentChatSendRequest,
    options: {
      signal?: AbortSignal
      onEvent: (event: { event: string; data: any }) => void
      /**
       * Phase 14.7 §B: the persona to answer as, as a QUERY parameter. `null`/`undefined`/blank means "let
       * the server pick", which is why the parameter is omitted entirely rather than sent empty.
       */
      persona?: string | null
    }
  ): Promise<void> => {
    const persona = options.persona?.trim()

    return streamSse({
      // Appended only when a persona was actually chosen: with none, the URL stays byte-identical to the
      // pre-14.7 one, so "server default" keeps meaning exactly that.
      path: `/boards/${boardId}/agent-chat/stream${
        persona ? `?persona=${encodeURIComponent(persona)}` : ''
      }`,
      body: request,
      signal: options.signal,
      onEvent: options.onEvent,
    })
  },

  /** `POST .../agent-chat/message` — records the answer as a Pending comment on an attached task. */
  saveAnswer: async (
    boardId: string,
    request: SaveAgentChatAnswerRequest
  ): Promise<AiActionLog> => {
    const res = await httpClient.post<AiActionLog>(`/boards/${boardId}/agent-chat/message`, request)
    return res.data
  },

  /** `POST .../agent-chat/proposal/confirm` — records a proposal as a Pending action. */
  submitProposal: async (
    boardId: string,
    request: SubmitAgentChatProposalRequest
  ): Promise<AiActionLog> => {
    const res = await httpClient.post<AiActionLog>(
      `/boards/${boardId}/agent-chat/proposal/confirm`,
      request
    )
    return res.data
  },
}
