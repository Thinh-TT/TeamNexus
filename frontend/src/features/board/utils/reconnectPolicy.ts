/**
 * Cấu hình và chính sách kết nối lại SignalR chống Render free-tier cold-start (B7, D9).
 * Bản Render free-tier tự ngủ sau 15 phút không có request và mất ~60s để thức dậy.
 * Chính sách backoff có trần 30s và thử lại vô hạn, không bao giờ bỏ cuộc.
 */

export const RECONNECT_MAX_DELAY_MS = 30_000
export const COLD_START_THRESHOLD_MS = 5_000

export const COLD_START_MESSAGE =
  'Máy chủ đang khởi động lại (bản miễn phí tự ngủ khi rảnh). Quá trình có thể mất khoảng 1 phút — dữ liệu của bạn vẫn an toàn.'

const RETRY_DELAYS: readonly number[] = [0, 2000, 5000, 10000, 30000]

/**
 * Tính toán thời gian (ms) chờ trước lần thử lại thứ `previousRetryCount` (0-based).
 * Sau khi chạm trần RECONNECT_MAX_DELAY_MS, hàm tiếp tục giữ nguyên mức trần mãi mãi (không bao giờ trả null).
 */
export function nextRetryDelay(previousRetryCount: number): number {
  if (typeof previousRetryCount !== 'number' || Number.isNaN(previousRetryCount) || previousRetryCount < 0) {
    return 0
  }

  const intIndex = Math.floor(previousRetryCount)
  if (intIndex >= RETRY_DELAYS.length) {
    return RECONNECT_MAX_DELAY_MS
  }

  return RETRY_DELAYS[intIndex]
}

/**
 * Trả về true khi số lần thử lại >= 3 (tổng thời gian chờ ~7 giây),
 * đủ lâu để xác định khả năng cao máy chủ đang spin up từ trạng thái ngủ.
 */
export function isColdStartLikely(previousRetryCount: number): boolean {
  if (typeof previousRetryCount !== 'number' || Number.isNaN(previousRetryCount)) {
    return false
  }
  return previousRetryCount >= 3
}
