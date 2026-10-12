import React from 'react'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BoardView } from '../BoardView'
import { aiActionApi } from '../../../ai/services/aiActionApi'
import { agentChatApi } from '../../../ai/services/agentChatApi'
import { notificationApi } from '../../../ai/services/notificationApi'
import { observerApi } from '../../../ai/services/observerApi'
import { httpClient } from '../../../../shared/api'
import type { BoardResponse, ColumnResponse, TaskResponse } from '../../types/board.types'

/**
 * Phase 14.5 §B — the board's AI Agent Chat entry point (AGENTCHAT-FE-1…4).
 *
 * <p>Kept in its own file rather than appended to `BoardView.test.tsx`: that suite's mock set is large and
 * its 11 tests assert Kanban behaviour on mount, so adding a chat feature there would put the two under the
 * same fixtures for no benefit. This file mocks only what the panel actually calls.</p>
 */

const mockBoard: BoardResponse = {
  id: 'board-1',
  workspaceId: 'ws-1',
  name: 'Sprint 2 Board',
  description: null,
  createdAt: '',
  updatedAt: '',
  columns: [],
}

const mockColumns: ColumnResponse[] = [
  {
    id: 'col-1',
    boardId: 'board-1',
    name: 'Cần làm',
    position: 0,
    isDone: false,
    createdAt: '',
    updatedAt: '',
    tasks: [],
  },
]

const mockTasks: TaskResponse[] = [
  {
    id: 'task-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Thẻ cần kéo',
    description: null,
    position: 0,
    priority: 'Urgent',
    assigneeName: null,
    labels: [],
    createdAt: '',
    updatedAt: '',
    commentCount: 0,
  },
]

const moveTask = vi.fn()

vi.mock('../../hooks/useBoard', () => ({
  useBoard: () => ({
    board: mockBoard,
    columns: mockColumns,
    tasksByColumn: { 'col-1': mockTasks },
    workspaceLabels: [],
    activeTask: null,
    connectionStatus: 'connected',
    reconnect: vi.fn(),
    isLoading: false,
    refetch: vi.fn(),
    setActiveTask: vi.fn(),
    createColumn: vi.fn(),
    updateColumn: vi.fn(),
    deleteColumn: vi.fn(),
    createTask: vi.fn(),
    updateTask: vi.fn(),
    moveTask,
    deleteTask: vi.fn(),
    createLabel: vi.fn(),
    attachLabel: vi.fn(),
    detachLabel: vi.fn(),
  }),
}))

vi.mock('../../../../shared/api', () => ({
  httpClient: { get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() },
}))

vi.mock('../../../ai/services/aiActionApi', () => ({
  aiActionApi: { listAiActions: vi.fn() },
}))

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: { listNotifications: vi.fn() },
}))

vi.mock('../../../ai/services/observerApi', () => ({
  observerApi: { listObserverRuns: vi.fn() },
}))

vi.mock('../../../ai/services/agentChatApi', () => ({
  agentChatApi: {
    getSession: vi.fn(),
    listMessages: vi.fn(),
    stream: vi.fn(),
    saveAnswer: vi.fn(),
    submitProposal: vi.fn(),
  },
}))

const mockedAgentChat = vi.mocked(agentChatApi)

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])
  vi.mocked(observerApi.listObserverRuns).mockResolvedValue([])
  vi.mocked(notificationApi.listNotifications).mockResolvedValue({ unreadCount: 0, items: [] })
  vi.mocked(httpClient.get).mockResolvedValue({ data: [] })

  mockedAgentChat.getSession.mockResolvedValue({ boardId: 'board-1', hasMessages: false, model: 'deepseek-chat' })
  mockedAgentChat.listMessages.mockResolvedValue([])
})

const renderComponent = () =>
  render(
    <MemoryRouter>
      <BoardView workspaceId="ws-1" boardId="board-1" />
    </MemoryRouter>
  )

describe('BoardView — AI Agent Chat', () => {
  it('AGENTCHAT_FE1_showsTheFloatingButtonAndKeepsTheBoardUsable', () => {
    renderComponent()

    expect(screen.getByTestId('agent-chat-fab')).toBeInTheDocument()
    // The Kanban itself is untouched: the chat is an addition, not a replacement.
    expect(screen.getByText('Sprint 2 Board')).toBeInTheDocument()
    expect(screen.getByText('Thẻ cần kéo')).toBeInTheDocument()
  })

  it('AGENTCHAT_FE2_opensAndClosesThePanelWithoutMovingAnyCard', async () => {
    renderComponent()

    fireEvent.click(screen.getByTestId('agent-chat-fab'))

    // The transcript is loaded from the server on open — that is what makes the board conversation survive
    // a reload.
    await waitFor(() => expect(mockedAgentChat.getSession).toHaveBeenCalledWith('board-1'))

    expect(await screen.findByTestId('agent-chat-panel')).toBeInTheDocument()
    expect(screen.getByTestId('agent-chat-attach-list')).toBeInTheDocument()

    // Opening a chat must never count as a drag: the card stays exactly where it was.
    expect(moveTask).not.toHaveBeenCalled()

    fireEvent.click(screen.getByTestId('agent-chat-fab'))

    await waitFor(() => expect(screen.queryByTestId('agent-chat-panel')).not.toBeInTheDocument())
    expect(moveTask).not.toHaveBeenCalled()
  })

  it('AGENTCHAT_FE3_thePanelClosesThroughItsOwnButton', async () => {
    renderComponent()

    fireEvent.click(screen.getByTestId('agent-chat-fab'))
    await screen.findByTestId('agent-chat-panel')

    fireEvent.click(screen.getByTestId('agent-chat-close'))

    await waitFor(() => expect(screen.queryByTestId('agent-chat-panel')).not.toBeInTheDocument())
  })

  it('AGENTCHAT_FE4_thePendingActionBadgeIsFetchedOncePerBoard', async () => {
    vi.mocked(aiActionApi.listAiActions).mockResolvedValue([])

    renderComponent()

    await waitFor(() => expect(vi.mocked(aiActionApi.listAiActions)).toHaveBeenCalled())

    // Phase 14.5 A2.3: the duplicate inline fetch was removed, so the mount path calls the endpoint once.
    // Two round trips for one badge number was the bug; this assertion is what keeps it fixed.
    expect(vi.mocked(aiActionApi.listAiActions)).toHaveBeenCalledTimes(1)
  })

  it('AGENTCHAT_FE5_sends a card to the chat from its "…" menu, without dragging it', async () => {
    renderComponent()

    // Phase 14.6 §A1: the card's own menu is the second way in. The panel is not open yet, so this also proves
    // the action OPENS the chat rather than only working once it is already visible.
    expect(screen.queryByTestId('agent-chat-panel')).not.toBeInTheDocument()

    fireEvent.click(screen.getByTestId('task-card-menu-task-1'))
    fireEvent.click(await screen.findByText('Gửi vào AI Chat'))

    expect(await screen.findByTestId('agent-chat-panel')).toBeInTheDocument()
    expect(await screen.findByTestId('agent-chat-attachment-task-1')).toBeInTheDocument()

    // Attaching is not moving: the card must still be in its column, and no write may have been issued.
    expect(moveTask).not.toHaveBeenCalled()
  })

  it('AGENTCHAT_FE6_offers the board picker with the same attachment representation as a drop', async () => {
    renderComponent()

    fireEvent.click(screen.getByTestId('agent-chat-fab'))
    await screen.findByTestId('agent-chat-panel')

    fireEvent.click(screen.getByTestId('agent-chat-attach-picker-button'))

    // Every card of the board is offered, even though the board's own search box is empty and the column
    // fixture holds exactly one card.
    expect(await screen.findByTestId('agent-chat-picker-option-task-1')).toBeInTheDocument()

    fireEvent.click(screen.getByTestId('agent-chat-picker-option-task-1'))

    expect(await screen.findByTestId('agent-chat-attachment-task-1')).toBeInTheDocument()
    expect(moveTask).not.toHaveBeenCalled()
  })
})
