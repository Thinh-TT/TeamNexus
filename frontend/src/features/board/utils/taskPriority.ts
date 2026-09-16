import type { TaskPriority } from '../types/board.types'

/**
 * Cấu hình hiển thị của một mức ưu tiên.
 * <para>
 * Tách ra thành **helper dùng chung** (Giai đoạn 13 §1) vì `TaskCard` và lịch (`TaskCalendar`) phải
 * tô **cùng một màu** cho cùng một mức ưu tiên — nếu mỗi nơi tự khai một bảng màu, hai màn hình sẽ
 * nói hai chuyện khác nhau về cùng một thẻ.
 * </para>
 */
export interface TaskPriorityConfig {
  /** Tên màu của antd `Tag`/`Badge` (`'error' | 'warning' | 'processing' | 'default'`). */
  color: string
  /** Nhãn tiếng Việt hiển thị cho người dùng. */
  label: string
  /** Màu nền nhạt cho chip. */
  bg: string
  /** Màu viền nhạt cho chip. */
  border: string
}

/**
 * Bảng màu/nhãn của 4 mức ưu tiên; `null` (không đặt ưu tiên) trả `null`.
 */
export const getPriorityConfig = (priority: TaskPriority | null): TaskPriorityConfig | null => {
  switch (priority) {
    case 'Urgent':
      return { color: 'error', label: 'Khẩn cấp', bg: '#fef2f2', border: '#fecaca' }
    case 'High':
      return { color: 'warning', label: 'Cao', bg: '#fffbeb', border: '#fde68a' }
    case 'Medium':
      return { color: 'processing', label: 'Trung bình', bg: '#eff6ff', border: '#bfdbfe' }
    case 'Low':
      return { color: 'default', label: 'Thấp', bg: '#f8fafc', border: '#e2e8f0' }
    default:
      return null
  }
}

/**
 * Thứ hạng ưu tiên để **sắp xếp tất định** (số lớn = gấp hơn).
 *
 * <para>
 * `null` (không đặt) nằm **giữa** Low và Medium: nó không phải "gấp" nhưng cũng không nên bị đẩy
 * xuống dưới cả những thẻ được cố tình đánh dấu "Thấp". Cố định con số ở đây để lịch và mọi bảng
 * sắp xếp sau này không tự nghĩ ra thứ tự khác nhau.
 * </para>
 */
export const priorityRank = (priority: TaskPriority | null): number => {
  switch (priority) {
    case 'Urgent':
      return 4
    case 'High':
      return 3
    case 'Medium':
      return 2
    case 'Low':
      return 1
    default:
      return 1.5
  }
}
