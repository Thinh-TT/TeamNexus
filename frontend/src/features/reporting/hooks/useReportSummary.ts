import { useCallback, useEffect, useState } from 'react'
import { reportingApi } from '../services/reportingApi'
import type { GetReportSummaryParams, ReportSummaryResponse } from '../types/reporting.types'
import { extractErrorMessage } from '../utils/reportError'

export interface UseReportSummaryResult {
  report: ReportSummaryResponse | null
  status: 'idle' | 'loading' | 'error'
  error: string | null
  httpStatus: number | null
  params: GetReportSummaryParams
  reload: (overrideParams?: GetReportSummaryParams) => Promise<void>
  setBoard: (boardId?: string) => void
  setRange: (from?: string, to?: string) => void
}

export function useReportSummary(
  workspaceId: string,
  initialParams?: GetReportSummaryParams
): UseReportSummaryResult {
  const [report, setReport] = useState<ReportSummaryResponse | null>(null)
  const [status, setStatus] = useState<'idle' | 'loading' | 'error'>(
    workspaceId ? 'loading' : 'idle'
  )
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)
  const [params, setParams] = useState<GetReportSummaryParams>(initialParams || {})

  const fetchReport = useCallback(
    async (requestParams: GetReportSummaryParams, ignoreState?: { current: boolean }) => {
      if (!workspaceId) return
      setStatus('loading')
      setError(null)
      setHttpStatus(null)

      try {
        const data = await reportingApi.getReportSummary(workspaceId, requestParams)
        if (!ignoreState?.current) {
          setReport(data)
          setStatus('idle')
        }
      } catch (err: unknown) {
        if (!ignoreState?.current) {
          const errInfo = await extractErrorMessage(err)
          setError(errInfo.message)
          setHttpStatus(errInfo.status ?? null)
          setStatus('error')
        }
      }
    },
    [workspaceId]
  )

  useEffect(() => {
    let ignore = false

    if (!workspaceId) return

    reportingApi
      .getReportSummary(workspaceId, params)
      .then((data) => {
        if (!ignore) {
          setReport(data)
          setError(null)
          setHttpStatus(null)
          setStatus('idle')
        }
      })
      .catch(async (err: unknown) => {
        if (!ignore) {
          const errInfo = await extractErrorMessage(err)
          setError(errInfo.message)
          setHttpStatus(errInfo.status ?? null)
          setStatus('error')
        }
      })

    return () => {
      ignore = true
    }
  }, [workspaceId, params])

  const reload = useCallback(
    async (overrideParams?: GetReportSummaryParams) => {
      const activeParams = overrideParams !== undefined ? overrideParams : params
      if (overrideParams !== undefined) {
        setParams(overrideParams)
      }
      await fetchReport(activeParams)
    },
    [fetchReport, params]
  )

  const setBoard = useCallback((boardId?: string) => {
    setParams((prev) => ({
      ...prev,
      boardId: boardId || undefined,
    }))
  }, [])

  const setRange = useCallback((from?: string, to?: string) => {
    setParams((prev) => ({
      ...prev,
      from: from || undefined,
      to: to || undefined,
    }))
  }, [])

  return {
    report,
    status,
    error,
    httpStatus,
    params,
    reload,
    setBoard,
    setRange,
  }
}
