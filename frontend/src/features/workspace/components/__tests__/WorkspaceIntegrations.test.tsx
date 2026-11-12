import React from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { boardApi } from '../../../board/services/boardApi'
import { WorkspaceSettingsPage } from '../../pages/WorkspaceSettingsPage'
import { integrationApi } from '../../services/integrationApi'
import type {
  GitHubWebhookDeliveryResponse,
  WorkspaceGitHubIntegrationResponse,
  WorkspaceWebhookEndpointResponse,
  WorkspaceWebhookListResponse,
} from '../../types/integration.types'

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: () => <div data-testid="app-header-mock" />,
}))

vi.mock('../../../../shared/hooks/useWorkspaceRole', () => ({
  useWorkspaceRole: () => ({
    isManagerOrAdmin: true,
    isAdmin: true,
    isOwner: true,
    loading: false,
  }),
}))

vi.mock('../../hooks/useWorkspaceDetail', () => ({
  useWorkspaceDetail: () => ({
    detail: {
      id: 'ws-1',
      name: 'Không Gian Chính',
      description: 'Workspace phục vụ kiểm thử tích hợp',
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-10T00:00:00Z',
      ownerId: 'u-1',
      ownerDisplayName: 'Trần Trường Thịnh',
      memberCount: 5,
      boardCount: 2,
      currentUserRole: 'Admin',
    },
    loading: false,
    save: vi.fn().mockResolvedValue(true),
    transfer: vi.fn().mockResolvedValue(true),
    remove: vi.fn().mockResolvedValue(true),
  }),
}))

vi.mock('../../../board/hooks/useWorkspaceMembers', () => ({
  useWorkspaceMembers: () => ({
    members: [],
    loading: false,
  }),
}))

vi.mock('../../../board/services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn(),
  },
}))

vi.mock('../../services/integrationApi', () => ({
  integrationApi: {
    listWebhooks: vi.fn(),
    createWebhook: vi.fn(),
    updateWebhook: vi.fn(),
    deleteWebhook: vi.fn(),
    testWebhook: vi.fn(),
    getWebhookDeliveries: vi.fn(),
    getGitHubIntegration: vi.fn(),
    upsertGitHubIntegration: vi.fn(),
    rotateGitHubSecret: vi.fn(),
    deleteGitHubIntegration: vi.fn(),
    getGitHubDeliveries: vi.fn(),
  },
}))

describe('WorkspaceSettingsPage & Integrations Tab', () => {
  const mockWebhook: WorkspaceWebhookEndpointResponse = {
    id: 'ep-1',
    workspaceId: 'ws-1',
    name: '#alerts-slack',
    provider: 'slack',
    maskedTargetUrl: 'https://hooks.slack.com/services/T000/B000/****abcd',
    subscribedEvents: ['ObserverAlert', 'AgentActionPending'],
    subscribedEventsCsv: 'ObserverAlert,AgentActionPending',
    isActive: true,
    failureCount: 0,
    lastSuccessAt: '2026-11-10T08:00:00Z',
    lastFailureAt: null,
    lastError: null,
    createdByUserId: 'u-1',
    createdAt: '2026-11-01T00:00:00Z',
    updatedAt: '2026-11-10T00:00:00Z',
  }

  const mockWebhookList: WorkspaceWebhookListResponse = {
    stats: { sentLast24h: 14, failedLast24h: 1, pendingCount: 2 },
    endpoints: [mockWebhook],
  }

  const mockGitHubConfig: WorkspaceGitHubIntegrationResponse = {
    id: 'gh-1',
    workspaceId: 'ws-1',
    webhookToken: '11111111-2222-3333-4444-555555555555',
    webhookPayloadUrl:
      'https://api.teamnexus.cloud/api/webhooks/github/11111111-2222-3333-4444-555555555555',
    secretHint: 'ef12',
    isEnabled: true,
    defaultBoardId: 'b-1',
    issueLabelFilter: 'teamnexus, bug',
    autoMoveOnPr: true,
    autoCreateTasksFromIssues: true,
    syncIssueComments: true,
    createdAt: '2026-11-01T00:00:00Z',
    updatedAt: '2026-11-10T00:00:00Z',
    webhookSecretPlaintext: null,
  }

  const mockDeliveries: GitHubWebhookDeliveryResponse[] = [
    {
      id: 'gh-del-1',
      deliveryId: 'delivery-abcdef123456',
      eventName: 'pull_request',
      action: 'closed',
      repositoryFullName: 'octocat/TeamNexus',
      status: 'Processed',
      summary: 'Chuyển TN-42 sang Hoàn thành',
      errorMessage: null,
      receivedAt: '2026-11-11T09:30:00Z',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(boardApi.getBoards).mockResolvedValue([
      {
        id: 'b-1',
        workspaceId: 'ws-1',
        name: 'Sprint 1',
        description: null,
        createdAt: '',
        updatedAt: '',
        columns: [],
      },
      {
        id: 'b-2',
        workspaceId: 'ws-1',
        name: 'Backlog',
        description: null,
        createdAt: '',
        updatedAt: '',
        columns: [],
      },
    ])
    vi.mocked(integrationApi.listWebhooks).mockResolvedValue(mockWebhookList)
    vi.mocked(integrationApi.getGitHubIntegration).mockResolvedValue(mockGitHubConfig)
    vi.mocked(integrationApi.getGitHubDeliveries).mockResolvedValue(mockDeliveries)
  })

  const renderPage = () => {
    return render(
      <MemoryRouter initialEntries={['/workspaces/ws-1/settings']}>
        <Routes>
          <Route path="/workspaces/:workspaceId/settings" element={<WorkspaceSettingsPage />} />
        </Routes>
      </MemoryRouter>
    )
  }

  const switchToIntegrationsTab = async () => {
    const tab = await screen.findByTestId('workspace-integrations-tab')
    fireEvent.click(tab)
    await waitFor(() => {
      expect(screen.getByTestId('outbound-webhooks-section')).toBeInTheDocument()
      expect(screen.getByTestId('github-integration-section')).toBeInTheDocument()
    })
  }

  const selectDefaultBoard = async (boardName: string) => {
    const select = screen.getByTestId('github-default-board-select')
    fireEvent.mouseDown(select.querySelector('.ant-select-selector') ?? select)
    const option = await screen.findByTitle(boardName)
    fireEvent.click(option)
  }

  it('defaults to "Cài đặt chung" tab and preserves open-settings-modal-btn', async () => {
    renderPage()

    expect(screen.getByTestId('open-settings-modal-btn')).toBeInTheDocument()
    expect(screen.getByText('Thông tin chung')).toBeInTheDocument()
    expect(screen.getByText('Không Gian Chính')).toBeInTheDocument()

    // Phase 15 remediation: integrations are lazy-loaded only when the tab is opened.
    expect(integrationApi.listWebhooks).not.toHaveBeenCalled()
    expect(integrationApi.getGitHubIntegration).not.toHaveBeenCalled()
  })

  it('loads integrations only after switching to the Tích hợp tab', async () => {
    renderPage()

    expect(integrationApi.listWebhooks).not.toHaveBeenCalled()

    await switchToIntegrationsTab()

    await waitFor(() => {
      expect(integrationApi.listWebhooks).toHaveBeenCalledWith('ws-1')
      expect(integrationApi.getGitHubIntegration).toHaveBeenCalledWith('ws-1')
    })
  })

  it('renders 24h webhook stats, masked URL, and Vietnamese subscribed event tags on Integrations tab', async () => {
    renderPage()
    await switchToIntegrationsTab()

    await waitFor(() => {
      expect(screen.getByText('14')).toBeInTheDocument()
      expect(screen.getByText('#alerts-slack')).toBeInTheDocument()
      expect(
        screen.getByText('https://hooks.slack.com/services/T000/B000/****abcd')
      ).toBeInTheDocument()
      expect(screen.getByText('Cảnh báo rủi ro AI Observer')).toBeInTheDocument()
      expect(screen.getByText('Đề xuất / Cảnh báo AI Agent')).toBeInTheDocument()
    })
  })

  it('creates a Discord webhook via modal and reloads webhook list', async () => {
    vi.mocked(integrationApi.createWebhook).mockResolvedValueOnce({
      ...mockWebhook,
      id: 'ep-2',
      provider: 'discord',
      name: 'Discord Ops',
    })

    renderPage()
    await switchToIntegrationsTab()

    fireEvent.click(screen.getByTestId('add-webhook-btn'))

    const modal = await screen.findByRole('dialog')
    fireEvent.click(within(modal).getByText('Discord'))

    fireEvent.change(within(modal).getByPlaceholderText(/#team-alerts/i), {
      target: { value: 'Discord Ops' },
    })
    fireEvent.change(within(modal).getByPlaceholderText(/hooks\.slack\.com/i), {
      target: { value: 'https://discord.com/api/webhooks/123456/abcdef' },
    })

    fireEvent.click(within(modal).getByRole('button', { name: 'Tạo Webhook' }))

    await waitFor(() => {
      expect(integrationApi.createWebhook).toHaveBeenCalledWith('ws-1', {
        provider: 'discord',
        name: 'Discord Ops',
        targetUrl: 'https://discord.com/api/webhooks/123456/abcdef',
        subscribedEvents: ['ObserverAlert', 'AgentActionPending', 'ClarificationNeeded'],
      })
    })
  })

  it('triggers testWebhook and opens outbox deliveries drawer', async () => {
    vi.mocked(integrationApi.testWebhook).mockResolvedValueOnce({
      success: true,
      deliveryId: 'del-test-1',
      status: 'Sent',
      httpStatus: 204,
      error: null,
      testedAt: '2026-11-11T10:00:00Z',
    })
    vi.mocked(integrationApi.getWebhookDeliveries).mockResolvedValueOnce([
      {
        id: 'out-1',
        endpointId: 'ep-1',
        eventType: 'ObserverAlert',
        status: 'Sent',
        attemptCount: 1,
        maxAttempts: 5,
        lastHttpStatus: 200,
        lastError: null,
        nextAttemptAt: '2026-11-11T09:00:00Z',
        sentAt: '2026-11-11T09:00:01Z',
        createdAt: '2026-11-11T09:00:00Z',
      },
    ])

    renderPage()
    await switchToIntegrationsTab()

    const testBtn = await screen.findByTestId('test-webhook-btn-ep-1')
    fireEvent.click(testBtn)

    await waitFor(() => {
      expect(integrationApi.testWebhook).toHaveBeenCalledWith('ws-1', 'ep-1')
    })

    fireEvent.click(screen.getByTestId('webhook-history-btn-ep-1'))

    await waitFor(() => {
      expect(integrationApi.getWebhookDeliveries).toHaveBeenCalledWith('ws-1', 'ep-1')
      expect(screen.getByText('Lịch sử Outbox: #alerts-slack')).toBeInTheDocument()
      expect(screen.getByText('200 · 1/5')).toBeInTheDocument()
    })
  })

  it('displays one-time webhookSecretPlaintext when enabling GitHub integration for the first time', async () => {
    vi.mocked(integrationApi.getGitHubIntegration).mockResolvedValueOnce(null)
    vi.mocked(integrationApi.upsertGitHubIntegration).mockResolvedValueOnce({
      ...mockGitHubConfig,
      webhookSecretPlaintext: 'whsec_one_time_plaintext_secret_999',
    })

    renderPage()
    await switchToIntegrationsTab()

    expect(
      await screen.findByText('Chưa kích hoạt tích hợp GitHub cho không gian làm việc này')
    ).toBeInTheDocument()
    expect(screen.queryByTestId('github-oneshot-secret-alert')).not.toBeInTheDocument()

    // Review 15.5 F3: a brand-new integration no longer preselects the first board, so the board
    // must be chosen explicitly before "Kích hoạt" can save an auto-create configuration.
    await selectDefaultBoard('Sprint 1')

    fireEvent.click(screen.getByTestId('enable-github-btn'))

    await waitFor(() => {
      expect(integrationApi.upsertGitHubIntegration).toHaveBeenCalledWith(
        'ws-1',
        expect.objectContaining({
          isEnabled: true,
          autoMoveOnPr: true,
          autoCreateTasksFromIssues: true,
          syncIssueComments: true,
        })
      )
      expect(screen.getByTestId('github-oneshot-secret-alert')).toBeInTheDocument()
      expect(screen.getByTestId('github-oneshot-secret-value')).toHaveValue(
        'whsec_one_time_plaintext_secret_999'
      )
    })
  })

  it('does not show one-time secret alert on initial GET, and reveals secret once on Rotate Secret', async () => {
    vi.mocked(integrationApi.rotateGitHubSecret).mockResolvedValueOnce({
      secretHint: '9876',
      webhookSecretPlaintext: 'rotated_secret_xyz_9876',
    })

    renderPage()
    await switchToIntegrationsTab()

    await waitFor(() => {
      expect(screen.getByTestId('github-payload-url-input')).toHaveValue(
        mockGitHubConfig.webhookPayloadUrl
      )
      expect(screen.getByText('****ef12')).toBeInTheDocument()
    })
    expect(screen.queryByTestId('github-oneshot-secret-alert')).not.toBeInTheDocument()

    fireEvent.click(screen.getByTestId('rotate-github-secret-btn'))
    const confirmBtn = await screen.findByRole('button', { name: 'Xoay vòng ngay' })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(integrationApi.rotateGitHubSecret).toHaveBeenCalledWith('ws-1')
      expect(screen.getByTestId('github-oneshot-secret-value')).toHaveValue(
        'rotated_secret_xyz_9876'
      )
      expect(screen.getByText('****9876')).toBeInTheDocument()
    })
  })

  it('saves updated GitHub configuration and copies Payload URL', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      configurable: true,
    })

    vi.mocked(integrationApi.upsertGitHubIntegration).mockResolvedValueOnce({
      ...mockGitHubConfig,
      issueLabelFilter: 'backend, urgent',
    })

    renderPage()
    await switchToIntegrationsTab()

    await waitFor(() => {
      expect(screen.getByTestId('github-label-filter-input')).toHaveValue('teamnexus, bug')
    })

    fireEvent.click(screen.getByTestId('copy-github-payload-url-btn'))
    expect(writeText).toHaveBeenCalledWith(mockGitHubConfig.webhookPayloadUrl)

    fireEvent.change(screen.getByTestId('github-label-filter-input'), {
      target: { value: 'backend, urgent' },
    })
    fireEvent.click(screen.getByTestId('save-github-config-btn'))

    await waitFor(() => {
      expect(integrationApi.upsertGitHubIntegration).toHaveBeenCalledWith(
        'ws-1',
        expect.objectContaining({
          defaultBoardId: 'b-1',
          issueLabelFilter: 'backend, urgent',
        })
      )
    })
  })

  it('renders GitHub delivery ledger rows and refreshes deliveries on click', async () => {
    renderPage()
    await switchToIntegrationsTab()

    await waitFor(() => {
      expect(screen.getByText('pull_request')).toBeInTheDocument()
      expect(screen.getByText('.closed')).toBeInTheDocument()
      expect(screen.getByText('octocat/TeamNexus')).toBeInTheDocument()
      expect(screen.getByText('Processed')).toBeInTheDocument()
      expect(screen.getByText('Chuyển TN-42 sang Hoàn thành')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId('refresh-github-deliveries-btn'))
    await waitFor(() => {
      expect(integrationApi.getGitHubDeliveries).toHaveBeenCalledTimes(2)
    })
  })

  it('blocks enabling auto-create from issues until a default board is selected', async () => {
    // Review 15.5 F3: without a board the API would accept the config but silently ignore every issue.
    vi.mocked(integrationApi.getGitHubIntegration).mockResolvedValueOnce(null)

    renderPage()
    await switchToIntegrationsTab()

    fireEvent.click(screen.getByTestId('enable-github-btn'))

    expect(
      await screen.findByText(
        /Vui lòng chọn "Bảng mặc định nhận Issue" trước khi bật tự động tạo thẻ/
      )
    ).toBeInTheDocument()
    expect(integrationApi.upsertGitHubIntegration).not.toHaveBeenCalled()
  })

  it('deletes the GitHub configuration and returns to the not-configured state', async () => {
    renderPage()
    await switchToIntegrationsTab()

    fireEvent.click(await screen.findByTestId('delete-github-config-btn'))
    fireEvent.click(await screen.findByRole('button', { name: 'Xoá cấu hình' }))

    await waitFor(() => {
      expect(integrationApi.deleteGitHubIntegration).toHaveBeenCalledWith('ws-1')
      expect(
        screen.getByText('Chưa kích hoạt tích hợp GitHub cho không gian làm việc này')
      ).toBeInTheDocument()
    })
  })

  it('reports an error instead of a false success when the clipboard API is unavailable', async () => {
    // Review 15.5 F2: `navigator.clipboard?.writeText` used to resolve as a no-op and show success.
    Object.defineProperty(navigator, 'clipboard', { value: undefined, configurable: true })

    renderPage()
    await switchToIntegrationsTab()

    fireEvent.click(await screen.findByTestId('copy-github-payload-url-btn'))

    expect(
      await screen.findByText(
        'Trình duyệt không cho phép sao chép — hãy bôi đen và sao chép thủ công.'
      )
    ).toBeInTheDocument()
  })
})
