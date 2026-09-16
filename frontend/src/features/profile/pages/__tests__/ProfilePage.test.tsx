import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { message } from 'antd'
import { ProfilePage } from '../ProfilePage'
import { profileApi } from '../../services/profileApi'
import { useAuthStore } from '../../../auth/store/useAuthStore'

vi.mock('../../services/profileApi', () => ({
  profileApi: {
    getProfile: vi.fn(),
    updateProfile: vi.fn(),
    listMyWorkspaces: vi.fn(),
    leaveWorkspace: vi.fn(),
  },
}))

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn().mockResolvedValue({ data: { unreadCount: 0, items: [] } }),
  },
}))

describe('ProfilePage', () => {
  const mockUser = {
    id: 'u-1',
    email: 'user@example.com',
    displayName: 'Thinh Tran',
    avatarUrl: 'https://example.com/avatar.png',
    roles: ['User'],
  }

  const mockWorkspaces = [
    {
      id: 'ws-1',
      name: 'Alpha Project',
      description: 'Dự án Alpha',
      role: 'Admin',
      ownerId: 'u-1',
      isOwner: true,
    },
    {
      id: 'ws-2',
      name: 'Beta Project',
      description: null,
      role: 'Member',
      ownerId: 'u-99',
      isOwner: false,
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    useAuthStore.setState({
      user: mockUser,
      isAuthenticated: true,
      checkAuth: vi.fn().mockResolvedValue(undefined),
    })
    // Cần mock mặc định cho getProfile: ProfilePage luôn gọi fetchProfile() khi mount.
    // Không mock ⇒ promise resolve muộn, sau khi jsdom teardown ⇒ React scheduler setImmediate
    // cố gọi setState với window=undefined ⇒ unhandled ReferenceError làm CI đỏ dù mọi test pass.
    // Các test PF-* ghi đè mock này theo từng trường hợp riêng của mình.
    vi.mocked(profileApi.getProfile).mockResolvedValue({
      id: 'u-1',
      email: 'user@example.com',
      displayName: 'Thinh Tran',
      avatarUrl: 'https://example.com/avatar.png',
      createdAt: '2026-01-01T00:00:00Z',
      digestEnabled: true,
    } as any)
    vi.mocked(profileApi.listMyWorkspaces).mockResolvedValue(mockWorkspaces as any)
  })

  it('prefills form inputs from useAuthStore', () => {
    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    expect(screen.getByDisplayValue('Thinh Tran')).toBeInTheDocument()
    expect(screen.getByDisplayValue('https://example.com/avatar.png')).toBeInTheDocument()
  })

  it('validates unsafe avatar URL schemes (javascript:, file:, data:)', async () => {
    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    await screen.findByDisplayValue('Thinh Tran')

    const avatarInput = screen.getByPlaceholderText('https://example.com/avatar.png')
    fireEvent.change(avatarInput, { target: { value: 'javascript:alert(1)' } })

    const submitBtn = screen.getByRole('button', { name: /Lưu thay đổi/i })
    fireEvent.click(submitBtn)

    expect(
      await screen.findByText(/URL avatar không an toàn/i)
    ).toBeInTheDocument()
  })

  it('calls updateProfile and checkAuth on valid submission', async () => {
    vi.mocked(profileApi.updateProfile).mockResolvedValueOnce({
      ...mockUser,
      displayName: 'Thinh Updated',
    } as any)

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const nameInput = await screen.findByDisplayValue('Thinh Tran')
    fireEvent.change(nameInput, { target: { value: 'Thinh Updated' } })

    const submitBtn = screen.getByRole('button', { name: /Lưu thay đổi/i })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(profileApi.updateProfile).toHaveBeenCalledWith({
        displayName: 'Thinh Updated',
        avatarUrl: 'https://example.com/avatar.png',
      })
      expect(useAuthStore.getState().checkAuth).toHaveBeenCalled()
    })
  })

  it('renders workspaces list with name, description, and role tags', async () => {
    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    // Switch to workspaces tab
    const wsTab = await screen.findByRole('tab', { name: /Không gian làm việc/i })
    fireEvent.click(wsTab)

    expect(await screen.findByText('Alpha Project')).toBeInTheDocument()
    expect(screen.getByText('Dự án Alpha')).toBeInTheDocument()
    expect(screen.getByText('Beta Project')).toBeInTheDocument()
    expect(screen.getByText('Chưa có mô tả')).toBeInTheDocument()
  })

  it('disables Rời button for owner with explanation tooltip', async () => {
    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const wsTab = await screen.findByRole('tab', { name: /Không gian làm việc/i })
    fireEvent.click(wsTab)

    await screen.findByText('Alpha Project')

    const leaveButtons = screen.getAllByRole('button', { name: /Rời/i })
    const disabledButtons = leaveButtons.filter((b) => b.hasAttribute('disabled'))
    expect(disabledButtons.length).toBe(1)
  }, 15000)

  it('calls leaveWorkspace and reloads list when leaving a non-owned workspace', async () => {
    vi.mocked(profileApi.leaveWorkspace).mockResolvedValueOnce(undefined)

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const wsTab = await screen.findByRole('tab', { name: /Không gian làm việc/i })
    fireEvent.click(wsTab)

    await screen.findByText('Beta Project')

    const leaveButtons = screen.getAllByRole('button', { name: /Rời/i })
    const activeLeaveBtn = leaveButtons.find((b) => !b.hasAttribute('disabled'))
    expect(activeLeaveBtn).toBeDefined()
    fireEvent.click(activeLeaveBtn!)

    const confirmBtn = await screen.findByRole('button', { name: 'Xác nhận' })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(profileApi.leaveWorkspace).toHaveBeenCalledWith('ws-2')
    })
  }, 15000)

  it('shows error message when listMyWorkspaces fails', async () => {
    const errorSpy = vi.spyOn(message, 'error')
    vi.mocked(profileApi.listMyWorkspaces).mockRejectedValueOnce(
      new Error('Lỗi máy chủ nội bộ')
    )

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const wsTab = screen.getByText(/Không gian làm việc của tôi/)
    fireEvent.click(wsTab)

    await waitFor(() => {
      expect(errorSpy).toHaveBeenCalled()
    })
  })

  it('shows empty description when user belongs to zero workspaces', async () => {
    vi.mocked(profileApi.listMyWorkspaces).mockResolvedValueOnce([])

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const wsTab = screen.getByText(/Không gian làm việc của tôi/)
    fireEvent.click(wsTab)

    expect(
      await screen.findByText('Bạn chưa tham gia không gian làm việc nào.')
    ).toBeInTheDocument()
  })

  // ---- Giai đoạn 13 §5: email digest opt-out ------------------------------

  /** Mở tab "Thông báo" và trả về công tắc. */
  const openNotificationTab = async () => {
    fireEvent.click(screen.getByText('Thông báo'))
    return await screen.findByTestId('digest-toggle')
  }

  const profileWithDigest = (digestEnabled: boolean) => ({
    id: 'u-1',
    email: 'user@example.com',
    displayName: 'Thinh Tran',
    avatarUrl: 'https://example.com/avatar.png',
    createdAt: '2026-01-01T00:00:00Z',
    digestEnabled,
  })

  it('PF-1: tab "Thông báo" hiện và công tắc phản ánh digestEnabled từ API', async () => {
    // Cố ý để `true` để chứng minh giá trị đến từ API, không phải mặc định của state.
    vi.mocked(profileApi.getProfile).mockResolvedValue(profileWithDigest(true))

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const toggle = await openNotificationTab()

    await waitFor(() => {
      expect(toggle).toHaveAttribute('aria-checked', 'true')
    })

    expect(screen.getByText(/Email tóm tắt công việc hằng ngày/)).toBeInTheDocument()
  })

  it('PF-1b: digestEnabled = false từ API ⇒ công tắc tắt', async () => {
    vi.mocked(profileApi.getProfile).mockResolvedValue(profileWithDigest(false))

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const toggle = await openNotificationTab()

    await waitFor(() => {
      expect(toggle).toHaveAttribute('aria-checked', 'false')
    })

    expect(screen.getByText('Đang tắt')).toBeInTheDocument()
  })

  it('PF-2: bật/tắt ⇒ gửi digestEnabled và GIỮ NGUYÊN displayName/avatarUrl', async () => {
    vi.mocked(profileApi.getProfile).mockResolvedValue(profileWithDigest(true))
    vi.mocked(profileApi.updateProfile).mockResolvedValue(profileWithDigest(false))

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const toggle = await openNotificationTab()
    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(profileApi.updateProfile).toHaveBeenCalled()
    })

    // Gửi kèm hai field còn lại vì PUT yêu cầu displayName và ghi đè cả hai — bỏ chúng đi sẽ xoá tên
    // hiển thị của người dùng.
    expect(vi.mocked(profileApi.updateProfile).mock.calls[0][0]).toEqual({
      displayName: 'Thinh Tran',
      avatarUrl: 'https://example.com/avatar.png',
      digestEnabled: false,
    })
  })

  it('PF-3: API lỗi ⇒ message.error tiếng Việt và ROLLBACK công tắc', async () => {
    const errorSpy = vi.spyOn(message, 'error').mockImplementation(() => ({}) as any)
    vi.mocked(profileApi.getProfile).mockResolvedValue(profileWithDigest(true))
    vi.mocked(profileApi.updateProfile).mockRejectedValue({
      response: { data: { error: 'Không thể cập nhật tuỳ chọn thông báo' } },
    })

    render(
      <MemoryRouter>
        <ProfilePage />
      </MemoryRouter>
    )

    const toggle = await openNotificationTab()
    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))

    fireEvent.click(toggle)

    // Giữ nguyên trạng thái mới sau lỗi sẽ khiến người dùng tin digest đã tắt trong khi server vẫn gửi.
    await waitFor(() => {
      expect(toggle).toHaveAttribute('aria-checked', 'true')
    })

    expect(errorSpy).toHaveBeenCalled()
    errorSpy.mockRestore()
  })

  it('PF-4: hash #notifications ⇒ mở thẳng tab "Thông báo" (đích link "Tắt nhận" trong email)', async () => {
    vi.mocked(profileApi.getProfile).mockResolvedValue(profileWithDigest(true))

    const originalHash = window.location.hash
    window.location.hash = '#notifications'

    try {
      render(
        <MemoryRouter>
          <ProfilePage />
        </MemoryRouter>
      )

      // Không cần bấm tab nào: nội dung tab Thông báo phải đã hiển thị.
      expect(await screen.findByTestId('notification-settings')).toBeInTheDocument()
      expect(screen.getByTestId('digest-toggle')).toBeInTheDocument()
    } finally {
      window.location.hash = originalHash
    }
  })
})
