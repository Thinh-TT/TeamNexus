import { beforeEach, describe, expect, it } from 'vitest'
import { useBoardStore } from '../boardStore'
import type {
  BoardResponse,
  ColumnPositionItem,
  ColumnResponse,
  CommentResponse,
  LabelResponse,
  TaskResponse,
} from '../../types/board.types'

const createMockColumns = (): ColumnResponse[] => [
  {
    id: 'col-1',
    boardId: 'board-1',
    name: 'To Do',
    position: 0,
    isDone: false,
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    tasks: [],
  },
  {
    id: 'col-2',
    boardId: 'board-1',
    name: 'Done',
    position: 1,
    isDone: true,
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    tasks: [],
  },
]

const createMockBoard = (): BoardResponse => {
  const cols = createMockColumns()
  const task1: TaskResponse = {
    id: 'task-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Task 1',
    description: 'Description 1',
    position: 0,
    priority: 'High',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    labels: [],
    commentCount: 0,
  }

  const task2: TaskResponse = {
    id: 'task-2',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Task 2',
    description: 'Description 2',
    position: 1,
    priority: 'Low',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    labels: [],
    commentCount: 2,
  }

  return {
    id: 'board-1',
    workspaceId: 'ws-1',
    name: 'Sprint Board',
    description: 'Test Board',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    columns: [
      { ...cols[0], tasks: [task2, task1] }, // intentionally out of order to test sorting
      { ...cols[1], tasks: [] },
    ],
  }
}

describe('boardStore', () => {
  beforeEach(() => {
    // Reset store state with fresh data
    useBoardStore.setState({
      board: null,
      columns: [],
      tasksByColumn: {},
      workspaceLabels: [],
      activeTask: null,
      connectionStatus: 'disconnected',
      isLoading: false,
      error: null,
    })
  })

  describe('setBoard', () => {
    it('sorts columns and tasks by position ascending', () => {
      useBoardStore.getState().setBoard(createMockBoard())
      const state = useBoardStore.getState()

      expect(state.columns).toHaveLength(2)
      expect(state.columns[0].id).toBe('col-1')
      expect(state.columns[1].id).toBe('col-2')

      const col1Tasks = state.tasksByColumn['col-1']
      expect(col1Tasks).toHaveLength(2)
      expect(col1Tasks[0].id).toBe('task-1') // position 0 first
      expect(col1Tasks[1].id).toBe('task-2') // position 1 second
    })
  })

  describe('optimisticMoveTask', () => {
    beforeEach(() => {
      useBoardStore.getState().setBoard(createMockBoard())
    })

    it('reorders task within same column and updates position indices', () => {
      // Move task-1 from position 0 to position 1
      const { rollback } = useBoardStore
        .getState()
        .optimisticMoveTask('task-1', 'col-1', 'col-1', 1)

      const col1Tasks = useBoardStore.getState().tasksByColumn['col-1']
      expect(col1Tasks[0].id).toBe('task-2')
      expect(col1Tasks[0].position).toBe(0)
      expect(col1Tasks[1].id).toBe('task-1')
      expect(col1Tasks[1].position).toBe(1)

      // Test rollback
      rollback()
      const reverted = useBoardStore.getState().tasksByColumn['col-1']
      expect(reverted[0].id).toBe('task-1')
      expect(reverted[1].id).toBe('task-2')
    })

    it('moves task to another column and sets completedAt if dest column isDone', () => {
      useBoardStore
        .getState()
        .optimisticMoveTask('task-1', 'col-1', 'col-2', 0)

      const state = useBoardStore.getState()
      expect(state.tasksByColumn['col-1']).toHaveLength(1)
      expect(state.tasksByColumn['col-1'][0].id).toBe('task-2')

      expect(state.tasksByColumn['col-2']).toHaveLength(1)
      const movedTask = state.tasksByColumn['col-2'][0]
      expect(movedTask.id).toBe('task-1')
      expect(movedTask.columnId).toBe('col-2')
      expect(movedTask.position).toBe(0)
      expect(movedTask.completedAt).not.toBeNull() // col-2 has isDone: true
    })
  })

  describe('SignalR event reducers', () => {
    beforeEach(() => {
      useBoardStore.getState().setBoard(createMockBoard())
    })

    it('applyTaskCreated: adds new task to correct column in sorted order', () => {
      const newTask: TaskResponse = {
        id: 'task-3',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'Task 3',
        description: null,
        position: 2,
        createdAt: '2026-09-09T00:00:00Z',
        updatedAt: '2026-09-09T00:00:00Z',
        labels: [],
        commentCount: 0,
      }

      useBoardStore.getState().applyTaskCreated(newTask)
      const tasks = useBoardStore.getState().tasksByColumn['col-1']
      expect(tasks).toHaveLength(3)
      expect(tasks[2].id).toBe('task-3')
    })

    it('applyTaskUpdated: updates task in column and activeTask if currently opened', () => {
      const active = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      useBoardStore.getState().setActiveTask(active)

      const updatedTask: TaskResponse = {
        ...active,
        title: 'Updated Task 1 Title',
        priority: 'Urgent',
      }

      useBoardStore.getState().applyTaskUpdated(updatedTask)

      const target = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      expect(target.title).toBe('Updated Task 1 Title')
      expect(target.priority).toBe('Urgent')
      expect(useBoardStore.getState().activeTask?.title).toBe('Updated Task 1 Title')
    })

    it('applyTaskMoved: synchronizes cross-column movement from server', () => {
      useBoardStore.getState().applyTaskMoved({
        taskId: 'task-1',
        fromColumnId: 'col-1',
        toColumnId: 'col-2',
        position: 0,
      })

      const state = useBoardStore.getState()
      expect(state.tasksByColumn['col-1']).toHaveLength(1)
      expect(state.tasksByColumn['col-2']).toHaveLength(1)
      expect(state.tasksByColumn['col-2'][0].id).toBe('task-1')
      expect(state.tasksByColumn['col-2'][0].completedAt).not.toBeNull()
    })

    it('applyTaskDeleted: removes task from column and clears activeTask if open', () => {
      const target = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      useBoardStore.getState().setActiveTask(target)

      useBoardStore.getState().applyTaskDeleted(target.id)

      const state = useBoardStore.getState()
      expect(state.tasksByColumn['col-1']).toHaveLength(1)
      expect(state.tasksByColumn['col-1'][0].id).toBe('task-2')
      expect(state.activeTask).toBeNull()
    })

    it('applyColumnCreated: appends new column and initializes its task map', () => {
      const newCol: ColumnResponse = {
        id: 'col-3',
        boardId: 'board-1',
        name: 'In Review',
        position: 2,
        isDone: false,
        createdAt: '2026-09-09T00:00:00Z',
        updatedAt: '2026-09-09T00:00:00Z',
        tasks: [],
      }

      useBoardStore.getState().applyColumnCreated(newCol)

      const state = useBoardStore.getState()
      expect(state.columns).toHaveLength(3)
      expect(state.columns[2].id).toBe('col-3')
      expect(state.tasksByColumn['col-3']).toEqual([])
    })

    it('applyColumnUpdated: renames column and updates isDone flag', () => {
      const updated: ColumnResponse = {
        ...createMockColumns()[0],
        name: 'Backlog Updated',
        isDone: false,
      }

      useBoardStore.getState().applyColumnUpdated(updated)
      expect(useBoardStore.getState().columns[0].name).toBe('Backlog Updated')
    })

    it('applyColumnsReordered: updates column order according to position list', () => {
      const reorderItems: ColumnPositionItem[] = [
        { id: 'col-1', position: 1 },
        { id: 'col-2', position: 0 },
      ]

      useBoardStore.getState().applyColumnsReordered(reorderItems)

      const state = useBoardStore.getState()
      expect(state.columns[0].id).toBe('col-2')
      expect(state.columns[1].id).toBe('col-1')
    })

    it('applyColumnDeleted: removes column and its task entries', () => {
      useBoardStore.getState().applyColumnDeleted('col-2')

      const state = useBoardStore.getState()
      expect(state.columns).toHaveLength(1)
      expect(state.columns[0].id).toBe('col-1')
      expect(state.tasksByColumn['col-2']).toBeUndefined()
    })

    it('applyCommentAdded and applyCommentDeleted: updates task commentCount', () => {
      const newComment: CommentResponse = {
        id: 'comm-1',
        taskId: 'task-1',
        authorId: 'user-1',
        authorName: 'Alice',
        content: 'Looks great',
        createdAt: '2026-09-09T00:00:00Z',
        updatedAt: '2026-09-09T00:00:00Z',
      }

      useBoardStore.getState().applyCommentAdded(newComment)
      const taskAfterAdd = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      expect(taskAfterAdd.commentCount).toBe(1)

      useBoardStore.getState().applyCommentDeleted('comm-1', 'task-1')
      const taskAfterDel = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      expect(taskAfterDel.commentCount).toBe(0)
    })

    it('attachLabelLocally and detachLabelLocally: updates task labels', () => {
      const label: LabelResponse = {
        id: 'lbl-1',
        workspaceId: 'ws-1',
        name: 'Bug',
        color: '#ef4444',
        createdAt: '2026-09-09T00:00:00Z',
      }

      useBoardStore.getState().attachLabelLocally('task-1', label)
      const taskWithLabel = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      expect(taskWithLabel.labels).toHaveLength(1)
      expect(taskWithLabel.labels[0].name).toBe('Bug')

      useBoardStore.getState().detachLabelLocally('task-1', 'lbl-1')
      const taskWithoutLabel = useBoardStore.getState().tasksByColumn['col-1'].find((t) => t.id === 'task-1')!
      expect(taskWithoutLabel.labels).toHaveLength(0)
    })
  })
})
