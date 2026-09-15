import React from 'react'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'
import { TaskSearchResultList } from '../TaskSearchResultList'
import type { TaskSearchItem } from '../../types/search.types'

describe('TaskSearchResultList', () => {
  const wsId = 'ws-123'
  const mockOnLoadMore = vi.fn()

  const mockItem1: TaskSearchItem = {
    task: {
      id: 't-1',
      boardId: 'b-1',
      columnId: 'c-1',
      title: 'Thiết kế giao diện tìm kiếm',
      description: 'Cần thiết kế UI đẹp',
      priority: 'High',
      dueDate: '2026-09-20T00:00:00Z',
      position: 0,
      createdAt: '2026-09-15T00:00:00Z',
      updatedAt: '2026-09-15T00:00:00Z',
      assigneeId: 'u-1',
      assigneeName: 'Trần An',
      assigneeEmail: 'an@test.com',
      labels: [{ id: 'l-1', name: 'Frontend', color: '#6366f1' }],
      commentCount: 3,
    },
    boardName: 'Board 1',
    columnName: 'Đang làm',
    isDoneColumn: false,
  }

  const mockItemDone: TaskSearchItem = {
    task: {
      id: 't-2',
      boardId: 'b-1',
      columnId: 'c-2',
      title: 'Tạo API backend search',
      priority: 'Medium',
      dueDate: null,
      position: 1,
      createdAt: '2026-09-15T00:00:00Z',
      updatedAt: '2026-09-15T00:00:00Z',
      assigneeId: null,
      labels: [],
      commentCount: 0,
    },
    boardName: 'Board 1',
    columnName: 'Hoàn thành',
    isDoneColumn: true,
  }

  it('renders search results with tags, board/column and done label', () => {
    render(
      <MemoryRouter>
        <TaskSearchResultList
          workspaceId={wsId}
          items={[mockItem1, mockItemDone]}
          loading={false}
          loadingMore={false}
          hasMore={false}
          hasQuery={true}
          onLoadMore={mockOnLoadMore}
        />
      </MemoryRouter>
    )

    expect(screen.getByText('Thiết kế giao diện tìm kiếm')).toBeInTheDocument()
    expect(screen.getByText('Cần thiết kế UI đẹp')).toBeInTheDocument()
    expect(screen.getByText('Frontend')).toBeInTheDocument()
    expect(screen.getByText('Trần An')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument() // comment count

    // Item done
    expect(screen.getByText('Tạo API backend search')).toBeInTheDocument()
    expect(screen.getByText('Đã xong')).toBeInTheDocument()
  })

  it('renders empty prompt when hasQuery is false', () => {
    render(
      <MemoryRouter>
        <TaskSearchResultList
          workspaceId={wsId}
          items={[]}
          loading={false}
          loadingMore={false}
          hasMore={false}
          hasQuery={false}
          onLoadMore={mockOnLoadMore}
        />
      </MemoryRouter>
    )

    expect(
      screen.getByText('Nhập từ khoá hoặc điều chỉnh bộ lọc để bắt đầu tìm kiếm thẻ')
    ).toBeInTheDocument()
  })

  it('renders not found when hasQuery is true and items is empty', () => {
    render(
      <MemoryRouter>
        <TaskSearchResultList
          workspaceId={wsId}
          items={[]}
          loading={false}
          loadingMore={false}
          hasMore={false}
          hasQuery={true}
          onLoadMore={mockOnLoadMore}
        />
      </MemoryRouter>
    )

    expect(
      screen.getByText('Không tìm thấy thẻ nào phù hợp với điều kiện tìm kiếm')
    ).toBeInTheDocument()
  })

  it('triggers onLoadMore when clicking load more button', async () => {
    const user = userEvent.setup()

    render(
      <MemoryRouter>
        <TaskSearchResultList
          workspaceId={wsId}
          items={[mockItem1]}
          loading={false}
          loadingMore={false}
          hasMore={true}
          hasQuery={true}
          onLoadMore={mockOnLoadMore}
        />
      </MemoryRouter>
    )

    const loadMoreBtn = screen.getByTestId('search-load-more-btn')
    expect(loadMoreBtn).toBeInTheDocument()

    await user.click(loadMoreBtn)
    expect(mockOnLoadMore).toHaveBeenCalled()
  })
})
