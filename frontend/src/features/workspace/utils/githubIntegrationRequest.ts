import type { UpsertWorkspaceGitHubIntegrationRequest } from '../types/integration.types'

/** The backend treats a zero GUID as "clear DefaultBoardId" on an update. */
export const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

export interface GitHubIntegrationFormValues {
  isEnabled?: boolean
  defaultBoardId?: string
  issueLabelFilter?: string
  autoMoveOnPr?: boolean
  autoCreateTasksFromIssues?: boolean
  syncIssueComments?: boolean
}

/**
 * Builds the PUT payload for the GitHub integration form.
 *
 * The backend distinguishes "absent" from "clear" (Phase 15.3 §T15.3.2), so the two normalization
 * rules that used to live inline — and were only pinned by a misleading report sentence — are
 * extracted here where they can be unit-tested:
 *
 * - On an **existing** config, clearing the board must send `EMPTY_GUID` (a missing field would mean
 *   "leave unchanged"), and clearing the label filter must send `""` (not `null`).
 * - On a **new** config without a board, the field is simply omitted.
 */
export function buildGitHubIntegrationRequest(
  values: GitHubIntegrationFormValues,
  hasExistingConfig: boolean
): UpsertWorkspaceGitHubIntegrationRequest {
  const filterText =
    typeof values.issueLabelFilter === 'string' ? values.issueLabelFilter.trim() : ''

  return {
    isEnabled: Boolean(values.isEnabled),
    defaultBoardId: values.defaultBoardId
      ? values.defaultBoardId
      : hasExistingConfig
        ? EMPTY_GUID
        : undefined,
    issueLabelFilter: filterText,
    autoMoveOnPr: Boolean(values.autoMoveOnPr),
    autoCreateTasksFromIssues: Boolean(values.autoCreateTasksFromIssues),
    syncIssueComments: Boolean(values.syncIssueComments),
  }
}

/**
 * A GitHub integration may only auto-create tasks when it knows which board receives them.
 * Enabling the feature without a board is accepted by the API but silently drops every issue
 * event, so the UI blocks the save instead (review 15.5 F3).
 */
export function requiresDefaultBoardForAutoCreate(values: GitHubIntegrationFormValues): boolean {
  return Boolean(values.autoCreateTasksFromIssues) && !values.defaultBoardId
}
