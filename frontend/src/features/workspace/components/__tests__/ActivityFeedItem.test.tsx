import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ActivityFeedItem } from '../ActivityFeedItem'
import type { WorkspaceActivityItem } from '../../types/workspace.types'

describe('ActivityFeedItem', () => {
  const baseItem: WorkspaceActivityItem = {
    id: 'act-1',
    boardId: 'b-1',
    userId: 'u-1',
    userDisplayName: 'Trần Văn A',
    entityType: 'Task',
    entityId: 'task-1',
    action: 'TaskCreated',
    payload: null,
    createdAt: '2026-09-14T10:00:00Z',
  }

  it('renders actor name and action label correctly', () => {
    render(<ActivityFeedItem item={baseItem} />)
    expect(screen.getByText('Trần Văn A')).toBeInTheDocument()
    expect(screen.getByText('đã tạo thẻ')).toBeInTheDocument()
  })

  it('renders "Hệ thống" when actor is null', () => {
    const systemItem = { ...baseItem, userId: null, userDisplayName: null }
    render(<ActivityFeedItem item={systemItem} />)
    expect(screen.getByText('Hệ thống')).toBeInTheDocument()
  })

  it('renders safely without crash when payload is null', () => {
    const { container } = render(<ActivityFeedItem item={{ ...baseItem, payload: null }} />)
    expect(container).toBeInTheDocument()
    expect(screen.getByText('đã tạo thẻ')).toBeInTheDocument()
  })

  it('renders boardName tag when boardName prop is provided', () => {
    render(<ActivityFeedItem item={baseItem} boardName="Bảng Kanban Chính" />)
    expect(screen.getByText('Bảng Kanban Chính')).toBeInTheDocument()
  })

  it('renders entity icon for different entity types', () => {
    const { container: cTask } = render(<ActivityFeedItem item={{ ...baseItem, entityType: 'Task' }} />)
    expect(cTask.querySelector('.anticon-check-square')).toBeInTheDocument()

    const { container: cComment } = render(<ActivityFeedItem item={{ ...baseItem, entityType: 'Comment' }} />)
    expect(cComment.querySelector('.anticon-message')).toBeInTheDocument()

    const { container: cWorkspace } = render(<ActivityFeedItem item={{ ...baseItem, entityType: 'Workspace' }} />)
    expect(cWorkspace.querySelector('.anticon-appstore')).toBeInTheDocument()
  })
})
