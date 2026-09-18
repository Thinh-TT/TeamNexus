import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { boardTemplateApi } from '../boardTemplateApi'
import type { BoardTemplateProposal } from '../../types/boardTemplate.types'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    post: vi.fn(),
  },
}))

describe('boardTemplateApi', () => {
  const wsId = 'ws-100'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('generateTemplate sends POST to /workspaces/{id}/smart-setup/template with { description }', async () => {
    const mockProposal: BoardTemplateProposal = {
      summary: 'Tóm tắt',
      boardName: 'Dự án Thương mại điện tử',
      boardDescription: 'Mô tả bảng',
      columns: [
        { name: 'Cần làm', isDone: false },
        { name: 'Hoàn thành', isDone: true },
      ],
      tasks: [
        { title: 'Task 1', columnName: 'Cần làm' },
      ],
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockProposal })

    const result = await boardTemplateApi.generateTemplate(wsId, 'Xây dựng website bán hàng trực tuyến')

    expect(httpClient.post).toHaveBeenCalledWith(
      '/workspaces/ws-100/smart-setup/template',
      { description: 'Xây dựng website bán hàng trực tuyến' }
    )
    expect(result).toEqual(mockProposal)
  })

  it('confirmTemplate sends POST to /workspaces/{id}/smart-setup/template/confirm with { proposal }', async () => {
    const mockProposal: BoardTemplateProposal = {
      boardName: 'Bảng mẫu',
      columns: [{ name: 'Done', isDone: true }],
      tasks: [{ title: 'Task 1', columnName: 'Done' }],
    }

    const mockActionLog = {
      id: 'log-1',
      action: 'CreateBoardFromTemplate',
      entityType: 'Workspace',
      entityId: wsId,
      status: 'Pending',
    }

    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockActionLog })

    const result = await boardTemplateApi.confirmTemplate(wsId, mockProposal)

    expect(httpClient.post).toHaveBeenCalledWith(
      '/workspaces/ws-100/smart-setup/template/confirm',
      { proposal: mockProposal }
    )
    expect(result).toEqual(mockActionLog)
  })

  it('propagates HTTP error status when API fails', async () => {
    vi.mocked(httpClient.post).mockRejectedValueOnce({
      response: {
        status: 403,
        data: { error: 'Chỉ Manager hoặc Admin mới có quyền tạo mẫu' },
      },
    })

    await expect(
      boardTemplateApi.generateTemplate(wsId, 'Mô tả bất kỳ')
    ).rejects.toMatchObject({
      response: { status: 403 },
    })
  })
})
