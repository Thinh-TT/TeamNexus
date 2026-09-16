import { beforeEach, describe, expect, it, vi } from 'vitest'
import { httpClient } from '../../../../shared/api/httpClient'
import { reportingApi } from '../reportingApi'
import type { ReportProgressSeriesResponse } from '../../types/reporting.types'

vi.mock('../../../../shared/api/httpClient', () => ({
  httpClient: {
    get: vi.fn(),
  },
}))

/**
 * Giai đoạn 13 §4 — `reportingApi.getProgressSeries`.
 *
 * Suite này khoá **hình dạng query string**, vì backend trả 400 cho tham số sai định dạng và **clamp**
 * im lặng cho tham số ngoài khoảng. Một URL sai ở đây không làm test nào khác đỏ — nó chỉ cho người
 * dùng một biểu đồ lệch múi giờ.
 */
describe('reportingApi.getProgressSeries', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('gọi đúng endpoint với đầy đủ tham số', async () => {
    const mockSeries: Partial<ReportProgressSeriesResponse> = { workspaceId: 'ws-1', mode: 'date' }
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: mockSeries })

    const result = await reportingApi.getProgressSeries('ws-1', {
      from: '2026-06-01T00:00:00Z',
      to: '2026-06-30T00:00:00Z',
      boardId: 'board-1',
      tzOffsetMinutes: 420,
    })

    expect(httpClient.get).toHaveBeenCalledWith(
      '/workspaces/ws-1/reports/progress-series' +
        '?from=2026-06-01T00%3A00%3A00Z&to=2026-06-30T00%3A00%3A00Z&boardId=board-1&tzOffsetMinutes=420'
    )
    expect(result).toEqual(mockSeries)
  })

  it('GỬI tzOffsetMinutes=0 (UTC là giá trị hợp lệ, không phải "rỗng")', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: {} })

    await reportingApi.getProgressSeries('ws-1', { tzOffsetMinutes: 0 })

    const url = vi.mocked(httpClient.get).mock.calls[0][0] as string

    // Nếu bị coi là "rỗng" thì tham số biến mất và backend mặc định về UTC — tình cờ đúng, nhưng chỉ
    // đúng vì mặc định cũng là 0. Đó là loại may mắn không nên dựa vào.
    expect(url).toContain('tzOffsetMinutes=0')
  })

  it('bỏ qua tham số rỗng thay vì gửi chuỗi rỗng', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: {} })

    await reportingApi.getProgressSeries('ws-1', {
      from: '',
      to: '',
      boardId: '',
      tzOffsetMinutes: undefined,
    })

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/reports/progress-series')
  })

  it('không truyền params ⇒ URL không có dấu hỏi', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: {} })

    await reportingApi.getProgressSeries('ws-1')

    expect(httpClient.get).toHaveBeenCalledWith('/workspaces/ws-1/reports/progress-series')
  })

  it('múi giờ âm (Tây bán cầu) giữ nguyên dấu', async () => {
    vi.mocked(httpClient.get).mockResolvedValueOnce({ data: {} })

    await reportingApi.getProgressSeries('ws-1', { tzOffsetMinutes: -300 })

    const url = vi.mocked(httpClient.get).mock.calls[0][0] as string
    expect(url).toContain('tzOffsetMinutes=-300')
  })

  it('lỗi 403 từ server được ném lại kèm response.status', async () => {
    const forbidden = Object.assign(new Error('Forbidden'), {
      response: { status: 403, data: { error: 'Chỉ quản lý mới xem được báo cáo.' } },
    })
    vi.mocked(httpClient.get).mockRejectedValueOnce(forbidden)

    await expect(reportingApi.getProgressSeries('ws-1')).rejects.toMatchObject({
      response: { status: 403 },
    })
  })
})
