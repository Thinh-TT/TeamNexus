import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { WorkspaceCreateModal } from '../WorkspaceCreateModal'

describe('WorkspaceCreateModal', () => {
  const onClose = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  const renderModal = (onSubmit = vi.fn().mockResolvedValue(true)) =>
    render(<WorkspaceCreateModal open={true} onClose={onClose} onSubmit={onSubmit} />)

  it('renders the name and optional description fields', () => {
    renderModal()

    expect(screen.getByText('Tên không gian làm việc')).toBeInTheDocument()
    expect(screen.getByText('Mô tả (tuỳ chọn)')).toBeInTheDocument()
    expect(screen.getByTestId('workspace-name-input')).toBeInTheDocument()
    expect(screen.getByTestId('workspace-description-input')).toBeInTheDocument()
  })

  it('does not submit when the name is blank', async () => {
    const onSubmit = vi.fn()
    renderModal(onSubmit)

    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => {
      expect(screen.getByText('Vui lòng nhập tên không gian làm việc')).toBeInTheDocument()
    })
    expect(onSubmit).not.toHaveBeenCalled()
  })

  it('submits trimmed values and closes the modal', async () => {
    const onSubmit = vi.fn().mockResolvedValue(true)
    renderModal(onSubmit)

    fireEvent.change(screen.getByTestId('workspace-name-input'), {
      target: { value: '  Dự án Beta  ' },
    })
    fireEvent.change(screen.getByTestId('workspace-description-input'), {
      target: { value: '  Mô tả Beta  ' },
    })

    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ name: 'Dự án Beta', description: 'Mô tả Beta' })
    })
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('sends null when the optional description is left empty', async () => {
    const onSubmit = vi.fn().mockResolvedValue(true)
    renderModal(onSubmit)

    fireEvent.change(screen.getByTestId('workspace-name-input'), { target: { value: 'Dự án' } })
    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith({ name: 'Dự án', description: null })
    })
  })

  it('keeps the modal open and swallows the error when the API rejects', async () => {
    const onSubmit = vi.fn().mockRejectedValue(new Error('boom'))
    renderModal(onSubmit)

    fireEvent.change(screen.getByTestId('workspace-name-input'), { target: { value: 'Dự án' } })
    fireEvent.click(screen.getByRole('button', { name: 'Tạo không gian' }))

    await waitFor(() => expect(onSubmit).toHaveBeenCalled())
    expect(onClose).not.toHaveBeenCalled()
  })
})
