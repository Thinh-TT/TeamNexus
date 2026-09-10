import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ObserverRunsDrawer } from '../ObserverRunsDrawer'
import { observerApi } from '../../services/observerApi'
import type {
  ObserverRunDetailResponse,
  ObserverRunResponse,
} from '../../types/notification.types'

vi.mock('../../services/observerApi', () => ({
  observerApi: {
    listObserverRuns: vi.fn(),
    getObserverRun: vi.fn(),
    triggerObserverScan: vi.fn(),
  },
}))

describe('ObserverRunsDrawer', () => {
  const mockRuns: ObserverRunResponse[] = [
    {
      id: 'run-1',
      workspaceId: 'ws-1',
      status: 'Completed',
      startedAt: '2026-09-10T12:00:00Z',
      finishedAt: '2026-09-10T12:00:05Z',
      signalsDetected: 2,
      findingsWritten: 2,
      notificationsCreated: 1,
      aiCalled: true,
    },
    {
      id: 'run-2',
      workspaceId: 'ws-1',
      status: 'Failed',
      startedAt: '2026-09-10T11:00:00Z',
      finishedAt: '2026-09-10T11:00:02Z',
      signalsDetected: 1,
      findingsWritten: 0,
      notificationsCreated: 0,
      aiCalled: false,
    },
  ]

  const mockRunDetail: ObserverRunDetailResponse = {
    id: 'run-1',
    workspaceId: 'ws-1',
    status: 'Completed',
    startedAt: '2026-09-10T12:00:00Z',
    finishedAt: '2026-09-10T12:00:05Z',
    signalsDetected: 2,
    findingsWritten: 2,
    notificationsCreated: 1,
    aiCalled: true,
    summary: {
      durationMs: 2400,
      promptTokens: 100,
      completionTokens: 50,
    },
    findings: [
      {
        type: 'OverdueTask',
        severity: 'High',
        title: 'Task trễ hạn nghiêm trọng',
        message: 'Task A đã quá hạn 10 ngày',
        taskIds: ['task-1'],
        userIds: [],
      },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(observerApi.listObserverRuns).mockResolvedValue(mockRuns)
    vi.mocked(observerApi.getObserverRun).mockResolvedValue(mockRunDetail)
  })

  const renderComponent = (open = true) => {
    return render(
      <ObserverRunsDrawer
        open={open}
        onClose={vi.fn()}
        workspaceId="ws-1"
        onScanFinished={vi.fn()}
      />
    )
  }

  it('renders drawer header, scan button, and runs list with status tags', async () => {
    renderComponent(true)

    expect(
      await screen.findByText('AI Observer — Lịch sử quét')
    ).toBeInTheDocument()
    expect(screen.getByText('Quét ngay')).toBeInTheDocument()
    expect(await screen.findByText('HOÀN TẤT')).toBeInTheDocument()
    expect(screen.getByText('THẤT BẠI')).toBeInTheDocument()
    expect(screen.getAllByText('Xem chi tiết →').length).toBe(2)
  })

  it('opens run detail view when clicking a run card', async () => {
    renderComponent(true)

    const detailBtns = await screen.findAllByText('Xem chi tiết →')
    fireEvent.click(detailBtns[0])

    expect(await screen.findByText('Chi tiết đợt quét')).toBeInTheDocument()
    expect(screen.getByText('Thông tin đợt quét')).toBeInTheDocument()
    expect(
      await screen.findByText('Task trễ hạn nghiêm trọng')
    ).toBeInTheDocument()
    expect(screen.getByText('2400 ms')).toBeInTheDocument()
  })

  it('triggers observer scan on clicking Quét ngay', async () => {
    vi.mocked(observerApi.triggerObserverScan).mockResolvedValueOnce({
      runId: 'run-3',
      workspaceId: 'ws-1',
      status: 'Completed',
      signalsDetected: 0,
      findingsWritten: 0,
      notificationsCreated: 0,
      aiCalled: false,
    })

    renderComponent(true)

    const scanBtn = await screen.findByText('Quét ngay')
    fireEvent.click(scanBtn)

    // Confirm inside Popconfirm
    const confirmBtn = await screen.findByRole('button', { name: 'Quét ngay' })
    fireEvent.click(confirmBtn)

    expect(observerApi.triggerObserverScan).toHaveBeenCalledWith('ws-1')
  })
})
