import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useReportProgressSeries } from '../useReportProgressSeries'
import { reportingApi } from '../../services/reportingApi'
import type { ReportProgressSeriesResponse } from '../../types/reporting.types'

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    getProgressSeries: vi.fn(),
  },
}))

/**
 * Giai đoạn 13 §4 — hook nạp chuỗi thời gian.
 *
 * Hai hành vi được khoá chặt vì chúng là hợp đồng với `ReportsPage`:
 *  1. `workspaceId` rỗng ⇒ **KHÔNG** gọi API (đó là cách trang chặn Member trước khi request đi).
 *  2. `tzOffsetMinutes` **luôn** được gửi, kể cả khi bằng 0.
 */
const mockSeries: ReportProgressSeriesResponse = {
  workspaceId: 'ws-1',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-06-01T00:00:00Z', to: '2026-06-07T00:00:00Z', days: 7, clamped: false },
  mode: 'date',
  bucketDays: 1,
  days: [
    { date: '2026-06-01', openTasks: 3, completions: 1, creations: 2 },
    { date: '2026-06-02', openTasks: 2, completions: 1, creations: 0 },
  ],
  weeks: [],
  velocity: { avgCompletionsPerWeek: 2, completedInRange: 2, openAtEnd: 2 },
  metricDefinitions: {},
  truncated: { bucketCapReached: false, maxBuckets: 90 },
  tzOffsetMinutes: 420,
}

describe('useReportProgressSeries', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('nạp chuỗi khi mount và chuyển sang idle', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValueOnce(mockSeries)

    const { result } = renderHook(() => useReportProgressSeries('ws-1'))

    expect(result.current.status).toBe('loading')

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    expect(result.current.series).toEqual(mockSeries)
    expect(result.current.error).toBeNull()
  })

  it('LUÔN gửi tzOffsetMinutes (không bị coi là tham số rỗng)', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValueOnce(mockSeries)

    renderHook(() => useReportProgressSeries('ws-1'))

    await waitFor(() => {
      expect(reportingApi.getProgressSeries).toHaveBeenCalled()
    })

    const call = vi.mocked(reportingApi.getProgressSeries).mock.calls[0]
    expect(call[0]).toBe('ws-1')
    expect(typeof call[1]?.tzOffsetMinutes).toBe('number')
    expect(Number.isFinite(call[1]!.tzOffsetMinutes!)).toBe(true)
  })

  it('chuyển tham số lọc xuống API', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValueOnce(mockSeries)

    renderHook(() =>
      useReportProgressSeries('ws-1', {
        boardId: 'board-1',
        from: '2026-06-01T00:00:00Z',
        to: '2026-06-07T00:00:00Z',
      })
    )

    await waitFor(() => {
      expect(reportingApi.getProgressSeries).toHaveBeenCalled()
    })

    expect(vi.mocked(reportingApi.getProgressSeries).mock.calls[0][1]).toMatchObject({
      boardId: 'board-1',
      from: '2026-06-01T00:00:00Z',
      to: '2026-06-07T00:00:00Z',
    })
  })

  it('workspaceId rỗng ⇒ KHÔNG gọi API và giữ trạng thái idle', async () => {
    const { result } = renderHook(() => useReportProgressSeries(''))

    // Một nhịp để mọi effect (nếu có) chạy xong.
    await act(async () => {
      await Promise.resolve()
    })

    expect(reportingApi.getProgressSeries).not.toHaveBeenCalled()
    expect(result.current.status).toBe('idle')
    expect(result.current.series).toBeNull()
  })

  it('403 ⇒ status error + httpStatus + message tiếng Việt', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockRejectedValueOnce({
      response: { status: 403, data: { error: 'Chỉ quản lý mới xem được báo cáo.' } },
    })

    const { result } = renderHook(() => useReportProgressSeries('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('error')
    })

    expect(result.current.httpStatus).toBe(403)
    expect(result.current.error).toBe('Chỉ quản lý mới xem được báo cáo.')
    // Lỗi **không** xoá dữ liệu cũ: nếu đã có biểu đồ thì nó vẫn dùng được.
    expect(result.current.series).toBeNull()
  })

  it('đổi boardId ⇒ gọi lại API', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValue(mockSeries)

    const { rerender } = renderHook(
      ({ boardId }: { boardId?: string }) => useReportProgressSeries('ws-1', { boardId }),
      { initialProps: { boardId: undefined as string | undefined } }
    )

    await waitFor(() => {
      expect(reportingApi.getProgressSeries).toHaveBeenCalledTimes(1)
    })

    rerender({ boardId: 'board-2' })

    await waitFor(() => {
      expect(reportingApi.getProgressSeries).toHaveBeenCalledTimes(2)
    })

    expect(vi.mocked(reportingApi.getProgressSeries).mock.calls[1][1]).toMatchObject({
      boardId: 'board-2',
    })
  })

  it('reload gọi lại API', async () => {
    vi.mocked(reportingApi.getProgressSeries).mockResolvedValue(mockSeries)

    const { result } = renderHook(() => useReportProgressSeries('ws-1'))

    await waitFor(() => {
      expect(result.current.status).toBe('idle')
    })

    await act(async () => {
      await result.current.reload()
    })

    expect(reportingApi.getProgressSeries).toHaveBeenCalledTimes(2)
  })
})
