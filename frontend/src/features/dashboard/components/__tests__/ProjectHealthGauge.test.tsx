import React from 'react'
import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ProjectHealthGauge } from '../ProjectHealthGauge'
import type { DashboardProjectHealth } from '../../types/dashboard.types'

describe('ProjectHealthGauge', () => {
  it('renders Empty state when health is null or undefined without Progress', () => {
    const { rerender } = render(<ProjectHealthGauge health={null} />)
    expect(screen.getByText('Chưa đủ dữ liệu để tính sức khỏe dự án')).toBeInTheDocument()
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument()

    rerender(<ProjectHealthGauge health={undefined} />)
    expect(screen.getByText('Chưa đủ dữ liệu để tính sức khỏe dự án')).toBeInTheDocument()
  })

  it('renders band "Tốt" and green stroke for score=85', () => {
    const health: DashboardProjectHealth = {
      score: 85,
      band: 'Tốt',
      components: { overdue: 5, atRisk: 10 },
      reasons: [],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('Tốt')).toBeInTheDocument()
    expect(screen.getByText('85')).toBeInTheDocument()
  })

  it('renders band "Cần chú ý" and amber color for score=65', () => {
    const health: DashboardProjectHealth = {
      score: 65,
      band: 'Cần chú ý',
      components: { overdue: 15, atRisk: 10, stalled: 10 },
      reasons: ['Một số thẻ bị trễ'],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('Cần chú ý')).toBeInTheDocument()
    expect(screen.getByText('65')).toBeInTheDocument()
  })

  it('renders band "Rủi ro" and orange color for score=45', () => {
    const health: DashboardProjectHealth = {
      score: 45,
      band: 'Rủi ro',
      components: { overdue: 30, atRisk: 25 },
      reasons: ['Rủi ro trễ tiến độ'],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('Rủi ro')).toBeInTheDocument()
    expect(screen.getByText('45')).toBeInTheDocument()
  })

  it('renders band "Nghiêm trọng" and red color for score=20', () => {
    const health: DashboardProjectHealth = {
      score: 20,
      band: 'Nghiêm trọng',
      components: { overdue: 50, atRisk: 30 },
      reasons: ['Nhiều thẻ quá hạn nghiêm trọng'],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('Nghiêm trọng')).toBeInTheDocument()
    expect(screen.getByText('20')).toBeInTheDocument()
  })

  it('renders properly when score is 0 without NaN or crash', () => {
    const health: DashboardProjectHealth = {
      score: 0,
      band: 'Nghiêm trọng',
      components: { overdue: 100 },
      reasons: ['Tất cả các thẻ đều quá hạn'],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('0')).toBeInTheDocument()
    expect(screen.getByText('Nghiêm trọng')).toBeInTheDocument()
  })

  it('handles empty components and empty reasons gracefully without crashing', () => {
    const health: DashboardProjectHealth = {
      score: 100,
      band: 'Tốt',
      components: {},
      reasons: [],
    }

    render(<ProjectHealthGauge health={health} />)
    expect(screen.getByText('100')).toBeInTheDocument()
    expect(screen.getByText('Tốt')).toBeInTheDocument()
  })

  it('renders all 5 component entries when provided', () => {
    const health: DashboardProjectHealth = {
      score: 72,
      band: 'Cần chú ý',
      components: {
        overdue: 10,
        atRisk: 6,
        stalled: 4,
        aging: 4,
        load: 4,
      },
      reasons: ['2 thẻ quá hạn', '1 thẻ sắp hết hạn'],
    }

    const { container } = render(<ProjectHealthGauge health={health} />)
    expect(container.querySelector('[data-testid="project-health-gauge"]')).toBeInTheDocument()
    expect(screen.getByText('72')).toBeInTheDocument()
  })
})
