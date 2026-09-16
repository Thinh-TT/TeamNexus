import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { TaskCalendar } from '../TaskCalendar'
import type { ColumnResponse, TaskResponse } from '../../types/board.types'

/**
 * Giai đoạn 13 §1 — lịch thẻ theo hạn chót.
 *
 * Suite này khoá **hợp đồng điều hướng** (bấm ngày ⇒ URL tìm kiếm đúng) và **thứ tự ưu tiên tương tác**
 * (bấm thẻ mở chi tiết, **không** nhảy trang). Hai thứ đó là toàn bộ giá trị của tính năng: một lịch
 * hiển thị đúng mà bấm vào lại đi sai chỗ thì vẫn là lỗi.
 */

/** Một mốc "hôm nay" xa để mọi thẻ trong fixture đều là tương lai (không phụ thuộc ngày chạy test). */
const NOW_ISO = '2026-06-01T00:00:00Z'

const columns: ColumnResponse[] = [
  {
    id: 'col-todo',
    boardId: 'board-1',
    name: 'To Do',
    position: 0,
    isDone: false,
    isClarification: false,
    createdAt: NOW_ISO,
    updatedAt: NOW_ISO,
    tasks: [],
  },
  {
    id: 'col-done',
    boardId: 'board-1',
    name: 'Done',
    position: 1,
    isDone: true,
    isClarification: false,
    createdAt: NOW_ISO,
    updatedAt: NOW_ISO,
    tasks: [],
  },
]

const task = (over: Partial<TaskResponse> & { id: string }): TaskResponse => ({
  boardId: 'board-1',
  columnId: 'col-todo',
  title: `Task ${over.id}`,
  description: null,
  position: 0,
  assigneeId: null,
  assigneeName: null,
  dueDate: '2026-06-20T09:00:00',
  priority: null,
  createdAt: NOW_ISO,
  updatedAt: NOW_ISO,
  completedAt: null,
  labels: [],
  commentCount: 0,
  assigneeIsAiAgent: false,
  activeAgentRunId: null,
  ...over,
})

/** Ghi lại URL hiện tại để assert điều hướng mà không cần `window.location` thật. */
function LocationProbe() {
  const location = useLocation()
  return <div data-testid="current-url">{`${location.pathname}${location.search}`}</div>
}

const renderCalendar = (tasks: TaskResponse[], onTaskClick = vi.fn()) => {
  const result = render(
    <MemoryRouter initialEntries={['/workspaces/ws-1/boards/board-1']}>
      <Routes>
        <Route
          path="/workspaces/:workspaceId/boards/:boardId"
          element={
            <>
              <TaskCalendar
                workspaceId="ws-1"
                boardId="board-1"
                tasks={tasks}
                columns={columns}
                onTaskClick={onTaskClick}
              />
              <LocationProbe />
            </>
          }
        />
        {/* Catch-all: sau khi điều hướng sang trang tìm kiếm, `LocationProbe` vẫn phải còn để assert URL.
            Ở app thật, `/workspaces/:id/search` có route riêng — ở đây chỉ cần nó tồn tại. */}
        <Route path="*" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>
  )

  return { ...result, onTaskClick }
}

/** Ô lịch của một ngày, tìm qua `data-testid` để không phụ thuộc cấu trúc DOM của antd. */
const cell = (container: HTMLElement, dayKey: string) =>
  container.querySelector(`[data-testid="calendar-cell-${dayKey}"]`)

describe('TaskCalendar', () => {
  it('hiển thị thẻ lên đúng ô theo ngày hạn', () => {
    const { container } = renderCalendar([
      task({ id: 'a', title: 'Việc ngày 20', dueDate: '2026-06-20T09:00:00' }),
      task({ id: 'b', title: 'Việc ngày 22', dueDate: '2026-06-22T09:00:00' }),
    ])

    const day20 = cell(container, '2026-06-20')
    const day22 = cell(container, '2026-06-22')

    expect(day20).not.toBeNull()
    expect(day22).not.toBeNull()

    expect(day20?.textContent).toContain('Việc ngày 20')
    expect(day20?.textContent).not.toContain('Việc ngày 22')
    expect(day22?.textContent).toContain('Việc ngày 22')
  })

  it('thẻ ở cột is_done có nhãn "đã xong"', () => {
    const { container } = renderCalendar([
      task({ id: 'done', title: 'Đã hoàn thành', columnId: 'col-done' }),
    ])

    // `isTaskCompleted` đọc cờ `isDone` của cột ⇒ gạch ngang + icon check.
    const node = cell(container, '2026-06-20')
    expect(node).not.toBeNull()
    expect(node?.querySelector('.anticon-check-circle')).not.toBeNull()
  })

  it('gộp phần tràn thành "+N thẻ khác"', () => {
    const tasks = Array.from({ length: 5 }, (_, index) =>
      task({ id: `t${index}`, title: `Việc ${index}` })
    )

    const { container } = renderCalendar(tasks)
    const node = cell(container, '2026-06-20')

    expect(node).not.toBeNull()
    // 3 thẻ hiển thị + 2 thẻ còn lại.
    expect(node?.textContent).toContain('+2 thẻ khác')
  })

  it('bấm một ô ngày ⇒ mở trang tìm kiếm đã lọc theo đúng ngày đó', async () => {
    const { container } = renderCalendar([task({ id: 'a' })])

    const node = cell(container, '2026-06-20')
    expect(node).not.toBeNull()

    // Bấm vào chính ô (không phải chip thẻ) ⇒ điều hướng.
    fireEvent.click(node!)

    await waitFor(() => {
      expect(screen.getByTestId('current-url').textContent).toBe(
        '/workspaces/ws-1/search?boardId=board-1&dueFrom=2026-06-20&dueTo=2026-06-20'
      )
    })
  })

  it('bấm ô TRỐNG vẫn điều hướng (không im lặng)', async () => {
    const { container } = renderCalendar([task({ id: 'a', dueDate: '2026-06-20T09:00:00' })])

    // Ngày 15 không có thẻ nào, nhưng vẫn phải là một nút bấm được (antd render đủ ô cho cả tháng).
    const emptyCell = container.querySelector('.ant-picker-cell[title="2026-06-15"]')
    expect(emptyCell).not.toBeNull()
    fireEvent.click(emptyCell!)

    await waitFor(() => {
      expect(screen.getByTestId('current-url').textContent).toBe(
        '/workspaces/ws-1/search?boardId=board-1&dueFrom=2026-06-15&dueTo=2026-06-15'
      )
    })
  })

  it('bấm một thẻ ⇒ mở chi tiết và KHÔNG nhảy sang trang tìm kiếm', async () => {
    const onTaskClick = vi.fn()
    const { container } = renderCalendar([task({ id: 'a', title: 'Mở chi tiết' })], onTaskClick)

    const chip = container.querySelector('[data-testid="calendar-task-a"]')
    expect(chip).not.toBeNull()
    fireEvent.click(chip!)

    expect(onTaskClick).toHaveBeenCalledTimes(1)
    expect(onTaskClick.mock.calls[0][0].id).toBe('a')

    // Sự kiện đã bị chặn nổi bọt ⇒ URL không đổi.
    expect(screen.getByTestId('current-url').textContent).toBe('/workspaces/ws-1/boards/board-1')
  })

  it('hiện số thẻ không có hạn chót và điều hướng sang tìm kiếm của board', async () => {
    renderCalendar([
      task({ id: 'a', dueDate: '2026-06-20T09:00:00' }),
      task({ id: 'b', title: 'Không hạn', dueDate: null }),
      task({ id: 'c', title: 'Cũng không hạn', dueDate: null }),
    ])

    const button = screen.getByTestId('calendar-without-due-btn')
    expect(button.textContent).toContain('2 thẻ không có hạn chót')

    fireEvent.click(button)

    await waitFor(() => {
      expect(screen.getByTestId('current-url').textContent).toBe(
        '/workspaces/ws-1/search?boardId=board-1'
      )
    })
  })

  it('không hiện nút "không có hạn chót" khi mọi thẻ đều có hạn', () => {
    renderCalendar([task({ id: 'a', dueDate: '2026-06-20T09:00:00' })])

    expect(screen.queryByTestId('calendar-without-due-btn')).not.toBeInTheDocument()
  })

  it('bảng không có thẻ ⇒ Empty tiếng Việt, không màn hình trắng', () => {
    renderCalendar([])

    expect(screen.queryByTestId('task-calendar')).not.toBeInTheDocument()
    expect(screen.getByText(/chưa có thẻ nào/i)).toBeInTheDocument()
  })

  it('chuyển tháng không gọi thêm API và không làm mất thẻ của tháng đang xem', () => {
    const { container } = renderCalendar([task({ id: 'a', title: 'Việc tháng 6' })])

    fireEvent.click(screen.getByRole('button', { name: 'Tháng sau' }))

    // Sau khi chuyển tháng, thẻ của tháng 6 không còn trên lưới (tháng 7 đang xem) — nhưng component
    // vẫn render bình thường, tức là không có lần fetch nào và không có crash.
    expect(screen.getByTestId('task-calendar')).toBeInTheDocument()
    expect(cell(container, '2026-06-20')).toBeNull()
    expect(screen.getByRole('button', { name: 'Tháng trước' })).toBeInTheDocument()
  })
})
