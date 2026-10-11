import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { streamSse } from '../sseStream'

vi.mock('../../../../shared/api/httpClient', () => ({
  getXsrfToken: vi.fn(() => 'mock-xsrf-token'),
}))

/**
 * Transport tests for the shared SSE helper (Phase 14.5 §B).
 *
 * <p>This is the ONE place the board chat and the task chat open a stream, so the CSRF/cookie contract and
 * the "check the status before reading the body" rule are asserted here rather than twice.</p>
 */
describe('streamSse', () => {
  const originalFetch = globalThis.fetch

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    globalThis.fetch = originalFetch
  })

  it('posts to the API base with the CSRF header and cookie credentials', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode('event: done\ndata: {"answer":"OK"}\n\n'))
        controller.close()
      },
    })

    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200, body: stream })
    globalThis.fetch = fetchMock

    const onEvent = vi.fn()
    await streamSse({
      path: '/boards/board-1/agent-chat/stream',
      body: { messages: [{ role: 'user', content: 'hỏi' }] },
      onEvent,
    })

    const [url, options] = fetchMock.mock.calls[0]
    expect(url).toContain('/boards/board-1/agent-chat/stream')
    expect(options.method).toBe('POST')
    expect(options.credentials).toBe('include')
    expect(options.headers['X-XSRF-TOKEN']).toBe('mock-xsrf-token')
    expect(JSON.parse(options.body)).toEqual({ messages: [{ role: 'user', content: 'hỏi' }] })
    expect(onEvent).toHaveBeenCalledWith({ event: 'done', data: { answer: 'OK' } })
  })

  it('throws with the real status and never reads the stream when the status is not ok', async () => {
    const json = vi.fn().mockResolvedValue({ error: 'AI Agent Chat đang tắt.' })
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: false, status: 503, json })

    const onEvent = vi.fn()
    const error = await streamSse({
      path: '/boards/board-1/agent-chat/stream',
      body: {},
      onEvent,
    }).catch((err: unknown) => err as { status?: number; message?: string })

    // 503 comes back as JSON BEFORE the stream opens, so it must surface as a status, not as a frame.
    expect((error as { status?: number }).status).toBe(503)
    expect((error as { message?: string }).message).toBe('AI Agent Chat đang tắt.')
    expect(onEvent).not.toHaveBeenCalled()
  })

  it('dispatches frames in order and reassembles a frame split across chunks', async () => {
    const encoder = new TextEncoder()
    const payload =
      'event: meta\ndata: {"boardId":"b1"}\n\n' +
      'event: delta\ndata: {"text":"Xin "}\n\n' +
      'event: delta\ndata: {"text":"chào"}\n\n' +
      'event: done\ndata: {"answer":"Xin chào"}\n\n'

    // Split mid-frame: the parser must carry the partial frame to the next chunk.
    const first = payload.slice(0, 40)
    const second = payload.slice(40)

    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode(first))
        controller.enqueue(encoder.encode(second))
        controller.close()
      },
    })

    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, body: stream })

    const received: { event: string; data: unknown }[] = []
    await streamSse({
      path: '/boards/b1/agent-chat/stream',
      body: {},
      onEvent: (evt) => received.push(evt),
    })

    expect(received.map((e) => e.event)).toEqual(['meta', 'delta', 'delta', 'done'])
    expect(received[1].data).toEqual({ text: 'Xin ' })
    expect(received[3].data).toEqual({ answer: 'Xin chào' })
  })

  it('turns an in-stream error frame into a thrown error', async () => {
    const encoder = new TextEncoder()
    const stream = new ReadableStream({
      start(controller) {
        controller.enqueue(encoder.encode('event: delta\ndata: {"text":"Đang xử lý..."}\n\n'))
        controller.enqueue(
          encoder.encode('event: error\ndata: {"error":"DeepSeek không phản hồi trong 60s (timeout)."}\n\n')
        )
        controller.close()
      },
    })

    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, body: stream })

    await expect(
      streamSse({ path: '/boards/b1/agent-chat/stream', body: {}, onEvent: vi.fn() })
    ).rejects.toThrow('DeepSeek không phản hồi trong 60s (timeout).')
  })

  it('resolves quietly when the caller aborts, with no unhandled rejection', async () => {
    const controller = new AbortController()

    globalThis.fetch = vi.fn().mockImplementation(() => {
      controller.abort()
      const abortErr = new Error('The user aborted a request.')
      abortErr.name = 'AbortError'
      return Promise.reject(abortErr)
    })

    await expect(
      streamSse({
        path: '/boards/b1/agent-chat/stream',
        body: {},
        signal: controller.signal,
        onEvent: vi.fn(),
      })
    ).resolves.toBeUndefined()
  })

  it('rejects when the server returns no body at all', async () => {
    globalThis.fetch = vi.fn().mockResolvedValue({ ok: true, status: 200, body: null })

    await expect(
      streamSse({ path: '/boards/b1/agent-chat/stream', body: {}, onEvent: vi.fn() })
    ).rejects.toThrow('Máy chủ không trả về dữ liệu stream.')
  })
})
