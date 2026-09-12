import { useCallback, useEffect, useRef } from 'react'
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
import type { AgentRunProgressEvent } from '../../ai/types/agentRun.types'
import { resolveHubUrl } from '../utils/hubUrl'
import { nextRetryDelay } from '../utils/reconnectPolicy'

export const SERVER_TIMEOUT_MS = 60_000
export const KEEP_ALIVE_INTERVAL_MS = 15_000

/**
 * Hook to manage SignalR lifecycle for real-time board collaboration.
 * Connects to SignalR board hub (resolved via resolveHubUrl), joins board room `board-{boardId}`,
 * automatically reconnects indefinitely across free-tier cold starts, and dispatches
 * real-time events to the Zustand boardStore.
 */
export const useBoardHub = (
  boardId: string | undefined,
  refetch?: () => void | Promise<void>
) => {
  const connectionRef = useRef<HubConnection | null>(null)
  const isJoinedRef = useRef<boolean>(false)
  const reconnectingLockRef = useRef<boolean>(false)
  const refetchRef = useRef(refetch)

  useEffect(() => {
    refetchRef.current = refetch
  }, [refetch])

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
    applyAgentRunProgress,
  } = useBoardStore()

  useEffect(() => {
    if (!boardId) return

    let isMounted = true
    isJoinedRef.current = false

    const hubUrl = resolveHubUrl(import.meta.env.VITE_API_BASE_URL)
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        withCredentials: true,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: ({ previousRetryCount }) =>
          nextRetryDelay(previousRetryCount),
      })
      .configureLogging(LogLevel.Warning)
      .build()

    connection.serverTimeoutInMilliseconds = SERVER_TIMEOUT_MS
    connection.keepAliveIntervalInMilliseconds = KEEP_ALIVE_INTERVAL_MS

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

    connection.on('AgentRunProgress', (event: AgentRunProgressEvent) => {
      if (event.boardId === boardId) {
        applyAgentRunProgress(event)
      }
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
        try {
          await refetchRef.current?.()
        } catch (err) {
          console.error('[SignalR] Failed to refetch board data after reconnect:', err)
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
    applyAgentRunProgress,
  ])

  const reconnect = useCallback(async () => {
    const conn = connectionRef.current
    if (!boardId || !conn || reconnectingLockRef.current) return
    if (conn.state !== HubConnectionState.Disconnected) return

    reconnectingLockRef.current = true
    try {
      setConnectionStatus('connecting')
      await conn.start()
      await conn.invoke('JoinBoard', boardId)
      isJoinedRef.current = true
      setConnectionStatus('connected')
      try {
        await refetchRef.current?.()
      } catch (refetchErr) {
        console.error('[SignalR] Failed to refetch board data after manual reconnect:', refetchErr)
      }
    } catch (err) {
      console.error('[SignalR] Manual reconnect failed:', err)
      setConnectionStatus('disconnected')
    } finally {
      reconnectingLockRef.current = false
    }
  }, [boardId, setConnectionStatus])

  return {
    getConnection: () => connectionRef.current,
    reconnect,
  }
}
