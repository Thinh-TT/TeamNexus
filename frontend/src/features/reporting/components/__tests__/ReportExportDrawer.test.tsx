import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ReportExportDrawer } from '../ReportExportDrawer'
import { useReportExport } from '../../hooks/useReportExport'

vi.mock('../../hooks/useReportExport', () => ({
  useReportExport: vi.fn(),
}))

describe('ReportExportDrawer', () => {
  const mockExportReport = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useReportExport).mockReturnValue({
      exporting: null,
      error: null,
      httpStatus: null,
      exportReport: mockExportReport,
    })
  })

  it('renders PDF and Excel format choices', () => {
    render(
      <ReportExportDrawer
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
      />
    )

    expect(screen.getByText('Xuất Báo Cáo & Dữ Liệu')).toBeInTheDocument()
    expect(screen.getByText('Tài liệu PDF (QuestPDF)')).toBeInTheDocument()
    expect(screen.getByText('Bảng tính Excel (ClosedXML)')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tải Báo Cáo PDF/i })).toBeInTheDocument()
  })

  it('triggers exportReport with pdf format by default', () => {
    mockExportReport.mockResolvedValueOnce(true)
    const handleClose = vi.fn()

    render(
      <ReportExportDrawer
        open={true}
        onClose={handleClose}
        workspaceId="ws-1"
      />
    )

    const downloadBtn = screen.getByRole('button', { name: /Tải Báo Cáo PDF/i })
    fireEvent.click(downloadBtn)

    expect(mockExportReport).toHaveBeenCalledWith('pdf')
  })

  it('displays error alert when export encounters an error', () => {
    vi.mocked(useReportExport).mockReturnValue({
      exporting: null,
      error: 'Requires Manager or Admin role in this workspace.',
      httpStatus: 403,
      exportReport: mockExportReport,
    })

    render(
      <ReportExportDrawer
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
      />
    )

    expect(screen.getByText('Không thể xuất báo cáo')).toBeInTheDocument()
    expect(screen.getByText('Requires Manager or Admin role in this workspace.')).toBeInTheDocument()
  })
})
