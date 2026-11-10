import { describe, expect, it } from 'vitest'
import { parseGitHubUrl } from '../githubUrlParser'

describe('githubUrlParser', () => {
  describe('Pull Requests', () => {
    it('parses standard https PR URL', () => {
      const result = parseGitHubUrl('https://github.com/facebook/react/pull/1234')
      expect(result).toEqual({
        type: 'pr',
        owner: 'facebook',
        repo: 'react',
        number: 1234,
      })
    })

    it('parses PR URL with hyphenated owner and repo name', () => {
      const result = parseGitHubUrl('https://github.com/Thinh-TT/TeamNexus/pull/142')
      expect(result).toEqual({
        type: 'pr',
        owner: 'Thinh-TT',
        repo: 'TeamNexus',
        number: 142,
      })
    })

    it('parses PR URL with trailing slash, query params, and anchors', () => {
      const result = parseGitHubUrl(
        'https://github.com/owner/repo/pull/42/?diff=split#issuecomment-99'
      )
      expect(result).toEqual({
        type: 'pr',
        owner: 'owner',
        repo: 'repo',
        number: 42,
      })
    })

    it('parses PR URL with subpath like /files', () => {
      const result = parseGitHubUrl('https://github.com/owner/repo/pull/77/files')
      expect(result).toEqual({
        type: 'pr',
        owner: 'owner',
        repo: 'repo',
        number: 77,
      })
    })

    it('parses http and www prefix', () => {
      const result = parseGitHubUrl('http://www.github.com/owner/repo/pull/10')
      expect(result).toEqual({
        type: 'pr',
        owner: 'owner',
        repo: 'repo',
        number: 10,
      })
    })

    it('trims leading and trailing whitespace', () => {
      const result = parseGitHubUrl('   https://github.com/owner/repo/pull/555   ')
      expect(result).toEqual({
        type: 'pr',
        owner: 'owner',
        repo: 'repo',
        number: 555,
      })
    })
  })

  describe('Issues', () => {
    it('parses standard https Issue URL', () => {
      const result = parseGitHubUrl('https://github.com/facebook/react/issues/5678')
      expect(result).toEqual({
        type: 'issue',
        owner: 'facebook',
        repo: 'react',
        number: 5678,
      })
    })

    it('parses Issue URL with trailing slash and query params', () => {
      const result = parseGitHubUrl('https://github.com/owner/repo/issues/89/?tab=all')
      expect(result).toEqual({
        type: 'issue',
        owner: 'owner',
        repo: 'repo',
        number: 89,
      })
    })
  })

  describe('Invalid URLs', () => {
    it('returns invalid for empty, null, or undefined input', () => {
      expect(parseGitHubUrl('')).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl(null)).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl(undefined)).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl('   ')).toEqual({ type: 'invalid' })
    })

    it('returns invalid for non-GitHub URLs', () => {
      expect(parseGitHubUrl('https://gitlab.com/owner/repo/pull/123')).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl('https://google.com')).toEqual({ type: 'invalid' })
    })

    it('returns invalid for repository root or other paths', () => {
      expect(parseGitHubUrl('https://github.com/owner/repo')).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl('https://github.com/owner/repo/actions/runs/123')).toEqual({
        type: 'invalid',
      })
      expect(parseGitHubUrl('https://github.com/owner/repo/commit/abcdef')).toEqual({
        type: 'invalid',
      })
    })

    it('returns invalid for non-numeric PR/Issue number', () => {
      expect(parseGitHubUrl('https://github.com/owner/repo/pull/abc')).toEqual({ type: 'invalid' })
      expect(parseGitHubUrl('https://github.com/owner/repo/pull/0')).toEqual({ type: 'invalid' })
    })
  })
})
