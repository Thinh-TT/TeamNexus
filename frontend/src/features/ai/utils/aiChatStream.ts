import { getXsrfToken } from '../../../shared/api/httpClient'
import type { AiChatRequestMessage } from '../types/aiChat.types'
import { parseSseFrames } from './sse'

export interface StreamAiChatOptions {
  taskId: string
  messages: AiChatRequestMessage[]
  signal?: AbortSignal
  onEvent: (event: { event: string; data: any }) => void
}

export interface StreamHttpError extends Error {
  status?: number
}

/**
 * Streams AI chat tokens via SSE over HTTP POST using native fetch and ReadableStream.
 * Validates HTTP status before reading stream to handle 4xx/5xx errors as JSON.
 */
export async function streamAiChat({
  taskId,
  messages,
  signal,
  onEvent,
}: StreamAiChatOptions): Promise<void> {
  const xsrfToken = getXsrfToken() ?? ''
  const baseUrl = import.meta.env.VITE_API_BASE_URL ?? '/api'
  const url = `${baseUrl}/tasks/${taskId}/ai-chat/stream`

  let response: Response
  try {
    response = await fetch(url, {
      method: 'POST',
      credentials: 'include',
      headers: {
        'Content-Type': 'application/json',
        'X-XSRF-TOKEN': xsrfToken,
      },
      body: JSON.stringify({ messages }),
      signal,
    })
  } catch (err: unknown) {
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
      // Non-JSON error body fallback
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
        let parsedData: any = evt.data
        try {
          parsedData = JSON.parse(evt.data)
        } catch {
          // keep as string if not JSON
        }

        onEvent({ event: evt.event, data: parsedData })

        if (evt.event === 'error') {
          const errMsg =
            parsedData && typeof parsedData === 'object' && 'error' in parsedData
              ? parsedData.error
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
