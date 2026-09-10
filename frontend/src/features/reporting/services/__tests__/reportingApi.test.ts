import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { reportingApi } from '../reportingApi'
import type { ReportBoardOptionResponse, ReportSummaryResponse } from '../../types/reporting.types'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

describe('reportingApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls getReportSummary with correct endpoint and query params', async () => {
    const mockSummary: Partial<ReportSummaryResponse> = {
      workspaceId: 'ws-1',
      workspaceName: 'Workspace 1',
      progress: { total: 10, done: 5, open: 5, overdue: 1, donePercent: 50 },
    }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockSummary })

    const params = { boardId: 'board-1', from: '2026-08-01T00:00:00Z', to: '2026-09-01T00:00:00Z' }
    const result = await reportingApi.getReportSummary('ws-1', params)

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/reports/summary', { params })
    expect(result).toEqual(mockSummary)
  })

  it('calls listReportBoards with correct endpoint', async () => {
    const mockBoards: ReportBoardOptionResponse[] = [
      { id: 'b-1', name: 'Board Alpha', taskCount: 5, isDoneColumns: 1 },
      { id: 'b-2', name: 'Board Beta', taskCount: 2, isDoneColumns: 1 },
    ]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockBoards })

    const result = await reportingApi.listReportBoards('ws-1')

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/reports/boards')
    expect(result).toEqual(mockBoards)
  })

  it('downloads PDF report with parsed standard filename and cap flag', async () => {
    const mockBlob = new Blob(['%PDF-1.4 test content'], { type: 'application/pdf' })
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: mockBlob,
      headers: {
        'content-disposition': 'attachment; filename="report-workspace-1.pdf"',
        'x-report-row-cap-reached': 'true',
      },
    })

    const result = await reportingApi.downloadReport('ws-1', {
      format: 'pdf',
      boardId: 'b-1',
    })

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/reports/export', {
      params: { format: 'pdf', boardId: 'b-1' },
      responseType: 'blob',
    })
    expect(result.blob).toBe(mockBlob)
    expect(result.fileName).toBe('report-workspace-1.pdf')
    expect(result.rowCapReached).toBe(true)
  })

  it('downloads Excel report and parses UTF-8 RFC5987 filename', async () => {
    const mockBlob = new Blob(['PK mock excel'], {
      type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    })
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: mockBlob,
      headers: {
        'content-disposition': "attachment; filename*=UTF-8''bao-cao-tieng-viet.xlsx",
        'x-report-row-cap-reached': 'false',
      },
    })

    const result = await reportingApi.downloadReport('ws-1', {
      format: 'excel',
    })

    expect(result.fileName).toBe('bao-cao-tieng-viet.xlsx')
    expect(result.rowCapReached).toBe(false)
  })

  it('falls back to default filename when Content-Disposition is missing', async () => {
    const mockBlob = new Blob(['dummy'], { type: 'application/pdf' })
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: mockBlob,
      headers: {},
    })

    const result = await reportingApi.downloadReport('ws-1', {
      format: 'pdf',
    })

    expect(result.fileName).toBe('teamnexus-report.pdf')
    expect(result.rowCapReached).toBe(false)
  })
})
