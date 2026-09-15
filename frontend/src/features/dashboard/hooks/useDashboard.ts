import { useCallback, useEffect, useState } from 'react'
import { message } from 'antd'
import { dashboardApi, type GetDashboardParams } from '../services/dashboardApi'
import type { DashboardResponse } from '../types/dashboard.types'

export interface UseDashboardResult {
  dashboard: DashboardResponse | null
  status: 'idle' | 'loading' | 'error'
  error: string | null
  httpStatus: number | null
  reload: () => Promise<void>
}

export function useDashboard(
  workspaceId: string,
  params?: GetDashboardParams
): UseDashboardResult {
  const [dashboard, setDashboard] = useState<DashboardResponse | null>(null)
  const [status, setStatus] = useState<'idle' | 'loading' | 'error'>(
    workspaceId ? 'loading' : 'idle'
  )
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const days = params?.days
  const take = params?.take

  const fetchDashboard = useCallback(
    async (ignoreState?: { current: boolean }) => {
      if (!workspaceId) return
      setStatus('loading')
      setError(null)
      setHttpStatus(null)

      try {
        const data = await dashboardApi.get(workspaceId, { days, take })
        if (!ignoreState?.current) {
          setDashboard(data)
          setStatus('idle')
        }
      } catch (err: unknown) {
        if (!ignoreState?.current) {
          const res = (err as { response?: { status?: number; data?: { error?: string } } })?.response
          const statusVal = res?.status ?? null
          const msg = res?.data?.error ?? 'Không thể tải thông tin tổng quan workspace'
          setError(msg)
          setHttpStatus(statusVal)
          setStatus('error')
          message.error(msg)
        }
      }
    },
    [workspaceId, days, take]
  )

  useEffect(() => {
    const ignoreState = { current: false }
    if (workspaceId) {
      Promise.resolve().then(() => {
        fetchDashboard(ignoreState)
      })
    }
    return () => {
      ignoreState.current = true
    }
  }, [fetchDashboard, workspaceId])

  const reload = useCallback(async () => {
    await fetchDashboard()
  }, [fetchDashboard])

  return {
    dashboard,
    status,
    error,
    httpStatus,
    reload,
  }
}
