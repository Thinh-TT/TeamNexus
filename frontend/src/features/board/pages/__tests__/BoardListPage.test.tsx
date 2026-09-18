import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BoardListPage } from '../BoardListPage'
import * as useWorkspaceRoleModule from '../../../../shared/hooks/useWorkspaceRole'

vi.mock('../../services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn().mockResolvedValue([
      {
        id: 'board-1',
        workspaceId: 'ws-1',
        name: 'Sprint 1',
        description: null,
        position: 0,
        createdAt: '2026-09-01T00:00:00Z',
        updatedAt: '2026-09-01T00:00:00Z',
        taskCount: 5,
        columnCount: 3,
      },
    ]),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: ({ children }: { children?: React.ReactNode }) => <header data-testid="app-header">{children}</header>,
}))

vi.mock('../../../ai', () => ({
  BoardTemplateModal: ({ open }: { open: boolean }) =>
    open ? <div data-testid="mock-board-template-modal">AI Board Template Modal</div> : null,
}))

describe('BoardListPage', () => {
  const wsId = 'ws-1'

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders "Tạo board bằng AI" button for Manager / Admin', async () => {
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Manager',
      isManagerOrAdmin: true,
      isAdmin: false,
      isMember: false,
      loading: false,
    })

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/boards`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/boards" element={<BoardListPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText('Sprint 1')).toBeInTheDocument()
    expect(screen.getByTestId('board-template-btn')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tạo Bảng Mới/i })).toBeInTheDocument()

    // Clicking AI template button opens modal
    fireEvent.click(screen.getByTestId('board-template-btn'))
    expect(screen.getByTestId('mock-board-template-modal')).toBeInTheDocument()
  })

  it('does NOT render "Tạo board bằng AI" button for standard Member', async () => {
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Member',
      isManagerOrAdmin: false,
      isAdmin: false,
      isMember: true,
      loading: false,
    })

    render(
      <MemoryRouter initialEntries={[`/workspaces/${wsId}/boards`]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/boards" element={<BoardListPage />} />
        </Routes>
      </MemoryRouter>
    )

    expect(await screen.findByText('Sprint 1')).toBeInTheDocument()
    expect(screen.queryByTestId('board-template-btn')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Tạo Bảng Mới/i })).toBeInTheDocument()
  })
})
