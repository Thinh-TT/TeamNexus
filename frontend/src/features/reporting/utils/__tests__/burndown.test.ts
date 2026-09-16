import { describe, expect, it } from 'vitest'
import type { ReportProgressSeriesResponse } from '../../types/reporting.types'
import {
  barHeightPercent,
  idealOpenSeries,
  seriesBuckets,
  seriesMax,
  velocityStats,
  visibleBuckets,
} from '../burndown'

/**
 * Giai đoạn 13 §4 — chuẩn hoá chuỗi thời gian (hàm thuần).
 *
 * Hai nhóm test đáng chú ý nhất:
 *  • **`barHeightPercent` với `max = 0`** — board rỗng. Không guard thì mọi cột là `NaN%`, CSS bỏ qua và
 *    biểu đồ trông như lỗi render.
 *  • **`idealOpenSeries` không âm** — khi số thẻ hoàn thành trong kỳ lớn hơn số thẻ mở ban đầu (hợp lệ
 *    nếu thẻ được tạo rồi đóng ngay trong cửa sổ), công thức thô cho số âm và đường vẽ chui dưới trục.
 */

const series = (over: Partial<ReportProgressSeriesResponse>): ReportProgressSeriesResponse =>
  ({
    workspaceId: 'ws-1',
    scope: { type: 'workspace', boardId: null, boardName: null },
    period: { from: '2026-06-01T00:00:00Z', to: '2026-06-07T00:00:00Z', days: 7, clamped: false },
    mode: 'date',
    bucketDays: 1,
    days: [],
    weeks: [],
    velocity: { avgCompletionsPerWeek: 0, completedInRange: 0, openAtEnd: 0 },
    metricDefinitions: {},
    truncated: { bucketCapReached: false, maxBuckets: 90 },
    tzOffsetMinutes: 0,
    ...over,
  }) as ReportProgressSeriesResponse

describe('burndown.seriesBuckets', () => {
  it('mode=date ⇒ dùng days, nhãn DD/MM', () => {
    const buckets = seriesBuckets(
      series({
        days: [
          { date: '2026-06-01', openTasks: 3, completions: 1, creations: 2 },
          { date: '2026-06-02', openTasks: 2, completions: 1, creations: 0 },
        ],
      })
    )

    expect(buckets).toHaveLength(2)
    expect(buckets[0]).toMatchObject({
      key: '2026-06-01',
      label: '01/06',
      openTasks: 3,
      completions: 1,
      creations: 2,
    })
    expect(buckets[0].openAtEnd).toBeUndefined()
  })

  it('mode=week ⇒ dùng weeks (bỏ qua days), nhãn "Tuần DD/MM"', () => {
    const buckets = seriesBuckets(
      series({
        mode: 'week',
        bucketDays: 7,
        // `days` cố ý có dữ liệu để chứng minh nó bị BỎ QUA khi mode = 'week'.
        days: [{ date: '2026-06-01', openTasks: 99, completions: 99, creations: 99 }],
        weeks: [{ weekStart: '2026-06-01', completions: 4, creations: 6, openAtEnd: 5 }],
      })
    )

    expect(buckets).toHaveLength(1)
    expect(buckets[0]).toMatchObject({
      key: 'W2026-06-01',
      label: 'Tuần 01/06',
      openTasks: 5,
      openAtEnd: 5,
      completions: 4,
      creations: 6,
    })
  })

  it('chuỗi rỗng ⇒ mảng rỗng (không ném)', () => {
    expect(seriesBuckets(series({}))).toEqual([])
  })
})

describe('burndown.visibleBuckets', () => {
  it('ít hơn cap ⇒ giữ nguyên, không ẩn gì', () => {
    const buckets = Array.from({ length: 5 }, (_, i) => ({
      key: `${i}`,
      label: `${i}`,
      openTasks: 1,
      completions: 1,
      creations: 1,
    }))

    const result = visibleBuckets(buckets, 10)

    expect(result.buckets).toHaveLength(5)
    expect(result.hiddenCount).toBe(0)
  })

  it('nhiều hơn cap ⇒ giữ những bucket MỚI NHẤT và báo số đã ẩn', () => {
    const buckets = Array.from({ length: 40 }, (_, i) => ({
      key: `d${i}`,
      label: `d${i}`,
      openTasks: 1,
      completions: 1,
      creations: 1,
    }))

    const result = visibleBuckets(buckets, 31)

    expect(result.buckets).toHaveLength(31)
    expect(result.hiddenCount).toBe(9)
    // Mới nhất ở cuối mảng ⇒ bucket cuối là d39 và bucket đầu là d9.
    expect(result.buckets[0].key).toBe('d9')
    expect(result.buckets[30].key).toBe('d39')
  })

  it('cap <= 0 vẫn vẽ được ít nhất 1 bucket', () => {
    const buckets = [{ key: 'a', label: 'a', openTasks: 1, completions: 1, creations: 1 }]

    expect(visibleBuckets(buckets, 0).buckets).toHaveLength(1)
  })
})

describe('burndown.idealOpenSeries', () => {
  const bucket = (completions: number) => ({
    key: `${completions}`,
    label: `${completions}`,
    openTasks: 0,
    completions,
    creations: 0,
  })

  it('giảm dần theo số thẻ hoàn thành tích luỹ', () => {
    expect(idealOpenSeries(10, [bucket(1), bucket(2), bucket(3)])).toEqual([9, 7, 4])
  })

  it('KẸP TẠI 0 khi hoàn thành nhiều hơn số thẻ mở ban đầu', () => {
    // 5 thẻ mở, nhưng 8 thẻ được đóng trong kỳ (thẻ tạo rồi đóng ngay) ⇒ không được trả số âm.
    expect(idealOpenSeries(5, [bucket(4), bucket(4)])).toEqual([1, 0])
  })

  it('mảng rỗng ⇒ mảng rỗng', () => {
    expect(idealOpenSeries(10, [])).toEqual([])
  })

  it('bỏ qua giá trị không hữu hạn thay vì sinh NaN', () => {
    const result = idealOpenSeries(5, [bucket(Number.NaN), bucket(2)])

    expect(result.every((value) => Number.isFinite(value))).toBe(true)
    expect(result).toEqual([5, 3])
  })

  it('startOpen không hữu hạn ⇒ coi như 0', () => {
    expect(idealOpenSeries(Number.POSITIVE_INFINITY, [bucket(1)])).toEqual([0])
  })
})

describe('burndown.barHeightPercent', () => {
  it('tính đúng phần trăm', () => {
    expect(barHeightPercent(5, 10)).toBe(50)
    expect(barHeightPercent(10, 10)).toBe(100)
    expect(barHeightPercent(1, 3)).toBe(33)
  })

  it('max = 0 ⇒ 0% (không chia 0, không NaN)', () => {
    // Đây là board rỗng: mọi cột phải cao 0% để lưới và trục vẫn vẽ bình thường.
    expect(barHeightPercent(0, 0)).toBe(0)
    expect(barHeightPercent(5, 0)).toBe(0)
  })

  it('giá trị âm hoặc không hữu hạn ⇒ 0%', () => {
    expect(barHeightPercent(-3, 10)).toBe(0)
    expect(barHeightPercent(Number.NaN, 10)).toBe(0)
    expect(barHeightPercent(3, Number.NaN)).toBe(0)
    expect(barHeightPercent(3, Number.POSITIVE_INFINITY)).toBe(0)
  })

  it('không bao giờ vượt quá 100%', () => {
    expect(barHeightPercent(20, 10)).toBe(100)
  })
})

describe('burndown.seriesMax', () => {
  it('lấy giá trị lớn nhất, bỏ NaN', () => {
    expect(seriesMax([1, Number.NaN, 7, 3])).toBe(7)
  })

  it('rỗng hoặc toàn NaN ⇒ 0', () => {
    expect(seriesMax([])).toBe(0)
    expect(seriesMax([Number.NaN])).toBe(0)
  })
})

describe('burndown.velocityStats', () => {
  it('lấy nguyên số từ server (không tính lại từ bucket đã cắt)', () => {
    const stats = velocityStats(
      series({ velocity: { avgCompletionsPerWeek: 4.5, completedInRange: 18, openAtEnd: 7 } })
    )

    expect(stats).toEqual({
      avgCompletionsPerWeek: 4.5,
      completedInRange: 18,
      openAtEnd: 7,
    })
  })

  it('avgCompletionsPerWeek = 0 ⇒ 0 (không "—")', () => {
    const stats = velocityStats(
      series({ velocity: { avgCompletionsPerWeek: 0, completedInRange: 0, openAtEnd: 0 } })
    )

    expect(stats.avgCompletionsPerWeek).toBe(0)
  })

  it('giá trị không hữu hạn ⇒ 0 thay vì NaN trên giao diện', () => {
    const stats = velocityStats(
      series({
        velocity: {
          avgCompletionsPerWeek: Number.NaN,
          completedInRange: 0,
          openAtEnd: 0,
        },
      })
    )

    expect(stats.avgCompletionsPerWeek).toBe(0)
  })
})
