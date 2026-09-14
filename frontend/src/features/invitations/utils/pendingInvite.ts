const STORAGE_KEY = 'teamnexus_pending_invite_token'

/**
 * Lưu token lời mời vào sessionStorage để khôi phục sau khi hoàn tất đăng nhập OAuth
 */
export const savePendingInviteToken = (token: string): void => {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      if (token && typeof token === 'string' && token.trim() !== '') {
        window.sessionStorage.setItem(STORAGE_KEY, token.trim())
      }
    }
  } catch {
    // Fail-soft: không ném ngoại lệ khi sessionStorage bị chặn (e.g. incognito hoặc quota)
  }
}

/**
 * Đọc token lời mời từ sessionStorage
 */
export const getPendingInviteToken = (): string | null => {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      const val = window.sessionStorage.getItem(STORAGE_KEY)
      if (val && typeof val === 'string' && val.trim() !== '') {
        return val.trim()
      }
    }
    return null
  } catch {
    return null
  }
}

/**
 * Xoá token lời mời khỏi sessionStorage
 */
export const clearPendingInviteToken = (): void => {
  try {
    if (typeof window !== 'undefined' && window.sessionStorage) {
      window.sessionStorage.removeItem(STORAGE_KEY)
    }
  } catch {
    // Fail-soft
  }
}
