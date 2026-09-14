import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { message } from 'antd'
import { AcceptInvitationPage } from '../AcceptInvitationPage'
import { invitationApi } from '../../services/invitationApi'
import { useAuthStore } from '../../../auth/store/useAuthStore'
import { getPendingInviteToken } from '../../utils/pendingInvite'

const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

vi.mock('../../services/invitationApi', () => ({
  invitationApi: {
    previewInvitation: vi.fn(),
    acceptInvitation: vi.fn(),
  },
}))

describe('AcceptInvitationPage', () => {
  const mockPreview = {
    workspaceId: 'ws-100',
    workspaceName: 'Engineering Workspace',
    invitedEmail: 'thinh@example.com',
    invitedRole: 'Member',
    invitedByName: 'Boss Tran',
    expiresAt: '2026-09-20T12:00:00Z',
    status: 'Pending',
  }

  beforeEach(() => {
    vi.clearAllMocks()
    sessionStorage.clear()
    useAuthStore.setState({
      user: { id: 'u-1', email: 'thinh@example.com', displayName: 'Thinh Tran', avatarUrl: null, roles: ['User'] },
      isAuthenticated: true,
      loginWithProvider: vi.fn(),
    })
    vi.mocked(invitationApi.previewInvitation).mockResolvedValue(mockPreview)
  })

  const renderComponent = (initialUrl = '/invitations/accept?token=valid-token') => {
    return render(
      <MemoryRouter initialEntries={[initialUrl]}>
        <Routes>
          <Route path="/invitations/accept" element={<AcceptInvitationPage />} />
        </Routes>
      </MemoryRouter>
    )
  }

  it('renders 400 error result when no token is present in URL or storage', () => {
    renderComponent('/invitations/accept')

    expect(screen.getByText('400')).toBeInTheDocument()
    expect(screen.getByText('Mã lời mời không hợp lệ hoặc bị thiếu.')).toBeInTheDocument()
  })

  it('shows OAuth sign-in buttons when user is not authenticated', () => {
    useAuthStore.setState({
      isAuthenticated: false,
      user: null,
    })

    renderComponent('/invitations/accept?token=oauth-token')

    expect(screen.getByText('Bạn nhận được lời mời tham gia workspace')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Đăng nhập bằng GitHub/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Đăng nhập bằng Google/i })).toBeInTheDocument()
    expect(getPendingInviteToken()).toBe('oauth-token')
  })

  it('renders preview with workspace name, inviter, role and expiry', async () => {
    renderComponent('/invitations/accept?token=valid-token')

    expect(await screen.findByText('Engineering Workspace')).toBeInTheDocument()
    expect(screen.getByText('Boss Tran')).toBeInTheDocument()
    expect(screen.getByText('thinh@example.com')).toBeInTheDocument()
    expect(screen.getByText('Thành viên')).toBeInTheDocument()
  })

  it('calls acceptInvitation and navigates to workspace boards on "Tham gia ngay"', async () => {
    vi.mocked(invitationApi.acceptInvitation).mockResolvedValueOnce({
      workspaceId: 'ws-100',
      role: 'Member',
      alreadyMember: false,
    })

    renderComponent('/invitations/accept?token=valid-token')

    const joinBtn = await screen.findByRole('button', { name: /Tham gia ngay/i })
    fireEvent.click(joinBtn)

    await waitFor(() => {
      expect(invitationApi.acceptInvitation).toHaveBeenCalledWith('valid-token')
      expect(mockNavigate).toHaveBeenCalledWith('/workspaces/ws-100/boards')
      expect(getPendingInviteToken()).toBeNull()
    })
  })

  it('displays info message and redirects when user is already a member', async () => {
    const infoSpy = vi.spyOn(message, 'info')
    vi.mocked(invitationApi.acceptInvitation).mockResolvedValueOnce({
      workspaceId: 'ws-100',
      role: 'Member',
      alreadyMember: true,
    })

    renderComponent('/invitations/accept?token=valid-token')

    const joinBtn = await screen.findByRole('button', { name: /Tham gia ngay/i })
    fireEvent.click(joinBtn)

    await waitFor(() => {
      expect(infoSpy).toHaveBeenCalledWith('Bạn đã là thành viên của không gian làm việc này.')
      expect(mockNavigate).toHaveBeenCalledWith('/workspaces/ws-100/boards')
    })
  })

  it('renders 403 error result when invitation is for a different email', async () => {
    const error403 = { response: { status: 403, data: { error: 'Email không khớp' } } }
    vi.mocked(invitationApi.previewInvitation).mockRejectedValueOnce(error403)

    renderComponent('/invitations/accept?token=wrong-email-token')

    expect(await screen.findByText('403 - Không có quyền truy cập')).toBeInTheDocument()
    expect(screen.getByText('Email không khớp')).toBeInTheDocument()
  })

  it('renders 410 error result when invitation is expired or cancelled', async () => {
    const error410 = { response: { status: 410, data: { error: 'Lời mời đã hết hạn' } } }
    vi.mocked(invitationApi.previewInvitation).mockRejectedValueOnce(error410)

    renderComponent('/invitations/accept?token=expired-token')

    expect(await screen.findByText('410 - Lời mời không còn hiệu lực')).toBeInTheDocument()
    expect(screen.getByText('Lời mời đã hết hạn')).toBeInTheDocument()
  })

  it('clears token from sessionStorage after completing or failing', async () => {
    sessionStorage.setItem('teamnexus_pending_invite_token', 'temp-token')
    const error410 = { response: { status: 410 } }
    vi.mocked(invitationApi.previewInvitation).mockRejectedValueOnce(error410)

    renderComponent('/invitations/accept?token=temp-token')

    await screen.findByText('410 - Lời mời không còn hiệu lực')
    expect(sessionStorage.getItem('teamnexus_pending_invite_token')).toBeNull()
  })

  it('shows error message on network failure during accept', async () => {
    const errorSpy = vi.spyOn(message, 'error')
    vi.mocked(invitationApi.acceptInvitation).mockRejectedValueOnce(
      new Error('Mất kết nối mạng')
    )

    renderComponent('/invitations/accept?token=valid-token')

    const joinBtn = await screen.findByRole('button', { name: /Tham gia ngay/i })
    fireEvent.click(joinBtn)

    await waitFor(() => {
      expect(errorSpy).toHaveBeenCalled()
    })
  })
})
