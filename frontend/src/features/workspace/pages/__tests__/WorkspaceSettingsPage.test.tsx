import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { WorkspaceSettingsPage } from '../WorkspaceSettingsPage'
import * as useWorkspaceRoleModule from '../../../../shared/hooks/useWorkspaceRole'
import * as useWorkspaceDetailModule from '../../hooks/useWorkspaceDetail'
import * as useWorkspaceMembersModule from '../../../board/hooks/useWorkspaceMembers'
import type { WorkspaceDetail } from '../../types/workspace.types'

// Mock AppHeader
vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: () => <div data-testid="app-header">AppHeader</div>,
}))

// Mock GitHubIntegrationSettings
vi.mock('../../components/GitHubIntegrationSettings', () => ({
  GitHubIntegrationSettings: ({ workspaceId }: { workspaceId: string }) => (
    <div data-testid="github-integration-settings-stub">GitHub Integration: {workspaceId}</div>
  ),
}))

// Mock TaxonomySettings
vi.mock('../../components/TaxonomySettings', () => ({
  TaxonomySettings: ({ workspaceId }: { workspaceId: string }) => (
    <div data-testid="taxonomy-settings-stub">Taxonomy Settings: {workspaceId}</div>
  ),
}))

describe('WorkspaceSettingsPage', () => {
  const wsId = 'ws-settings-test'

  const mockDetail: WorkspaceDetail = {
    id: wsId,
    name: 'Không Gian Dự Án A',
    description: 'Mô tả dự án A',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-10T00:00:00Z',
    ownerId: 'u-owner',
    ownerDisplayName: 'Nguyễn Chủ Sở Hữu',
    memberCount: 3,
    boardCount: 2,
    currentUserRole: 'Admin',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Admin',
      isMember: true,
      isManagerOrAdmin: true,
      isAdmin: true,
      isOwner: true,
      loading: false,
      reload: vi.fn(),
    })

    vi.spyOn(useWorkspaceDetailModule, 'useWorkspaceDetail').mockReturnValue({
      detail: mockDetail,
      loading: false,
      save: vi.fn().mockResolvedValue(true),
      transfer: vi.fn().mockResolvedValue(true),
      remove: vi.fn().mockResolvedValue(true),
      reload: vi.fn(),
    })

    vi.spyOn(useWorkspaceMembersModule, 'useWorkspaceMembers').mockReturnValue({
      members: [],
      loading: false,
      refetch: vi.fn(),
    })
  })

  const renderPage = (initialUrl = `/workspaces/${wsId}/settings`) => {
    return render(
      <MemoryRouter initialEntries={[initialUrl]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/settings" element={<WorkspaceSettingsPage />} />
          <Route path="/workspaces/:workspaceId/boards" element={<div>Trang danh sách bảng</div>} />
          <Route path="/" element={<div>Trang chủ</div>} />
        </Routes>
      </MemoryRouter>
    )
  }

  it('renders page header and tabs (Chung, Taxonomy, Tích hợp GitHub)', async () => {
    renderPage()

    expect(screen.getByText('Cài đặt không gian làm việc')).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Chung/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Phân loại \(Taxonomy\)/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /Tích hợp GitHub/i })).toBeInTheDocument()
  })

  it('renders workspace general info by default', () => {
    renderPage()

    expect(screen.getByText('Không Gian Dự Án A')).toBeInTheDocument()
    expect(screen.getByText('Mô tả dự án A')).toBeInTheDocument()
    expect(screen.getByText('Nguyễn Chủ Sở Hữu')).toBeInTheDocument()
  })

  it('switches to Tích hợp GitHub tab and renders GitHubIntegrationSettings', async () => {
    renderPage()

    const githubTab = screen.getByRole('tab', { name: /Tích hợp GitHub/i })
    fireEvent.click(githubTab)

    await waitFor(() => {
      expect(screen.getByTestId('github-integration-settings-stub')).toBeInTheDocument()
      expect(
        screen.getByText(`GitHub Integration: ${wsId}`)
      ).toBeInTheDocument()
    })
  })

  it('switches to Phân loại (Taxonomy) tab and renders TaxonomySettings', async () => {
    renderPage()

    const taxonomyTab = screen.getByRole('tab', { name: /Phân loại \(Taxonomy\)/i })
    fireEvent.click(taxonomyTab)

    await waitFor(() => {
      expect(screen.getByTestId('taxonomy-settings-stub')).toBeInTheDocument()
      expect(screen.getByText(`Taxonomy Settings: ${wsId}`)).toBeInTheDocument()
    })
  })
})
