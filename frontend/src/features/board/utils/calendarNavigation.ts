import { dayKey } from './boardCalendar'

/**
 * URL của trang tìm kiếm đã lọc theo **một ngày hạn** (Giai đoạn 13 §1).
 *
 * <para>
 * Đây là điểm nối duy nhất giữa lịch và tìm kiếm. `TaskSearchPage` **đã** đọc `boardId`/`dueFrom`/`dueTo`
 * từ URL từ Giai đoạn 12, nên lịch không cần API mới — chỉ cần dựng đúng URL.
 * </para>
 *
 * <para>
 * <b>`dayKey` chứ không phải ISO đầy đủ:</b> `TaskSearchPage` đọc thẳng chuỗi này rồi chuyển cho
 * `searchApi` → `?dueFrom=&dueTo=`, và backend chuẩn hoá về UTC. Gửi `2026-01-20` là ngày mà **người
 * dùng** nhìn thấy; gửi `2026-01-20T00:00:00.000Z` sẽ bị hiểu là nửa đêm UTC, tức **07:00 sáng giờ
 * Việt Nam** — lệch hẳn một buổi.
 * </para>
 */
export function buildDaySearchUrl(
  workspaceId: string,
  boardId: string | undefined,
  day: string | Date
): string {
  const key = typeof day === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(day) ? day : dayKey(day)

  const params = new URLSearchParams()
  if (boardId) params.set('boardId', boardId)
  params.set('dueFrom', key)
  params.set('dueTo', key)

  return `/workspaces/${workspaceId}/search?${params.toString()}`
}

/**
 * URL của trang tìm kiếm cho **cả board** (không lọc hạn) — dùng cho khối *"N thẻ không có hạn chót"*.
 *
 * Search **không** có tham số "không có hạn chót", nên đây là lựa chọn trung thực nhất: mở phạm vi
 * board để người dùng tự tìm, thay vì bịa một bộ lọc không tồn tại.
 */
export function buildBoardSearchUrl(workspaceId: string, boardId: string | undefined): string {
  return boardId
    ? `/workspaces/${workspaceId}/search?boardId=${encodeURIComponent(boardId)}`
    : `/workspaces/${workspaceId}/search`
}
