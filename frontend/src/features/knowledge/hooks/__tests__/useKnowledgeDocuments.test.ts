import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useKnowledgeDocuments } from '../useKnowledgeDocuments'
import { knowledgeApi } from '../../services/knowledgeApi'
import type { KnowledgeDocument } from '../../types/knowledge.types'

vi.mock('../../services/knowledgeApi', () => ({
  knowledgeApi: {
    getDocuments: vi.fn(),
    createDocument: vi.fn(),
    deleteDocument: vi.fn(),
  },
}))

const mocked = vi.mocked(knowledgeApi)

const makeDocument = (overrides: Partial<KnowledgeDocument> = {}): KnowledgeDocument => ({
  id: 'doc-1',
  title: 'Quy trình bảo mật',
  sourceUrl: null,
  status: 'ready',
  chunkCount: 3,
  error: null,
  createdAt: '2026-09-01T00:00:00Z',
  updatedAt: '2026-09-01T00:00:00Z',
  ...overrides,
})

const actionLog = {
  id: 'log-1',
  action: 'CreateKnowledgeDocument',
  entityType: 'Workspace',
  entityId: 'ws-1',
  status: 'Pending' as const,
  requestedByUserId: 'u-1',
  requestedByName: null,
  decidedByUserId: null,
  decidedByName: null,
  decidedAt: null,
  decisionNote: null,
  taskCount: 0,
  createdAt: '',
  updatedAt: '',
}

describe('useKnowledgeDocuments (Phase 14.7 §B)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocked.getDocuments.mockResolvedValue({ enabled: true, items: [makeDocument()] })
  })

  it('loads the documents and the enabled flag on mount', async () => {
    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))

    await waitFor(() => expect(result.current.documents).toHaveLength(1))

    expect(mocked.getDocuments).toHaveBeenCalledWith('ws-1')
    expect(result.current.enabled).toBe(true)
    expect(result.current.loading).toBe(false)
    expect(result.current.error).toBeNull()
  })

  it('reports the feature as off and keeps no rows when the server says so', async () => {
    mocked.getDocuments.mockResolvedValue({ enabled: false, items: [] })

    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))

    // Waiting on `enabled` rather than on `loading`: `loading` STARTS false and the first fetch is deferred a
    // microtask, so a `waitFor` on it would pass before the response had been applied.
    await waitFor(() => expect(result.current.enabled).toBe(false))

    // `enabled: false` is `Jev:RagEnabled` being off, which the page must say out loud.
    expect(result.current.documents).toEqual([])
    // The empty list here is NOT an error: nothing went wrong, the feature is switched off.
    expect(result.current.error).toBeNull()
    expect(result.current.loading).toBe(false)
  })

  it('refetch replaces the list', async () => {
    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))
    await waitFor(() => expect(result.current.documents).toHaveLength(1))

    mocked.getDocuments.mockResolvedValue({
      enabled: true,
      items: [makeDocument({ id: 'doc-2', title: 'Tài liệu mới' })],
    })

    await act(async () => {
      await result.current.refetch()
    })

    expect(result.current.documents).toHaveLength(1)
    expect(result.current.documents[0].id).toBe('doc-2')
  })

  it('create hands back the pending action without inventing a row', async () => {
    mocked.createDocument.mockResolvedValue(actionLog)

    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))
    await waitFor(() => expect(result.current.documents).toHaveLength(1))

    let returned: unknown
    await act(async () => {
      returned = await result.current.create({
        title: 'Tài liệu mới',
        sourceUrl: null,
        content: 'Nội dung',
      })
    })

    expect(returned).toEqual(actionLog)
    // Nothing has been written yet, so the table must not gain a row.
    expect(result.current.documents).toHaveLength(1)
    expect(result.current.error).toBeNull()
  })

  it('surfaces a 400 create error VERBATIM and resolves null', async () => {
    const serverMessage = 'Bộ nhớ ngữ nghĩa đang tắt, không thể thêm tài liệu.'
    mocked.createDocument.mockRejectedValue({
      response: { status: 400, data: { error: serverMessage } },
    })

    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))
    await waitFor(() => expect(result.current.documents).toHaveLength(1))

    let returned: unknown
    await act(async () => {
      returned = await result.current.create({ title: 'x', sourceUrl: null, content: 'y' })
    })

    expect(returned).toBeNull()
    // Not a replacement sentence: the server's message names the rule that was broken.
    expect(result.current.error).toBe(serverMessage)
  })

  it('surfaces a 400 delete error verbatim too', async () => {
    const serverMessage = 'Không thể xoá tài liệu khi bộ nhớ ngữ nghĩa đang tắt.'
    mocked.deleteDocument.mockRejectedValue({
      response: { status: 400, data: { error: serverMessage } },
    })

    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))
    await waitFor(() => expect(result.current.documents).toHaveLength(1))

    let returned: unknown
    await act(async () => {
      returned = await result.current.remove('doc-1')
    })

    expect(returned).toBeNull()
    expect(mocked.deleteDocument).toHaveBeenCalledWith('ws-1', 'doc-1')
    expect(result.current.error).toBe(serverMessage)
    // A refused delete changes nothing on screen.
    expect(result.current.documents).toHaveLength(1)
  })

  it('reports a failed load without wiping the page state into an error-free blank', async () => {
    mocked.getDocuments.mockRejectedValue({
      response: { status: 500, data: { error: 'Máy chủ đang bận.' } },
    })

    const { result } = renderHook(() => useKnowledgeDocuments('ws-1'))

    await waitFor(() => expect(result.current.error).toBe('Máy chủ đang bận.'))
    expect(result.current.documents).toEqual([])
    expect(result.current.loading).toBe(false)
  })

  it('does not call the API without a workspace', async () => {
    const { result } = renderHook(() => useKnowledgeDocuments(undefined))

    await act(async () => {
      await result.current.refetch()
    })

    expect(mocked.getDocuments).not.toHaveBeenCalled()
    expect(result.current.documents).toEqual([])
  })
})
