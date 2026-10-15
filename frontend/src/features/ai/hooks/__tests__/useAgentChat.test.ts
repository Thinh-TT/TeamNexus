import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAgentChat } from '../useAgentChat'
import { agentChatApi } from '../../services/agentChatApi'

vi.mock('../../services/agentChatApi', () => ({
  agentChatApi: {
    getSession: vi.fn(),
    listMessages: vi.fn(),
    stream: vi.fn(),
    saveAnswer: vi.fn(),
    submitProposal: vi.fn(),
  },
}))

const mocked = vi.mocked(agentChatApi)

beforeEach(() => {
  vi.clearAllMocks()

  // The panel always opens with these two calls, so every test needs a sane default.
  mocked.getSession.mockResolvedValue({ boardId: 'b1', hasMessages: false, model: 'deepseek-chat' })
  mocked.listMessages.mockResolvedValue([])
})

describe('useAgentChat', () => {
  it('loads the stored transcript and the model name when the panel opens', async () => {
    mocked.listMessages.mockResolvedValue([
      {
        id: 'm1',
        role: 'user',
        content: 'Câu hỏi cũ',
        attachments: [{ taskId: 't1', title: 'Thẻ 1' }],
        createdAt: '2026-01-01T00:00:00Z',
      },
      {
        id: 'm2',
        role: 'assistant',
        content: 'Câu trả lời cũ',
        attachments: [],
        createdAt: '2026-01-01T00:00:01Z',
      },
    ])

    const { result } = renderHook(() => useAgentChat('b1'))

    // The board conversation lives on the server, so opening the panel is what loads it — this is the
    // whole reason Phase 14.5 added a table.
    await waitFor(() => expect(result.current.messages).toHaveLength(2))

    expect(result.current.messages[0].content).toBe('Câu hỏi cũ')
    expect(result.current.model).toBe('deepseek-chat')
    expect(result.current.status).toBe('idle')
  })

  it('keeps the panel usable and reports the reason when the transcript cannot be loaded', async () => {
    mocked.getSession.mockRejectedValue({ response: { data: { error: 'Không có quyền' } } })

    const { result } = renderHook(() => useAgentChat('b1'))

    await waitFor(() => expect(result.current.status).toBe('error'))

    expect(result.current.error).toBe('Không có quyền')
    // The chat itself still works: a failed history load is a degradation, not a dead feature.
    expect(result.current.messages).toHaveLength(0)
  })

  it('streams an answer, keeps the text from the done frame, and offers no card without a proposal', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'delta', data: { text: 'Xin ' } })
      options.onEvent({ event: 'delta', data: { text: 'chào' } })
      options.onEvent({ event: 'done', data: { answer: 'Xin chào, đây là câu trả lời.' } })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    await act(async () => {
      await result.current.ask('Bảng này thế nào?')
    })

    expect(result.current.messages).toHaveLength(2)
    expect(result.current.messages[0]).toMatchObject({ role: 'user', content: 'Bảng này thế nào?' })
    // `done.answer` is authoritative: a lost delta frame must not truncate the saved turn.
    expect(result.current.messages[1].content).toBe('Xin chào, đây là câu trả lời.')
    expect(result.current.proposal).toBeNull()
    expect(result.current.proposalJson).toBeNull()
    expect(result.current.status).toBe('idle')
  })

  it('splits a proposal out of the answer so the transcript shows prose and the card offers the action', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      const answer =
        'Nên chuyển thẻ sang cột Xong.\n\n```json\n' +
        '{"proposal":{"kind":"MoveTaskToColumn","summary":"Chuyển thẻ","payload":{"taskId":"t1","columnId":"c1"}}}\n```'
      options.onEvent({ event: 'done', data: { answer } })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    await act(async () => {
      await result.current.ask('Nên làm gì?')
    })

    // The user's bubble shows prose — not a wall of JSON.
    expect(result.current.messages[1].content).toBe('Nên chuyển thẻ sang cột Xong.')

    expect(result.current.proposal).toMatchObject({ kind: 'MoveTaskToColumn' })
    expect(result.current.proposalJson).toContain('MoveTaskToColumn')
  })

  it('sends the attached task ids with the question', async () => {
    mocked.stream.mockImplementation(async () => {})

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    act(() => {
      result.current.attach({ taskId: 't1', title: 'Thẻ 1' })
      result.current.attach({ taskId: 't2', title: 'Thẻ 2' })
    })

    await act(async () => {
      await result.current.ask('So sánh hai thẻ này')
    })

    const [, request] = mocked.stream.mock.calls[0]
    expect(request.attachedTaskIds).toEqual([{ taskId: 't1' }, { taskId: 't2' }])
    // The client copy of the title never crosses the wire: the server reads the live task.
    expect(JSON.stringify(request)).not.toContain('Thẻ 1')
  })

  it('tracks attachments through attach and detach', async () => {
    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    act(() => {
      result.current.attach({ taskId: 't1', title: 'Thẻ 1' })
    })
    expect(result.current.attachments).toHaveLength(1)

    act(() => {
      result.current.detach('t1')
    })
    expect(result.current.attachments).toHaveLength(0)
  })

  it('keeps the partial answer visible and reports the status when the stream fails', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'delta', data: { text: 'Đang trả lời dở...' } })
      throw Object.assign(new Error('DeepSeek không phản hồi trong 60s (timeout).'), { status: 502 })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    expect(result.current.status).toBe('error')
    expect(result.current.error).toContain('timeout')
    expect(result.current.httpStatus).toBe(502)
    // Throwing away text the user already read would be worse than showing an alert next to it.
    expect(result.current.streamingAnswer).toBe('Đang trả lời dở...')
  })

  it('saves the answer as a comment on the first attached task', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'done', data: { answer: 'Kết luận cuối cùng.' } })
    })

    mocked.saveAnswer.mockResolvedValue({
      id: 'log-1',
      action: 'PostComment',
      entityType: 'Task',
      entityId: 't1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: null,
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 0,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    act(() => {
      result.current.attach({ taskId: 't1', title: 'Thẻ 1' })
    })

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    await act(async () => {
      await result.current.saveAnswer()
    })

    expect(mocked.saveAnswer).toHaveBeenCalledWith('b1', {
      content: 'Kết luận cuối cùng.',
      taskId: 't1',
    })
  })

  it('refuses to save when no task is attached, with a Vietnamese reason', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'done', data: { answer: 'Nội dung.' } })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    let saved: unknown
    await act(async () => {
      saved = await result.current.saveAnswer()
    })

    expect(saved).toBeNull()
    expect(mocked.saveAnswer).not.toHaveBeenCalled()
    expect(result.current.error).toContain('gắn ít nhất một thẻ')
  })

  it('submits the proposal and clears the card on success', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({
        event: 'done',
        data: {
          answer:
            'Đề xuất.\n\n```json\n{"proposal":{"kind":"MoveTaskToColumn","summary":"x","payload":{"taskId":"t1","columnId":"c1"}}}\n```',
        },
      })
    })

    mocked.submitProposal.mockResolvedValue({
      id: 'log-2',
      action: 'MoveTaskToColumn',
      entityType: 'Task',
      entityId: 't1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: null,
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 0,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    act(() => {
      result.current.attach({ taskId: 't1', title: 'Thẻ 1' })
    })

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    await act(async () => {
      await result.current.submitProposal()
    })

    const [, request] = mocked.submitProposal.mock.calls[0]
    expect(request.proposalJson).toContain('MoveTaskToColumn')
    expect(request.attachedTaskIds).toEqual([{ taskId: 't1' }])
    // Cleared only on success, so an error keeps the card for a retry.
    expect(result.current.proposal).toBeNull()
  })

  it('keeps the card and reports the reason when submitting fails', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({
        event: 'done',
        data: {
          answer:
            'Đề xuất.\n\n```json\n{"proposal":{"kind":"MoveTaskToColumn","summary":"x","payload":{"taskId":"t1","columnId":"c1"}}}\n```',
        },
      })
    })

    mocked.submitProposal.mockRejectedValue({
      response: { status: 400, data: { error: 'Đề xuất thao tác trên một thẻ không được gắn.' } },
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    act(() => {
      result.current.attach({ taskId: 't1', title: 'Thẻ 1' })
    })

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    await act(async () => {
      await result.current.submitProposal()
    })

    expect(result.current.error).toContain('không được gắn')
    expect(result.current.proposal).not.toBeNull()
  })

  it('does not append a bubble when the user cancels mid-stream', async () => {
    mocked.stream.mockImplementation(
      async (_boardId, _request, options) =>
        new Promise<void>((resolve) => {
          options.onEvent({ event: 'delta', data: { text: 'Đang gõ dở...' } })
          options.signal?.addEventListener('abort', () => resolve())
        })
    )

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    let pending: Promise<void> | undefined
    act(() => {
      pending = result.current.ask('Hỏi')
    })

    act(() => {
      result.current.cancel()
    })

    await act(async () => {
      await pending
    })

    // Only the user's own message remains: an abandoned answer must not become a rubbish bubble.
    expect(result.current.messages).toHaveLength(1)
    expect(result.current.streamingAnswer).toBe('')
  })

  // ---- Phase 14.6 §A2: the identifier leak ---------------------------------

  it('hides an identifier the model leaked into its prose, live and in the kept turn', async () => {
    const leakedId = '9724c884-d103-4147-9b68-7532d97f04a9'

    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({
        event: 'delta',
        data: { text: `Nên chuyển sang cột "process" (id: ${leakedId}).` },
      })
      options.onEvent({
        event: 'done',
        data: { answer: `Nên chuyển sang cột "process" (id: ${leakedId}).` },
      })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    let pending: Promise<void> | undefined
    act(() => {
      pending = result.current.ask('Nên chuyển thẻ này đi đâu?')
    })

    // The live bubble is filtered too, not just the turn that lands in the transcript.
    await waitFor(() => expect(result.current.streamingAnswer).not.toBe(''))

    await act(async () => {
      await pending
    })

    expect(result.current.streamingAnswer).not.toContain(leakedId)
    expect(result.current.messages[1].content).not.toContain(leakedId)
    expect(result.current.messages[1].content).toContain('[mã nội bộ]')

    // Redacted, not deleted: the sentence the user reads is otherwise intact.
    expect(result.current.messages[1].content).toContain('Nên chuyển sang cột "process"')
  })

  it('keeps the real ids inside a proposal block so the card stays submittable', async () => {
    const taskId = '9724c884-d103-4147-9b68-7532d97f04a9'
    const columnId = '11111111-2222-3333-4444-555555555555'

    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({
        event: 'done',
        data: {
          answer:
            `Nên chuyển thẻ sang cột Xong (id: ${columnId}).\n\n` +
            '```json\n' +
            `{"proposal":{"kind":"MoveTaskToColumn","summary":"Chuyển thẻ","payload":{"taskId":"${taskId}","columnId":"${columnId}"}}}\n` +
            '```',
        },
      })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    await act(async () => {
      await result.current.ask('Nên làm gì?')
    })

    // The prose is filtered...
    expect(result.current.messages[1].content).not.toContain(columnId)
    expect(result.current.messages[1].content).not.toContain('```json')

    // ...while the block keeps the ids the server needs to re-validate and apply the proposal. Redacting here
    // would turn a submittable proposal into a guaranteed 400.
    expect(result.current.proposalJson).toContain(taskId)
    expect(result.current.proposalJson).toContain(columnId)
    expect(result.current.proposal).toMatchObject({ kind: 'MoveTaskToColumn' })
  })

  // ---- Phase 14.7 §B: the persona the turn is answered as -------------------

  const personaSession = {
    boardId: 'b1',
    hasMessages: false,
    model: 'deepseek-chat',
    personas: [
      { key: 'general', displayName: 'TeamNexus Agent' },
      { key: 'scrum_master', displayName: 'AI Scrum Master' },
      { key: 'qa_security', displayName: 'AI QA & Security' },
    ],
    defaultPersona: 'general',
  }

  it('loads the persona catalogue and adopts the server default', async () => {
    mocked.getSession.mockResolvedValue(personaSession)

    const { result } = renderHook(() => useAgentChat('b1'))

    await waitFor(() => expect(result.current.personas).toHaveLength(3))
    expect(result.current.persona).toBe('general')
  })

  it('sends the current persona on every turn, including one chosen after the first turn', async () => {
    mocked.getSession.mockResolvedValue(personaSession)
    mocked.stream.mockImplementation(async () => {})

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(result.current.persona).toBe('general'))

    await act(async () => {
      await result.current.ask('Câu hỏi 1')
    })

    act(() => {
      result.current.setPersona('qa_security')
    })

    await act(async () => {
      await result.current.ask('Câu hỏi 2')
    })

    // Turn 1 answered as the server default; turn 2 as the role the user picked — never a stale one.
    expect(mocked.stream.mock.calls[0][2].persona).toBe('general')
    expect(mocked.stream.mock.calls[1][2].persona).toBe('qa_security')
  })

  it('sends no persona when the session offers none, so the server default still applies', async () => {
    // No catalogue at all (the shape every pre-14.7 session had): the request must be unchanged.
    mocked.getSession.mockResolvedValue({ boardId: 'b1', hasMessages: false, model: 'deepseek-chat' })
    mocked.stream.mockImplementation(async () => {})

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(mocked.getSession).toHaveBeenCalled())

    expect(result.current.personas).toEqual([])
    expect(result.current.persona).toBeNull()

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    expect(mocked.stream.mock.calls[0][2].persona).toBeNull()
  })

  it('does not adopt a defaultPersona the catalogue does not contain', async () => {
    // A key the server would refuse: sending it back would turn every question into a 400.
    mocked.getSession.mockResolvedValue({ ...personaSession, defaultPersona: 'da_bi_xoa' })

    const { result } = renderHook(() => useAgentChat('b1'))

    await waitFor(() => expect(result.current.personas).toHaveLength(3))
    expect(result.current.persona).toBeNull()
  })

  it('labels only the turn it answered as, and never a turn reloaded from the server', async () => {
    mocked.getSession.mockResolvedValue(personaSession)
    mocked.listMessages.mockResolvedValue([
      {
        id: 'm-old',
        role: 'assistant',
        content: 'Câu trả lời cũ',
        attachments: [],
        createdAt: '2026-01-01T00:00:00Z',
      },
    ])
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'done', data: { answer: 'Câu trả lời mới' } })
    })

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(result.current.messages).toHaveLength(1))

    // History: no persona, because the stored transcript does not record one.
    expect(result.current.messages[0].personaKey).toBeUndefined()

    act(() => {
      result.current.setPersona('scrum_master')
    })

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    const assistant = result.current.messages.find((m) => m.content === 'Câu trả lời mới')
    expect(assistant?.personaKey).toBe('scrum_master')
  })

  it('labels the turn with the persona it STARTED with, even if the user switches mid-stream', async () => {
    mocked.getSession.mockResolvedValue(personaSession)

    let release: (() => void) | undefined
    mocked.stream.mockImplementation(
      async (_boardId, _request, options) =>
        new Promise<void>((resolve) => {
          release = () => {
            options.onEvent({ event: 'done', data: { answer: 'Trả lời xong' } })
            resolve()
          }
        })
    )

    const { result } = renderHook(() => useAgentChat('b1'))
    await waitFor(() => expect(result.current.persona).toBe('general'))

    let pending: Promise<void> | undefined
    act(() => {
      pending = result.current.ask('Hỏi')
    })

    // The user changes their mind while the answer is still arriving.
    act(() => {
      result.current.setPersona('qa_security')
    })

    await act(async () => {
      release?.()
      await pending
    })

    const assistant = result.current.messages.find((m) => m.role === 'assistant')
    expect(assistant?.personaKey).toBe('general')
  })
})
