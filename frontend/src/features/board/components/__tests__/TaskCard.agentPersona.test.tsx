import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { TaskCard } from '../TaskCard'
import { agentApi } from '../../../ai/services/agentApi'
import type { AgentRunResponse } from '../../../ai/types/agentRun.types'
import type { TaskResponse } from '../../types/board.types'

/**
 * Phase 14.7 §B — "which agent is on this card?".
 *
 * <p>A separate file rather than another case in `TaskCard.test.tsx`: that suite runs the card with the
 * real `agentApi` (its run fetch simply fails and is swallowed), and this one needs a controlled run to
 * prove the card names the persona the run actually executed as.</p>
 */

vi.mock('../../../ai/services/agentApi', () => ({
  agentApi: {
    listRuns: vi.fn(),
  },
}))

const run: AgentRunResponse = {
  id: 'run-1',
  taskId: 'task-1',
  boardId: 'board-1',
  agentUserId: 'agent-1',
  agentDisplayName: 'AI QA & Security',
  triggeredByUserId: 'user-1',
  triggeredByName: 'Manager',
  status: 'Running',
  stopReason: null,
  clarificationQuestion: null,
  aiActionLogId: null,
  outputKind: null,
  error: null,
  toolCallCount: 0,
  llmCallCount: 0,
  promptTokens: 0,
  completionTokens: 0,
  totalTokens: 0,
  startedAt: '2026-09-12T10:00:00Z',
  finishedAt: null,
  traceTruncated: false,
}

const agentTask: TaskResponse = {
  id: 'task-1',
  boardId: 'board-1',
  columnId: 'col-1',
  title: 'Rà soát bảo mật luồng đăng nhập',
  description: null,
  position: 0,
  priority: 'High',
  dueDate: null,
  // The AI member has no configured display name on this workspace — the common case this change is about.
  assigneeId: 'agent-1',
  assigneeName: null,
  createdAt: '',
  updatedAt: '',
  completedAt: null,
  labels: [],
  commentCount: 0,
  assigneeIsAiAgent: true,
  activeAgentRunId: 'run-1',
}

const renderCard = (task: TaskResponse) =>
  render(
    <DndContext>
      <TaskCard task={task} />
    </DndContext>
  )

describe('TaskCard — the agent persona on the card (Phase 14.7 §B)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(agentApi.listRuns).mockResolvedValue([run])
  })

  it('TASKCARD_PERSONA1_names the persona the run executed as when the assignee has no name', async () => {
    renderCard(agentTask)

    const avatar = await screen.findByTestId('agent-assignee-avatar')

    await waitFor(() =>
      expect(avatar).toHaveAttribute('aria-label', 'Người phụ trách: AI QA & Security')
    )
    // The guess this replaced: a generic "AI Agent" that told the user nothing.
    expect(avatar.getAttribute('aria-label')).not.toBe('Người phụ trách: AI Agent')
  })

  it('TASKCARD_PERSONA2_prefers the assignee\'s own configured name when there is one', async () => {
    renderCard({ ...agentTask, assigneeName: 'Trợ lý của nhóm' })

    const avatar = await screen.findByTestId('agent-assignee-avatar')

    expect(avatar).toHaveAttribute('aria-label', 'Người phụ trách: Trợ lý của nhóm')
  })

  it('TASKCARD_PERSONA3_keeps the neutral label when no run name is available', async () => {
    vi.mocked(agentApi.listRuns).mockResolvedValue([])

    renderCard(agentTask)

    const avatar = await screen.findByTestId('agent-assignee-avatar')

    // No name from the run and none from the member: the card says "AI Agent" rather than blaming a persona.
    await waitFor(() => expect(avatar).toHaveAttribute('aria-label', 'Người phụ trách: AI Agent'))
  })
})
