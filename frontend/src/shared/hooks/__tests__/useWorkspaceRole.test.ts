import { describe, expect, it } from 'vitest'
import { resolveWorkspaceRole } from '../useWorkspaceRole'

describe('resolveWorkspaceRole (pure function)', () => {
  const sampleRows = [
    { id: 'ws-admin', role: 'Admin', ownerId: 'user-1' },
    { id: 'ws-manager', role: 'manager', ownerId: 'user-2' },
    { id: 'ws-member', role: 'MEMBER', ownerId: 'user-3' },
    { id: 'ws-legacy', role: 'Admin' }, // legacy without ownerId
  ]

  it('returns default null/false when workspaceId is empty or not found', () => {
    expect(resolveWorkspaceRole(sampleRows, '', 'user-1')).toEqual({
      role: null,
      isMember: false,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
    })

    expect(resolveWorkspaceRole(sampleRows, 'ws-unknown', 'user-1')).toEqual({
      role: null,
      isMember: false,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
    })
  })

  it('resolves Admin role correctly and sets isOwner when userId matches ownerId', () => {
    const result = resolveWorkspaceRole(sampleRows, 'ws-admin', 'user-1')
    expect(result.role).toBe('Admin')
    expect(result.isMember).toBe(true)
    expect(result.isManagerOrAdmin).toBe(true)
    expect(result.isAdmin).toBe(true)
    expect(result.isOwner).toBe(true)
  })

  it('resolves Manager role case-insensitively with isManagerOrAdmin=true and isAdmin=false', () => {
    const result = resolveWorkspaceRole(sampleRows, 'ws-manager', 'other-user')
    expect(result.role).toBe('Manager')
    expect(result.isMember).toBe(true)
    expect(result.isManagerOrAdmin).toBe(true)
    expect(result.isAdmin).toBe(false)
    expect(result.isOwner).toBe(false)
  })

  it('resolves Member role case-insensitively with isManagerOrAdmin=false', () => {
    const result = resolveWorkspaceRole(sampleRows, 'ws-member', 'user-3')
    expect(result.role).toBe('Member')
    expect(result.isMember).toBe(true)
    expect(result.isManagerOrAdmin).toBe(false)
    expect(result.isAdmin).toBe(false)
    expect(result.isOwner).toBe(true)
  })

  it('handles legacy workspace row lacking ownerId gracefully without throwing', () => {
    const result = resolveWorkspaceRole(sampleRows, 'ws-legacy', 'user-1')
    expect(result.role).toBe('Admin')
    expect(result.isMember).toBe(true)
    expect(result.isManagerOrAdmin).toBe(true)
    expect(result.isAdmin).toBe(true)
    expect(result.isOwner).toBe(false)
  })

  it('treats unknown role string safely as not a member', () => {
    const customRows = [{ id: 'ws-guest', role: 'Guest', ownerId: 'user-9' }]
    const result = resolveWorkspaceRole(customRows, 'ws-guest', 'user-9')
    expect(result.role).toBeNull()
    expect(result.isMember).toBe(false)
    expect(result.isManagerOrAdmin).toBe(false)
    expect(result.isAdmin).toBe(false)
    expect(result.isOwner).toBe(false)
  })
})
