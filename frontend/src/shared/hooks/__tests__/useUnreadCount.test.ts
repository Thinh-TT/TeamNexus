import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useUnreadCount } from '../useUnreadCount'
import { notificationApi } from '../../../features/ai/services/notificationApi'

vi.mock('../../../features/ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn(),
  },
}))

describe('useUnreadCount', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls notificationApi.listNotifications on mount with take: 1', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce({
      unreadCount: 5,
      items: [],
    })

    const { result } = renderHook(() => useUnreadCount(0))

    await waitFor(() => {
      expect(notificationApi.listNotifications).toHaveBeenCalledWith({ take: 1 })
    })

    expect(result.current.unreadCount).toBe(5)
  })

  it('returns unreadCount when API responds with unread count', async () => {
    vi.mocked(notificationApi.listNotifications).mockResolvedValueOnce({
      unreadCount: 12,
      items: [],
    })

    const { result } = renderHook(() => useUnreadCount(0))

    await waitFor(() => {
      expect(result.current.unreadCount).toBe(12)
    })
  })

  it('handles API errors gracefully and falls back to 0 without throwing', async () => {
    vi.mocked(notificationApi.listNotifications).mockRejectedValueOnce(
      new Error('Network error')
    )

    const { result } = renderHook(() => useUnreadCount(0))

    await waitFor(() => {
      expect(result.current.loading).toBe(false)
    })

    expect(result.current.unreadCount).toBe(0)
  })

  it('re-fetches notifications unread count when calling refresh', async () => {
    vi.mocked(notificationApi.listNotifications)
      .mockResolvedValueOnce({ unreadCount: 2, items: [] })
      .mockResolvedValueOnce({ unreadCount: 7, items: [] })

    const { result } = renderHook(() => useUnreadCount(0))

    await waitFor(() => {
      expect(result.current.unreadCount).toBe(2)
    })

    await act(async () => {
      await result.current.refresh()
    })

    expect(result.current.unreadCount).toBe(7)
    expect(notificationApi.listNotifications).toHaveBeenCalledTimes(2)
  })
})
