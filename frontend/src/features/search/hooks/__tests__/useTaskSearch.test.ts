import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useTaskSearch } from '../useTaskSearch'
import { searchApi } from '../../services/searchApi'
import type { TaskSearchResponse } from '../../types/search.types'

vi.mock('../../services/searchApi', () => ({
  searchApi: {
    searchTasks: vi.fn(),
  },
}))

vi.mock('antd', async () => {
  const actual = await vi.importActual('antd')
  return {
    ...actual,
    message: {
      success: vi.fn(),
      error: vi.fn(),
    },
  }
})

describe('useTaskSearch hook', () => {
  const wsId = 'ws-123'

  beforeEach(() => {
    vi.clearAllMocks()
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('debounces search requests when typing query', async () => {
    const mockRes: TaskSearchResponse = {
      items: [],
      nextCursor: null,
      hasMore: false,
      hasQuery: true,
    }
    vi.mocked(searchApi.searchTasks).mockResolvedValue(mockRes)

    const { result } = renderHook(() => useTaskSearch(wsId))

    act(() => {
      result.current.updateFilters({ q: 'a' })
    })
    act(() => {
      vi.advanceTimersByTime(100)
    })
    act(() => {
      result.current.updateFilters({ q: 'ab' })
    })
    act(() => {
      vi.advanceTimersByTime(100)
    })
    act(() => {
      result.current.updateFilters({ q: 'abc' })
    })
    act(() => {
      vi.advanceTimersByTime(350)
    })

    await act(async () => {})

    // Only one search triggered with 'abc'
    expect(searchApi.searchTasks).toHaveBeenCalledTimes(1)
    expect(searchApi.searchTasks).toHaveBeenCalledWith(wsId, expect.objectContaining({ q: 'abc' }))
  })

  it('appends items when calling loadMore', async () => {
    const item1: any = { task: { id: 't-1', title: 'Task 1' } }
    const item2: any = { task: { id: 't-2', title: 'Task 2' } }

    vi.mocked(searchApi.searchTasks)
      .mockResolvedValueOnce({
        items: [item1],
        nextCursor: 'cur-page-2',
        hasMore: true,
        hasQuery: true,
      })
      .mockResolvedValueOnce({
        items: [item2],
        nextCursor: null,
        hasMore: false,
        hasQuery: true,
      })

    const { result } = renderHook(() => useTaskSearch(wsId, { q: 'test' }))

    act(() => {
      vi.advanceTimersByTime(350)
    })
    await act(async () => {})

    expect(result.current.items).toHaveLength(1)
    expect(result.current.hasMore).toBe(true)
    expect(result.current.nextCursor).toBe('cur-page-2')

    // Call loadMore
    await act(async () => {
      await result.current.loadMore()
    })

    expect(result.current.items).toHaveLength(2)
    expect(result.current.hasMore).toBe(false)
    expect(result.current.nextCursor).toBeNull()
  })

  it('resets items and filter state on reset()', async () => {
    const { result } = renderHook(() => useTaskSearch(wsId, { q: 'test', overdue: true }))

    act(() => {
      result.current.reset()
    })

    expect(result.current.filters).toEqual({})
    expect(result.current.items).toEqual([])
    expect(result.current.hasMore).toBe(false)
    expect(result.current.nextCursor).toBeNull()
  })
})
