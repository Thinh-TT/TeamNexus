import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { renderMarkdown } from '../markdown'

describe('renderMarkdown', () => {
  it('returns null for null, undefined, or empty string', () => {
    const { container: c1 } = render(<div>{renderMarkdown(null)}</div>)
    expect(c1.innerHTML).toBe('<div></div>')

    const { container: c2 } = render(<div>{renderMarkdown(undefined)}</div>)
    expect(c2.innerHTML).toBe('<div></div>')

    const { container: c3 } = render(<div>{renderMarkdown('')}</div>)
    expect(c3.innerHTML).toBe('<div></div>')
  })

  it('renders strong, code, and anchor elements correctly', () => {
    render(
      <div>
        {renderMarkdown('Text **đậm** và mã `test-id` cùng [Trang chủ](https://example.com)')}
      </div>
    )

    const strongEl = screen.getByText('đậm')
    expect(strongEl.tagName).toBe('STRONG')

    const codeEl = screen.getByText('test-id')
    expect(codeEl.tagName).toBe('CODE')

    const linkEl = screen.getByRole('link', { name: 'Trang chủ' })
    expect(linkEl).toBeInTheDocument()
    expect(linkEl).toHaveAttribute('href', 'https://example.com')
    expect(linkEl).toHaveAttribute('target', '_blank')
    expect(linkEl).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('renders raw HTML as verbatim text without executing or injecting tags', () => {
    const malicious = '<img src=x onerror=alert(1) /><b>thô</b>'
    const { container } = render(<div>{renderMarkdown(malicious)}</div>)

    // Verify there is no img element created in DOM
    expect(container.querySelector('img')).toBeNull()
    expect(container.querySelector('b')).toBeNull()
    // Text content must match raw string
    expect(container.textContent).toContain('<img src=x onerror=alert(1) /><b>thô</b>')
  })

  it('renders line breaks (<br />) for newlines in text', () => {
    const { container } = render(<div>{renderMarkdown('Dòng 1\nDòng 2')}</div>)
    expect(container.querySelector('br')).toBeInTheDocument()
    expect(container.textContent).toBe('Dòng 1Dòng 2')
  })
})
