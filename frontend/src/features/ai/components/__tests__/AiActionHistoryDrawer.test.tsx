import React from 'react'
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AiActionHistoryDrawer } from '../AiActionHistoryDrawer'
import { aiActionApi } from '../../services/aiActionApi'
import type { AiActionLog } from '../../types/aiAction.types'

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    listAiActions: vi.fn(),
    getAiAction: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

describe('AiActionHistoryDrawer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    cleanup()
  })

  it('fetches logs and renders drawer title, segmented filters, and items when open', async () => {
    const mockLogs: AiActionLog[] = [
      {
        id: 'log-1',
        action: 'CreateSubtasks',
        entityType: 'Board',
        entityId: 'board-1',
        status: 'Pending',
        requestedByUserId: 'u1',
        requestedByName: 'Thinh-TT',
        decidedByUserId: null,
        decidedByName: null,
        decidedAt: null,
        decisionNote: null,
        taskCount: 3,
        createdAt: '2026-09-10T08:00:00Z',
        updatedAt: '2026-09-10T08:00:00Z',
      },
    ]

    vi.mocked(aiActionApi.listAiActions).mockResolvedValueOnce(mockLogs)

    render(
      <AiActionHistoryDrawer
        open={true}
        onClose={vi.fn()}
        boardId="board-1"
      />
    )

    expect(screen.getByText('Lịch sử Hành động AI')).toBeInTheDocument()
    expect(screen.getByText('Tất cả')).toBeInTheDocument()

    await waitFor(() => {
      expect(aiActionApi.listAiActions).toHaveBeenCalledWith('board-1', {
        status: undefined,
        take: 50,
      })
      expect(screen.getByText('Tạo sub-tasks đề xuất')).toBeInTheDocument()
      expect(screen.getByText('3 tasks')).toBeInTheDocument()
    })
  })

  it('changes filter and reloads filtered logs', async () => {
    vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])

    render(
      <AiActionHistoryDrawer
        open={true}
        onClose={vi.fn()}
        boardId="board-1"
      />
    )

    const approvedTab = screen.getByText('Đã duyệt')
    fireEvent.click(approvedTab)

    await waitFor(() => {
      expect(aiActionApi.listAiActions).toHaveBeenCalledWith('board-1', {
        status: 'Approved',
        take: 50,
      })
    })
  })

  it('renders empty state when no logs returned', async () => {
    vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])

    render(
      <AiActionHistoryDrawer
        open={true}
        onClose={vi.fn()}
        boardId="board-1"
      />
    )

    await waitFor(() => {
      expect(
        screen.getByText(/Chưa có hành động AI nào được ghi nhận/i)
      ).toBeInTheDocument()
    })
  })
})
