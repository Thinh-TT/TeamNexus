import { apiClient } from '../../../shared/api/apiClient'

export interface GitHubIntegrationResponse {
  workspaceId: string
  provider: string
  repoName: string | null
  hasPat: boolean
  maskedPat: string | null
  hasWebhookSecret: boolean
  isEnabled: boolean
  webhookUrl: string
  updatedAt: string
}

export interface UpdateGitHubIntegrationRequest {
  repoName?: string | null
  personalAccessToken?: string | null
  webhookSecret?: string | null
  isEnabled?: boolean
}

export interface TestGitHubConnectionResponse {
  success: boolean
  repoFullName?: string | null
  defaultBranch?: string | null
  errorMessage?: string | null
}

export const getGitHubIntegration = async (
  workspaceId: string
): Promise<GitHubIntegrationResponse> => {
  const res = await apiClient.get<GitHubIntegrationResponse>(
    `/workspaces/${workspaceId}/integrations/github`
  )
  return res.data
}

export const updateGitHubIntegration = async (
  workspaceId: string,
  req: UpdateGitHubIntegrationRequest
): Promise<GitHubIntegrationResponse> => {
  const res = await apiClient.put<GitHubIntegrationResponse>(
    `/workspaces/${workspaceId}/integrations/github`,
    req
  )
  return res.data
}

export const deleteGitHubIntegration = async (
  workspaceId: string
): Promise<void> => {
  await apiClient.delete(`/workspaces/${workspaceId}/integrations/github`)
}

export const testGitHubConnection = async (
  workspaceId: string
): Promise<TestGitHubConnectionResponse> => {
  const res = await apiClient.post<TestGitHubConnectionResponse>(
    `/workspaces/${workspaceId}/integrations/github/test`
  )
  return res.data
}

export const workspaceIntegrationsApi = {
  getGitHubIntegration,
  updateGitHubIntegration,
  deleteGitHubIntegration,
  testGitHubConnection,
}

export default workspaceIntegrationsApi
