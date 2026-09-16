import dayjs from 'dayjs'
import type {
  ReportDailyProgressPoint,
  ReportProgressSeriesResponse,
  ReportWeeklyProgressPoint,
} from '../types/reporting.types'

/**
 * Một cột trên biểu đồ, đã chuẩn hoá khỏi sự khác biệt giữa `mode = 'date'` và `'week'`.
 *
 * @property key Khoá ổn định để React không dựng lại cột (`'2026-06-20'` hoặc `'W2026-06-15'`).
 * @property label Nhãn dưới trục hoành (`20/06` hoặc `Tuần 15/06`).
 */
export interface BurndownBucket {
  key: string
  label: string
  completions: number
  /** Số thẻ mở ở **cuối** bucket. */
  openTasks: number
  creations: number
  /** Chỉ có ở `mode = 'week'`. */
  openAtEnd?: number
}

/**
 * Số cột tối đa hiển thị. Backend đã cap 90 bucket/ngày, nhưng một cửa sổ 60 ngày vẫn cho 60 cột —
 * nhiều hơn mức đọc được trên một màn hình, nên biểu đồ chỉ vẽ `maxBuckets` bucket **cuối** và nói rõ
 * là đang cắt (không âm thầm bỏ dữ liệu).
 */
export const MAX_CHART_BUCKETS = 31

/**
 * Chuẩn hoá `days`/`weeks` thành **một** mảng bucket để component chỉ phải vẽ một đường.
 *
 * <para>
 * Backend bảo đảm `days` và `weeks` **loại trừ nhau**, nên hàm này chọn theo `mode` thay vì đoán theo
 * mảng nào có phần tử — đoán sẽ che mất một payload sai.
 * </para>
 */
export function seriesBuckets(series: ReportProgressSeriesResponse): BurndownBucket[] {
  if (series.mode === 'week') {
    return series.weeks.map((week: ReportWeeklyProgressPoint) => ({
      key: `W${week.weekStart}`,
      label: `Tuần ${dayjs(week.weekStart).format('DD/MM')}`,
      completions: week.completions,
      openTasks: week.openAtEnd,
      creations: week.creations,
      openAtEnd: week.openAtEnd,
    }))
  }

  return series.days.map((day: ReportDailyProgressPoint) => ({
    key: day.date,
    label: dayjs(day.date).format('DD/MM'),
    completions: day.completions,
    openTasks: day.openTasks,
    creations: day.creations,
  }))
}

/**
 * Cắt bớt số cột cho vừa màn hình, giữ **những bucket mới nhất** (hàm THUẦN).
 *
 * Giữ phần mới nhất chứ không phải phần cũ nhất: người xem báo cáo quan tâm "gần đây thế nào", và cắt
 * từ đầu là quy tắc mà backend đã dùng cho cap 90 bucket — hai chỗ phải cắt cùng một phía.
 */
export function visibleBuckets(
  buckets: BurndownBucket[],
  maxBuckets: number = MAX_CHART_BUCKETS
): { buckets: BurndownBucket[]; hiddenCount: number } {
  const cap = Math.max(1, maxBuckets)

  if (buckets.length <= cap) {
    return { buckets, hiddenCount: 0 }
  }

  return { buckets: buckets.slice(-cap), hiddenCount: buckets.length - cap }
}

/**
 * Đường "lý tưởng" của biểu đồ burndown: số thẻ mở ban đầu trừ dần số thẻ hoàn thành mỗi bucket.
 *
 * <para>
 * <b>Kẹp tại 0</b> (và loại `NaN`): nếu số thẻ hoàn thành trong kỳ lớn hơn số thẻ mở ở bucket đầu — hoàn
 * toàn hợp lệ khi nhiều thẻ được tạo rồi đóng trong cùng cửa sổ — thì công thức thô sẽ cho số âm và
 * biểu đồ sẽ vẽ một đường chui xuống dưới trục. Đường "lý tưởng" là một hình minh hoạ, không phải số
 * liệu, nên kẹp tại 0 là hành vi đúng.
 * </para>
 */
export function idealOpenSeries(
  startOpen: number,
  buckets: BurndownBucket[]
): number[] {
  const start = Number.isFinite(startOpen) ? startOpen : 0
  let cumulativeCompletions = 0

  return buckets.map((bucket) => {
    cumulativeCompletions += Number.isFinite(bucket.completions) ? bucket.completions : 0
    return Math.max(0, start - cumulativeCompletions)
  })
}

/**
 * Chiều cao cột theo **phần trăm**, đã chống chia 0.
 *
 * <para>
 * Board không có thẻ nào ⇒ `max = 0` ⇒ nếu không guard, mọi cột là `NaN%` và CSS bỏ qua chúng, khiến
 * biểu đồ trông như lỗi render. Trả `0` để lưới vẫn đứng yên và trục vẫn vẽ.
 * </para>
 */
export function barHeightPercent(value: number, max: number): number {
  if (!Number.isFinite(value) || !Number.isFinite(max) || max <= 0 || value <= 0) {
    return 0
  }

  return Math.min(100, Math.round((value / max) * 100))
}

/** Giá trị lớn nhất trong một chuỗi số (đã bỏ `NaN`); rỗng ⇒ 0. */
export function seriesMax(values: number[]): number {
  const finite = values.filter((value) => Number.isFinite(value))
  return finite.length === 0 ? 0 : Math.max(...finite)
}

/**
 * Trung bình số thẻ hoàn thành mỗi tuần — lấy từ server để **không** tính lại ở client.
 *
 * <p>
 * Backend đã chia cho `số ngày / 7` trên đúng cửa sổ mà nó trả về; nếu client tự tính từ mảng bucket
 * (vốn có thể đã bị cắt còn 31 cột) thì con số sẽ khác `/reports/summary` cho cùng một workspace.
 * </p>
 */
export function velocityStats(series: ReportProgressSeriesResponse) {
  const velocity = series.velocity

  return {
    avgCompletionsPerWeek: Number.isFinite(velocity?.avgCompletionsPerWeek)
      ? velocity.avgCompletionsPerWeek
      : 0,
    completedInRange: velocity?.completedInRange ?? 0,
    openAtEnd: velocity?.openAtEnd ?? 0,
  }
}
