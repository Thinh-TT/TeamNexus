import React from 'react'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { RecentActivityPanel } from '../RecentActivityPanel'
import type { DashboardActivityItem } from '../../types/dashboard.types'

describe('RecentActivityPanel', () => {
  const wsId = 'ws-123'

  const mockActivities: DashboardActivityItem[] = [
    {
      id: 'a-1',
      boardId: 'b-1',
      entityType: 'Task',
      action: 'TaskCreated',
      authorName: 'Trần Thinh',
      createdAt: '2026-09-15T11:00:00Z',
      payload: null,
    },
    {
      id: 'a-2',
      boardId: null,
      entityType: 'Workspace',
      action: 'CustomUnknownAction',
      authorName: null,
      createdAt: '2026-09-15T10:00:00Z',
      payload: null,
    },
  ]

  it('renders Vietnamese label for known action and fallback for unknown action', () => {
    render(
      <MemoryRouter>
        <RecentActivityPanel workspaceId={wsId} activities={mockActivities} />
      </MemoryRouter>
    )

    expect(screen.getByText('Trần Thinh')).toBeInTheDocument()
    expect(screen.getByText(/đã tạo thẻ/i)).toBeInTheDocument()

    // Author is null -> "Hệ thống", action fallback
    expect(screen.getByText('Hệ thống')).toBeInTheDocument()
    expect(screen.getByText(/CustomUnknownAction/i)).toBeInTheDocument()
  })

  it('renders empty description when activities array is empty', () => {
    render(
      <MemoryRouter>
        <RecentActivityPanel workspaceId={wsId} activities={[]} />
      </MemoryRouter>
    )

    expect(screen.getByText('Chưa có hoạt động nào trong workspace')).toBeInTheDocument()
  })
})
