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

  it('renders ManualRequired alert when confidence is low', async () => {
    const mockData: AssigneeSuggestionResponse = {
      taskId: 't-3',
      recommendedUserId: null,
      confidenceScore: 0.35,
      routingAdvice: 'ManualRequired',
      candidates: [],
      degraded: false,
      latencyMs: 140,
    }

    vi.mocked(jevApi.suggestAssignees).mockResolvedValue(mockData)

    render(
      <AssigneeSuggestion
        boardId="b-1"
        taskId="t-3"
        onSelectAssignee={onSelectAssignee}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i }))

    await waitFor(() => {
      expect(
        screen.getByText(/Chưa có ứng viên đạt độ tương thích cao/i)
      ).toBeInTheDocument()
    })
  })

  it('renders degraded alert when service is degraded', async () => {
    const mockData: AssigneeSuggestionResponse = {
      taskId: 't-4',
      recommendedUserId: null,
      confidenceScore: 0,
      routingAdvice: 'ManualRequired',
      candidates: [],
      degraded: true,
      latencyMs: 461,
    }

    vi.mocked(jevApi.suggestAssignees).mockResolvedValue(mockData)

    render(
      <AssigneeSuggestion
        boardId="b-1"
        taskId="t-4"
        onSelectAssignee={onSelectAssignee}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i }))

    await waitFor(() => {
      expect(
        screen.getByText(/Dịch vụ AI tạm không khả dụng/i)
      ).toBeInTheDocument()
    })
  })

  /**
   * ISSUE-03 regression guard: no Tailwind classes, real Ant Design structure.
   */
  describe('ISSUE-03: no Tailwind, real Ant Design structure', () => {
    it('never emits a Tailwind utility class anywhere in the tree', async () => {
      const mockData: AssigneeSuggestionResponse = {
        taskId: 't-guard',
        recommendedUserId: 'u-1',
        confidenceScore: 0.9,
        routingAdvice: 'DirectAssign',
        candidates: [
          {
            userId: 'u-1',
            displayName: 'Dev Alpha',
            probability: 0.9,
            reason: 'Phù hợp kỹ năng',
          },
        ],
        degraded: false,
        latencyMs: 150,
      }

      vi.mocked(jevApi.suggestAssignees).mockResolvedValue(mockData)

      const { container } = render(
        <AssigneeSuggestion
          boardId="b-1"
          taskId="t-guard"
          onSelectAssignee={onSelectAssignee}
        />
      )

      fireEvent.click(screen.getByRole('button', { name: /Gợi ý người làm \(Jev\)/i }))

      await waitFor(() => {
        expect(screen.getByText('Dev Alpha')).toBeInTheDocument()
      })

      const tailwindish =
        /(^|\s)(bg-|text-\[|border-|space-[xy]-|grid-cols-|gap-\d|p-\d|px-\d|py-\d|rounded-|flex-wrap|backdrop-|animate-|shrink-0)/

      const offenders: string[] = []
      container.querySelectorAll('*').forEach((el) => {
        const cls = el.getAttribute('class') || ''
        if (cls.split(/\s+/).some((token) => tailwindish.test(token))) {
          offenders.push(`<${el.tagName.toLowerCase()} class="${cls}">`)
        }
      })

      expect(
        offenders,
        `Found Tailwind classes in AssigneeSuggestion — project uses pure Ant Design:\n${offenders.join('\n')}`
      ).toEqual([])
    })
  })
})

