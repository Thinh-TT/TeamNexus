import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { aiActionApi } from '../aiActionApi'
import type {
  AiActionLog,
  AiActionLogDetail,
  ConfirmSmartSetupRequest,
} from '../../types/aiAction.types'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('aiActionApi', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('confirmSmartSetup calls POST /boards/{boardId}/smart-setup/confirm with payload', async () => {
    const mockPayload: ConfirmSmartSetupRequest = {
      description: 'Mô tả tính năng',
      summary: 'Tóm tắt',
      tasks: [
        {
          title: 'Task 1',
          description: null,
          priority: 'High',
          labels: [],
          assignee: null,
        },
      ],
      columnId: 'col-1',
    }

    const mockResponse: AiActionLog = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u-1',
      requestedByName: 'Thinh-TT',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockResponse })

    const result = await aiActionApi.confirmSmartSetup('board-1', mockPayload)

    expect(httpClient.post).toHaveBeenCalledWith(
      '/boards/board-1/smart-setup/confirm',
      mockPayload
    )
    expect(result).toEqual(mockResponse)
  })

  it('listAiActions calls GET /boards/{boardId}/ai-actions with query params', async () => {
    const mockLogs: AiActionLog[] = [
      {
        id: 'log-1',
        action: 'CreateSubtasks',
        entityType: 'Board',
        entityId: 'board-1',
        status: 'Pending',
        requestedByUserId: 'u-1',
        requestedByName: 'Thinh-TT',
        decidedByUserId: null,
        decidedByName: null,
        decidedAt: null,
        decisionNote: null,
        taskCount: 3,
        createdAt: '2026-09-10T08:00:00Z',
        updatedAt: '2026-09-10T08:00:00Z',
      },
    ]

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockLogs })

    const result = await aiActionApi.listAiActions('board-1', {
      status: 'Pending',
      take: 10,
    })

    expect(httpClient.get).toHaveBeenCalledWith('/boards/board-1/ai-actions', {
      params: { status: 'Pending', take: 10 },
    })
    expect(result).toEqual(mockLogs)
  })

  it('getAiAction calls GET /ai-actions/{logId}', async () => {
    const mockDetail: AiActionLogDetail = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Pending',
      requestedByUserId: 'u-1',
      requestedByName: 'Thinh-TT',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:00:00Z',
      basis: { boardId: 'board-1' },
      beforeSnapshot: null,
      afterSnapshot: {
        description: 'Mô tả',
        summary: null,
        columnId: null,
        tasks: [],
      },
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockDetail })

    const result = await aiActionApi.getAiAction('log-1')

    expect(httpClient.get).toHaveBeenCalledWith('/ai-actions/log-1')
    expect(result).toEqual(mockDetail)
  })

  it('approveAiAction calls POST /ai-actions/{logId}/approve', async () => {
    const mockDetail: AiActionLogDetail = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Approved',
      requestedByUserId: 'u-1',
      requestedByName: 'Thinh-TT',
      decidedByUserId: 'u-2',
      decidedByName: 'Manager-1',
      decidedAt: '2026-09-10T08:05:00Z',
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:05:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: {
        entityType: 'Board',
        entityId: 'board-1',
        createdTaskIds: ['task-1'],
        createdLabelIds: [],
        warnings: [],
        appliedAt: '2026-09-10T08:05:00Z',
      },
      createdTaskIds: ['task-1'],
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockDetail })

    const result = await aiActionApi.approveAiAction('log-1')

    expect(httpClient.post).toHaveBeenCalledWith('/ai-actions/log-1/approve')
    expect(result).toEqual(mockDetail)
  })

  it('rejectAiAction calls POST /ai-actions/{logId}/reject with note body', async () => {
    const mockDetail: AiActionLogDetail = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Rejected',
      requestedByUserId: 'u-1',
      requestedByName: 'Thinh-TT',
      decidedByUserId: 'u-2',
      decidedByName: 'Manager-1',
      decidedAt: '2026-09-10T08:05:00Z',
      decisionNote: 'Không phù hợp với sprint này',
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:05:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockDetail })

    const result = await aiActionApi.rejectAiAction(
      'log-1',
      'Không phù hợp với sprint này'
    )

    expect(httpClient.post).toHaveBeenCalledWith('/ai-actions/log-1/reject', {
      note: 'Không phù hợp với sprint này',
    })
    expect(result).toEqual(mockDetail)
  })

  it('rejectAiAction calls POST /ai-actions/{logId}/reject with empty body when note is omitted', async () => {
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { id: 'log-1' } })

    await aiActionApi.rejectAiAction('log-1')

    expect(httpClient.post).toHaveBeenCalledWith('/ai-actions/log-1/reject', {})
  })

  it('undoAiAction calls POST /ai-actions/{logId}/undo', async () => {
    const mockDetail: AiActionLogDetail = {
      id: 'log-1',
      action: 'CreateSubtasks',
      entityType: 'Board',
      entityId: 'board-1',
      status: 'Undone',
      requestedByUserId: 'u-1',
      requestedByName: 'Thinh-TT',
      decidedByUserId: 'u-2',
      decidedByName: 'Manager-1',
      decidedAt: '2026-09-10T08:10:00Z',
      decisionNote: null,
      taskCount: 1,
      createdAt: '2026-09-10T08:00:00Z',
      updatedAt: '2026-09-10T08:10:00Z',
      basis: null,
      beforeSnapshot: null,
      afterSnapshot: null,
      appliedSnapshot: null,
      createdTaskIds: [],
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockDetail })

    const result = await aiActionApi.undoAiAction('log-1')

    expect(httpClient.post).toHaveBeenCalledWith('/ai-actions/log-1/undo')
    expect(result).toEqual(mockDetail)
  })
})
