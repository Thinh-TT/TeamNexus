import { describe, expect, it } from 'vitest'
import {
  COLD_START_MESSAGE,
  COLD_START_THRESHOLD_MS,
  isColdStartLikely,
  nextRetryDelay,
  RECONNECT_MAX_DELAY_MS,
} from '../reconnectPolicy'

describe('reconnectPolicy', () => {
  it('follows standard backoff schedule for retry counts 0 to 4', () => {
    expect(nextRetryDelay(0)).toBe(0)
    expect(nextRetryDelay(1)).toBe(2000)
    expect(nextRetryDelay(2)).toBe(5000)
    expect(nextRetryDelay(3)).toBe(10000)
    expect(nextRetryDelay(4)).toBe(30000)
  })

  it('caps at RECONNECT_MAX_DELAY_MS (30000ms) and never returns null or gives up for subsequent retries', () => {
    expect(nextRetryDelay(5)).toBe(RECONNECT_MAX_DELAY_MS)
    expect(nextRetryDelay(6)).toBe(RECONNECT_MAX_DELAY_MS)
    expect(nextRetryDelay(100)).toBe(RECONNECT_MAX_DELAY_MS)
    expect(nextRetryDelay(10_000)).toBe(RECONNECT_MAX_DELAY_MS)
  })

  it('handles abnormal or invalid inputs deterministically with safe finite values >= 0', () => {
    expect(nextRetryDelay(-1)).toBe(0)
    expect(nextRetryDelay(-999)).toBe(0)
    expect(nextRetryDelay(Number.NaN)).toBe(0)
    expect(nextRetryDelay(Number.MAX_SAFE_INTEGER)).toBe(RECONNECT_MAX_DELAY_MS)
  })

  it('evaluates isColdStartLikely correctly based on retry count threshold', () => {
    expect(isColdStartLikely(0)).toBe(false)
    expect(isColdStartLikely(1)).toBe(false)
    expect(isColdStartLikely(2)).toBe(false)

    expect(isColdStartLikely(3)).toBe(true)
    expect(isColdStartLikely(4)).toBe(true)
    expect(isColdStartLikely(10)).toBe(true)

    expect(isColdStartLikely(-1)).toBe(false)
    expect(isColdStartLikely(Number.NaN)).toBe(false)
  })

  it('provides constants for cold-start UX', () => {
    expect(COLD_START_THRESHOLD_MS).toBe(5000)
    expect(COLD_START_MESSAGE).toContain('Máy chủ đang khởi động lại')
    expect(COLD_START_MESSAGE).toContain('1 phút')
  })
})
