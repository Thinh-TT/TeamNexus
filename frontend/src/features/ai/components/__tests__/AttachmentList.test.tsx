import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AttachmentList } from '../AttachmentList'
import { agentApi } from '../../services/agentApi'
import type { AttachmentResponse } from '../../types/agentRun.types'

vi.mock('../../services/agentApi', () => ({
  agentApi: {
    listAttachments: vi.fn(),
    downloadAttachment: vi.fn(),
  },
}))

describe('AttachmentList', () => {
  const mockAttachments: AttachmentResponse[] = [
    {
      id: 'att-1',
      taskId: 'task-1',
      fileName: 'Architecture_Plan.md',
      contentType: 'text/markdown',
      sizeBytes: 2048,
      createdByUserId: 'agent-1',
      createdByName: 'TeamNexus Agent',
      sourceRunId: 'run-1',
      createdAt: '2026-09-12T10:00:00Z',
    },
    {
      id: 'att-2',
      taskId: 'task-1',
      fileName: 'schema.sql',
      contentType: 'text/plain',
      sizeBytes: 1048576,
      createdByUserId: 'user-1',
      createdByName: 'John Doe',
      sourceRunId: null,
      createdAt: '2026-09-12T10:30:00Z',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders empty message when there are no attachments', async () => {
    render(<AttachmentList taskId="task-1" attachments={[]} />)

    expect(screen.getByText('Chưa có tệp đính kèm nào')).toBeInTheDocument()
    expect(screen.getByText('Tệp đính kèm (0)')).toBeInTheDocument()
  })

  it('renders list of attachments with formatted sizes and tags', async () => {
    render(<AttachmentList taskId="task-1" attachments={mockAttachments} />)

    expect(screen.getByText('Tệp đính kèm (2)')).toBeInTheDocument()
    expect(screen.getByText('Architecture_Plan.md')).toBeInTheDocument()
    expect(screen.getByText('schema.sql')).toBeInTheDocument()
    expect(screen.getByText('2 KB')).toBeInTheDocument()
    expect(screen.getByText('1 MB')).toBeInTheDocument()
    expect(screen.getByText('do AI Agent tạo')).toBeInTheDocument()
  })

  it('calls downloadAttachment when clicking download button', async () => {
    vi.mocked(agentApi.downloadAttachment).mockResolvedValueOnce()

    render(<AttachmentList taskId="task-1" attachments={mockAttachments} />)

    const downloadBtn = screen.getByTestId('download-btn-att-1')
    fireEvent.click(downloadBtn)

    expect(agentApi.downloadAttachment).toHaveBeenCalledWith(
      'task-1',
      'att-1',
      'Architecture_Plan.md'
    )
  })
})
