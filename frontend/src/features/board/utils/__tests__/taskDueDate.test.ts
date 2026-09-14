import { describe, expect, it } from 'vitest'
import {
  dueDateLabel,
  isOverdue,
  isTaskCompleted,
  overdueDays,
} from '../taskDueDate'

describe('taskDueDate utils', () => {
  const baseNow = new Date('2026-09-14T10:00:00.000Z')

  it('returns false and null when task has no dueDate', () => {
    const task = { dueDate: null, completedAt: null }
    expect(isOverdue(task, baseNow)).toBe(false)
    expect(overdueDays(task, baseNow)).toBe(0)
    expect(dueDateLabel(task, baseNow)).toBeNull()
  })

  it('is not overdue when due date is today (even if earlier in the day)', () => {
    const task = { dueDate: '2026-09-14T02:00:00.000Z', completedAt: null }
    expect(isOverdue(task, baseNow)).toBe(false)
    expect(overdueDays(task, baseNow)).toBe(0)
    expect(dueDateLabel(task, baseNow)).toBe('Hôm nay')
  })

  it('is overdue by 1 day when due date was yesterday', () => {
    const task = { dueDate: '2026-09-13T15:00:00.000Z', completedAt: null }
    expect(isOverdue(task, baseNow)).toBe(true)
    expect(overdueDays(task, baseNow)).toBe(1)
    expect(dueDateLabel(task, baseNow)).toBe('Quá hạn 1 ngày')
  })

  it('is overdue by 3 days when due date was 3 days ago', () => {
    const task = { dueDate: '2026-09-11T09:00:00.000Z', completedAt: null }
    expect(isOverdue(task, baseNow)).toBe(true)
    expect(overdueDays(task, baseNow)).toBe(3)
    expect(dueDateLabel(task, baseNow)).toBe('Quá hạn 3 ngày')
  })

  it('is not overdue when task has completedAt even if due date is in the past', () => {
    const task = {
      dueDate: '2026-09-10T10:00:00.000Z',
      completedAt: '2026-09-12T10:00:00.000Z',
    }
    expect(isOverdue(task, baseNow)).toBe(false)
    expect(overdueDays(task, baseNow)).toBe(0)
    expect(dueDateLabel(task, baseNow)).toBe('10/09')
  })

  it('is not overdue when task is in a done column', () => {
    const task = { dueDate: '2026-09-10T10:00:00.000Z', completedAt: null }
    expect(isOverdue(task, baseNow, true)).toBe(false)
    expect(overdueDays(task, baseNow, true)).toBe(0)
    expect(dueDateLabel(task, baseNow, true)).toBe('10/09')
  })

  it('formats future due dates as DD/MM', () => {
    const task = { dueDate: '2026-09-20T08:00:00.000Z', completedAt: null }
    expect(isOverdue(task, baseNow)).toBe(false)
    expect(overdueDays(task, baseNow)).toBe(0)
    expect(dueDateLabel(task, baseNow)).toBe('20/09')
  })

  it('handles midnight boundary comparisons accurately', () => {
    // 23:59 on day 13 vs 00:01 on day 14 in the active timezone
    const task = {
      dueDate: new Date(2026, 8, 13, 23, 59, 0).toISOString(),
      completedAt: null,
    }
    const now = new Date(2026, 8, 14, 0, 1, 0)
    expect(isOverdue(task, now)).toBe(true)
    expect(overdueDays(task, now)).toBe(1)
  })

  it('evaluates isTaskCompleted correctly for completedAt and isDoneColumn', () => {
    expect(isTaskCompleted({ completedAt: null })).toBe(false)
    expect(isTaskCompleted({ completedAt: null }, false)).toBe(false)
    expect(isTaskCompleted({ completedAt: '2026-09-14T00:00:00.000Z' })).toBe(true)
    expect(isTaskCompleted({ completedAt: null }, true)).toBe(true)
    expect(isTaskCompleted({ completedAt: '2026-09-14T00:00:00.000Z' }, true)).toBe(true)
  })
})
