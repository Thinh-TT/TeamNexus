import React from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AiActionLogItem } from '../AiActionLogItem'
import type { AiActionLog, AiActionLogDetail } from '../../types/aiAction.types'

const mockPendingLog: AiActionLog = {
  id: 'log-1',
  action: 'CreateSubtasks',
  entityType: 'Board',
  entityId: 'board-1',
  status: 'Pending',
  requestedByUserId: 'u-1',
  requestedByName: 'Thinh-TT',
  decidedByUserId: null,
  decidedByName: null,
  decidedAt: null,
  decisionNote: null,
  taskCount: 2,
  createdAt: '2026-09-10T08:00:00Z',
  updatedAt: '2026-09-10T08:00:00Z',
}

const mockApprovedLog: AiActionLog = {
  ...mockPendingLog,
  id: 'log-2',
  status: 'Approved',
  decidedByUserId: 'u-2',
  decidedByName: 'Manager-1',
  decidedAt: '2026-09-10T08:05:00Z',
}

const mockRejectedLog: AiActionLog = {
  ...mockPendingLog,
  id: 'log-3',
  status: 'Rejected',
  decidedByUserId: 'u-2',
  decidedByName: 'Manager-1',
  decidedAt: '2026-09-10T08:05:00Z',
  decisionNote: 'Không phù hợp với sprint này',
}

const mockUndoneLog: AiActionLog = {
  ...mockPendingLog,
  id: 'log-4',
  status: 'Undone',
  decidedByUserId: 'u-2',
  decidedByName: 'Manager-1',
  decidedAt: '2026-09-10T08:10:00Z',
}

describe('AiActionLogItem', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('renders Pending status tag and action buttons (Duyệt, Từ chối)', () => {
    const onApprove = vi.fn().mockResolvedValue({})
    const onReject = vi.fn().mockResolvedValue({})
    const onUndo = vi.fn().mockResolvedValue({})

    render(
      <AiActionLogItem
        log={mockPendingLog}
        onApprove={onApprove}
        onReject={onReject}
        onUndo={onUndo}
      />
    )

    expect(screen.getByText('Chờ duyệt')).toBeInTheDocument()
    expect(screen.getByText('Tạo sub-tasks đề xuất')).toBeInTheDocument()
    expect(screen.getByText('2 tasks')).toBeInTheDocument()
    expect(screen.getByText('Thinh-TT')).toBeInTheDocument()

    expect(screen.getByRole('button', { name: /Duyệt & áp dụng/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Từ chối/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Hoàn tác/i })).not.toBeInTheDocument()
  })

  it('renders Approved status and Hoàn tác button', () => {
    render(
      <AiActionLogItem
        log={mockApprovedLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Đã duyệt')).toBeInTheDocument()
    expect(screen.getByText('Manager-1')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Hoàn tác hành động/i })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Duyệt/i })).not.toBeInTheDocument()
  })

  it('renders Rejected status with decision note and no action buttons', () => {
    render(
      <AiActionLogItem
        log={mockRejectedLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Đã từ chối')).toBeInTheDocument()
    expect(screen.getByText('Không phù hợp với sprint này')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Duyệt/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Hoàn tác/i })).not.toBeInTheDocument()
  })

  it('renders Undone status and no action buttons', () => {
    render(
      <AiActionLogItem
        log={mockUndoneLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Đã hoàn tác')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Duyệt/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Hoàn tác/i })).not.toBeInTheDocument()
  })

  it('toggles detail collapse and fetches log detail', async () => {
    const mockDetail: AiActionLogDetail = {
      ...mockPendingLog,
      basis: { descriptionExcerpt: 'Mô tả tóm tắt 50 ký tự' },
      beforeSnapshot: null,
      afterSnapshot: {
        description: 'Mô tả',
        summary: null,
        columnId: null,
        tasks: [
          {
            title: 'Subtask 1: Backend API',
            description: 'Tạo REST API',
            priority: 'High',
            labels: [{ labelId: null, name: 'backend', exists: false }],
            assignee: null,
          },
        ],
      },
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    const onFetchDetail = vi.fn().mockResolvedValue(mockDetail)

    render(
      <AiActionLogItem
        log={mockPendingLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
        onFetchDetail={onFetchDetail}
      />
    )

    const detailBtn = screen.getByRole('button', { name: /Chi tiết/i })
    fireEvent.click(detailBtn)

    await waitFor(() => {
      expect(onFetchDetail).toHaveBeenCalledWith('log-1')
      expect(screen.getByText('Mô tả tóm tắt 50 ký tự')).toBeInTheDocument()
      expect(screen.getByText('Subtask 1: Backend API')).toBeInTheDocument()
      expect(screen.getByText('Tạo REST API')).toBeInTheDocument()
    })
  })
})
