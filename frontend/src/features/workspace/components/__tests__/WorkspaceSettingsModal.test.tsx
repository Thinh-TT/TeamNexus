import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceSettingsModal } from '../WorkspaceSettingsModal'
import type { WorkspaceMemberResponse } from '../../../board/types/board.types'
import type { WorkspaceDetail } from '../../types/workspace.types'

describe('WorkspaceSettingsModal', () => {
  const mockDetail: WorkspaceDetail = {
    id: 'ws-1',
    name: 'Không Gian Dự Án A',
    description: 'Mô tả dự án A',
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-10T00:00:00Z',
    ownerId: 'u-owner',
    ownerDisplayName: 'Nguyễn Chủ Sở Hữu',
    memberCount: 3,
    boardCount: 2,
    currentUserRole: 'Admin',
  }

  const mockMembers: WorkspaceMemberResponse[] = [
    {
      userId: 'u-owner',
      displayName: 'Nguyễn Chủ Sở Hữu',
      email: 'owner@example.com',
      role: 'Admin',
      memberType: 'human',
    },
    {
      userId: 'u-human',
      displayName: 'Trần Thành Viên',
      email: 'human@example.com',
      role: 'Member',
      memberType: 'human',
    },
    {
      userId: 'u-ai-agent',
      displayName: 'Nexus Agent Executor',
      email: 'agent@nexus.ai',
      role: 'Member',
      memberType: 'ai_agent', // AI Agent member!
    },
  ]

  const onSave = vi.fn().mockResolvedValue(true)
  const onTransfer = vi.fn().mockResolvedValue(true)
  const onDelete = vi.fn().mockResolvedValue(true)
  const onClose = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  const renderModal = (isAdminOrOwner = true, isManagerOrAdmin = true) => {
    return render(
      <WorkspaceSettingsModal
        open={true}
        onClose={onClose}
        detail={mockDetail}
        members={mockMembers}
        isManagerOrAdmin={isManagerOrAdmin}
        isAdminOrOwner={isAdminOrOwner}
        onSave={onSave}
        onTransfer={onTransfer}
        onDelete={onDelete}
      />
    )
  }

  it('renders modal with workspace name and description loaded in form', () => {
    renderModal()
    expect(screen.getByDisplayValue('Không Gian Dự Án A')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Mô tả dự án A')).toBeInTheDocument()
  })

  it('calls onSave with updated trimmed name and description when form submitted', async () => {
    renderModal()

    const nameInput = screen.getByDisplayValue('Không Gian Dự Án A')
    fireEvent.change(nameInput, { target: { value: 'Tên Đã Đổi' } })

    const saveBtn = screen.getByRole('button', { name: 'Lưu Thay Đổi' })
    fireEvent.click(saveBtn)

    await waitFor(() => {
      expect(onSave).toHaveBeenCalledWith({
        name: 'Tên Đã Đổi',
        description: 'Mô tả dự án A',
      })
    })
  })

  it('hides Danger Zone tab when user is not Admin or Owner', () => {
    renderModal(false, true)
    expect(screen.queryByText('Vùng nguy hiểm')).not.toBeInTheDocument()
  })

  it('shows Danger Zone tab when user is Admin or Owner', () => {
    renderModal(true, true)
    expect(screen.getByText('Vùng nguy hiểm')).toBeInTheDocument()
  })

  it('ownership transfer select NEVER includes AI Agent members', async () => {
    renderModal(true, true)

    // Switch to Danger tab
    fireEvent.click(screen.getByText('Vùng nguy hiểm'))

    await waitFor(() => {
      expect(screen.getByTestId('transfer-owner-select')).toBeInTheDocument()
    })

    // Open member select dropdown
    const select = screen.getByTestId('transfer-owner-select')
    fireEvent.mouseDown(select.querySelector('.ant-select-selector') || select)

    await waitFor(() => {
      // Human member is present
      expect(screen.getByText('Trần Thành Viên (human@example.com)')).toBeInTheDocument()
      // AI agent is strictly NOT in the dropdown
      expect(screen.queryByText('Nexus Agent Executor (agent@nexus.ai)')).not.toBeInTheDocument()
    })
  })

  it('disables delete button when confirmation text does not match workspace name', async () => {
    renderModal(true, true)
    fireEvent.click(screen.getByText('Vùng nguy hiểm'))

    await waitFor(() => {
      expect(screen.getByTestId('delete-workspace-btn')).toBeInTheDocument()
    })

    const input = screen.getByTestId('delete-workspace-input')
    fireEvent.change(input, { target: { value: 'Sai Tên' } })

    const deleteBtn = screen.getByTestId('delete-workspace-btn')
    expect(deleteBtn).toBeDisabled()
  })

  it('enables delete button and triggers onDelete when workspace name matches exactly', async () => {
    renderModal(true, true)
    fireEvent.click(screen.getByText('Vùng nguy hiểm'))

    await waitFor(() => {
      expect(screen.getByTestId('delete-workspace-input')).toBeInTheDocument()
    })

    const input = screen.getByTestId('delete-workspace-input')
    fireEvent.change(input, { target: { value: 'Không Gian Dự Án A' } })

    const deleteBtn = screen.getByTestId('delete-workspace-btn')
    expect(deleteBtn).not.toBeDisabled()

    // Click button to show popconfirm
    fireEvent.click(deleteBtn)

    // Confirm in popconfirm
    await waitFor(() => {
      expect(screen.getByText('Xác nhận xoá vĩnh viễn')).toBeInTheDocument()
    })
    fireEvent.click(screen.getByText('Xác nhận xoá vĩnh viễn'))

    await waitFor(() => {
      expect(onDelete).toHaveBeenCalled()
    })
  })
})
