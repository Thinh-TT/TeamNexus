import { afterEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api'
import { saveBlob } from '../../../reporting/utils/reportDownload'
import { agentApi } from '../agentApi'

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
  },
}))

vi.mock('../../../reporting/utils/reportDownload', () => ({
  saveBlob: vi.fn(),
}))

describe('agentApi', () => {
  afterEach(() => {
    vi.clearAllMocks()
  })

  describe('startRun', () => {
    it('calls POST /api/tasks/{taskId}/agent-runs and returns 202 AgentRunResponse', async () => {
      const mockRun = { id: 'run-1', taskId: 'task-1', status: 'Running' }
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockRun, status: 202 })

      const result = await agentApi.startRun('task-1')

      expect(httpClient.post).toHaveBeenCalledWith('/tasks/task-1/agent-runs')
      expect(result).toEqual(mockRun)
    })
  })

  describe('rerun', () => {
    it('calls POST /api/tasks/{taskId}/agent-runs/{runId}/rerun and returns 202 AgentRunResponse', async () => {
      const mockNewRun = { id: 'run-2', taskId: 'task-1', status: 'Running', previousRunId: 'run-1' }
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockNewRun, status: 202 })

      const result = await agentApi.rerun('task-1', 'run-1')

      expect(httpClient.post).toHaveBeenCalledWith('/tasks/task-1/agent-runs/run-1/rerun')
      expect(result).toEqual(mockNewRun)
    })
  })

  describe('listRuns', () => {
    it('calls GET /api/tasks/{taskId}/agent-runs with default take=20', async () => {
      const mockRuns = [{ id: 'run-1' }, { id: 'run-2' }]
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockRuns })

      const result = await agentApi.listRuns('task-1')

      expect(httpClient.get).toHaveBeenCalledWith('/tasks/task-1/agent-runs', {
        params: { take: 20 },
      })
      expect(result).toEqual(mockRuns)
    })

    it('supports custom take parameter', async () => {
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: [] })

      await agentApi.listRuns('task-1', 5)

      expect(httpClient.get).toHaveBeenCalledWith('/tasks/task-1/agent-runs', {
        params: { take: 5 },
      })
    })
  })

  describe('getRunDetail', () => {
    it('calls GET /api/agent-runs/{runId}', async () => {
      const mockDetail = {
        run: { id: 'run-1', status: 'Completed' },
        toolCallTrace: [{ name: 'SearchSystemData', durationMs: 120 }],
      }
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockDetail })

      const result = await agentApi.getRunDetail('run-1')

      expect(httpClient.get).toHaveBeenCalledWith('/agent-runs/run-1')
      expect(result).toEqual(mockDetail)
    })
  })

  describe('cancelRun', () => {
    it('calls POST /api/agent-runs/{runId}/cancel and returns 200 with cancelled detail', async () => {
      const mockCancelled = {
        run: { id: 'run-1', status: 'Failed', stopReason: 'Cancelled' },
        toolCallTrace: [],
      }
      vi.mocked(httpClient.post).mockResolvedValueOnce({ data: mockCancelled })

      const result = await agentApi.cancelRun('run-1')

      expect(httpClient.post).toHaveBeenCalledWith('/agent-runs/run-1/cancel')
      expect(result).toEqual(mockCancelled)
    })
  })

  describe('listAttachments', () => {
    it('calls GET /api/tasks/{taskId}/attachments', async () => {
      const mockAttachments = [{ id: 'att-1', fileName: 'report.md' }]
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockAttachments })

      const result = await agentApi.listAttachments('task-1')

      expect(httpClient.get).toHaveBeenCalledWith('/tasks/task-1/attachments')
      expect(result).toEqual(mockAttachments)
    })
  })

  describe('downloadAttachment', () => {
    it('calls GET /api/tasks/{taskId}/attachments/{attachmentId}/download with responseType blob and calls saveBlob', async () => {
      const mockBlob = new Blob(['sample content'], { type: 'text/markdown' })
      vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockBlob })

      await agentApi.downloadAttachment('task-1', 'att-1', 'output.md')

      expect(httpClient.get).toHaveBeenCalledWith(
        '/tasks/task-1/attachments/att-1/download',
        { responseType: 'blob' }
      )
      expect(saveBlob).toHaveBeenCalledWith(mockBlob, 'output.md')
    })
  })
})
