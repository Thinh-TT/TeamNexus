import React from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaskSearchPage } from '../TaskSearchPage'
import { searchApi } from '../../services/searchApi'

vi.mock('../../services/searchApi', () => ({
  searchApi: {
    searchTasks: vi.fn(),
  },
}))

vi.mock('../../../board/services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn().mockResolvedValue([]),
    getLabels: vi.fn().mockResolvedValue([]),
  },
}))

vi.mock('../../../board/hooks/useWorkspaceMembers', () => ({
  useWorkspaceMembers: vi.fn().mockReturnValue({
    members: [],
    loading: false,
    error: null,
    refetch: vi.fn(),
  }),
}))

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: ({ children }: { children?: React.ReactNode }) => <header data-testid="app-header">{children}</header>,
}))

describe('TaskSearchPage', () => {
  const wsId = '11111111-1111-1111-1111-111111111111'

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(searchApi.searchTasks).mockResolvedValue({
      items: [],
      nextCursor: null,
      hasMore: false,
      hasQuery: false,
    })
  })

  it('renders search page and mounts with query from URL', async () => {
    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/search?q=testing`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/search" element={<TaskSearchPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText('Tìm Kiếm & Lọc Thẻ')).toBeInTheDocument()
    const input = screen.getByTestId('search-input') as HTMLInputElement
    expect(input.value).toBe('testing')
  })
})
