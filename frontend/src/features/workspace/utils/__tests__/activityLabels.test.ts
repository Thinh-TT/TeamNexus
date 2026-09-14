import { describe, expect, it } from 'vitest'
import {
  getActivityActionLabel,
  getActivityActorName,
  getActivityPayloadSummary,
} from '../activityLabels'
import type { WorkspaceActivityItem } from '../../types/workspace.types'

describe('activityLabels utils', () => {
  it('maps known activity action codes to standard Vietnamese labels', () => {
    expect(getActivityActionLabel('TaskCreated')).toBe('đã tạo thẻ')
    expect(getActivityActionLabel('TaskUpdated')).toBe('đã cập nhật thẻ')
    expect(getActivityActionLabel('TaskMoved')).toBe('đã chuyển thẻ')
    expect(getActivityActionLabel('TaskCompleted')).toBe('đã hoàn thành thẻ')
    expect(getActivityActionLabel('TaskDeleted')).toBe('đã xoá thẻ')
    expect(getActivityActionLabel('CommentAdded')).toBe('đã bình luận')
    expect(getActivityActionLabel('WorkspaceUpdated')).toBe('đã cập nhật workspace')
    expect(getActivityActionLabel('WorkspaceOwnerTransferred')).toBe('đã chuyển quyền sở hữu')
    expect(getActivityActionLabel('WorkspaceDeleted')).toBe('đã xoá workspace')
  })

  it('safely falls back to the raw action string when given an unknown action', () => {
    expect(getActivityActionLabel('CustomActionTriggered')).toBe('CustomActionTriggered')
    expect(getActivityActionLabel('')).toBe('')
  })

  it('returns "Hệ thống" when actor is null or undefined', () => {
    expect(getActivityActorName({ userId: null, userDisplayName: null })).toBe('Hệ thống')
    expect(getActivityActorName({ userId: 'u-1', userDisplayName: null })).toBe('Hệ thống')
    expect(getActivityActorName({ userId: null, userDisplayName: 'Admin' })).toBe('Hệ thống')
    expect(getActivityActorName({ userId: 'u-1', userDisplayName: 'Nguyễn Văn A' })).toBe('Nguyễn Văn A')
  })

  it('handles null, undefined, or empty payload without throwing', () => {
    const item: WorkspaceActivityItem = {
      id: 'act-1',
      boardId: null,
      userId: 'u-1',
      userDisplayName: 'Alex',
      entityType: 'Task',
      entityId: 't-1',
      action: 'TaskCreated',
      payload: null,
      createdAt: '2026-09-14T00:00:00Z',
    }
    expect(getActivityPayloadSummary(item)).toBeNull()
  })

  it('extracts human-readable payload summary for TaskMoved and TaskUpdated', () => {
    const moveItem: WorkspaceActivityItem = {
      id: 'act-2',
      boardId: 'b-1',
      userId: 'u-1',
      userDisplayName: 'Alex',
      entityType: 'Task',
      entityId: 't-1',
      action: 'TaskMoved',
      payload: { fromColumnName: 'Cần làm', toColumnName: 'Đang làm' },
      createdAt: '2026-09-14T00:00:00Z',
    }
    expect(getActivityPayloadSummary(moveItem)).toBe('từ "Cần làm" sang "Đang làm"')

    const updateItem: WorkspaceActivityItem = {
      id: 'act-3',
      boardId: 'b-1',
      userId: 'u-1',
      userDisplayName: 'Alex',
      entityType: 'Task',
      entityId: 't-1',
      action: 'TaskUpdated',
      payload: { priority: 'Urgent', dueDate: '2026-09-20T00:00:00Z' },
      createdAt: '2026-09-14T00:00:00Z',
    }
    expect(getActivityPayloadSummary(updateItem)).toBe('ưu tiên Urgent, hạn chót')
  })
})
