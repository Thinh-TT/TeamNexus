import type { WorkspaceRole } from '../../members/types/member.types'

export interface UserProfileResponse {
  id: string
  email: string
  displayName: string
  avatarUrl: string | null
  createdAt: string
  /**
   * Bật/tắt email tóm tắt công việc hằng ngày (Giai đoạn 13 §3.4).
   *
   * <p>
   * Field được **append ở cuối** payload nên client cũ vẫn đọc được. Giá trị đến từ `GET /api/users/me`
   * — **không** lấy từ `useAuthStore`, vì `/api/auth/me` (endpoint bootstrap của store) **không** trả
   * field này.
   * </p>
   */
  digestEnabled: boolean
}

export interface UpdateProfileRequest {
  displayName: string
  avatarUrl?: string | null
  /**
   * `undefined`/`null` ⇒ **giữ nguyên** giá trị đang lưu ở server.
   *
   * <p>
   * <b>Đây là bẫy quan trọng nhất của ô này:</b> backend phân biệt rõ "không nhắc tới" với "tắt". Nếu
   * client gửi `false` ở chỗ đáng lẽ phải bỏ trống (ví dụ khi người dùng chỉ sửa tên hiển thị), digest
   * sẽ bị tắt âm thầm. Chỉ gửi `digestEnabled` khi người dùng thực sự bấm công tắc.
   * </p>
   */
  digestEnabled?: boolean | null
}

export interface MyWorkspaceResponse {
  id: string
  name: string
  description: string | null
  role: WorkspaceRole
  ownerId: string
  isOwner: boolean
}
