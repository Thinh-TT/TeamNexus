import { describe, expect, it } from 'vitest'
import { isSafeHref, parseInlineMarkdown } from '../markdown'

describe('markdown parser (parseInlineMarkdown & isSafeHref)', () => {
  it('returns empty array for empty string', () => {
    expect(parseInlineMarkdown('')).toEqual([])
  })

  it('parses plain text without formatting', () => {
    const nodes = parseInlineMarkdown('Đây là văn bản thuần không có định dạng')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Đây là văn bản thuần không có định dạng' },
    ])
  })

  it('parses bold text syntax (**text**)', () => {
    const nodes = parseInlineMarkdown('Văn bản **rất quan trọng** cần chú ý')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Văn bản ' },
      { kind: 'strong', value: 'rất quan trọng' },
      { kind: 'text', value: ' cần chú ý' },
    ])
  })

  it('parses italic text syntax (*text*)', () => {
    const nodes = parseInlineMarkdown('Văn bản *nghiêng nhẹ* ở đây')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Văn bản ' },
      { kind: 'em', value: 'nghiêng nhẹ' },
      { kind: 'text', value: ' ở đây' },
    ])
  })

  it('parses inline code syntax (`code`)', () => {
    const nodes = parseInlineMarkdown('Sử dụng lệnh `npm run test` để kiểm thử')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Sử dụng lệnh ' },
      { kind: 'code', value: 'npm run test' },
      { kind: 'text', value: ' để kiểm thử' },
    ])
  })

  it('parses safe markdown links [label](https://...)', () => {
    const nodes = parseInlineMarkdown('Truy cập [Google](https://google.com) để tra cứu')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Truy cập ' },
      { kind: 'link', text: 'Google', href: 'https://google.com' },
      { kind: 'text', value: ' để tra cứu' },
    ])
  })

  it('parses a sentence mixing all 4 markdown styles', () => {
    const text = 'Xem **thẻ này** với *chú ý* mã `PR-102` tại [Liên kết](https://teamnexus.dev).'
    const nodes = parseInlineMarkdown(text)
    expect(nodes).toEqual([
      { kind: 'text', value: 'Xem ' },
      { kind: 'strong', value: 'thẻ này' },
      { kind: 'text', value: ' với ' },
      { kind: 'em', value: 'chú ý' },
      { kind: 'text', value: ' mã ' },
      { kind: 'code', value: 'PR-102' },
      { kind: 'text', value: ' tại ' },
      { kind: 'link', text: 'Liên kết', href: 'https://teamnexus.dev' },
      { kind: 'text', value: '.' },
    ])
  })

  it('treats unclosed markdown markers as verbatim plain text', () => {
    const nodes1 = parseInlineMarkdown('Đây là **chưa đóng')
    expect(nodes1).toEqual([{ kind: 'text', value: 'Đây là **chưa đóng' }])

    const nodes2 = parseInlineMarkdown('Mã lệnh `chưa đóng dòng')
    expect(nodes2).toEqual([{ kind: 'text', value: 'Mã lệnh `chưa đóng dòng' }])

    const nodes3 = parseInlineMarkdown('Link hỏng [nhãn](không có đóng ngoặc')
    expect(nodes3).toEqual([{ kind: 'text', value: 'Link hỏng [nhãn](không có đóng ngoặc' }])
  })

  it('rejects javascript: URLs and does not generate link node', () => {
    expect(isSafeHref('javascript:alert(1)')).toBe(false)
    const nodes = parseInlineMarkdown('[bấm vào đây](javascript:alert(1))')
    expect(nodes).toEqual([
      { kind: 'text', value: '[bấm vào đây](javascript:alert(1))' },
    ])
  })

  it('rejects data:, file:, vbscript: URLs as unsafe', () => {
    expect(isSafeHref('data:text/html,<script>alert(1)</script>')).toBe(false)
    expect(isSafeHref('file:///C:/Windows/system32')).toBe(false)
    expect(isSafeHref('vbscript:msgbox(1)')).toBe(false)

    const nodes = parseInlineMarkdown('[tệp tin](file:///C:/passwords.txt)')
    expect(nodes).toEqual([
      { kind: 'text', value: '[tệp tin](file:///C:/passwords.txt)' },
    ])
  })

  it('preserves newlines in text nodes', () => {
    const nodes = parseInlineMarkdown('Dòng 1\nDòng 2\nDòng 3')
    expect(nodes).toEqual([
      { kind: 'text', value: 'Dòng 1\nDòng 2\nDòng 3' },
    ])
  })
})
