import { describe, expect, it } from 'vitest'
import { resolveHubUrl } from '../hubUrl'

describe('resolveHubUrl', () => {
  it('returns default /hubs/board when apiBaseUrl is undefined', () => {
    expect(resolveHubUrl()).toBe('/hubs/board')
    expect(resolveHubUrl(undefined)).toBe('/hubs/board')
  })

  it('returns /hubs/board when apiBaseUrl is an empty or whitespace string', () => {
    expect(resolveHubUrl('')).toBe('/hubs/board')
    expect(resolveHubUrl('   ')).toBe('/hubs/board')
  })

  it('returns relative /hubs/board when apiBaseUrl is a relative path (dev proxy)', () => {
    expect(resolveHubUrl('/api')).toBe('/hubs/board')
    expect(resolveHubUrl('/api/v1')).toBe('/hubs/board')
  })

  it('strips API subpath and returns absolute hub URL when apiBaseUrl is an absolute URL', () => {
    expect(resolveHubUrl('https://api.example.com/api')).toBe('https://api.example.com/hubs/board')
    expect(resolveHubUrl('https://api.example.com/api/v1')).toBe('https://api.example.com/hubs/board')
  })

  it('avoids double slashes when apiBaseUrl has a trailing slash', () => {
    expect(resolveHubUrl('https://api.example.com/api/')).toBe('https://api.example.com/hubs/board')
  })

  it('handles absolute base URL without any path', () => {
    expect(resolveHubUrl('https://api.example.com')).toBe('https://api.example.com/hubs/board')
  })

  it('handles invalid or garbage URL deterministically without throwing an exception', () => {
    expect(resolveHubUrl(':::')).toBe('/hubs/board')
    expect(resolveHubUrl('not-a-valid-url')).toBe('/hubs/board')
  })

  it('supports custom hubPath with or without leading slash', () => {
    expect(resolveHubUrl('https://api.example.com/api', '/hubs/custom')).toBe(
      'https://api.example.com/hubs/custom'
    )
    expect(resolveHubUrl('https://api.example.com/api', 'hubs/custom')).toBe(
      'https://api.example.com/hubs/custom'
    )
    expect(resolveHubUrl('/api', 'hubs/custom')).toBe('/hubs/custom')
  })
})
