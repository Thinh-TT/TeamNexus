import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ColumnModal } from '../ColumnModal'
import type { ColumnResponse } from '../../types/board.types'

describe('ColumnModal', () => {
  const onClose = vi.fn()
  const onSubmit = vi.fn().mockResolvedValue({})

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a column with isInReview checked when "Đang duyệt PR" is selected', async () => {
    render(<ColumnModal open={true} onClose={onClose} onSubmit={onSubmit} />)

    const nameInput = screen.getByPlaceholderText(
      'Ví dụ: Cần làm, Đang làm, Đang duyệt PR, Đã xong...'
    )
    fireEvent.change(nameInput, { target: { value: 'Review PR' } })

    const inReviewCheckbox = screen.getByTestId('column-is-in-review-checkbox')
    fireEvent.click(inReviewCheckbox)

    fireEvent.click(screen.getByRole('button', { name: 'Tạo cột' }))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({
        name: 'Review PR',
        isDone: false,
        isInReview: true,
      })
    })
  })

  it('enforces mutual exclusivity between isDone and isInReview checkboxes', async () => {
    render(<ColumnModal open={true} onClose={onClose} onSubmit={onSubmit} />)

    const doneCheckbox = screen.getByTestId('column-is-done-checkbox') as HTMLInputElement
    const inReviewCheckbox = screen.getByTestId(
      'column-is-in-review-checkbox'
    ) as HTMLInputElement

    fireEvent.click(doneCheckbox)
    expect(doneCheckbox.checked).toBe(true)
    expect(inReviewCheckbox.checked).toBe(false)

    fireEvent.click(inReviewCheckbox)
    expect(inReviewCheckbox.checked).toBe(true)
    expect(doneCheckbox.checked).toBe(false)
  })

  it('disables both isDone and isInReview checkboxes when column isClarification is true', () => {
    const clarifyCol: ColumnResponse = {
      id: 'col-clarify',
      boardId: 'b-1',
      name: 'Chờ làm rõ',
      position: 3,
      isDone: false,
      isClarification: true,
      isInReview: false,
      createdAt: '',
      updatedAt: '',
      tasks: [],
    }

    render(<ColumnModal open={true} column={clarifyCol} onClose={onClose} onSubmit={onSubmit} />)

    expect(screen.getByTestId('column-is-done-checkbox')).toBeDisabled()
    expect(screen.getByTestId('column-is-in-review-checkbox')).toBeDisabled()
  })

  it('pre-populates isInReview state when editing an existing In Review column', () => {
    const reviewCol: ColumnResponse = {
      id: 'col-review',
      boardId: 'b-1',
      name: 'Code Review',
      position: 2,
      isDone: false,
      isClarification: false,
      isInReview: true,
      createdAt: '',
      updatedAt: '',
      tasks: [],
    }

    render(<ColumnModal open={true} column={reviewCol} onClose={onClose} onSubmit={onSubmit} />)

    const inReviewCheckbox = screen.getByTestId(
      'column-is-in-review-checkbox'
    ) as HTMLInputElement
    expect(inReviewCheckbox.checked).toBe(true)
  })

  it('keeps the modal open when onSubmit rejects (e.g. 409 duplicate In Review column)', async () => {
    vi.mocked(onSubmit).mockRejectedValueOnce(new Error('409 conflict'))

    render(<ColumnModal open={true} onClose={onClose} onSubmit={onSubmit} />)

    fireEvent.change(
      screen.getByPlaceholderText('Ví dụ: Cần làm, Đang làm, Đang duyệt PR, Đã xong...'),
      { target: { value: 'Review trùng' } }
    )
    fireEvent.click(screen.getByTestId('column-is-in-review-checkbox'))
    fireEvent.click(screen.getByRole('button', { name: 'Tạo cột' }))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalled()
    })

    expect(onClose).not.toHaveBeenCalled()
  })
})
