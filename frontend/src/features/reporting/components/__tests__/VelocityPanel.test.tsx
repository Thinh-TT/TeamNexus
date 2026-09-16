import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { VelocityPanel } from '../VelocityPanel'
import type { ReportProgressSeriesResponse } from '../../types/reporting.types'

/**
 * Giai đoạn 13 §4 — khối chỉ số năng suất.
 *
 * Điểm mấu chốt: các con số lấy **nguyên** từ `velocity` mà server trả, **không** tính lại từ mảng
 * bucket đã hiển thị (biểu đồ có thể đã cắt còn 31 cột, còn năng suất tính trên toàn cửa sổ). Nếu client
 * tự tính, hai chỗ trong cùng một trang sẽ nói hai số khác nhau.
 *
 * Lưu ý về cách assert: antd `Statistic` tách phần nguyên và phần thập phân thành **hai** `<span>`, nên
 * `getByText('4.5')` không khớp được. Đọc `textContent` của cả khối là cách kiểm tra đúng thứ người dùng
 * nhìn thấy.
 */
const series = (over: Partial<ReportProgressSeriesResponse> = {}): ReportProgressSeriesResponse => ({
  workspaceId: 'ws-1',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-06-01T00:00:00Z', to: '2026-06-30T00:00:00Z', days: 30, clamped: false },
  mode: 'date',
  bucketDays: 1,
  days: [{ date: '2026-06-01', openTasks: 5, completions: 2, creations: 3 }],
  weeks: [],
  velocity: { avgCompletionsPerWeek: 4.5, completedInRange: 18, openAtEnd: 7 },
  metricDefinitions: {},
  truncated: { bucketCapReached: false, maxBuckets: 90 },
  tzOffsetMinutes: 0,
  ...over,
})

/** Toàn bộ chữ của một `Statistic`, đã gộp khoảng trắng. */
const statText = (testId: string): string =>
  (screen.getByTestId(testId).textContent ?? '').replace(/\s+/g, '')

describe('VelocityPanel', () => {
  it('hiển thị cả 3 chỉ số lấy từ server', () => {
    render(<VelocityPanel series={series()} />)

    expect(screen.getByTestId('velocity-avg')).toBeInTheDocument()
    expect(screen.getByTestId('velocity-completed')).toBeInTheDocument()
    expect(screen.getByTestId('velocity-open')).toBeInTheDocument()

    expect(screen.getByText('Năng suất trung bình / tuần')).toBeInTheDocument()
    expect(statText('velocity-avg')).toContain('4.5')
    expect(statText('velocity-completed')).toContain('18')
    expect(statText('velocity-open')).toContain('7')
  })

  it('avgCompletionsPerWeek = 0 ⇒ hiện 0 (không "—" khó hiểu)', () => {
    render(
      <VelocityPanel
        series={series({
          velocity: { avgCompletionsPerWeek: 0, completedInRange: 0, openAtEnd: 0 },
        })}
      />
    )

    expect(statText('velocity-avg')).toContain('0')
    expect(statText('velocity-completed')).toContain('0')
    expect(statText('velocity-open')).toContain('0')
  })

  it('series = null ⇒ vẫn render khối với số 0, không crash', () => {
    render(<VelocityPanel series={null} />)

    expect(screen.getByTestId('velocity-panel')).toBeInTheDocument()
    expect(statText('velocity-avg')).toContain('0')
    expect(statText('velocity-completed')).toContain('0')
    expect(statText('velocity-open')).toContain('0')
  })

  it('giá trị không hữu hạn ⇒ hiện 0 thay vì NaN', () => {
    render(
      <VelocityPanel
        series={series({
          velocity: {
            avgCompletionsPerWeek: Number.NaN,
            completedInRange: 0,
            openAtEnd: 0,
          },
        })}
      />
    )

    expect(statText('velocity-avg')).not.toContain('NaN')
    expect(statText('velocity-avg')).toContain('0')
  })

  it('nói rõ chỉ số tính trên toàn kỳ khi bucket đã bị cap', () => {
    render(
      <VelocityPanel series={series({ truncated: { bucketCapReached: true, maxBuckets: 90 } })} />
    )

    expect(screen.getByText(/toàn bộ khoảng thời gian/i)).toBeInTheDocument()
  })

  it('không hiện ghi chú cap khi không bị cap', () => {
    render(<VelocityPanel series={series()} />)

    expect(screen.queryByText(/toàn bộ khoảng thời gian/i)).not.toBeInTheDocument()
  })
})
