import React from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SmartSetupModal } from '../SmartSetupModal'
import { smartSetupApi } from '../../services/smartSetupApi'
import { aiActionApi } from '../../services/aiActionApi'
import type { SmartSetupProposal } from '../../types/smartSetup.types'
import type { AiActionLog, AiActionLogDetail } from '../../types/aiAction.types'
import type { ColumnResponse } from '../../../board/types/board.types'

vi.mock('../../services/smartSetupApi', () => ({
  smartSetupApi: {
    generateSmartSetup: vi.fn(),
    getWorkspaceMembers: vi.fn(),
  },
}))

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    confirmSmartSetup: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

const mockColumns: ColumnResponse[] = [
  {
    id: 'col-1',
    boardId: 'board-1',
    name: 'To Do',
    position: 0,
    isDone: false,
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    tasks: [],
  },
  {
    id: 'col-2',
    boardId: 'board-1',
    name: 'Done',
    position: 1,
    isDone: true,
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    tasks: [],
  },
]

describe('SmartSetupModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(smartSetupApi.getWorkspaceMembers).mockResolvedValue([])
  })

  afterEach(() => {
    cleanup()
  })

  it('renders Step 1 prompt input when open', () => {
    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
        columns={mockColumns}
      />
    )

    expect(screen.getByText('AI Smart Setup')).toBeInTheDocument()
    expect(
      screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    ).toBeInTheDocument()
  })

  it('generates proposal and transitions to Step 2 review', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Phân tích hệ thống thành 2 sub-task',
      tasks: [
        {
          title: 'Subtask 1: Backend Setup',
          description: 'Cấu hình database schema',
          priority: 'High',
          labels: [{ labelId: null, name: 'backend', exists: false }],
          assignee: null,
        },
      ],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
        columns={mockColumns}
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, {
      target: { value: 'Xây dựng module AI Smart Setup' },
    })

    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    fireEvent.click(generateBtn)

    await waitFor(() => {
      expect(smartSetupApi.generateSmartSetup).toHaveBeenCalledWith('board-1', {
        description: 'Xây dựng module AI Smart Setup',
      })
      expect(screen.getByText('Phân tích hệ thống thành 2 sub-task')).toBeInTheDocument()
      expect(screen.getByDisplayValue('Subtask 1: Backend Setup')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /Xác nhận đề xuất/i })).toBeInTheDocument()
    })
  })

  it('transitions to Step 3 pending state on clicking confirm, and approves proposal', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Hoàn thành đề xuất',
      tasks: [
        {
          title: 'Task A',
          description: 'Desc A',
          priority: 'Medium',
          labels: [],
          assignee: null,
        },
      ],
    }

    const mockLog: AiActionLog = {
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
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }

    const mockApproved: AiActionLogDetail = {
      ...mockLog,
      status: 'Approved',
      decidedByUserId: 'u-2',
      decidedByName: 'Manager',
      decidedAt: '2026-09-10T08:05:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: {
        entityType: 'Board',
        entityId: 'board-1',
        createdTaskIds: ['t1'],
        createdLabelIds: [],
        warnings: [],
        appliedAt: '2026-09-10T08:05:00Z',
      },
      createdTaskIds: ['t1'],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)
    vi.mocked(aiActionApi.confirmSmartSetup).mockResolvedValueOnce(mockLog)
    vi.mocked(aiActionApi.approveAiAction).mockResolvedValueOnce(mockApproved)

    const onApplied = vi.fn()

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
        columns={mockColumns}
        onApplied={onApplied}
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ' } })
    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    fireEvent.click(generateBtn)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Xác nhận đề xuất/i })).toBeInTheDocument()
    })

    const confirmBtn = screen.getByRole('button', { name: /Xác nhận đề xuất/i })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(aiActionApi.confirmSmartSetup).toHaveBeenCalledWith('board-1', {
        description: 'Mô tả hợp lệ',
        summary: 'Hoàn thành đề xuất',
        tasks: [
          {
            title: 'Task A',
            description: 'Desc A',
            priority: 'Medium',
            labels: [],
            assignee: null,
          },
        ],
        columnId: 'col-1',
      })
      expect(
        screen.getByText('Đã tạo yêu cầu chờ phê duyệt!')
      ).toBeInTheDocument()
      expect(screen.getByText(/Pending/i)).toBeInTheDocument()
      expect(screen.queryByText(/Giai đoạn 4/i)).not.toBeInTheDocument()
    })

    // Click "Duyệt & áp dụng ngay"
    const approveBtn = screen.getByRole('button', { name: /Duyệt & áp dụng ngay/i })
    fireEvent.click(approveBtn)

    await waitFor(() => {
      expect(aiActionApi.approveAiAction).toHaveBeenCalledWith('log-1')
      expect(
        screen.getByText('Đã duyệt & áp dụng thành công!')
      ).toBeInTheDocument()
      expect(onApplied).toHaveBeenCalledTimes(1)
    })
  })

  it('displays error alert when generation fails with 403 Forbidden', async () => {
    const axiosErr = {
      isAxiosError: true,
      response: {
        status: 403,
        data: { error: 'Requires Manager or Admin role in this workspace.' },
      },
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockRejectedValueOnce(axiosErr)

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
        columns={mockColumns}
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả từ Member' } })

    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    await act(async () => {
      fireEvent.click(generateBtn)
    })

    await waitFor(() => {
      expect(smartSetupApi.generateSmartSetup).toHaveBeenCalledWith('board-1', {
        description: 'Mô tả từ Member',
      })
    })

    expect(await screen.findByText(/Không đủ quyền thực hiện/i)).toBeInTheDocument()
    expect(screen.getByText(/Manager/i)).toBeInTheDocument()
  })
})
