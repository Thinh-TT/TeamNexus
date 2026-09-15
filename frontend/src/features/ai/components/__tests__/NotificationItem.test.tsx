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

  it('renders translated labels with raw codes for Phase 7 agent notifications', () => {
    const typesToTest = [
      { type: 'AgentRunFailed', expected: 'Agent thất bại (AgentRunFailed)' },
      { type: 'AgentAwaitingClarification', expected: 'Agent chờ làm rõ (AgentAwaitingClarification)' },
      { type: 'AgentOutputPending', expected: 'Agent chờ duyệt kết quả (AgentOutputPending)' },
    ]

    typesToTest.forEach(({ type, expected }) => {
      const notif: NotificationResponse = {
        ...mockNotification,
        id: `n-${type}`,
        type: type as any,
      }

      const { unmount } = render(
        <MemoryRouter>
          <NotificationItem
            notification={notif}
            onMarkRead={vi.fn()}
            workspaceId="ws-1"
          />
        </MemoryRouter>
      )

      expect(screen.getByText(expected)).toBeInTheDocument()
      unmount()
    })
  })

  it('renders translated labels for Phase 11 notification types: TaskAssigned, CommentOnTask, WorkspaceInvitation', () => {
    const phase11Types = [
      { type: 'TaskAssigned', expected: 'Được giao thẻ' },
      { type: 'CommentOnTask', expected: 'Bình luận mới' },
      { type: 'WorkspaceInvitation', expected: 'Lời mời workspace' },
    ]

    phase11Types.forEach(({ type, expected }) => {
      const notif: NotificationResponse = {
        ...mockNotification,
        id: `n-${type}`,
        type: type as any,
      }

      const { unmount } = render(
        <MemoryRouter>
          <NotificationItem
            notification={notif}
            onMarkRead={vi.fn()}
            workspaceId="ws-1"
          />
        </MemoryRouter>
      )

      expect(screen.getByText(expected)).toBeInTheDocument()
      unmount()
    })
  })

  it('renders neutrally without severity tag when notification payload has no severity', () => {
    const noSeverityNotif: NotificationResponse = {
      ...mockNotification,
      id: 'n-no-sev',
      type: 'TaskAssigned',
      payload: {
        taskId: 't-1',
        boardId: 'b-1',
      },
    }

    render(
      <MemoryRouter>
        <NotificationItem
          notification={noSeverityNotif}
          onMarkRead={vi.fn()}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )

    // Không hiển thị chip CRITICAL hay MEDIUM đỏ/cảnh báo
    expect(screen.queryByText('CRITICAL')).not.toBeInTheDocument()
    expect(screen.queryByText('MEDIUM')).not.toBeInTheDocument()
    expect(screen.getByText('Được giao thẻ')).toBeInTheDocument()
  })

  it('renders CommentMention type as "Được nhắc đến" and handles null payload gracefully', () => {
    const mentionNotif: NotificationResponse = {
      ...mockNotification,
      id: 'n-mention',
      type: 'CommentMention',
      title: 'Bạn được nhắc đến trong một bình luận',
      message: 'Trần An: Nhờ bạn xem lại phần auth',
      payload: null,
    }

    render(
      <MemoryRouter>
        <NotificationItem
          notification={mentionNotif}
          onMarkRead={vi.fn()}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )

    expect(screen.getByText('Bạn được nhắc đến trong một bình luận')).toBeInTheDocument()
    expect(screen.getByText('Được nhắc đến')).toBeInTheDocument()

    // Expand details to see message
    const expandBtn = screen.getAllByRole('button')[1]
    fireEvent.click(expandBtn)
    expect(screen.getByText('Trần An: Nhờ bạn xem lại phần auth')).toBeInTheDocument()
  })
})
