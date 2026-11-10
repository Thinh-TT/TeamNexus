export type GitHubUrlType = 'pr' | 'issue' | 'invalid'

export interface ParsedGitHubUrl {
  type: GitHubUrlType
  owner?: string
  repo?: string
  number?: number
}

/**
 * Parses a GitHub PR or Issue URL.
 * Supported formats:
 * - https://github.com/:owner/:repo/pull/:number
 * - https://github.com/:owner/:repo/issues/:number
 * With optional http/https, www, trailing slashes, fragments (#), query params (?).
 */
export function parseGitHubUrl(url?: string | null): ParsedGitHubUrl {
  if (!url || typeof url !== 'string') {
    return { type: 'invalid' }
  }

  const trimmed = url.trim()
  const regex = /^(?:https?:\/\/)?(?:www\.)?github\.com\/([^/\s]+)\/([^/\s]+)\/(pull|issues)\/(\d+)(?:[/?#].*)?$/i
  const match = trimmed.match(regex)

  if (!match) {
    return { type: 'invalid' }
  }

  const [, owner, repo, rawType, rawNumber] = match
  const parsedNumber = parseInt(rawNumber, 10)

  if (Number.isNaN(parsedNumber) || parsedNumber <= 0) {
    return { type: 'invalid' }
  }

  return {
    type: rawType.toLowerCase() === 'pull' ? 'pr' : 'issue',
    owner,
    repo,
    number: parsedNumber,
  }
}
