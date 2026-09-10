import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { smartSetupApi } from '../../services/smartSetupApi'
import { aiActionApi } from '../../services/aiActionApi'
import { useSmartSetup } from '../useSmartSetup'
import type { SmartSetupProposal, WorkspaceMember } from '../../types/smartSetup.types'
import type { AiActionLog, AiActionLogDetail } from '../../types/aiAction.types'

vi.mock('../../services/smartSetupApi', () => ({
  smartSetupApi: {
    generateSmartSetup: vi.fn(),
    getWorkspaceMembers: vi.fn(),
  },
}))

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    confirmSmartSetup: vi.fn(),
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

describe('useSmartSetup', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('initializes with default idle state', () => {
    const { result } = renderHook(() => useSmartSetup())

    expect(result.current.status).toBe('idle')
    expect(result.current.description).toBe('')
    expect(result.current.summary).toBeNull()
    expect(result.current.tasks).toEqual([])
    expect(result.current.members).toEqual([])
    expect(result.current.actionLog).toBeNull()
    expect(result.current.error).toBeNull()
    expect(result.current.httpStatus).toBeNull()
  })

  it('fetchMembers fetches workspace members and updates state', async () => {
    const mockMembers: WorkspaceMember[] = [
      { userId: 'u1', displayName: 'Admin User', role: 'Admin', avatarUrl: null },
    ]
    vi.mocked(smartSetupApi.getWorkspaceMembers).mockResolvedValueOnce(mockMembers)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.fetchMembers('ws-1')
    })

    expect(smartSetupApi.getWorkspaceMembers).toHaveBeenCalledWith('ws-1')
    expect(result.current.members).toEqual(mockMembers)
  })

  it('generate validates empty description and sets error', async () => {
    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      const res = await result.current.generate('board-1', '   ')
      expect(res).toBeNull()
    })

    expect(result.current.status).toBe('error')
    expect(result.current.httpStatus).toBe(400)
    expect(result.current.error).toContain('Vui lòng nhập mô tả')
  })

  it('generate transitions to ready on successful AI proposal response', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Tóm tắt phân rã',
      tasks: [
        {
          title: 'Tạo bảng CSDL',
          description: 'Thiết kế schema',
          priority: 'High',
          labels: [{ labelId: null, name: 'database', exists: false }],
          assignee: { userId: 'u1', displayName: 'Thinh', matched: true },
        },
      ],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Thiết kế tính năng lưu trữ')
    })

    expect(result.current.status).toBe('ready')
    expect(result.current.summary).toBe('Tóm tắt phân rã')
    expect(result.current.tasks).toHaveLength(1)
    expect(result.current.tasks[0].title).toBe('Tạo bảng CSDL')
    expect(result.current.tasks[0].tempId).toBeDefined()
  })

  it('handles 403 Forbidden error appropriately', async () => {
    const axiosErr = {
      isAxiosError: true,
      response: {
        status: 403,
        data: { error: 'Requires Manager or Admin role in this workspace.' },
      },
    }
    vi.mocked(smartSetupApi.generateSmartSetup).mockRejectedValueOnce(axiosErr)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Mô tả hợp lệ')
    })

    expect(result.current.status).toBe('error')
    expect(result.current.httpStatus).toBe(403)
    expect(result.current.error).toContain('Manager')
  })

  it('handles 502 Bad Gateway error appropriately', async () => {
    const axiosErr = {
      isAxiosError: true,
      response: {
        status: 502,
        data: { error: 'DeepSeek service unavailable' },
      },
    }
    vi.mocked(smartSetupApi.generateSmartSetup).mockRejectedValueOnce(axiosErr)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Mô tả hợp lệ')
    })

    expect(result.current.status).toBe('error')
    expect(result.current.httpStatus).toBe(502)
    expect(result.current.error).toContain('DeepSeek')
  })

  it('allows updating, adding, and deleting proposed tasks', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: null,
      tasks: [
        {
          title: 'Task 1',
          description: null,
          priority: 'Medium',
          labels: [],
          assignee: null,
        },
      ],
    }
    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Mô tả')
    })

    const initialTempId = result.current.tasks[0].tempId

    // Update task
    act(() => {
      result.current.updateTask(initialTempId, {
        title: 'Task 1 Updated',
        priority: 'Urgent',
      })
    })
    expect(result.current.tasks[0].title).toBe('Task 1 Updated')
    expect(result.current.tasks[0].priority).toBe('Urgent')

    // Add label
    act(() => {
      result.current.addLabel(initialTempId, {
        labelId: 'l1',
        name: 'frontend',
        exists: true,
      })
    })
    expect(result.current.tasks[0].labels).toHaveLength(1)
    expect(result.current.tasks[0].labels[0].name).toBe('frontend')

    // Remove label
    act(() => {
      result.current.removeLabel(initialTempId, 0)
    })
    expect(result.current.tasks[0].labels).toHaveLength(0)

    // Add new task
    act(() => {
      result.current.addTask()
    })
    expect(result.current.tasks).toHaveLength(2)

    // Delete task
    act(() => {
      result.current.deleteTask(initialTempId)
    })
    expect(result.current.tasks).toHaveLength(1)
    expect(result.current.tasks[0].title).toBe('Công việc mới')
  })

  it('submitConfirm calls aiActionApi.confirmSmartSetup and transitions to pending state', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Tóm tắt 1',
      tasks: [
        {
          title: 'Task A',
          description: 'Desc A',
          priority: 'High',
          labels: [],
          assignee: null,
        },
      ],
    }
    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    const mockLog: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: 'Thinh',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }
    vi.mocked(aiActionApi.confirmSmartSetup).mockResolvedValueOnce(mockLog)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Mô tả tính năng')
    })

    expect(result.current.status).toBe('ready')

    let returnedLog: AiActionLog | null = null
    await act(async () => {
      returnedLog = await result.current.submitConfirm('board-1', 'col-1')
    })

    expect(aiActionApi.confirmSmartSetup).toHaveBeenCalledWith('board-1', {
      description: 'Mô tả tính năng',
      summary: 'Tóm tắt 1',
      tasks: [
        {
          title: 'Task A',
          description: 'Desc A',
          priority: 'High',
          labels: [],
          assignee: null,
        },
      ],
      columnId: 'col-1',
    })

    expect(returnedLog).toEqual(mockLog)
    expect(result.current.status).toBe('pending')
    expect(result.current.actionLog).toEqual(mockLog)
  })

  it('handles approveAction, rejectAction, undoAction on actionLog', async () => {
    const mockLog: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: 'Thinh',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }

    const mockApproved: AiActionLogDetail = {
      ...mockLog,
      status: 'Approved',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: {
        entityType: 'Board',
        entityId: 'board-1',
        createdTaskIds: ['t1'],
        createdLabelIds: [],
        warnings: [],
        appliedAt: '2026-09-10T08:05:00Z',
      },
      createdTaskIds: ['t1'],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce({
      summary: null,
      tasks: [{ title: 'T1', description: null, priority: null, labels: [], assignee: null }],
    })
    vi.mocked(aiActionApi.confirmSmartSetup).mockResolvedValueOnce(mockLog)
    vi.mocked(aiActionApi.approveAiAction).mockResolvedValueOnce(mockApproved)

    const { result } = renderHook(() => useSmartSetup())

    await act(async () => {
      await result.current.generate('board-1', 'Mô tả')
    })

    await act(async () => {
      await result.current.submitConfirm('board-1', null)
    })

    expect(result.current.actionLog?.status).toBe('Pending')

    // Approve
    await act(async () => {
      const updated = await result.current.approveAction()
      expect(updated?.status).toBe('Approved')
    })
    expect(result.current.actionLog?.status).toBe('Approved')
  })

  it('supports backToPrompt and reset state transitions', async () => {
    const { result } = renderHook(() => useSmartSetup())

    act(() => {
      result.current.setDescription('Mô tả thử nghiệm')
      result.current.backToPrompt()
    })
    expect(result.current.status).toBe('idle')
    expect(result.current.description).toBe('Mô tả thử nghiệm')

    act(() => {
      result.current.reset()
    })
    expect(result.current.status).toBe('idle')
    expect(result.current.description).toBe('')
    expect(result.current.tasks).toEqual([])
    expect(result.current.actionLog).toBeNull()
  })
})
