import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { boardApi } from '../../../board/services/boardApi'
import { integrationApi } from '../../services/integrationApi'
import type { WorkspaceGitHubIntegrationResponse } from '../../types/integration.types'
import { useWorkspaceIntegrations } from '../useWorkspaceIntegrations'

vi.mock('../../services/integrationApi', () => ({
  integrationApi: {
    listWebhooks: vi.fn(),
    getGitHubIntegration: vi.fn(),
    getGitHubDeliveries: vi.fn(),
    upsertGitHubIntegration: vi.fn(),
  },
}))

vi.mock('../../../board/services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn(),
  },
}))

describe('useWorkspaceIntegrations', () => {
  const config: WorkspaceGitHubIntegrationResponse = {
    id: 'gh-1',
    workspaceId: 'ws-1',
    webhookToken: '11111111-2222-3333-4444-555555555555',
    webhookPayloadUrl: 'https://api.teamnexus.cloud/api/webhooks/github/11111111-2222-3333-4444-555555555555',
    secretHint: 'ef12',
    isEnabled: true,
    defaultBoardId: 'b-1',
    issueLabelFilter: null,
    autoMoveOnPr: true,
    autoCreateTasksFromIssues: true,
    syncIssueComments: true,
    createdAt: '2026-11-01T00:00:00Z',
    updatedAt: '2026-11-10T00:00:00Z',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(integrationApi.listWebhooks).mockResolvedValue({
      stats: { sentLast24h: 0, failedLast24h: 0, pendingCount: 0 },
      endpoints: [],
    })
    vi.mocked(integrationApi.getGitHubIntegration).mockResolvedValue(null)
    vi.mocked(integrationApi.getGitHubDeliveries).mockResolvedValue([])
    vi.mocked(boardApi.getBoards).mockResolvedValue([])
  })

  it('keeps the one-time webhook secret out of the long-lived GitHub config state', async () => {
    const plaintext = 'whsec_one_time_plaintext_secret_999'
    vi.mocked(integrationApi.upsertGitHubIntegration).mockResolvedValueOnce({
      ...config,
      webhookSecretPlaintext: plaintext,
    })

    const { result } = renderHook(() => useWorkspaceIntegrations('ws-1'))

    await waitFor(() => {
      expect(integrationApi.getGitHubIntegration).toHaveBeenCalledWith('ws-1')
    })

    let saved: WorkspaceGitHubIntegrationResponse | null = null
    await act(async () => {
      saved = await result.current.saveGitHubIntegration({ isEnabled: true })
    })

    expect(result.current.githubIntegration).not.toHaveProperty('webhookSecretPlaintext')
    expect(result.current.oneTimeSecret).toBe(plaintext)
    expect(saved).not.toHaveProperty('webhookSecretPlaintext')
  })
})
