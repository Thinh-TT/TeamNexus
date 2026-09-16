import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { BurndownChart } from '../BurndownChart'
import type { ReportProgressSeriesResponse } from '../../types/reporting.types'

/**
 * Giai đoạn 13 §4 — biểu đồ Burndown.
 *
 * Suite này khoá những thứ mà một thư viện biểu đồ thường lo giúp, nhưng ở đây phải tự lo: **chia 0 khi
 * board rỗng**, **cắt bớt cột khi cửa sổ dài**, và **nói rõ khi đã cắt** (không âm thầm bỏ dữ liệu).
 */

const series = (over: Partial<ReportProgressSeriesResponse> = {}): ReportProgressSeriesResponse => ({
  workspaceId: 'ws-1',
  scope: { type: 'workspace', boardId: null, boardName: null },
  period: { from: '2026-06-01T00:00:00Z', to: '2026-06-07T00:00:00Z', days: 7, clamped: false },
  mode: 'date',
  bucketDays: 1,
  days: [
    { date: '2026-06-01', openTasks: 3, completions: 1, creations: 2 },
    { date: '2026-06-02', openTasks: 2, completions: 1, creations: 1 },
    { date: '2026-06-03', openTasks: 1, completions: 1, creations: 0 },
  ],
  weeks: [],
  velocity: { avgCompletionsPerWeek: 3, completedInRange: 3, openAtEnd: 1 },
  metricDefinitions: {},
  truncated: { bucketCapReached: false, maxBuckets: 90 },
  tzOffsetMinutes: 420,
  ...over,
})

const barHeight = (testId: string): string => {
  const node = screen.getByTestId(testId)
  return node.style.height
}

describe('BurndownChart', () => {
  it('vẽ đúng một nhóm cột cho mỗi mốc', () => {
    render(<BurndownChart series={series()} />)

    expect(screen.getByTestId('burndown-bucket-2026-06-01')).toBeInTheDocument()
    expect(screen.getByTestId('burndown-bucket-2026-06-02')).toBeInTheDocument()
    expect(screen.getByTestId('burndown-bucket-2026-06-03')).toBeInTheDocument()
  })

  it('chiều cao cột tỉ lệ với giá trị lớn nhất', () => {
    render(<BurndownChart series={series()} />)

    // max = 3 (openTasks ngày đầu) ⇒ 3/3 = 100%, 2/3 = 67%, 1/3 = 33%.
    expect(barHeight('burndown-open-2026-06-01')).toBe('100%')
    expect(barHeight('burndown-open-2026-06-02')).toBe('67%')
    expect(barHeight('burndown-open-2026-06-03')).toBe('33%')
  })

  it('board rỗng (mọi giá trị 0) ⇒ cột cao 0%, KHÔNG NaN và không vỡ layout', () => {
    render(
      <BurndownChart
        series={series({
          days: [
            { date: '2026-06-01', openTasks: 0, completions: 0, creations: 0 },
            { date: '2026-06-02', openTasks: 0, completions: 0, creations: 0 },
          ],
        })}
      />
    )

    const empty = screen.getByTestId('burndown-bucket-2026-06-01')
    expect(empty).toBeInTheDocument()

    expect(barHeight('burndown-open-2026-06-01')).toBe('0%')
    expect(barHeight('burndown-completions-2026-06-01')).toBe('0%')
    expect(barHeight('burndown-open-2026-06-01')).not.toContain('NaN')
  })

  it('mode = week ⇒ nhãn "Tuần DD/MM"', () => {
    render(
      <BurndownChart
        series={series({
          mode: 'week',
          bucketDays: 7,
          days: [],
          weeks: [
            { weekStart: '2026-06-01', completions: 4, creations: 6, openAtEnd: 5 },
            { weekStart: '2026-06-08', completions: 2, creations: 1, openAtEnd: 3 },
          ],
        })}
      />
    )

    expect(screen.getAllByText(/Tuần 01\/06/).length).toBeGreaterThan(0)
    expect(screen.getByTestId('burndown-bucket-W2026-06-01')).toBeInTheDocument()
  })

  it('không có bucket nào ⇒ Empty tiếng Việt, không màn hình trắng', () => {
    render(<BurndownChart series={series({ days: [] })} />)

    expect(screen.getByTestId('burndown-chart')).toBeInTheDocument()
    expect(screen.getByText(/Chưa có dữ liệu để vẽ biểu đồ tiến độ/i)).toBeInTheDocument()
  })

  it('series = null ⇒ Empty (đang tải hoặc lỗi)', () => {
    render(<BurndownChart series={null} />)

    expect(screen.getByText(/Chưa có dữ liệu/i)).toBeInTheDocument()
  })

  it('series = null + loading ⇒ thông báo đang tải', () => {
    render(<BurndownChart series={null} loading />)

    expect(screen.getByText(/Đang tải biểu đồ/i)).toBeInTheDocument()
  })

  it('truncated.bucketCapReached ⇒ cảnh báo tiếng Việt nói rõ số mốc hiển thị', () => {
    render(
      <BurndownChart series={series({ truncated: { bucketCapReached: true, maxBuckets: 90 } })} />
    )

    expect(screen.getByText(/chỉ hiển thị 90 mốc gần nhất/i)).toBeInTheDocument()
  })

  it('quá nhiều bucket ⇒ chỉ vẽ 31 mốc cuối VÀ nói rõ đã ẩn bao nhiêu', () => {
    // 40 mốc liên tiếp bắt đầu 2026-06-01 ⇒ mốc cuối là 2026-07-10 (dùng `Date` để không tự cộng ngày
    // bằng tay và không sinh ra "ngày 40").
    const start = new Date('2026-06-01T00:00:00Z')
    const days = Array.from({ length: 40 }, (_, index) => {
      const date = new Date(start)
      date.setUTCDate(date.getUTCDate() + index)
      return {
        date: date.toISOString().slice(0, 10),
        openTasks: index,
        completions: 1,
        creations: 1,
      }
    })

    render(<BurndownChart series={series({ days })} />)

    const rendered = screen.getAllByTestId(/^burndown-bucket-/)
    expect(rendered).toHaveLength(31)

    // Mốc mới nhất vẫn còn, mốc cũ nhất đã bị ẩn, và người dùng được thông báo.
    expect(screen.getByTestId('burndown-bucket-2026-07-10')).toBeInTheDocument()
    expect(screen.queryByTestId('burndown-bucket-2026-06-01')).not.toBeInTheDocument()
    expect(screen.getByText(/không hiển thị/i)).toBeInTheDocument()
  })

  it('hiển thị phạm vi và múi giờ thực dùng để số liệu không gây hiểu nhầm', () => {
    render(<BurndownChart series={series({ tzOffsetMinutes: 420 })} />)

    expect(screen.getByText(/toàn workspace/i)).toBeInTheDocument()
    expect(screen.getByText(/UTC\+7/)).toBeInTheDocument()
    expect(screen.getByText(/mốc theo ngày/i)).toBeInTheDocument()
  })

  it('scope board ⇒ hiện tên board', () => {
    render(
      <BurndownChart
        series={series({
          scope: { type: 'board', boardId: 'b-1', boardName: 'Board chính' },
        })}
      />
    )

    expect(screen.getByText(/Board chính/)).toBeInTheDocument()
  })
})
