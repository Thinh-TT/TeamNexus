import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LandingPage } from '../LandingPage'
import { useAuth } from '../../../auth/hooks/useAuth'

vi.mock('../../../auth/hooks/useAuth', () => ({
  useAuth: vi.fn(),
}))

describe('LandingPage (Public Home / Unauthenticated)', () => {
  const mockLoginWithProvider = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useAuth).mockReturnValue({
      user: null,
      isLoading: false,
      isAuthenticated: false,
      checkAuth: vi.fn(),
      loginWithProvider: mockLoginWithProvider,
      logout: vi.fn(),
    })
  })

  it('renders branding and headline for visitors', () => {
    render(<LandingPage />)

    expect(screen.getAllByText('TeamNexus').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/Quản trị & Điều phối Dự án Thông minh/i)).toBeInTheDocument()
    expect(screen.getByText(/Trí tuệ Nhân tạo & Kanban Thời gian thực/i)).toBeInTheDocument()
  })

  it('renders OAuth login buttons and triggers loginWithProvider', () => {
    render(<LandingPage />)

    const githubBtns = screen.getAllByRole('button', { name: /GitHub/i })
    expect(githubBtns.length).toBeGreaterThan(0)
    fireEvent.click(githubBtns[0])
    expect(mockLoginWithProvider).toHaveBeenCalledWith('github')

    const googleBtns = screen.getAllByRole('button', { name: /Google/i })
    expect(googleBtns.length).toBeGreaterThan(0)
    fireEvent.click(googleBtns[0])
    expect(mockLoginWithProvider).toHaveBeenCalledWith('google')
  })

  it('renders core feature pillars', () => {
    render(<LandingPage />)

    expect(screen.getByText('Bảng Kanban Thời gian thực')).toBeInTheDocument()
    expect(screen.getByText('AI Task Copilot (DeepSeek)')).toBeInTheDocument()
    expect(screen.getByText('AI Risk Observer & Health Gauge')).toBeInTheDocument()
    expect(screen.getByText('AI Board Templates Thông minh')).toBeInTheDocument()
    expect(screen.getByText('AI Accountability Layer')).toBeInTheDocument()
  })

  it('allows switching between interactive mockup tabs', () => {
    render(<LandingPage />)

    // Click on AI Copilot Chat tab
    const aiTab = screen.getByRole('button', { name: /AI Copilot Chat/i })
    fireEvent.click(aiTab)
    expect(screen.getByText(/Nexus AI Task Copilot/i)).toBeInTheDocument()

    // Click on Health tab
    const healthTab = screen.getByRole('button', { name: /Sức khỏe 92\/100/i })
    fireEvent.click(healthTab)
    expect(screen.getByText(/Đánh giá rủi ro tự động từ AI Risk Observer/i)).toBeInTheDocument()
  })
})
