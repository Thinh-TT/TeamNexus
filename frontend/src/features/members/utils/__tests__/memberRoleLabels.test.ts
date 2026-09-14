import { describe, expect, it } from 'vitest'
import {
  getMemberTypeLabel,
  getRoleLabel,
  getRoleTagColor,
  isMemberActionDisabled,
} from '../memberRoleLabels'
import type { WorkspaceMemberResponse } from '../../types/member.types'

describe('memberRoleLabels utils', () => {
  it('maps valid roles to Vietnamese labels and tag colors', () => {
    expect(getRoleLabel('Admin')).toBe('Quản trị viên')
    expect(getRoleLabel('Manager')).toBe('Quản lý')
    expect(getRoleLabel('Member')).toBe('Thành viên')

    expect(getRoleTagColor('Admin')).toBe('red')
    expect(getRoleTagColor('Manager')).toBe('gold')
    expect(getRoleTagColor('Member')).toBe('blue')
  })

  it('maps member type ai_agent and human to appropriate labels', () => {
    expect(getMemberTypeLabel('ai_agent')).toBe('AI Agent')
    expect(getMemberTypeLabel('human')).toBe('Người dùng')
    expect(getMemberTypeLabel(null)).toBe('Người dùng')
  })

  it('provides safe fallbacks for unexpected or unknown roles', () => {
    expect(getRoleLabel('CustomRole')).toBe('CustomRole')
    expect(getRoleLabel(null)).toBe('Thành viên')
    expect(getRoleTagColor('CustomRole')).toBe('default')
  })

  it('disables actions for workspace owner, self, or ai agent', () => {
    const ownerMember: WorkspaceMemberResponse = {
      userId: 'user-owner',
      displayName: 'Owner User',
      role: 'Admin',
      avatarUrl: null,
      memberType: 'human',
      email: 'owner@example.com',
      joinedAt: '2026-09-01T00:00:00Z',
      isOwner: true,
    }

    const selfMember: WorkspaceMemberResponse = {
      ...ownerMember,
      userId: 'user-me',
      isOwner: false,
    }

    const agentMember: WorkspaceMemberResponse = {
      ...ownerMember,
      userId: 'user-agent',
      memberType: 'ai_agent',
      isOwner: false,
    }

    const normalMember: WorkspaceMemberResponse = {
      ...ownerMember,
      userId: 'user-normal',
      isOwner: false,
    }

    expect(isMemberActionDisabled(ownerMember, 'user-other').disabled).toBe(true)
    expect(isMemberActionDisabled(selfMember, 'user-me').disabled).toBe(true)
    expect(isMemberActionDisabled(agentMember, 'user-other').disabled).toBe(true)
    expect(isMemberActionDisabled(normalMember, 'user-other').disabled).toBe(false)
  })
})
