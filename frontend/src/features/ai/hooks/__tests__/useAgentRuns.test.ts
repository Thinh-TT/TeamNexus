import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { agentApi } from '../../services/agentApi'
import { useAgentRuns } from '../useAgentRuns'
import type { AgentRunDetailResponse, AgentRunResponse } from '../../types/agentRun.types'

vi.mock('../../services/agentApi', () => ({
  agentApi: {
    startRun: vi.fn(),
    rerun: vi.fn(),
    listRuns: vi.fn(),
    getRunDetail: vi.fn(),
    cancelRun: vi.fn(),
  },
}))

describe('useAgentRuns', () => {
  const mockRun: AgentRunResponse = {
    id: 'run-1',
    taskId: 'task-1',
    boardId: 'board-1',
    agentUserId: 'agent-1',
    agentDisplayName: 'TeamNexus Agent',
    triggeredByUserId: 'user-1',
    triggeredByName: 'Manager User',
    status: 'Running',
    stopReason: null,
    clarificationQuestion: null,
    aiActionLogId: null,
    outputKind: null,
    error: null,
    toolCallCount: 2,
    llmCallCount: 1,
    promptTokens: 100,
    completionTokens: 50,
    totalTokens: 150,
    startedAt: '2026-09-12T00:00:00Z',
    finishedAt: null,
    traceTruncated: false,
  }

  const mockDetail: AgentRunDetailResponse = {
    run: mockRun,
    toolCallTrace: [
      {
        name: 'SearchSystemData',
        arguments: '{"query":"tasks"}',
        resultSummary: 'Found 3 tasks',
        isError: false,
        at: '2026-09-12T00:00:01Z',
        durationMs: 45,
      },
    ],
    previousRunId: null,
    previousQuestion: null,
    resolutionCommentContent: null,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('fetches list of runs and detail for latest run on mount', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValueOnce([mockRun])
    vi.mocked(agentApi.getRunDetail).mockResolvedValueOnce(mockDetail)

    const { result } = renderHook(() => useAgentRuns({ taskId: 'task-1' }))

    await waitFor(() => {
      expect(result.current.runs).toEqual([mockRun])
      expect(result.current.currentRun).toEqual(mockRun)
      expect(result.current.runDetail).toEqual(mockDetail)
    })

    expect(agentApi.listRuns).toHaveBeenCalledWith('task-1', 20)
    expect(agentApi.getRunDetail).toHaveBeenCalledWith('run-1')
  })

  it('handles startRun and prepends to run list', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValueOnce([])
    vi.mocked(agentApi.startRun).mockResolvedValueOnce(mockRun)
    vi.mocked(agentApi.getRunDetail).mockResolvedValueOnce(mockDetail)

    const { result } = renderHook(() => useAgentRuns({ taskId: 'task-1' }))

    await waitFor(() => {
      expect(result.current.loading).toBe(false)
    })

    let newRun: AgentRunResponse | null = null
    await act(async () => {
      newRun = await result.current.startRun()
    })

    expect(newRun).toEqual(mockRun)
    expect(result.current.currentRun).toEqual(mockRun)
    expect(result.current.runs).toContainEqual(mockRun)
  })

  it('handles rerun action', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValueOnce([mockRun])
    vi.mocked(agentApi.getRunDetail).mockResolvedValueOnce(mockDetail)

    const rerunResult: AgentRunResponse = {
      ...mockRun,
      id: 'run-2',
      status: 'Running',
    }
    vi.mocked(agentApi.rerun).mockResolvedValueOnce(rerunResult)

    const { result } = renderHook(() => useAgentRuns({ taskId: 'task-1' }))

    await waitFor(() => {
      expect(result.current.currentRun).toBeDefined()
    })

    await act(async () => {
      const res = await result.current.rerun('run-1')
      expect(res).toEqual(rerunResult)
    })

    expect(result.current.currentRun?.id).toBe('run-2')
  })

  it('handles cancelRun action', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValueOnce([mockRun])
    vi.mocked(agentApi.getRunDetail).mockResolvedValueOnce(mockDetail)

    const cancelledDetail: AgentRunDetailResponse = {
      ...mockDetail,
      run: {
        ...mockRun,
        status: 'Failed',
        stopReason: 'Cancelled',
      },
    }
    vi.mocked(agentApi.cancelRun).mockResolvedValueOnce(cancelledDetail)

    const { result } = renderHook(() => useAgentRuns({ taskId: 'task-1' }))

    await waitFor(() => {
      expect(result.current.currentRun).toBeDefined()
    })

    await act(async () => {
      const res = await result.current.cancelRun('run-1')
      expect(res?.run.status).toBe('Failed')
      expect(res?.run.stopReason).toBe('Cancelled')
    })
  })

  it('updates currentRun on receiving SignalR progress event', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValueOnce([mockRun])
    vi.mocked(agentApi.getRunDetail).mockResolvedValueOnce(mockDetail)

    const { result } = renderHook(() => useAgentRuns({ taskId: 'task-1' }))

    await waitFor(() => {
      expect(result.current.currentRun).toBeDefined()
    })

    act(() => {
      result.current.handleProgressEvent({
        runId: 'run-1',
        taskId: 'task-1',
        boardId: 'board-1',
        status: 'AwaitingClarification',
        stopReason: 'QuestionAsked',
        toolCallCount: 5,
        totalTokens: 1200,
        clarificationQuestion: 'Bạn muốn export theo định dạng nào?',
      })
    })

    expect(result.current.currentRun?.status).toBe('AwaitingClarification')
    expect(result.current.currentRun?.clarificationQuestion).toBe(
      'Bạn muốn export theo định dạng nào?'
    )
    expect(result.current.currentRun?.toolCallCount).toBe(5)
    expect(result.current.currentRun?.totalTokens).toBe(1200)
  })
})
