import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { boardApi } from '../../services/boardApi'
import { useWorkspaceMembers } from '../useWorkspaceMembers'
import type { WorkspaceMemberResponse } from '../../types/board.types'

vi.mock('../../services/boardApi', () => ({
  boardApi: {
    getMembers: vi.fn(),
  },
}))

describe('useWorkspaceMembers', () => {
  const mockMembers: WorkspaceMemberResponse[] = [
    {
      userId: 'u-1',
      displayName: 'Alex Tran',
      role: 'Manager',
      avatarUrl: null,
      memberType: 'human',
    },
    {
      userId: 'agent-1',
      displayName: 'TeamNexus Agent',
      role: 'Member',
      avatarUrl: null,
      memberType: 'ai_agent',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('does nothing when workspaceId is undefined', async () => {
    const { result } = renderHook(() => useWorkspaceMembers(undefined))

    expect(result.current.members).toEqual([])
    expect(result.current.loading).toBe(false)
    expect(boardApi.getMembers).not.toHaveBeenCalled()
  })

  it('fetches and returns members for a workspaceId', async () => {
    vi.mocked(boardApi.getMembers).mockResolvedValueOnce(mockMembers)

    const { result } = renderHook(() => useWorkspaceMembers('ws-1'))

    await waitFor(() => {
      expect(result.current.members).toEqual(mockMembers)
    })

    expect(boardApi.getMembers).toHaveBeenCalledWith('ws-1')
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('handles API errors gracefully', async () => {
    vi.mocked(boardApi.getMembers).mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(() => useWorkspaceMembers('ws-error'))

    await waitFor(() => {
      expect(result.current.error).toBe('Network error')
    })

    expect(result.current.loading).toBe(false)
  })

  it('allows manual refetching of members', async () => {
    vi.mocked(boardApi.getMembers).mockResolvedValue(mockMembers)

    const { result } = renderHook(() => useWorkspaceMembers('ws-refetch'))

    await waitFor(() => {
      expect(result.current.members).toEqual(mockMembers)
    })

    const updatedMembers: WorkspaceMemberResponse[] = [
      ...mockMembers,
      {
        userId: 'u-2',
        displayName: 'New Member',
        role: 'Member',
        avatarUrl: null,
        memberType: 'human',
      },
    ]

    vi.mocked(boardApi.getMembers).mockResolvedValueOnce(updatedMembers)

    await result.current.refetch()

    await waitFor(() => {
      expect(result.current.members).toHaveLength(3)
    })
  })
})
