import { describe, expect, it } from 'vitest'
import { timeZoneOffsetMinutes } from '../timeZoneOffsetMinutes'

/**
 * Giai đoạn 13 §4 — đảo dấu `getTimezoneOffset()`.
 *
 * Test này tồn tại vì cái sai ở đây **không** gây lỗi: gửi `-420` thay vì `+420` vẫn được backend chấp
 * nhận (nó hợp lệ và nằm trong khoảng clamp), chỉ có điều mọi thẻ bị dồn về sai ngày và biểu đồ lệch
 * 14 tiếng so với thực tế.
 */
describe('timeZoneOffsetMinutes', () => {
  it('đảo dấu: UTC+7 (getTimezoneOffset = -420) ⇒ +420', () => {
    const fake = { getTimezoneOffset: () => -420 } as unknown as Date

    expect(timeZoneOffsetMinutes(fake)).toBe(420)
  })

  it('UTC (getTimezoneOffset = 0) ⇒ 0', () => {
    const fake = { getTimezoneOffset: () => 0 } as unknown as Date

    expect(timeZoneOffsetMinutes(fake)).toBe(0)
  })

  it('Tây bán cầu (getTimezoneOffset = +300) ⇒ -300', () => {
    const fake = { getTimezoneOffset: () => 300 } as unknown as Date

    expect(timeZoneOffsetMinutes(fake)).toBe(-300)
  })

  it('UTC-11 (getTimezoneOffset = +660) ⇒ -660', () => {
    const fake = { getTimezoneOffset: () => 660 } as unknown as Date

    expect(timeZoneOffsetMinutes(fake)).toBe(-660)
  })

  it('không truyền gì ⇒ dùng đồng hồ thật và trả số hữu hạn', () => {
    const value = timeZoneOffsetMinutes()

    expect(Number.isFinite(value)).toBe(true)
    // Khoảng thực tế của thế giới: UTC-12 .. UTC+14.
    expect(value).toBeGreaterThanOrEqual(-12 * 60)
    expect(value).toBeLessThanOrEqual(14 * 60)
  })
})
