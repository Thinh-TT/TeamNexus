import { afterEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { boardApi } from '../boardApi'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

describe('boardApi', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  describe('Boards', () => {
    it('getBoards calls GET /workspaces/{wsId}/boards', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [{ id: 'b1' }] })
      const res = await boardApi.getBoards('ws-1')
      expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/boards')
      expect(res).toEqual([{ id: 'b1' }])
    })

    it('getBoard calls GET /workspaces/{wsId}/boards/{boardId}', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: { id: 'b1' } })
      const res = await boardApi.getBoard('ws-1', 'b1')
      expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/boards/b1')
      expect(res).toEqual({ id: 'b1' })
    })

    it('createBoard calls POST /workspaces/{wsId}/boards', async () => {
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { id: 'b1', name: 'New Board' } })
      const res = await boardApi.createBoard('ws-1', { name: 'New Board' })
      expect(httpClient.post).toHaveBeenCalledWith('/workspaces/ws-1/boards', { name: 'New Board' })
      expect(res.name).toBe('New Board')
    })

    it('updateBoard calls PUT /workspaces/{wsId}/boards/{boardId}', async () => {
      vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { id: 'b1', name: 'Updated' } })
      const res = await boardApi.updateBoard('ws-1', 'b1', { name: 'Updated' })
      expect(httpClient.put).toHaveBeenCalledWith('/workspaces/ws-1/boards/b1', { name: 'Updated' })
      expect(res.name).toBe('Updated')
    })

    it('deleteBoard calls DELETE /workspaces/{wsId}/boards/{boardId}', async () => {
      vi.mocked(httpClient.delete).mockResolvedValueOnce({})
      await boardApi.deleteBoard('ws-1', 'b1')
      expect(httpClient.delete).toHaveBeenCalledWith('/workspaces/ws-1/boards/b1')
    })
  })

  describe('Columns', () => {
    it('getColumns calls GET /boards/{boardId}/columns', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [] })
      await boardApi.getColumns('b1')
      expect(httpClient.get).toHaveBeenCalledWith('/boards/b1/columns')
    })

    it('createColumn calls POST /boards/{boardId}/columns', async () => {
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { id: 'c1' } })
      await boardApi.createColumn('b1', { name: 'To Do', isDone: false })
      expect(httpClient.post).toHaveBeenCalledWith('/boards/b1/columns', { name: 'To Do', isDone: false })
    })

    it('updateColumn calls PUT /boards/{boardId}/columns/{colId}', async () => {
      vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { id: 'c1', name: 'Doing' } })
      await boardApi.updateColumn('b1', 'c1', { name: 'Doing' })
      expect(httpClient.put).toHaveBeenCalledWith('/boards/b1/columns/c1', { name: 'Doing' })
    })

    it('reorderColumns calls PUT /boards/{boardId}/columns/reorder', async () => {
      vi.mocked(httpClient.put).mockResolvedValueOnce({})
      await boardApi.reorderColumns('b1', { items: [{ id: 'c1', position: 0 }] })
      expect(httpClient.put).toHaveBeenCalledWith('/boards/b1/columns/reorder', {
        items: [{ id: 'c1', position: 0 }],
      })
    })

    it('deleteColumn calls DELETE /boards/{boardId}/columns/{colId}', async () => {
      vi.mocked(httpClient.delete).mockResolvedValueOnce({})
      await boardApi.deleteColumn('b1', 'c1')
      expect(httpClient.delete).toHaveBeenCalledWith('/boards/b1/columns/c1')
    })
  })

  describe('Tasks', () => {
    it('getTasks calls GET /boards/{boardId}/tasks with optional column filter', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [] })
      await boardApi.getTasks('b1', 'c1')
      expect(httpClient.get).toHaveBeenCalledWith('/boards/b1/tasks', { params: { columnId: 'c1' } })
    })

    it('moveTask calls PUT /boards/{boardId}/tasks/{taskId}/move', async () => {
      vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { id: 't1' } })
      await boardApi.moveTask('b1', 't1', { columnId: 'c2', position: 1 })
      expect(httpClient.put).toHaveBeenCalledWith('/boards/b1/tasks/t1/move', {
        columnId: 'c2',
        position: 1,
      })
    })

    it('deleteTask calls DELETE /boards/{boardId}/tasks/{taskId}', async () => {
      vi.mocked(httpClient.delete).mockResolvedValueOnce({})
      await boardApi.deleteTask('b1', 't1')
      expect(httpClient.delete).toHaveBeenCalledWith('/boards/b1/tasks/t1')
    })
  })

  describe('Labels', () => {
    it('attachLabel calls POST /tasks/{taskId}/labels', async () => {
      vi.mocked(httpClient.post).mockResolvedValueOnce({})
      await boardApi.attachLabel('t1', 'lbl-1')
      expect(httpClient.post).toHaveBeenCalledWith('/tasks/t1/labels', { labelId: 'lbl-1' })
    })

    it('detachLabel calls DELETE /tasks/{taskId}/labels/{labelId}', async () => {
      vi.mocked(httpClient.delete).mockResolvedValueOnce({})
      await boardApi.detachLabel('t1', 'lbl-1')
      expect(httpClient.delete).toHaveBeenCalledWith('/tasks/t1/labels/lbl-1')
    })
  })

  describe('Comments', () => {
    it('getComments calls GET /tasks/{taskId}/comments', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [] })
      await boardApi.getComments('t1')
      expect(httpClient.get).toHaveBeenCalledWith('/tasks/t1/comments')
    })

    it('createComment calls POST /tasks/{taskId}/comments', async () => {
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: { id: 'comm-1' } })
      await boardApi.createComment('t1', { content: 'Nice job' })
      expect(httpClient.post).toHaveBeenCalledWith('/tasks/t1/comments', { content: 'Nice job' })
    })

    it('updateComment calls PUT /tasks/{taskId}/comments/{commentId}', async () => {
      vi.mocked(httpClient.put).mockResolvedValueOnce({ data: { id: 'comm-1', content: 'Updated' } })
      await boardApi.updateComment('t1', 'comm-1', { content: 'Updated' })
      expect(httpClient.put).toHaveBeenCalledWith('/tasks/t1/comments/comm-1', { content: 'Updated' })
    })

    it('deleteComment calls DELETE /tasks/{taskId}/comments/{commentId}', async () => {
      vi.mocked(httpClient.delete).mockResolvedValueOnce({})
      await boardApi.deleteComment('t1', 'comm-1')
      expect(httpClient.delete).toHaveBeenCalledWith('/tasks/t1/comments/comm-1')
    })
  })
})
