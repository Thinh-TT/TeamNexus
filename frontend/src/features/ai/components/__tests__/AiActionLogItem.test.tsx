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

  it('renders "Tạo board từ mẫu AI" label for action CreateBoardFromTemplate', () => {
    const templateLog: AiActionLog = {
      ...mockPendingLog,
      action: 'CreateBoardFromTemplate',
      entityType: 'Workspace',
    }

    render(
      <AiActionLogItem
        log={templateLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Tạo board từ mẫu AI')).toBeInTheDocument()
  })

  it('displays custom Popconfirm description for CreateBoardFromTemplate when Undo is clicked', async () => {
    const approvedTemplateLog: AiActionLog = {
      ...mockApprovedLog,
      action: 'CreateBoardFromTemplate',
      entityType: 'Workspace',
    }

    render(
      <AiActionLogItem
        log={approvedTemplateLog}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    const undoBtn = screen.getByRole('button', { name: /Hoàn tác hành động/i })
    fireEvent.click(undoBtn)

    await waitFor(() => {
      expect(
        screen.getByText(/Board và mọi thẻ trong đó sẽ bị ẩn khỏi không gian làm việc/i)
      ).toBeInTheDocument()
    })
  })

  // Phase 14.5 §B: the two field-level actions the board chat can propose. Without their own labels they
  // would fall through to the raw action string ("UpdateTaskFields" / "MoveTaskToColumn").
  it('labels the UpdateTaskFields action the board chat proposes', () => {
    render(
      <AiActionLogItem
        log={{ ...mockPendingLog, action: 'UpdateTaskFields', entityType: 'Task', entityId: 'task-1' }}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Cập nhật thẻ (AI đề xuất)')).toBeInTheDocument()
  })

  it('labels the MoveTaskToColumn action the board chat proposes', () => {
    render(
      <AiActionLogItem
        log={{ ...mockPendingLog, action: 'MoveTaskToColumn', entityType: 'Task', entityId: 'task-1' }}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Di chuyển thẻ (AI đề xuất)')).toBeInTheDocument()
  })

  // Phase 15.3/15.4 review F9: system GitHub actions reach the same history drawer, so they need a
  // Vietnamese label, no misleading "0 tasks" tag, and a basis line that explains the GitHub event.
  it('labels the GitHubPrMoveTask action and hides the empty task-count tag', () => {
    render(
      <AiActionLogItem
        log={{ ...mockApprovedLog, action: 'GitHubPrMoveTask', taskCount: 0 }}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Tự động chuyển thẻ (GitHub PR)')).toBeInTheDocument()
    expect(screen.queryByText('0 tasks')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Hoàn tác hành động/i })).toBeInTheDocument()
  })

  it('labels the GitHubIssueCreateTask action and keeps its task count', () => {
    render(
      <AiActionLogItem
        log={{ ...mockApprovedLog, action: 'GitHubIssueCreateTask', taskCount: 1 }}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
      />
    )

    expect(screen.getByText('Tạo thẻ từ GitHub Issue')).toBeInTheDocument()
    expect(screen.getByText('1 tasks')).toBeInTheDocument()
  })

  it('describes the GitHub basis instead of rendering an empty description', async () => {
    const githubDetail: AiActionLogDetail = {
      ...mockApprovedLog,
      action: 'GitHubIssueCreateTask',
      taskCount: 0,
      basis: { repo: 'octocat/TeamNexus', issueNumber: 42, state: 'open' },
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    render(
      <AiActionLogItem
        log={{ ...mockApprovedLog, action: 'GitHubIssueCreateTask', taskCount: 0 }}
        onApprove={vi.fn()}
        onReject={vi.fn()}
        onUndo={vi.fn()}
        onFetchDetail={vi.fn().mockResolvedValue(githubDetail)}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Chi tiết/i }))

    await waitFor(() => {
      expect(
        screen.getByText('octocat/TeamNexus · Issue #42 · trạng thái open')
      ).toBeInTheDocument()
      expect(screen.queryByText(/Mô tả 0 ký tự/)).not.toBeInTheDocument()
    })
  })
})
