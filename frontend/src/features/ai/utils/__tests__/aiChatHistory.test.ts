import { describe, expect, it } from 'vitest'
import type { AiChatMessage } from '../../types/aiChat.types'
import { canSaveAnswer, toRequestMessages, trimHistory } from '../aiChatHistory'

describe('aiChatHistory', () => {
  it('trims history keeping only the most recent 10 messages', () => {
    const messages: AiChatMessage[] = Array.from({ length: 15 }, (_, i) => ({
      id: `msg-${i}`,
      role: i % 2 === 0 ? 'user' : 'assistant',
      content: `Message ${i}`,
      createdAt: new Date().toISOString(),
    }))

    const trimmed = trimHistory(messages, 10)
    expect(trimmed).toHaveLength(10)
    expect(trimmed[0].id).toBe('msg-5')
    expect(trimmed[9].id).toBe('msg-14')
  })

  it('preserves all messages if count is within limit', () => {
    const messages: AiChatMessage[] = [
      { id: '1', role: 'user', content: 'hi', createdAt: '' },
      { id: '2', role: 'assistant', content: 'hello', createdAt: '' },
    ]

    const trimmed = trimHistory(messages, 10)
    expect(trimmed).toHaveLength(2)
    expect(trimmed).toEqual(messages)
  })

  it('maps toRequestMessages with role and content fields only', () => {
    const messages: AiChatMessage[] = [
      { id: '1', role: 'user', content: 'Xin chào', createdAt: '2026-01-01' },
      { id: '2', role: 'assistant', content: 'Chào bạn', createdAt: '2026-01-01' },
    ]

    const result = toRequestMessages(messages)
    expect(result).toEqual([
      { role: 'user', content: 'Xin chào' },
      { role: 'assistant', content: 'Chào bạn' },
    ])
  })

  it('validates canSaveAnswer correctly for boundaries (0, 2000, 2001 chars)', () => {
    expect(canSaveAnswer('')).toBe(false)
    expect(canSaveAnswer('   ')).toBe(false)
    expect(canSaveAnswer('Valid comment')).toBe(true)

    const exactly2000 = 'a'.repeat(2000)
    expect(canSaveAnswer(exactly2000)).toBe(true)

    const exactly2001 = 'a'.repeat(2001)
    expect(canSaveAnswer(exactly2001)).toBe(false)
  })
})
