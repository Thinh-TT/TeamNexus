import type { TaskExternalLinkSummary } from '../types/board.types'

export const GITHUB_BOT_USER_ID = '00000000-0000-0000-0000-00000000b071'

export interface ExternalLinkStateMeta {
  label: string
  color: string
  bg: string
  border: string
}

/**
 * Formats a sequential workspace task number as `TN-123` (or `null` when absent/0).
 */
export function formatTaskCode(taskNumber?: number | null): string | null {
  if (typeof taskNumber === 'number' && taskNumber > 0) {
    return `TN-${taskNumber}`
  }
  return null
}

/**
 * Extracts compact `#123` reference from a GitHub external id (`owner/repo#123` -> `#123`).
 */
export function getShortExternalNumber(externalId: string): string {
  if (!externalId) return ''
  const hashIndex = externalId.lastIndexOf('#')
  if (hashIndex !== -1 && hashIndex < externalId.length - 1) {
    return `#${externalId.slice(hashIndex + 1)}`
  }
  return externalId.startsWith('#') ? externalId : `#${externalId}`
}

/**
 * Semantic Luminous Enterprise styling for GitHub PR/Issue link states.
 */
export function getExternalLinkStateMeta(state: string): ExternalLinkStateMeta {
  switch (state.toLowerCase()) {
    case 'open':
      return {
        label: 'Open',
        color: '#059669',
        bg: '#ecfdf5',
        border: '#a7f3d0',
      }
    case 'merged':
      return {
        label: 'Merged',
        color: '#7c3aed',
        bg: '#f5f3ff',
        border: '#ddd6fe',
      }
    case 'closed':
      return {
        label: 'Closed',
        color: '#dc2626',
        bg: '#fef2f2',
        border: '#fecaca',
      }
    case 'draft':
    default:
      return {
        label: state.toLowerCase() === 'draft' ? 'Draft' : state,
        color: '#475569',
        bg: '#f1f5f9',
        border: '#cbd5e1',
      }
  }
}

/**
 * Builds the compact Kanban card chip text such as `PR #88 · Merged` or `Issue #12 · Open`.
 */
export function formatExternalLinkChipText(link: TaskExternalLinkSummary): string {
  const prefix = link.linkType === 'pull_request' ? 'PR' : 'Issue'
  const shortNum = getShortExternalNumber(link.externalId)
  const meta = getExternalLinkStateMeta(link.state)
  return `${prefix} ${shortNum} · ${meta.label}`
}
