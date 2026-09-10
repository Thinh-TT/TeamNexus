import { useCallback, useState } from 'react'
import type { AxiosError } from 'axios'
import { observerApi } from '../services/observerApi'
import type {
  ObserverRunDetailResponse,
  ObserverRunResponse,
} from '../types/notification.types'

export interface UseObserverRunsResult {
  runs: ObserverRunResponse[]
  selectedRun: ObserverRunDetailResponse | null
  status: 'idle' | 'loading' | 'scanning'
  error: string | null
  httpStatus: number | null
  reload: (take?: number) => Promise<void>
  openRun: (runId: string) => Promise<void>
  closeRun: () => void
  triggerScan: (onScanFinished?: () => void) => Promise<void>
}

export function useObserverRuns(workspaceId: string): UseObserverRunsResult {
  const [runs, setRuns] = useState<ObserverRunResponse[]>([])
  const [selectedRun, setSelectedRun] = useState<ObserverRunDetailResponse | null>(null)
  const [status, setStatus] = useState<'idle' | 'loading' | 'scanning'>('idle')
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const extractError = (err: unknown) => {
    const axErr = err as AxiosError<{ error?: string }>
    const code = axErr.response?.status ?? null
    setHttpStatus(code)
    const msg =
      axErr.response?.data?.error ??
      axErr.message ??
      'Không thể xử lý yêu cầu AI Observer.'
    setError(msg)
  }

  const reload = useCallback(
    async (take?: number) => {
      if (!workspaceId) return
      setStatus('loading')
      setError(null)
      setHttpStatus(null)
      try {
        const data = await observerApi.listObserverRuns(workspaceId, take ?? 30)
        setRuns(Array.isArray(data) ? data : [])
      } catch (err: unknown) {
        extractError(err)
      } finally {
        setStatus('idle')
      }
    },
    [workspaceId]
  )

  const openRun = useCallback(async (runId: string) => {
    setStatus('loading')
    setError(null)
    setHttpStatus(null)
    try {
      const detail = await observerApi.getObserverRun(runId)
      setSelectedRun(detail)
    } catch (err: unknown) {
      extractError(err)
    } finally {
      setStatus('idle')
    }
  }, [])

  const closeRun = useCallback(() => {
    setSelectedRun(null)
  }, [])

  const triggerScan = useCallback(
    async (onScanFinished?: () => void) => {
      if (!workspaceId) return
      setStatus('scanning')
      setError(null)
      setHttpStatus(null)
      try {
        await observerApi.triggerObserverScan(workspaceId)
        // Refresh runs after scan
        const data = await observerApi.listObserverRuns(workspaceId, 30)
        setRuns(Array.isArray(data) ? data : [])
        onScanFinished?.()
      } catch (err: unknown) {
        extractError(err)
      } finally {
        setStatus('idle')
      }
    },
    [workspaceId]
  )

  return {
    runs,
    selectedRun,
    status,
    error,
    httpStatus,
    reload,
    openRun,
    closeRun,
    triggerScan,
  }
}
