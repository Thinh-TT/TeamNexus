import { describe, expect, it } from 'vitest'
import {
  MAX_ATTACHED_TASKS,
  attachRefusalReason,
  attachTask,
  canAttach,
  detachTask,
  toRequestIds,
} from '../agentChatAttachments'

const task = (id: string, title = `Thẻ ${id}`) => ({ taskId: id, title })

describe('agentChatAttachments', () => {
  it('adds a task and keeps the order', () => {
    const list = attachTask(attachTask([], task('a')), task('b'))

    expect(list.map((t) => t.taskId)).toEqual(['a', 'b'])
  })

  it('ignores a duplicate and returns the SAME reference so no re-render is triggered', () => {
    const list = attachTask([], task('a'))

    // Identity matters: a duplicate drag must not reorder the pills or churn state.
    expect(attachTask(list, task('a'))).toBe(list)
  })

  it('enforces the cap that keeps the client from building a request the server would refuse', () => {
    let list = []
    for (let i = 0; i < MAX_ATTACHED_TASKS; i++) {
      list = attachTask(list, task(`t${i}`))
    }

    expect(list).toHaveLength(MAX_ATTACHED_TASKS)
    expect(canAttach(list)).toBe(false)
    // The ninth attachment is dropped rather than sent and rejected with a 400.
    expect(attachTask(list, task('overflow'))).toBe(list)
  })

  it('detaches by id and returns the same reference when nothing would change', () => {
    const list = attachTask(attachTask([], task('a')), task('b'))

    expect(detachTask(list, 'a').map((t) => t.taskId)).toEqual(['b'])
    expect(detachTask(list, 'missing')).toBe(list)
  })

  it('sends only ids, never the client copy of a title', () => {
    const list = attachTask([], task('a', 'Tiêu đề cũ'))

    expect(toRequestIds(list)).toEqual([{ taskId: 'a' }])
  })

  describe('attachRefusalReason', () => {
    it('explains a duplicate instead of silently doing nothing', () => {
      const list = attachTask([], task('a'))

      expect(attachRefusalReason(list, 'a')).toContain('đã được gắn')
    })

    it('explains the cap and names it', () => {
      let list = []
      for (let i = 0; i < MAX_ATTACHED_TASKS; i++) {
        list = attachTask(list, task(`t${i}`))
      }

      expect(attachRefusalReason(list, 'new')).toContain(String(MAX_ATTACHED_TASKS))
    })

    it('is null when the drop is allowed', () => {
      expect(attachRefusalReason([], 'a')).toBeNull()
    })
  })
})
