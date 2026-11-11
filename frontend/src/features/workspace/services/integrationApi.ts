import axios from 'axios'
import { httpClient } from '../../../shared/api/httpClient'
import type {
  CreateWorkspaceWebhookRequest,
  GitHubWebhookDeliveryResponse,
  RotateGitHubSecretResponse,
  UpdateWorkspaceWebhookRequest,
  UpsertWorkspaceGitHubIntegrationRequest,
  WebhookDeliveryItemResponse,
  WebhookTestResultResponse,
  WorkspaceGitHubIntegrationResponse,
  WorkspaceWebhookEndpointResponse,
  WorkspaceWebhookListResponse,
} from '../types/integration.types'

/**
 * API client for Phase 15 Workspace External Integrations (Slack/Discord Outbound Webhooks & GitHub Inbound Webhooks).
 */
export const integrationApi = {
  // ---- Outbound Webhooks (Slack / Discord) ----

  listWebhooks: async (workspaceId: string): Promise<WorkspaceWebhookListResponse> => {
    const res = await httpClient.get<WorkspaceWebhookListResponse>(
      `/workspaces/${workspaceId}/integrations/webhooks`
    )
    return res.data
  },

  createWebhook: async (
    workspaceId: string,
    data: CreateWorkspaceWebhookRequest
  ): Promise<WorkspaceWebhookEndpointResponse> => {
    const res = await httpClient.post<WorkspaceWebhookEndpointResponse>(
      `/workspaces/${workspaceId}/integrations/webhooks`,
      data
    )
    return res.data
  },

  updateWebhook: async (
    workspaceId: string,
    endpointId: string,
    data: UpdateWorkspaceWebhookRequest
  ): Promise<WorkspaceWebhookEndpointResponse> => {
    const res = await httpClient.patch<WorkspaceWebhookEndpointResponse>(
      `/workspaces/${workspaceId}/integrations/webhooks/${endpointId}`,
      data
    )
    return res.data
  },

  deleteWebhook: async (workspaceId: string, endpointId: string): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}/integrations/webhooks/${endpointId}`)
  },

  testWebhook: async (
    workspaceId: string,
    endpointId: string
  ): Promise<WebhookTestResultResponse> => {
    const res = await httpClient.post<WebhookTestResultResponse>(
      `/workspaces/${workspaceId}/integrations/webhooks/${endpointId}/test`
    )
    return res.data
  },

  getWebhookDeliveries: async (
    workspaceId: string,
    endpointId: string
  ): Promise<WebhookDeliveryItemResponse[]> => {
    const res = await httpClient.get<WebhookDeliveryItemResponse[]>(
      `/workspaces/${workspaceId}/integrations/webhooks/${endpointId}/deliveries`
    )
    return res.data
  },

  // ---- Inbound GitHub Integration ----

  getGitHubIntegration: async (
    workspaceId: string
  ): Promise<WorkspaceGitHubIntegrationResponse | null> => {
    try {
      const res = await httpClient.get<WorkspaceGitHubIntegrationResponse>(
        `/workspaces/${workspaceId}/integrations/github`
      )
      return res.data
    } catch (err: unknown) {
      const status = axios.isAxiosError(err)
        ? err.response?.status
        : (err as { response?: { status?: number } })?.response?.status
      if (status === 404) {
        return null
      }
      throw err
    }
  },

  upsertGitHubIntegration: async (
    workspaceId: string,
    data: UpsertWorkspaceGitHubIntegrationRequest
  ): Promise<WorkspaceGitHubIntegrationResponse> => {
    const res = await httpClient.put<WorkspaceGitHubIntegrationResponse>(
      `/workspaces/${workspaceId}/integrations/github`,
      data
    )
    return res.data
  },

  rotateGitHubSecret: async (workspaceId: string): Promise<RotateGitHubSecretResponse> => {
    const res = await httpClient.post<RotateGitHubSecretResponse>(
      `/workspaces/${workspaceId}/integrations/github/rotate-secret`
    )
    return res.data
  },

  deleteGitHubIntegration: async (workspaceId: string): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}/integrations/github`)
  },

  getGitHubDeliveries: async (
    workspaceId: string,
    take = 30
  ): Promise<GitHubWebhookDeliveryResponse[]> => {
    const res = await httpClient.get<GitHubWebhookDeliveryResponse[]>(
      `/workspaces/${workspaceId}/integrations/github/deliveries`,
      { params: { take } }
    )
    return res.data
  },
}
