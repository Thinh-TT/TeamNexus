import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RecoveryPlanCard } from '../RecoveryPlanCard'
import { aiActionApi } from '../../services/aiActionApi'
import type { AiActionLog } from '../../types/aiAction.types'

vi.mock('../../services/aiActionApi', () => ({
  aiActionApi: {
    approveAiAction: vi.fn(),
    rejectAiAction: vi.fn(),
    undoAiAction: vi.fn(),
  },
}))

describe('RecoveryPlanCard', () => {
  const onActionSuccess = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders EscalateManager plan with prominent warning', () => {
    const actionLog: AiActionLog = {
      id: 'log-1',
      workspaceId: 'ws-1',
      actionType: 'RecoveryPlan',
      status: 'Pending',
      basisJson: JSON.stringify({
        kind: 'EscalateManager',
        reason: 'Nhiều rủi ro tiến độ nghiêm trọng',
      }),
      createdAt: '2026-10-15T08:00:00Z',
    }

    render(
      <RecoveryPlanCard actionLog={actionLog} onActionSuccess={onActionSuccess} />
    )

    expect(screen.getByTestId('escalate-manager-warning')).toBeInTheDocument()
    expect(screen.getByText('Cần họp khẩn cấp')).toBeInTheDocument()
    expect(
      screen.getByText(/không có thay đổi dữ liệu nào được áp dụng/i)
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Phê duyệt giải pháp/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Từ chối/i })).toBeInTheDocument()
  })

  it('approves a pending recovery plan on click', async () => {
    const actionLog: AiActionLog = {
      id: 'log-2',
      workspaceId: 'ws-1',
      actionType: 'RecoveryPlan',
      status: 'Pending',
      basisJson: JSON.stringify({
        kind: 'ExtendDeadline',
        newDueDate: '2026-10-25T00:00:00Z',
      }),
      createdAt: '2026-10-15T08:00:00Z',
    }

    vi.mocked(aiActionApi.approveAiAction).mockResolvedValue({
      ...actionLog,
      status: 'Approved',
      approvedAt: '2026-10-15T08:05:00Z',
      beforeSnapshotJson: null,
      afterSnapshotJson: actionLog.basisJson,
      isReversible: true,
      reversedAt: null,
    })

    render(
      <RecoveryPlanCard actionLog={actionLog} onActionSuccess={onActionSuccess} />
    )

    const approveBtn = screen.getByRole('button', { name: /Phê duyệt giải pháp/i })
    fireEvent.click(approveBtn)

    await waitFor(() => {
      expect(aiActionApi.approveAiAction).toHaveBeenCalledWith('log-2')
      expect(onActionSuccess).toHaveBeenCalled()
    })
  })

  it('renders undo button when recovery plan is already Approved', async () => {
    const actionLog: AiActionLog = {
      id: 'log-3',
      workspaceId: 'ws-1',
      actionType: 'RecoveryPlan',
      status: 'Approved',
      basisJson: JSON.stringify({
        kind: 'ReassignTask',
        reassignUserId: 'u-target',
      }),
      createdAt: '2026-10-15T08:00:00Z',
    }

    vi.mocked(aiActionApi.undoAiAction).mockResolvedValue({
      ...actionLog,
      status: 'RolledBack',
      approvedAt: '2026-10-15T08:05:00Z',
      beforeSnapshotJson: null,
      afterSnapshotJson: null,
      isReversible: false,
      reversedAt: '2026-10-15T08:10:00Z',
    })

    render(
      <RecoveryPlanCard actionLog={actionLog} onActionSuccess={onActionSuccess} />
    )

    const undoBtn = screen.getByRole('button', { name: /Hoàn tác \(Undo\)/i })
    fireEvent.click(undoBtn)

    await waitFor(() => {
      expect(aiActionApi.undoAiAction).toHaveBeenCalledWith('log-3')
      expect(onActionSuccess).toHaveBeenCalled()
    })
  })

  /**
   * ISSUE-04 regression guard: no Tailwind classes, real Ant Design structure.
   */
  describe('ISSUE-04: no Tailwind, real Ant Design structure', () => {
    it('never emits a Tailwind utility class anywhere in the tree', () => {
      const actionLog: AiActionLog = {
        id: 'log-guard',
        workspaceId: 'ws-1',
        actionType: 'RecoveryPlan',
        status: 'Pending',
        basisJson: JSON.stringify({
          kind: 'EscalateManager',
          reason: 'Báo cáo họp khẩn',
        }),
        createdAt: '2026-10-15T08:00:00Z',
      }

      const { container } = render(
        <RecoveryPlanCard actionLog={actionLog} onActionSuccess={onActionSuccess} />
      )

      const tailwindish =
        /(^|\s)(bg-|text-\[|border-|space-[xy]-|grid-cols-|gap-\d|p-\d|px-\d|py-\d|rounded-|flex-wrap|backdrop-|animate-|shrink-0)/

      const offenders: string[] = []
      container.querySelectorAll('*').forEach((el) => {
        const cls = el.getAttribute('class') || ''
        if (cls.split(/\s+/).some((token) => tailwindish.test(token))) {
          offenders.push(`<${el.tagName.toLowerCase()} class="${cls}">`)
        }
      })

      expect(
        offenders,
        `Found Tailwind classes in RecoveryPlanCard — project uses pure Ant Design:\n${offenders.join('\n')}`
      ).toEqual([])
    })
  })
})

