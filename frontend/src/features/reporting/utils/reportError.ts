export interface ExtractedErrorInfo {
  status?: number
  message: string
}

const DEFAULT_STATUS_MESSAGES: Record<number, string> = {
  400: 'Tham số yêu cầu không hợp lệ.',
  401: 'Hết phiên đăng nhập. Vui lòng đăng nhập lại.',
  403: 'Bạn cần quyền Quản lý hoặc Quản trị viên để thực hiện thao tác này.',
  404: 'Không tìm thấy workspace hoặc bảng yêu cầu.',
  503: 'Tính năng báo cáo hiện đang tạm tắt.',
}

export async function extractErrorMessage(err: unknown): Promise<ExtractedErrorInfo> {
  if (!err || typeof err !== 'object') {
    return { message: 'Đã có lỗi xảy ra. Vui lòng thử lại sau.' }
  }

  const axiosErr = err as {
    response?: {
      status?: number
      data?: unknown
    }
    message?: string
  }

  const status = axiosErr.response?.status
  const data = axiosErr.response?.data

  if (data instanceof Blob) {
    try {
      const text = await data.text()
      const parsed = JSON.parse(text)
      if (parsed && typeof parsed === 'object' && typeof parsed.error === 'string') {
        return { status, message: parsed.error }
      }
    } catch {
      // Data is a non-JSON blob (e.g. raw binary or corrupted response)
    }
  } else if (data && typeof data === 'object') {
    const typedData = data as { error?: string; message?: string }
    if (typeof typedData.error === 'string' && typedData.error) {
      return { status, message: typedData.error }
    }
    if (typeof typedData.message === 'string' && typedData.message) {
      return { status, message: typedData.message }
    }
  }

  if (status && DEFAULT_STATUS_MESSAGES[status]) {
    return { status, message: DEFAULT_STATUS_MESSAGES[status] }
  }

  return {
    status,
    message: axiosErr.message || 'Đã có lỗi xảy ra. Vui lòng thử lại sau.',
  }
}
