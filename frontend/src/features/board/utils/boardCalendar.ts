import dayjs from 'dayjs'
import type { TaskResponse } from '../types/board.types'
import { isOverdue } from './taskDueDate'
import { priorityRank } from './taskPriority'

/** Số thẻ tối đa hiển thị trong một ô lịch trước khi gộp thành `+N`. */
export const MAX_TASKS_PER_DAY_CELL = 3

/**
 * Một ngày trên lịch, đã gom thẻ và cắt bớt cho vừa ô.
 *
 * @property key Khoá ngày `YYYY-MM-DD` **theo giờ địa phương** — cũng là giá trị truyền cho
 *   `?dueFrom=`/`?dueTo=` của trang tìm kiếm. Cố ý **không** dùng ISO có `T00:00:00Z`: một chuỗi
 *   như vậy vừa khó đọc trên URL vừa dễ lệch ngày khi trình duyệt ở múi giờ khác UTC.
 * @property total Tổng số thẻ trong ngày (**không** bị cắt) — ô lịch hiện `+N` dựa trên số này.
 * @property items Thẻ hiển thị (tối đa `maxPerCell`).
 * @property overflowCount `total - items.length` khi tràn, ngược lại `0`.
 */
export interface CalendarDaySummary {
  key: string
  date: string
  total: number
  items: TaskResponse[]
  overflowCount: number
  hasOverdue: boolean
}

/**
 * Khoá ngày địa phương của một mốc ISO (`YYYY-MM-DD`).
 *
 * Dùng `dayjs(...)` (giờ địa phương) chứ **không** `toISOString().slice(0, 10)` (giờ UTC): thẻ có hạn
 * 23:30 giờ Việt Nam là `16:30Z` **cùng ngày**, nhưng thẻ hạn 07:00 giờ Việt Nam là `00:00Z` — và ở
 * múi giờ âm thì `toISOString()` sẽ đẩy nó sang **ngày hôm sau**. Người dùng nhìn hạn chót trên lịch
 * của **họ**, nên khoá phải là ngày của **họ**.
 */
export function dayKey(isoDate: string | Date): string {
  return dayjs(isoDate).format('YYYY-MM-DD')
}

/**
 * Sắp xếp thẻ trong một ô lịch theo thứ tự **tất định**: ưu tiên giảm dần → tiêu đề tăng dần → id.
 *
 * `id` là chốt chặn cuối để hai thẻ cùng tiêu đề, cùng ưu tiên vẫn có thứ tự ổn định giữa các lần
 * render — nếu không, React sẽ đảo thứ tự tùy theo thứ tự mảng đầu vào và snapshot test sẽ chập chờn.
 */
function compareForCalendar(a: TaskResponse, b: TaskResponse): number {
  const byPriority = priorityRank(b.priority) - priorityRank(a.priority)
  if (byPriority !== 0) return byPriority

  const byTitle = a.title.localeCompare(b.title, 'vi')
  if (byTitle !== 0) return byTitle

  return a.id.localeCompare(b.id)
}

/**
 * Gom thẻ theo **ngày hạn địa phương** (hàm THUẦN, không I/O).
 *
 * <para>
 * Thẻ **không có** `dueDate` bị loại khỏi lưới — lịch biểu diễn hạn chót, và một thẻ không hạn không
 * có chỗ trên đó. `TaskCalendar` hiện số lượng thẻ như vậy ở một khối riêng để chúng không biến mất
 * im lặng.
 * </para>
 *
 * @param maxPerCell Số thẻ tối đa giữ lại mỗi ngày (mặc định {@link MAX_TASKS_PER_DAY_CELL}).
 */
export function groupTasksByDueDate(
  tasks: TaskResponse[],
  now: Date = new Date(),
  maxPerCell: number = MAX_TASKS_PER_DAY_CELL
): Map<string, CalendarDaySummary> {
  const grouped = new Map<string, TaskResponse[]>()

  for (const task of tasks) {
    if (!task.dueDate) continue

    const key = dayKey(task.dueDate)
    const bucket = grouped.get(key)

    if (bucket) {
      bucket.push(task)
    } else {
      grouped.set(key, [task])
    }
  }

  const result = new Map<string, CalendarDaySummary>()
  const limit = Math.max(1, maxPerCell)

  for (const [key, bucket] of grouped) {
    const ordered = [...bucket].sort(compareForCalendar)
    const items = ordered.slice(0, limit)

    result.set(key, {
      key,
      date: key,
      total: ordered.length,
      items,
      overflowCount: ordered.length - items.length,
      // "Quá hạn" tính trên **toàn bộ** thẻ của ngày, không chỉ những thẻ được hiển thị: ô lịch phải
      // báo đỏ kể cả khi thẻ trễ hạn bị đẩy xuống hàng `+N`.
      hasOverdue: ordered.some((task) => isOverdue(task, now)),
    })
  }

  return result
}

/**
 * Thẻ **không có** hạn chót — hiển thị ở khối phụ dưới lịch.
 */
export function tasksWithoutDueDate(tasks: TaskResponse[]): TaskResponse[] {
  return tasks.filter((task) => !task.dueDate)
}

/**
 * Tháng mà lịch nên mở sẵn, suy từ chính dữ liệu (hàm THUẦN).
 *
 * <para>
 * antd `Calendar` mặc định mở **tháng hiện tại của đồng hồ máy**. Với một board toàn thẻ hạn tháng 6
 * trong khi hôm nay là tháng 9, người dùng mở tab "Lịch" và thấy một lưới **trống** — trông như tính
 * năng hỏng, dù dữ liệu vẫn còn đó ở tháng khác.
 * </para>
 *
 * <para>
 * Quy tắc: **tháng gần nhất trong tương lai** (tính từ `now`, bao gồm tháng hiện tại) nếu có; nếu mọi
 * hạn đều đã qua thì lấy **tháng mới nhất**. Không có thẻ nào có hạn ⇒ <c>null</c> để `Calendar` tự
 * quyết định (tháng hiện tại).
 * </para>
 */
export function initialCalendarMonth(tasks: TaskResponse[], now: Date = new Date()): string | null {
  const keys = [...new Set(tasks.filter((task) => task.dueDate).map((task) => dayKey(task.dueDate!)))].sort()

  if (keys.length === 0) return null

  const currentMonth = dayjs(now).format('YYYY-MM')
  const upcoming = keys.find((key) => key.slice(0, 7) >= currentMonth)

  return upcoming ?? keys[keys.length - 1]
}

