import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ReportSummaryPanel } from '../ReportSummaryPanel'
import type { ReportSummaryResponse } from '../../types/reporting.types'

const mockSummaryFixture: ReportSummaryResponse = {
  workspaceId: 'ws-1',
  workspaceName: 'Workspace Kiểm Thử',
  scope: { type: 'board', boardId: 'board-alpha', boardName: 'Bảng Alpha' },
  period: {
    from: '2026-08-01T00:00:00+00:00',
    to: '2026-09-10T15:00:00+00:00',
    days: 41,
    clamped: false,
  },
  generatedAt: '2026-09-10T15:37:25.75+00:00',
  progress: { total: 5, done: 3, open: 2, overdue: 2, donePercent: 60.0 },
  performance: {
    createdInRange: 4,
    completedInRange: 2,
    avgCompletionHours: 636.0,
    avgLeadTimeHours: 504.0,
    onTimeRate: 0.0,
    overdueRate: 100.0,
    throughputPerWeek: 0.3,
    completedAtMissing: 1,
  },
  byBoard: [
    {
      boardId: 'board-alpha',
      boardName: 'Bảng Alpha',
      total: 5,
      done: 3,
      open: 2,
      overdue: 2,
      completedInRange: 2,
      avgCompletionHours: 636.0,
      onTimeRate: 0.0,
    },
  ],
  byAssignee: [
    {
      assigneeId: 'user-1',
      assigneeName: 'S5 Member',
      total: 3,
      done: 2,
      open: 1,
      overdue: 1,
      completedInRange: 2,
      avgCompletionHours: 636.0,
      onTimeRate: 0.0,
    },
    {
      assigneeId: null,
      assigneeName: 'Chưa gán',
      total: 2,
      done: 1,
      open: 1,
      overdue: 1,
      completedInRange: 0,
      avgCompletionHours: null,
      onTimeRate: null,
    },
  ],
  activity: {
    totalActions: 3,
    byAction: [
      { action: 'TaskCreated', count: 1 },
      { action: 'TaskDeleted', count: 1 },
      { action: 'TaskMoved', count: 1 },
    ],
    activeUsers: 2,
    actionsPerDay: 0.1,
  },
  health: {
    runsScanned: 1,
    signalsByType: [
      { type: 'OverdueTask', count: 3 },
      { type: 'StalledTask', count: 2 },
    ],
    findingsBySeverity: [
      { severity: 'Unknown', count: 3 },
      { severity: 'High', count: 1 },
      { severity: 'Medium', count: 1 },
    ],
  },
  metricDefinitions: {
    total: 'Số task hiện có trong phạm vi báo cáo.',
    done: 'Task hoàn thành.',
  },
  truncated: { rowCapReached: false, maxRows: 5000 },
}

describe('ReportSummaryPanel', () => {
  it('renders all metrics from fixture properly', () => {
    render(<ReportSummaryPanel report={mockSummaryFixture} />)

    expect(screen.getByText('Tổng số Task')).toBeInTheDocument()
    expect(screen.getByText('Đã Hoàn Thành')).toBeInTheDocument()
    expect(screen.getByText('Đang Mở')).toBeInTheDocument()
    expect(screen.getByText('Quá Hạn')).toBeInTheDocument()

    // Values
    expect(screen.getByText('Tiến Độ Hoàn Thành Toàn Diện')).toBeInTheDocument()
    expect(screen.getByText('3/5 task (60.0%)')).toBeInTheDocument()
    expect(screen.getByText('Bảng Alpha')).toBeInTheDocument()

    // Switch to By Assignee tab
    const assigneeTab = screen.getByText(/Theo Người Phụ Trách/i)
    fireEvent.click(assigneeTab)
    expect(screen.getByText('S5 Member')).toBeInTheDocument()

    // Activity
    expect(screen.getByText('Hoạt Động Trong Kỳ')).toBeInTheDocument()
    expect(screen.getByText(/TaskCreated:/)).toBeInTheDocument()

    // AI Health
    expect(screen.getByText('Sức Khoẻ Dự Án (AI Observer)')).toBeInTheDocument()
    expect(screen.getByText('1 lượt quét')).toBeInTheDocument()
  })

  it('displays warning alert when rowCapReached is true', () => {
    const cappedReport: ReportSummaryResponse = {
      ...mockSummaryFixture,
      truncated: { rowCapReached: true, maxRows: 100 },
    }

    render(<ReportSummaryPanel report={cappedReport} />)

    expect(screen.getByText('Giới hạn hiển thị dòng dữ liệu')).toBeInTheDocument()
    expect(
      screen.getByText(/Dữ liệu chi tiết đã đạt giới hạn tối đa \(100 dòng\)/)
    ).toBeInTheDocument()
  })

  it('renders empty description when total is 0', () => {
    const emptyReport: ReportSummaryResponse = {
      ...mockSummaryFixture,
      progress: { total: 0, done: 0, open: 0, overdue: 0, donePercent: 0 },
      byBoard: [],
      byAssignee: [],
    }

    render(<ReportSummaryPanel report={emptyReport} />)

    expect(
      screen.getByText('Không có task nào trong phạm vi báo cáo đã chọn')
    ).toBeInTheDocument()
  })
})
