import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ReportFilters } from '../ReportFilters'
import { reportingApi } from '../../services/reportingApi'

vi.mock('../../services/reportingApi', () => ({
  reportingApi: {
    listReportBoards: vi.fn(),
  },
}))

describe('ReportFilters', () => {
  const mockBoards = [
    { id: 'b-1', name: 'Board Alpha', taskCount: 5, isDoneColumns: 1 },
    { id: 'b-2', name: 'Board Beta', taskCount: 2, isDoneColumns: 1 },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(reportingApi.listReportBoards).mockResolvedValue(mockBoards)
  })

  it('renders board select and preset select options', async () => {
    render(
      <ReportFilters
        workspaceId="ws-1"
        onChange={vi.fn()}
      />
    )

    await waitFor(() => {
      expect(reportingApi.listReportBoards).toHaveBeenCalledWith('ws-1')
    })

    expect(screen.getByText('30 ngày qua')).toBeInTheDocument()
  })

  it('triggers onChange when switching preset interval', async () => {
    const handleChange = vi.fn()
    render(
      <ReportFilters
        workspaceId="ws-1"
        onChange={handleChange}
      />
    )

    // Open preset select
    const presetSelect = screen.getByText('30 ngày qua')
    fireEvent.mouseDown(presetSelect)

    // Select 7 days option
    const option7Days = await screen.findByText('7 ngày qua')
    fireEvent.click(option7Days)

    expect(handleChange).toHaveBeenCalledWith(
      expect.objectContaining({
        from: expect.any(String),
        to: expect.any(String),
      })
    )
  })
})
