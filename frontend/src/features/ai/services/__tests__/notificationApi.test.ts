import { afterEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { notificationApi } from '../notificationApi'
import type {
  NotificationListResponse,
  NotificationResponse,
} from '../../types/notification.types'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('notificationApi', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('listNotifications calls GET /notifications without params when none provided', async () => {
    const mockData: NotificationListResponse = {
      unreadCount: 2,
      items: [
        {
          id: 'n1',
          workspaceId: 'ws-1',
          type: 'OverdueTask',
          title: 'Task quá hạn',
          message: 'Task ABC trễ hạn',
          payload: { severity: 'High' },
          isRead: false,
          createdAt: '2026-09-10T12:00:00Z',
          readAt: null,
        },
      ],
    }

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const res = await notificationApi.listNotifications()

    expect(httpClient.get).toHaveBeenCalledWith('/notifications', {
      params: {},
    })
    expect(res).toEqual(mockData)
  })

  it('listNotifications calls GET /notifications with query params isRead and take', async () => {
    const mockData: NotificationListResponse = {
      unreadCount: 0,
      items: [],
    }

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const res = await notificationApi.listNotifications({ isRead: false, take: 10 })

    expect(httpClient.get).toHaveBeenCalledWith('/notifications', {
      params: { isRead: 'false', take: 10 },
    })
    expect(res).toEqual(mockData)
  })

  it('markNotificationRead calls POST /notifications/{id}/read', async () => {
    const mockData: NotificationResponse = {
      id: 'n1',
      workspaceId: 'ws-1',
      type: 'OverdueTask',
      title: 'Task quá hạn',
      message: 'Task ABC trễ hạn',
      payload: null,
      isRead: true,
      createdAt: '2026-09-10T12:00:00Z',
      readAt: '2026-09-10T12:05:00Z',
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockData })

    const res = await notificationApi.markNotificationRead('n1')

    expect(httpClient.post).toHaveBeenCalledWith('/notifications/n1/read')
    expect(res).toEqual(mockData)
  })

  it('markAllNotificationsRead calls POST /notifications/read-all', async () => {
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { updated: 5 } })

    const res = await notificationApi.markAllNotificationsRead()

    expect(httpClient.post).toHaveBeenCalledWith('/notifications/read-all')
    expect(res).toEqual({ updated: 5 })
  })
})
