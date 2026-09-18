import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useBoardTemplate } from '../useBoardTemplate'
import { boardTemplateApi } from '../../services/boardTemplateApi'
import type { BoardTemplateProposal } from '../../types/boardTemplate.types'

vi.mock('../../services/boardTemplateApi', () => ({
  boardTemplateApi: {
    generateTemplate: vi.fn(),
    confirmTemplate: vi.fn(),
  },
}))

describe('useBoardTemplate', () => {
  const wsId = 'ws-123'
  const mockProposal: BoardTemplateProposal = {
    summary: 'Phân tích dự án',
    boardName: 'Dự án Alpha',
    boardDescription: 'Mô tả',
    columns: [
      { name: 'To Do', isDone: false },
      { name: 'Done', isDone: true },
    ],
    tasks: [{ title: 'Task 1', columnName: 'To Do' }],
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('generates proposal and moves to preview state', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)

    const { result } = renderHook(() => useBoardTemplate(wsId))
    expect(result.current.status).toBe('idle')

    await act(async () => {
      await result.current.generate('Mô tả hệ thống đặt vé xem phim trực tuyến')
    })

    expect(result.current.status).toBe('preview')
    expect(result.current.proposal).toEqual(mockProposal)
  })

  it('updates proposal fields without losing unpatched fields', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)

    const { result } = renderHook(() => useBoardTemplate(wsId))

    await act(async () => {
      await result.current.generate('Mô tả')
    })

    act(() => {
      result.current.updateProposal({ boardName: 'Tên bảng mới được cập nhật' })
    })

    expect(result.current.proposal?.boardName).toBe('Tên bảng mới được cập nhật')
    expect(result.current.proposal?.columns).toEqual(mockProposal.columns)
    expect(result.current.proposal?.tasks).toEqual(mockProposal.tasks)
  })

  it('confirms proposal and transitions to created state', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    vi.mocked(boardTemplateApi.confirmTemplate).mockResolvedValueOnce({
      id: 'log-1',
      action: 'CreateBoardFromTemplate',
      entityType: 'Workspace',
      entityId: wsId,
      status: 'Pending',
      requestedByUserId: 'u-1',
      requestedByName: 'Admin',
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '',
      updatedAt: '',
    })

    const { result } = renderHook(() => useBoardTemplate(wsId))

    await act(async () => {
      await result.current.generate('Mô tả')
    })

    let actionLog: any
    await act(async () => {
      actionLog = await result.current.confirm()
    })

    expect(result.current.status).toBe('created')
    expect(actionLog.id).toBe('log-1')
  })

  it('sets error state on confirm error while preserving the proposal for retry', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    vi.mocked(boardTemplateApi.confirmTemplate).mockRejectedValueOnce({
      response: {
        status: 400,
        data: { error: 'Số cột vượt quá giới hạn 6' },
      },
    })

    const { result } = renderHook(() => useBoardTemplate(wsId))

    await act(async () => {
      await result.current.generate('Mô tả')
    })

    await act(async () => {
      await result.current.confirm()
    })

    expect(result.current.status).toBe('error')
    expect(result.current.error).toBe('Số cột vượt quá giới hạn 6')
    expect(result.current.proposal).toEqual(mockProposal)
  })

  it('resets state back to idle on reset()', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)

    const { result } = renderHook(() => useBoardTemplate(wsId))

    await act(async () => {
      await result.current.generate('Mô tả')
    })

    act(() => {
      result.current.reset()
    })

    expect(result.current.status).toBe('idle')
    expect(result.current.proposal).toBeNull()
  })
})
