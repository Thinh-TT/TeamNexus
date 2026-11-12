import { useCallback, useEffect, useState } from 'react'
import { message } from 'antd'
import { boardApi } from '../../board/services/boardApi'
import type { BoardResponse } from '../../board/types/board.types'
import { integrationApi } from '../services/integrationApi'
import type {
  CreateWorkspaceWebhookRequest,
  GitHubWebhookDeliveryResponse,
  UpdateWorkspaceWebhookRequest,
  UpsertWorkspaceGitHubIntegrationRequest,
  WebhookDeliveryItemResponse,
  WebhookTestResultResponse,
  WorkspaceGitHubIntegrationResponse,
  WorkspaceWebhookEndpointResponse,
  WorkspaceWebhookStatsResponse,
} from '../types/integration.types'

function extractApiError(err: unknown, fallback: string): string {
  const resData = (err as { response?: { data?: { error?: string; message?: string } } })
    ?.response?.data
  return resData?.error || resData?.message || fallback
}

const EMPTY_STATS: WorkspaceWebhookStatsResponse = {
  sentLast24h: 0,
  failedLast24h: 0,
  pendingCount: 0,
}

export function useWorkspaceIntegrations(workspaceId?: string, enabled = true) {
  const [webhooks, setWebhooks] = useState<WorkspaceWebhookEndpointResponse[]>([])
  const [webhookStats, setWebhookStats] = useState<WorkspaceWebhookStatsResponse>(EMPTY_STATS)
  const [githubIntegration, setGithubIntegration] =
    useState<WorkspaceGitHubIntegrationResponse | null>(null)
  const [githubDeliveries, setGithubDeliveries] = useState<GitHubWebhookDeliveryResponse[]>([])
  const [boards, setBoards] = useState<BoardResponse[]>([])
  const [oneTimeSecret, setOneTimeSecret] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    if (!workspaceId || !enabled) return
    setLoading(true)
    setError(null)
    try {
      const [webhookData, ghData, boardList] = await Promise.all([
        integrationApi.listWebhooks(workspaceId),
        integrationApi.getGitHubIntegration(workspaceId),
        boardApi.getBoards(workspaceId).catch(() => [] as BoardResponse[]),
      ])
      setWebhooks(webhookData.endpoints)
      setWebhookStats(webhookData.stats)
      setGithubIntegration(ghData)
      setBoards(boardList)

      if (ghData) {
        const deliveries = await integrationApi.getGitHubDeliveries(workspaceId, 30).catch(() => [])
        setGithubDeliveries(deliveries)
      } else {
        setGithubDeliveries([])
      }
    } catch (err: unknown) {
      const msg = extractApiError(err, 'Không thể tải cấu hình tích hợp của không gian làm việc.')
      setError(msg)
    } finally {
      setLoading(false)
    }
  }, [workspaceId, enabled])

  useEffect(() => {
    if (!workspaceId || !enabled) return
    let ignore = false
    Promise.resolve().then(() => {
      if (!ignore) {
        void reload()
      }
    })
    return () => {
      ignore = true
    }
  }, [workspaceId, enabled, reload])

  const reloadWebhooks = useCallback(async () => {
    if (!workspaceId) return
    const data = await integrationApi.listWebhooks(workspaceId)
    setWebhooks(data.endpoints)
    setWebhookStats(data.stats)
  }, [workspaceId])

  const createWebhook = async (req: CreateWorkspaceWebhookRequest): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await integrationApi.createWebhook(workspaceId, req)
      message.success('Đã thêm Webhook thành công.')
      await reloadWebhooks()
      return true
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể tạo Webhook.'))
      return false
    }
  }

  const updateWebhook = async (
    endpointId: string,
    req: UpdateWorkspaceWebhookRequest
  ): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await integrationApi.updateWebhook(workspaceId, endpointId, req)
      message.success('Đã cập nhật cấu hình Webhook.')
      await reloadWebhooks()
      return true
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể cập nhật Webhook.'))
      return false
    }
  }

  const deleteWebhook = async (endpointId: string): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await integrationApi.deleteWebhook(workspaceId, endpointId)
      message.success('Đã xoá Webhook.')
      await reloadWebhooks()
      return true
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể xoá Webhook.'))
      return false
    }
  }

  const testWebhook = async (endpointId: string): Promise<WebhookTestResultResponse | null> => {
    if (!workspaceId) return null
    try {
      const result = await integrationApi.testWebhook(workspaceId, endpointId)
      if (result.success) {
        message.success(
          `Gửi thử thành công${result.httpStatus ? ` (HTTP ${result.httpStatus})` : ''}.`
        )
      } else {
        message.error(result.error || 'Gửi thử thất bại.')
      }
      await reloadWebhooks()
      return result
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể gửi thử Webhook.'))
      return null
    }
  }

  const fetchWebhookDeliveries = async (
    endpointId: string
  ): Promise<WebhookDeliveryItemResponse[]> => {
    if (!workspaceId) return []
    try {
      return await integrationApi.getWebhookDeliveries(workspaceId, endpointId)
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể tải lịch sử gửi Outbox.'))
      return []
    }
  }

  const saveGitHubIntegration = async (
    req: UpsertWorkspaceGitHubIntegrationRequest
  ): Promise<WorkspaceGitHubIntegrationResponse | null> => {
    if (!workspaceId) return null
    try {
      const saved = await integrationApi.upsertGitHubIntegration(workspaceId, req)
      // The plaintext secret is a one-time value. Keep it only in `oneTimeSecret` (the alert the user
      // must copy); never retain it in the long-lived configuration state or return it to callers.
      const { webhookSecretPlaintext, ...safeConfig } = saved
      setGithubIntegration(safeConfig)
      if (webhookSecretPlaintext) {
        setOneTimeSecret(webhookSecretPlaintext)
      }
      const deliveries = await integrationApi.getGitHubDeliveries(workspaceId, 30).catch(() => [])
      setGithubDeliveries(deliveries)
      message.success('Đã lưu cấu hình tích hợp GitHub.')
      return safeConfig
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể lưu cấu hình GitHub.'))
      return null
    }
  }

  const rotateGitHubSecret = async (): Promise<string | null> => {
    if (!workspaceId) return null
    try {
      const res = await integrationApi.rotateGitHubSecret(workspaceId)
      setGithubIntegration((prev) => (prev ? { ...prev, secretHint: res.secretHint } : prev))
      setOneTimeSecret(res.webhookSecretPlaintext)
      message.success('Đã xoay vòng Webhook Secret mới.')
      return res.webhookSecretPlaintext
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể xoay vòng Webhook Secret.'))
      return null
    }
  }

  const deleteGitHubIntegration = async (): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await integrationApi.deleteGitHubIntegration(workspaceId)
      setGithubIntegration(null)
      setGithubDeliveries([])
      setOneTimeSecret(null)
      message.success('Đã xoá cấu hình tích hợp GitHub.')
      return true
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể xoá cấu hình GitHub.'))
      return false
    }
  }

  const refreshGitHubDeliveries = async (): Promise<void> => {
    if (!workspaceId || !githubIntegration) return
    try {
      const deliveries = await integrationApi.getGitHubDeliveries(workspaceId, 30)
      setGithubDeliveries(deliveries)
    } catch (err: unknown) {
      message.error(extractApiError(err, 'Không thể tải nhật ký GitHub Webhook.'))
    }
  }

  const dismissOneTimeSecret = () => {
    setOneTimeSecret(null)
  }

  return {
    webhooks,
    webhookStats,
    githubIntegration,
    githubDeliveries,
    boards,
    oneTimeSecret,
    loading,
    error,
    reload,
    createWebhook,
    updateWebhook,
    deleteWebhook,
    testWebhook,
    fetchWebhookDeliveries,
    saveGitHubIntegration,
    rotateGitHubSecret,
    deleteGitHubIntegration,
    refreshGitHubDeliveries,
    dismissOneTimeSecret,
  }
}
