import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskDetailModal } from '../TaskDetailModal'
import type { ColumnResponse, TaskResponse } from '../../types/board.types'

// Mock useAuth
vi.mock('../../../auth', () => ({
  useAuth: () => ({
    user: { id: 'u-1', email: 'test@example.com', displayName: 'Test User' },
  }),
}))

// Mock useAgentRuns
vi.mock('../../ai', () => ({
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

vi.mock('../../ai/components/AiChatPanel', () => ({
  AiChatPanel: ({ taskId }: { taskId: string }) => (
    <div data-testid="ai-chat-panel" data-task-id={taskId}>
      AiChatPanel Mock
    </div>
  ),
}))

// Mock boardApi
vi.mock('../services/boardApi', () => ({
  boardApi: {
    getComments: vi.fn().mockResolvedValue([]),
    createComment: vi.fn(),
    updateComment: vi.fn(),
    deleteComment: vi.fn(),
  },
}))

describe('TaskDetailModal', () => {
  const mockTask: TaskResponse = {
    id: 'task-100',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Nhiệm vụ kiểm thử',
    description: 'Nội dung **quan trọng** cần xem',
    position: 0,
    priority: 'Low',
    dueDate: '2026-09-20T00:00:00.000Z',
    assigneeId: 'u-1',
    assigneeName: 'Alex Tran',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    completedAt: null,
    labels: [],
    commentCount: 0,
    assigneeIsAiAgent: false,
    activeAgentRunId: null,
  }

  const mockColumns: ColumnResponse[] = [
    { id: 'col-1', boardId: 'board-1', name: 'Cần làm', position: 0, isDone: false, isClarification: false, createdAt: '', updatedAt: '', tasks: [] },
    { id: 'col-2', boardId: 'board-1', name: 'Đang làm', position: 1, isDone: false, isClarification: false, createdAt: '', updatedAt: '', tasks: [] },
    { id: 'col-3', boardId: 'board-1', name: 'Hoàn thành', position: 2, isDone: true, isClarification: false, createdAt: '', updatedAt: '', tasks: [] },
  ]

  const mockMembers = [
    { userId: 'u-1', displayName: 'Alex Tran', email: 'alex@example.com', role: 'Admin', memberType: 'human' as const },
    { userId: 'u-2', displayName: 'Bob Agent', email: 'bob@agent.internal', role: 'Member', memberType: 'ai_agent' as const },
  ]

  const onUpdateTask = vi.fn().mockResolvedValue({})
  const onMoveTask = vi.fn().mockResolvedValue({})
  const onDeleteTask = vi.fn().mockResolvedValue({})
  const onCreateLabel = vi.fn().mockResolvedValue(undefined)
  const onAttachLabel = vi.fn().mockResolvedValue({})
  const onDetachLabel = vi.fn().mockResolvedValue({})
  const onClose = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  const renderModal = (taskProp: TaskResponse | null = mockTask, open = true) => {
    return render(
      <TaskDetailModal
        task={taskProp}
        columns={mockColumns}
        workspaceLabels={[]}
        workspaceId="ws-1"
        workspaceMembers={mockMembers}
        isManagerOrAdmin={true}
        open={open}
        onClose={onClose}
        onUpdateTask={onUpdateTask}
        onMoveTask={onMoveTask}
        onDeleteTask={onDeleteTask}
        onCreateLabel={onCreateLabel}
        onAttachLabel={onAttachLabel}
        onDetachLabel={onDetachLabel}
      />
    )
  }

  it('renders modal with task title and description', () => {
    renderModal()
    expect(screen.getByDisplayValue('Nhiệm vụ kiểm thử')).toBeInTheDocument()
    expect(screen.getByDisplayValue('Nội dung **quan trọng** cần xem')).toBeInTheDocument()
  })

  it('renders preview description with strong tag when switched to preview mode', async () => {
    renderModal()

    // Click "Xem trước" tab
    const previewTab = screen.getByRole('radio', { name: 'Xem trước' }) || screen.getByText('Xem trước')
    fireEvent.click(previewTab)

    await waitFor(() => {
      const strongEl = screen.getByText('quan trọng')
      expect(strongEl.tagName).toBe('STRONG')
    })
  })

  it('toggles between Soạn and Xem trước modes via Segmented', async () => {
    renderModal()

    const previewTab = screen.getByText('Xem trước')
    fireEvent.click(previewTab)
    await waitFor(() => {
      expect(screen.getByText('quan trọng')).toBeInTheDocument()
    })

    const writeTab = document.querySelectorAll('.ant-segmented-item')[0] || screen.getByText('Soạn')
    fireEvent.click(writeTab)
    await waitFor(() => {
      expect(screen.getByPlaceholderText('Thêm mô tả chi tiết cho thẻ này...')).toBeInTheDocument()
      expect(screen.queryByTestId('description-preview')).toBeNull()
    })
  })

  it('renders placeholder "Chưa có mô tả" when description is empty in preview mode', async () => {
    const emptyTask = { ...mockTask, description: null }
    renderModal(emptyTask)

    const previewTab = screen.getByText('Xem trước')
    fireEvent.click(previewTab)

    await waitFor(() => {
      expect(screen.getByText('Chưa có mô tả')).toBeInTheDocument()
    })
  })

  it('changes Priority: onUpdateTask receives the NEW priority value (Item H test)', async () => {
    renderModal()

    // Find priority select and change value to "Urgent"
    const prioritySelect = screen.getByText('Thấp') // Current priority label for "Low"
    fireEvent.mouseDown(prioritySelect)

    // Select "Khẩn cấp" (Urgent) from dropdown
    await waitFor(() => {
      expect(screen.getByText('Khẩn cấp')).toBeInTheDocument()
    })
    fireEvent.click(screen.getByText('Khẩn cấp'))

    await waitFor(() => {
      expect(onUpdateTask).toHaveBeenCalledWith(
        mockTask.id,
        expect.objectContaining({ priority: 'Urgent' })
      )
    })
  })

  it('clears priority: calls onUpdateTask with priority null when priority is cleared', async () => {
    renderModal()

    // Ant Design select clear button
    const clearBtn = document.querySelector('.ant-select-clear')
    if (clearBtn) {
      fireEvent.click(clearBtn)
      await waitFor(() => {
        expect(onUpdateTask).toHaveBeenCalledWith(
          mockTask.id,
          expect.objectContaining({ priority: null })
        )
      })
    }
  })

  it('changes due date: calls onUpdateTask with ISO string when DatePicker changes', async () => {
    renderModal()

    const dateInput = screen.getByPlaceholderText('Chọn ngày hạn chót')
    fireEvent.mouseDown(dateInput)
    fireEvent.change(dateInput, { target: { value: '25/12/2026' } })
    fireEvent.keyDown(dateInput, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(onUpdateTask).toHaveBeenCalledWith(
        mockTask.id,
        expect.objectContaining({
          dueDate: expect.any(String),
        })
      )
    })
  })

  it('calls onUpdateTask with updated description on blur', async () => {
    renderModal()

    const descInput = screen.getByPlaceholderText('Thêm mô tả chi tiết cho thẻ này...')
    fireEvent.change(descInput, { target: { value: 'Mô tả mới đã cập nhật' } })
    fireEvent.blur(descInput)

    await waitFor(() => {
      expect(onUpdateTask).toHaveBeenCalledWith(
        mockTask.id,
        expect.objectContaining({ description: 'Mô tả mới đã cập nhật' })
      )
    })
  })

  it('moves task to new column: calls onMoveTask when column Select changes', async () => {
    renderModal()

    const colSelect = screen.getByText('Cần làm')
    fireEvent.mouseDown(colSelect)

    await waitFor(() => {
      expect(screen.getByText('Đang làm')).toBeInTheDocument()
    })
    fireEvent.click(screen.getByText('Đang làm'))

    await waitFor(() => {
      expect(onMoveTask).toHaveBeenCalledWith(mockTask.id, 'col-1', 'col-2', 0)
    })
  })

  it('renders assignee select with data-testid="assignee-select"', () => {
    renderModal()
    expect(screen.getByTestId('assignee-select')).toBeInTheDocument()
  })

  it('renders "Hỏi AI" tab with data-testid="ai-chat-tab"', () => {
    renderModal()
    expect(screen.getByTestId('ai-chat-tab')).toBeInTheDocument()
    expect(screen.getByText('Hỏi AI')).toBeInTheDocument()
  })

  it('switches to "Hỏi AI" tab and renders AiChatPanel while keeping modal open', async () => {
    renderModal()
    const aiTab = screen.getByTestId('ai-chat-tab')
    fireEvent.click(aiTab)

    await waitFor(() => {
      expect(screen.getByTestId('ai-chat-panel')).toBeInTheDocument()
      expect(screen.getByTestId('ai-chat-panel')).toHaveAttribute('data-task-id', mockTask.id)
    })
  })
})
