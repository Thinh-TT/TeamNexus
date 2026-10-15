import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useJevTriage } from '../useJevTriage'
import { jevApi } from '../../services/jevApi'
import type { TaskTriageResponse } from '../../types/jev.types'

vi.mock('../../services/jevApi', () => ({
  jevApi: {
    triageTask: vi.fn(),
  },
}))

describe('useJevTriage', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  const mockResponse: TaskTriageResponse = {
    taskId: null,
    suggestedDomain: 'Frontend',
    suggestedWorkType: 'Feature',
    suggestedPriority: 'High',
    needsSecurityReview: false,
    confidenceScore: 0.92,
    routingAdvice: 'DirectAssign',
    options: [],
    degraded: false,
    latencyMs: 150,
  }

  it('does not trigger triage if title has fewer than 3 characters', () => {
    renderHook(() =>
      useJevTriage({
        boardId: 'b-1',
        title: 'ab',
      })
    )

    act(() => {
      vi.advanceTimersByTime(500)
    })

    expect(jevApi.triageTask).not.toHaveBeenCalled()
  })

  it('triggers triage after 400ms debounce when title length >= 3', async () => {
    vi.mocked(jevApi.triageTask).mockResolvedValue(mockResponse)

    const { result } = renderHook(() =>
      useJevTriage({
        boardId: 'b-1',
        title: 'Fix authentication token expiry bug',
      })
    )

    expect(jevApi.triageTask).not.toHaveBeenCalled()

    await act(async () => {
      vi.advanceTimersByTime(400)
    })

    expect(jevApi.triageTask).toHaveBeenCalledWith(
      'b-1',
      {
        title: 'Fix authentication token expiry bug',
        description: null,
      },
      expect.any(AbortSignal)
    )

    expect(result.current.triageData).toEqual(mockResponse)
    expect(result.current.isDegraded).toBe(false)
  })

  it('handles 503 by setting isJevDisabled to true', async () => {
    vi.mocked(jevApi.triageTask).mockRejectedValue({
      response: { status: 503, data: { message: 'Jev feature disabled' } },
    })

    const { result } = renderHook(() =>
      useJevTriage({
        boardId: 'b-1',
        title: 'Deploy microservice container',
      })
    )

    await act(async () => {
      vi.advanceTimersByTime(400)
    })

    expect(result.current.isJevDisabled).toBe(true)
    expect(result.current.triageData).toBeNull()
  })

  it('handles general errors by setting isDegraded and error message', async () => {
    vi.mocked(jevApi.triageTask).mockRejectedValue({
      response: { status: 500, data: { message: 'Inference timeout' } },
    })

    const { result } = renderHook(() =>
      useJevTriage({
        boardId: 'b-1',
        title: 'Build search query optimizer',
      })
    )

    await act(async () => {
      vi.advanceTimersByTime(400)
    })

    expect(result.current.isDegraded).toBe(true)
    expect(result.current.error).toBe('Inference timeout')
  })
})
