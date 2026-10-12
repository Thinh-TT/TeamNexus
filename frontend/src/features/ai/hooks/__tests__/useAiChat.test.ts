import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { useAiChat } from '../useAiChat'
import * as streamModule from '../../utils/aiChatStream'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    // Phase 14.6 §A3: the hook now also READS the task's stored transcript, so the module under test needs
    // a `get`. Left unstubbed it would reject and be swallowed — the tests would still pass while proving
    // nothing about the load path, so `beforeEach` gives it an explicit empty transcript.
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('useAiChat', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(httpClient.get).mockResolvedValue({ data: [] })
  })

  it('handles ask flow successfully from streaming to done', async () => {
    vi.spyOn(streamModule, 'streamAiChat').mockImplementation(async ({ onEvent }) => {
      onEvent({ event: 'delta', data: { text: 'Xin ' } })
      onEvent({ event: 'delta', data: { text: 'chào!' } })
      onEvent({ event: 'done', data: { answer: 'Xin chào!', promptTokens: 10, completionTokens: 5 } })
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    expect(result.current.status).toBe('idle')
    expect(result.current.messages).toHaveLength(0)

    await act(async () => {
      await result.current.ask('Chào bạn')
    })

    expect(result.current.status).toBe('idle')
    expect(result.current.messages).toHaveLength(2)
    expect(result.current.messages[0]).toMatchObject({
      role: 'user',
      content: 'Chào bạn',
    })
    expect(result.current.messages[1]).toMatchObject({
      role: 'assistant',
      content: 'Xin chào!',
    })
  })

  it('uses done.answer as authoritative final message content', async () => {
    vi.spyOn(streamModule, 'streamAiChat').mockImplementation(async ({ onEvent }) => {
      onEvent({ event: 'delta', data: { text: 'Đoạn 1 ' } })
      onEvent({ event: 'done', data: { answer: 'Câu trả lời hoàn chỉnh từ done', promptTokens: 10, completionTokens: 5 } })
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    await act(async () => {
      await result.current.ask('Hỏi')
    })

    expect(result.current.messages[1].content).toBe('Câu trả lời hoàn chỉnh từ done')
  })

  it('handles stream cancel without appending incomplete bubble to messages', async () => {
    vi.spyOn(streamModule, 'streamAiChat').mockImplementation(async ({ onEvent }) => {
      onEvent({ event: 'delta', data: { text: 'Đang gõ dở...' } })
      // Simulating a delay before completion
      await new Promise((resolve) => setTimeout(resolve, 50))
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    let askPromise: Promise<void>
    act(() => {
      askPromise = result.current.ask('Test cancel')
    })

    act(() => {
      result.current.cancel()
    })

    await act(async () => {
      await askPromise
    })

    expect(result.current.status).toBe('idle')
    // Only user message should exist in transcript, no assistant garbage bubble
    expect(result.current.messages).toHaveLength(1)
    expect(result.current.messages[0].role).toBe('user')
    expect(result.current.streamingAnswer).toBe('')
  })

  it('sets status to error and preserves streaming text when error occurs', async () => {
    vi.spyOn(streamModule, 'streamAiChat').mockImplementation(async ({ onEvent }) => {
      onEvent({ event: 'delta', data: { text: 'Phần đã nhận được' } })
      const err: streamModule.StreamHttpError = new Error('Quá giới hạn token')
      err.status = 400
      throw err
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    await act(async () => {
      await result.current.ask('Câu hỏi gây lỗi')
    })

    expect(result.current.status).toBe('error')
    expect(result.current.error).toBe('Quá giới hạn token')
    expect(result.current.httpStatus).toBe(400)
    expect(result.current.streamingAnswer).toBe('Phần đã nhận được')
  })

  it('includes trimmed history on subsequent ask turns', async () => {
    const streamSpy = vi
      .spyOn(streamModule, 'streamAiChat')
      .mockImplementation(async ({ onEvent }) => {
        onEvent({ event: 'done', data: { answer: 'Trả lời', promptTokens: 5, completionTokens: 5 } })
      })

    const { result } = renderHook(() => useAiChat('task-1'))

    await act(async () => {
      await result.current.ask('Lượt 1')
    })

    await act(async () => {
      await result.current.ask('Lượt 2')
    })

    expect(streamSpy).toHaveBeenCalledTimes(2)
    const secondCallPayload = streamSpy.mock.calls[1][0].messages
    expect(secondCallPayload).toHaveLength(3)
    expect(secondCallPayload[0]).toEqual({ role: 'user', content: 'Lượt 1' })
    expect(secondCallPayload[1]).toEqual({ role: 'assistant', content: 'Trả lời' })
    expect(secondCallPayload[2]).toEqual({ role: 'user', content: 'Lượt 2' })
  })

  it('calls saveAnswer API and returns pending action log', async () => {
    const mockLog = {
      id: 'log-123',
      action: 'PostComment',
      entityType: 'Task',
      entityId: 'task-1',
      status: 'Pending',
    }
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockLog })

    const { result } = renderHook(() => useAiChat('task-1'))

    let saveResult: any
    await act(async () => {
      saveResult = await result.current.saveAnswer('Nội dung bình luận mẫu')
    })

    expect(httpClient.post).toHaveBeenCalledWith('/tasks/task-1/ai-chat/message', {
      content: 'Nội dung bình luận mẫu',
    })
    expect(saveResult).toEqual(mockLog)
    expect(result.current.status).toBe('idle')
  })

  it('handles saveAnswer error and updates error state', async () => {
    vi.mocked(httpClient.post).mockRejectedValueOnce({
      response: {
        status: 400,
        data: { error: 'Task đã bị xoá' },
      },
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    let caughtErr: any
    await act(async () => {
      try {
        await result.current.saveAnswer('Bình luận lỗi')
      } catch (err) {
        caughtErr = err
      }
    })

    expect(caughtErr).toBeDefined()
    expect(result.current.status).toBe('error')
    expect(result.current.error).toBe('Task đã bị xoá')
  })

  // ---- Phase 14.6 §A3: the transcript now survives a reload -----------------

  it('loads the stored transcript when the tab opens', async () => {
    vi.mocked(httpClient.get).mockResolvedValue({
      data: [
        { id: 'm1', role: 'user', content: 'Câu hỏi cũ', attachments: [], createdAt: '2026-01-01T00:00:00Z' },
        { id: 'm2', role: 'assistant', content: 'Câu trả lời cũ', attachments: [], createdAt: '2026-01-01T00:00:01Z' },
      ],
    })

    const { result } = renderHook(() => useAiChat('task-1'))

    // The whole point of A3: the conversation used to live only in this hook's state, so F5 erased it and no
    // teammate could ever see it.
    await waitFor(() => expect(result.current.messages).toHaveLength(2))

    expect(httpClient.get).toHaveBeenCalledWith('/tasks/task-1/agent-chat/messages', {
      params: { take: 50 },
    })
    expect(result.current.messages[0]).toMatchObject({ role: 'user', content: 'Câu hỏi cũ' })
    expect(result.current.status).toBe('idle')
  })

  it('replays the loaded history as context on the next question', async () => {
    vi.mocked(httpClient.get).mockResolvedValue({
      data: [
        { id: 'm1', role: 'user', content: 'Câu hỏi cũ', attachments: [], createdAt: '2026-01-01T00:00:00Z' },
        { id: 'm2', role: 'assistant', content: 'Câu trả lời cũ', attachments: [], createdAt: '2026-01-01T00:00:01Z' },
      ],
    })

    const streamSpy = vi
      .spyOn(streamModule, 'streamAiChat')
      .mockImplementation(async ({ onEvent }) => {
        onEvent({ event: 'done', data: { answer: 'Trả lời mới', promptTokens: 1, completionTokens: 1 } })
      })

    const { result } = renderHook(() => useAiChat('task-1'))
    await waitFor(() => expect(result.current.messages).toHaveLength(2))

    await act(async () => {
      await result.current.ask('Câu hỏi mới')
    })

    // A restored conversation is a real conversation: the server judges each request on its own, so the
    // history has to travel with it.
    const payload = streamSpy.mock.calls[0][0].messages
    expect(payload).toEqual([
      { role: 'user', content: 'Câu hỏi cũ' },
      { role: 'assistant', content: 'Câu trả lời cũ' },
      { role: 'user', content: 'Câu hỏi mới' },
    ])
  })

  it('stays usable when the transcript cannot be loaded', async () => {
    vi.mocked(httpClient.get).mockRejectedValue(new Error('mạng lỗi'))

    const { result } = renderHook(() => useAiChat('task-1'))

    await waitFor(() => expect(httpClient.get).toHaveBeenCalled())

    // A failed history load is a degradation, not a dead tab.
    expect(result.current.messages).toHaveLength(0)
    expect(result.current.status).toBe('idle')
    expect(result.current.error).toBeNull()
  })

  it('hides an identifier stored before §A2 and one the model leaks now', async () => {
    const leakedId = '9724c884-d103-4147-9b68-7532d97f04a9'

    // A row written by the old build: the identifier is already in the database.
    vi.mocked(httpClient.get).mockResolvedValue({
      data: [
        { id: 'm1', role: 'assistant', content: `Cột liên quan có id ${leakedId}.`, attachments: [], createdAt: '' },
      ],
    })

    vi.spyOn(streamModule, 'streamAiChat').mockImplementation(async ({ onEvent }) => {
      onEvent({ event: 'done', data: { answer: `Sau đó xử lý thẻ ${leakedId} trước.`, promptTokens: 1, completionTokens: 1 } })
    })

    const { result } = renderHook(() => useAiChat('task-1'))
    await waitFor(() => expect(result.current.messages).toHaveLength(1))

    await act(async () => {
      await result.current.ask('Làm gì tiếp?')
    })

    // Both directions: a reload must not resurrect what was hidden, and a fresh answer must not add to it.
    expect(result.current.messages[0].content).not.toContain(leakedId)
    expect(result.current.messages[0].content).toContain('[mã nội bộ]')
    expect(result.current.messages[2].content).not.toContain(leakedId)
    expect(result.current.messages[2].content).toContain('[mã nội bộ]')
  })
})
