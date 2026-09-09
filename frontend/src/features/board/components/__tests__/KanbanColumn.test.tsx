import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { DndContext } from '@dnd-kit/core'
import { KanbanColumn } from '../KanbanColumn'
import type { ColumnResponse, TaskResponse } from '../../types/board.types'

const renderWithDnd = (ui: React.ReactElement) => {
  return render(<DndContext>{ui}</DndContext>)
}

describe('KanbanColumn', () => {
  const mockColumn: ColumnResponse = {
    id: 'col-1',
    boardId: 'b-1',
    name: 'Đang Thực Hiện',
    position: 0,
    isDone: false,
    createdAt: '2026-09-09T00:00:00Z',
    updatedAt: '2026-09-09T00:00:00Z',
    tasks: [],
  }

  const mockTasks: TaskResponse[] = [
    {
      id: 'task-1',
      boardId: 'b-1',
      columnId: 'col-1',
      title: 'Task Alpha',
      position: 0,
      createdAt: '2026-09-09T00:00:00Z',
      updatedAt: '2026-09-09T00:00:00Z',
      labels: [],
      commentCount: 0,
    },
  ]

  it('renders column header with name and task counter', () => {
    renderWithDnd(
      <KanbanColumn
        column={mockColumn}
        tasks={mockTasks}
        onTaskClick={vi.fn()}
        onEditColumn={vi.fn()}
        onDeleteColumn={vi.fn()}
        onCreateTask={vi.fn()}
      />
    )

    expect(screen.getByText('Đang Thực Hiện')).toBeInTheDocument()
    expect(screen.getByText('1')).toBeInTheDocument() // count badge
    expect(screen.getByText('Task Alpha')).toBeInTheDocument()
  })

  it('renders empty dropzone placeholder when tasks list is empty', () => {
    renderWithDnd(
      <KanbanColumn
        column={mockColumn}
        tasks={[]}
        onTaskClick={vi.fn()}
        onEditColumn={vi.fn()}
        onDeleteColumn={vi.fn()}
        onCreateTask={vi.fn()}
      />
    )

    expect(screen.getByText('Kéo thẻ vào đây hoặc tạo mới')).toBeInTheDocument()
  })

  it('toggles quick add task form and triggers onCreateTask', async () => {
    const onCreateTask = vi.fn().mockResolvedValue({})

    renderWithDnd(
      <KanbanColumn
        column={mockColumn}
        tasks={[]}
        onTaskClick={vi.fn()}
        onEditColumn={vi.fn()}
        onDeleteColumn={vi.fn()}
        onCreateTask={onCreateTask}
      />
    )

    // Click "+ Thêm thẻ mới" button
    const addBtn = screen.getByText('Thêm thẻ mới')
    fireEvent.click(addBtn)

    // Input textarea appears
    const textarea = screen.getByPlaceholderText('Nhập tiêu đề cho thẻ...')
    expect(textarea).toBeInTheDocument()

    fireEvent.change(textarea, { target: { value: 'New Test Task' } })

    const submitBtn = screen.getByText('Thêm thẻ')
    fireEvent.click(submitBtn)

    await waitFor(() => {
      expect(onCreateTask).toHaveBeenCalledWith({
        columnId: 'col-1',
        title: 'New Test Task',
      })
    })
  })
})
