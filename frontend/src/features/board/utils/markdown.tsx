import React from 'react'

export type MarkdownNode =
  | { kind: 'text'; value: string }
  | { kind: 'strong'; value: string }
  | { kind: 'em'; value: string }
  | { kind: 'code'; value: string }
  | { kind: 'link'; text: string; href: string }

/**
 * Kiểm tra an toàn cho URL: chỉ chấp nhận scheme http: hoặc https:.
 * Chặn javascript:, data:, file:, vbscript: v.v.
 */
export function isSafeHref(href: string): boolean {
  if (!href) return false
  const trimmed = href.trim().toLowerCase()
  return trimmed.startsWith('http://') || trimmed.startsWith('https://')
}

/**
 * Phân tích markdown nội dòng cơ bản:
 * - **đậm** -> strong
 * - *nghiêng* -> em
 * - `code` -> code
 * - [nhãn](url) -> link (chỉ khi url hợp lệ theo isSafeHref)
 * Không ném ngoại lệ khi gặp cú pháp chưa đóng hoặc lạ; render nguyên văn.
 */
export function parseInlineMarkdown(text: string): MarkdownNode[] {
  if (!text) return []

  const nodes: MarkdownNode[] = []
  let textBuffer = ''

  const flushText = () => {
    if (textBuffer.length > 0) {
      nodes.push({ kind: 'text', value: textBuffer })
      textBuffer = ''
    }
  }

  let i = 0
  const len = text.length

  while (i < len) {
    // 1. Bold: **text**
    if (text.startsWith('**', i)) {
      const closeIdx = text.indexOf('**', i + 2)
      if (closeIdx !== -1) {
        const inner = text.substring(i + 2, closeIdx)
        if (inner.length > 0 && !inner.includes('\n')) {
          flushText()
          nodes.push({ kind: 'strong', value: inner })
          i = closeIdx + 2
          continue
        }
      }
    }

    // 2. Inline code: `text`
    if (text[i] === '`') {
      const closeIdx = text.indexOf('`', i + 1)
      if (closeIdx !== -1) {
        const inner = text.substring(i + 1, closeIdx)
        if (inner.length > 0 && !inner.includes('\n')) {
          flushText()
          nodes.push({ kind: 'code', value: inner })
          i = closeIdx + 1
          continue
        }
      }
    }

    // 3. Link: [text](href)
    if (text[i] === '[') {
      const closeBracket = text.indexOf(']', i + 1)
      if (closeBracket !== -1 && text[closeBracket + 1] === '(') {
        const closeParen = text.indexOf(')', closeBracket + 2)
        if (closeParen !== -1) {
          const linkText = text.substring(i + 1, closeBracket)
          const href = text.substring(closeBracket + 2, closeParen).trim()
          if (!linkText.includes('\n') && !href.includes('\n') && isSafeHref(href)) {
            flushText()
            nodes.push({ kind: 'link', text: linkText, href })
            i = closeParen + 1
            continue
          }
        }
      }
    }

    // 4. Italic: *text* (chú ý không phải ** đã xét ở trên)
    if (text[i] === '*' && text[i + 1] !== '*') {
      // Tìm * đóng tiếp theo (không phải **)
      let closeIdx = -1
      for (let j = i + 1; j < len; j++) {
        if (text[j] === '\n') break
        if (text[j] === '*' && text[j - 1] !== '*' && text[j + 1] !== '*') {
          closeIdx = j
          break
        }
      }
      if (closeIdx !== -1) {
        const inner = text.substring(i + 1, closeIdx)
        if (inner.length > 0) {
          flushText()
          nodes.push({ kind: 'em', value: inner })
          i = closeIdx + 1
          continue
        }
      }
    }

    // Default: append character to text buffer
    textBuffer += text[i]
    i++
  }

  flushText()
  return nodes
}

/**
 * Render chuỗi markdown thành React elements an toàn, không dangerouslySetInnerHTML.
 */
export function renderMarkdown(text: string | null | undefined): React.ReactNode {
  if (!text) return null

  const nodes = parseInlineMarkdown(text)
  if (nodes.length === 0) return null

  return (
    <>
      {nodes.map((node, nodeIdx) => {
        switch (node.kind) {
          case 'strong':
            return <strong key={nodeIdx}>{node.value}</strong>
          case 'em':
            return <em key={nodeIdx}>{node.value}</em>
          case 'code':
            return (
              <code
                key={nodeIdx}
                style={{
                  backgroundColor: 'rgba(0, 0, 0, 0.06)',
                  padding: '2px 4px',
                  borderRadius: 4,
                  fontSize: '0.9em',
                  fontFamily: 'monospace',
                }}
              >
                {node.value}
              </code>
            )
          case 'link':
            return (
              <a
                key={nodeIdx}
                href={node.href}
                target="_blank"
                rel="noopener noreferrer"
                style={{ color: '#4f46e5', textDecoration: 'underline' }}
              >
                {node.text}
              </a>
            )
          case 'text': {
            const lines = node.value.split('\n')
            return (
              <React.Fragment key={nodeIdx}>
                {lines.map((line, lineIdx) => (
                  <React.Fragment key={lineIdx}>
                    {lineIdx > 0 && <br />}
                    {line}
                  </React.Fragment>
                ))}
              </React.Fragment>
            )
          }
        }
      })}
    </>
  )
}
