import { httpClient } from '../../../shared/api/httpClient'
import type {
  GetWorkspaceActivityParams,
  TransferOwnershipRequest,
  UpdateWorkspaceRequest,
  WorkspaceActivityPage,
  WorkspaceDetail,
  WorkspaceSummary,
} from '../types/workspace.types'

export const workspaceApi = {
  /** Lấy danh sách workspace người dùng tham gia */
  list: async (): Promise<WorkspaceSummary[]> => {
    const res = await httpClient.get<WorkspaceSummary[]>('/workspaces')
    return res.data
  },

  /** Lấy chi tiết workspace theo id */
  get: async (workspaceId: string): Promise<WorkspaceDetail> => {
    const res = await httpClient.get<WorkspaceDetail>(`/workspaces/${workspaceId}`)
    return res.data
  },

  /** Cập nhật tên và mô tả workspace (Manager+) */
  update: async (
    workspaceId: string,
    data: UpdateWorkspaceRequest
  ): Promise<void> => {
    await httpClient.put(`/workspaces/${workspaceId}`, data)
  },

  /** Chuyển quyền sở hữu workspace cho thành viên khác (owner/Admin) */
  transferOwnership: async (
    workspaceId: string,
    data: TransferOwnershipRequest
  ): Promise<void> => {
    await httpClient.put(`/workspaces/${workspaceId}/owner`, data)
  },

  /** Xoá mềm workspace (owner/Admin) */
  deleteWorkspace: async (workspaceId: string): Promise<void> => {
    await httpClient.delete(`/workspaces/${workspaceId}`)
  },

  /** Lấy lịch sử hoạt động workspace phân trang keyset (Manager+) */
  getActivity: async (
    workspaceId: string,
    params?: GetWorkspaceActivityParams
  ): Promise<WorkspaceActivityPage> => {
    const query = new URLSearchParams()
    if (params) {
      if (params.boardId !== undefined && params.boardId !== '') {
        query.append('boardId', params.boardId)
      }
      if (params.entityType !== undefined && params.entityType !== '') {
        query.append('entityType', params.entityType)
      }
      if (params.action !== undefined && params.action !== '') {
        query.append('action', params.action)
      }
      if (params.take !== undefined) {
        query.append('take', params.take.toString())
      }
      if (params.before !== undefined && params.before !== '') {
        query.append('before', params.before)
      }
    }

    const queryString = query.toString()
    const url = queryString
      ? `/workspaces/${workspaceId}/activity?${queryString}`
      : `/workspaces/${workspaceId}/activity`

    const res = await httpClient.get<WorkspaceActivityPage>(url)
    return res.data
  },
}
