import { describe, expect, it } from 'vitest'
import { parseProposal, splitProposal, unwrapEnvelope } from '../agentChatProposal'

/**
 * Pure parser tests for the board chat (Phase 14.5 §B).
 *
 * <p>Deliberately mirrors the C# `AgentChatProposalTests` cases: the two parsers must agree, because the
 * UI decides whether to OFFER an action card while the server decides whether to ACCEPT it. A drift shows
 * the user a button that is guaranteed to fail.</p>
 */
describe('agentChatProposal', () => {
  describe('splitProposal', () => {
    it('separates the readable text from the proposal block', () => {
      const answer =
        'Tôi thấy thẻ này nên chuyển cột.\n\n```json\n{"proposal":{"kind":"MoveTaskToColumn"}}\n```'

      const result = splitProposal(answer)

      expect(result.visibleText).toBe('Tôi thấy thẻ này nên chuyển cột.')
      // The envelope is unwrapped, so what is offered is the proposal itself.
      expect(result.jsonBlock).toBe('{"kind":"MoveTaskToColumn"}')
    })

    it('takes the LAST block, so an example in the explanation is not submitted', () => {
      const answer =
        'Ví dụ định dạng:\n```json\n{"kind":"Example"}\n```\n' +
        'Đề xuất thật:\n```json\n{"kind":"MoveTaskToColumn"}\n```'

      const result = splitProposal(answer)

      expect(result.jsonBlock).toContain('MoveTaskToColumn')
      expect(result.jsonBlock).not.toContain('Example')
      expect(result.visibleText).toContain('Ví dụ định dạng:')
    })

    it('leaves a plain answer untouched', () => {
      const result = splitProposal('Chỉ là câu trả lời bình thường.')

      expect(result.visibleText).toBe('Chỉ là câu trả lời bình thường.')
      expect(result.jsonBlock).toBeNull()
    })

    it('never throws on an unterminated fence, an empty fence or a blank answer', () => {
      const unterminated = splitProposal('Giải thích trước\n```json\n{"proposal":{')
      expect(unterminated.jsonBlock).toBeNull()
      expect(unterminated.visibleText).toContain('Giải thích trước')

      expect(splitProposal('Không có gì\n```json\n\n```').jsonBlock).toBeNull()
      expect(splitProposal('   ')).toEqual({ visibleText: '', jsonBlock: null })
      expect(splitProposal(null)).toEqual({ visibleText: '', jsonBlock: null })
      expect(splitProposal(undefined)).toEqual({ visibleText: '', jsonBlock: null })
    })
  })

  describe('unwrapEnvelope', () => {
    it('extracts the proposal object and accepts a bare object', () => {
      expect(unwrapEnvelope('{"proposal":{"kind":"MoveTaskToColumn"}}')).toBe(
        '{"kind":"MoveTaskToColumn"}'
      )
      expect(unwrapEnvelope('{"kind":"MoveTaskToColumn"}')).toBe('{"kind":"MoveTaskToColumn"}')
    })

    it('returns malformed JSON unchanged, because the server owns that error message', () => {
      expect(unwrapEnvelope('{not valid json')).toBe('{not valid json')
      expect(unwrapEnvelope('')).toBe('')
    })
  })

  describe('parseProposal', () => {
    it('describes each supported kind', () => {
      expect(
        parseProposal(
          '{"kind":"MoveTaskToColumn","summary":"Chuyển","payload":{"taskId":"t1","columnId":"c1"}}'
        )
      ).toMatchObject({ kind: 'MoveTaskToColumn', summary: 'Chuyển' })

      expect(
        parseProposal(
          '{"kind":"UpdateTaskFields","summary":"Sửa","payload":{"taskId":"t1","priority":"Urgent"}}'
        )
      ).toMatchObject({ kind: 'UpdateTaskFields' })

      expect(
        parseProposal('{"kind":"CreateSubtasks","summary":"Tạo","payload":{"columnId":"c1"}}')
      ).toMatchObject({ kind: 'CreateSubtasks' })
    })

    it('returns null for an unknown kind so no card is offered', () => {
      expect(parseProposal('{"kind":"DeleteEverything","payload":{}}')).toBeNull()
    })

    it('returns null for malformed JSON or a missing block', () => {
      expect(parseProposal('{not valid json')).toBeNull()
      expect(parseProposal(null)).toBeNull()
    })

    it('tolerates a proposal with no payload rather than crashing the panel', () => {
      const parsed = parseProposal('{"kind":"MoveTaskToColumn","summary":"x"}')

      expect(parsed).not.toBeNull()
      expect(parsed!.payload.taskId).toBeNull()
      expect(parsed!.payload.columnId).toBeNull()
    })

    it('accepts the wrapped envelope, which is the shape the model is asked for', () => {
      const parsed = parseProposal('{"proposal":{"kind":"MoveTaskToColumn","summary":"Chuyển"}}')

      expect(parsed).toMatchObject({ kind: 'MoveTaskToColumn' })
    })
  })
})
