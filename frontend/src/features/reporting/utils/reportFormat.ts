export function formatDurationHours(hours: number | null | undefined): string {
  if (hours === null || hours === undefined) return '—'
  return `${hours.toFixed(1)} giờ`
}

export function formatPercent(rate: number | null | undefined): string {
  if (rate === null || rate === undefined) return '—'
  return `${rate.toFixed(1)}%`
}

export function formatCount(count: number | null | undefined): string {
  if (count === null || count === undefined) return '0'
  return count.toLocaleString('vi-VN')
}
