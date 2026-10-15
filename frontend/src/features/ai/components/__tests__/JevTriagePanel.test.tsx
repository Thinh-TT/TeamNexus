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

  /**
   * ISSUE-02 regression guards.
   *
   * The panel was written with Tailwind utility classes in a project that has NO Tailwind (no
   * `tailwind.config.*`, no PostCSS pipeline, no dependency). Every class resolved to nothing, so the
   * browser rendered run-together text and a raw HTML button on the live site:
   * `Phân loại thông minh (Jev System 1)AI tự tin 96%` and `Miền kỹ thuậtDevOps`.
   *
   * These assertions are deliberately about STRUCTURE rather than snapshot pixels: a snapshot would have
   * happily recorded the broken markup, which is how the defect reached production in the first place.
   */
  describe('ISSUE-02: no Tailwind, real Ant Design structure', () => {
    const directAssign: TaskTriageResponse = {
      taskId: 't-regress',
      suggestedDomain: 'DevOps',
      suggestedWorkType: 'Documentation',
      suggestedPriority: 'Low',
      needsSecurityReview: false,
      confidenceScore: 0.96,
      routingAdvice: 'DirectAssign',
      options: [],
      degraded: false,
      latencyMs: 1101,
    }

    it('never emits a Tailwind utility class anywhere in the tree', () => {
      const { container } = render(
        <JevTriagePanel triageData={directAssign} isLoading={false} isDegraded={false} onApplyDirect={vi.fn()} />
      )

      // The precise failure mode: these class names existed in the markup but no CSS was ever generated for
      // them, so their presence is the bug regardless of what they were supposed to look like.
      const tailwindish =
        /(^|\s)(bg-|text-\[|border-|space-[xy]-|grid-cols-|gap-\d|p-\d|px-\d|py-\d|rounded-|flex-wrap|backdrop-|animate-|shrink-0)/

      const offenders: string[] = []
      container.querySelectorAll<HTMLElement>('*').forEach((el) => {
        const className = typeof el.className === 'string' ? el.className : ''
        if (className && tailwindish.test(className)) {
          offenders.push(`${el.tagName.toLowerCase()}: ${className}`)
        }
      })

      expect(offenders).toEqual([])
    })

    it('renders the three suggestion fields as SEPARATE elements so labels cannot glue to values', () => {
      // The live symptom was the strings being concatenated by missing layout CSS. Each label and each value
      // must therefore be findable on its own.
      render(<JevTriagePanel triageData={directAssign} isLoading={false} isDegraded={false} />)

      expect(screen.getByText('Miền kỹ thuật')).toBeInTheDocument()
      expect(screen.getByText('Loại hình')).toBeInTheDocument()
      expect(screen.getByText('Mức ưu tiên')).toBeInTheDocument()

      expect(screen.getByText('DevOps')).toBeInTheDocument()
      expect(screen.getByText('Documentation')).toBeInTheDocument()
      expect(screen.getByText('Low')).toBeInTheDocument()

      // …and the header title must not be glued to the confidence badge either.
      expect(screen.getByText('Phân loại thông minh (Jev System 1)')).toBeInTheDocument()
      expect(screen.getByText('AI tự tin 96%')).toBeInTheDocument()
    })

    it('renders the apply button as a real Ant Design button, not a bare HTML element', () => {
      const { container } = render(
        <JevTriagePanel triageData={directAssign} isLoading={false} isDegraded={false} onApplyDirect={vi.fn()} />
      )

      const button = screen.getByRole('button', { name: /Áp dụng toàn bộ gợi ý/i })

      // Ant Design's `Button` is recognisable by its own class namespace. A bare `<button>` — the live defect —
      // has none of these.
      expect(button.className).toMatch(/ant-btn/)
      expect(button.className).toMatch(/ant-btn-primary/)

      // The original render also used a gradient/shadow Tailwind stack that produced nothing; the button must
      // not carry those leftovers either.
      expect(container.querySelector('button[class*="bg-emerald"]')).toBeNull()
    })

    it('offers the middle-band options as real Ant Design buttons with a distinct accessible name each', () => {
      const triageData: TaskTriageResponse = {
        taskId: 't-offer',
        suggestedDomain: null,
        suggestedWorkType: null,
        suggestedPriority: null,
        needsSecurityReview: false,
        confidenceScore: 0.65,
        routingAdvice: 'OfferChoices',
        options: [
          { field: 'domain', value: 'DevOps', probability: 0.6 },
          { field: 'workType', value: 'Bug', probability: 0.3 },
        ],
        degraded: false,
        latencyMs: 180,
      }

      render(<JevTriagePanel triageData={triageData} isLoading={false} isDegraded={false} onSelectOption={vi.fn()} />)

      const devops = screen.getByRole('button', { name: /Miền kỹ thuật: DevOps/ })
      const bug = screen.getByRole('button', { name: /Loại hình: Bug/ })

      expect(devops.className).toMatch(/ant-btn/)
      expect(bug.className).toMatch(/ant-btn/)

      // Distinct names are what make each option individually selectable by a screen reader — and by the tests.
      expect(devops.getAttribute('aria-label')).not.toBe(bug.getAttribute('aria-label'))
    })
  })
})
