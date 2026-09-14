import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { AppHeader } from '../AppHeader'
import { useAuthStore } from '../../../features/auth/store/useAuthStore'

const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

vi.mock('../../../features/ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn().mockResolvedValue({ unreadCount: 0, items: [] }),
  },
}))

describe('AppHeader', () => {
  const mockLogout = vi.fn()
  const mockUser = {
    id: 'u-1',
    email: 'test@example.com',
    displayName: 'Thinh Tran',
    avatarUrl: 'https://example.com/avatar.png',
    roles: ['Admin'],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    useAuthStore.setState({
      user: mockUser,
      isAuthenticated: true,
      logout: mockLogout,
    })
  })

  it('renders default title TeamNexus and navigates home on click', () => {
    render(
      <MemoryRouter>
        <AppHeader />
      </MemoryRouter>
    )

    const title = screen.getByText('TeamNexus')
    expect(title).toBeInTheDocument()
    fireEvent.click(title)
    expect(mockNavigate).toHaveBeenCalledWith('/')
  })

  it('renders custom title when provided', () => {
    render(
      <MemoryRouter>
        <AppHeader title="Bảng điều khiển riêng" />
      </MemoryRouter>
    )

    expect(screen.getByText('Bảng điều khiển riêng')).toBeInTheDocument()
  })

  it('renders avatar and display name', () => {
    render(
      <MemoryRouter>
        <AppHeader />
      </MemoryRouter>
    )

    expect(screen.getByText('Thinh Tran')).toBeInTheDocument()
  })

  it('opens dropdown with "Hồ sơ cá nhân" navigating to /profile', async () => {
    render(
      <MemoryRouter>
        <AppHeader />
      </MemoryRouter>
    )

    const dropdownTrigger = screen.getByTestId('user-dropdown-trigger')
    fireEvent.click(dropdownTrigger)

    const profileOption = await screen.findByText('Hồ sơ cá nhân')
    expect(profileOption).toBeInTheDocument()
    fireEvent.click(profileOption)

    expect(mockNavigate).toHaveBeenCalledWith('/profile')
  })

  it('calls logout when clicking Đăng xuất', () => {
    render(
      <MemoryRouter>
        <AppHeader />
      </MemoryRouter>
    )

    const logoutButtons = screen.getAllByRole('button', { name: /Đăng xuất/i })
    expect(logoutButtons.length).toBeGreaterThan(0)
    fireEvent.click(logoutButtons[0])

    expect(mockLogout).toHaveBeenCalled()
  })

  it('renders children and hides notification bell when showNotifications is false', () => {
    render(
      <MemoryRouter>
        <AppHeader showNotifications={false}>
          <button data-testid="custom-child-btn">Nút tuỳ chỉnh</button>
        </AppHeader>
      </MemoryRouter>
    )

    expect(screen.getByTestId('custom-child-btn')).toBeInTheDocument()
    expect(screen.queryByTestId('notification-bell')).not.toBeInTheDocument()
  })
})
