import type { WorkspaceMemberResponse } from '../types/board.types'

/**
 * Trích xuất danh sách userId được mention trong nội dung bình luận.
 * Quy tắc:
 * 1. Sắp xếp danh sách thành viên theo độ dài tên hiển thị GIẢM DẦN (ưu tiên khớp tên dài trước).
 * 2. Khớp chuỗi dạng "@" + displayName. Ký tự ngay trước "@" phải là đầu chuỗi hoặc không phải chữ/số (không bắt nhầm email "a@b.c").
 * 3. Sau khi một tên dài khớp, thay thế vị trí đó để tên ngắn hơn lồng bên trong không bị khớp trùng (ví dụ "Trần An Bình" không làm kích hoạt "Trần An").
 * 4. Nếu 2 thành viên có cùng tên hiển thị, chọn ID của người đầu tiên trong danh sách members.
 * 5. Loại bỏ chính tác giả (currentUserId).
 * 6. Khử trùng lặp ID và giới hạn tối đa 20 userId.
 */
export function extractMentionUserIds(
  content: string,
  members: WorkspaceMemberResponse[],
  currentUserId?: string
): string[] {
  if (!content || !members || members.length === 0) {
    return []
  }

  // Lọc bỏ AI Agent và thành viên không có tên
  const eligibleMembers = members.filter(
    (m) => m.memberType !== 'ai_agent' && Boolean(m.displayName && m.displayName.trim())
  )

  // Sắp xếp theo độ dài tên hiển thị giảm dần
  const sortedMembers = [...eligibleMembers].sort(
    (a, b) => b.displayName.length - a.displayName.length
  )

  const matchedUserIds: string[] = []
  const seenUserIds = new Set<string>()
  let workingContent = content

  for (const member of sortedMembers) {
    const name = member.displayName.trim()
    const escapedName = name.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')

    // Ký tự ngay trước @ không được là chữ cái hoặc số (để tránh email a@b.c)
    // Sau tên không được dính liền chữ cái/số
    const regex = new RegExp(`(^|[^a-zA-Z0-9_À-ỹ])@${escapedName}(?![a-zA-Z0-9_À-ỹ])`, 'i')

    if (regex.test(workingContent)) {
      // Loại bỏ chính mình
      if (!currentUserId || member.userId !== currentUserId) {
        if (!seenUserIds.has(member.userId)) {
          seenUserIds.add(member.userId)
          matchedUserIds.push(member.userId)
        }
      }

      // Thay thế tất cả các lần xuất hiện của mention này trong workingContent
      const replaceRegex = new RegExp(`(^|[^a-zA-Z0-9_À-ỹ])@${escapedName}(?![a-zA-Z0-9_À-ỹ])`, 'gi')
      workingContent = workingContent.replace(replaceRegex, '$1__MENTIONED__')

      if (matchedUserIds.length >= 20) {
        break
      }
    }
  }

  return matchedUserIds
}
