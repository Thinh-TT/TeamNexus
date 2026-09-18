import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { NexusLogo } from '../NexusLogo'

describe('NexusLogo component', () => {
  it('renders svg with custom size', () => {
    render(<NexusLogo size={40} />)
    const svg = screen.getByTestId('nexus-logo')
    expect(svg).toBeInTheDocument()
    expect(svg).toHaveAttribute('width', '40')
    expect(svg).toHaveAttribute('height', '40')
  })
})
