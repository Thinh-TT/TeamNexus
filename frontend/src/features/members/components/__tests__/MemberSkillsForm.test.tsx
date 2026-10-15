import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemberSkillsForm } from '../MemberSkillsForm'
import { jevApi } from '../../../ai/services/jevApi'
import type { WorkspaceMemberResponse } from '../../../board/types/board.types'

vi.mock('../../../ai/services/jevApi', () => ({
  jevApi: {
    updateMemberProfile: vi.fn(),
  },
}))

describe('MemberSkillsForm', () => {
  const onClose = vi.fn()
  const onSuccess = vi.fn()

  const mockMember: WorkspaceMemberResponse = {
    userId: 'u-1',
    displayName: 'Nguyễn Kỹ Sư',
    email: 'engineer@example.com',
    role: 'Member',
    memberType: 'human',
    skills: ['react', 'typescript'],
    primaryRole: 'Frontend Dev',
    capacityWeight: 1.0,
    maxWipLimit: 4,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders modal with preloaded skills and role data', () => {
    render(
      <MemberSkillsForm
        open={true}
        onClose={onClose}
        workspaceId="ws-1"
        member={mockMember}
        onSuccess={onSuccess}
      />
    )

    expect(screen.getByText(/Hồ sơ năng lực/i)).toBeInTheDocument()
    expect(screen.getByText(/Nguyễn Kỹ Sư/i)).toBeInTheDocument()
    expect(screen.getByText('react')).toBeInTheDocument()
    expect(screen.getByText('typescript')).toBeInTheDocument()
  })

  it('submits updated profile when save button clicked', async () => {
    vi.mocked(jevApi.updateMemberProfile).mockResolvedValue()

    render(
      <MemberSkillsForm
        open={true}
        onClose={onClose}
        workspaceId="ws-1"
        member={mockMember}
        onSuccess={onSuccess}
      />
    )

    const saveBtn = screen.getByRole('button', { name: /Lưu hồ sơ/i })
    fireEvent.click(saveBtn)

    await waitFor(() => {
      expect(jevApi.updateMemberProfile).toHaveBeenCalledWith('ws-1', 'u-1', {
        skills: ['react', 'typescript'],
        primaryRole: 'Frontend Dev',
        capacityWeight: 1.0,
        maxWipLimit: 4,
      })
      expect(onSuccess).toHaveBeenCalled()
      expect(onClose).toHaveBeenCalled()
    })
  })
})
