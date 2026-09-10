import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { notificationApi } from '../../services/notificationApi'
import { useNotifications } from '../useNotifications'
import type { NotificationListResponse, NotificationResponse } from '../../types/notification.types'

vi.mock('../../services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn(),
    markNotificationRead: vi.fn(),
    markAllNotificationsRead: vi.fn(),
  },
}))

describe('useNotifications', () => {
  const mockNotification1: NotificationResponse = {
    id: 'notif-1',
    workspaceId: 'ws-1',
    type: 'OverdueTask',
    title: 'Task A quá hạn',
    message: 'Chi tiết task A trễ hạn',
    payload: { severity: 'High', boardId: 'board-1' },
    isRead: false,
    createdAt: '2026-09-10T10:00:00Z',
    readAt: null,
  }

  const mockNotification2: NotificationResponse = {
    id: 'notif-2',
    workspaceId: 'ws-1',
    type: 'StalledTask',
    title: 'Task B đứng yên',
    message: 'Chi tiết task B không cập nhật',
    payload: { severity: 'Medium' },
    isRead: true,
    createdAt: '2026-09-10T09:00:00Z',
    readAt: '2026-09-10T09:30:00Z',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('initializes with default state', () => {
    const { result } = renderHook(() => useNotifications())

    expect(result.current.items).toEqual([])
    expect(result.current.unreadCount).toBe(0)
    expect(result.current.status).toBe('idle')
    expect(result.current.error).toBeNull()
    expect(result.current.httpStatus).toBeNull()
  })

  it('reload fetches notifications and updates state', async () => {
    const mockListRes: NotificationListResponse = {
      unreadCount: 1,
      items: [mockNotification1, mockNotification2],
    }
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce(mockListRes)

    const { result } = renderHook(() => useNotifications())

    await act(async () => {
      await result.current.reload()
    })

    expect(notificationApi.listNotifications).toHaveBeenCalledWith({
      isRead: undefined,
      take: 50,
    })
    expect(result.current.items).toEqual([mockNotification1, mockNotification2])
    expect(result.current.unreadCount).toBe(1)
    expect(result.current.status).toBe('idle')
  })

  it('markRead updates specific notification and decrements unreadCount', async () => {
    const mockListRes: NotificationListResponse = {
      unreadCount: 1,
      items: [mockNotification1],
    }
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce(mockListRes)

    const updatedNotif: NotificationResponse = {
      ...mockNotification1,
      isRead: true,
      readAt: '2026-09-10T11:00:00Z',
    }
    vi.mocked(notificationApi.markNotificationRead).mockResolvedValueOnce(updatedNotif)

    const { result } = renderHook(() => useNotifications())

    await act(async () => {
      await result.current.reload()
    })

    await act(async () => {
      await result.current.markRead('notif-1')
    })

    expect(notificationApi.markNotificationRead).toHaveBeenCalledWith('notif-1')
    expect(result.current.items[0].isRead).toBe(true)
    expect(result.current.items[0].readAt).toBe('2026-09-10T11:00:00Z')
    expect(result.current.unreadCount).toBe(0)
  })

  it('markRead removes item when initial filter is unread (isRead = false)', async () => {
    const mockListRes: NotificationListResponse = {
      unreadCount: 1,
      items: [mockNotification1],
    }
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce(mockListRes)

    const updatedNotif: NotificationResponse = {
      ...mockNotification1,
      isRead: true,
      readAt: '2026-09-10T11:00:00Z',
    }
    vi.mocked(notificationApi.markNotificationRead).mockResolvedValueOnce(updatedNotif)

    const { result } = renderHook(() => useNotifications(false))

    await act(async () => {
      await result.current.reload(false)
    })

    await act(async () => {
      await result.current.markRead('notif-1')
    })

    expect(result.current.items).toEqual([])
    expect(result.current.unreadCount).toBe(0)
  })

  it('markAllRead marks all notifications as read and resets unreadCount to 0', async () => {
    const mockListRes: NotificationListResponse = {
      unreadCount: 2,
      items: [mockNotification1, { ...mockNotification2, isRead: false }],
    }
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce(mockListRes)
    vi.mocked(notificationApi.markAllNotificationsRead).mockResolvedValueOnce({ updated: 2 })

    const { result } = renderHook(() => useNotifications())

    await act(async () => {
      await result.current.reload()
    })

    await act(async () => {
      await result.current.markAllRead()
    })

    expect(notificationApi.markAllNotificationsRead).toHaveBeenCalled()
    expect(result.current.unreadCount).toBe(0)
    expect(result.current.items.every((i) => i.isRead)).toBe(true)
  })

  it('handles errors when reload fails', async () => {
    const errorObj = {
      response: {
        status: 403,
        data: { error: 'Không có quyền truy cập.' },
      },
    }
    vi.mocked(notificationApi.listNotifications).mockRejectedValueOnce(errorObj)

    const { result } = renderHook(() => useNotifications())

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.status).toBe('idle')
    expect(result.current.httpStatus).toBe(403)
    expect(result.current.error).toBe('Không có quyền truy cập.')
  })

  it('polls unread count periodically', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValue({
      unreadCount: 5,
      items: [],
    })

    renderHook(() => useNotifications())

    await act(async () => {
      vi.advanceTimersByTime(60000)
    })

    expect(notificationApi.listNotifications).toHaveBeenCalledWith({ take: 1 })
  })
})
