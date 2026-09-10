import { create } from 'zustand'
import type {
  BoardResponse,
  ColumnPositionItem,
  ColumnResponse,
  CommentResponse,
  HubConnectionStatus,
  LabelResponse,
  TaskMovedEventPayload,
  TaskResponse,
} from '../types/board.types'

interface BoardState {
  board: BoardResponse | null
  columns: ColumnResponse[]
  tasksByColumn: Record<string, TaskResponse[]>
  workspaceLabels: LabelResponse[]
  activeTask: TaskResponse | null
  connectionStatus: HubConnectionStatus
  isLoading: boolean
  error: string | null

  // ---- State setters ----
  setBoard: (board: BoardResponse) => void
  setWorkspaceLabels: (labels: LabelResponse[]) => void
  setConnectionStatus: (status: HubConnectionStatus) => void
  setLoading: (loading: boolean) => void
  setError: (error: string | null) => void
  setActiveTask: (task: TaskResponse | null) => void

  // ---- Optimistic Actions ----
  optimisticMoveTask: (
    taskId: string,
    sourceColId: string,
    destColId: string,
    newPosition: number
  ) => { rollback: () => void }
  revertTasksByColumn: (snapshot: Record<string, TaskResponse[]>) => void
  optimisticReorderColumns: (newColumns: ColumnResponse[]) => void

  // ---- Real-time Event Reducers ----
  applyTaskCreated: (task: TaskResponse) => void
  applyTaskUpdated: (task: TaskResponse) => void
  applyTaskMoved: (payload: TaskMovedEventPayload) => void
  applyTaskDeleted: (taskId: string) => void
  applyColumnCreated: (column: ColumnResponse) => void
  applyColumnUpdated: (column: ColumnResponse) => void
  applyColumnsReordered: (items: ColumnPositionItem[]) => void
  applyColumnDeleted: (columnId: string) => void
  applyCommentAdded: (comment: CommentResponse) => void
  applyCommentDeleted: (commentId: string, taskId: string) => void

  // ---- Local helpers for comments/labels ----
  attachLabelLocally: (taskId: string, label: LabelResponse) => void
  detachLabelLocally: (taskId: string, labelId: string) => void
}

export const useBoardStore = create<BoardState>((set, get) => ({
  board: null,
  columns: [],
  tasksByColumn: {},
  workspaceLabels: [],
  activeTask: null,
  connectionStatus: 'disconnected',
  isLoading: false,
  error: null,

  setBoard: (board) => {
    const sortedColumns = [...board.columns].sort((a, b) => a.position - b.position)
    const tasksByColumn: Record<string, TaskResponse[]> = {}

    sortedColumns.forEach((col) => {
      tasksByColumn[col.id] = [...(col.tasks ?? [])].sort((a, b) => a.position - b.position)
    })

    set({
      board,
      columns: sortedColumns,
      tasksByColumn,
      isLoading: false,
      error: null,
    })
  },

  setWorkspaceLabels: (workspaceLabels) => set({ workspaceLabels }),
  setConnectionStatus: (connectionStatus) => set({ connectionStatus }),
  setLoading: (isLoading) => set({ isLoading }),
  setError: (error) => set({ error }),
  setActiveTask: (activeTask) => set({ activeTask }),

  // ---- Optimistic Move Task ----
  optimisticMoveTask: (taskId, sourceColId, destColId, newPosition) => {
    const currentTasksByColumn = get().tasksByColumn
    const snapshot: Record<string, TaskResponse[]> = {}
    Object.keys(currentTasksByColumn).forEach((colId) => {
      snapshot[colId] = [...currentTasksByColumn[colId]]
    })

    const sourceTasks = [...(currentTasksByColumn[sourceColId] || [])]
    const destTasks =
      sourceColId === destColId ? sourceTasks : [...(currentTasksByColumn[destColId] || [])]

    const taskIndex = sourceTasks.findIndex((t) => t.id === taskId)
    if (taskIndex === -1) {
      return { rollback: () => get().revertTasksByColumn(snapshot) }
    }

    const [movedTask] = sourceTasks.splice(taskIndex, 1)

    // Check if dest column is marked as Done
    const destCol = get().columns.find((c) => c.id === destColId)
    const updatedTask: TaskResponse = {
      ...movedTask,
      columnId: destColId,
      position: newPosition,
      completedAt: destCol?.isDone ? new Date().toISOString() : null,
    }

    if (sourceColId === destColId) {
      sourceTasks.splice(newPosition, 0, updatedTask)
      const renumberedSource = sourceTasks.map((t, idx) => ({ ...t, position: idx }))
      set({
        tasksByColumn: {
          ...currentTasksByColumn,
          [sourceColId]: renumberedSource,
        },
      })
    } else {
      destTasks.splice(newPosition, 0, updatedTask)
      const renumberedSource = sourceTasks.map((t, idx) => ({ ...t, position: idx }))
      const renumberedDest = destTasks.map((t, idx) => ({ ...t, position: idx }))
      set({
        tasksByColumn: {
          ...currentTasksByColumn,
          [sourceColId]: renumberedSource,
          [destColId]: renumberedDest,
        },
      })
    }

    return {
      rollback: () => get().revertTasksByColumn(snapshot),
    }
  },

  revertTasksByColumn: (snapshot) => {
    set({ tasksByColumn: snapshot })
  },

  optimisticReorderColumns: (newColumns) => {
    set({ columns: newColumns })
  },

  // ---- Real-time Event Reducers ----
  applyTaskCreated: (task) => {
    const { tasksByColumn, activeTask } = get()
    const colTasks = tasksByColumn[task.columnId] ? [...tasksByColumn[task.columnId]] : []

    // Avoid duplicate if already exists
    const existingIndex = colTasks.findIndex((t) => t.id === task.id)
    if (existingIndex !== -1) {
      colTasks[existingIndex] = task
    } else {
      colTasks.push(task)
    }
    colTasks.sort((a, b) => a.position - b.position)

    set({
      tasksByColumn: {
        ...tasksByColumn,
        [task.columnId]: colTasks,
      },
      activeTask: activeTask?.id === task.id ? task : activeTask,
    })
  },

  applyTaskUpdated: (task) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    // Find and remove task from any previous column, then add to task.columnId
    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      if (colId === task.columnId) {
        const index = tasks.findIndex((t) => t.id === task.id)
        if (index !== -1) {
          const updated = [...tasks]
          updated[index] = task
          updated.sort((a, b) => a.position - b.position)
          newTasksByColumn[colId] = updated
        } else {
          const updated = [...tasks, task]
          updated.sort((a, b) => a.position - b.position)
          newTasksByColumn[colId] = updated
        }
      } else {
        newTasksByColumn[colId] = tasks.filter((t) => t.id !== task.id)
      }
    })

    set({
      tasksByColumn: newTasksByColumn,
      activeTask: activeTask?.id === task.id ? task : activeTask,
    })
  },

  applyTaskMoved: ({ taskId, fromColumnId, toColumnId, position }) => {
    const { tasksByColumn, columns, activeTask } = get()
    const fromTasks = tasksByColumn[fromColumnId] ? [...tasksByColumn[fromColumnId]] : []
    const toTasks =
      fromColumnId === toColumnId
        ? fromTasks
        : tasksByColumn[toColumnId]
        ? [...tasksByColumn[toColumnId]]
        : []

    // Locate the task
    let task: TaskResponse | undefined
    const fromIndex = fromTasks.findIndex((t) => t.id === taskId)
    if (fromIndex !== -1) {
      ;[task] = fromTasks.splice(fromIndex, 1)
    } else {
      // Look across other columns in case fromColumnId was out of sync
      for (const colId of Object.keys(tasksByColumn)) {
        const idx = tasksByColumn[colId].findIndex((t) => t.id === taskId)
        if (idx !== -1) {
          task = tasksByColumn[colId][idx]
          tasksByColumn[colId] = tasksByColumn[colId].filter((t) => t.id !== taskId)
          break
        }
      }
    }

    if (!task) return

    const destCol = columns.find((c) => c.id === toColumnId)
    const updatedTask: TaskResponse = {
      ...task,
      columnId: toColumnId,
      position,
      completedAt: destCol?.isDone ? task.completedAt || new Date().toISOString() : null,
    }

    if (fromColumnId === toColumnId) {
      fromTasks.splice(position, 0, updatedTask)
      const renumberedFrom = fromTasks.map((t, i) => ({ ...t, position: i }))
      set({
        tasksByColumn: {
          ...tasksByColumn,
          [toColumnId]: renumberedFrom,
        },
        activeTask: activeTask?.id === taskId ? updatedTask : activeTask,
      })
    } else {
      toTasks.splice(position, 0, updatedTask)
      const renumberedFrom = fromTasks.map((t, i) => ({ ...t, position: i }))
      const renumberedTo = toTasks.map((t, i) => ({ ...t, position: i }))
      set({
        tasksByColumn: {
          ...tasksByColumn,
          [fromColumnId]: renumberedFrom,
          [toColumnId]: renumberedTo,
        },
        activeTask: activeTask?.id === taskId ? updatedTask : activeTask,
      })
    }
  },

  applyTaskDeleted: (taskId) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      newTasksByColumn[colId] = tasks.filter((t) => t.id !== taskId)
    })

    set({
      tasksByColumn: newTasksByColumn,
      activeTask: activeTask?.id === taskId ? null : activeTask,
    })
  },

  applyColumnCreated: (column) => {
    const { columns, tasksByColumn } = get()
    if (columns.some((c) => c.id === column.id)) return

    const newColumns = [...columns, column].sort((a, b) => a.position - b.position)
    set({
      columns: newColumns,
      tasksByColumn: {
        ...tasksByColumn,
        [column.id]: column.tasks ? [...column.tasks] : [],
      },
    })
  },

  applyColumnUpdated: (column) => {
    const { columns } = get()
    const newColumns = columns
      .map((c) => (c.id === column.id ? { ...c, name: column.name, isDone: column.isDone } : c))
      .sort((a, b) => a.position - b.position)

    set({ columns: newColumns })
  },

  applyColumnsReordered: (items) => {
    const { columns } = get()
    const posMap = new Map(items.map((i) => [i.id, i.position]))

    const newColumns = columns
      .map((c) => ({
        ...c,
        position: posMap.has(c.id) ? posMap.get(c.id)! : c.position,
      }))
      .sort((a, b) => a.position - b.position)

    set({ columns: newColumns })
  },

  applyColumnDeleted: (columnId) => {
    const { columns, tasksByColumn } = get()
    const newColumns = columns.filter((c) => c.id !== columnId)
    const newTasksByColumn = { ...tasksByColumn }
    delete newTasksByColumn[columnId]

    set({
      columns: newColumns,
      tasksByColumn: newTasksByColumn,
    })
  },

  applyCommentAdded: (comment) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      newTasksByColumn[colId] = tasks.map((t) =>
        t.id === comment.taskId ? { ...t, commentCount: t.commentCount + 1 } : t
      )
    })

    set({
      tasksByColumn: newTasksByColumn,
      activeTask:
        activeTask?.id === comment.taskId
          ? { ...activeTask, commentCount: activeTask.commentCount + 1 }
          : activeTask,
    })
  },

  applyCommentDeleted: (_commentId, taskId) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      newTasksByColumn[colId] = tasks.map((t) =>
        t.id === taskId ? { ...t, commentCount: Math.max(0, t.commentCount - 1) } : t
      )
    })

    set({
      tasksByColumn: newTasksByColumn,
      activeTask:
        activeTask?.id === taskId
          ? { ...activeTask, commentCount: Math.max(0, activeTask.commentCount - 1) }
          : activeTask,
    })
  },

  attachLabelLocally: (taskId, label) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      newTasksByColumn[colId] = tasks.map((t) => {
        if (t.id === taskId) {
          if (t.labels.some((l) => l.id === label.id)) return t
          return { ...t, labels: [...t.labels, label] }
        }
        return t
      })
    })

    const updatedActive =
      activeTask?.id === taskId
        ? {
            ...activeTask,
            labels: activeTask.labels.some((l) => l.id === label.id)
              ? activeTask.labels
              : [...activeTask.labels, label],
          }
        : activeTask

    set({ tasksByColumn: newTasksByColumn, activeTask: updatedActive })
  },

  detachLabelLocally: (taskId, labelId) => {
    const { tasksByColumn, activeTask } = get()
    const newTasksByColumn: Record<string, TaskResponse[]> = {}

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      newTasksByColumn[colId] = tasks.map((t) => {
        if (t.id === taskId) {
          return { ...t, labels: t.labels.filter((l) => l.id !== labelId) }
        }
        return t
      })
    })

    const updatedActive =
      activeTask?.id === taskId
        ? { ...activeTask, labels: activeTask.labels.filter((l) => l.id !== labelId) }
        : activeTask

    set({ tasksByColumn: newTasksByColumn, activeTask: updatedActive })
  },
}))
