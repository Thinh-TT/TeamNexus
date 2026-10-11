import { getXsrfToken } from '../../../shared/api/httpClient'
import { parseSseFrames } from './sse'

export interface SseEvent {
  event: string
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  data: any
}

export interface StreamSseOptions {
  /** Path under the API base (for example `/boards/{id}/agent-chat/stream`). */
  path: string
  body: unknown
  signal?: AbortSignal
  onEvent: (event: SseEvent) => void
}

export interface StreamHttpError extends Error {
  status?: number
}

/**
 * Opens an SSE stream with `fetch` + `ReadableStream` and dispatches every frame (Phase 14.5 §B).
 *
 * <p><b>Why `fetch` and never `EventSource`:</b> `EventSource` can neither POST nor set the
 * `X-XSRF-TOKEN` header, and every mutating verb in this API is protected by antiforgery — the request
 * would simply answer 403. Keeping the fetch contract in ONE place is what lets the task chat and the
 * board chat share a transport instead of growing two subtly different ones.</p>
 *
 * <p><b>Status is checked before the body is read.</b> A failure that happens before the stream opens
 * (400/401/403/404/503) arrives as JSON with a real status code, so it is read as JSON and thrown as an
 * error carrying that status. Only after `response.ok` is the body treated as frames.</p>
 */
export async function streamSse({ path, body, signal, onEvent }: StreamSseOptions): Promise<void> {
  const xsrfToken = getXsrfToken() ?? ''
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api'
  const url = `${baseUrl}${path}`

  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': xsrfToken,
      },
      body: JSON.stringify(body),
      signal,
    })
  } catch (err: unknown) {
    // An aborted request is a normal outcome (the user pressed "Dừng" or the panel unmounted).
    if ((err as Error)?.name === 'AbortError') {
      return
    }
    throw err
  }

  if (!response.ok) {
    let errorMsg = `Yêu cầu thất bại (${response.status})`
    try {
      const errorJson = await response.json()
      if (errorJson && typeof errorJson.error === 'string') {
        errorMsg = errorJson.error
      }
    } catch {
      // Non-JSON error body: keep the generic message rather than surfacing a parse error.
    }

    const httpError: StreamHttpError = new Error(errorMsg)
    httpError.status = response.status
    throw httpError
  }

  if (!response.body) {
    throw new Error('Máy chủ không trả về dữ liệu stream.')
  }

  const reader = response.body.getReader()
  const decoder = new TextDecoder()
  // `carry` holds an incomplete frame between TCP chunks. Dropping it loses text or crashes the parser,
  // and `sse.test.ts` has a case for exactly that.
  let carry = ''

  try {
    while (true) {
      if (signal?.aborted) {
        await reader.cancel()
        break
      }

      const { done, value } = await reader.read()
      if (done) {
        break
      }

      const chunkText = decoder.decode(value, { stream: true })
      const { events, carry: nextCarry } = parseSseFrames(carry + chunkText)
      carry = nextCarry

      for (const evt of events) {
        let parsedData: unknown = evt.data
        try {
          parsedData = JSON.parse(evt.data)
        } catch {
          // A frame whose data is not JSON stays a string; the caller decides what to do with it.
        }

        onEvent({ event: evt.event, data: parsedData })

        if (evt.event === 'error') {
          const errMsg =
            parsedData && typeof parsedData === 'object' && 'error' in parsedData
              ? String((parsedData as { error: unknown }).error)
              : 'Đã xảy ra lỗi trong quá trình sinh câu trả lời.'
          throw new Error(errMsg)
        }
      }
    }
  } catch (err: unknown) {
    if ((err as Error)?.name === 'AbortError' || signal?.aborted) {
      return
    }
    throw err
  }
}
