import { useEffect, useRef } from 'react'
import {
  type HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import { useBoardStore } from '../stores/boardStore'
import type {
  ColumnPositionItem,
  ColumnResponse,
  CommentDeletedEventPayload,
  CommentResponse,
  TaskMovedEventPayload,
  TaskResponse,
} from '../types/board.types'

/**
 * Hook to manage SignalR lifecycle for real-time board collaboration.
 * Connects to /hubs/board, joins board room `board-{boardId}`, and dispatches
 * real-time events to the Zustand boardStore.
 */
export const useBoardHub = (boardId: string | undefined) => {
  const connectionRef = useRef<HubConnection | null>(null)
  const isJoinedRef = useRef<boolean>(false)

  const {
    setConnectionStatus,
    applyTaskCreated,
    applyTaskUpdated,
    applyTaskMoved,
    applyTaskDeleted,
    applyColumnCreated,
    applyColumnUpdated,
    applyColumnsReordered,
    applyColumnDeleted,
    applyCommentAdded,
    applyCommentDeleted,
  } = useBoardStore()

  useEffect(() => {
    if (!boardId) return

    let isMounted = true
    isJoinedRef.current = false

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/board', {
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    connectionRef.current = connection

    // ---- Event Handlers ----
    connection.on('TaskCreated', (task: TaskResponse) => {
      if (task.boardId === boardId) {
        applyTaskCreated(task)
      }
    })

    connection.on('TaskUpdated', (task: TaskResponse) => {
      if (task.boardId === boardId) {
        applyTaskUpdated(task)
      }
    })

    connection.on('TaskMoved', (payload: TaskMovedEventPayload) => {
      applyTaskMoved(payload)
    })

    connection.on('TaskDeleted', ({ taskId }: { taskId: string }) => {
      applyTaskDeleted(taskId)
    })

    connection.on('ColumnCreated', (column: ColumnResponse) => {
      if (column.boardId === boardId) {
        applyColumnCreated(column)
      }
    })

    connection.on('ColumnUpdated', (column: ColumnResponse) => {
      if (column.boardId === boardId) {
        applyColumnUpdated(column)
      }
    })

    connection.on('ColumnsReordered', (items: ColumnPositionItem[]) => {
      applyColumnsReordered(items)
    })

    connection.on('ColumnDeleted', ({ columnId }: { columnId: string }) => {
      applyColumnDeleted(columnId)
    })

    connection.on('CommentAdded', (comment: CommentResponse) => {
      applyCommentAdded(comment)
    })

    connection.on('CommentDeleted', (payload: CommentDeletedEventPayload) => {
      applyCommentDeleted(payload.commentId, payload.taskId)
    })

    // ---- Lifecycle Callbacks ----
    connection.onreconnecting(() => {
      if (isMounted) setConnectionStatus('reconnecting')
      isJoinedRef.current = false
    })

    connection.onreconnected(async () => {
      if (isMounted) {
        setConnectionStatus('connected')
        try {
          await connection.invoke('JoinBoard', boardId)
          isJoinedRef.current = true
        } catch (err) {
          console.error('[SignalR] Failed to rejoin board after reconnect:', err)
        }
      }
    })

    connection.onclose(() => {
      if (isMounted) setConnectionStatus('disconnected')
      isJoinedRef.current = false
    })

    // ---- Start Connection & Join Board ----
    const startConnection = async () => {
      try {
        setConnectionStatus('connecting')
        await connection.start()
        if (!isMounted) {
          await connection.stop()
          return
        }

        await connection.invoke('JoinBoard', boardId)
        isJoinedRef.current = true
        setConnectionStatus('connected')
      } catch (err) {
        if (isMounted) {
          console.error('[SignalR] Connection or JoinBoard failed:', err)
          setConnectionStatus('disconnected')
        }
      }
    }

    startConnection()

    return () => {
      isMounted = false
      const conn = connectionRef.current
      if (conn) {
        if (isJoinedRef.current && conn.state === HubConnectionState.Connected) {
          conn
            .invoke('LeaveBoard', boardId)
            .catch((e) => console.warn('[SignalR] LeaveBoard error:', e))
            .finally(() => {
              conn.stop().catch(() => {})
            })
        } else {
          conn.stop().catch(() => {})
        }
      }
      setConnectionStatus('disconnected')
    }
  }, [
    boardId,
    setConnectionStatus,
    applyTaskCreated,
    applyTaskUpdated,
    applyTaskMoved,
    applyTaskDeleted,
    applyColumnCreated,
    applyColumnUpdated,
    applyColumnsReordered,
    applyColumnDeleted,
    applyCommentAdded,
    applyCommentDeleted,
  ])

  return {
    getConnection: () => connectionRef.current,
  }
}
