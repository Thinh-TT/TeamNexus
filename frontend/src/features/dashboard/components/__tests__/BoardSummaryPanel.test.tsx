import React from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { BoardSummaryPanel } from '../BoardSummaryPanel'
import type { DashboardBoardSummary } from '../../types/dashboard.types'

describe('BoardSummaryPanel', () => {
  const wsId = 'ws-123'

  const mockBoards: DashboardBoardSummary[] = [
    {
      boardId: 'b-1',
      name: 'Sprint 1',
      total: 10,
      done: 6,
      open: 4,
      overdue: 1,
      columns: [
        { columnId: 'c-1', name: 'To Do', isDone: false, count: 4 },
        { columnId: 'c-2', name: 'Done', isDone: true, count: 6 },
      ],
    },
    {
      boardId: 'b-2',
      name: 'Bảng Trống',
      total: 0,
      done: 0,
      open: 0,
      overdue: 0,
      columns: [],
    },
  ]

  it('renders board details, progress and columns correctly', () => {
    render(
      <MemoryRouter>
        <BoardSummaryPanel workspaceId={wsId} boards={mockBoards} boardsTruncated={false} />
      </MemoryRouter>
    )

    expect(screen.getByText('Sprint 1')).toBeInTheDocument()
    expect(screen.getByText('To Do: 4')).toBeInTheDocument()
    expect(screen.getByText('Done: 6')).toBeInTheDocument()
    expect(screen.getByText('Quá hạn:')).toBeInTheDocument()
    // Board rỗng không bị chia cho 0
    expect(screen.getByText('Bảng Trống')).toBeInTheDocument()
  })

  it('displays warning alert when boardsTruncated is true', () => {
    render(
      <MemoryRouter>
        <BoardSummaryPanel workspaceId={wsId} boards={mockBoards} boardsTruncated={true} />
      </MemoryRouter>
    )

    expect(
      screen.getByText('Workspace có nhiều bảng: chỉ hiển thị 20 bảng đầu tiên.')
    ).toBeInTheDocument()
  })

  it('renders empty description when boards array is empty', () => {
    render(
      <MemoryRouter>
        <BoardSummaryPanel workspaceId={wsId} boards={[]} boardsTruncated={false} />
      </MemoryRouter>
    )

    expect(screen.getByText('Chưa có bảng nào trong workspace')).toBeInTheDocument()
  })
})
