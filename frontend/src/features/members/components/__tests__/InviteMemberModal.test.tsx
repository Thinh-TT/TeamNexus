import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { InviteMemberModal } from '../InviteMemberModal'

describe('InviteMemberModal component', () => {
  it('renders with Vietnamese labels and default role Member', () => {
    render(
      <InviteMemberModal
        open={true}
        onClose={vi.fn()}
        onInvite={vi.fn()}
      />
    )

    expect(
      screen.getByText('Mời thành viên vào không gian làm việc')
    ).toBeInTheDocument()
    expect(screen.getByText('Địa chỉ Email')).toBeInTheDocument()
    expect(screen.getByText('Vai trò được chỉ định')).toBeInTheDocument()
    expect(screen.getByText('Thành viên (Member)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Gửi lời mời' })).toBeInTheDocument()
  })

  it('validates empty email field and displays error message', async () => {
    render(
      <InviteMemberModal
        open={true}
        onClose={vi.fn()}
        onInvite={vi.fn()}
      />
    )

    const submitBtn = screen.getByRole('button', { name: 'Gửi lời mời' })
    fireEvent.click(submitBtn)

    expect(
      await screen.findByText('Vui lòng nhập địa chỉ email')
    ).toBeInTheDocument()
  })

  it('validates 5 invalid email formats and displays format error', async () => {
    const invalidEmails = [
      'plainaddress',
      '#@%^%#$@#$@#.com',
      '@example.com',
      'email@example',
      'email with space@domain.com',
    ]

    render(
      <InviteMemberModal
        open={true}
        onClose={vi.fn()}
        onInvite={vi.fn()}
      />
    )

    const input = screen.getByPlaceholderText('nhanvien@example.com')
    const submitBtn = screen.getByRole('button', { name: 'Gửi lời mời' })

    for (const badEmail of invalidEmails) {
      fireEvent.change(input, { target: { value: badEmail } })
      fireEvent.click(submitBtn)

      expect(
        await screen.findByText('Định dạng email không hợp lệ')
      ).toBeInTheDocument()
    }
  })

  it('calls onInvite with correct trimmed payload when valid', async () => {
    const onInvite = vi.fn().mockResolvedValue({
      id: 'inv-1',
      invitedEmail: 'thinh@example.com',
      invitedRole: 'Member',
      emailSent: true,
    })

    render(
      <InviteMemberModal
        open={true}
        onClose={vi.fn()}
        onInvite={onInvite}
      />
    )

    const input = screen.getByPlaceholderText('nhanvien@example.com')
    fireEvent.change(input, { target: { value: '  thinh@example.com  ' } })

    const submitBtn = screen.getByRole('button', { name: 'Gửi lời mời' })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(onInvite).toHaveBeenCalledWith({
        email: 'thinh@example.com',
        role: 'Member',
      })
    })
  })

  it('displays warning alert and Resend button when emailSent is false', async () => {
    const onInvite = vi.fn().mockResolvedValue({
      id: 'inv-failed',
      invitedEmail: 'fail@example.com',
      invitedRole: 'Member',
      emailSent: false,
    })
    const onResend = vi.fn().mockResolvedValue(undefined)

    render(
      <InviteMemberModal
        open={true}
        onClose={vi.fn()}
        onInvite={onInvite}
        onResend={onResend}
      />
    )

    const input = screen.getByPlaceholderText('nhanvien@example.com')
    fireEvent.change(input, { target: { value: 'fail@example.com' } })

    const submitBtn = screen.getByRole('button', { name: 'Gửi lời mời' })
    fireEvent.click(submitBtn)

    expect(
      await screen.findByText('Đã tạo lời mời nhưng gửi email thất bại')
    ).toBeInTheDocument()

    const resendBtn = screen.getByRole('button', { name: /Gửi lại/i })
    expect(resendBtn).toBeInTheDocument()

    fireEvent.click(resendBtn)
    await waitFor(() => {
      expect(onResend).toHaveBeenCalledWith('inv-failed', 'fail@example.com', 'Member')
    })
  })

  it('calls onClose when invitation is successfully created and sent', async () => {
    const onClose = vi.fn()
    const onInvite = vi.fn().mockResolvedValue({
      id: 'inv-success',
      invitedEmail: 'ok@example.com',
      invitedRole: 'Member',
      emailSent: true,
    })

    render(
      <InviteMemberModal
        open={true}
        onClose={onClose}
        onInvite={onInvite}
      />
    )

    const input = screen.getByPlaceholderText('nhanvien@example.com')
    fireEvent.change(input, { target: { value: 'ok@example.com' } })

    const submitBtn = screen.getByRole('button', { name: 'Gửi lời mời' })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(onClose).toHaveBeenCalled()
    })
  })

  it('closes modal when clicking Đóng', () => {
    const onClose = vi.fn()
    render(
      <InviteMemberModal
        open={true}
        onClose={onClose}
        onInvite={vi.fn()}
      />
    )

    const closeBtn = screen.getByRole('button', { name: 'Đóng' })
    fireEvent.click(closeBtn)

    expect(onClose).toHaveBeenCalled()
  })
})
