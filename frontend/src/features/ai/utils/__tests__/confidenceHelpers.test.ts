import { describe, expect, it } from 'vitest'
import {
  formatConfidencePercent,
  getAdviceBadgeClass,
  isDirectAssign,
  isManualRequired,
  isOfferChoices,
} from '../confidenceHelpers'

describe('confidenceHelpers', () => {
  it('correctly categorizes routing advice', () => {
    expect(isDirectAssign('DirectAssign')).toBe(true)
    expect(isDirectAssign('OfferChoices')).toBe(false)
    expect(isDirectAssign('ManualRequired')).toBe(false)
    expect(isDirectAssign(undefined)).toBe(false)

    expect(isOfferChoices('OfferChoices')).toBe(true)
    expect(isOfferChoices('DirectAssign')).toBe(false)

    expect(isManualRequired('ManualRequired')).toBe(true)
    expect(isManualRequired('DirectAssign')).toBe(false)
    expect(isManualRequired(undefined as unknown as string)).toBe(false)
  })

  it('formats confidence scores as percentages', () => {
    expect(formatConfidencePercent(0.854)).toBe('85%')
    expect(formatConfidencePercent(1)).toBe('100%')
    expect(formatConfidencePercent(0)).toBe('0%')
    expect(formatConfidencePercent(0.499)).toBe('50%')
    expect(formatConfidencePercent(undefined as unknown as number)).toBe('0%')
  })

  it('returns appropriate CSS badge classes for routing advice', () => {
    expect(getAdviceBadgeClass('DirectAssign')).toContain('emerald')
    expect(getAdviceBadgeClass('OfferChoices')).toContain('amber')
    expect(getAdviceBadgeClass('ManualRequired')).toContain('slate')
    expect(getAdviceBadgeClass('Unknown')).toContain('slate')
  })
})
