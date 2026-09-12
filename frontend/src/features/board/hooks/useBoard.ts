import { useCallback, useEffect } from 'react'
import { message } from 'antd'
import { boardApi } from '../services/boardApi'
import { useBoardStore } from '../stores/boardStore'
import type {
  ColumnPositionItem,
  CreateColumnRequest,
  CreateCommentRequest,
  CreateLabelRequest,
  CreateTaskRequest,
  UpdateColumnRequest,
  UpdateCommentRequest,
  UpdateTaskRequest,
} from '../types/board.types'
import { useBoardHub } from './useBoardHub'

export const useBoard = (workspaceId?: string, boardId?: string) => {
  const {
    board,
    columns,
    tasksByColumn,
    workspaceLabels,
    activeTask,
    connectionStatus,
    isLoading,
    error,
    setBoard,
    setWorkspaceLabels,
    setLoading,
    setError,
    setActiveTask,
    optimisticMoveTask,
    optimisticReorderColumns,
    attachLabelLocally,
    detachLabelLocally,
    applyTaskCreated,
    applyTaskUpdated,
    applyTaskDeleted,
    applyColumnCreated,
    applyColumnUpdated,
    applyColumnDeleted,
  } = useBoardStore()

  // Fetch Board and Labels
  const fetchBoardData = useCallback(async () => {
    if (!workspaceId || !boardId) return

    setLoading(true)
    setError(null)

    try {
      const [boardData, labelsData] = await Promise.all([
        boardApi.getBoard(workspaceId, boardId),
        boardApi.getLabels(workspaceId).catch(() => []),
      ])

      setBoard(boardData)
      setWorkspaceLabels(labelsData)
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải dữ liệu bảng'
      setError(errorMsg)
      message.error(errorMsg)
    } finally {
      setLoading(false)
    }
  }, [workspaceId, boardId, setBoard, setWorkspaceLabels, setLoading, setError])

  // Connect SignalR hub with automatic reconnect and refetch on reconnect
  const { reconnect } = useBoardHub(boardId, fetchBoardData)

  useEffect(() => {
    fetchBoardData()
  }, [fetchBoardData])

  // ---- Columns Actions ----
  const handleCreateColumn = async (data: CreateColumnRequest) => {
    if (!boardId) return
    try {
      const created = await boardApi.createColumn(boardId, data)
      applyColumnCreated(created)
      message.success(`Đã tạo cột "${created.name}"`)
      return created
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Tạo cột thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleUpdateColumn = async (columnId: string, data: UpdateColumnRequest) => {
    if (!boardId) return
    try {
      const updated = await boardApi.updateColumn(boardId, columnId, data)
      applyColumnUpdated(updated)
      message.success('Cập nhật cột thành công')
      return updated
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Cập nhật cột thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleReorderColumns = async (items: ColumnPositionItem[]) => {
    if (!boardId) return
    const prevColumns = [...columns]
    const posMap = new Map(items.map((i) => [i.id, i.position]))
    const newColumns = columns
      .map((c) => ({
        ...c,
        position: posMap.has(c.id) ? posMap.get(c.id)! : c.position,
      }))
      .sort((a, b) => a.position - b.position)

    optimisticReorderColumns(newColumns)

    try {
      await boardApi.reorderColumns(boardId, { items })
    } catch (err: unknown) {
      optimisticReorderColumns(prevColumns)
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Sắp xếp cột thất bại'
      message.error(errorMsg)
    }
  }

  const handleDeleteColumn = async (columnId: string) => {
    if (!boardId) return
    try {
      await boardApi.deleteColumn(boardId, columnId)
      applyColumnDeleted(columnId)
      message.success('Đã xoá cột thành công')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string }; status?: number } })?.response?.data
          ?.error ?? 'Xoá cột thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  // ---- Tasks Actions ----
  const handleCreateTask = async (data: CreateTaskRequest) => {
    if (!boardId) return
    try {
      const created = await boardApi.createTask(boardId, data)
      applyTaskCreated(created)
      message.success(`Đã tạo thẻ "${created.title}"`)
      return created
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Tạo thẻ thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleUpdateTask = async (taskId: string, data: UpdateTaskRequest) => {
    if (!boardId) return
    try {
      const updated = await boardApi.updateTask(boardId, taskId, data)
      applyTaskUpdated(updated)
      message.success('Cập nhật thẻ thành công')
      return updated
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Cập nhật thẻ thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleMoveTask = async (
    taskId: string,
    sourceColId: string,
    destColId: string,
    newPosition: number
  ) => {
    if (!boardId) return

    // 1. Optimistic Update local UI state
    const { rollback } = optimisticMoveTask(taskId, sourceColId, destColId, newPosition)

    try {
      // 2. Call backend
      const updated = await boardApi.moveTask(boardId, taskId, {
        columnId: destColId,
        position: newPosition,
      })
      applyTaskUpdated(updated)
    } catch (err: unknown) {
      // 3. Rollback on failure
      rollback()
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Di chuyển thẻ thất bại'
      message.error(errorMsg)
    }
  }

  const handleDeleteTask = async (taskId: string) => {
    if (!boardId) return
    try {
      await boardApi.deleteTask(boardId, taskId)
      applyTaskDeleted(taskId)
      message.success('Đã xoá thẻ thành công')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Xoá thẻ thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  // ---- Labels Actions ----
  const handleCreateLabel = async (data: CreateLabelRequest) => {
    if (!workspaceId) return
    try {
      const created = await boardApi.createLabel(workspaceId, data)
      setWorkspaceLabels([...workspaceLabels, created])
      message.success(`Đã tạo nhãn "${created.name}"`)
      return created
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Tạo nhãn thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleDeleteLabel = async (labelId: string) => {
    if (!workspaceId) return
    try {
      await boardApi.deleteLabel(workspaceId, labelId)
      setWorkspaceLabels(workspaceLabels.filter((l) => l.id !== labelId))
      message.success('Đã xoá nhãn')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Xoá nhãn thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleAttachLabel = async (taskId: string, labelId: string) => {
    try {
      await boardApi.attachLabel(taskId, labelId)
      const label = workspaceLabels.find((l) => l.id === labelId)
      if (label) {
        attachLabelLocally(taskId, label)
      }
      message.success('Đã gắn nhãn')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Gắn nhãn thất bại'
      message.error(errorMsg)
    }
  }

  const handleDetachLabel = async (taskId: string, labelId: string) => {
    try {
      await boardApi.detachLabel(taskId, labelId)
      detachLabelLocally(taskId, labelId)
      message.success('Đã gỡ nhãn')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Gỡ nhãn thất bại'
      message.error(errorMsg)
    }
  }

  // ---- Comments Actions ----
  const handleCreateComment = async (taskId: string, data: CreateCommentRequest) => {
    try {
      const created = await boardApi.createComment(taskId, data)
      message.success('Đã gửi bình luận')
      return created
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Gửi bình luận thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleUpdateComment = async (
    taskId: string,
    commentId: string,
    data: UpdateCommentRequest
  ) => {
    try {
      const updated = await boardApi.updateComment(taskId, commentId, data)
      message.success('Đã sửa bình luận')
      return updated
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Sửa bình luận thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleDeleteComment = async (taskId: string, commentId: string) => {
    try {
      await boardApi.deleteComment(taskId, commentId)
      message.success('Đã xoá bình luận')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Xoá bình luận thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  return {
    board,
    columns,
    tasksByColumn,
    workspaceLabels,
    activeTask,
    connectionStatus,
    reconnect,
    isLoading,
    error,
    refetch: fetchBoardData,
    setActiveTask,
    // Column actions
    createColumn: handleCreateColumn,
    updateColumn: handleUpdateColumn,
    reorderColumns: handleReorderColumns,
    deleteColumn: handleDeleteColumn,
    // Task actions
    createTask: handleCreateTask,
    updateTask: handleUpdateTask,
    moveTask: handleMoveTask,
    deleteTask: handleDeleteTask,
    // Label actions
    createLabel: handleCreateLabel,
    deleteLabel: handleDeleteLabel,
    attachLabel: handleAttachLabel,
    detachLabel: handleDetachLabel,
    // Comment actions
    createComment: handleCreateComment,
    updateComment: handleUpdateComment,
    deleteComment: handleDeleteComment,
  }
}
