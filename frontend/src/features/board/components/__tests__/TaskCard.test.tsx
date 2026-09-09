import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from '../TaskCard'
import type { TaskResponse } from '../../types/board.types'

const renderWithDnd = (ui: React.ReactElement) => {
  return render(<DndContext>{ui}</DndContext>)
}

describe('TaskCard', () => {
  const baseTask: TaskResponse = {
    id: 'task-1',
    boardId: 'board-1',
    columnId: 'col-1',
    title: 'Implement Auth Service',
    description: 'Implement JWT with refresh token',
    position: 0,
    priority: 'Urgent',
    dueDate: '2026-12-31T00:00:00Z',
    assigneeId: 'u-1',
    assigneeName: 'Alex Tran',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    labels: [
      { id: 'lbl-1', workspaceId: 'ws-1', name: 'Security', color: '#ef4444', createdAt: '2026-09-09T00:00:00Z' },
    ],
    commentCount: 5,
  }

  it('renders task title, priority tag, and labels', () => {
    renderWithDnd(<TaskCard task={baseTask} />)

    expect(screen.getByText('Implement Auth Service')).toBeInTheDocument()
    expect(screen.getByText('Khẩn cấp')).toBeInTheDocument()
    expect(screen.getByText('Security')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument() // comment count
    expect(screen.getByText('A')).toBeInTheDocument() // avatar letter
  })

  it('renders completed styling when completedAt is set or in done column', () => {
    const completedTask: TaskResponse = {
      ...baseTask,
      completedAt: '2026-09-09T10:00:00Z',
    }

    const { container } = renderWithDnd(<TaskCard task={completedTask} isDoneColumn={true} />)

    expect(container.querySelector('.anticon-check-circle')).toBeInTheDocument()
    expect(container.querySelector('del')).toBeInTheDocument()
  })

  it('renders unassigned placeholder when assigneeName is null', () => {
    const unassignedTask: TaskResponse = {
      ...baseTask,
      assigneeId: null,
      assigneeName: null,
    }

    renderWithDnd(<TaskCard task={unassignedTask} />)
    expect(screen.queryByText('Alex Tran')).not.toBeInTheDocument()
  })
})
