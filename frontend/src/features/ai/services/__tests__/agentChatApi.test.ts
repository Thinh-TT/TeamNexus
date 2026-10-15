import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { agentChatApi } from '../agentChatApi'
import { streamSse } from '../../utils/sseStream'

/**
 * Phase 14.7 §B — the persona query parameter on the board chat's stream URL.
 *
 * <p>These cases live at the service boundary on purpose: they are the only place the URL that actually
 * leaves the browser is observable. A component test can only prove that a persona was PASSED along; this
 * file proves what it turns into on the wire, including the "byte-identical when absent" rule.</p>
 */

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../utils/sseStream', () => ({
  streamSse: vi.fn(),
}))

const mockedStreamSse = vi.mocked(streamSse)

const streamPathOf = (callIndex = 0): string =>
  (mockedStreamSse.mock.calls[callIndex][0] as { path: string }).path

const send = (persona?: string | null) =>
  agentChatApi.stream(
    'b1',
    { messages: [{ role: 'user', content: 'hỏi' }], attachedTaskIds: [] },
    { onEvent: vi.fn(), persona }
  )

describe('agentChatApi.stream — persona (Phase 14.7 §B)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockedStreamSse.mockResolvedValue(undefined)
  })

  it('getSession surfaces the persona catalogue and its default, keeping the old fields', async () => {
    const session = {
      boardId: 'b1',
      hasMessages: false,
      model: 'deepseek-chat',
      personas: [
        { key: 'general', displayName: 'TeamNexus Agent' },
        { key: 'qa_security', displayName: 'AI QA & Security' },
      ],
      defaultPersona: 'general',
    }

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: session })

    const result = await agentChatApi.getSession('b1')

    expect(httpClient.get).toHaveBeenCalledWith('/boards/b1/agent-chat/session')
    // The catalogue is what the picker is built from, so it MUST survive the response mapping.
    expect(result.personas).toEqual(session.personas)
    expect(result.defaultPersona).toBe('general')
    // …and the fields Phase 14.5 added are still there.
    expect(result.model).toBe('deepseek-chat')
    expect(result.hasMessages).toBe(false)
  })

  it('uses the byte-identical pre-14.7 URL when no persona is chosen', async () => {
    await send()

    // Exact equality, not `toContain`: "no persona" must not quietly become `?persona=`.
    expect(streamPathOf()).toBe('/boards/b1/agent-chat/stream')
  })

  it('treats an explicit null and a blank string as "no persona"', async () => {
    await send(null)
    await send('   ')

    expect(streamPathOf(0)).toBe('/boards/b1/agent-chat/stream')
    expect(streamPathOf(1)).toBe('/boards/b1/agent-chat/stream')
  })

  it('appends the chosen persona as a query parameter', async () => {
    await send('qa_security')

    expect(streamPathOf()).toBe('/boards/b1/agent-chat/stream?persona=qa_security')
  })

  it('URL-encodes a persona key that is not URL-safe', async () => {
    const key = 'vai trò/đặc biệt'

    await send(key)

    expect(streamPathOf()).toBe(
      `/boards/b1/agent-chat/stream?persona=${encodeURIComponent(key)}`
    )
    // The raw key must never reach the URL: an unencoded `&` or `#` in a key would silently change which
    // parameters the server sees.
    expect(streamPathOf()).not.toContain(key)
  })

  it('still forwards the body and the event callback untouched', async () => {
    const onEvent = vi.fn()

    await agentChatApi.stream(
      'b1',
      { messages: [{ role: 'user', content: 'hỏi' }], attachedTaskIds: [{ taskId: 't1' }] },
      { onEvent, persona: 'tech_lead' }
    )

    const options = mockedStreamSse.mock.calls[0][0]
    expect(options.body).toEqual({
      messages: [{ role: 'user', content: 'hỏi' }],
      attachedTaskIds: [{ taskId: 't1' }],
    })
    expect(options.onEvent).toBe(onEvent)
    expect(options.path).toBe('/boards/b1/agent-chat/stream?persona=tech_lead')
  })
})
