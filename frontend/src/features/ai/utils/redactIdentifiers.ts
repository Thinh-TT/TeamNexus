import { splitProposal } from './agentChatProposal'

/**
 * Hides database identifiers (GUID / UUID) inside AI **prose** (Phase 14.6 §A2).
 *
 * <p><b>Why the client filters at all, when the server already asks the model not to.</b> A prompt is a
 * request, not an enforcement point. Live testing caught the model writing
 * <i>"…chuyển sang cột "process" (id: 9724c884-…) "</i> straight into a user-facing sentence, and the text
 * arrives one delta at a time — so a server-side filter would either have to buffer the whole answer (which
 * breaks streaming) or risk cutting an identifier that spans two frames. Filtering the <b>accumulated</b>
 * text on the client has neither problem: the answer is still rendered as it is produced, and a Guid split
 * across two deltas is whole by the time it is matched.</p>
 *
 * <p><b>Prose only.</b> The fenced proposal block legitimately carries real ids — the Accountability Layer
 * needs them to apply anything — so the block is split out first and never passed through the pattern.
 * Redacting inside it would silently turn a submittable proposal into an invalid one.</p>
 *
 * <p>Mirrors `AiChatAnswerSanitizer` (C#) so a stored turn and a live one are hidden by the same rule.</p>
 */

/** What an identifier becomes. Visible on purpose: a silent deletion reads like the model never said it. */
export const IDENTIFIER_PLACEHOLDER = '[mã nội bộ]'

/**
 * Canonical UUID shape: 8-4-4-4-12 hexadecimal digits, not glued to more hex on either side. The
 * lookarounds matter: `-` is not a word character, so a bare `\b` would let the pattern bite a
 * 36-character window out of a longer hyphenated hex blob that is not an identifier at all.
 */
const IDENTIFIER_PATTERN = /(?<![0-9a-fA-F-])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F-])/g

const IDENTIFIER_TEST = /(?<![0-9a-fA-F-])[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}(?![0-9a-fA-F-])/

/** Replaces every identifier-shaped token with {@link IDENTIFIER_PLACEHOLDER}. Never throws. */
export function redactIdentifiers(text: string | null | undefined): string {
  if (!text) {
    return ''
  }

  return text.replace(IDENTIFIER_PATTERN, IDENTIFIER_PLACEHOLDER)
}

/** True when the text still contains something shaped like a database identifier. */
export function containsIdentifier(text: string | null | undefined): boolean {
  return Boolean(text) && IDENTIFIER_TEST.test(text as string)
}

/**
 * Redacts an AI answer's prose while leaving its proposal block untouched.
 *
 * <p>Applied to the accumulated stream (for the live bubble) and to the final answer (for the turn kept in
 * the transcript) — both times on the <b>whole</b> text received so far, never on a single delta.</p>
 */
export function redactAnswerProse(answer: string | null | undefined): string {
  if (!answer) {
    return ''
  }

  return redactIdentifiers(splitProposal(answer).visibleText)
}
