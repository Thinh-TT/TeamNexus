import { describe, expect, it } from 'vitest'
import { parseSseFrames } from '../sse'

describe('parseSseFrames', () => {
  it('parses a single complete frame with standard delimiter', () => {
    const input = 'event: delta\ndata: {"text":"hello"}\n\n'
    const result = parseSseFrames(input)
    expect(result.events).toEqual([
      { event: 'delta', data: '{"text":"hello"}' },
    ])
    expect(result.carry).toBe('')
  })

  it('parses multiple frames in a single chunk', () => {
    const input =
      'event: meta\ndata: {"model":"deepseek"}\n\nevent: delta\ndata: {"text":"Xin chào"}\n\n'
    const result = parseSseFrames(input)
    expect(result.events).toHaveLength(2)
    expect(result.events[0]).toEqual({
      event: 'meta',
      data: '{"model":"deepseek"}',
    })
    expect(result.events[1]).toEqual({
      event: 'delta',
      data: '{"text":"Xin chào"}',
    })
    expect(result.carry).toBe('')
  })

  it('correctly manages carry when a frame is split across 2 chunks', () => {
    const chunk1 = 'event: delta\ndata: {"text":'
    const result1 = parseSseFrames(chunk1)
    expect(result1.events).toEqual([])
    expect(result1.carry).toBe(chunk1)

    const chunk2 = '"hoàn thành"}\n\n'
    const result2 = parseSseFrames(result1.carry + chunk2)
    expect(result2.events).toEqual([
      { event: 'delta', data: '{"text":"hoàn thành"}' },
    ])
    expect(result2.carry).toBe('')
  })

  it('correctly manages carry when a frame is split across 3 chunks', () => {
    const part1 = 'event: meta\n'
    const r1 = parseSseFrames(part1)
    expect(r1.events).toEqual([])
    expect(r1.carry).toBe(part1)

    const part2 = 'data: {"taskId":"123"'
    const r2 = parseSseFrames(r1.carry + part2)
    expect(r2.events).toEqual([])

    const part3 = '}\n\n'
    const r3 = parseSseFrames(r2.carry + part3)
    expect(r3.events).toEqual([
      { event: 'meta', data: '{"taskId":"123"}' },
    ])
    expect(r3.carry).toBe('')
  })

  it('handles Windows CRLF (\\r\\n\\r\\n) delimiters seamlessly', () => {
    const input = 'event: done\r\ndata: {"answer":"OK"}\r\n\r\n'
    const result = parseSseFrames(input)
    expect(result.events).toEqual([
      { event: 'done', data: '{"answer":"OK"}' },
    ])
    expect(result.carry).toBe('')
  })

  it('ignores comment lines starting with colon (:)', () => {
    const input = ': keep-alive ping\nevent: delta\n: another comment\ndata: {"text":"ok"}\n\n'
    const result = parseSseFrames(input)
    expect(result.events).toEqual([
      { event: 'delta', data: '{"text":"ok"}' },
    ])
  })

  it('concatenates multiple data: lines with newline', () => {
    const input = 'event: delta\ndata: line 1\ndata: line 2\n\n'
    const result = parseSseFrames(input)
    expect(result.events).toEqual([
      { event: 'delta', data: 'line 1\nline 2' },
    ])
  })

  it('defaults event name to "message" when event: line is omitted', () => {
    const input = 'data: simple message\n\n'
    const result = parseSseFrames(input)
    expect(result.events).toEqual([
      { event: 'message', data: 'simple message' },
    ])
  })

  it('returns empty events and empty carry for empty input', () => {
    expect(parseSseFrames('')).toEqual({ events: [], carry: '' })
  })
})
