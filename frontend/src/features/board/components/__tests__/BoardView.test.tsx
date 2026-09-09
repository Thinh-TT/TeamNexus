import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { BoardView } from '../BoardView'
import type { BoardResponse, ColumnResponse, TaskResponse } from '../../types/board.types'

const mockBoard: BoardResponse = {
  id: 'board-1',
  workspaceId: 'ws-1',
  name: 'Sprint 2 Board',
  description: 'Main Sprint Board',
  createdAt: '2026-09-09T00:00:00Z',
  updatedAt: '2026-09-09T00:00:00Z',
  columns: [],
}

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
]

const mockTasks: TaskResponse[] = [
  {
    id: 'task-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Deploy to Staging',
    description: null,
    position: 0,
    priority: 'Urgent',
    assigneeName: 'John Doe',
    labels: [{ id: 'lbl-1', workspaceId: 'ws-1', name: 'DevOps', color: '#3b82f6', createdAt: '' }],
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    commentCount: 0,
  },
  {
    id: 'task-2',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Write Documentation',
    description: null,
    position: 1,
    priority: 'Low',
    assigneeName: 'Jane Smith',
    labels: [],
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    commentCount: 0,
  },
]

const mockUseBoardResult = {
  board: mockBoard,
  columns: mockColumns,
  tasksByColumn: { 'col-1': mockTasks },
  workspaceLabels: [],
  activeTask: null,
  connectionStatus: 'connected',
  isLoading: false,
  refetch: vi.fn(),
  setActiveTask: vi.fn(),
  createColumn: vi.fn(),
  updateColumn: vi.fn(),
  deleteColumn: vi.fn(),
  createTask: vi.fn(),
  updateTask: vi.fn(),
  moveTask: vi.fn(),
  deleteTask: vi.fn(),
  createLabel: vi.fn(),
  attachLabel: vi.fn(),
  detachLabel: vi.fn(),
}

vi.mock('../../hooks/useBoard', () => ({
  useBoard: () => mockUseBoardResult,
}))

describe('BoardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  const renderComponent = () => {
    return render(
      <MemoryRouter>
        <BoardView workspaceId="ws-1" boardId="board-1" />
      </MemoryRouter>
    )
  }

  it('renders board title, connection status tag, and column cards', () => {
    renderComponent()

    expect(screen.getByText('Sprint 2 Board')).toBeInTheDocument()
    expect(screen.getByText('Đã kết nối')).toBeInTheDocument()
    expect(screen.getByText('To Do')).toBeInTheDocument()
    expect(screen.getByText('Deploy to Staging')).toBeInTheDocument()
    expect(screen.getByText('Write Documentation')).toBeInTheDocument()
  })

  it('filters tasks based on search input query (matching title or assignee or label)', () => {
    renderComponent()

    const searchInput = screen.getByPlaceholderText('Tìm kiếm thẻ, nhãn, người...')
    fireEvent.change(searchInput, { target: { value: 'Deploy' } })

    expect(screen.getByText('Deploy to Staging')).toBeInTheDocument()
    expect(screen.queryByText('Write Documentation')).not.toBeInTheDocument()

    // Search by label
    fireEvent.change(searchInput, { target: { value: 'DevOps' } })
    expect(screen.getByText('Deploy to Staging')).toBeInTheDocument()

    // Search by assignee
    fireEvent.change(searchInput, { target: { value: 'Jane' } })
    expect(screen.queryByText('Deploy to Staging')).not.toBeInTheDocument()
    expect(screen.getByText('Write Documentation')).toBeInTheDocument()
  })
})
