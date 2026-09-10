import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { ReportsPage } from '../ReportsPage'
import { httpClient } from '../../../../shared/api/httpClient'
import { reportingApi } from '../../services/reportingApi'
import type { ReportSummaryResponse } from '../../types/reporting.types'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    getReportSummary: vi.fn(),
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

describe('ReportsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(reportingApi.listReportBoards).mockResolvedValue([])
    vi.mocked(reportingApi.getReportSummary).mockResolvedValue(mockReportSummary)
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
    })

    expect(screen.getByRole('button', { name: /Xuất Báo Cáo/i })).toBeInTheDocument()
    expect(screen.getByText('Tổng số Task')).toBeInTheDocument()
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
})
