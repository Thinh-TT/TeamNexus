import { useCallback, useEffect, useState } from 'react'
import { message } from 'antd'
import { workspaceApi } from '../services/workspaceApi'
import type {
  TransferOwnershipRequest,
  UpdateWorkspaceRequest,
  WorkspaceDetail,
} from '../types/workspace.types'

export function useWorkspaceDetail(workspaceId?: string) {
  const [detail, setDetail] = useState<WorkspaceDetail | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    if (!workspaceId) return
    setLoading(true)
    setError(null)
    try {
      const data = await workspaceApi.get(workspaceId)
      setDetail(data)
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải thông tin workspace'
      setError(msg)
      setDetail(null)
    } finally {
      setLoading(false)
    }
  }, [workspaceId])

  useEffect(() => {
    Promise.resolve().then(() => {
      reload()
    })
  }, [reload])

  const save = async (data: UpdateWorkspaceRequest): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await workspaceApi.update(workspaceId, data)
      message.success('Cập nhật thông tin workspace thành công')
      await reload()
      return true
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Cập nhật workspace thất bại'
      message.error(msg)
      return false
    }
  }

  const transfer = async (data: TransferOwnershipRequest): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await workspaceApi.transferOwnership(workspaceId, data)
      message.success('Chuyển quyền sở hữu thành công')
      await reload()
      return true
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Chuyển quyền sở hữu thất bại'
      message.error(msg)
      return false
    }
  }

  const remove = async (): Promise<boolean> => {
    if (!workspaceId) return false
    try {
      await workspaceApi.deleteWorkspace(workspaceId)
      message.success('Xoá không gian làm việc thành công')
      return true
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Xoá workspace thất bại'
      message.error(msg)
      return false
    }
  }

  return {
    detail,
    loading,
    error,
    reload,
    save,
    transfer,
    remove,
  }
}
