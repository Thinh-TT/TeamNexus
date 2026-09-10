import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { NotificationDrawer } from '../NotificationDrawer'
import { notificationApi } from '../../services/notificationApi'
import type { NotificationListResponse } from '../../types/notification.types'

vi.mock('../../services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn(),
    markNotificationRead: vi.fn(),
    markAllNotificationsRead: vi.fn(),
  },
}))

describe('NotificationDrawer', () => {
  const mockNotificationList: NotificationListResponse = {
    unreadCount: 1,
    items: [
      {
        id: 'notif-1',
        workspaceId: 'ws-1',
        type: 'StalledTask',
        title: 'Task bị đứng yên',
        message: 'Task không có cập nhật trong 10 ngày',
        payload: { severity: 'High' },
        isRead: false,
        createdAt: '2026-09-10T10:00:00Z',
        readAt: null,
      },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(notificationApi.listNotifications).mockResolvedValue(mockNotificationList)
  })

  const renderComponent = (open = true) => {
    return render(
      <MemoryRouter>
        <NotificationDrawer
          open={open}
          onClose={vi.fn()}
          workspaceId="ws-1"
        />
      </MemoryRouter>
    )
  }

  it('renders drawer header, unread badge, and notification items when open', async () => {
    renderComponent(true)

    expect(await screen.findByText('Cảnh báo AI Observer')).toBeInTheDocument()
    expect(screen.getByText('1 chưa đọc')).toBeInTheDocument()
    expect(await screen.findByText('Task bị đứng yên')).toBeInTheDocument()
    expect(screen.getByText('Đọc tất cả')).toBeInTheDocument()
  })

  it('switches filter between Chưa đọc and Tất cả', async () => {
    renderComponent(true)

    expect(await screen.findByText('Task bị đứng yên')).toBeInTheDocument()

    const allTab = screen.getByText('Tất cả')
    fireEvent.click(allTab)

    expect(notificationApi.listNotifications).toHaveBeenCalledWith({
      isRead: undefined,
      take: 50,
    })
  })

  it('calls markAllNotificationsRead when clicking Đọc tất cả button', async () => {
    vi.mocked(notificationApi.markAllNotificationsRead).mockResolvedValueOnce({
      updated: 1,
    })

    renderComponent(true)

    const markAllBtn = await screen.findByText('Đọc tất cả')
    fireEvent.click(markAllBtn)

    expect(notificationApi.markAllNotificationsRead).toHaveBeenCalled()
  })

  it('renders empty description when items list is empty', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce({
      unreadCount: 0,
      items: [],
    })

    renderComponent(true)

    expect(
      await screen.findByText('Không có cảnh báo chưa đọc nào.')
    ).toBeInTheDocument()
  })
})
