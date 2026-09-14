import dayjs from 'dayjs'
import type { TaskResponse } from '../types/board.types'

/** Một "task đã xong" không bao giờ bị coi là quá hạn. */
export function isTaskCompleted(
  task: Pick<TaskResponse, 'completedAt'>,
  isDoneColumn?: boolean
): boolean {
  return Boolean(isDoneColumn || task.completedAt)
}

/** So sánh theo NGÀY (không theo giờ) — hạn hôm nay KHÔNG phải quá hạn. */
export function isOverdue(
  task: Pick<TaskResponse, 'dueDate' | 'completedAt'>,
  now: Date,
  isDoneColumn?: boolean
): boolean {
  if (!task.dueDate) return false
  if (isTaskCompleted(task, isDoneColumn)) return false

  return dayjs(task.dueDate).startOf('day').isBefore(dayjs(now).startOf('day'))
}

/** Số ngày quá hạn (>= 1 khi quá hạn, 0 khi không). */
export function overdueDays(
  task: Pick<TaskResponse, 'dueDate' | 'completedAt'>,
  now: Date,
  isDoneColumn?: boolean
): number {
  if (!isOverdue(task, now, isDoneColumn)) return 0

  const diff = dayjs(now).startOf('day').diff(dayjs(task.dueDate).startOf('day'), 'day')
  return Math.max(0, diff)
}

/** Nhãn hiển thị: 'Quá hạn N ngày' | 'Hôm nay' | 'DD/MM' | null (khi không có hạn). */
export function dueDateLabel(
  task: Pick<TaskResponse, 'dueDate' | 'completedAt'>,
  now: Date,
  isDoneColumn?: boolean
): string | null {
  if (!task.dueDate) return null

  if (isOverdue(task, now, isDoneColumn)) {
    const days = overdueDays(task, now, isDoneColumn)
    return `Quá hạn ${days} ngày`
  }

  const isToday = dayjs(task.dueDate).startOf('day').isSame(dayjs(now).startOf('day'))
  if (isToday) {
    return 'Hôm nay'
  }

  return dayjs(task.dueDate).format('DD/MM')
}
