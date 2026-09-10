import { useCallback, useState } from 'react'
import { aiActionApi } from '../services/aiActionApi'
import type {
  AiActionLog,
  AiActionLogDetail,
  AiActionStatus,
} from '../types/aiAction.types'

export type AiActionHookStatus =
  | 'idle'
  | 'loading'
  | 'approving'
  | 'rejecting'
  | 'undoing'

export interface UseAiActionsOptions {
  boardId: string
  onBoardChanged?: () => void
}

export interface UseAiActionsResult {
  logs: AiActionLog[]
  pendingCount: number
  status: AiActionHookStatus
  error: string | null
  httpStatus: number | null
  selectedLogDetail: AiActionLogDetail | null
  isLoadingDetail: boolean
  reload: (statusFilter?: AiActionStatus) => Promise<AiActionLog[]>
  fetchDetail: (logId: string) => Promise<AiActionLogDetail | null>
  approve: (logId: string) => Promise<AiActionLogDetail | null>
  reject: (logId: string, note?: string | null) => Promise<AiActionLogDetail | null>
  undo: (logId: string) => Promise<AiActionLogDetail | null>
  clearError: () => void
}

const mapHttpError = (err: unknown): { message: string; status: number } => {
  const errObj = err as {
    response?: { status?: number; data?: { error?: string; title?: string } }
    message?: string
  }
  const status = errObj?.response?.status ?? 500
  const serverError =
    errObj?.response?.data?.error || errObj?.response?.data?.title

  if (status === 400) {
    return {
      status,
      message:
        serverError ||
        'Yêu cầu không hợp lệ hoặc dữ liệu hành động AI không khớp.',
    }
  }
  if (status === 401) {
    return {
      status,
      message: 'Phiên làm việc đã hết hạn. Vui lòng đăng nhập lại.',
    }
  }
  if (status === 403) {
    return {
      status,
      message:
        serverError ||
        'Bạn cần quyền Manager hoặc Admin trong workspace này để thực hiện.',
    }
  }
  if (status === 404) {
    return {
      status,
      message: serverError || 'Không tìm thấy bảng hoặc hành động AI này.',
    }
  }
  if (status === 409) {
    return {
      status,
      message:
        serverError ||
        'Hành động này đã được xử lý ở nơi khác (không còn ở trạng thái chờ).',
    }
  }
  return {
    status,
    message:
      serverError ||
      (err instanceof Error
        ? err.message
        : 'Đã xảy ra lỗi hệ thống. Vui lòng thử lại sau.'),
  }
}

export const useAiActions = ({
  boardId,
  onBoardChanged,
}: UseAiActionsOptions): UseAiActionsResult => {
  const [logs, setLogs] = useState<AiActionLog[]>([])
  const [pendingCount, setPendingCount] = useState<number>(0)
  const [status, setStatus] = useState<AiActionHookStatus>('idle')
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)
  const [selectedLogDetail, setSelectedLogDetail] =
    useState<AiActionLogDetail | null>(null)
  const [isLoadingDetail, setIsLoadingDetail] = useState(false)

  const reload = useCallback(
    async (statusFilter?: AiActionStatus): Promise<AiActionLog[]> => {
      if (!boardId) return []
      setStatus('loading')
      setError(null)
      setHttpStatus(null)

      try {
        const data = await aiActionApi.listAiActions(boardId, {
          status: statusFilter,
          take: 50,
        })
        setLogs(data)

        // If not filtered or filtering by Pending, update pendingCount
        if (!statusFilter) {
          const pCount = data.filter((l) => l.status === 'Pending').length
          setPendingCount(pCount)
        } else if (statusFilter === 'Pending') {
          setPendingCount(data.length)
        } else {
          // Fetch pending count separately in background if filtered by another status
          aiActionApi
            .listAiActions(boardId, { status: 'Pending', take: 100 })
            .then((pData) => setPendingCount(pData.length))
            .catch(() => {})
        }

        setStatus('idle')
        return data
      } catch (err) {
        const { message, status: code } = mapHttpError(err)
        setError(message)
        setHttpStatus(code)
        setStatus('idle')
        return []
      }
    },
    [boardId]
  )

  const fetchDetail = useCallback(
    async (logId: string): Promise<AiActionLogDetail | null> => {
      setIsLoadingDetail(true)
      try {
        const detail = await aiActionApi.getAiAction(logId)
        setSelectedLogDetail(detail)
        return detail
      } catch (err) {
        const { message, status: code } = mapHttpError(err)
        setError(message)
        setHttpStatus(code)
        return null
      } finally {
        setIsLoadingDetail(false)
      }
    },
    []
  )

  const approve = useCallback(
    async (logId: string): Promise<AiActionLogDetail | null> => {
      setStatus('approving')
      setError(null)
      setHttpStatus(null)

      try {
        const result = await aiActionApi.approveAiAction(logId)
        setLogs((prev) =>
          prev.map((item) =>
            item.id === logId
              ? {
                  ...item,
                  status: result.status,
                  decidedByUserId: result.decidedByUserId,
                  decidedByName: result.decidedByName,
                  decidedAt: result.decidedAt,
                  decisionNote: result.decisionNote,
                }
              : item
          )
        )
        setPendingCount((prev) => Math.max(0, prev - 1))
        if (selectedLogDetail?.id === logId) {
          setSelectedLogDetail(result)
        }
        setStatus('idle')
        onBoardChanged?.()
        return result
      } catch (err) {
        const { message, status: code } = mapHttpError(err)
        setError(message)
        setHttpStatus(code)
        setStatus('idle')
        if (code === 409 && boardId) {
          const freshLogs = await aiActionApi
            .listAiActions(boardId, { take: 50 })
            .catch(() => [])
          setLogs(freshLogs)
          setPendingCount(freshLogs.filter((l) => l.status === 'Pending').length)
        }
        return null
      }
    },
    [boardId, onBoardChanged, selectedLogDetail]
  )

  const reject = useCallback(
    async (
      logId: string,
      note?: string | null
    ): Promise<AiActionLogDetail | null> => {
      setStatus('rejecting')
      setError(null)
      setHttpStatus(null)

      try {
        const result = await aiActionApi.rejectAiAction(logId, note)
        setLogs((prev) =>
          prev.map((item) =>
            item.id === logId
              ? {
                  ...item,
                  status: result.status,
                  decidedByUserId: result.decidedByUserId,
                  decidedByName: result.decidedByName,
                  decidedAt: result.decidedAt,
                  decisionNote: result.decisionNote,
                }
              : item
          )
        )
        setPendingCount((prev) => Math.max(0, prev - 1))
        if (selectedLogDetail?.id === logId) {
          setSelectedLogDetail(result)
        }
        setStatus('idle')
        return result
      } catch (err) {
        const { message, status: code } = mapHttpError(err)
        setError(message)
        setHttpStatus(code)
        setStatus('idle')
        if (code === 409 && boardId) {
          const freshLogs = await aiActionApi
            .listAiActions(boardId, { take: 50 })
            .catch(() => [])
          setLogs(freshLogs)
          setPendingCount(freshLogs.filter((l) => l.status === 'Pending').length)
        }
        return null
      }
    },
    [boardId, selectedLogDetail]
  )

  const undo = useCallback(
    async (logId: string): Promise<AiActionLogDetail | null> => {
      setStatus('undoing')
      setError(null)
      setHttpStatus(null)

      try {
        const result = await aiActionApi.undoAiAction(logId)
        setLogs((prev) =>
          prev.map((item) =>
            item.id === logId
              ? {
                  ...item,
                  status: result.status,
                  decidedByUserId: result.decidedByUserId,
                  decidedByName: result.decidedByName,
                  decidedAt: result.decidedAt,
                }
              : item
          )
        )
        if (selectedLogDetail?.id === logId) {
          setSelectedLogDetail(result)
        }
        setStatus('idle')
        onBoardChanged?.()
        return result
      } catch (err) {
        const { message, status: code } = mapHttpError(err)
        setError(message)
        setHttpStatus(code)
        setStatus('idle')
        if (code === 409 && boardId) {
          const freshLogs = await aiActionApi
            .listAiActions(boardId, { take: 50 })
            .catch(() => [])
          setLogs(freshLogs)
          setPendingCount(freshLogs.filter((l) => l.status === 'Pending').length)
        }
        return null
      }
    },
    [boardId, onBoardChanged, selectedLogDetail]
  )

  const clearError = useCallback(() => {
    setError(null)
    setHttpStatus(null)
  }, [])

  return {
    logs,
    pendingCount,
    status,
    error,
    httpStatus,
    selectedLogDetail,
    isLoadingDetail,
    reload,
    fetchDetail,
    approve,
    reject,
    undo,
    clearError,
  }
}
