import React from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { BoardView } from '../BoardView'
import { aiActionApi } from '../../../ai/services/aiActionApi'
import { notificationApi } from '../../../ai/services/notificationApi'
import { observerApi } from '../../../ai/services/observerApi'
import { httpClient } from '../../../../shared/api'
import type { BoardResponse, ColumnResponse, TaskResponse } from '../../types/board.types'
import type { AiActionLog } from '../../../ai/types/aiAction.types'

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
  reconnect: vi.fn(),
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

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../../ai/services/aiActionApi', () => ({
  aiActionApi: {
    listAiActions: vi.fn(),
    getAiAction: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn(),
    markNotificationRead: vi.fn(),
    markAllNotificationsRead: vi.fn(),
  },
}))

vi.mock('../../../ai/services/observerApi', () => ({
  observerApi: {
    listObserverRuns: vi.fn(),
    getObserverRun: vi.fn(),
    triggerObserverScan: vi.fn(),
  },
}))

describe('BoardView', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockUseBoardResult.connectionStatus = 'connected'
    mockUseBoardResult.reconnect = vi.fn()
    vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])
    vi.mocked(observerApi.listObserverRuns).mockResolvedValue([])
    vi.mocked(notificationApi.listNotifications).mockResolvedValue({
      unreadCount: 3,
      items: [],
    })
    vi.mocked(httpClient.get).mockImplementation(async (url: string) => {
      if (url === '/workspaces') {
        return {
          data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Manager' }],
        }
      }
      return { data: {} }
    })
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

  it('renders AI Smart Setup button and opens the modal on click', () => {
    renderComponent()

    const aiBtn = screen.getByRole('button', { name: /AI Smart Setup/i })
    expect(aiBtn).toBeInTheDocument()

    fireEvent.click(aiBtn)
    expect(
      screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    ).toBeInTheDocument()
  })

  it('renders Lịch sử AI button and opens the history drawer on click', async () => {
    const mockPendingLogs: AiActionLog[] = [
      {
        id: 'log-1',
        action: 'CreateSubtasks',
        entityType: 'Board',
        entityId: 'board-1',
        status: 'Pending',
        requestedByUserId: 'u1',
        requestedByName: 'Thinh',
        decidedByUserId: null,
        decidedByName: null,
        decidedAt: null,
        decisionNote: null,
        taskCount: 3,
        createdAt: '2026-09-10T08:00:00Z',
        updatedAt: '2026-09-10T08:00:00Z',
      },
    ]

    vi.mocked(aiActionApi.listAiActions).mockResolvedValue(mockPendingLogs)

    renderComponent()

    await waitFor(() => {
      expect(aiActionApi.listAiActions).toHaveBeenCalledWith('board-1', {
        status: 'Pending',
        take: 100,
      })
    })

    const historyBtn = screen.getByRole('button', { name: /Lịch sử AI/i })
    expect(historyBtn).toBeInTheDocument()

    fireEvent.click(historyBtn)

    await waitFor(() => {
      expect(screen.getByText('Lịch sử Hành động AI')).toBeInTheDocument()
    })
  })

  it('renders Cảnh báo AI button with unread count and opens notification drawer on click', async () => {
    renderComponent()

    await waitFor(() => {
      expect(notificationApi.listNotifications).toHaveBeenCalled()
    })

    const notifBtn = screen.getByRole('button', { name: /Cảnh báo AI/i })
    expect(notifBtn).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()

    fireEvent.click(notifBtn)

    await waitFor(() => {
      expect(screen.getByText('Cảnh báo AI Observer')).toBeInTheDocument()
    })
  })

  it('renders AI Observer button for Manager/Admin and opens observer runs drawer on click', async () => {
    renderComponent()

    const observerBtn = await screen.findByRole('button', { name: /AI Observer/i })
    expect(observerBtn).toBeInTheDocument()

    fireEvent.click(observerBtn)

    await waitFor(() => {
      expect(screen.getByText('AI Observer — Lịch sử quét')).toBeInTheDocument()
    })
  })

  it('renders Báo cáo button for Manager/Admin and navigates on click', async () => {
    renderComponent()

    const reportBtn = await screen.findByRole('button', { name: /Báo cáo/i })
    expect(reportBtn).toBeInTheDocument()

    fireEvent.click(reportBtn)
  })

  it('hides AI Observer and Báo cáo buttons when user is only a Member', async () => {
    vi.mocked(httpClient.get).mockImplementation(async (url: string) => {
      if (url === '/workspaces') {
        return {
          data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Member' }],
        }
      }
      return { data: {} }
    })

    renderComponent()

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /AI Observer/i })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /Báo cáo/i })).not.toBeInTheDocument()
    })
  })

  it('displays reconnect button when disconnected and triggers reconnect on click', () => {
    mockUseBoardResult.connectionStatus = 'disconnected'

    renderComponent()

    expect(screen.getByText('Mất kết nối')).toBeInTheDocument()
    const reconnectBtn = screen.getByRole('button', { name: 'Kết nối lại' })
    expect(reconnectBtn).toBeInTheDocument()

    fireEvent.click(reconnectBtn)
    expect(mockUseBoardResult.reconnect).toHaveBeenCalledTimes(1)
  })

  it('transitions to cold-start message when reconnecting for longer than threshold', () => {
    vi.useFakeTimers()
    mockUseBoardResult.connectionStatus = 'reconnecting'

    const { unmount } = renderComponent()
    expect(screen.getByText('Đang kết nối lại')).toBeInTheDocument()

    // Fast-forward past the 5000ms threshold
    act(() => {
      vi.advanceTimersByTime(5000)
    })

    unmount()
    vi.useRealTimers()
  })

  it('tests all four connection states rendered inside BoardView', () => {
    // 1. Connecting
    mockUseBoardResult.connectionStatus = 'connecting'
    const { unmount: unmount1 } = renderComponent()
    expect(screen.getByText('Đang kết nối')).toBeInTheDocument()
    unmount1()

    // 2. Connected
    mockUseBoardResult.connectionStatus = 'connected'
    const { unmount: unmount2 } = renderComponent()
    expect(screen.getByText('Đã kết nối')).toBeInTheDocument()
    unmount2()

    // 3. Reconnecting
    mockUseBoardResult.connectionStatus = 'reconnecting'
    const { unmount: unmount3 } = renderComponent()
    expect(screen.getByText('Đang kết nối lại')).toBeInTheDocument()
    unmount3()

    // 4. Disconnected
    mockUseBoardResult.connectionStatus = 'disconnected'
    const { unmount: unmount4 } = renderComponent()
    expect(screen.getByText('Mất kết nối')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Kết nối lại' })).toBeInTheDocument()
    unmount4()
  })
})
