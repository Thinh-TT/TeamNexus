import { describe, expect, it } from 'vitest'
import {
  IDENTIFIER_PLACEHOLDER,
  containsIdentifier,
  redactAnswerProse,
  redactIdentifiers,
} from '../redactIdentifiers'

/**
 * Phase 14.6 §A2 — the client half of "AI Chat never shows a raw Guid".
 *
 * <p>The exact sentence that leaked on the deployed build is the first case: a model is free to ignore the
 * prompt rule, so the filter is the only part of the requirement that can actually be locked down.</p>
 */
describe('redactIdentifiers', () => {
  const leakedId = '9724c884-d103-4147-9b68-7532d97f04a9'

  it('redacts the identifier the deployed model actually wrote, and leaves the sentence readable', () => {
    const answer = `Sau khi có thẻ trong ngữ cảnh, mình sẽ đề xuất chuyển sang cột "process" (id: ${leakedId}).`

    const result = redactIdentifiers(answer)

    expect(result).not.toContain(leakedId)
    expect(result).toContain(IDENTIFIER_PLACEHOLDER)
    // A redaction, not a deletion: the sentence must still read as a sentence.
    expect(result).toContain('cột "process"')
    expect(result.endsWith(').')).toBe(true)
  })

  it('redacts every identifier in a list and is idempotent', () => {
    const answer = `Cột 11111111-1111-1111-1111-111111111111 chứa thẻ 22222222-2222-2222-2222-222222222222.`

    const once = redactIdentifiers(answer)

    expect(containsIdentifier(once)).toBe(false)
    expect(once.match(/\[mã nội bộ\]/g)).toHaveLength(2)
    expect(redactIdentifiers(once)).toBe(once)
  })

  it('does not mistake a longer hyphenated hex run for an identifier', () => {
    // `-` is not a word character, so a naive `\b`-delimited pattern would bite a 36-character window out of
    // this digest and mangle a legitimate value.
    const digest = 'deadbeef-dead-beef-dead-beefdeadbeefdead'
    const sha = 'a'.repeat(64)

    expect(redactIdentifiers(digest)).toBe(digest)
    expect(redactIdentifiers(sha)).toBe(sha)
    expect(containsIdentifier(digest)).toBe(false)
  })

  it('leaves ordinary prose — and empty input — untouched', () => {
    const prose = 'Cột "Đang làm" có 3 thẻ quá hạn, nên xử lý trước.'

    expect(redactIdentifiers(prose)).toBe(prose)
    expect(redactIdentifiers('')).toBe('')
    expect(redactIdentifiers(null)).toBe('')
    expect(redactIdentifiers(undefined)).toBe('')
    expect(containsIdentifier('')).toBe(false)
    expect(containsIdentifier(null)).toBe(false)
  })
})

describe('redactAnswerProse', () => {
  const leakedId = '9724c884-d103-4147-9b68-7532d97f04a9'

  it('redacts the prose but never touches the proposal block', () => {
    const answer =
      `Nên chuyển thẻ sang cột (id: ${leakedId}) để tiếp tục.\n\n` +
      '```json\n' +
      `{"proposal":{"kind":"MoveTaskToColumn","summary":"Chuyển thẻ","payload":{"taskId":"${leakedId}","columnId":"${leakedId}"}}}\n` +
      '```'

    const result = redactAnswerProse(answer)

    // Prose: filtered.
    expect(result).not.toContain(`(id: ${leakedId})`)
    expect(result).toContain(IDENTIFIER_PLACEHOLDER)

    // Block: gone from the prose entirely — and crucially, the real ids were never rewritten, because a
    // redacted `taskId` would make the proposal unsubmittable.
    expect(result).not.toContain('```json')
    expect(result).toBe('Nên chuyển thẻ sang cột (id: [mã nội bộ]) để tiếp tục.')
  })

  it('still filters while the fence is incomplete', () => {
    // Mid-stream the closing fence has not arrived yet, so the whole buffer is treated as prose. Identifiers
    // must not slip through during that window just because the block is unfinished.
    const partial = `Đang trả lời, id ${leakedId}\n\n\`\`\`json\n{"proposal":{"taskId":"${leakedId}"`

    const result = redactAnswerProse(partial)

    expect(result).not.toContain(leakedId)
  })

  it('returns an empty string for empty input', () => {
    expect(redactAnswerProse('')).toBe('')
    expect(redactAnswerProse(null)).toBe('')
  })
})
