import { beforeEach, describe, expect, it, vi } from 'vitest'
import { apiClient } from '../../../../shared/api/apiClient'
import {
  deleteGitHubIntegration,
  getGitHubIntegration,
  testGitHubConnection,
  updateGitHubIntegration,
  workspaceIntegrationsApi,
} from '../workspaceIntegrationsApi'

vi.mock('../../../../shared/api/apiClient', () => ({
  apiClient: {
    get: vi.fn(),
    put: vi.fn(),
    post: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('workspaceIntegrationsApi', () => {
  const wsId = 'ws-test-123'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getGitHubIntegration: calls GET /workspaces/{workspaceId}/integrations/github', async () => {
    const mockResponse = {
      workspaceId: wsId,
      provider: 'github',
      repoName: 'Thinh-TT/TeamNexus',
      hasPat: true,
      maskedPat: 'ghp_••••••••3944',
      hasWebhookSecret: true,
      isEnabled: true,
      webhookUrl: 'https://api.teamnexus.cloud/api/webhooks/github',
      updatedAt: '2026-11-10T08:00:00Z',
    }
    vi.mocked(apiClient.get).mockResolvedValueOnce({ data: mockResponse })

    const result = await getGitHubIntegration(wsId)
    expect(apiClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github`
    )
    expect(result).toEqual(mockResponse)
  })

  it('updateGitHubIntegration: calls PUT /workspaces/{workspaceId}/integrations/github with payload', async () => {
    const payload = {
      repoName: 'Thinh-TT/TeamNexus',
      personalAccessToken: 'ghp_newtoken123456',
      webhookSecret: 'secret_abc123',
      isEnabled: true,
    }
    const mockResponse = {
      workspaceId: wsId,
      provider: 'github',
      repoName: 'Thinh-TT/TeamNexus',
      hasPat: true,
      maskedPat: 'ghp_••••••••3456',
      hasWebhookSecret: true,
      isEnabled: true,
      webhookUrl: 'https://api.teamnexus.cloud/api/webhooks/github',
      updatedAt: '2026-11-10T08:30:00Z',
    }
    vi.mocked(apiClient.put).mockResolvedValueOnce({ data: mockResponse })

    const result = await updateGitHubIntegration(wsId, payload)
    expect(apiClient.put).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github`,
      payload
    )
    expect(result).toEqual(mockResponse)
  })

  it('deleteGitHubIntegration: calls DELETE /workspaces/{workspaceId}/integrations/github', async () => {
    vi.mocked(apiClient.delete).mockResolvedValueOnce({ data: undefined })

    await deleteGitHubIntegration(wsId)
    expect(apiClient.delete).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github`
    )
  })

  it('testGitHubConnection: calls POST /workspaces/{workspaceId}/integrations/github/test', async () => {
    const mockResponse = {
      success: true,
      repoFullName: 'Thinh-TT/TeamNexus',
      defaultBranch: 'main',
    }
    vi.mocked(apiClient.post).mockResolvedValueOnce({ data: mockResponse })

    const result = await testGitHubConnection(wsId)
    expect(apiClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github/test`
    )
    expect(result).toEqual(mockResponse)
  })

  it('workspaceIntegrationsApi object exports all methods', () => {
    expect(workspaceIntegrationsApi.getGitHubIntegration).toBe(getGitHubIntegration)
    expect(workspaceIntegrationsApi.updateGitHubIntegration).toBe(updateGitHubIntegration)
    expect(workspaceIntegrationsApi.deleteGitHubIntegration).toBe(deleteGitHubIntegration)
    expect(workspaceIntegrationsApi.testGitHubConnection).toBe(testGitHubConnection)
  })
})
