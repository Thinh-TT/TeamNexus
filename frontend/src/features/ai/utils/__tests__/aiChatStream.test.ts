import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { streamAiChat } from '../aiChatStream'

vi.mock('../../../../shared/api/httpClient', () => ({
  getXsrfToken: vi.fn(() => 'mock-xsrf-token'),
}))

describe('streamAiChat', () => {
  const originalFetch = globalThis.fetch

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
  })

  it('sends POST request with credentials, X-XSRF-TOKEN, and correct body', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(
          encoder.encode('event: done\ndata: {"answer":"OK"}\n\n')
        )
        controller.close()
      },
    })

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      body: stream,
    })
    globalThis.fetch = fetchMock

    const onEvent = vi.fn()
    await streamAiChat({
      taskId: 'task-123',
      messages: [{ role: 'user', content: 'test question' }],
      onEvent,
    })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toContain('/tasks/task-123/ai-chat/stream')
    expect(options.method).toBe('POST')
    expect(options.credentials).toBe('include')
    expect(options.headers['X-XSRF-TOKEN']).toBe('mock-xsrf-token')
    expect(JSON.parse(options.body)).toEqual({
      messages: [{ role: 'user', content: 'test question' }],
    })
    expect(onEvent).toHaveBeenCalledWith({
      event: 'done',
      data: { answer: 'OK' },
    })
  })

  it('throws error with status=403 and does not read stream when HTTP status is not ok', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 403,
      json: vi.fn().mockResolvedValue({ error: 'CSRF token không hợp lệ' }),
    })
    globalThis.fetch = fetchMock

    const onEvent = vi.fn()
    let caughtErr: any
    try {
      await streamAiChat({
        taskId: 'task-123',
        messages: [{ role: 'user', content: 'hi' }],
        onEvent,
      })
    } catch (err: any) {
      caughtErr = err
    }

    expect(caughtErr).toBeDefined()
    expect(caughtErr.status).toBe(403)
    expect(caughtErr.message).toBe('CSRF token không hợp lệ')
    expect(onEvent).not.toHaveBeenCalled()
  })

  it('dispatches events in correct sequence (meta -> delta x2 -> done)', async () => {
    const encoder = new TextEncoder()
    const ssePayload =
      'event: meta\ndata: {"taskId":"task-123","model":"deepseek"}\n\n' +
      'event: delta\ndata: {"text":"Xin "}\n\n' +
      'event: delta\ndata: {"text":"chào"}\n\n' +
      'event: done\ndata: {"answer":"Xin chào","promptTokens":10,"completionTokens":5}\n\n'

    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(ssePayload))
        controller.close()
      },
    })

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      body: stream,
    })

    const eventsReceived: any[] = []
    await streamAiChat({
      taskId: 'task-123',
      messages: [{ role: 'user', content: 'Xin chào' }],
      onEvent: (evt) => eventsReceived.push(evt),
    })

    expect(eventsReceived).toHaveLength(4)
    expect(eventsReceived[0].event).toBe('meta')
    expect(eventsReceived[1].event).toBe('delta')
    expect(eventsReceived[1].data).toEqual({ text: 'Xin ' })
    expect(eventsReceived[2].event).toBe('delta')
    expect(eventsReceived[2].data).toEqual({ text: 'chào' })
    expect(eventsReceived[3].event).toBe('done')
    expect(eventsReceived[3].data.answer).toBe('Xin chào')
  })

  it('throws error when event: error occurs mid-stream', async () => {
    const encoder = new TextEncoder()
    const ssePayload =
      'event: delta\ndata: {"text":"Đang xử lý..."}\n\n' +
      'event: error\ndata: {"error":"DeepSeek không phản hồi trong 60s (timeout)."}\n\n'

    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(ssePayload))
        controller.close()
      },
    })

    globalThis.fetch = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      body: stream,
    })

    await expect(
      streamAiChat({
        taskId: 'task-123',
        messages: [{ role: 'user', content: 'test' }],
        onEvent: vi.fn(),
      })
    ).rejects.toThrow('DeepSeek không phản hồi trong 60s (timeout).')
  })

  it('gracefully aborts without unhandled rejections when AbortController aborts', async () => {
    const controller = new AbortController()

    globalThis.fetch = vi.fn().mockImplementation(() => {
      controller.abort()
      const abortErr = new Error('The user aborted a request.')
      abortErr.name = 'AbortError'
      return Promise.reject(abortErr)
    })

    await expect(
      streamAiChat({
        taskId: 'task-123',
        messages: [{ role: 'user', content: 'test' }],
        signal: controller.signal,
        onEvent: vi.fn(),
      })
    ).resolves.toBeUndefined()
  })
})
