import { useCallback, useEffect, useState } from 'react'
import { message } from 'antd'
import { workspaceApi } from '../services/workspaceApi'
import type {
  GetWorkspaceActivityParams,
  WorkspaceActivityItem,
} from '../types/workspace.types'

export function useWorkspaceActivity(
  workspaceId?: string,
  filterParams?: Omit<GetWorkspaceActivityParams, 'take' | 'before'>
) {
  const [items, setItems] = useState<WorkspaceActivityItem[]>([])
  const [hasMore, setHasMore] = useState(false)
  const [nextCursor, setNextCursor] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)

  const entityType = filterParams?.entityType
  const boardId = filterParams?.boardId

  const reload = useCallback(async () => {
    if (!workspaceId) return
    setLoading(true)
    try {
      const page = await workspaceApi.getActivity(workspaceId, {
        entityType,
        boardId,
        take: 50,
      })
      setItems(page.items)
      setHasMore(page.hasMore)
      setNextCursor(page.nextCursor)
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải lịch sử hoạt động'
      message.error(msg)
      setItems([])
      setHasMore(false)
      setNextCursor(null)
    } finally {
      setLoading(false)
    }
  }, [workspaceId, entityType, boardId])

  useEffect(() => {
    Promise.resolve().then(() => {
      reload()
    })
  }, [reload])

  const loadMore = async () => {
    if (!workspaceId || !nextCursor || loadingMore) return
    setLoadingMore(true)
    try {
      const page = await workspaceApi.getActivity(workspaceId, {
        entityType,
        boardId,
        take: 50,
        before: nextCursor,
      })
      setItems((prev) => [...prev, ...page.items])
      setHasMore(page.hasMore)
      setNextCursor(page.nextCursor)
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải thêm hoạt động'
      message.error(msg)
    } finally {
      setLoadingMore(false)
    }
  }

  return {
    items,
    hasMore,
    nextCursor,
    loading,
    loadingMore,
    loadMore,
    reload,
  }
}
