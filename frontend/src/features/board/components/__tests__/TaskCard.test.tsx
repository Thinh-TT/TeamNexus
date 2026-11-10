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
    dueDate: '2099-12-31T00:00:00Z',
    assigneeId: 'u-1',
    assigneeName: 'Alex Tran',
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    completedAt: null,
    labels: [
      { id: 'lbl-1', workspaceId: 'ws-1', name: 'Security', color: '#ef4444', createdAt: '2026-09-09T00:00:00Z' },
    ],
    commentCount: 5,
    assigneeIsAiAgent: false,
    activeAgentRunId: null,
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

  it('renders AI Agent active badge and agent assignee avatar when task is assigned to agent', () => {
    const agentTask: TaskResponse = {
      ...baseTask,
      assigneeName: 'TeamNexus Agent',
      assigneeIsAiAgent: true,
      activeAgentRunId: 'run-99',
    }

    renderWithDnd(<TaskCard task={agentTask} />)

    expect(screen.getByTestId('agent-active-badge')).toBeInTheDocument()
    expect(screen.getByTestId('agent-assignee-avatar')).toBeInTheDocument()
  })

  it('renders overdue badge and red border when task is overdue', () => {
    const overdueTask: TaskResponse = {
      ...baseTask,
      dueDate: '2020-01-01T00:00:00Z',
      completedAt: null,
    }

    const { container } = renderWithDnd(<TaskCard task={overdueTask} isDoneColumn={false} />)

    const badge = screen.getByTestId('task-card-overdue-badge')
    expect(badge).toBeInTheDocument()
    expect(badge.textContent).toContain('Quá hạn')

    // Top-level draggable div has red borderLeft
    const cardEl = container.firstChild as HTMLElement
    expect(cardEl.style.borderLeft).toMatch(/3px solid (?:#ef4444|rgb\(239,\s*68,\s*68\))/)
  })

  it('does not render overdue badge when overdue task is completed or in done column', () => {
    const overdueCompletedTask: TaskResponse = {
      ...baseTask,
      dueDate: '2020-01-01T00:00:00Z',
      completedAt: '2020-01-02T00:00:00Z',
    }

    const { rerender } = renderWithDnd(
      <TaskCard task={overdueCompletedTask} isDoneColumn={false} />
    )
    expect(screen.queryByTestId('task-card-overdue-badge')).not.toBeInTheDocument()

    // Test in done column without completedAt
    const overdueInDoneColumn: TaskResponse = {
      ...baseTask,
      dueDate: '2020-01-01T00:00:00Z',
      completedAt: null,
    }
    rerender(<DndContext><TaskCard task={overdueInDoneColumn} isDoneColumn={true} /></DndContext>)
    expect(screen.queryByTestId('task-card-overdue-badge')).not.toBeInTheDocument()
  })

  it('renders DD/MM format when due date is in the future', () => {
    const futureTask: TaskResponse = {
      ...baseTask,
      dueDate: '2099-12-25T00:00:00Z',
      completedAt: null,
    }

    renderWithDnd(<TaskCard task={futureTask} />)
    expect(screen.getByText('25/12')).toBeInTheDocument()
    expect(screen.queryByTestId('task-card-overdue-badge')).not.toBeInTheDocument()
  })

  it('does not render due date section when dueDate is null', () => {
    const noDueDateTask: TaskResponse = {
      ...baseTask,
      dueDate: null,
    }

    const { container } = renderWithDnd(<TaskCard task={noDueDateTask} />)
    expect(screen.queryByTestId('task-card-overdue-badge')).not.toBeInTheDocument()
    expect(container.querySelector('.anticon-calendar')).toBeNull()
  })

  it('preserves priority tag and labels when overdue', () => {
    const overdueWithLabels: TaskResponse = {
      ...baseTask,
      priority: 'Urgent',
      dueDate: '2020-01-01T00:00:00Z',
      labels: [
        { id: 'l1', workspaceId: 'w1', name: 'Frontend', color: '#6366f1', createdAt: '' },
      ],
    }

    renderWithDnd(<TaskCard task={overdueWithLabels} />)
    expect(screen.getByTestId('task-card-overdue-badge')).toBeInTheDocument()
    expect(screen.getByText('Khẩn cấp')).toBeInTheDocument()
    expect(screen.getByText('Frontend')).toBeInTheDocument()
  })

  it('renders GitHub PR chip when task has githubPrNumber and githubPrUrl', () => {
    const taskWithGithub: TaskResponse = {
      ...baseTask,
      githubPrNumber: 142,
      githubPrUrl: 'https://github.com/Thinh-TT/TeamNexus/pull/142',
      githubPrStatus: 'merged',
    }

    renderWithDnd(<TaskCard task={taskWithGithub} />)
    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #142 Merged')
  })

  it('renders GitHub Issue chip when task has githubIssueNumber and githubIssueUrl', () => {
    const taskWithIssue: TaskResponse = {
      ...baseTask,
      githubIssueNumber: 89,
      githubIssueUrl: 'https://github.com/Thinh-TT/TeamNexus/issues/89',
    }

    renderWithDnd(<TaskCard task={taskWithIssue} />)
    const chip = screen.getByTestId('github-issue-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('Issue #89')
  })

  it('does not render GitHub chip when task has no GitHub information', () => {
    renderWithDnd(<TaskCard task={baseTask} />)
    expect(screen.queryByTestId('github-pr-chip')).toBeNull()
    expect(screen.queryByTestId('github-issue-chip')).toBeNull()
  })
})
