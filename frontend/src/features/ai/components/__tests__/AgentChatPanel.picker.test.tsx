import React from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentChatPanel } from '../AgentChatPanel'
import { AgentChatTaskPicker } from '../AgentChatTaskPicker'
import { agentChatApi } from '../../services/agentChatApi'
import type { ColumnResponse, TaskResponse } from '../../../board/types/board.types'

/**
 * Phase 14.6 §A1 — the card picker that lets a card be attached WITHOUT dragging it.
 *
 * <p>Kept in its own file rather than appended to `AgentChatPanel.test.tsx`: that suite's fixtures have no
 * task list at all (drag &amp; drop needs none), so the picker's cases would be sharing a fixture they do not
 * describe.</p>
 */

vi.mock('../../services/agentChatApi', () => ({
  agentChatApi: {
    getSession: vi.fn(),
    listMessages: vi.fn(),
    stream: vi.fn(),
    saveAnswer: vi.fn(),
    submitProposal: vi.fn(),
  },
}))

const mocked = vi.mocked(agentChatApi)

const columns: ColumnResponse[] = [
  {
    id: 'c1',
    boardId: 'b1',
    name: 'Cần làm',
    position: 0,
    isDone: false,
    createdAt: '',
    updatedAt: '',
    tasks: [],
  },
]

const makeTask = (id: string, title: string, assigneeName: string | null = null): TaskResponse => ({
  id,
  boardId: 'b1',
  columnId: 'c1',
  title,
  description: null,
  position: 0,
  assigneeId: assigneeName ? 'u1' : null,
  assigneeName,
  dueDate: null,
  priority: 'High',
  createdAt: '',
  updatedAt: '',
  completedAt: null,
  labels: [],
  commentCount: 0,
  assigneeIsAiAgent: false,
  activeAgentRunId: null,
})

const tasks = [
  makeTask('t1', 'Thiết kế API chat', 'Linh'),
  makeTask('t2', 'Viết tài liệu module', null),
  makeTask('t3', 'Sửa lỗi đăng nhập', 'Alex'),
]

beforeEach(() => {
  vi.clearAllMocks()
  mocked.getSession.mockResolvedValue({ boardId: 'b1', hasMessages: false, model: 'deepseek-chat' })
  mocked.listMessages.mockResolvedValue([])
})

const renderPanel = () =>
  render(<AgentChatPanel boardId="b1" columns={columns} width={360} onClose={vi.fn()} tasks={tasks} />)

describe('AgentChatPanel — card picker (A1)', () => {
  it('PICKER1_attaches a card chosen from the searchable list, with no drag involved', async () => {
    renderPanel()

    await screen.findByText('Hỏi AI về cả bảng này')

    fireEvent.click(screen.getByTestId('agent-chat-attach-picker-button'))

    const picker = await screen.findByTestId('agent-chat-task-picker')

    // Title, priority and assignee are the three fields the picker promises to show.
    expect(within(picker).getByText('Thiết kế API chat')).toBeInTheDocument()
    expect(within(picker).getAllByText('Cao').length).toBeGreaterThan(0)
    expect(within(picker).getByText('Linh')).toBeInTheDocument()

    fireEvent.click(screen.getByTestId('agent-chat-picker-option-t2'))

    // The pill appears exactly as it does after a drop — one attachment mechanism, one representation.
    // Scoped to the attach region because the picker's own list may still be mounted while it closes.
    const attachList = screen.getByTestId('agent-chat-attach-list')

    expect(await screen.findByTestId('agent-chat-attachment-t2')).toBeInTheDocument()
    expect(within(attachList).getByText('Viết tài liệu module')).toBeInTheDocument()
  })

  it('PICKER2_filters by title and by assignee, and says so when nothing matches', async () => {
    renderPanel()
    await screen.findByText('Hỏi AI về cả bảng này')

    fireEvent.click(screen.getByTestId('agent-chat-attach-picker-button'))
    await screen.findByTestId('agent-chat-task-picker')

    const search = screen.getByTestId('agent-chat-task-picker-search')

    fireEvent.change(search, { target: { value: 'tài liệu' } })
    await waitFor(() => expect(screen.queryByTestId('agent-chat-picker-option-t1')).not.toBeInTheDocument())
    expect(screen.getByTestId('agent-chat-picker-option-t2')).toBeInTheDocument()

    // The board's own search box is a different question ("what do I want to look at?"), so this one also
    // matches the assignee.
    fireEvent.change(search, { target: { value: 'alex' } })
    await waitFor(() => expect(screen.getByTestId('agent-chat-picker-option-t3')).toBeInTheDocument())
    expect(screen.queryByTestId('agent-chat-picker-option-t2')).not.toBeInTheDocument()

    fireEvent.change(search, { target: { value: 'không có thẻ nào tên thế này' } })
    expect(await screen.findByText('Không tìm thấy thẻ nào')).toBeInTheDocument()
  })

  it('PICKER3_refuses a card that is already attached instead of attaching it twice', async () => {
    const { rerender } = renderPanel()
    await screen.findByText('Hỏi AI về cả bảng này')

    rerender(
      <AgentChatPanel
        boardId="b1"
        columns={columns}
        width={360}
        onClose={vi.fn()}
        tasks={tasks}
        attachRequest={{ token: 1, task: { taskId: 't1', title: 'Thiết kế API chat' } }}
      />
    )

    await screen.findByTestId('agent-chat-attachment-t1')

    fireEvent.click(screen.getByTestId('agent-chat-attach-picker-button'))
    await screen.findByTestId('agent-chat-task-picker')

    // Disabled, so the cap/duplicate rule is enforced by the control itself rather than by a warning after
    // the fact.
    expect(screen.getByTestId('agent-chat-picker-option-t1')).toBeDisabled()
    expect(screen.getByTestId('agent-chat-picker-option-t2')).not.toBeDisabled()
  })
})

describe('AgentChatTaskPicker — the cap is visible before it is hit', () => {
  it('PICKER4_states the cap and disables every option once it is reached', () => {
    const attachments = Array.from({ length: 8 }, (_, i) => ({
      taskId: `t${i}`,
      title: `Thẻ ${i}`,
    }))

    render(<AgentChatTaskPicker tasks={tasks} attachments={attachments} onPick={vi.fn()} />)

    expect(screen.getByText(/Đã gắn tối đa 8 thẻ/)).toBeInTheDocument()
    expect(screen.getByTestId('agent-chat-picker-option-t1')).toBeDisabled()
    expect(screen.getByTestId('agent-chat-picker-option-t3')).toBeDisabled()
  })

  it('PICKER5_reports progress against the cap while there is still room', () => {
    render(
      <AgentChatTaskPicker
        tasks={tasks}
        attachments={[{ taskId: 't1', title: 'Thiết kế API chat' }]}
        onPick={vi.fn()}
      />
    )

    expect(screen.getByText('Đã gắn 1/8 thẻ')).toBeInTheDocument()
    expect(screen.queryByText(/Đã gắn tối đa/)).not.toBeInTheDocument()
  })
})
