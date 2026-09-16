import { describe, expect, it } from 'vitest'
import { buildBoardSearchUrl, buildDaySearchUrl } from '../calendarNavigation'

/**
 * Giai đoạn 13 §1 — URL nối lịch với trang tìm kiếm.
 *
 * Đây là **hợp đồng** giữa hai màn hình: `TaskSearchPage` đọc `boardId`/`dueFrom`/`dueTo` từ URL và
 * chuyển thẳng cho `searchApi`. Sai định dạng ngày ở đây ⇒ người dùng bấm một ô lịch và nhận về một
 * khoảng thời gian khác (hoặc 400 từ backend), nên hình dạng chuỗi được khoá lại bằng test.
 */
describe('calendarNavigation.buildDaySearchUrl', () => {
  it('dựng URL đúng cho một ngày', () => {
    const url = buildDaySearchUrl('ws-1', 'board-1', '2026-01-20')

    expect(url).toBe('/workspaces/ws-1/search?boardId=board-1&dueFrom=2026-01-20&dueTo=2026-01-20')
  })

  it('khoá ngày là YYYY-MM-DD, KHÔNG phải ISO có T00:00:00Z', () => {
    const url = buildDaySearchUrl('ws-1', 'board-1', '2026-01-20')

    // `2026-01-20T00:00:00.000Z` bị hiểu là nửa đêm UTC = 07:00 sáng giờ Việt Nam, tức lệch nửa buổi.
    expect(url).toContain('dueFrom=2026-01-20')
    expect(url).not.toContain('T00:00:00')
    expect(url).not.toContain('%3A')
  })

  it('bỏ boardId khi không có board', () => {
    const url = buildDaySearchUrl('ws-1', undefined, '2026-01-20')

    expect(url).toBe('/workspaces/ws-1/search?dueFrom=2026-01-20&dueTo=2026-01-20')
    expect(url).not.toContain('boardId')
  })

  it('nhận cả đối tượng Date và chuẩn hoá về cùng khoá ngày', () => {
    const fromDate = buildDaySearchUrl('ws-1', 'board-1', new Date('2026-01-20T10:00:00'))

    expect(fromDate).toContain('dueFrom=2026-01-20')
  })

  it('dueFrom và dueTo luôn bằng nhau (một ngày, không phải một khoảng)', () => {
    const url = buildDaySearchUrl('ws-1', 'board-1', '2026-03-05')
    const params = new URLSearchParams(url.split('?')[1])

    expect(params.get('dueFrom')).toBe(params.get('dueTo'))
  })
})

describe('calendarNavigation.buildBoardSearchUrl', () => {
  it('mở phạm vi board, không lọc hạn', () => {
    const url = buildBoardSearchUrl('ws-1', 'board-1')

    expect(url).toBe('/workspaces/ws-1/search?boardId=board-1')
    expect(url).not.toContain('dueFrom')
  })

  it('không có board ⇒ mở tìm kiếm toàn workspace', () => {
    expect(buildBoardSearchUrl('ws-1', undefined)).toBe('/workspaces/ws-1/search')
  })
})
