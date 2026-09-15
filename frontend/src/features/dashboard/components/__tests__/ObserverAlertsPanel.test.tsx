import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ObserverAlertsPanel } from '../ObserverAlertsPanel'
import { notificationApi } from '../../../ai/services/notificationApi'

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn(),
  },
}))

describe('ObserverAlertsPanel', () => {
  const wsId = 'ws-123'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches unread observer notifications and displays alert when count > 0', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce({
      unreadCount: 2,
      items: [
        {
          id: 'n-1',
          workspaceId: wsId,
          type: 'OverdueTask',
          title: 'Phát hiện thẻ quá hạn',
          message: 'Task XYZ đã quá hạn 2 ngày',
          payload: null,
          isRead: false,
          createdAt: '2026-09-15T10:00:00Z',
          readAt: null,
        },
      ],
    })

    render(<ObserverAlertsPanel workspaceId={wsId} />)

    await waitFor(() => {
      expect(notificationApi.listNotifications).toHaveBeenCalledWith({
        isRead: false,
        kind: 'observer',
        take: 10,
      })
    })

    expect(await screen.findByText(/Có 2 cảnh báo AI Observer chưa đọc/i)).toBeInTheDocument()
    expect(screen.getByText(/Phát hiện thẻ quá hạn/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Xem chi tiết/i })).toBeInTheDocument()
  })

  it('renders empty description when unreadCount is 0', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce({
      unreadCount: 0,
      items: [],
    })

    render(<ObserverAlertsPanel workspaceId={wsId} />)

    await waitFor(() => {
      expect(notificationApi.listNotifications).toHaveBeenCalled()
    })

    expect(
      await screen.findByText('Không có cảnh báo AI Observer chưa đọc')
    ).toBeInTheDocument()
  })
})
