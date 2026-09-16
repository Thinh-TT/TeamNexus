import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ReportsPage } from '../ReportsPage'
import { httpClient } from '../../../../shared/api/httpClient'
import { reportingApi } from '../../services/reportingApi'
import type {
  ReportProgressSeriesResponse,
  ReportSummaryResponse,
} from '../../types/reporting.types'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    getReportSummary: vi.fn(),
    getProgressSeries: vi.fn(),
    listReportBoards: vi.fn(),
    downloadReport: vi.fn(),
  },
}))

const mockReportSummary: ReportSummaryResponse = {
  workspaceId: 'ws-1',
  workspaceName: 'Workspace Kiểm Thử',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-08-01T00:00:00Z', to: '2026-09-01T00:00:00Z', days: 30, clamped: false },
  generatedAt: '2026-09-01T00:00:00Z',
  progress: { total: 10, done: 6, open: 4, overdue: 1, donePercent: 60 },
  performance: {
    createdInRange: 8,
    completedInRange: 5,
    avgCompletionHours: 48,
    avgLeadTimeHours: 72,
    onTimeRate: 80,
    overdueRate: 20,
    throughputPerWeek: 1.2,
    completedAtMissing: 0,
  },
  byBoard: [],
  byAssignee: [],
  activity: { totalActions: 15, byAction: [], activeUsers: 3, actionsPerDay: 0.5 },
  health: { runsScanned: 0, signalsByType: [], findingsBySeverity: [] },
  metricDefinitions: {},
  truncated: { rowCapReached: false, maxRows: 5000 },
}

const mockProgressSeries: ReportProgressSeriesResponse = {
  workspaceId: 'ws-1',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-08-01T00:00:00Z', to: '2026-09-01T00:00:00Z', days: 30, clamped: false },
  mode: 'date',
  bucketDays: 1,
  days: [
    { date: '2026-08-01', openTasks: 4, completions: 1, creations: 2 },
    { date: '2026-08-02', openTasks: 3, completions: 1, creations: 0 },
  ],
  weeks: [],
  velocity: { avgCompletionsPerWeek: 2, completedInRange: 2, openAtEnd: 3 },
  metricDefinitions: {},
  truncated: { bucketCapReached: false, maxBuckets: 90 },
  tzOffsetMinutes: 0,
}

describe('ReportsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(reportingApi.listReportBoards).mockResolvedValue([])
    vi.mocked(reportingApi.getReportSummary).mockResolvedValue(mockReportSummary)
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValue(mockProgressSeries)
  })

  const renderComponent = (workspaceId = 'ws-1') => {
    return render(
      <MemoryRouter initialEntries={[`/workspaces/${workspaceId}/reports`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/reports" element={<ReportsPage />} />
        </Routes>
      </MemoryRouter>
    )
  }

  /**
   * `ReportsPage` dựng cả `ReportSummaryPanel` (nhiều bảng) **và** biểu đồ, nên mỗi lần render khá nặng.
   * Khi chạy đủ bộ 86 file test song song, CPU bị chia sẻ và mặc định `waitFor` 1000 ms của Testing Library
   * có thể bị vượt — test đỏ vì **máy bận**, không vì sản phẩm sai. Dùng ngưỡng chờ rộng hơn cho file này.
   */
  const WAIT = { timeout: 5000 }

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('renders 403 Forbidden screen when user is only a Member', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Member' }],
    })

    renderComponent()

    await waitFor(() => {
      expect(screen.getByText(/403 - Quyền Truy Cập Bị Từ Chối/i)).toBeInTheDocument()
    })

    expect(screen.queryByText('Báo Cáo & Xuất Dữ Liệu')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Xuất Báo Cáo/i })).not.toBeInTheDocument()
  })

  it('renders full reporting panel and export button when user is Manager', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Manager' }],
    })

    renderComponent()

    await waitFor(() => {
      expect(screen.getByText('Báo Cáo & Xuất Dữ Liệu')).toBeInTheDocument()
    }, WAIT)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Xuất Báo Cáo/i })).toBeInTheDocument()
      expect(screen.getByText('Tổng số Task')).toBeInTheDocument()
    }, WAIT)
  })

  it('opens export drawer when clicking Xuất Báo Cáo button', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Admin' }],
    })

    renderComponent()

    const exportBtn = await screen.findByRole('button', { name: /Xuất Báo Cáo/i })
    fireEvent.click(exportBtn)

    await waitFor(() => {
      expect(screen.getByText('Tài liệu PDF (QuestPDF)')).toBeInTheDocument()
    })
  })

  // ---- Giai đoạn 13 §4: Burndown + Velocity ------------------------------

  it('hiển thị biểu đồ Burndown và khối Năng suất khi tải được chuỗi thời gian', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Manager' }],
    })

    renderComponent()

    await waitFor(() => {
      expect(screen.getByTestId('burndown-chart')).toBeInTheDocument()
    }, WAIT)

    expect(screen.getByTestId('velocity-panel')).toBeInTheDocument()

    // Ít nhất một bucket phải được vẽ. Assert theo **số lượng** chứ không theo một khoá ngày cụ thể:
    // `timeZoneOffsetMinutes()` phụ thuộc múi giờ của máy chạy test, nên ở múi giờ âm nhãn ngày có thể
    // lệch. Điều cần chứng minh ở đây là "dữ liệu từ API đã đi tới biểu đồ", không phải một ngày cụ thể
    // (chi tiết ngày/ô đã có `boardCalendar.test.ts` và `BurndownChart.test.tsx` lo).
    await waitFor(() => {
      expect(screen.getAllByTestId(/^burndown-bucket-/).length).toBeGreaterThan(0)
    }, WAIT)

    // Gọi đúng endpoint, kèm `tzOffsetMinutes` (luôn có, kể cả khi bằng 0).
    expect(reportingApi.getProgressSeries).toHaveBeenCalled()
    expect(vi.mocked(reportingApi.getProgressSeries).mock.calls[0][0]).toBe('ws-1')
  })

  it('chuỗi thời gian lỗi ⇒ Alert cục bộ NHƯNG bảng báo cáo vẫn hiển thị', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({
      data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Manager' }],
    })
    vi.mocked(reportingApi.getProgressSeries).mockRejectedValueOnce({
      response: { status: 500, data: { error: 'Máy chủ bận.' } },
    })

    renderComponent()

    // Lỗi biểu đồ được báo riêng…
    await waitFor(() => {
      expect(screen.getByTestId('progress-series-error')).toBeInTheDocument()
    }, WAIT)

    // …và **không** kéo theo phần báo cáo đã tải xong: một sự cố nhỏ không được biến thành trang trắng.
    expect(screen.getByText('Tổng số Task')).toBeInTheDocument()
    expect(screen.queryByTestId('burndown-chart')).not.toBeInTheDocument()
  })
})
