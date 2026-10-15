import { describe, expect, it } from 'vitest'
import {
  formatConfidencePercent,
  getAdviceIconColor,
  getAdviceLabel,
  getAdviceTagColor,
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

  /**
   * ISSUE-02: this suite previously asserted Tailwind class names (`toContain('emerald')`) for a helper that
   * fed components styled with classes **no CSS was ever generated for**. Asserting the broken output made the
   * defect look verified. The contract that matters now is Ant Design's own `Tag` colour names.
   */
  it('maps routing advice to Ant Design Tag colours', () => {
    expect(getAdviceTagColor('DirectAssign')).toBe('success')
    expect(getAdviceTagColor('OfferChoices')).toBe('warning')
    expect(getAdviceTagColor('ManualRequired')).toBe('default')
    expect(getAdviceTagColor('Unknown')).toBe('default')
    expect(getAdviceTagColor(undefined as unknown as string)).toBe('default')
  })

  it('maps routing advice to hex icon colours, which are a different scale from Tag colours', () => {
    // A `Tag` colour name is not usable as an icon colour, which is why these are two helpers rather than one.
    for (const advice of ['DirectAssign', 'OfferChoices', 'ManualRequired', 'Unknown']) {
      const colour = getAdviceIconColor(advice)
      expect(colour).toMatch(/^#[0-9a-f]{6}$/i)
      expect(colour).not.toBe(getAdviceTagColor(advice))
    }
  })

  it('provides a Vietnamese label for every branch including the fallback', () => {
    expect(getAdviceLabel('DirectAssign')).toBe('AI Tự tin cao')
    expect(getAdviceLabel('OfferChoices')).toBe('Gợi ý lựa chọn')
    expect(getAdviceLabel('ManualRequired')).toBe('Cần chọn thủ công')
    expect(getAdviceLabel('Unknown')).toBe('Cần chọn thủ công')
  })

  it('never returns a Tailwind utility class from the Ant Design helpers', () => {
    // The guard against reintroducing ISSUE-02: these helpers exist to feed Ant Design props, so a value
    // containing Tailwind syntax means someone wired the wrong helper back in.
    const tailwindish = /(^|\s)(bg-|text-|border-|space-|grid-|flex-|p-\d|m-\d)/

    for (const advice of ['DirectAssign', 'OfferChoices', 'ManualRequired', 'Unknown']) {
      expect(getAdviceTagColor(advice)).not.toMatch(tailwindish)
      expect(getAdviceIconColor(advice)).not.toMatch(tailwindish)
      expect(getAdviceLabel(advice)).not.toMatch(tailwindish)
    }
  })
})
