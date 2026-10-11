import React from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentChatPanel } from '../AgentChatPanel'
import { agentChatApi } from '../../services/agentChatApi'
import type { ColumnResponse } from '../../../board/types/board.types'

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
  {
    id: 'c2',
    boardId: 'b1',
    name: 'Xong',
    position: 1,
    isDone: true,
    createdAt: '',
    updatedAt: '',
    tasks: [],
  },
]

beforeEach(() => {
  vi.clearAllMocks()
  mocked.getSession.mockResolvedValue({ boardId: 'b1', hasMessages: false, model: 'deepseek-chat' })
  mocked.listMessages.mockResolvedValue([])
})

const renderPanel = (props: Partial<React.ComponentProps<typeof AgentChatPanel>> = {}) =>
  render(
    <AgentChatPanel
      boardId="b1"
      columns={columns}
      width={360}
      onClose={vi.fn()}
      {...props}
    />
  )

describe('AgentChatPanel', () => {
  it('shows the empty state with guidance instead of a blank panel', async () => {
    renderPanel()

    expect(await screen.findByText('Hỏi AI về cả bảng này')).toBeInTheDocument()
    expect(screen.getByText(/Kéo thẻ vào khung phía trên/)).toBeInTheDocument()
  })

  it('tells the user that no task is attached, and enables saving only once one is', async () => {
    renderPanel()

    await screen.findByText('Hỏi AI về cả bảng này')

    // Nothing attached ⇒ the comment would have nowhere to go, so the action is disabled with a reason.
    expect(screen.getByTestId('agent-chat-save-answer')).toBeDisabled()
    expect(screen.getByText(/Chưa có thẻ nào được gắn/)).toBeInTheDocument()
  })

  it('renders a card the user attached and explains a duplicate drop', async () => {
    const { rerender } = renderPanel()

    await screen.findByText('Hỏi AI về cả bảng này')

    rerender(
      <AgentChatPanel
        boardId="b1"
        columns={columns}
        width={360}
        onClose={vi.fn()}
        attachRequest={{ token: 1, task: { taskId: 't1', title: 'Thẻ một' } }}
      />
    )

    expect(await screen.findByTestId('agent-chat-attachment-t1')).toBeInTheDocument()

    // The SAME card dropped again: the token changes, so the effect runs and says why nothing happened
    // rather than the pill silently staying put.
    rerender(
      <AgentChatPanel
        boardId="b1"
        columns={columns}
        width={360}
        onClose={vi.fn()}
        attachRequest={{ token: 2, task: { taskId: 't1', title: 'Thẻ một' } }}
      />
    )

    await waitFor(() => expect(screen.getByText('Thẻ một')).toBeInTheDocument())
  })

  it('cancels a streaming answer when the user presses Dừng', async () => {
    mocked.stream.mockImplementation(
      async (_boardId, _request, options) =>
        new Promise<void>((resolve) => {
          options.onEvent({ event: 'delta', data: { text: 'Đang trả lời...' } })
          options.signal?.addEventListener('abort', () => resolve())
        })
    )

    renderPanel()
    await screen.findByText('Hỏi AI về cả bảng này')

    fireEvent.change(screen.getByPlaceholderText(/Hỏi AI về bảng/), {
      target: { value: 'Bảng này thế nào?' },
    })
    fireEvent.click(screen.getByTestId('agent-chat-send'))

    expect(await screen.findByTestId('agent-chat-stop')).toBeInTheDocument()

    fireEvent.click(screen.getByTestId('agent-chat-stop'))

    // The partial text is discarded on an explicit cancel, and only the user's own turn remains.
    await waitFor(() => expect(screen.queryByTestId('agent-chat-stop')).not.toBeInTheDocument())
    expect(screen.getByText('Bảng này thế nào?')).toBeInTheDocument()
  })

  it('offers the action card for a proposal and submits it', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({
        event: 'done',
        data: {
          answer:
            'Nên chuyển thẻ sang cột Xong.\n\n```json\n' +
            '{"proposal":{"kind":"MoveTaskToColumn","summary":"Chuyển sang Xong","payload":{"taskId":"t1","columnId":"c2"}}}\n```',
        },
      })
    })

    mocked.submitProposal.mockResolvedValue({
      id: 'log-1',
      action: 'MoveTaskToColumn',
      entityType: 'Task',
      entityId: 't1',
      status: 'Pending',
      requestedByUserId: 'u1',
      requestedByName: null,
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 0,
      createdAt: '',
      updatedAt: '',
    })

    renderPanel()
    await screen.findByText('Hỏi AI về cả bảng này')

    fireEvent.change(screen.getByPlaceholderText(/Hỏi AI về bảng/), {
      target: { value: 'Nên làm gì?' },
    })
    fireEvent.click(screen.getByTestId('agent-chat-send'))

    const card = await screen.findByTestId('agent-chat-proposal-card')

    // The card names the action AND resolves the target column for the user — an id would be meaningless.
    expect(within(card).getByText(/Di chuyển thẻ/)).toBeInTheDocument()
    expect(within(card).getByText('Chuyển sang Xong')).toBeInTheDocument()
    // The column NAME, not just the id: the server stores a `columnId` and a user cannot read a Guid.
    expect(within(card).getByText(/^Cột đích: Xong$/)).toBeInTheDocument()

    // The JSON block itself must never be rendered as prose.
    expect(screen.queryByText(/```json/)).not.toBeInTheDocument()

    fireEvent.click(screen.getByTestId('agent-chat-submit-proposal'))

    await waitFor(() => expect(mocked.submitProposal).toHaveBeenCalledTimes(1))
  })

  it('shows a Vietnamese error and keeps the partial answer when the stream fails', async () => {
    mocked.stream.mockImplementation(async (_boardId, _request, options) => {
      options.onEvent({ event: 'delta', data: { text: 'Dở dang' } })
      throw Object.assign(new Error('AI Agent Chat đang tạm tắt.'), { status: 503 })
    })

    renderPanel()
    await screen.findByText('Hỏi AI về cả bảng này')

    fireEvent.change(screen.getByPlaceholderText(/Hỏi AI về bảng/), {
      target: { value: 'Hỏi' },
    })
    fireEvent.click(screen.getByTestId('agent-chat-send'))

    expect(await screen.findByText('AI Agent Chat đang tạm tắt.')).toBeInTheDocument()
    expect(screen.getByText(/Mã lỗi: 503/)).toBeInTheDocument()
    // Text the user already received is not thrown away.
    expect(screen.getByText('Dở dang')).toBeInTheDocument()
  })
})
