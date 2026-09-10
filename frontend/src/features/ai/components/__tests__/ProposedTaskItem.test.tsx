import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ProposedTaskItem } from '../ProposedTaskItem'
import type { EditableTaskProposal, WorkspaceMember } from '../../types/smartSetup.types'
import type { LabelResponse } from '../../../board/types/board.types'

const mockTask: EditableTaskProposal = {
  tempId: 'temp-1',
  title: 'Xây dựng API xác thực',
  description: 'Endpoint đăng nhập với JWT',
  priority: 'High',
  labels: [
    { labelId: 'lbl-1', name: 'backend', exists: true },
    { labelId: null, name: 'auth-v2', exists: false },
  ],
  assignee: { userId: 'u1', displayName: 'Thinh', matched: true },
}

const mockMembers: WorkspaceMember[] = [
  { userId: 'u1', displayName: 'Thinh', role: 'Admin', avatarUrl: null },
  { userId: 'u2', displayName: 'Jane Doe', role: 'Member', avatarUrl: null },
]

const mockWorkspaceLabels: LabelResponse[] = [
  { id: 'lbl-1', workspaceId: 'ws-1', name: 'backend', color: '#3b82f6', createdAt: '' },
  { id: 'lbl-2', workspaceId: 'ws-1', name: 'frontend', color: '#10b981', createdAt: '' },
]

describe('ProposedTaskItem', () => {
  it('renders task title, priority, labels, and description', () => {
    const onUpdate = vi.fn()
    const onDelete = vi.fn()
    const onAddLabel = vi.fn()
    const onRemoveLabel = vi.fn()

    render(
      <ProposedTaskItem
        task={mockTask}
        index={0}
        members={mockMembers}
        workspaceLabels={mockWorkspaceLabels}
        onUpdate={onUpdate}
        onDelete={onDelete}
        onAddLabel={onAddLabel}
        onRemoveLabel={onRemoveLabel}
      />
    )

    expect(screen.getByDisplayValue('Xây dựng API xác thực')).toBeInTheDocument()
    expect(screen.getByText('backend')).toBeInTheDocument()
    expect(screen.getByText('auth-v2')).toBeInTheDocument()
    expect(screen.getByText('(mới)')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Endpoint đăng nhập với JWT')).toBeInTheDocument()
  })

  it('triggers onUpdate when editing title and description', () => {
    const onUpdate = vi.fn()
    const onDelete = vi.fn()
    const onAddLabel = vi.fn()
    const onRemoveLabel = vi.fn()

    render(
      <ProposedTaskItem
        task={mockTask}
        index={0}
        members={mockMembers}
        workspaceLabels={mockWorkspaceLabels}
        onUpdate={onUpdate}
        onDelete={onDelete}
        onAddLabel={onAddLabel}
        onRemoveLabel={onRemoveLabel}
      />
    )

    const titleInput = screen.getByDisplayValue('Xây dựng API xác thực')
    fireEvent.change(titleInput, { target: { value: 'Xây dựng API OAuth' } })
    expect(onUpdate).toHaveBeenCalledWith('temp-1', { title: 'Xây dựng API OAuth' })

    const descInput = screen.getByDisplayValue('Endpoint đăng nhập với JWT')
    fireEvent.change(descInput, { target: { value: 'Chi tiết mới' } })
    expect(onUpdate).toHaveBeenCalledWith('temp-1', { description: 'Chi tiết mới' })
  })

  it('shows unmatched assignee warning tag when matched is false', () => {
    const unmatchedTask: EditableTaskProposal = {
      ...mockTask,
      assignee: { userId: null, displayName: 'Linh Nguyen', matched: false },
    }

    render(
      <ProposedTaskItem
        task={unmatchedTask}
        index={0}
        members={mockMembers}
        workspaceLabels={mockWorkspaceLabels}
        onUpdate={vi.fn()}
        onDelete={vi.fn()}
        onAddLabel={vi.fn()}
        onRemoveLabel={vi.fn()}
      />
    )

    expect(screen.getByText(/chưa khớp thành viên/i)).toBeInTheDocument()
    expect(screen.getByText('Linh Nguyen')).toBeInTheDocument()
  })

  it('triggers onDelete when clicking the delete button', () => {
    const onDelete = vi.fn()

    render(
      <ProposedTaskItem
        task={mockTask}
        index={0}
        members={mockMembers}
        workspaceLabels={mockWorkspaceLabels}
        onUpdate={vi.fn()}
        onDelete={onDelete}
        onAddLabel={vi.fn()}
        onRemoveLabel={vi.fn()}
      />
    )

    const deleteBtn = screen.getByRole('button', { name: /delete/i })
    fireEvent.click(deleteBtn)
    expect(onDelete).toHaveBeenCalledWith('temp-1')
  })
})
