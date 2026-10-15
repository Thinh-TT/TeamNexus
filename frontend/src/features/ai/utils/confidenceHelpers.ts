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

export function getAdviceBadgeClass(routingAdvice: RoutingAdvice | string): string {
  switch (routingAdvice) {
    case 'DirectAssign':
      return 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20'
    case 'OfferChoices':
      return 'bg-amber-500/10 text-amber-400 border-amber-500/20'
    case 'ManualRequired':
    default:
      return 'bg-slate-500/10 text-slate-400 border-slate-500/20'
  }
}

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
