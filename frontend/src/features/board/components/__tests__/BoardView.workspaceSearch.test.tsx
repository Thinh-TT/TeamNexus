import React from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter } from 'react-router-dom'
import { BoardView } from '../BoardView'
import { httpClient } from '../../../../shared/api'

const mockNavigate = vi.fn()

vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  }
})

vi.mock('../../hooks/useBoard', () => ({
  useBoard: () => ({
    board: { id: 'board-1', name: 'Board 1', workspaceId: 'ws-1', columns: [] },
    columns: [],
    tasksByColumn: {},
    workspaceLabels: [],
    activeTask: null,
    connectionStatus: 'connected',
    reconnect: vi.fn(),
    isLoading: false,
    refetch: vi.fn(),
    setActiveTask: vi.fn(),
  }),
}))

vi.mock('../../../../shared/api', () => ({
  httpClient: {
    get: vi.fn(),
    post: vi.fn(),
    put: vi.fn(),
    delete: vi.fn(),
  },
}))

vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn().mockResolvedValue({ unreadCount: 0, items: [] }),
  },
}))

vi.mock('../../../ai/services/aiActionApi', () => ({
  aiActionApi: {
    listAiActions: vi.fn().mockResolvedValue([]),
  },
}))

describe('BoardView - Workspace Search navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(httpClient.get).mockImplementation(async (url: string) => {
      if (url === '/workspaces') {
        return {
          data: [{ id: 'ws-1', name: 'Workspace 1', role: 'Manager' }],
        }
      }
      return { data: {} }
    })
  })

  it('renders "Tìm trong workspace →" button and navigates with boardId and searchQuery', async () => {
    const user = userEvent.setup()

    render(
      <MemoryRouter>
        <BoardView workspaceId="ws-1" boardId="board-1" />
      </MemoryRouter>
    )

    const searchWorkspaceBtn = screen.getByTestId('board-search-workspace-btn')
    expect(searchWorkspaceBtn).toBeInTheDocument()

    // Type in client search input
    const input = screen.getByPlaceholderText('Tìm kiếm thẻ, nhãn, người...')
    await user.type(input, 'database')

    await user.click(searchWorkspaceBtn)

    expect(mockNavigate).toHaveBeenCalledWith('/workspaces/ws-1/search?boardId=board-1&q=database')
  })
})
