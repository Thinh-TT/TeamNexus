import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { knowledgeApi } from '../knowledgeApi'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('knowledgeApi service (Phase 14.7 §B)', () => {
  const wsId = 'ws-100'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('getDocuments calls GET /workspaces/{workspaceId}/knowledge/documents and returns the envelope', async () => {
    const envelope = {
      enabled: true,
      items: [
        {
          id: 'doc-1',
          title: 'Quy trình bảo mật',
          sourceUrl: null,
          status: 'ready',
          chunkCount: 3,
          error: null,
          createdAt: '2026-09-01T00:00:00Z',
          updatedAt: '2026-09-01T00:00:00Z',
        },
      ],
    }

    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: envelope })

    const result = await knowledgeApi.getDocuments(wsId)

    expect(httpClient.get).toHaveBeenCalledWith(`/workspaces/${wsId}/knowledge/documents`)
    // The WHOLE envelope matters: `enabled: false` is a state of the feature, not an empty list.
    expect(result).toEqual(envelope)
  })

  it('createDocument posts the request body verbatim and returns the AI action log', async () => {
    const actionLog = { id: 'log-1', action: 'CreateKnowledgeDocument', status: 'Pending' }
    vi.mocked(httpClient.post).mockResolvedValueOnce({ data: actionLog })

    const request = {
      title: 'Quy trình bảo mật',
      sourceUrl: 'https://example.com/security',
      content: 'Nội dung tài liệu',
    }

    const result = await knowledgeApi.createDocument(wsId, request)

    expect(httpClient.post).toHaveBeenCalledWith(
      `/workspaces/${wsId}/knowledge/documents`,
      request
    )
    // An action, NOT a document: the caller must not treat this as a created row.
    expect(result).toEqual(actionLog)
  })

  it('createDocument propagates a 400 with the server message untouched', async () => {
    const error400 = {
      response: { status: 400, data: { error: 'Bộ nhớ ngữ nghĩa đang tắt, không thể thêm tài liệu.' } },
    }
    vi.mocked(httpClient.post).mockRejectedValueOnce(error400)

    await expect(
      knowledgeApi.createDocument(wsId, { title: 'x', sourceUrl: null, content: 'y' })
    ).rejects.toEqual(error400)
  })

  it('deleteDocument calls DELETE /workspaces/{workspaceId}/knowledge/documents/{documentId}', async () => {
    const actionLog = { id: 'log-2', action: 'DeleteKnowledgeDocument', status: 'Pending' }
    vi.mocked(httpClient.delete).mockResolvedValueOnce({ data: actionLog })

    const result = await knowledgeApi.deleteDocument(wsId, 'doc-9')

    expect(httpClient.delete).toHaveBeenCalledWith(
      `/workspaces/${wsId}/knowledge/documents/doc-9`
    )
    expect(result).toEqual(actionLog)
  })
})
