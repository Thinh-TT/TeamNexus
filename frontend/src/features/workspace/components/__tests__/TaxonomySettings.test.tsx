import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { TaxonomySettings } from '../TaxonomySettings'
import { jevApi } from '../../../ai/services/jevApi'
import type { WorkspaceTaxonomyResponse } from '../../../ai/types/jev.types'

vi.mock('../../../ai/services/jevApi', () => ({
  jevApi: {
    getTaxonomies: vi.fn(),
    upsertTaxonomies: vi.fn(),
  },
}))

describe('TaxonomySettings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('loads and displays taxonomies from API', async () => {
    const mockResponse: WorkspaceTaxonomyResponse = {
      workspaceId: 'ws-1',
      domains: [
        {
          id: 'tax-1',
          kind: 'domain',
          key: 'AI/ML',
          label: 'Trí tuệ nhân tạo',
          colorHex: '#8b5cf6',
          position: 0,
        },
      ],
      workTypes: [],
      seeded: false,
    }

    vi.mocked(jevApi.getTaxonomies).mockResolvedValue(mockResponse)

    render(<TaxonomySettings workspaceId="ws-1" isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByText('Trí tuệ nhân tạo')).toBeInTheDocument()
      expect(screen.getByText('(AI/ML)')).toBeInTheDocument()
    })
  })

  it('allows adding default taxonomy items if list is empty', async () => {
    vi.mocked(jevApi.getTaxonomies).mockResolvedValue({
      workspaceId: 'ws-1',
      domains: [],
      workTypes: [],
      seeded: false,
    })

    render(<TaxonomySettings workspaceId="ws-1" isManagerOrAdmin={true} />)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /Khôi phục mặc định/i })).toBeInTheDocument()
    })

    fireEvent.click(screen.getByRole('button', { name: /Khôi phục mặc định/i }))

    expect(screen.getByText('Backend')).toBeInTheDocument()
    expect(screen.getByText('Frontend')).toBeInTheDocument()
    expect(screen.getByText('Feature')).toBeInTheDocument()
    expect(screen.getByText('Bug')).toBeInTheDocument()
  })
})
