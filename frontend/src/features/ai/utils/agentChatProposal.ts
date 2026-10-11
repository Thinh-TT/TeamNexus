/**
 * Client-side mirror of the server's proposal parser (Phase 14.5 §B).
 *
 * <p><b>Why mirror it at all.</b> The server stores the transcript WITHOUT the JSON block and it is the
 * server that validates a proposal, so this file never decides what is valid — it only decides whether to
 * <i>offer</i> an action card. Splitting the answer is still needed here because the raw block must not be
 * shown to the user as prose, and the click must send the exact JSON the model produced.</p>
 *
 * <p><b>Symmetric with `AgentChatProposalParser` (C#), on purpose.</b> Both take the LAST fenced `json`
 * block and both unwrap the `{ "proposal": … }` envelope; the two test suites assert the same cases so the
 * pair cannot drift into "the UI shows a card the server would refuse".</p>
 */

export interface SplitProposalResult {
  /** The answer with the proposal block removed — what the user reads. */
  visibleText: string
  /** The inner JSON of the last fenced `json` block, or null when there is none. */
  jsonBlock: string | null
}

const FENCE = '```json'

/** Splits an answer into its readable text and its optional proposal block. Never throws. */
export function splitProposal(answer: string | null | undefined): SplitProposalResult {
  if (!answer || answer.trim() === '') {
    return { visibleText: '', jsonBlock: null }
  }

  // LAST, not first: the prompt asks for the explanation first and the real proposal last, and a model
  // that shows an example block while explaining would otherwise have the example submitted.
  const openIndex = answer.toLowerCase().lastIndexOf(FENCE)

  if (openIndex < 0) {
    return { visibleText: answer.trim(), jsonBlock: null }
  }

  const bodyStart = answer.indexOf('\n', openIndex)
  if (bodyStart < 0) {
    return { visibleText: answer.trim(), jsonBlock: null }
  }

  const closeIndex = answer.indexOf('```', bodyStart)
  if (closeIndex < 0) {
    // Unterminated fence: the model was cut off. Nothing to submit, and the raw text is the honest thing
    // to show.
    return { visibleText: answer.trim(), jsonBlock: null }
  }

  const json = answer.slice(bodyStart + 1, closeIndex).trim()
  const visibleText = (answer.slice(0, openIndex) + answer.slice(closeIndex + 3)).trim()

  return { visibleText, jsonBlock: json === '' ? null : unwrapEnvelope(json) }
}

/**
 * Extracts the `proposal` object from the documented envelope, accepting a bare object too.
 *
 * <p>Applied on both sides of the round trip — to the model's answer and to the block the client kept —
 * so the object the card previews is exactly the object the server re-validates.</p>
 */
export function unwrapEnvelope(json: string): string {
  try {
    const parsed: unknown = JSON.parse(json)

    if (
      parsed !== null &&
      typeof parsed === 'object' &&
      'proposal' in parsed &&
      typeof (parsed as { proposal: unknown }).proposal === 'object' &&
      (parsed as { proposal: unknown }).proposal !== null
    ) {
      return JSON.stringify((parsed as { proposal: unknown }).proposal)
    }

    return json
  } catch {
    // Hand the raw text back: the server owns the error message for malformed JSON.
    return json
  }
}

/**
 * Parses a proposal block for display. Returns null when the JSON is not a proposal the UI can describe —
 * the caller then shows plain text instead of a card the server would reject.
 */
export function parseProposal(jsonBlock: string | null): {
  kind: 'CreateSubtasks' | 'UpdateTaskFields' | 'MoveTaskToColumn'
  summary: string
  payload: {
    taskId?: string | null
    columnId?: string | null
    title?: string | null
    description?: string | null
    priority?: string | null
    dueDate?: string | null
  }
} | null {
  if (!jsonBlock) {
    return null
  }

  try {
    const parsed = JSON.parse(unwrapEnvelope(jsonBlock)) as {
      kind?: unknown
      summary?: unknown
      payload?: unknown
    }

    const kind = parsed?.kind

    if (
      kind !== 'CreateSubtasks' &&
      kind !== 'UpdateTaskFields' &&
      kind !== 'MoveTaskToColumn'
    ) {
      return null
    }

    const payload =
      parsed.payload && typeof parsed.payload === 'object'
        ? (parsed.payload as Record<string, unknown>)
        : {}

    return {
      kind,
      summary: typeof parsed.summary === 'string' ? parsed.summary : '',
      payload: {
        taskId: typeof payload.taskId === 'string' ? payload.taskId : null,
        columnId: typeof payload.columnId === 'string' ? payload.columnId : null,
        title: typeof payload.title === 'string' ? payload.title : null,
        description: typeof payload.description === 'string' ? payload.description : null,
        priority: typeof payload.priority === 'string' ? payload.priority : null,
        dueDate: typeof payload.dueDate === 'string' ? payload.dueDate : null,
      },
    }
  } catch {
    return null
  }
}
