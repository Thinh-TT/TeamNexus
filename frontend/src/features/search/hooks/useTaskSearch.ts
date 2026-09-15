import { useCallback, useEffect, useRef, useState } from 'react'
import { message } from 'antd'
import { searchApi } from '../services/searchApi'
import type { TaskSearchFilters, TaskSearchItem, TaskSearchResponse } from '../types/search.types'

export interface UseTaskSearchResult {
  filters: TaskSearchFilters
  setFilters: React.Dispatch<React.SetStateAction<TaskSearchFilters>>
  updateFilters: (updates: Partial<TaskSearchFilters>) => void
  items: TaskSearchItem[]
  nextCursor: string | null
  hasMore: boolean
  hasQuery: boolean
  loading: boolean
  loadingMore: boolean
  error: string | null
  loadMore: () => Promise<void>
  reset: () => void
  reload: () => Promise<void>
}

export function useTaskSearch(
  workspaceId: string,
  initialFilters: TaskSearchFilters = {}
): UseTaskSearchResult {
  const [filters, setFilters] = useState<TaskSearchFilters>(initialFilters)
  const [items, setItems] = useState<TaskSearchItem[]>([])
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [hasMore, setHasMore] = useState<boolean>(false)
  const [hasQuery, setHasQuery] = useState<boolean>(false)
  const [loading, setLoading] = useState<boolean>(false)
  const [loadingMore, setLoadingMore] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const activeRequestIdRef = useRef<number>(0)

  const executeSearch = useCallback(
    async (searchFilters: TaskSearchFilters) => {
      if (!workspaceId) return
      const requestId = ++activeRequestIdRef.current
      setLoading(true)
      setError(null)

      try {
        const res: TaskSearchResponse = await searchApi.searchTasks(workspaceId, {
          ...searchFilters,
          cursor: undefined, // Fresh search always starts from page 1
        })

        if (requestId === activeRequestIdRef.current) {
          setItems(res.items)
          setNextCursor(res.nextCursor)
          setHasMore(res.hasMore)
          setHasQuery(res.hasQuery)
        }
      } catch (err: unknown) {
        if (requestId === activeRequestIdRef.current) {
          const msg =
            (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
            'Không thể thực hiện tìm kiếm'
          setError(msg)
          message.error(msg)
        }
      } finally {
        if (requestId === activeRequestIdRef.current) {
          setLoading(false)
        }
      }
    },
    [workspaceId]
  )

  useEffect(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current)
    }

    debounceTimerRef.current = setTimeout(() => {
      executeSearch(filters)
    }, 300)

    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current)
      }
    }
  }, [executeSearch, filters])

  const loadMore = useCallback(async () => {
    if (!workspaceId || !nextCursor || loadingMore || loading) return

    setLoadingMore(true)
    try {
      const res = await searchApi.searchTasks(workspaceId, {
        ...filters,
        cursor: nextCursor,
      })
      setItems((prev) => [...prev, ...res.items])
      setNextCursor(res.nextCursor)
      setHasMore(res.hasMore)
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải thêm kết quả'
      message.error(msg)
    } finally {
      setLoadingMore(false)
    }
  }, [filters, loading, loadingMore, nextCursor, workspaceId])

  const updateFilters = useCallback((updates: Partial<TaskSearchFilters>) => {
    setFilters((prev) => ({ ...prev, ...updates }))
  }, [])

  const reset = useCallback(() => {
    setFilters({})
    setItems([])
    setNextCursor(null)
    setHasMore(false)
    setHasQuery(false)
  }, [])

  const reload = useCallback(async () => {
    await executeSearch(filters)
  }, [executeSearch, filters])

  return {
    filters,
    setFilters,
    updateFilters,
    items,
    nextCursor,
    hasMore,
    hasQuery,
    loading,
    loadingMore,
    error,
    loadMore,
    reset,
    reload,
  }
}
