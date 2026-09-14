import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PendingInvitationsTable } from '../PendingInvitationsTable'
import type { InvitationResponse } from '../../types/member.types'

describe('PendingInvitationsTable component', () => {
  const mockInvitations: InvitationResponse[] = [
    {
      id: 'inv-1',
      invitedEmail: 'pending@example.com',
      invitedRole: 'Member',
      status: 'Pending',
      invitedByName: 'Admin User',
      expiresAt: '2026-09-20T10:00:00Z',
      createdAt: '2026-09-14T10:00:00Z',
      acceptedByUserId: null,
      acceptedAt: null,
      emailSent: true,
    },
    {
      id: 'inv-2',
      invitedEmail: 'accepted@example.com',
      invitedRole: 'Manager',
      status: 'Accepted',
      invitedByName: 'Admin User',
      expiresAt: '2026-09-20T10:00:00Z',
      createdAt: '2026-09-14T10:00:00Z',
      acceptedByUserId: 'u-accepted',
      acceptedAt: '2026-09-15T10:00:00Z',
      emailSent: true,
    },
    {
      id: 'inv-3',
      invitedEmail: 'expired@example.com',
      invitedRole: 'Member',
      status: 'Expired',
      invitedByName: 'Admin User',
      expiresAt: '2026-09-10T10:00:00Z',
      createdAt: '2026-09-03T10:00:00Z',
      acceptedByUserId: null,
      acceptedAt: null,
      emailSent: true,
    },
  ]

  it('renders invitation rows with email, role, inviter, and status tag', () => {
    render(
      <PendingInvitationsTable
        invitations={mockInvitations}
        onCancelInvitation={vi.fn()}
      />
    )

    expect(screen.getByText('pending@example.com')).toBeInTheDocument()
    expect(screen.getByText('accepted@example.com')).toBeInTheDocument()
    expect(screen.getByText('expired@example.com')).toBeInTheDocument()
    expect(screen.getByText('Đang chờ')).toBeInTheDocument()
    expect(screen.getByText('Đã chấp nhận')).toBeInTheDocument()
    expect(screen.getByText('Hết hạn')).toBeInTheDocument()
  })

  it('only renders Huỷ button for rows with Pending status', () => {
    render(
      <PendingInvitationsTable
        invitations={mockInvitations}
        onCancelInvitation={vi.fn()}
      />
    )

    const cancelButtons = screen.getAllByRole('button', { name: /Huỷ/i })
    expect(cancelButtons.length).toBe(1)
    expect(screen.getByLabelText('Huỷ lời mời pending@example.com')).toBeInTheDocument()
  })

  it('calls onCancelInvitation when confirming cancellation popconfirm', async () => {
    const onCancel = vi.fn()
    render(
      <PendingInvitationsTable
        invitations={mockInvitations}
        onCancelInvitation={onCancel}
      />
    )

    const cancelBtn = screen.getByLabelText('Huỷ lời mời pending@example.com')
    fireEvent.click(cancelBtn)

    const confirmBtn = await screen.findByRole('button', { name: 'Xác nhận' })
    fireEvent.click(confirmBtn)

    expect(onCancel).toHaveBeenCalledWith('inv-1')
  })

  it('displays empty description when invitations list is empty', () => {
    render(
      <PendingInvitationsTable
        invitations={[]}
        onCancelInvitation={vi.fn()}
      />
    )

    expect(
      screen.getByText('Chưa có lời mời nào đang chờ xử lý.')
    ).toBeInTheDocument()
  })
})
