import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { QuickEmailModal } from '../QuickEmailModal'
import type { WorkspaceMemberResponse } from '../../types/member.types'

describe('QuickEmailModal component', () => {
  const mockMembers: WorkspaceMemberResponse[] = [
    {
      userId: 'u-human-1',
      displayName: 'Nguyễn Văn Người',
      role: 'Member',
      avatarUrl: null,
      memberType: 'human',
      email: 'human@example.com',
      joinedAt: '2026-09-01T00:00:00Z',
      isOwner: false,
    },
    {
      userId: 'u-agent-1',
      displayName: 'Nexus AI Agent',
      role: 'Member',
      avatarUrl: null,
      memberType: 'ai_agent',
      email: null,
      joinedAt: '2026-09-01T00:00:00Z',
      isOwner: false,
    },
  ]

  it('filters out AI Agent members from recipient options', () => {
    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={vi.fn()}
      />
    )

    // Select container is rendered
    expect(screen.getByText('Người nhận (chỉ thành viên là người dùng)')).toBeInTheDocument()
    expect(screen.queryByText('Nexus AI Agent')).not.toBeInTheDocument()
  })

  it('validates empty subject and empty body fields', async () => {
    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={vi.fn()}
      />
    )

    const submitBtn = screen.getByRole('button', { name: /Gửi email/i })
    fireEvent.click(submitBtn)

    expect(
      await screen.findByText('Vui lòng chọn ít nhất một người nhận')
    ).toBeInTheDocument()
    expect(
      await screen.findByText('Vui lòng nhập tiêu đề email')
    ).toBeInTheDocument()
    expect(
      await screen.findByText('Vui lòng nhập nội dung email')
    ).toBeInTheDocument()
  })

  it('shows character counter for subject and body inputs', () => {
    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={vi.fn()}
      />
    )

    // Ant Design showCount render 0 / 200 and 0 / 8000
    expect(screen.getByText('0 / 200')).toBeInTheDocument()
    expect(screen.getByText('0 / 8000')).toBeInTheDocument()
  })

  it('rejects subject exceeding 200 characters limit', async () => {
    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={vi.fn()}
      />
    )

    const subjectInput = screen.getByPlaceholderText('Nhập tiêu đề thư...')
    const tooLongSubject = 'a'.repeat(205)
    fireEvent.change(subjectInput, { target: { value: tooLongSubject } })

    const submitBtn = screen.getByRole('button', { name: /Gửi email/i })
    fireEvent.click(submitBtn)

    expect(
      await screen.findByText('Tiêu đề không được vượt quá 200 ký tự')
    ).toBeInTheDocument()
  })

  it('calls onSend with correct recipient list and content', async () => {
    const onSend = vi.fn().mockResolvedValue({
      requested: 1,
      sent: 1,
      failed: 0,
      errors: [],
    })

    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={onSend}
      />
    )

    // Open select dropdown and pick member
    const select = screen.getByRole('combobox')
    fireEvent.mouseDown(select)

    const option = await screen.findByText(/Nguyễn Văn Người/)
    fireEvent.click(option)

    const subjectInput = screen.getByPlaceholderText('Nhập tiêu đề thư...')
    fireEvent.change(subjectInput, { target: { value: 'Họp khẩn' } })

    const bodyInput = screen.getByPlaceholderText(
      'Nhập nội dung chi tiết cần thông báo tới các thành viên...'
    )
    fireEvent.change(bodyInput, { target: { value: 'Nội dung cuộc họp lúc 10h' } })

    const submitBtn = screen.getByRole('button', { name: /Gửi email/i })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(onSend).toHaveBeenCalledWith({
        subject: 'Họp khẩn',
        body: 'Nội dung cuộc họp lúc 10h',
        recipientUserIds: ['u-human-1'],
      })
    })
  })

  it('displays sent/requested success alert after sending emails', async () => {
    const onSend = vi.fn().mockResolvedValue({
      requested: 2,
      sent: 2,
      failed: 0,
      errors: [],
    })

    render(
      <QuickEmailModal
        open={true}
        onClose={vi.fn()}
        members={mockMembers}
        onSend={onSend}
      />
    )

    // Pick member
    const select = screen.getByRole('combobox')
    fireEvent.mouseDown(select)
    const option = await screen.findByText(/Nguyễn Văn Người/)
    fireEvent.click(option)

    fireEvent.change(screen.getByPlaceholderText('Nhập tiêu đề thư...'), {
      target: { value: 'Thông báo' },
    })
    fireEvent.change(
      screen.getByPlaceholderText('Nhập nội dung chi tiết cần thông báo tới các thành viên...'),
      { target: { value: 'Nội dung' } }
    )

    fireEvent.click(screen.getByRole('button', { name: /Gửi email/i }))

    expect(
      await screen.findByText('Đã gửi thành công 2/2 email')
    ).toBeInTheDocument()
  })
})
