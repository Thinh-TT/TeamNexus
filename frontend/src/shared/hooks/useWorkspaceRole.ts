import { useCallback, useEffect, useMemo, useState } from 'react'
import { httpClient } from '../api'
import { useAuth } from '../../features/auth/hooks/useAuth'

export type WorkspaceRole = 'Admin' | 'Manager' | 'Member'

export interface WorkspaceRoleState {
  role: WorkspaceRole | null
  isMember: boolean
  isManagerOrAdmin: boolean
  isAdmin: boolean
  isOwner: boolean
}

/** HÀM THUẦN — test không cần render. */
export function resolveWorkspaceRole(
  rows: Array<{ id: string; role: string; ownerId?: string }>,
  workspaceId: string,
  userId?: string | null
): WorkspaceRoleState {
  if (!workspaceId) {
    return {
      role: null,
      isMember: false,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
    }
  }

  const currentWs = rows.find((w) => w.id === workspaceId)
  if (!currentWs) {
    return {
      role: null,
      isMember: false,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
    }
  }

  const roleStr = currentWs.role?.toLowerCase()
  let role: WorkspaceRole | null = null
  if (roleStr === 'admin') role = 'Admin'
  else if (roleStr === 'manager') role = 'Manager'
  else if (roleStr === 'member') role = 'Member'

  const isMember = role !== null
  const isManagerOrAdmin = role === 'Manager' || role === 'Admin'
  const isAdmin = role === 'Admin'
  const isOwner = Boolean(isMember && userId && currentWs.ownerId === userId)

  return {
    role,
    isMember,
    isManagerOrAdmin,
    isAdmin,
    isOwner,
  }
}

export function useWorkspaceRole(workspaceId?: string): WorkspaceRoleState & {
  loading: boolean
  reload: () => void
} {
  const { user } = useAuth()
  const [loading, setLoading] = useState(true)
  const [rows, setRows] = useState<Array<{ id: string; role: string; ownerId?: string }>>([])
  const [tick, setTick] = useState(0)

  const reload = useCallback(() => {
    setTick((t) => t + 1)
  }, [])

  useEffect(() => {
    let ignore = false
    Promise.resolve().then(() => {
      if (!ignore) setLoading(true)
    })

    httpClient
      .get<Array<{ id: string; role: string; ownerId?: string }>>('/workspaces')
      .then((res) => {
        if (!ignore && res.data) {
          setRows(res.data)
        }
      })
      .catch(() => {
        if (!ignore) {
          setRows([])
        }
      })
      .finally(() => {
        if (!ignore) {
          setLoading(false)
        }
      })

    return () => {
      ignore = true
    }
  }, [tick])

  const roleState = useMemo(
    () => resolveWorkspaceRole(rows, workspaceId ?? '', user?.id),
    [rows, workspaceId, user?.id]
  )

  return {
    ...roleState,
    loading,
    reload,
  }
}
