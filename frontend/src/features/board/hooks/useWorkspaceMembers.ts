import { useCallback, useEffect, useState } from 'react'
import { boardApi } from '../services/boardApi'
import type { WorkspaceMemberResponse } from '../types/board.types'

interface UseWorkspaceMembersResult {
  members: WorkspaceMemberResponse[]
  loading: boolean
  error: string | null
  refetch: () => Promise<void>
}

// In-memory cache by workspaceId to avoid duplicate network requests across components
const membersCache = new Map<string, WorkspaceMemberResponse[]>()

export const useWorkspaceMembers = (workspaceId?: string): UseWorkspaceMembersResult => {
  const [members, setMembers] = useState<WorkspaceMemberResponse[]>(() => {
    return (workspaceId && membersCache.get(workspaceId)) || []
  })
  const [loading, setLoading] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const fetchMembers = useCallback(async () => {
    if (!workspaceId) {
      setMembers([])
      return
    }

    setLoading(true)
    setError(null)
    try {
      const data = await boardApi.getMembers(workspaceId)
      membersCache.set(workspaceId, data)
      setMembers(data)
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Không thể tải danh sách thành viên'
      setError(message)
    } finally {
      setLoading(false)
    }
  }, [workspaceId])

  useEffect(() => {
    if (!workspaceId) return

    Promise.resolve().then(() => {
      fetchMembers()
    })
  }, [workspaceId, fetchMembers])

  return {
    members,
    loading,
    error,
    refetch: fetchMembers,
  }
}
