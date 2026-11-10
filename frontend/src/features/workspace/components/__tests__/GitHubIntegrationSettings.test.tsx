import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { GitHubIntegrationSettings } from '../GitHubIntegrationSettings'
import {
  type GitHubIntegrationResponse,
  type TestGitHubConnectionResponse,
  workspaceIntegrationsApi,
} from '../../services/workspaceIntegrationsApi'

vi.mock('../../services/workspaceIntegrationsApi', () => ({
  workspaceIntegrationsApi: {
    getGitHubIntegration: vi.fn(),
    updateGitHubIntegration: vi.fn(),
    deleteGitHubIntegration: vi.fn(),
    testGitHubConnection: vi.fn(),
  },
}))

describe('GitHubIntegrationSettings', () => {
  const wsId = 'ws-test-456'

  const mockIntegrationData: GitHubIntegrationResponse = {
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

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders correctly with existing integration data', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    // Wait for data to load
    await waitFor(() => {
      expect(screen.getByText('Tích hợp GitHub Repository')).toBeInTheDocument()
      expect(screen.getByText('● Đang kích hoạt')).toBeInTheDocument()
    })

    // Verify fields populated
    const repoInput = screen.getByTestId('github-repo-input') as HTMLInputElement
    expect(repoInput.value).toBe('Thinh-TT/TeamNexus')

    // Verify badges for configured PAT and Webhook Secret
    expect(screen.getByText('Đã cấu hình (ghp_••••••••3944)')).toBeInTheDocument()
    expect(screen.getByText('Đã cấu hình')).toBeInTheDocument()

    // Verify Webhook URL box
    const webhookUrlInput = screen.getByTestId('github-webhook-url') as HTMLInputElement
    expect(webhookUrlInput.value).toBe('https://api.teamnexus.cloud/api/webhooks/github')

    // Disconnect button should be present
    expect(screen.getByTestId('github-disconnect-btn')).toBeInTheDocument()
  })

  it('renders default empty state when no integration exists', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockRejectedValueOnce(
      new Error('Not found')
    )

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByText('Tích hợp GitHub Repository')).toBeInTheDocument()
      expect(screen.getByText('○ Chưa kích hoạt')).toBeInTheDocument()
    })

    const repoInput = screen.getByTestId('github-repo-input') as HTMLInputElement
    expect(repoInput.value).toBe('')

    // Disconnect button should NOT be present when empty
    expect(screen.queryByTestId('github-disconnect-btn')).not.toBeInTheDocument()
  })

  it('toggles enable/disable switch and submits updated status', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )
    vi.mocked(workspaceIntegrationsApi.updateGitHubIntegration).mockResolvedValueOnce({
      ...mockIntegrationData,
      isEnabled: false,
    })

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-enable-switch')).toBeInTheDocument()
    })

    const switchBtn = screen.getByTestId('github-enable-switch')
    fireEvent.click(switchBtn)

    const saveBtn = screen.getByTestId('github-save-btn')
    fireEvent.click(saveBtn)

    await waitFor(() => {
      expect(workspaceIntegrationsApi.updateGitHubIntegration).toHaveBeenCalledWith(
        wsId,
        expect.objectContaining({
          isEnabled: false,
        })
      )
    })
  })

  it('copies webhook URL to clipboard when clicking copy button', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )

    const writeTextMock = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, {
      clipboard: {
        writeText: writeTextMock,
      },
    })

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-copy-url-btn')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId('github-copy-url-btn'))

    await waitFor(() => {
      expect(writeTextMock).toHaveBeenCalledWith(
        'https://api.teamnexus.cloud/api/webhooks/github'
      )
    })
  })

  it('tests connection successfully and displays success alert', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )
    const testSuccess: TestGitHubConnectionResponse = {
      success: true,
      repoFullName: 'Thinh-TT/TeamNexus',
      defaultBranch: 'main',
    }
    vi.mocked(workspaceIntegrationsApi.testGitHubConnection).mockResolvedValueOnce(
      testSuccess
    )

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-test-btn')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId('github-test-btn'))

    await waitFor(() => {
      expect(workspaceIntegrationsApi.testGitHubConnection).toHaveBeenCalledWith(wsId)
      expect(screen.getByTestId('github-test-alert')).toBeInTheDocument()
      expect(screen.getByText('Kết nối GitHub thành công')).toBeInTheDocument()
      expect(
        screen.getByText(/Đã xác thực repository "Thinh-TT\/TeamNexus"\. Nhánh mặc định: main\./i)
      ).toBeInTheDocument()
    })
  })

  it('displays error alert when test connection fails', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )
    const testFailure: TestGitHubConnectionResponse = {
      success: false,
      errorMessage: 'Bad credentials or repository not found',
    }
    vi.mocked(workspaceIntegrationsApi.testGitHubConnection).mockResolvedValueOnce(
      testFailure
    )

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-test-btn')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByTestId('github-test-btn'))

    await waitFor(() => {
      expect(workspaceIntegrationsApi.testGitHubConnection).toHaveBeenCalledWith(wsId)
      expect(screen.getByTestId('github-test-alert')).toBeInTheDocument()
      expect(screen.getByText('Kết nối GitHub không thành công')).toBeInTheDocument()
      expect(
        screen.getByText('Bad credentials or repository not found')
      ).toBeInTheDocument()
    })
  })

  it('submits form with updated repository name and new PAT/secret', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )
    vi.mocked(workspaceIntegrationsApi.updateGitHubIntegration).mockResolvedValueOnce({
      ...mockIntegrationData,
      repoName: 'new-org/new-repo',
      maskedPat: 'ghp_••••••••8888',
    })

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-repo-input')).toBeInTheDocument()
    })

    const repoInput = screen.getByTestId('github-repo-input')
    fireEvent.change(repoInput, { target: { value: 'new-org/new-repo' } })

    const patInput = screen.getByTestId('github-pat-input')
    fireEvent.change(patInput, { target: { value: 'ghp_newtoken88888888' } })

    const secretInput = screen.getByTestId('github-secret-input')
    fireEvent.change(secretInput, { target: { value: 'my_new_webhook_secret' } })

    const saveBtn = screen.getByTestId('github-save-btn')
    fireEvent.click(saveBtn)

    await waitFor(() => {
      expect(workspaceIntegrationsApi.updateGitHubIntegration).toHaveBeenCalledWith(wsId, {
        isEnabled: true,
        repoName: 'new-org/new-repo',
        personalAccessToken: 'ghp_newtoken88888888',
        webhookSecret: 'my_new_webhook_secret',
      })
    })
  })

  it('disconnects GitHub integration when confirmed', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )
    vi.mocked(workspaceIntegrationsApi.deleteGitHubIntegration).mockResolvedValueOnce()

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-disconnect-btn')).toBeInTheDocument()
    })

    // Click disconnect button to show Popconfirm
    fireEvent.click(screen.getByTestId('github-disconnect-btn'))

    await waitFor(() => {
      expect(screen.getByText('Xác nhận ngắt kết nối GitHub?')).toBeInTheDocument()
    })

    // Click confirm in popconfirm
    const confirmBtn = screen.getByRole('button', { name: 'Ngắt kết nối' })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(workspaceIntegrationsApi.deleteGitHubIntegration).toHaveBeenCalledWith(wsId)
    })
  })

  it('disables controls when isManagerOrAdmin is false', async () => {
    vi.mocked(workspaceIntegrationsApi.getGitHubIntegration).mockResolvedValueOnce(
      mockIntegrationData
    )

    render(<GitHubIntegrationSettings workspaceId={wsId} isManagerOrAdmin={false} />)

    await waitFor(() => {
      expect(screen.getByTestId('github-save-btn')).toBeDisabled()
      expect(screen.getByTestId('github-test-btn')).toBeDisabled()
      expect(screen.getByTestId('github-disconnect-btn')).toBeDisabled()
    })
  })
})
