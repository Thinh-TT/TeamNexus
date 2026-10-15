import React from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentChatPanel } from '../AgentChatPanel'
import { agentChatApi } from '../../services/agentChatApi'
import { streamSse } from '../../utils/sseStream'
import type { AgentChatPersona, AgentChatSession } from '../../types/agentChat.types'
import type { ColumnResponse } from '../../../board/types/board.types'

/**
 * Phase 14.7 §B — the persona picker on the board's AI Agent Chat.
 *
 * <p><b>Why the SSE transport is mocked instead of the API object.</b> The whole question here is "what URL
 * does choosing a persona produce?", and the URL is built inside `agentChatApi.stream`. Mocking
 * `agentChatApi` (as the other panel suites do) would stub that construction away and the assertion would
 * prove nothing. Only `streamSse` is faked, so the REAL `stream` still runs and the path it hands to the
 * transport is observable — panel → hook → service → URL, all of it.</p>
 */

vi.mock('../../services/agentChatApi', async () => {
  const actual = await vi.importActual<typeof import('../../services/agentChatApi')>(
    '../../services/agentChatApi'
  )

  return {
    agentChatApi: {
      ...actual.agentChatApi,
      // Only the two calls the panel makes on open are replaced; `stream` stays real.
      getSession: vi.fn(),
      listMessages: vi.fn(),
    },
  }
})

vi.mock('../../utils/sseStream', () => ({
  streamSse: vi.fn(),
}))

const mocked = vi.mocked(agentChatApi)
const mockedStreamSse = vi.mocked(streamSse)

const personas: AgentChatPersona[] = [
  { key: 'general', displayName: 'TeamNexus Agent' },
  { key: 'scrum_master', displayName: 'AI Scrum Master' },
  { key: 'tech_lead', displayName: 'AI Tech Lead' },
  { key: 'qa_security', displayName: 'AI QA & Security' },
]

const columns: ColumnResponse[] = [
  {
    id: 'c1',
    boardId: 'b1',
    name: 'Cần làm',
    position: 0,
    isDone: false,
    isClarification: false,
    createdAt: '',
    updatedAt: '',
    tasks: [],
  },
]

const renderPanel = () =>
  render(<AgentChatPanel boardId="b1" columns={columns} width={360} onClose={vi.fn()} />)

const newSession = (overrides: Partial<AgentChatSession> = {}) =>
  mocked.getSession.mockResolvedValue({
    boardId: 'b1',
    hasMessages: false,
    model: 'deepseek-chat',
    personas,
    defaultPersona: 'general',
    ...overrides,
  })

/** Opens the picker and picks the option by its server-provided label. */
const choosePersona = async (displayName: string) => {
  const row = await screen.findByTestId('agent-chat-persona-row')
  fireEvent.mouseDown(within(row).getByRole('combobox'))

  fireEvent.click(await screen.findByText(displayName))
}

const ask = async (question: string) => {
  fireEvent.change(screen.getByPlaceholderText(/Hỏi AI về bảng/), {
    target: { value: question },
  })
  fireEvent.click(screen.getByTestId('agent-chat-send'))
}

/** The path the real `agentChatApi.stream` handed to the transport for the given turn. */
const streamPathOf = (turn = 0): string =>
  (mockedStreamSse.mock.calls[turn][0] as { path: string }).path

describe('AgentChatPanel — persona picker (Phase 14.7 §B)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    newSession()
    mocked.listMessages.mockResolvedValue([])
    mockedStreamSse.mockResolvedValue(undefined)
  })

  it('PERSONA1_sends the server default at first, then the chosen persona on the NEXT turn', async () => {
    renderPanel()

    // The catalogue comes from the session, and its `defaultPersona` is what the picker starts on.
    const row = await screen.findByTestId('agent-chat-persona-row')
    expect(within(row).getByText('TeamNexus Agent')).toBeInTheDocument()

    await choosePersona('AI QA & Security')

    await ask('Thẻ này có lỗ hổng bảo mật không?')

    await waitFor(() => expect(mockedStreamSse).toHaveBeenCalledTimes(1))
    expect(streamPathOf()).toBe('/boards/b1/agent-chat/stream?persona=qa_security')
  })

  it('PERSONA2_sends no persona parameter at all when nothing is chosen', async () => {
    mocked.getSession.mockResolvedValue({
      boardId: 'b1',
      hasMessages: false,
      model: 'deepseek-chat',
      personas,
      // The server offers a catalogue but no default: "let the server decide" is a real state, and the
      // request must then be the pre-14.7 request.
      defaultPersona: null,
    })

    renderPanel()

    const row = await screen.findByTestId('agent-chat-persona-row')
    expect(within(row).getByText('Mặc định của hệ thống')).toBeInTheDocument()

    await ask('Bảng này thế nào?')

    await waitFor(() => expect(mockedStreamSse).toHaveBeenCalledTimes(1))
    expect(streamPathOf()).toBe('/boards/b1/agent-chat/stream')
    expect(streamPathOf()).not.toContain('persona')
  })

  it('PERSONA3_hides the picker when the catalogue has fewer than two entries', async () => {
    mocked.getSession.mockResolvedValue({
      boardId: 'b1',
      hasMessages: false,
      model: 'deepseek-chat',
      personas: [{ key: 'general', displayName: 'TeamNexus Agent' }],
      defaultPersona: 'general',
    })

    renderPanel()

    // The model name only appears once the SESSION RESPONSE has been applied — which is the exact moment the
    // picker decision is taken. Waiting for it is what makes "no picker" a fact rather than a race with the
    // first render: asserting the absence earlier would pass even if the picker showed up a moment later.
    await screen.findByText('Toàn bảng · deepseek-chat')
    expect(screen.queryByTestId('agent-chat-persona-row')).not.toBeInTheDocument()
  })

  it('PERSONA4_keeps the SSE frame contract: the kept answer is the done frame, not the deltas glued', async () => {
    mockedStreamSse.mockImplementation(async (options) => {
      options.onEvent({ event: 'meta', data: { boardId: 'b1' } })
      options.onEvent({ event: 'delta', data: { text: 'Xin ' } })
      options.onEvent({ event: 'delta', data: { text: 'chào ' } })
      options.onEvent({ event: 'delta', data: { text: 'bạn' } })
      options.onEvent({ event: 'done', data: { answer: 'Xin chào bạn' } })
    })

    renderPanel()
    await screen.findByTestId('agent-chat-persona-row')

    await ask('Chào AI')

    // The transport was actually reached, so the frames below were consumed…
    await waitFor(() => expect(mockedStreamSse).toHaveBeenCalledTimes(1))
    // …and the turn is committed once streaming stops.
    await waitFor(() => expect(screen.queryByTestId('agent-chat-stop')).not.toBeInTheDocument())

    // Σ delta == done.answer, so the transcript shows the sentence exactly once.
    expect(screen.getAllByText('Xin chào bạn')).toHaveLength(1)
  })

  it('PERSONA5_tags the new assistant turn with its persona and leaves reloaded history untagged', async () => {
    mocked.listMessages.mockResolvedValue([
      {
        id: 'm-old',
        role: 'assistant',
        content: 'Câu trả lời cũ',
        attachments: [],
        createdAt: '2026-01-01T00:00:00Z',
      },
    ])

    mockedStreamSse.mockImplementation(async (options) => {
      options.onEvent({ event: 'delta', data: { text: 'Kiểm tra xong' } })
      options.onEvent({ event: 'done', data: { answer: 'Kiểm tra xong' } })
    })

    renderPanel()

    // History is on screen before anything new is asked…
    expect(await screen.findByText('Câu trả lời cũ')).toBeInTheDocument()
    // …and it carries no persona label: the server does not record which role answered it.
    expect(screen.queryByTestId('agent-chat-persona-tag')).not.toBeInTheDocument()

    await choosePersona('AI QA & Security')
    await ask('Kiểm tra giúp tôi')

    const tags = await screen.findAllByTestId('agent-chat-persona-tag')
    expect(tags).toHaveLength(1)
    expect(tags[0]).toHaveTextContent('AI QA & Security')

    // The tag belongs to the NEW turn's bubble — not to the old one.
    const bubble = tags[0].parentElement?.parentElement as HTMLElement
    expect(bubble).toHaveTextContent('Kiểm tra xong')
    expect(bubble).not.toHaveTextContent('Câu trả lời cũ')
    expect(screen.getByText('Câu trả lời cũ')).not.toContainElement(tags[0])
  })

  it('PERSONA6_emits no Tailwind utility class anywhere in the panel', async () => {
    mockedStreamSse.mockImplementation(async (options) => {
      options.onEvent({ event: 'done', data: { answer: 'Đã kiểm tra.' } })
    })

    const { container } = renderPanel()

    await choosePersona('AI QA & Security')
    await ask('Kiểm tra giúp tôi')
    await screen.findByTestId('agent-chat-persona-tag')

    // TeamNexus has no Tailwind (no config, no PostCSS pipeline, no dependency): any of these class names
    // would render as nothing, so their presence is the defect regardless of intent.
    const tailwindish =
      /(^|\s)(bg-|text-\[|border-|space-[xy]-|grid-cols-|gap-\d|p-\d|px-\d|py-\d|rounded-|flex-wrap|backdrop-|animate-|shrink-0)/

    const offenders: string[] = []
    container.querySelectorAll<HTMLElement>('*').forEach((el) => {
      const className = typeof el.className === 'string' ? el.className : ''
      if (className && tailwindish.test(className)) {
        offenders.push(`${el.tagName.toLowerCase()}: ${className}`)
      }
    })

    expect(
      offenders,
      `Found Tailwind classes in AgentChatPanel — the project uses pure Ant Design:\n${offenders.join('\n')}`
    ).toEqual([])
  })
})
