import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ObserverFindingCard } from '../ObserverFindingCard'
import type { ObserverRunFinding } from '../../types/notification.types'

describe('ObserverFindingCard', () => {
  const mockFinding: ObserverRunFinding = {
    type: 'OverdueTask',
    severity: 'High',
    title: 'Task S5 overdue task trễ hạn 15 ngày',
    message: 'Task này đã quá hạn từ ngày 2026-08-25 mà chưa hoàn thành.',
    taskIds: ['11111111-2222-3333-4444-555555555555'],
    userIds: ['aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee'],
  }

  it('renders severity tag, type, title, and message', () => {
    render(<ObserverFindingCard finding={mockFinding} />)

    expect(screen.getByText('HIGH')).toBeInTheDocument()
    expect(screen.getByText('OverdueTask')).toBeInTheDocument()
    expect(screen.getByText('Task S5 overdue task trễ hạn 15 ngày')).toBeInTheDocument()
    expect(
      screen.getByText('Task này đã quá hạn từ ngày 2026-08-25 mà chưa hoàn thành.')
    ).toBeInTheDocument()
  })

  it('renders shortened evidence IDs', () => {
    render(<ObserverFindingCard finding={mockFinding} />)

    expect(screen.getByText('Tasks:')).toBeInTheDocument()
    expect(screen.getByText('...555555')).toBeInTheDocument()
    expect(screen.getByText('Users:')).toBeInTheDocument()
    expect(screen.getByText('...eeeeee')).toBeInTheDocument()
  })
})
