/**
 * Ghép đường dẫn SignalR hub với API base.
 * - base rỗng/undefined/relative ('/api')  ⇒ giữ đường dẫn tương đối '/hubs/board' (Vite dev proxy, hành vi cũ).
 * - base tuyệt đối ('https://api.x.com/api') ⇒ 'https://api.x.com/hubs/board' (bỏ path của base).
 * - base có dấu '/' cuối ⇒ không sinh '//'.
 * Hàm THUẦN: không đọc import.meta.env, không I/O ⇒ truyền base vào từ chỗ gọi.
 */
export function resolveHubUrl(apiBaseUrl?: string, hubPath = '/hubs/board'): string {
  const normalizedHubPath = hubPath.startsWith('/') ? hubPath : `/${hubPath}`
  if (!apiBaseUrl || !apiBaseUrl.trim()) {
    return normalizedHubPath
  }

  const trimmed = apiBaseUrl.trim()
  if (trimmed.startsWith('/')) {
    return normalizedHubPath
  }

  try {
    const url = new URL(trimmed)
    return `${url.origin}${normalizedHubPath}`
  } catch {
    return normalizedHubPath
  }
}
