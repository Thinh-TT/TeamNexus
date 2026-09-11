import { useCallback, useEffect, useState } from 'react'
import { message } from 'antd'
import { agentApi } from '../services/agentApi'
import type {
  AgentRunDetailResponse,
  AgentRunProgressEvent,
  AgentRunResponse,
} from '../types/agentRun.types'

interface UseAgentRunsProps {
  taskId?: string
  autoFetch?: boolean
}

interface UseAgentRunsResult {
  runs: AgentRunResponse[]
  currentRun: AgentRunResponse | null
  runDetail: AgentRunDetailResponse | null
  loading: boolean
  actionLoading: boolean
  error: string | null
  fetchRuns: (take?: number) => Promise<void>
  fetchDetail: (runId: string) => Promise<AgentRunDetailResponse | null>
  startRun: () => Promise<AgentRunResponse | null>
  rerun: (runId: string) => Promise<AgentRunResponse | null>
  cancelRun: (runId: string) => Promise<AgentRunDetailResponse | null>
  handleProgressEvent: (event: AgentRunProgressEvent) => void
  clearError: () => void
}

export const useAgentRuns = ({
  taskId,
  autoFetch = true,
}: UseAgentRunsProps): UseAgentRunsResult => {
  const [runs, setRuns] = useState<AgentRunResponse[]>([])
  const [currentRun, setCurrentRun] = useState<AgentRunResponse | null>(null)
  const [runDetail, setRunDetail] = useState<AgentRunDetailResponse | null>(null)
  const [loading, setLoading] = useState<boolean>(false)
  const [actionLoading, setActionLoading] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const fetchRuns = useCallback(
    async (take = 20) => {
      if (!taskId) {
        setRuns([])
        setCurrentRun(null)
        setRunDetail(null)
        return
      }

      setLoading(true)
      setError(null)
      try {
        const data = await agentApi.listRuns(taskId, take)
        setRuns(data)
        const latest = data.length > 0 ? data[0] : null
        setCurrentRun(latest)

        // If the latest run is current, load its detail
        if (latest) {
          try {
            const detail = await agentApi.getRunDetail(latest.id)
            setRunDetail(detail)
          } catch {
            // detail fetch failure is non-critical for listing
          }
        } else {
          setRunDetail(null)
        }
      } catch (err: unknown) {
        const errorMsg = (err as { response?: { data?: { error?: string } } })?.response?.data
          ?.error ||
          (err instanceof Error ? err.message : 'Không thể tải lịch sử chạy của Agent')
        setError(errorMsg)
      } finally {
        setLoading(false)
      }
    },
    [taskId]
  )

  const fetchDetail = useCallback(async (runId: string) => {
    try {
      const detail = await agentApi.getRunDetail(runId)
      setRunDetail(detail)
      return detail
    } catch (err: unknown) {
      const errorMsg = (err as { response?: { data?: { error?: string } } })?.response?.data
        ?.error ||
        (err instanceof Error ? err.message : 'Không thể tải chi tiết run')
      setError(errorMsg)
      return null
    }
  }, [])

  const startRun = useCallback(async (): Promise<AgentRunResponse | null> => {
    if (!taskId) return null

    setActionLoading(true)
    setError(null)
    try {
      const newRun = await agentApi.startRun(taskId)
      message.success('Đã kích hoạt AI Agent Executor')
      setRuns((prev) => [newRun, ...prev.filter((r) => r.id !== newRun.id)])
      setCurrentRun(newRun)
      // fetch detail for new run
      fetchDetail(newRun.id)
      return newRun
    } catch (err: unknown) {
      const errorMsg = (err as { response?: { data?: { error?: string } } })?.response?.data
        ?.error ||
        (err instanceof Error ? err.message : 'Không thể bắt đầu chạy AI Agent')

      if (errorMsg.includes('not assigned to the AI Agent')) {
        message.warning('Task chưa được phân công cho AI Agent. Vui lòng chọn người thực hiện là AI Agent trước.')
      } else if (errorMsg.includes('already in progress')) {
        message.warning('Một tác vụ AI Agent đang chạy cho task này.')
      } else if (errorMsg.includes('disabled')) {
        message.error('Tính năng AI Agent đang tạm tắt.')
      } else {
        message.error(errorMsg)
      }

      setError(errorMsg)
      return null
    } finally {
      setActionLoading(false)
    }
  }, [taskId, fetchDetail])

  const rerun = useCallback(
    async (runId: string): Promise<AgentRunResponse | null> => {
      if (!taskId) return null

      setActionLoading(true)
      setError(null)
      try {
        const newRun = await agentApi.rerun(taskId, runId)
        message.success('Đã chạy lại AI Agent')
        setRuns((prev) => [newRun, ...prev.filter((r) => r.id !== newRun.id)])
        setCurrentRun(newRun)
        fetchDetail(newRun.id)
        return newRun
      } catch (err: unknown) {
        const errorMsg = (err as { response?: { data?: { error?: string } } })?.response?.data
          ?.error ||
          (err instanceof Error ? err.message : 'Không thể chạy lại AI Agent')

        if (errorMsg.includes('No answer found')) {
          message.warning('Chưa tìm thấy câu trả lời cho yêu cầu làm rõ. Trưởng nhóm vui lòng để lại bình luận trả lời trước.')
        } else {
          message.error(errorMsg)
        }

        setError(errorMsg)
        return null
      } finally {
        setActionLoading(false)
      }
    },
    [taskId, fetchDetail]
  )

  const cancelRun = useCallback(
    async (runId: string): Promise<AgentRunDetailResponse | null> => {
      setActionLoading(true)
      setError(null)
      try {
        const detail = await agentApi.cancelRun(runId)
        message.info('Đã huỷ tác vụ AI Agent')
        setRunDetail(detail)
        setCurrentRun(detail.run)
        setRuns((prev) => prev.map((r) => (r.id === detail.run.id ? detail.run : r)))
        return detail
      } catch (err: unknown) {
        const errorMsg = (err as { response?: { data?: { error?: string } } })?.response?.data
          ?.error ||
          (err instanceof Error ? err.message : 'Không thể huỷ tác vụ AI Agent')

        if (errorMsg.includes('Only a Running agent run can be cancelled')) {
          message.warning('Chỉ có thể huỷ tác vụ đang chạy.')
          // Refresh detail to get latest status
          fetchDetail(runId)
        } else {
          message.error(errorMsg)
        }

        setError(errorMsg)
        return null
      } finally {
        setActionLoading(false)
      }
    },
    [fetchDetail]
  )

  const handleProgressEvent = useCallback(
    (event: AgentRunProgressEvent) => {
      if (!taskId || event.taskId !== taskId) return

      setCurrentRun((prev) => {
        if (!prev || prev.id === event.runId) {
          const updated: AgentRunResponse = prev
            ? {
                ...prev,
                status: event.status,
                stopReason: event.stopReason,
                toolCallCount: event.toolCallCount,
                totalTokens: event.totalTokens,
                clarificationQuestion: event.clarificationQuestion ?? prev.clarificationQuestion,
              }
            : {
                id: event.runId,
                taskId: event.taskId,
                boardId: event.boardId,
                agentUserId: '',
                agentDisplayName: 'TeamNexus Agent',
                triggeredByUserId: '',
                triggeredByName: '',
                status: event.status,
                stopReason: event.stopReason,
                clarificationQuestion: event.clarificationQuestion,
                aiActionLogId: null,
                outputKind: null,
                error: null,
                toolCallCount: event.toolCallCount,
                llmCallCount: 0,
                promptTokens: 0,
                completionTokens: 0,
                totalTokens: event.totalTokens,
                startedAt: new Date().toISOString(),
                finishedAt: null,
                traceTruncated: false,
              }
          return updated
        }
        return prev
      })

      // Refresh detail when run finishes or asks question
      if (event.status !== 'Running') {
        fetchDetail(event.runId)
      }
    },
    [taskId, fetchDetail]
  )

  useEffect(() => {
    if (autoFetch && taskId) {
      Promise.resolve().then(() => {
        fetchRuns()
      })
    }
  }, [autoFetch, taskId, fetchRuns])

  return {
    runs,
    currentRun,
    runDetail,
    loading,
    actionLoading,
    error,
    fetchRuns,
    fetchDetail,
    startRun,
    rerun,
    cancelRun,
    handleProgressEvent,
    clearError: () => setError(null),
  }
}
