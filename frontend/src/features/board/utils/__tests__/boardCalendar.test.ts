import { describe, expect, it } from 'vitest'
import dayjs from 'dayjs'
import type { TaskResponse } from '../../types/board.types'
import { dayKey, groupTasksByDueDate, initialCalendarMonth, tasksWithoutDueDate } from '../boardCalendar'

/**
 * Giai đoạn 13 §1 — gom thẻ theo ngày hạn (hàm thuần).
 *
 * Mọi mốc thời gian ở đây đều **có offset tường minh** vì chính múi giờ là thứ dễ sai nhất: `dayjs` đọc
 * `dueDate` theo **giờ địa phương**, nên một mốc `2026-01-20T17:00:00Z` ở UTC+7 là ngày **21**. Test
 * khoá hành vi đó lại để một lần "tối ưu" bằng `toISOString().slice(0, 10)` sẽ đỏ ngay.
 */
const task = (over: Partial<TaskResponse> & { id: string }): TaskResponse => ({
  boardId: 'board-1',
  columnId: 'col-1',
  title: `Task ${over.id}`,
  description: null,
  position: 0,
  assigneeId: null,
  assigneeName: null,
  dueDate: null,
  priority: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  completedAt: null,
  labels: [],
  commentCount: 0,
  assigneeIsAiAgent: false,
  activeAgentRunId: null,
  ...over,
})

/** Một mốc "bây giờ" cố định để nhãn quá hạn không phụ thuộc ngày chạy test. */
const NOW = new Date('2026-01-15T12:00:00Z')

describe('boardCalendar.groupTasksByDueDate', () => {
  it('gom nhiều thẻ cùng một ngày vào một ô', () => {
    const days = groupTasksByDueDate(
      [
        task({ id: 'a', dueDate: '2026-01-20T09:00:00Z' }),
        task({ id: 'b', dueDate: '2026-01-20T15:00:00Z' }),
      ],
      NOW
    )

    expect(days.size).toBe(1)
    expect(days.get('2026-01-20')?.total).toBe(2)
  })

  it('loại thẻ không có hạn chót khỏi lưới', () => {
    const tasks = [
      task({ id: 'a', dueDate: '2026-01-20T09:00:00Z' }),
      task({ id: 'b', dueDate: null }),
    ]

    const days = groupTasksByDueDate(tasks, NOW)

    expect(days.size).toBe(1)
    expect(days.get('2026-01-20')?.total).toBe(1)

    // …và chúng được đếm riêng để không biến mất im lặng.
    expect(tasksWithoutDueDate(tasks).map((t) => t.id)).toEqual(['b'])
  })

  it('hai thẻ khác giờ trong cùng một ngày ĐỊA PHƯƠNG nằm cùng một ô', () => {
    // 07:00 và 23:00 giờ Việt Nam (UTC+7) — cả hai đều là ngày 20 theo lịch của người dùng.
    const days = groupTasksByDueDate(
      [
        task({ id: 'a', dueDate: '2026-01-20T00:00:00Z' }), // 07:00 VN
        task({ id: 'b', dueDate: '2026-01-20T16:00:00Z' }), // 23:00 VN
      ],
      NOW
    )

    expect([...days.keys()]).toEqual(['2026-01-20'])
    expect(days.get('2026-01-20')?.total).toBe(2)
  })

  it('khoá là ngày ĐỊA PHƯƠNG của mốc ISO, không phải ngày UTC', () => {
    // 23:30 UTC ngày 20 ⇒ ở UTC+7 đã là 06:30 ngày **21**. Test này chạy dưới TZ của tiến trình
    // Vitest (xem `setupFiles`), nên mốc dưới đây được chọn để cùng một khẳng định đúng ở cả hai phía:
    // khoá phải là ngày mà `dayjs` (giờ địa phương) nhìn thấy.
    const iso = '2026-01-20T23:30:00Z'

    expect(dayKey(iso)).toBe(dayjs(iso).format('YYYY-MM-DD'))

    // Và mốc **không** offset (giờ tường minh) giữ nguyên ngày trên nhãn.
    expect(dayKey('2026-01-20T23:30:00')).toBe('2026-01-20')
  })

  it('cắt bớt danh sách và báo đúng số thẻ tràn', () => {
    const tasks = Array.from({ length: 7 }, (_, index) =>
      task({ id: `t${index}`, dueDate: '2026-01-20T09:00:00Z' })
    )

    const summary = groupTasksByDueDate(tasks, NOW).get('2026-01-20')

    expect(summary?.total).toBe(7)
    expect(summary?.items).toHaveLength(3)
    expect(summary?.overflowCount).toBe(4)
  })

  it('tôn trọng giới hạn ô tuỳ chỉnh', () => {
    const tasks = Array.from({ length: 4 }, (_, index) =>
      task({ id: `t${index}`, dueDate: '2026-01-20T09:00:00Z' })
    )

    const summary = groupTasksByDueDate(tasks, NOW, 2).get('2026-01-20')

    expect(summary?.items).toHaveLength(2)
    expect(summary?.overflowCount).toBe(2)
  })

  it('đánh dấu ngày có thẻ quá hạn', () => {
    const days = groupTasksByDueDate(
      [
        task({ id: 'late', dueDate: '2026-01-10T09:00:00Z' }),
        task({ id: 'future', dueDate: '2026-01-25T09:00:00Z' }),
      ],
      NOW
    )

    expect(days.get('2026-01-10')?.hasOverdue).toBe(true)
    expect(days.get('2026-01-25')?.hasOverdue).toBe(false)
  })

  it('thẻ đã xong KHÔNG bị coi là quá hạn', () => {
    const days = groupTasksByDueDate(
      [task({ id: 'done', dueDate: '2026-01-10T09:00:00Z', completedAt: '2026-01-09T09:00:00Z' })],
      NOW
    )

    expect(days.get('2026-01-10')?.hasOverdue).toBe(false)
  })

  it('báo quá hạn kể cả khi thẻ trễ hạn bị đẩy xuống hàng "+N"', () => {
    // Thẻ trễ hạn nằm ở ngày 10; ngày 11 chỉ có thẻ MỚI. `hasOverdue` phải phản ánh **nội dung của
    // chính ngày đó** — một ngày toàn thẻ mới không được báo đỏ chỉ vì "ở đâu đó trong board có thẻ trễ".
    const days = groupTasksByDueDate(
      [
        task({ id: 'a', title: 'A', priority: 'Urgent', dueDate: '2026-01-25T09:00:00Z' }),
        task({ id: 'b', title: 'B', priority: 'High', dueDate: '2026-01-25T09:00:00Z' }),
        task({ id: 'c', title: 'C', priority: 'Medium', dueDate: '2026-01-25T09:00:00Z' }),
        task({ id: 'z', title: 'Z', priority: 'Low', dueDate: '2026-01-10T09:00:00Z' }),
      ],
      NOW
    )

    const late = days.get('2026-01-10')
    expect(late?.total).toBe(1)
    expect(late?.overflowCount).toBe(0)
    expect(late?.hasOverdue).toBe(true)

    const future = days.get('2026-01-25')
    expect(future?.total).toBe(3)
    expect(future?.overflowCount).toBe(0)
    expect(future?.hasOverdue).toBe(false)
  })

  it('ô báo quá hạn ngay cả khi thẻ trễ hạn nằm ngoài danh sách hiển thị', () => {
    // 5 thẻ cùng ngày, thẻ trễ hạn xếp CUỐI (ưu tiên thấp) ⇒ bị cắt khỏi `items`, nhưng ô vẫn phải đỏ.
    const days = groupTasksByDueDate(
      [
        task({ id: 'a', title: 'A', priority: 'Urgent', dueDate: '2026-01-11T09:00:00Z' }),
        task({ id: 'b', title: 'B', priority: 'High', dueDate: '2026-01-11T09:00:00Z' }),
        task({ id: 'c', title: 'C', priority: 'Medium', dueDate: '2026-01-11T09:00:00Z' }),
        task({ id: 'd', title: 'D', priority: 'Medium', dueDate: '2026-01-11T09:00:00Z' }),
        task({ id: 'z', title: 'Z', priority: 'Low', dueDate: '2026-01-10T09:00:00Z' }),
      ],
      NOW
    )

    const summary = days.get('2026-01-11')

    expect(summary?.total).toBe(4)
    expect(summary?.items.map((t) => t.id)).toEqual(['a', 'b', 'c'])
    expect(summary?.overflowCount).toBe(1)
    expect(summary?.hasOverdue).toBe(true)
  })

  it('sắp xếp trong ô theo ưu tiên giảm dần, rồi tiêu đề — tất định', () => {
    const days = groupTasksByDueDate(
      [
        task({ id: 'low', title: 'Zebra', priority: 'Low', dueDate: '2026-01-20T09:00:00Z' }),
        task({ id: 'urgent', title: 'Alpha', priority: 'Urgent', dueDate: '2026-01-20T09:00:00Z' }),
        task({ id: 'none', title: 'Middle', priority: null, dueDate: '2026-01-20T09:00:00Z' }),
      ],
      NOW
    )

    expect(days.get('2026-01-20')?.items.map((t) => t.id)).toEqual(['urgent', 'none', 'low'])
  })

  it('hai thẻ cùng tiêu đề và cùng ưu tiên vẫn có thứ tự ổn định', () => {
    const days = groupTasksByDueDate(
      [
        task({ id: 'b', title: 'Same', priority: 'High', dueDate: '2026-01-20T09:00:00Z' }),
        task({ id: 'a', title: 'Same', priority: 'High', dueDate: '2026-01-20T09:00:00Z' }),
      ],
      NOW
    )

    expect(days.get('2026-01-20')?.items.map((t) => t.id)).toEqual(['a', 'b'])
  })

  it('mảng rỗng trả Map rỗng (không ném)', () => {
    expect(groupTasksByDueDate([], NOW).size).toBe(0)
    expect(tasksWithoutDueDate([])).toEqual([])
  })
})

describe('boardCalendar.initialCalendarMonth', () => {
  it('chọn tháng gần nhất còn hạn trong tương lai', () => {
    // Hôm nay là 2026-01-15; board có thẻ hạn tháng 3 và tháng 5 ⇒ mở tháng 3 (gần nhất).
    const month = initialCalendarMonth(
      [
        task({ id: 'a', dueDate: '2026-05-10T09:00:00' }),
        task({ id: 'b', dueDate: '2026-03-02T09:00:00' }),
      ],
      NOW
    )

    expect(month).toBe('2026-03-02')
  })

  it('bỏ qua hạn đã qua nếu còn hạn trong tương lai', () => {
    const month = initialCalendarMonth(
      [
        task({ id: 'past', dueDate: '2025-12-01T09:00:00' }),
        task({ id: 'future', dueDate: '2026-02-01T09:00:00' }),
      ],
      NOW
    )

    expect(month).toBe('2026-02-01')
  })

  it('mọi hạn đều đã qua ⇒ lấy tháng mới nhất (không mở lưới trống)', () => {
    const month = initialCalendarMonth(
      [
        task({ id: 'older', dueDate: '2025-07-01T09:00:00' }),
        task({ id: 'newer', dueDate: '2025-11-20T09:00:00' }),
      ],
      NOW
    )

    expect(month).toBe('2025-11-20')
  })

  it('tính cả tháng hiện tại là "còn hạn"', () => {
    const month = initialCalendarMonth([task({ id: 'a', dueDate: '2026-01-20T09:00:00' })], NOW)

    expect(month).toBe('2026-01-20')
  })

  it('không có thẻ nào có hạn ⇒ null (để Calendar tự quyết định)', () => {
    expect(initialCalendarMonth([task({ id: 'a', dueDate: null })], NOW)).toBeNull()
    expect(initialCalendarMonth([], NOW)).toBeNull()
  })
})
