import type { RoutingAdvice } from '../types/jev.types'

export function isDirectAssign(routingAdvice: RoutingAdvice | string): boolean {
  return routingAdvice === 'DirectAssign'
}

export function isOfferChoices(routingAdvice: RoutingAdvice | string): boolean {
  return routingAdvice === 'OfferChoices'
}

export function isManualRequired(routingAdvice: RoutingAdvice | string): boolean {
  return routingAdvice === 'ManualRequired'
}

export function formatConfidencePercent(confidenceScore: number): string {
  if (typeof confidenceScore !== 'number' || isNaN(confidenceScore)) {
    return '0%'
  }
  const pct = Math.round(Math.max(0, Math.min(1, confidenceScore)) * 100)
  return `${pct}%`
}

/**
 * Ant Design `Tag` colour for a routing advice.
 *
 * ⚠️ **ISSUE-02:** this replaced `getAdviceBadgeClass`, which returned Tailwind utility strings
 * (`bg-emerald-500/10 text-emerald-400 border-emerald-500/20`). TeamNexus has **no Tailwind** — no
 * `tailwind.config.*`, no PostCSS pipeline, no dependency — so those classes resolved to nothing and every
 * element rendered with browser defaults: text ran together and buttons lost their shape. Ant Design's own
 * `color` prop is the supported way to colour a `Tag` here.
 */
export function getAdviceTagColor(routingAdvice: RoutingAdvice | string): string {
  switch (routingAdvice) {
    case 'DirectAssign':
      return 'success'
    case 'OfferChoices':
      return 'warning'
    case 'ManualRequired':
    default:
      return 'default'
  }
}

/**
 * Hex colour for the leading icon of each branch.
 *
 * A separate helper from {@link getAdviceTagColor} because a `Tag` colour name and an icon colour are
 * different scales — Ant Design's `success` is not a hex value an icon can use.
 */
export function getAdviceIconColor(routingAdvice: RoutingAdvice | string): string {
  switch (routingAdvice) {
    case 'DirectAssign':
      return '#52c41a'
    case 'OfferChoices':
      return '#faad14'
    case 'ManualRequired':
    default:
      return '#8c8c8c'
  }
}

/**
 * Short Vietnamese label for each branch, matching the wording the roadmap uses for the three confidence
 * bands so the UI and the documentation cannot drift apart.
 */
export function getAdviceLabel(routingAdvice: RoutingAdvice | string): string {
  switch (routingAdvice) {
    case 'DirectAssign':
      return 'AI Tự tin cao'
    case 'OfferChoices':
      return 'Gợi ý lựa chọn'
    case 'ManualRequired':
    default:
      return 'Cần chọn thủ công'
  }
}
