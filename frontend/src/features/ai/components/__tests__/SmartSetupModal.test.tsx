import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { SmartSetupModal } from '../SmartSetupModal'
import { smartSetupApi } from '../../services/smartSetupApi'
import type { SmartSetupProposal } from '../../types/smartSetup.types'

vi.mock('../../services/smartSetupApi', () => ({
  smartSetupApi: {
    generateSmartSetup: vi.fn(),
    getWorkspaceMembers: vi.fn(),
  },
}))

describe('SmartSetupModal', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(smartSetupApi.getWorkspaceMembers).mockResolvedValue([])
  })

  afterEach(() => {
    cleanup()
  })

  it('renders Step 1 prompt input when open', () => {
    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
      />
    )

    expect(screen.getByText('AI Smart Setup')).toBeInTheDocument()
    expect(
      screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    ).toBeInTheDocument()
  })

  it('generates proposal and transitions to Step 2 review', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Phân tích hệ thống thành 2 sub-task',
      tasks: [
        {
          title: 'Subtask 1: Backend Setup',
          description: 'Cấu hình database schema',
          priority: 'High',
          labels: [{ labelId: null, name: 'backend', exists: false }],
          assignee: null,
        },
      ],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, {
      target: { value: 'Xây dựng module AI Smart Setup' },
    })

    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    fireEvent.click(generateBtn)

    await waitFor(() => {
      expect(smartSetupApi.generateSmartSetup).toHaveBeenCalledWith('board-1', {
        description: 'Xây dựng module AI Smart Setup',
      })
      expect(screen.getByText('Phân tích hệ thống thành 2 sub-task')).toBeInTheDocument()
      expect(screen.getByDisplayValue('Subtask 1: Backend Setup')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /Xác nhận đề xuất/i })).toBeInTheDocument()
    })
  })

  it('transitions to Step 3 confirmed state on clicking confirm', async () => {
    const mockProposal: SmartSetupProposal = {
      summary: 'Hoàn thành đề xuất',
      tasks: [
        {
          title: 'Task A',
          description: null,
          priority: 'Medium',
          labels: [],
          assignee: null,
        },
      ],
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockResolvedValueOnce(mockProposal)

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả hợp lệ' } })
    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    fireEvent.click(generateBtn)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Xác nhận đề xuất/i })).toBeInTheDocument()
    })

    const confirmBtn = screen.getByRole('button', { name: /Xác nhận đề xuất/i })
    fireEvent.click(confirmBtn)

    await waitFor(() => {
      expect(
        screen.getByText('Đề xuất phân rã công việc đã được xác nhận!')
      ).toBeInTheDocument()
      expect(screen.getByText(/Accountability Layer/i)).toBeInTheDocument()
    })
  })

  it('displays error alert when generation fails with 403 Forbidden', async () => {
    const axiosErr = {
      isAxiosError: true,
      response: {
        status: 403,
        data: { error: 'Requires Manager or Admin role in this workspace.' },
      },
    }

    vi.mocked(smartSetupApi.generateSmartSetup).mockRejectedValueOnce(axiosErr)

    render(
      <SmartSetupModal
        open={true}
        onClose={vi.fn()}
        workspaceId="ws-1"
        boardId="board-1"
      />
    )

    const textarea = screen.getByPlaceholderText(/Xây dựng tính năng thông báo Real-time/i)
    fireEvent.change(textarea, { target: { value: 'Mô tả từ Member' } })

    const generateBtn = screen.getByRole('button', { name: /Tạo đề xuất với AI/i })
    await waitFor(() => {
      expect(generateBtn).not.toBeDisabled()
    })
    await act(async () => {
      fireEvent.click(generateBtn)
    })

    await waitFor(() => {
      expect(smartSetupApi.generateSmartSetup).toHaveBeenCalledWith('board-1', {
        description: 'Mô tả từ Member',
      })
    })

    expect(await screen.findByText(/Không đủ quyền thực hiện/i)).toBeInTheDocument()
    expect(screen.getByText(/Manager/i)).toBeInTheDocument()
  })
})
