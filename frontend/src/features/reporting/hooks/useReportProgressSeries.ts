import { useCallback, useEffect, useState } from 'react'
import { reportingApi } from '../services/reportingApi'
import type {
  ReportProgressSeriesParams,
  ReportProgressSeriesResponse,
} from '../types/reporting.types'
import { extractErrorMessage } from '../utils/reportError'
import { timeZoneOffsetMinutes } from '../utils/timeZoneOffsetMinutes'

export interface UseReportProgressSeriesResult {
  series: ReportProgressSeriesResponse | null
  status: 'idle' | 'loading' | 'error'
  error: string | null
  httpStatus: number | null
  reload: () => Promise<void>
}

/**
 * Nạp chuỗi thời gian Burndown/Velocity (Giai đoạn 13 §4).
 *
 * Theo đúng pattern của repo (`useReportSummary`): `useState` + `useEffect` + `reload`, **không**
 * react-query. `workspaceId` rỗng ⇒ **không gọi API** — `ReportsPage` dùng đúng cách đó để chặn Member
 * trước khi request được gửi, nên một Member sẽ không bao giờ nhận 403 từ đây.
 *
 * `tzOffsetMinutes` được tính **một lần** khi hook chạy và giữ nguyên cho mọi lần gọi lại: múi giờ của
 * trình duyệt không đổi giữa hai lần fetch, còn tính lại mỗi render sẽ tạo ra một giá trị mới và làm
 * `useEffect` chạy vô hạn.
 */
export function useReportProgressSeries(
  workspaceId: string,
  params?: ReportProgressSeriesParams
): UseReportProgressSeriesResult {
  const [series, setSeries] = useState<ReportProgressSeriesResponse | null>(null)
  const [status, setStatus] = useState<'idle' | 'loading' | 'error'>(
    workspaceId ? 'loading' : 'idle'
  )
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const { from, to, boardId } = params ?? {}

  const fetchSeries = useCallback(
    async (ignoreState?: { current: boolean }) => {
      if (!workspaceId) return

      setStatus('loading')
      setError(null)
      setHttpStatus(null)

      try {
        const data = await reportingApi.getProgressSeries(workspaceId, {
          from,
          to,
          boardId,
          tzOffsetMinutes: timeZoneOffsetMinutes(),
        })

        if (!ignoreState?.current) {
          setSeries(data)
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
    [workspaceId, from, to, boardId]
  )

  useEffect(() => {
    const ignoreState = { current: false }

    // Gọi ở microtask kế tiếp thay vì đồng bộ trong thân effect: `fetchSeries` đặt `status = 'loading'`
    // ngay dòng đầu, và `setState` đồng bộ trong effect làm React render thêm một lượt vô ích (bị
    // `oxlint` cảnh báo, và `npm run lint` phải giữ 0 warning). Việc trì hoãn không đổi hành vi: state
    // khởi tạo đã đúng (`loading` khi có `workspaceId`), nên vòng render đầu tiên vẫn hiện "đang tải".
    Promise.resolve().then(() => {
      void fetchSeries(ignoreState)
    })

    return () => {
      ignoreState.current = true
    }
  }, [fetchSeries])

  const reload = useCallback(() => fetchSeries(), [fetchSeries])

  return { series, status, error, httpStatus, reload }
}
