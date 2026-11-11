import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { integrationApi } from '../integrationApi'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    patch: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('integrationApi service', () => {
  const wsId = 'ws-int-1'
  const endpointId = 'ep-1'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('listWebhooks: calls GET /workspaces/{id}/integrations/webhooks', async () => {
    const mockData = {
      stats: { sentLast24h: 3, failedLast24h: 1, pendingCount: 0 },
      endpoints: [],
    }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockData })

    const res = await integrationApi.listWebhooks(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/integrations/webhooks`)
    expect(res).toEqual(mockData)
  })

  it('createWebhook: calls POST /workspaces/{id}/integrations/webhooks', async () => {
    const payload = {
      name: '#alerts',
      provider: 'slack' as const,
      targetUrl: 'https://hooks.slack.com/services/T1/B1/secret',
      subscribedEvents: ['ObserverAlert'],
    }
    const mockCreated = { id: endpointId, ...payload }
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockCreated })

    const res = await integrationApi.createWebhook(wsId, payload)
    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/webhooks`,
      payload
    )
    expect(res).toEqual(mockCreated)
  })

  it('updateWebhook: calls PATCH /workspaces/{id}/integrations/webhooks/{endpointId}', async () => {
    const patchBody = { name: '#alerts-v2', isActive: true }
    const mockUpdated = { id: endpointId, ...patchBody }
    vi.mocked(httpClient.patch).mockResolvedValueOnce({ data: mockUpdated })

    const res = await integrationApi.updateWebhook(wsId, endpointId, patchBody)
    expect(httpClient.patch).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/webhooks/${endpointId}`,
      patchBody
    )
    expect(res).toEqual(mockUpdated)
  })

  it('deleteWebhook: calls DELETE /workspaces/{id}/integrations/webhooks/{endpointId}', async () => {
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: undefined })

    await integrationApi.deleteWebhook(wsId, endpointId)
    expect(httpClient.delete).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/webhooks/${endpointId}`
    )
  })

  it('testWebhook: calls POST /workspaces/{id}/integrations/webhooks/{endpointId}/test', async () => {
    const mockResult = { success: true, status: 'Sent', httpStatus: 200 }
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockResult })

    const res = await integrationApi.testWebhook(wsId, endpointId)
    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/webhooks/${endpointId}/test`
    )
    expect(res).toEqual(mockResult)
  })

  it('getWebhookDeliveries: calls GET /workspaces/{id}/integrations/webhooks/{endpointId}/deliveries', async () => {
    const mockDeliveries = [{ id: 'del-1', status: 'Sent' }]
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockDeliveries })

    const res = await integrationApi.getWebhookDeliveries(wsId, endpointId)
    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/webhooks/${endpointId}/deliveries`
    )
    expect(res).toEqual(mockDeliveries)
  })

  it('getGitHubIntegration: returns config when 200 OK', async () => {
    const mockConfig = { id: 'gh-1', workspaceId: wsId, secretHint: 'abcd' }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockConfig })

    const res = await integrationApi.getGitHubIntegration(wsId)
    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/integrations/github`)
    expect(res).toEqual(mockConfig)
  })

  it('getGitHubIntegration: returns null gracefully when backend responds 404 (not configured)', async () => {
    vi.mocked(httpClient.get).mockRejectedValueOnce({ response: { status: 404 } })

    const res = await integrationApi.getGitHubIntegration(wsId)
    expect(res).toBeNull()
  })

  it('getGitHubIntegration: rethrows non-404 errors', async () => {
    vi.mocked(httpClient.get).mockRejectedValueOnce({ response: { status: 500 } })

    await expect(integrationApi.getGitHubIntegration(wsId)).rejects.toEqual({
      response: { status: 500 },
    })
  })

  it('upsertGitHubIntegration, rotateGitHubSecret & deleteGitHubIntegration call exact endpoints', async () => {
    const upsertPayload = { isEnabled: true, autoMoveOnPr: true }
    vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { id: 'gh-1', ...upsertPayload } })
    await integrationApi.upsertGitHubIntegration(wsId, upsertPayload)
    expect(httpClient.put).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github`,
      upsertPayload
    )

    vi.mocked(httpClient.post).mockResolvedValueOnce({
      data: { secretHint: '9999', webhookSecretPlaintext: 'new-secret' },
    })
    const rotated = await integrationApi.rotateGitHubSecret(wsId)
    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github/rotate-secret`
    )
    expect(rotated.webhookSecretPlaintext).toBe('new-secret')

    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: undefined })
    await integrationApi.deleteGitHubIntegration(wsId)
    expect(httpClient.delete).toHaveBeenCalledWith(`/workspaces/${wsId}/integrations/github`)
  })

  it('getGitHubDeliveries: calls GET /workspaces/{id}/integrations/github/deliveries with take param', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [] })

    await integrationApi.getGitHubDeliveries(wsId, 30)
    expect(httpClient.get).toHaveBeenCalledWith(
      `/workspaces/${wsId}/integrations/github/deliveries`,
      { params: { take: 30 } }
    )
  })
})
