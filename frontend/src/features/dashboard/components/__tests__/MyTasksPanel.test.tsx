import React from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'
import { MyTasksPanel } from '../MyTasksPanel'
import type { DashboardTaskItem } from '../../types/dashboard.types'

describe('MyTasksPanel', () => {
  const wsId = 'ws-123'

  const taskOverdue: DashboardTaskItem = {
    id: 't-1',
    boardId: 'b-1',
    columnId: 'c-1',
    title: 'Nhiệm vụ bị trễ',
    boardName: 'Bảng Dự Án',
    columnName: 'Đang làm',
    dueDate: '2026-09-10T00:00:00Z',
    priority: 'Urgent',
    createdAt: '2026-09-01T00:00:00Z',
    assigneeId: 'u-1',
    isDone: false,
    overdueByDays: 3,
  }

  const taskNoDue: DashboardTaskItem = {
    id: 't-2',
    boardId: 'b-1',
    columnId: 'c-1',
    title: 'Nhiệm vụ không hạn chót',
    boardName: 'Bảng Dự Án',
    columnName: 'Cần làm',
    dueDate: null,
    priority: 'Low',
    createdAt: '2026-09-14T00:00:00Z',
    assigneeId: 'u-1',
    isDone: false,
    overdueByDays: null,
  }

  it('renders tab headers with exact count even when items are truncated', () => {
    const myTasks = {
      overdue: { count: 15, items: [taskOverdue] },
      dueSoon: { count: 4, items: [] },
      recentlyAssigned: { count: 8, items: [taskNoDue] },
    }

    render(
      <MemoryRouter>
        <MyTasksPanel workspaceId={wsId} myTasks={myTasks} />
      </MemoryRouter>
    )

    expect(screen.getByText('Quá hạn (15)')).toBeInTheDocument()
    expect(screen.getByText('Sắp đến hạn (4)')).toBeInTheDocument()
    expect(screen.getByText('Mới giao (8)')).toBeInTheDocument()
  })

  it('renders overdue label correctly and priority tag', () => {
    const myTasks = {
      overdue: { count: 1, items: [taskOverdue] },
      dueSoon: { count: 0, items: [] },
      recentlyAssigned: { count: 0, items: [] },
    }

    render(
      <MemoryRouter>
        <MyTasksPanel workspaceId={wsId} myTasks={myTasks} />
      </MemoryRouter>
    )

    expect(screen.getByText('Nhiệm vụ bị trễ')).toBeInTheDocument()
    expect(screen.getByText('Quá hạn 3 ngày')).toBeInTheDocument()
    expect(screen.getByText('Khẩn cấp')).toBeInTheDocument()
  })

  it('renders empty description when a tab has no tasks', async () => {
    const user = userEvent.setup()
    const myTasks = {
      overdue: { count: 0, items: [] },
      dueSoon: { count: 0, items: [] },
      recentlyAssigned: { count: 0, items: [] },
    }

    render(
      <MemoryRouter>
        <MyTasksPanel workspaceId={wsId} myTasks={myTasks} />
      </MemoryRouter>
    )

    expect(screen.getByText('Không có task quá hạn')).toBeInTheDocument()

    // Switch to Sắp đến hạn
    await user.click(screen.getByText('Sắp đến hạn (0)'))
    expect(screen.getByText('Không có task sắp đến hạn')).toBeInTheDocument()
  })
})
