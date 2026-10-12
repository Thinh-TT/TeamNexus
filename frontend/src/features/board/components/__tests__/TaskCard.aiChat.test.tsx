import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from '../TaskCard'
import type { TaskResponse } from '../../types/board.types'

/**
 * Phase 14.6 §A1 — the "…" menu on a Kanban card and its "Gửi vào AI Chat" action.
 *
 * <p>Kept separate from `TaskCard.test.tsx` so that suite's fixtures and assertions stay exactly as they were:
 * the menu is <b>additive</b> (absent unless a handler is supplied), and this file is what proves that.</p>
 */

const task: TaskResponse = {
  id: 'task-1',
  boardId: 'board-1',
  columnId: 'col-1',
  title: 'Thiết kế API chat',
  description: null,
  position: 0,
  priority: 'Urgent',
  dueDate: null,
  assigneeId: null,
  assigneeName: null,
  createdAt: '',
  updatedAt: '',
  completedAt: null,
  labels: [],
  commentCount: 0,
  assigneeIsAiAgent: false,
  activeAgentRunId: null,
}

const renderCard = (ui: React.ReactElement) => render(<DndContext>{ui}</DndContext>)

describe('TaskCard — "Gửi vào AI Chat" (A1)', () => {
  it('TASKCARD_AI1_sends the card to the chat from the "…" menu', async () => {
    const onSendToAiChat = vi.fn()

    renderCard(<TaskCard task={task} onSendToAiChat={onSendToAiChat} />)

    fireEvent.click(screen.getByTestId('task-card-menu-task-1'))

    const item = await screen.findByText('Gửi vào AI Chat')
    fireEvent.click(item)

    expect(onSendToAiChat).toHaveBeenCalledTimes(1)
    expect(onSendToAiChat).toHaveBeenCalledWith(task)
  })

  it('TASKCARD_AI2_does not open the task modal when the menu is used', async () => {
    const onClick = vi.fn()

    renderCard(<TaskCard task={task} onClick={onClick} onSendToAiChat={vi.fn()} />)

    // The card opens the detail modal on click and dnd-kit listens for pointer-down on the same element, so
    // both are stopped on the trigger — otherwise "open this menu" would also mean "open the task".
    fireEvent.click(screen.getByTestId('task-card-menu-task-1'))

    expect(onClick).not.toHaveBeenCalled()

    fireEvent.click(await screen.findByText('Gửi vào AI Chat'))

    expect(onClick).not.toHaveBeenCalled()
  })

  it('TASKCARD_AI3_shows no menu when no handler is wired up', () => {
    renderCard(<TaskCard task={task} />)

    // Every pre-existing caller (and the drag overlay) must be unchanged: no handler ⇒ no control.
    expect(screen.queryByTestId('task-card-menu-task-1')).not.toBeInTheDocument()
  })

  it('TASKCARD_AI4_suppresses the menu on the drag overlay', () => {
    renderCard(<TaskCard task={task} isDragOverlay onSendToAiChat={vi.fn()} />)

    // The ghost follows the pointer: an interactive control inside it could never be reached.
    expect(screen.queryByTestId('task-card-menu-task-1')).not.toBeInTheDocument()
  })
})
