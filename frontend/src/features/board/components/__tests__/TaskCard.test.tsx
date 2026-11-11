import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
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

  it('renders TN-42 monospace badge when taskNumber > 0 and omits it when absent/0', () => {
    const numberedTask: TaskResponse = {
      ...baseTask,
      priority: null,
      labels: [],
      taskNumber: 42,
    }

    const { rerender } = renderWithDnd(<TaskCard task={numberedTask} />)
    const codeBadge = screen.getByTestId('task-card-number')
    expect(codeBadge).toHaveTextContent('TN-42')
    expect(codeBadge.style.fontFamily).toContain('JetBrains Mono')

    rerender(
      <DndContext>
        <TaskCard task={{ ...numberedTask, taskNumber: 0 }} />
      </DndContext>
    )
    expect(screen.queryByTestId('task-card-number')).not.toBeInTheDocument()
  })

  it('renders GitHub PR and Issue state chips with overflow indicator when > 3 links', () => {
    const linkedTask: TaskResponse = {
      ...baseTask,
      taskNumber: 42,
      externalLinks: [
        {
          id: 'lnk-1',
          provider: 'github',
          linkType: 'pull_request',
          externalId: 'octocat/TeamNexus#88',
          externalUrl: 'https://github.com/octocat/TeamNexus/pull/88',
          title: 'feat: implement oauth',
          state: 'merged',
          updatedAt: '2026-11-10T10:00:00Z',
        },
        {
          id: 'lnk-2',
          provider: 'github',
          linkType: 'issue',
          externalId: 'octocat/TeamNexus#12',
          externalUrl: 'https://github.com/octocat/TeamNexus/issues/12',
          title: 'Login bug',
          state: 'open',
          updatedAt: '2026-11-10T10:00:00Z',
        },
        {
          id: 'lnk-3',
          provider: 'github',
          linkType: 'pull_request',
          externalId: 'octocat/TeamNexus#89',
          externalUrl: 'https://github.com/octocat/TeamNexus/pull/89',
          title: 'wip',
          state: 'draft',
          updatedAt: '2026-11-10T10:00:00Z',
        },
        {
          id: 'lnk-4',
          provider: 'github',
          linkType: 'pull_request',
          externalId: 'octocat/TeamNexus#90',
          externalUrl: 'https://github.com/octocat/TeamNexus/pull/90',
          title: 'old pr',
          state: 'closed',
          updatedAt: '2026-11-10T10:00:00Z',
        },
      ],
    }

    renderWithDnd(<TaskCard task={linkedTask} />)
    expect(screen.getByTestId('task-card-external-links')).toBeInTheDocument()
    expect(screen.getByText('PR #88 · Merged')).toBeInTheDocument()
    expect(screen.getByText('Issue #12 · Open')).toBeInTheDocument()
    expect(screen.getByText('PR #89 · Draft')).toBeInTheDocument()
    expect(screen.getByText('+1')).toBeInTheDocument()
  })

  it('does not trigger card onClick when clicking a GitHub chip link', () => {
    const onClick = vi.fn()
    const linkedTask: TaskResponse = {
      ...baseTask,
      externalLinks: [
        {
          id: 'lnk-1',
          provider: 'github',
          linkType: 'pull_request',
          externalId: 'octocat/TeamNexus#88',
          externalUrl: 'https://github.com/octocat/TeamNexus/pull/88',
          title: 'feat: implement oauth',
          state: 'merged',
          updatedAt: '2026-11-10T10:00:00Z',
        },
      ],
    }

    renderWithDnd(<TaskCard task={linkedTask} onClick={onClick} />)
    const chipLink = screen.getByText('PR #88 · Merged').closest('a')
    expect(chipLink).toHaveAttribute('href', 'https://github.com/octocat/TeamNexus/pull/88')
    expect(chipLink).toHaveAttribute('target', '_blank')

    fireEvent.click(chipLink!)
    expect(onClick).not.toHaveBeenCalled()
  })
})
