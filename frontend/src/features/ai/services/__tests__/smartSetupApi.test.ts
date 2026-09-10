import { afterEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { smartSetupApi } from '../smartSetupApi'
import type { SmartSetupProposal, WorkspaceMember } from '../../types/smartSetup.types'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

describe('smartSetupApi', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  it('generateSmartSetup calls POST /boards/{boardId}/smart-setup with description payload', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Tóm tắt phân rã công việc',
      tasks: [
        {
          title: 'Subtask 1',
          description: 'Chi tiết subtask',
          priority: 'High',
          labels: [{ labelId: null, name: 'backend', exists: false }],
          assignee: { userId: 'u1', displayName: 'Thinh', matched: true },
        },
      ],
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockProposal })

    const res = await smartSetupApi.generateSmartSetup('board-123', {
      description: 'Xây dựng chức năng bình luận',
    })

    expect(httpClient.post).toHaveBeenCalledWith('/boards/board-123/smart-setup', {
      description: 'Xây dựng chức năng bình luận',
    })
    expect(res).toEqual(mockProposal)
  })

  it('getWorkspaceMembers calls GET /workspaces/{workspaceId}/members', async () => {
    const mockMembers: WorkspaceMember[] = [
      { userId: 'u1', displayName: 'Thinh', role: 'Admin', avatarUrl: null },
      { userId: 'u2', displayName: 'An', role: 'Member', avatarUrl: 'https://example.com/avatar.jpg' },
    ]

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockMembers })

    const res = await smartSetupApi.getWorkspaceMembers('ws-123')

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-123/members')
    expect(res).toEqual(mockMembers)
  })
})
