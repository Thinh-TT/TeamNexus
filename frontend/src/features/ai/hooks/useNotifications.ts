import { useCallback, useEffect, useRef, useState } from 'react'
import type { AxiosError } from 'axios'
import { notificationApi } from '../services/notificationApi'
import type { NotificationResponse } from '../types/notification.types'

export interface UseNotificationsResult {
  items: NotificationResponse[]
  unreadCount: number
  status: 'idle' | 'loading' | 'marking'
  error: string | null
  httpStatus: number | null
  reload: (isRead?: boolean) => Promise<void>
  markRead: (notificationId: string) => Promise<void>
  markAllRead: () => Promise<void>
  refreshUnreadCount: () => Promise<void>
}

export function useNotifications(initialIsReadFilter?: boolean): UseNotificationsResult {
  const [items, setItems] = useState<NotificationResponse[]>([])
  const [unreadCount, setUnreadCount] = useState<number>(0)
  const [status, setStatus] = useState<'idle' | 'loading' | 'marking'>('idle')
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const currentFilterRef = useRef<boolean | undefined>(initialIsReadFilter)

  const extractError = (err: unknown) => {
    const axErr = err as AxiosError<{ error?: string }>
    const code = axErr.response?.status ?? null
    setHttpStatus(code)
    const msg =
      axErr.response?.data?.error ??
      axErr.message ??
      'Không thể xử lý yêu cầu thông báo.'
    setError(msg)
  }

  const reload = useCallback(async (isReadFilter?: boolean) => {
    currentFilterRef.current = isReadFilter
    setStatus('loading')
    setError(null)
    setHttpStatus(null)
    try {
      const res = await notificationApi.listNotifications({
        isRead: isReadFilter,
        take: 50,
      })
      setItems(res.items)
      setUnreadCount(res.unreadCount)
    } catch (err: unknown) {
      extractError(err)
    } finally {
      setStatus('idle')
    }
  }, [])

  const refreshUnreadCount = useCallback(async () => {
    try {
      const res = await notificationApi.listNotifications({ take: 1 })
      setUnreadCount(res.unreadCount)
    } catch {
      // Background poll failure is non-fatal
    }
  }, [])

  const markRead = useCallback(async (notificationId: string) => {
    setStatus('marking')
    setError(null)
    setHttpStatus(null)
    try {
      const updated = await notificationApi.markNotificationRead(notificationId)
      setItems((prev) => {
        if (currentFilterRef.current === false) {
          // If filtering unread only, remove it
          return prev.filter((item) => item.id !== notificationId)
        }
        return prev.map((item) => (item.id === notificationId ? updated : item))
      })
      setUnreadCount((prev) => Math.max(0, prev - 1))
    } catch (err: unknown) {
      extractError(err)
    } finally {
      setStatus('idle')
    }
  }, [])

  const markAllRead = useCallback(async () => {
    setStatus('marking')
    setError(null)
    setHttpStatus(null)
    try {
      await notificationApi.markAllNotificationsRead()
      const nowIso = new Date().toISOString()
      setItems((prev) => {
        if (currentFilterRef.current === false) {
          return []
        }
        return prev.map((item) => ({
          ...item,
          isRead: true,
          readAt: item.readAt ?? nowIso,
        }))
      })
      setUnreadCount(0)
    } catch (err: unknown) {
      extractError(err)
    } finally {
      setStatus('idle')
    }
  }, [])

  // Poll unread count every 60s when tab is visible
  useEffect(() => {
    let intervalId: ReturnType<typeof setInterval> | null = null

    const startPolling = () => {
      if (intervalId) return
      intervalId = setInterval(() => {
        if (typeof document !== 'undefined' && document.visibilityState === 'visible') {
          refreshUnreadCount()
        }
      }, 60000)
    }

    const stopPolling = () => {
      if (intervalId) {
        clearInterval(intervalId)
        intervalId = null
      }
    }

    const handleVisibilityChange = () => {
      if (typeof document !== 'undefined' && document.visibilityState === 'visible') {
        refreshUnreadCount()
        startPolling()
      } else {
        stopPolling()
      }
    }

    if (typeof document !== 'undefined') {
      document.addEventListener('visibilitychange', handleVisibilityChange)
      if (document.visibilityState === 'visible') {
        startPolling()
      }
    }

    return () => {
      stopPolling()
      if (typeof document !== 'undefined') {
        document.removeEventListener('visibilitychange', handleVisibilityChange)
      }
    }
  }, [refreshUnreadCount])

  return {
    items,
    unreadCount,
    status,
    error,
    httpStatus,
    reload,
    markRead,
    markAllRead,
    refreshUnreadCount,
  }
}
