import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AssigneeSuggestion } from '../AssigneeSuggestion'
import { jevApi } from '../../services/jevApi'
import type { AssigneeSuggestionResponse } from '../../types/jev.types'

vi.mock('../../services/jevApi', () => ({
  jevApi: {
    suggestAssignees: vi.fn(),
  },
}))

describe('AssigneeSuggestion', () => {
  const onSelectAssignee = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders collapsed button initially', () => {
    render(
      <AssigneeSuggestion
        boardId="b-1"
        taskId="t-1"
        onSelectAssignee={onSelectAssignee}
      />
    )
    expect(screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i })).toBeInTheDocument()
    expect(screen.queryByText('Đánh giá phân công')).not.toBeInTheDocument()
  })

  it('fetches and displays DirectAssign candidate when opened', async () => {
    const mockData: AssigneeSuggestionResponse = {
      taskId: 't-1',
      recommendedUserId: 'u-expert',
      confidenceScore: 0.89,
      routingAdvice: 'DirectAssign',
      candidates: [
        {
          userId: 'u-expert',
          displayName: 'Nguyễn Senior Dev',
          probability: 0.89,
          reason: 'Phù hợp kỹ năng Frontend/React và còn dung lượng WIP',
        },
      ],
      degraded: false,
      latencyMs: 210,
    }

    vi.mocked(jevApi.suggestAssignees).mockResolvedValue(mockData)

    render(
      <AssigneeSuggestion
        boardId="b-1"
        taskId="t-1"
        onSelectAssignee={onSelectAssignee}
      />
    )

    const toggleBtn = screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i })
    fireEvent.click(toggleBtn)

    await waitFor(() => {
      expect(screen.getByText('Nguyễn Senior Dev')).toBeInTheDocument()
      expect(screen.getByText(/89% phù hợp/i)).toBeInTheDocument()
      expect(screen.getByText(/Phù hợp kỹ năng Frontend\/React/i)).toBeInTheDocument()
      expect(screen.getByText('210ms')).toBeInTheDocument()
    })

    const assignBtn = screen.getByRole('button', { name: /Gán ngay/i })
    fireEvent.click(assignBtn)

    expect(onSelectAssignee).toHaveBeenCalledWith('u-expert')
  })

  it('renders OfferChoices list when multiple candidates are viable', async () => {
    const mockData: AssigneeSuggestionResponse = {
      taskId: 't-2',
      recommendedUserId: null,
      confidenceScore: 0.65,
      routingAdvice: 'OfferChoices',
      candidates: [
        {
          userId: 'u-1',
          displayName: 'Lê Dev A',
          probability: 0.68,
          reason: 'Kinh nghiệm Backend',
        },
        {
          userId: 'u-2',
          displayName: 'Phạm Dev B',
          probability: 0.55,
          reason: 'Đang rảnh',
        },
      ],
      degraded: false,
      latencyMs: 195,
    }

    vi.mocked(jevApi.suggestAssignees).mockResolvedValue(mockData)

    render(
      <AssigneeSuggestion
        boardId="b-1"
        taskId="t-2"
        onSelectAssignee={onSelectAssignee}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i }))

    await waitFor(() => {
      expect(screen.getByText('Lê Dev A')).toBeInTheDocument()
      expect(screen.getByText('Phạm Dev B')).toBeInTheDocument()
    })

    const selectBtns = screen.getAllByRole('button', { name: 'Chọn' })
    fireEvent.click(selectBtns[0])

    expect(onSelectAssignee).toHaveBeenCalledWith('u-1')
  })
})
