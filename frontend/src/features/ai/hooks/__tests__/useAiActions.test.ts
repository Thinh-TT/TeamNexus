import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { aiActionApi } from '../../services/aiActionApi'
import { useAiActions } from '../useAiActions'
import type {
  AiActionLog,
  AiActionLogDetail,
} from '../../types/aiAction.types'

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    listAiActions: vi.fn(),
    getAiAction: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

describe('useAiActions', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('initializes with default empty state', () => {
    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1' })
    )

    expect(result.current.logs).toEqual([])
    expect(result.current.pendingCount).toBe(0)
    expect(result.current.status).toBe('idle')
    expect(result.current.error).toBeNull()
    expect(result.current.httpStatus).toBeNull()
    expect(result.current.selectedLogDetail).toBeNull()
  })

  it('reload fetches logs and calculates pendingCount correctly', async () => {
    const mockLogs: AiActionLog[] = [
      {
        id: 'log-1',
        action: 'CreateSubtasks',
        entityType: 'Board',
        entityId: 'board-1',
        status: 'Pending',
        requestedByUserId: 'u1',
        requestedByName: 'User 1',
        decidedByUserId: null,
        decidedByName: null,
        decidedAt: null,
        decisionNote: null,
        taskCount: 3,
        createdAt: '2026-09-10T08:00:00Z',
        updatedAt: '2026-09-10T08:00:00Z',
      },
      {
        id: 'log-2',
        action: 'CreateSubtasks',
        entityType: 'Board',
        entityId: 'board-1',
        status: 'Approved',
        requestedByUserId: 'u1',
        requestedByName: 'User 1',
        decidedByUserId: 'u2',
        decidedByName: 'Manager',
        decidedAt: '2026-09-10T08:05:00Z',
        decisionNote: null,
        taskCount: 2,
        createdAt: '2026-09-10T07:00:00Z',
        updatedAt: '2026-09-10T08:05:00Z',
      },
    ]

    vi.mocked(aiActionApi.listAiActions).mockResolvedValueOnce(mockLogs)

    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1' })
    )

    await act(async () => {
      await result.current.reload()
    })

    expect(aiActionApi.listAiActions).toHaveBeenCalledWith('board-1', {
      status: undefined,
      take: 50,
    })
    expect(result.current.logs).toEqual(mockLogs)
    expect(result.current.pendingCount).toBe(1)
  })

  it('approve updates log status to Approved and triggers onBoardChanged callback', async () => {
    const onBoardChanged = vi.fn()
    const mockLog: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: 'User 1',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 2,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }

    const mockApprovedDetail: AiActionLogDetail = {
      ...mockLog,
      status: 'Approved',
      decidedByUserId: 'u2',
      decidedByName: 'Manager',
      decidedAt: '2026-09-10T08:05:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: {
        entityType: 'Board',
        entityId: 'board-1',
        createdTaskIds: ['t1', 't2'],
        createdLabelIds: [],
        warnings: [],
        appliedAt: '2026-09-10T08:05:00Z',
      },
      createdTaskIds: ['t1', 't2'],
    }

    vi.mocked(aiActionApi.listAiActions).mockResolvedValueOnce([mockLog])
    vi.mocked(aiActionApi.approveAiAction).mockResolvedValueOnce(mockApprovedDetail)

    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1', onBoardChanged })
    )

    await act(async () => {
      await result.current.reload()
    })

    expect(result.current.pendingCount).toBe(1)

    await act(async () => {
      const res = await result.current.approve('log-1')
      expect(res?.status).toBe('Approved')
    })

    expect(aiActionApi.approveAiAction).toHaveBeenCalledWith('log-1')
    expect(result.current.logs[0].status).toBe('Approved')
    expect(result.current.pendingCount).toBe(0)
    expect(onBoardChanged).toHaveBeenCalledTimes(1)
  })

  it('reject updates log status to Rejected with decision note', async () => {
    const mockLog: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: 'User 1',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 2,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }

    const mockRejectedDetail: AiActionLogDetail = {
      ...mockLog,
      status: 'Rejected',
      decidedByUserId: 'u2',
      decidedByName: 'Manager',
      decidedAt: '2026-09-10T08:05:00Z',
      decisionNote: 'Không duyệt trong sprint này',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    vi.mocked(aiActionApi.listAiActions).mockResolvedValueOnce([mockLog])
    vi.mocked(aiActionApi.rejectAiAction).mockResolvedValueOnce(mockRejectedDetail)

    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1' })
    )

    await act(async () => {
      await result.current.reload()
    })

    await act(async () => {
      const res = await result.current.reject('log-1', 'Không duyệt trong sprint này')
      expect(res?.status).toBe('Rejected')
    })

    expect(aiActionApi.rejectAiAction).toHaveBeenCalledWith(
      'log-1',
      'Không duyệt trong sprint này'
    )
    expect(result.current.logs[0].status).toBe('Rejected')
    expect(result.current.logs[0].decisionNote).toBe('Không duyệt trong sprint này')
  })

  it('undo updates log status to Undone and triggers onBoardChanged', async () => {
    const onBoardChanged = vi.fn()
    const mockLog: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Approved',
      requestedByUserId: 'u1',
      requestedByName: 'User 1',
      decidedByUserId: 'u2',
      decidedByName: 'Manager',
      decidedAt: '2026-09-10T08:05:00Z',
      decisionNote: null,
      taskCount: 2,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:05:00Z',
    }

    const mockUndoneDetail: AiActionLogDetail = {
      ...mockLog,
      status: 'Undone',
      decidedAt: '2026-09-10T08:10:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    vi.mocked(aiActionApi.listAiActions).mockResolvedValueOnce([mockLog])
    vi.mocked(aiActionApi.undoAiAction).mockResolvedValueOnce(mockUndoneDetail)

    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1', onBoardChanged })
    )

    await act(async () => {
      await result.current.reload()
    })

    await act(async () => {
      const res = await result.current.undo('log-1')
      expect(res?.status).toBe('Undone')
    })

    expect(aiActionApi.undoAiAction).toHaveBeenCalledWith('log-1')
    expect(result.current.logs[0].status).toBe('Undone')
    expect(onBoardChanged).toHaveBeenCalledTimes(1)
  })

  it('handles 409 Conflict error by setting error and triggering reload', async () => {
    const conflictErr = {
      isAxiosError: true,
      response: {
        status: 409,
        data: {
          error:
            'This AI action is no longer Pending (it was already decided elsewhere).',
        },
      },
    }

    vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])
    vi.mocked(aiActionApi.approveAiAction).mockRejectedValueOnce(conflictErr)

    const { result } = renderHook(() =>
      useAiActions({ boardId: 'board-1' })
    )

    await act(async () => {
      const res = await result.current.approve('log-1')
      expect(res).toBeNull()
    })

    expect(result.current.httpStatus).toBe(409)
    expect(result.current.error).toContain('no longer Pending')
    expect(aiActionApi.listAiActions).toHaveBeenCalledTimes(1)
  })
})
