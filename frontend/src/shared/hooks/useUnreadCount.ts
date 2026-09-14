import { useCallback, useEffect, useState } from 'react'
import { notificationApi } from '../../features/ai/services/notificationApi'

export const useUnreadCount = (pollIntervalMs = 60_000) => {
  const [unreadCount, setUnreadCount] = useState<number>(0)
  const [loading, setLoading] = useState<boolean>(false)

  const fetchUnreadCount = useCallback(async () => {
    try {
      setLoading(true)
      const res = await notificationApi.listNotifications({ take: 1 })
      setUnreadCount(typeof res.unreadCount === 'number' ? res.unreadCount : 0)
    } catch {
      // Fail-soft: return 0 on network/API errors, do not throw
      setUnreadCount(0)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    Promise.resolve().then(() => {
      fetchUnreadCount()
    })

    if (pollIntervalMs > 0) {
      const timer = setInterval(fetchUnreadCount, pollIntervalMs)
      return () => clearInterval(timer)
    }
  }, [fetchUnreadCount, pollIntervalMs])

  return {
    unreadCount,
    loading,
    refresh: fetchUnreadCount,
  }
}
