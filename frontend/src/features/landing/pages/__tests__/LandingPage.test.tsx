import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LandingPage } from '../LandingPage'
import { useAuth } from '../../../auth/hooks/useAuth'

const mockNavigate = vi.fn()
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

vi.mock('../../../auth/hooks/useAuth', () => ({
  useAuth: vi.fn(),
}))

describe('LandingPage (Luminous Enterprise v4.2)', () => {
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

  it('renders branding, title, v4.2 badge, and live proof metrics', () => {
    render(<LandingPage />)

    // Brand and logo
    expect(screen.getAllByText('TeamNexus').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText(/v4\.2 • Hybrid AI/i)).toBeInTheDocument()

    // Main headline
    expect(
      screen.getAllByText(/Điều phối Không gian Làm việc Thông minh với/i).length
    ).toBeGreaterThanOrEqual(1)
    expect(
      screen.getAllByText(/Hybrid AI & Lớp Giải trình Minh bạch/i).length
    ).toBeGreaterThanOrEqual(1)

    // Proof strip metrics
    expect(screen.getAllByText(/< 300ms/i).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText(/912 BE \| 738 FE/i).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText(/14 Migrations/i).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText(/ASP\.NET Core SignalR/i).length).toBeGreaterThanOrEqual(1)
  })

  it('triggers loginWithProvider when clicking GitHub and Google OAuth CTA buttons', () => {
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

  it('navigates to /login when clicking Header Login button', () => {
    render(<LandingPage />)

    const loginBtn = screen.getByRole('button', { name: /^Đăng nhập$/i })
    fireEvent.click(loginBtn)
    expect(mockNavigate).toHaveBeenCalledWith('/login')
  })

  it('allows switching between the 4 Interactive Command Center tabs', () => {
    render(<LandingPage />)

    // Default tab is Jev Triage
    expect(screen.getByText(/Kịch bản Nhiệm vụ Đầu vào \(Input Task\)/i)).toBeInTheDocument()

    // Switch to Kanban & AI Agent tab
    const kanbanTab = screen.getByRole('button', { name: /Kanban & AI Agent/i })
    fireEvent.click(kanbanTab)
    expect(screen.getByText(/⚡ IN PROGRESS \(AI AGENT ACTIVE\)/i)).toBeInTheDocument()
    expect(screen.getByText(/@tech_lead Agent/i)).toBeInTheDocument()

    // Switch to Accountability Layer tab
    const accountabilityTab = screen.getByRole('button', { name: /Accountability Layer/i })
    fireEvent.click(accountabilityTab)
    expect(screen.getByText(/Bản Ghi Đề Xuất AI \(AiActionLog #8492\)/i)).toBeInTheDocument()

    // Switch to Health tab
    const healthTab = screen.getByRole('button', { name: /Health & Auto-Healing/i })
    fireEvent.click(healthTab)
    expect(screen.getByText(/Project Health Score/i)).toBeInTheDocument()
    expect(screen.getByText(/92/i)).toBeInTheDocument()
    expect(screen.getByText(/Quá hạn \(Trọng số 40\)/i)).toBeInTheDocument()
  })

  it('allows toggling Jev confidence levels and renders corresponding decision strategies', () => {
    render(<LandingPage />)

    // Ensure we are on Jev triage tab
    const triageTab = screen.getByRole('button', { name: /Jev <300ms Triage/i })
    fireEvent.click(triageTab)

    // Default: High confidence -> DirectAssign
    expect(screen.getByText(/Strategy: DirectAssign/i)).toBeInTheDocument()
    expect(screen.getByText(/Lê Hoàng Anh \(Tech Lead\)/i)).toBeInTheDocument()

    // Click Medium confidence
    const medBtn = screen.getByRole('button', { name: /Trung bình \(68%\)/i })
    fireEvent.click(medBtn)
    expect(screen.getByText(/Strategy: OfferChoices/i)).toBeInTheDocument()
    expect(screen.getByText(/Nguyễn Minh Trí \(Senior Fullstack\)/i)).toBeInTheDocument()

    // Click Low confidence
    const lowBtn = screen.getByRole('button', { name: /Thấp \(38% < 0\.50\)/i })
    fireEvent.click(lowBtn)
    expect(screen.getByText(/Strategy: ManualRequired/i)).toBeInTheDocument()
    expect(screen.getByText(/Độ tự tin dưới ngưỡng an toàn/i)).toBeInTheDocument()
  })

  it('allows executing the Accountability workflow (Approve -> Undo -> Reset)', () => {
    render(<LandingPage />)

    // Switch to Accountability Layer tab
    const accountabilityTab = screen.getByRole('button', { name: /Accountability Layer/i })
    fireEvent.click(accountabilityTab)

    // 1. Initial status: Pending
    expect(screen.getByText(/Pending Approval \(Chờ duyệt\)/i)).toBeInTheDocument()

    // 2. Click Approve & Apply
    const approveBtn = screen.getByRole('button', { name: /Phê duyệt & Áp dụng/i })
    fireEvent.click(approveBtn)
    expect(screen.getByText(/Approved & Applied \(Đã áp dụng\)/i)).toBeInTheDocument()

    // 3. Click Undo
    const undoBtn = screen.getByRole('button', { name: /Hoàn tác \(Undo 1 chạm/i })
    fireEvent.click(undoBtn)
    expect(screen.getByText(/Undone & Restored \(Đã khôi phục\)/i)).toBeInTheDocument()

    // 4. Click Reset Demo
    const resetBtn = screen.getByRole('button', { name: /Thử lại kịch bản \(Reset Demo\)/i })
    fireEvent.click(resetBtn)
    expect(screen.getByText(/Pending Approval \(Chờ duyệt\)/i)).toBeInTheDocument()
  })

  it('renders Hybrid AI Dual-Card Bento section (System 1 Jev vs System 2 DeepSeek)', () => {
    render(<LandingPage />)

    expect(screen.getByText(/System 1: TypeSafe Jev 1\.13/i)).toBeInTheDocument()
    expect(screen.getByText(/Endpoint riêng \/api\/alpha\/decisions/i)).toBeInTheDocument()
    expect(screen.getByText(/Chống rò rỉ GUID 100%/i)).toBeInTheDocument()

    expect(screen.getByText(/System 2: DeepSeek Chat/i)).toBeInTheDocument()
    expect(screen.getByText(/Server-Sent Events \(SSE\) Streaming/i)).toBeInTheDocument()
    expect(screen.getByText(/Bộ lọc kép chống rò rỉ định danh/i)).toBeInTheDocument()
  })

  it('renders Multi-Agent Personas and pgvector Knowledge Base section', () => {
    render(<LandingPage />)

    expect(screen.getByText('@general')).toBeInTheDocument()
    expect(screen.getByText('@scrum_master')).toBeInTheDocument()
    expect(screen.getByText('@tech_lead')).toBeInTheDocument()
    expect(screen.getByText('@qa_security')).toBeInTheDocument()

    expect(screen.getByText(/Kho Tri Thức Ngữ Nghĩa \(pgvector HNSW RAG\)/i)).toBeInTheDocument()
    expect(screen.getAllByText(/QuestPDF/i).length).toBeGreaterThanOrEqual(1)
    expect(screen.getAllByText(/ClosedXML/i).length).toBeGreaterThanOrEqual(1)
  })

  it('renders Modular Monolith architecture and Ecosystem extension roadmap', () => {
    render(<LandingPage />)

    expect(screen.getByText(/Cấu Trúc Giải Pháp Thực Tế \(\.NET 10\)/i)).toBeInTheDocument()
    expect(screen.getByText(/TeamNexus\.Persistence/i)).toBeInTheDocument()
    expect(screen.getByText(/Modules\.Board/i)).toBeInTheDocument()
    expect(screen.getByText(/Modules\.Ai/i)).toBeInTheDocument()

    // Extension Roadmap
    expect(screen.getByText(/Hệ Sinh Thái Mở \(Webhooks\)/i)).toBeInTheDocument()
    expect(screen.getByText(/Ứng Dụng Di Động Đa Nền Tảng \(Flutter\)/i)).toBeInTheDocument()
  })

  it('renders bottom conversion card and footer navigation links', () => {
    render(<LandingPage />)

    expect(
      screen.getByText(/Sẵn Sàng Nâng Tầm Năng Suất Đội Ngũ Cùng TeamNexus\?/i)
    ).toBeInTheDocument()
    expect(
      screen.getByText(/© 2026 TeamNexus\. All rights reserved\./i)
    ).toBeInTheDocument()
  })
})
