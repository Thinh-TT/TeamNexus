import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import {
  clearPendingInviteToken,
  getPendingInviteToken,
  savePendingInviteToken,
} from '../pendingInvite'

describe('pendingInvite utility', () => {
  beforeEach(() => {
    sessionStorage.clear()
  })

  afterEach(() => {
    sessionStorage.clear()
  })

  it('saves, reads, and clears invite token in sessionStorage', () => {
    savePendingInviteToken('sample-token-123')
    expect(getPendingInviteToken()).toBe('sample-token-123')

    clearPendingInviteToken()
    expect(getPendingInviteToken()).toBeNull()
  })

  it('returns null when no invite token has been saved', () => {
    expect(getPendingInviteToken()).toBeNull()
  })

  it('safely handles empty string or whitespace without saving junk', () => {
    savePendingInviteToken('   ')
    expect(getPendingInviteToken()).toBeNull()
  })

  it('does not throw when sessionStorage operations encounter an error', () => {
    const originalSetItem = sessionStorage.setItem
    sessionStorage.setItem = () => {
      throw new Error('QuotaExceeded')
    }

    expect(() => savePendingInviteToken('test-token')).not.toThrow()
    expect(() => clearPendingInviteToken()).not.toThrow()

    sessionStorage.setItem = originalSetItem
  })
})
