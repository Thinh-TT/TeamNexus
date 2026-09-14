import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { WorkspaceMembersPage } from '../WorkspaceMembersPage'
import { memberApi } from '../../services/memberApi'
import { useWorkspaceRole } from '../../../../shared/hooks/useWorkspaceRole'
import { useAuthStore } from '../../../auth/store/useAuthStore'

vi.mock('../../services/memberApi', () => ({
  memberApi: {
    listMembers: vi.fn(),
    listInvitations: vi.fn(),
    updateMemberRole: vi.fn(),
    removeMember: vi.fn(),
    createInvitation: vi.fn(),
    cancelInvitation: vi.fn(),
    sendQuickEmail: vi.fn(),
  },
}))

vi.mock('../../../../shared/hooks/useWorkspaceRole', () => ({
  useWorkspaceRole: vi.fn(),
}))

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn().mockResolvedValue({ unreadCount: 0, items: [] }),
  },
}))

describe('WorkspaceMembersPage', () => {
  const wsId = 'ws-123'
  const mockMembers = [
    {
      userId: 'u-1',
      displayName: 'Admin User',
      role: 'Admin',
      avatarUrl: null,
      memberType: 'human',
      email: 'admin@example.com',
      joinedAt: '2026-09-01T00:00:00Z',
      isOwner: true,
    },
  ]
  const mockInvitations = [
    {
      id: 'inv-1',
      invitedEmail: 'invited@example.com',
      invitedRole: 'Member',
      status: 'Pending',
      invitedByName: 'Admin User',
      expiresAt: '2026-09-20T00:00:00Z',
      createdAt: '2026-09-14T00:00:00Z',
      acceptedByUserId: null,
      acceptedAt: null,
      emailSent: true,
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    useAuthStore.setState({
      user: { id: 'u-1', email: 'admin@example.com', displayName: 'Admin User', avatarUrl: null, roles: ['Admin'] },
      isAuthenticated: true,
    })
    vi.mocked(memberApi.listMembers).mockResolvedValue(mockMembers as any)
    vi.mocked(memberApi.listInvitations).mockResolvedValue(mockInvitations as any)
  })

  const renderComponent = () => {
    return render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/members`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/members" element={<WorkspaceMembersPage />} />
        </Routes>
      </MemoryRouter>
    )
  }

  it('renders members table and pending invitations tab for Manager/Admin', async () => {
    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: 'Admin',
      isAdmin: true,
      isManagerOrAdmin: true,
      isOwner: true,
      loading: false,
    } as any)

    renderComponent()

    expect(await screen.findByText('Admin User')).toBeInTheDocument()
    expect(screen.getByText(/Thành viên \(1\)/)).toBeInTheDocument()
    expect(screen.getByText(/Lời mời đang chờ \(1\)/)).toBeInTheDocument()
  })

  it('hides Invite and Quick Email buttons when user is only a Member', async () => {
    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: 'Member',
      isAdmin: false,
      isManagerOrAdmin: false,
      isOwner: false,
      loading: false,
    } as any)

    renderComponent()

    await screen.findByText('Admin User')

    expect(screen.queryByTestId('invite-member-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('quick-email-btn')).not.toBeInTheDocument()
  })

  it('opens invite member modal when clicking Mời thành viên button', async () => {
    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: 'Admin',
      isAdmin: true,
      isManagerOrAdmin: true,
      isOwner: true,
      loading: false,
    } as any)

    renderComponent()

    const inviteBtn = await screen.findByTestId('invite-member-btn')
    fireEvent.click(inviteBtn)

    expect(
      await screen.findByText('Mời thành viên vào không gian làm việc')
    ).toBeInTheDocument()
  })

  it('renders 403 Result view when API returns 403 Forbidden', async () => {
    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: null,
      isAdmin: false,
      isManagerOrAdmin: false,
      isOwner: false,
      loading: false,
    } as any)

    const error403 = { response: { status: 403 } }
    vi.mocked(memberApi.listMembers).mockRejectedValueOnce(error403)

    renderComponent()

    expect(
      await screen.findByText('403')
    ).toBeInTheDocument()
    expect(
      screen.getByText('Bạn không có quyền truy cập vào danh sách thành viên của workspace này.')
    ).toBeInTheDocument()
  })
})
