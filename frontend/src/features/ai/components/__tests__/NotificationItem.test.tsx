import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { NotificationItem } from '../NotificationItem'
import type { NotificationResponse } from '../../types/notification.types'

const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

describe('NotificationItem', () => {
  const mockNotification: NotificationResponse = {
    id: 'n-123',
    workspaceId: 'ws-1',
    type: 'OverdueTask',
    title: 'Thẻ Backend API trễ hạn',
    message: 'Thẻ Backend API đã quá hạn 3 ngày tại cột Đang làm.',
    payload: {
      severity: 'Critical',
      boardId: 'board-999',
    },
    isRead: false,
    createdAt: '2026-09-10T08:00:00Z',
    readAt: null,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders title, severity, type, and mark-read button when unread', () => {
    const onMarkRead = vi.fn()
    render(
      <MemoryRouter>
        <NotificationItem
          notification={mockNotification}
          onMarkRead={onMarkRead}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )

    expect(screen.getByText('Thẻ Backend API trễ hạn')).toBeInTheDocument()
    expect(screen.getByText('CRITICAL')).toBeInTheDocument()
    expect(screen.getByText('Quá hạn')).toBeInTheDocument()
    expect(screen.getByText('Đã đọc')).toBeInTheDocument()
  })

  it('calls onMarkRead when clicking Đã đọc button', () => {
    const onMarkRead = vi.fn()
    render(
      <MemoryRouter>
        <NotificationItem
          notification={mockNotification}
          onMarkRead={onMarkRead}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )

    const markBtn = screen.getByText('Đã đọc')
    fireEvent.click(markBtn)

    expect(onMarkRead).toHaveBeenCalledWith('n-123')
  })

  it('expands details and navigates to board on link click', () => {
    const onMarkRead = vi.fn()
    render(
      <MemoryRouter>
        <NotificationItem
          notification={mockNotification}
          onMarkRead={onMarkRead}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )

    // Expand details
    const expandBtn = screen.getAllByRole('button')[1] // Second button is expand icon
    fireEvent.click(expandBtn)

    expect(
      screen.getByText('Thẻ Backend API đã quá hạn 3 ngày tại cột Đang làm.')
    ).toBeInTheDocument()

    const boardLink = screen.getByText('Mở bảng Kanban liên quan')
    expect(boardLink).toBeInTheDocument()
    fireEvent.click(boardLink)

    expect(mockNavigate).toHaveBeenCalledWith('/workspaces/ws-1/boards/board-999')
  })
})
