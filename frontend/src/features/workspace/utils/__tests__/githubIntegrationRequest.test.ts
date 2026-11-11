import { describe, expect, it } from 'vitest'
import {
  EMPTY_GUID,
  buildGitHubIntegrationRequest,
  requiresDefaultBoardForAutoCreate,
} from '../githubIntegrationRequest'

describe('githubIntegrationRequest', () => {
  it('sends EMPTY_GUID when an existing config clears the default board', () => {
    const request = buildGitHubIntegrationRequest(
      {
        isEnabled: true,
        defaultBoardId: undefined,
        issueLabelFilter: '',
        autoMoveOnPr: true,
        autoCreateTasksFromIssues: false,
        syncIssueComments: true,
      },
      true
    )

    expect(request.defaultBoardId).toBe(EMPTY_GUID)
  })

  it('omits the default board for a brand-new config and sends "" to clear the label filter', () => {
    const request = buildGitHubIntegrationRequest(
      {
        isEnabled: true,
        defaultBoardId: undefined,
        issueLabelFilter: '   ',
        autoMoveOnPr: false,
        autoCreateTasksFromIssues: false,
        syncIssueComments: false,
      },
      false
    )

    expect(request.defaultBoardId).toBeUndefined()
    expect(request.issueLabelFilter).toBe('')
    expect(request.autoMoveOnPr).toBe(false)
    expect(request.autoCreateTasksFromIssues).toBe(false)
    expect(request.syncIssueComments).toBe(false)
  })

  it('keeps a selected board and trims the label filter', () => {
    const request = buildGitHubIntegrationRequest(
      {
        isEnabled: true,
        defaultBoardId: 'b-1',
        issueLabelFilter: '  backend, urgent  ',
        autoMoveOnPr: true,
        autoCreateTasksFromIssues: true,
        syncIssueComments: true,
      },
      true
    )

    expect(request.defaultBoardId).toBe('b-1')
    expect(request.issueLabelFilter).toBe('backend, urgent')
  })

  it('requires a default board only when auto-create from issues is enabled', () => {
    expect(
      requiresDefaultBoardForAutoCreate({ autoCreateTasksFromIssues: true, defaultBoardId: undefined })
    ).toBe(true)
    expect(
      requiresDefaultBoardForAutoCreate({ autoCreateTasksFromIssues: true, defaultBoardId: 'b-1' })
    ).toBe(false)
    expect(
      requiresDefaultBoardForAutoCreate({
        autoCreateTasksFromIssues: false,
        defaultBoardId: undefined,
      })
    ).toBe(false)
  })
})
