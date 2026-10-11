import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { aiActionApi } from '../../services/aiActionApi'
import { useAiActions } from '../useAiActions'
import type { AiActionLog } from '../../types/aiAction.types'

/**
 * Phase 14.5 §Track A (A1.1) — the hook's second scope.
 *
 * <p>The board-scoped behaviour is covered by `useAiActions.test.ts` and must stay untouched. This file
 * pins the workspace scope, which exists because a log that CREATES a board has no board to be scoped
 * to and therefore can never appear in a board-scoped list.</p>
 */

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    listAiActions: vi.fn(),
    listWorkspaceAiActions: vi.fn(),
    getAiAction: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

const log = (overrides: Partial<AiActionLog>): AiActionLog => ({
  id: 'log-1',
  action: 'CreateBoardFromTemplate',
  entityType: 'Workspace',
  entityId: 'ws-1',
  status: 'Pending',
  requestedByUserId: 'u1',
  requestedByName: 'Manager',
  decidedByUserId: null,
  decidedByName: null,
  decidedAt: null,
  decisionNote: null,
  taskCount: 5,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

describe('useAiActions — workspace scope', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('reads the workspace endpoint, not the board one', async () => {
    vi.mocked(aiActionApi.listWorkspaceAiActions).mockResolvedValue([log({})])

    const { result } = renderHook(() => useAiActions({ workspaceId: 'ws-1' }))

    await act(async () => {
      await result.current.reload()
    })

    expect(aiActionApi.listWorkspaceAiActions).toHaveBeenCalledWith('ws-1', {
      status: undefined,
      take: 50,
    })
    // The board endpoint must NOT be used: it cannot return a workspace-scoped log at all.
    expect(aiActionApi.listAiActions).not.toHaveBeenCalled()
    expect(result.current.logs).toHaveLength(1)
    expect(result.current.pendingCount).toBe(1)
  })

  it('passes the status filter through', async () => {
    vi.mocked(aiActionApi.listWorkspaceAiActions).mockResolvedValue([])

    const { result } = renderHook(() => useAiActions({ workspaceId: 'ws-1' }))

    await act(async () => {
      await result.current.reload('Approved')
    })

    expect(aiActionApi.listWorkspaceAiActions).toHaveBeenCalledWith('ws-1', {
      status: 'Approved',
      take: 50,
    })
  })

  it('returns an empty list and does not call the API when no scope is given', async () => {
    const { result } = renderHook(() => useAiActions({}))

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.logs).toEqual([])
    expect(aiActionApi.listWorkspaceAiActions).not.toHaveBeenCalled()
    expect(aiActionApi.listAiActions).not.toHaveBeenCalled()
  })

  it('surfaces a 403 as a Vietnamese permission message rather than crashing the drawer', async () => {
    vi.mocked(aiActionApi.listWorkspaceAiActions).mockRejectedValue({
      response: { status: 403, data: {} },
    })

    const { result } = renderHook(() => useAiActions({ workspaceId: 'ws-1' }))

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.httpStatus).toBe(403)
    expect(result.current.error).toContain('Manager hoặc Admin')
  })
})
