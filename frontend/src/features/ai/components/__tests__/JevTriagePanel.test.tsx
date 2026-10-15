import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { JevTriagePanel } from '../JevTriagePanel'
import type { TaskTriageResponse } from '../../types/jev.types'

describe('JevTriagePanel', () => {
  it('renders loading state when isLoading is true', () => {
    render(<JevTriagePanel triageData={null} isLoading={true} isDegraded={false} />)
    expect(screen.getByText('Jev đang phân tích và triage thẻ...')).toBeInTheDocument()
  })

  it('renders degraded message when isDegraded is true', () => {
    render(<JevTriagePanel triageData={null} isLoading={false} isDegraded={true} />)
    expect(
      screen.getByText('Gợi ý AI tạm không khả dụng — bạn có thể tiếp tục điền thủ công.')
    ).toBeInTheDocument()
  })

  it('renders nothing when triageData is null and not loading/degraded', () => {
    const { container } = render(
      <JevTriagePanel triageData={null} isLoading={false} isDegraded={false} />
    )
    expect(container.firstChild).toBeNull()
  })

  it('renders DirectAssign branch with apply all button and security warning', () => {
    const onApplyDirect = vi.fn()
    const triageData: TaskTriageResponse = {
      taskId: 't-1',
      suggestedDomain: 'Backend',
      suggestedWorkType: 'Bug',
      suggestedPriority: 'High',
      needsSecurityReview: true,
      confidenceScore: 0.94,
      routingAdvice: 'DirectAssign',
      options: [],
      degraded: false,
      latencyMs: 240,
    }

    render(
      <JevTriagePanel
        triageData={triageData}
        isLoading={false}
        isDegraded={false}
        onApplyDirect={onApplyDirect}
      />
    )

    expect(screen.getByText('Backend')).toBeInTheDocument()
    expect(screen.getByText('Bug')).toBeInTheDocument()
    expect(screen.getByText('High')).toBeInTheDocument()
    expect(screen.getByText('240ms')).toBeInTheDocument()
    expect(
      screen.getByText('Thẻ này có yếu tố bảo mật/dữ liệu nhạy cảm cần được rà soát.')
    ).toBeInTheDocument()

    const applyBtn = screen.getByRole('button', { name: /Áp dụng toàn bộ gợi ý/i })
    fireEvent.click(applyBtn)

    expect(onApplyDirect).toHaveBeenCalledWith({
      domain: 'Backend',
      workType: 'Bug',
      priority: 'High',
    })
  })

  it('renders OfferChoices branch with selectable pills', () => {
    const onSelectOption = vi.fn()
    const triageData: TaskTriageResponse = {
      taskId: 't-2',
      suggestedDomain: null,
      suggestedWorkType: null,
      suggestedPriority: null,
      needsSecurityReview: false,
      confidenceScore: 0.65,
      routingAdvice: 'OfferChoices',
      options: [
        { field: 'domain', value: 'DevOps', probability: 0.6 },
        { field: 'domain', value: 'Backend', probability: 0.4 },
      ],
      degraded: false,
      latencyMs: 180,
    }

    render(
      <JevTriagePanel
        triageData={triageData}
        isLoading={false}
        isDegraded={false}
        onSelectOption={onSelectOption}
      />
    )

    expect(screen.getByText('DevOps')).toBeInTheDocument()
    expect(screen.getByText('Backend')).toBeInTheDocument()

    const devopsBtn = screen.getByRole('button', { name: /DevOps/i })
    fireEvent.click(devopsBtn)

    expect(onSelectOption).toHaveBeenCalledWith({
      field: 'domain',
      value: 'DevOps',
      probability: 0.6,
    })
  })

  it('renders ManualRequired branch when confidence is low', () => {
    const triageData: TaskTriageResponse = {
      taskId: 't-3',
      suggestedDomain: null,
      suggestedWorkType: null,
      suggestedPriority: null,
      needsSecurityReview: false,
      confidenceScore: 0.35,
      routingAdvice: 'ManualRequired',
      options: [],
      degraded: false,
      latencyMs: 120,
    }

    render(<JevTriagePanel triageData={triageData} isLoading={false} isDegraded={false} />)

    expect(
      screen.getByText(/Thông tin chưa đủ để Jev tự tin phân loại/i)
    ).toBeInTheDocument()
  })
})
