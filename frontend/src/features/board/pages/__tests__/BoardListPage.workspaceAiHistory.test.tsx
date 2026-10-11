import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BoardListPage } from '../BoardListPage'
import * as useWorkspaceRoleModule from '../../../../shared/hooks/useWorkspaceRole'

/**
 * Phase 14.5 §Track A (A1.1) — the workspace-wide AI history entry point.
 *
 * <p>Kept separate from `BoardListPage.test.tsx` so the two suites do not share mock state: that file
 * asserts the board-template button and must stay green untouched, while this one adds the new button.</p>
 */

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
        taskCount: 0,
        columnCount: 0,
      },
    ]),
    createBoard: vi.fn(),
    updateBoard: vi.fn(),
    deleteBoard: vi.fn(),
  },
}))

vi.mock('../../../../shared/components/AppHeader', () => ({
  AppHeader: ({ children }: { children?: React.ReactNode }) => (
    <header data-testid="app-header">{children}</header>
  ),
}))

vi.mock('../../../ai', () => ({
  BoardTemplateModal: () => null,
}))

// The real drawer is a large component with its own API calls; this suite only asserts that the entry
// point exists, opens it and hands it the WORKSPACE scope rather than a board.
vi.mock('../../../ai/components/AiActionHistoryDrawer', () => ({
  AiActionHistoryDrawer: ({ open, workspaceId }: { open: boolean; workspaceId?: string }) =>
    open ? (
      <div data-testid="mock-ai-history-drawer" data-workspace-id={workspaceId}>
        Lịch sử AI
      </div>
    ) : null,
}))

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={['/workspaces/ws-1/boards']}>
      <Routes>
        <Route path="/workspaces/:workspaceId/boards" element={<BoardListPage />} />
      </Routes>
    </MemoryRouter>
  )

beforeEach(() => {
  vi.clearAllMocks()
})

describe('BoardListPage — workspace AI history (A1.1)', () => {
  it('offers the workspace history to a Manager and scopes it to the workspace', async () => {
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Manager',
      isManagerOrAdmin: true,
      isAdmin: false,
      isMember: false,
      loading: false,
    })

    renderPage()

    await screen.findByText('Sprint 1')

    const button = screen.getByTestId('workspace-ai-history-btn')
    fireEvent.click(button)

    const drawer = screen.getByTestId('mock-ai-history-drawer')

    // The WORKSPACE id, not a board id: a board-template log has no board to be scoped to, which is the
    // whole reason this entry point exists.
    expect(drawer.getAttribute('data-workspace-id')).toBe('ws-1')
  })

  it('hides the workspace history from a plain Member', async () => {
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Member',
      isManagerOrAdmin: false,
      isAdmin: false,
      isMember: true,
      loading: false,
    })

    renderPage()

    await screen.findByText('Sprint 1')

    // Manager+ only on the server too, so showing it to a Member would only produce a 403.
    expect(screen.queryByTestId('workspace-ai-history-btn')).not.toBeInTheDocument()
  })
})
