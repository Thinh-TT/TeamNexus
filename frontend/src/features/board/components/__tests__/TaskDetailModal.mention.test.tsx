import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskDetailModal } from '../TaskDetailModal'
import { boardApi } from '../../services/boardApi'
import type { ColumnResponse, TaskResponse, WorkspaceMemberResponse } from '../../types/board.types'

vi.mock('../../../auth/hooks/useAuth', () => ({
  useAuth: () => ({
    user: { id: 'u-current', email: 'me@example.com', displayName: 'Current User' },
  }),
}))

vi.mock('../../hooks/useWorkspaceMembers', () => ({
  useWorkspaceMembers: () => ({
    members: [
      {
        userId: 'u-target',
        displayName: 'Trần An',
        email: 'an@example.com',
        role: 'Member',
        memberType: 'human',
        joinedAt: '',
      },
      {
        userId: 'agent-1',
        displayName: 'AI Bot',
        email: 'bot@example.com',
        role: 'Member',
        memberType: 'ai_agent',
        joinedAt: '',
      },
    ],
    loading: false,
    error: null,
    refetch: vi.fn(),
  }),
}))

vi.mock('../../../ai', () => ({
  useAgentRuns: () => ({
    currentRun: null,
    runDetail: null,
    loading: false,
    actionLoading: false,
    fetchRuns: vi.fn(),
    startRun: vi.fn(),
    rerun: vi.fn(),
    cancelRun: vi.fn(),
  }),
  AgentRunPanel: () => <div data-testid="agent-run-panel" />,
  AttachmentList: () => <div data-testid="attachment-list" />,
}))

vi.mock('../../services/boardApi', () => ({
  boardApi: {
    getComments: vi.fn().mockResolvedValue([]),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

describe('TaskDetailModal - @mention comments', () => {
  const mockTask: TaskResponse = {
    id: 'task-100',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Thẻ kiểm tra mention',
    description: '',
    position: 0,
    priority: 'Low',
    dueDate: null,
    createdAt: '2026-09-15T00:00:00Z',
    updatedAt: '2026-09-15T00:00:00Z',
    commentCount: 0,
  }

  const mockColumns: ColumnResponse[] = [
    { id: 'col-1', boardId: 'board-1', name: 'To Do', position: 0, isDone: false, createdAt: '', updatedAt: '', tasks: [] },
  ]

  const mockMembers: WorkspaceMemberResponse[] = [
    {
      userId: 'u-target',
      displayName: 'Trần An',
      email: 'an@example.com',
      role: 'Member',
      memberType: 'human',
      joinedAt: '',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(boardApi.createComment).mockResolvedValue({
      id: 'c-1',
      taskId: 'task-100',
      userId: 'u-current',
      userName: 'Current User',
      content: 'Nhờ @Trần An xem giúp',
      createdAt: '2026-09-15T12:00:00Z',
      updatedAt: '2026-09-15T12:00:00Z',
    })
  })

  it('submits comment with mentionUserIds extracted from content', async () => {
    render(
      <TaskDetailModal
        open={true}
        onClose={vi.fn()}
        task={mockTask}
        columns={mockColumns}
        workspaceLabels={[]}
        workspaceId="ws-1"
        workspaceMembers={mockMembers}
      />
    )

    // Find Mentions textarea by placeholder
    const textarea = screen.getByPlaceholderText('Viết bình luận...')
    expect(textarea).toBeInTheDocument()

    // Type comment mentioning Trần An
    fireEvent.change(textarea, { target: { value: 'Nhờ @Trần An xem giúp' } })

    // Click "Gửi bình luận"
    const submitBtn = screen.getByRole('button', { name: /Gửi bình luận/i })
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(boardApi.createComment).toHaveBeenCalledWith('task-100', {
        content: 'Nhờ @Trần An xem giúp',
        mentionUserIds: ['u-target'],
      })
    })
  }, 15000)
})
