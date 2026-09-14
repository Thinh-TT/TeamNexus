import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MembersTable } from '../MembersTable'
import type { WorkspaceMemberResponse } from '../../types/member.types'

describe('MembersTable component', () => {
  const mockMembers: WorkspaceMemberResponse[] = [
    {
      userId: 'user-owner',
      displayName: 'Nguyễn Văn Owner',
      role: 'Admin',
      avatarUrl: 'https://example.com/owner.png',
      memberType: 'human',
      email: 'owner@example.com',
      joinedAt: '2026-09-01T10:00:00Z',
      isOwner: true,
    },
    {
      userId: 'user-admin',
      displayName: 'Trần Văn Admin',
      role: 'Admin',
      avatarUrl: null,
      memberType: 'human',
      email: 'admin@example.com',
      joinedAt: '2026-09-02T10:00:00Z',
      isOwner: false,
    },
    {
      userId: 'user-agent',
      displayName: 'Nexus Bot',
      role: 'Member',
      avatarUrl: null,
      memberType: 'ai_agent',
      email: null,
      joinedAt: '2026-09-03T10:00:00Z',
      isOwner: false,
    },
    {
      userId: 'user-member',
      displayName: 'Lê Văn Member',
      role: 'Member',
      avatarUrl: null,
      memberType: 'human',
      email: 'member@example.com',
      joinedAt: '2026-09-04T10:00:00Z',
      isOwner: false,
    },
  ]

  it('renders all standard table columns: Thành viên, Vai trò, Loại, Ngày tham gia', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={false}
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    expect(screen.getByRole('columnheader', { name: 'Thành viên' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Vai trò' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Loại' })).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Ngày tham gia' })).toBeInTheDocument()
  })

  it('displays owner badge for workspace owner', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={false}
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    expect(screen.getByTestId('owner-badge')).toBeInTheDocument()
    expect(screen.getByText('Chủ sở hữu')).toBeInTheDocument()
  })

  it('renders AI Agent tag for agent members', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={false}
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    const agentTags = screen.getAllByText('AI Agent')
    expect(agentTags.length).toBeGreaterThan(0)
  })

  it('hides action column when canManage is false (Member viewer)', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={false}
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    expect(screen.queryByText('Hành động')).not.toBeInTheDocument()
  })

  it('hides action column when canManage is false (Manager viewer)', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={false}
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    expect(screen.queryByText('Hành động')).not.toBeInTheDocument()
  })

  it('renders action column and controls when canManage is true (Admin viewer)', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={true}
        currentUserId="user-admin"
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    expect(screen.getByText('Hành động')).toBeInTheDocument()
  })

  it('disables kick action button for workspace owner', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={true}
        currentUserId="user-admin"
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    const buttons = screen.getAllByRole('button')
    // Owner button disabled
    const disabledButtons = buttons.filter((b) => b.hasAttribute('disabled'))
    expect(disabledButtons.length).toBeGreaterThan(0)
  })

  it('disables actions for the current user themselves', () => {
    render(
      <MembersTable
        members={mockMembers}
        canManage={true}
        currentUserId="user-admin"
        onRoleChange={vi.fn()}
        onRemoveMember={vi.fn()}
      />
    )

    // Current user is user-admin
    const selfRemoveBtn = screen.queryByLabelText('Xoá Trần Văn Admin')
    expect(selfRemoveBtn).not.toBeInTheDocument() // Disabled tooltip button replaces it
  })

  it('triggers onRemoveMember callback after confirming popconfirm for a normal member', async () => {
    const onRemove = vi.fn()
    render(
      <MembersTable
        members={mockMembers}
        canManage={true}
        currentUserId="user-admin"
        onRoleChange={vi.fn()}
        onRemoveMember={onRemove}
      />
    )

    const kickBtn = screen.getByLabelText('Xoá Lê Văn Member')
    fireEvent.click(kickBtn)

    const confirmBtn = await screen.findByRole('button', { name: 'Xác nhận' })
    fireEvent.click(confirmBtn)

    expect(onRemove).toHaveBeenCalledWith('user-member')
  })
})
