import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AgentRunPanel } from '../AgentRunPanel'
import type { AgentRunDetailResponse, AgentRunResponse } from '../../types/agentRun.types'

describe('AgentRunPanel', () => {
  const baseRun: AgentRunResponse = {
    id: 'run-1',
    taskId: 'task-1',
    boardId: 'board-1',
    agentUserId: 'agent-1',
    agentDisplayName: 'TeamNexus Agent',
    triggeredByUserId: 'user-1',
    triggeredByName: 'Manager',
    status: 'Running',
    stopReason: null,
    clarificationQuestion: null,
    aiActionLogId: null,
    outputKind: null,
    error: null,
    toolCallCount: 3,
    llmCallCount: 2,
    promptTokens: 200,
    completionTokens: 100,
    totalTokens: 300,
    startedAt: '2026-09-12T10:00:00Z',
    finishedAt: null,
    traceTruncated: false,
  }

  const baseDetail: AgentRunDetailResponse = {
    run: baseRun,
    toolCallTrace: [
      {
        name: 'SearchSystemData',
        arguments: '{"term":"security"}',
        resultSummary: 'Success',
        isError: false,
        at: '2026-09-12T10:00:02Z',
        durationMs: 80,
      },
    ],
    previousRunId: null,
    previousQuestion: null,
    resolutionCommentContent: null,
  }

  const defaultProps = {
    taskId: 'task-1',
    currentRun: baseRun,
    runDetail: baseDetail,
    loading: false,
    actionLoading: false,
    isManagerOrAdmin: true,
    onStartRun: vi.fn(),
    onRerun: vi.fn(),
    onCancelRun: vi.fn(),
    onRefresh: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders status "Đang chạy" and disables "Chạy Agent" button when running', () => {
    render(<AgentRunPanel {...defaultProps} />)

    expect(screen.getByText('Đang chạy')).toBeInTheDocument()
    const startBtn = screen.getByTestId('start-agent-btn')
    expect(startBtn).toBeDisabled()
  })

  it('renders "Chạy lại" button when status is AwaitingClarification', () => {
    const waitingRun: AgentRunResponse = {
      ...baseRun,
      status: 'AwaitingClarification',
      stopReason: 'QuestionAsked',
      clarificationQuestion: 'Vui lòng chọn cơ sở dữ liệu phù hợp?',
    }

    render(
      <AgentRunPanel
        {...defaultProps}
        currentRun={waitingRun}
        runDetail={{ ...baseDetail, run: waitingRun }}
      />
    )

    expect(screen.getByText('Chờ làm rõ')).toBeInTheDocument()
    const rerunBtn = screen.getByTestId('rerun-btn')
    expect(rerunBtn).toBeInTheDocument()
    expect(screen.getByTestId('clarification-alert')).toBeInTheDocument()
    expect(
      screen.getByText(/Vui lòng chọn cơ sở dữ liệu phù hợp\?/)
    ).toBeInTheDocument()

    fireEvent.click(rerunBtn)
    expect(defaultProps.onRerun).toHaveBeenCalledWith('run-1')
  })

  it('hides control buttons when user is only a Member', () => {
    render(<AgentRunPanel {...defaultProps} isManagerOrAdmin={false} />)

    expect(screen.queryByTestId('start-agent-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('rerun-btn')).not.toBeInTheDocument()
    expect(screen.queryByTestId('cancel-btn')).not.toBeInTheDocument()
  })

  it('renders error alert when status is Failed', () => {
    const failedRun: AgentRunResponse = {
      ...baseRun,
      status: 'Failed',
      stopReason: 'ToolLimit',
    }

    render(
      <AgentRunPanel
        {...defaultProps}
        currentRun={failedRun}
        runDetail={{ ...baseDetail, run: failedRun }}
      />
    )

    expect(screen.getByText('Thất bại')).toBeInTheDocument()
    expect(screen.getByTestId('agent-error-alert')).toBeInTheDocument()
    expect(
      screen.getByText(/Đã đạt giới hạn số lần gọi công cụ/)
    ).toBeInTheDocument()
  })

  it('displays metric counters for tools and tokens', () => {
    render(<AgentRunPanel {...defaultProps} />)

    expect(screen.getByText('3')).toBeInTheDocument() // tool call count
    expect(screen.getByText('300')).toBeInTheDocument() // total tokens
  })
})
