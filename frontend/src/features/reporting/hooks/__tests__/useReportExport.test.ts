import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { message } from 'antd'
import { useReportExport } from '../useReportExport'
import { reportingApi } from '../../services/reportingApi'
import * as downloadUtil from '../../utils/reportDownload'

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    downloadReport: vi.fn(),
  },
}))

vi.mock('../../utils/reportDownload', () => ({
  saveBlob: vi.fn(),
}))

vi.mock('antd', async () => {
  const actual = await vi.importActual('antd')
  return {
    ...actual,
    message: {
      success: vi.fn(),
      error: vi.fn(),
    },
  }
})

describe('useReportExport', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('exports report successfully and triggers saveBlob', async () => {
    const mockBlob = new Blob(['pdf data'], { type: 'application/pdf' })
    vi.mocked(reportingApi.downloadReport).mockResolvedValueOnce({
      blob: mockBlob,
      fileName: 'report.pdf',
      rowCapReached: false,
    })

    const { result } = renderHook(() =>
      useReportExport('ws-1', () => ({ boardId: 'b-1' }))
    )

    let success = false
    await act(async () => {
      success = await result.current.exportReport('pdf')
    })

    expect(success).toBe(true)
    expect(reportingApi.downloadReport).toHaveBeenCalledWith('ws-1', {
      format: 'pdf',
      boardId: 'b-1',
    })
    expect(downloadUtil.saveBlob).toHaveBeenCalledWith(mockBlob, 'report.pdf')
    expect(message.success).toHaveBeenCalledWith('Đã tải báo cáo')
    expect(result.current.exporting).toBeNull()
    expect(result.current.error).toBeNull()
  })

  it('prevents concurrent export requests when already exporting', async () => {
    let resolveDownload: (val: any) => void
    const downloadPromise = new Promise((resolve) => {
      resolveDownload = resolve
    })

    vi.mocked(reportingApi.downloadReport).mockReturnValueOnce(downloadPromise as any)

    const { result } = renderHook(() => useReportExport('ws-1'))

    let promise1: Promise<boolean>
    act(() => {
      promise1 = result.current.exportReport('pdf')
    })

    expect(result.current.exporting).toBe('pdf')

    // Second call during in-flight export
    let call2Result: boolean | undefined
    await act(async () => {
      call2Result = await result.current.exportReport('excel')
    })

    expect(call2Result).toBe(false)

    // Complete first download
    await act(async () => {
      resolveDownload!({
        blob: new Blob(['data']),
        fileName: 'report.pdf',
        rowCapReached: false,
      })
      await promise1
    })

    expect(result.current.exporting).toBeNull()
  })

  it('handles export failure and sets error message', async () => {
    const errorJson = JSON.stringify({ error: 'Requires Manager or Admin role in this workspace.' })
    const errorBlob = new Blob([errorJson], { type: 'application/json' })

    vi.mocked(reportingApi.downloadReport).mockRejectedValueOnce({
      response: {
        status: 403,
        data: errorBlob,
      },
    })

    const { result } = renderHook(() => useReportExport('ws-1'))

    let success = true
    await act(async () => {
      success = await result.current.exportReport('excel')
    })

    expect(success).toBe(false)
    expect(result.current.error).toBe('Requires Manager or Admin role in this workspace.')
    expect(result.current.httpStatus).toBe(403)
    expect(message.error).toHaveBeenCalledWith('Requires Manager or Admin role in this workspace.')
    expect(downloadUtil.saveBlob).not.toHaveBeenCalled()
  })
})
