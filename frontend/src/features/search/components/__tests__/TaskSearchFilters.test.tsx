import React from 'react'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskSearchFilters } from '../TaskSearchFilters'
import { boardApi } from '../../../board/services/boardApi'
import { useWorkspaceMembers } from '../../../board/hooks/useWorkspaceMembers'

vi.mock('../../../board/services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn(),
    getLabels: vi.fn(),
  },
}))

vi.mock('../../../board/hooks/useWorkspaceMembers', () => ({
  useWorkspaceMembers: vi.fn(),
}))

describe('TaskSearchFilters', () => {
  const wsId = 'ws-123'
  const mockOnFilterChange = vi.fn()
  const mockOnReset = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(boardApi.getBoards).mockResolvedValue([
      { id: 'b-1', name: 'Board Alpha', workspaceId: wsId, description: '', createdAt: '' },
    ])
    vi.mocked(boardApi.getLabels).mockResolvedValue([
      { id: 'l-1', name: 'Frontend', color: '#6366f1', workspaceId: wsId },
    ])
    vi.mocked(useWorkspaceMembers).mockReturnValue({
      members: [
        {
          userId: 'u-1',
          displayName: 'Trần An',
          email: 'an@example.com',
          role: 'Member',
          joinedAt: '',
          memberType: 'human',
        },
      ],
      loading: false,
      error: null,
      refetch: vi.fn(),
    })
  })

  it('renders filter controls and triggers onReset when clicking Xoá bộ lọc', async () => {
    const user = userEvent.setup()

    render(
      <TaskSearchFilters
        workspaceId={wsId}
        filters={{}}
        onFilterChange={mockOnFilterChange}
        onReset={mockOnReset}
      />
    )

    await waitFor(() => {
      expect(boardApi.getBoards).toHaveBeenCalledWith(wsId)
      expect(boardApi.getLabels).toHaveBeenCalledWith(wsId)
    })

    expect(screen.getByText('Bộ lọc nâng cao')).toBeInTheDocument()
    expect(screen.getByTestId('filter-reset-btn')).toBeInTheDocument()

    await user.click(screen.getByTestId('filter-reset-btn'))
    expect(mockOnReset).toHaveBeenCalled()
  })

  it('toggles overdue switch and notifies parent', async () => {
    const user = userEvent.setup()

    render(
      <TaskSearchFilters
        workspaceId={wsId}
        filters={{ overdue: false }}
        onFilterChange={mockOnFilterChange}
        onReset={mockOnReset}
      />
    )

    const switchBtn = screen.getByTestId('filter-overdue-switch')
    await user.click(switchBtn)

    expect(mockOnFilterChange).toHaveBeenCalledWith({ overdue: true })
  })
})
