import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { BoardTemplateModal } from '../BoardTemplateModal'
import { boardTemplateApi } from '../../services/boardTemplateApi'
import type { BoardTemplateProposal } from '../../types/boardTemplate.types'

vi.mock('../../services/boardTemplateApi', () => ({
  boardTemplateApi: {
    generateTemplate: vi.fn(),
    confirmTemplate: vi.fn(),
  },
}))

describe('BoardTemplateModal', () => {
  const wsId = 'ws-test-1'
  const mockProposal: BoardTemplateProposal = {
    summary: 'Phân tích hệ thống',
    boardName: 'Bảng Ban Đầu',
    boardDescription: 'Mô tả ban đầu',
    columns: [
      { name: 'Cần làm', isDone: false },
      { name: 'Đang làm', isDone: false },
      { name: 'Hoàn thành', isDone: true },
    ],
    tasks: [
      { title: 'Nhiệm vụ 1', columnName: 'Cần làm', priority: 'High' },
      { title: 'Nhiệm vụ 2', columnName: 'Đang làm', priority: 'Medium' },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('disables "Tạo mẫu đề xuất" button when description is under 20 characters', () => {
    render(<BoardTemplateModal open={true} onClose={vi.fn()} workspaceId={wsId} />)

    const generateBtn = screen.getByRole('button', { name: /Tạo mẫu đề xuất/i })
    expect(generateBtn).toBeDisabled()

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả quá ngắn' } })
    expect(generateBtn).toBeDisabled()
  })

  it('enables button and calls generateTemplate with valid description', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    render(<BoardTemplateModal open={true} onClose={vi.fn()} workspaceId={wsId} />)

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, {
      target: { value: 'Xây dựng website bán lẻ trực tuyến cho thương hiệu thời trang' },
    })

    const generateBtn = screen.getByRole('button', { name: /Tạo mẫu đề xuất/i })
    expect(generateBtn).not.toBeDisabled()

    fireEvent.click(generateBtn)

    await waitFor(() => {
      expect(boardTemplateApi.generateTemplate).toHaveBeenCalledWith(
        wsId,
        'Xây dựng website bán lẻ trực tuyến cho thương hiệu thời trang'
      )
      expect(screen.getByDisplayValue('Bảng Ban Đầu')).toBeInTheDocument()
      expect(screen.getByText('Nhiệm vụ 1')).toBeInTheDocument()
      expect(screen.getByText('Nhiệm vụ 2')).toBeInTheDocument()
    })
  })

  it('allows editing board name, deleting a task, and confirms with updated payload', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    vi.mocked(boardTemplateApi.confirmTemplate).mockResolvedValueOnce({
      id: 'log-1',
      action: 'CreateBoardFromTemplate',
      entityType: 'Workspace',
      entityId: wsId,
      status: 'Pending',
      requestedByUserId: 'u-1',
      requestedByName: null,
      decidedByUserId: null,
      decidedByName: null,
      decidedAt: null,
      decisionNote: null,
      taskCount: 1,
      createdAt: '',
      updatedAt: '',
    })

    const onClose = vi.fn()
    const onCreated = vi.fn()

    render(
      <BoardTemplateModal
        open={true}
        onClose={onClose}
        workspaceId={wsId}
        onCreated={onCreated}
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ trên hai mươi ký tự' } })
    fireEvent.click(screen.getByRole('button', { name: /Tạo mẫu đề xuất/i }))

    await waitFor(() => {
      expect(screen.getByDisplayValue('Bảng Ban Đầu')).toBeInTheDocument()
    })

    // Edit board name
    const boardNameInput = screen.getByDisplayValue('Bảng Ban Đầu')
    fireEvent.change(boardNameInput, { target: { value: 'Bảng Sau Khi Sửa' } })

    // Delete task 2
    const deleteButtons = screen.getAllByRole('button').filter((btn) => btn.querySelector('.anticon-delete'))
    expect(deleteButtons.length).toBe(2)
    fireEvent.click(deleteButtons[1])

    await waitFor(() => {
      expect(screen.queryByText('Nhiệm vụ 2')).not.toBeInTheDocument()
    })

    // Click confirm
    const confirmBtn = screen.getByRole('button', { name: /Xác nhận tạo/i })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(boardTemplateApi.confirmTemplate).toHaveBeenCalledWith(
        wsId,
        expect.objectContaining({
          boardName: 'Bảng Sau Khi Sửa',
          tasks: [expect.objectContaining({ title: 'Nhiệm vụ 1' })],
        })
      )
      expect(onCreated).toHaveBeenCalled()
      expect(onClose).toHaveBeenCalled()
    })
  })

  it('ensures only one isDone column can be selected via Radio', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    render(<BoardTemplateModal open={true} onClose={vi.fn()} workspaceId={wsId} />)

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ trên hai mươi ký tự' } })
    fireEvent.click(screen.getByRole('button', { name: /Tạo mẫu đề xuất/i }))

    await waitFor(() => {
      expect(screen.getByText('Cột trạng thái (3 cột):')).toBeInTheDocument()
    })

    // Click on Radio for 'Đang làm'
    const radios = screen.getAllByRole('radio')
    expect(radios).toHaveLength(3)

    // Radio 2 corresponds to 'Đang làm'
    fireEvent.click(radios[1])

    expect(radios[1]).toBeChecked()
    expect(radios[0]).not.toBeChecked()
    expect(radios[2]).not.toBeChecked()
  })

  it('displays Vietnamese alert on 403 error during generate without crashing', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockRejectedValueOnce({
      response: {
        status: 403,
        data: { error: 'Chỉ Manager mới có quyền tạo mẫu bảng' },
      },
    })

    render(<BoardTemplateModal open={true} onClose={vi.fn()} workspaceId={wsId} />)

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ trên hai mươi ký tự' } })
    fireEvent.click(screen.getByRole('button', { name: /Tạo mẫu đề xuất/i }))

    await waitFor(() => {
      expect(screen.getByText('Chỉ Manager mới có quyền tạo mẫu bảng')).toBeInTheDocument()
    })
  })

  it('retains preview on 400 error during confirm so user can retry', async () => {
    vi.mocked(boardTemplateApi.generateTemplate).mockResolvedValueOnce(mockProposal)
    vi.mocked(boardTemplateApi.confirmTemplate).mockRejectedValueOnce({
      response: {
        status: 400,
        data: { error: 'Tên bảng không được để trống' },
      },
    })

    render(<BoardTemplateModal open={true} onClose={vi.fn()} workspaceId={wsId} />)

    const textarea = screen.getByPlaceholderText(/Xây dựng ứng dụng web/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ trên hai mươi ký tự' } })
    fireEvent.click(screen.getByRole('button', { name: /Tạo mẫu đề xuất/i }))

    await waitFor(() => {
      expect(screen.getByDisplayValue('Bảng Ban Đầu')).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: /Xác nhận tạo/i }))

    await waitFor(() => {
      expect(screen.getByText('Tên bảng không được để trống')).toBeInTheDocument()
      // Preview elements still visible
      expect(screen.getByDisplayValue('Bảng Ban Đầu')).toBeInTheDocument()
    })
  })
})
