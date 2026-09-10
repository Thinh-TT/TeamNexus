import { httpClient } from '../../../shared/api/httpClient'
import type {
  DownloadReportParams,
  GetReportSummaryParams,
  ReportBoardOptionResponse,
  ReportExportResult,
  ReportSummaryResponse,
} from '../types/reporting.types'

function parseFileNameFromDisposition(disposition?: string, fallback = 'teamnexus-report.pdf'): string {
  if (!disposition) return fallback

  // Check for RFC 5987 filename*=UTF-8''encoded_name
  const utf8Match = disposition.match(/filename\*=UTF-8''([^;]+)/i)
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].trim())
    } catch {
      // ignore decode error and fallback to standard filename
    }
  }

  // Check for standard filename="name" or filename=name
  const standardMatch = disposition.match(/filename="?([^";]+)"?/i)
  if (standardMatch?.[1]) {
    return standardMatch[1].trim()
  }

  return fallback
}

export const reportingApi = {
  async getReportSummary(
    workspaceId: string,
    params?: GetReportSummaryParams
  ): Promise<ReportSummaryResponse> {
    const res = await httpClient.get<ReportSummaryResponse>(
      `/workspaces/${workspaceId}/reports/summary`,
      { params }
    )
    return res.data
  },

  async listReportBoards(workspaceId: string): Promise<ReportBoardOptionResponse[]> {
    const res = await httpClient.get<ReportBoardOptionResponse[]>(
      `/workspaces/${workspaceId}/reports/boards`
    )
    return res.data
  },

  async downloadReport(
    workspaceId: string,
    params: DownloadReportParams
  ): Promise<ReportExportResult> {
    const res = await httpClient.get<Blob>(`/workspaces/${workspaceId}/reports/export`, {
      params,
      responseType: 'blob',
    })

    const ext = params.format === 'excel' ? 'xlsx' : 'pdf'
    const fallbackFileName = `teamnexus-report.${ext}`
    const disposition = (res.headers?.['content-disposition'] ||
      res.headers?.['Content-Disposition']) as string | undefined
    const fileName = parseFileNameFromDisposition(disposition, fallbackFileName)

    const capHeader = (res.headers?.['x-report-row-cap-reached'] ||
      res.headers?.['X-Report-Row-Cap-Reached']) as string | undefined
    const rowCapReached = capHeader?.toLowerCase() === 'true'

    return {
      blob: res.data,
      fileName,
      rowCapReached,
    }
  },
}
