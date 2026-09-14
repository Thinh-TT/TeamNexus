import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { WorkspaceActivityPage } from '../WorkspaceActivityPage'
import { workspaceApi } from '../../services/workspaceApi'
import * as useWorkspaceRoleModule from '../../../../shared/hooks/useWorkspaceRole'
import type { WorkspaceActivityPage as ActivityPageType } from '../../types/workspace.types'

vi.mock('../../services/workspaceApi', () => ({
  workspaceApi: {
    getActivity: vi.fn(),
  },
}))

vi.mock('../../../board/services/boardApi', () => ({
  boardApi: {
    getBoards: vi.fn().mockResolvedValue([
      { id: 'b-1', name: 'Bảng Frontend' },
    ]),
  },
}))

describe('WorkspaceActivityPage', () => {
  const wsId = 'ws-act-test'

  const mockPageResponse: ActivityPageType = {
    items: [
      {
        id: 'act-1',
        boardId: 'b-1',
        userId: 'u-1',
        userDisplayName: 'Alex Tran',
        entityType: 'Task',
        entityId: 't-1',
        action: 'TaskCreated',
        payload: null,
        createdAt: '2026-09-14T09:00:00Z',
      },
      {
        id: 'act-2',
        boardId: 'b-1',
        userId: 'u-2',
        userDisplayName: 'Bob Dev',
        entityType: 'Comment',
        entityId: 'c-1',
        action: 'CommentAdded',
        payload: null,
        createdAt: '2026-09-14T09:05:00Z',
      },
    ],
    nextCursor: '2026-09-14T09:00:00Z|act-1',
    hasMore: true,
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Admin',
      isMember: true,
      isManagerOrAdmin: true,
      isAdmin: true,
      isOwner: true,
      loading: false,
      reload: vi.fn(),
    })
    vi.mocked(workspaceApi.getActivity).mockResolvedValue(mockPageResponse)
  })

  const renderPage = (initialUrl = `/workspaces/${wsId}/activity`) => {
    return render(
      <MemoryRouter initialEntries={[initialUrl]}>
        <Routes>
          <Route path="/workspaces/:workspaceId/activity" element={<WorkspaceActivityPage />} />
        </Routes>
      </MemoryRouter>
    )
  }

  it('renders activity items list and board name', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Alex Tran')).toBeInTheDocument()
      expect(screen.getByText('đã tạo thẻ')).toBeInTheDocument()
      expect(screen.getByText('Bob Dev')).toBeInTheDocument()
      expect(screen.getByText('đã bình luận')).toBeInTheDocument()
      expect(screen.getByText('Tải thêm hoạt động')).toBeInTheDocument()
    })
  })

  it('calls getActivity with nextCursor when clicking "Tải thêm"', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByTestId('load-more-activity-btn')).toBeInTheDocument()
    })

    const secondPage: ActivityPageType = {
      items: [
        {
          id: 'act-3',
          boardId: 'b-1',
          userId: 'u-3',
          userDisplayName: 'Carol QA',
          entityType: 'Task',
          entityId: 't-3',
          action: 'TaskCompleted',
          payload: null,
          createdAt: '2026-09-14T08:30:00Z',
        },
      ],
      nextCursor: null,
      hasMore: false,
    }
    vi.mocked(workspaceApi.getActivity).mockResolvedValueOnce(secondPage)

    fireEvent.click(screen.getByTestId('load-more-activity-btn'))

    await waitFor(() => {
      expect(workspaceApi.getActivity).toHaveBeenCalledWith(
        wsId,
        expect.objectContaining({
          before: '2026-09-14T09:00:00Z|act-1',
        })
      )
    })
  })

  it('re-fetches activity with filter when changing entity type filter', async () => {
    renderPage()

    await waitFor(() => {
      expect(screen.getByText('Alex Tran')).toBeInTheDocument()
    })

    // Switch filter to "Thẻ" (Task)
    const taskFilterBtn = screen.getByText('Thẻ')
    fireEvent.click(taskFilterBtn)

    await waitFor(() => {
      expect(workspaceApi.getActivity).toHaveBeenCalledWith(
        wsId,
        expect.objectContaining({
          entityType: 'Task',
        })
      )
    })
  })

  it('renders 403 Result when user is not a Manager or Admin', async () => {
    vi.spyOn(useWorkspaceRoleModule, 'useWorkspaceRole').mockReturnValue({
      role: 'Member',
      isMember: true,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
      loading: false,
      reload: vi.fn(),
    })

    renderPage()

    await waitFor(() => {
      expect(screen.getByText('403')).toBeInTheDocument()
      expect(screen.getByText(/Chỉ Manager hoặc Admin/)).toBeInTheDocument()
    })
  })
})
