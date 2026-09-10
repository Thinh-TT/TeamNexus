import { useCallback, useState } from 'react'
import { message } from 'antd'
import { reportingApi } from '../services/reportingApi'
import type { DownloadReportParams, ReportFormat } from '../types/reporting.types'
import { saveBlob } from '../utils/reportDownload'
import { extractErrorMessage } from '../utils/reportError'

export interface UseReportExportResult {
  exporting: ReportFormat | null
  error: string | null
  httpStatus: number | null
  exportReport: (format: ReportFormat) => Promise<boolean>
}

export function useReportExport(
  workspaceId: string,
  getParams?: () => Omit<DownloadReportParams, 'format'>
): UseReportExportResult {
  const [exporting, setExporting] = useState<ReportFormat | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const exportReport = useCallback(
    async (format: ReportFormat): Promise<boolean> => {
      if (exporting !== null) {
        return false // Chặn gọi trùng khi đang xuất file
      }

      if (!workspaceId) {
        message.error('Thiếu mã workspace.')
        return false
      }

      setExporting(format)
      setError(null)
      setHttpStatus(null)

      try {
        const extraParams = getParams ? getParams() : {}
        const result = await reportingApi.downloadReport(workspaceId, {
          format,
          ...extraParams,
        })

        saveBlob(result.blob, result.fileName)
        message.success('Đã tải báo cáo')
        return true
      } catch (err: unknown) {
        const errInfo = await extractErrorMessage(err)
        setError(errInfo.message)
        setHttpStatus(errInfo.status ?? null)
        message.error(errInfo.message)
        return false
      } finally {
        setExporting(null)
      }
    },
    [exporting, getParams, workspaceId]
  )

  return {
    exporting,
    error,
    httpStatus,
    exportReport,
  }
}
