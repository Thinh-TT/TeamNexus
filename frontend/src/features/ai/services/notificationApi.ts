import { httpClient } from '../../../shared/api'
import type {
  NotificationListResponse,
  NotificationResponse,
} from '../types/notification.types'

export const notificationApi = {
  /**
   * GET /api/notifications?isRead=&take=
   */
  listNotifications: async (params?: {
    isRead?: boolean
    take?: number
  }): Promise<NotificationListResponse> => {
    const queryParams: Record<string, string | number> = {}
    if (typeof params?.isRead === 'boolean') {
      queryParams.isRead = params.isRead ? 'true' : 'false'
    }
    if (typeof params?.take === 'number') {
      queryParams.take = params.take
    }
    const res = await httpClient.get<NotificationListResponse>('/notifications', {
      params: queryParams,
    })
    return res.data
  },

  /**
   * POST /api/notifications/{id}/read
   */
  markNotificationRead: async (
    notificationId: string
  ): Promise<NotificationResponse> => {
    const res = await httpClient.post<NotificationResponse>(
      `/notifications/${notificationId}/read`
    )
    return res.data
  },

  /**
   * POST /api/notifications/read-all
   */
  markAllNotificationsRead: async (): Promise<{ updated: number }> => {
    const res = await httpClient.post<{ updated: number }>(
      '/notifications/read-all'
    )
    return res.data
  },
}
