import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { saveBlob } from '../reportDownload'
import { extractErrorMessage } from '../reportError'
import { formatCount, formatDurationHours, formatPercent } from '../reportFormat'

describe('reportDownload utils', () => {
  const originalCreateObjectURL = URL.createObjectURL
  const originalRevokeObjectURL = URL.revokeObjectURL

  beforeEach(() => {
    URL.createObjectURL = vi.fn().mockReturnValue('blob:http://localhost/mock-uuid')
    URL.revokeObjectURL = vi.fn()
  })

  afterEach(() => {
    URL.createObjectURL = originalCreateObjectURL
    URL.revokeObjectURL = originalRevokeObjectURL
    vi.restoreAllMocks()
  })

  it('saveBlob creates an object URL, clicks anchor, and revokes URL in finally block', () => {
    const mockBlob = new Blob(['test content'], { type: 'text/plain' })
    const clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    const appendChildSpy = vi.spyOn(document.body, 'appendChild')
    const removeChildSpy = vi.spyOn(document.body, 'removeChild')

    saveBlob(mockBlob, 'test-report.pdf')

    expect(URL.createObjectURL).toHaveBeenCalledWith(mockBlob)
    expect(appendChildSpy).toHaveBeenCalled()
    expect(clickSpy).toHaveBeenCalled()
    expect(removeChildSpy).toHaveBeenCalled()
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:http://localhost/mock-uuid')
  })
})

describe('reportError extractErrorMessage', () => {
  it('extracts error from a JSON Blob response', async () => {
    const errorJson = JSON.stringify({ error: 'Requires Manager or Admin role in this workspace.' })
    const errorBlob = new Blob([errorJson], { type: 'application/json' })

    const err = {
      response: {
        status: 403,
        data: errorBlob,
      },
    }

    const result = await extractErrorMessage(err)
    expect(result.status).toBe(403)
    expect(result.message).toBe('Requires Manager or Admin role in this workspace.')
  })

  it('falls back to default status message when Blob is not valid JSON', async () => {
    const invalidBlob = new Blob(['<html>Internal Server Error</html>'], { type: 'text/html' })

    const err = {
      response: {
        status: 503,
        data: invalidBlob,
      },
    }

    const result = await extractErrorMessage(err)
    expect(result.status).toBe(503)
    expect(result.message).toBe('Tính năng báo cáo hiện đang tạm tắt.')
  })

  it('extracts error from a normal JSON object response', async () => {
    const err = {
      response: {
        status: 400,
        data: { error: "Unknown value for from 'xyz'. Expected ISO-8601 date-time." },
      },
    }

    const result = await extractErrorMessage(err)
    expect(result.status).toBe(400)
    expect(result.message).toBe("Unknown value for from 'xyz'. Expected ISO-8601 date-time.")
  })

  it('returns fallback message for unknown network errors without response', async () => {
    const err = new Error('Network Error')
    const result = await extractErrorMessage(err)
    expect(result.message).toBe('Network Error')
  })
})

describe('reportFormat utils', () => {
  it('formats duration hours correctly', () => {
    expect(formatDurationHours(null)).toBe('—')
    expect(formatDurationHours(undefined)).toBe('—')
    expect(formatDurationHours(12.34)).toBe('12.3 giờ')
    expect(formatDurationHours(0)).toBe('0.0 giờ')
  })

  it('formats percentage correctly', () => {
    expect(formatPercent(null)).toBe('—')
    expect(formatPercent(undefined)).toBe('—')
    expect(formatPercent(85.67)).toBe('85.7%')
    expect(formatPercent(0)).toBe('0.0%')
  })

  it('formats count correctly', () => {
    expect(formatCount(null)).toBe('0')
    expect(formatCount(undefined)).toBe('0')
    expect(formatCount(12345)).toBe((12345).toLocaleString('vi-VN'))
  })
})
