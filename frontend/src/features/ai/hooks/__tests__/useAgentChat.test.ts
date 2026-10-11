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
})
