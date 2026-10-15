export type RoutingAdvice = 'DirectAssign' | 'OfferChoices' | 'ManualRequired'

export interface TriageOption {
  field: 'domain' | 'workType' | 'priority' | string
  value: string
  probability: number
}

export interface TaskTriageRequest {
  title: string
  description?: string | null
}

export interface TaskTriageResponse {
  suggestedDomain: string | null
  suggestedWorkType: string | null
  suggestedPriority: string | null
  needsSecurityReview: boolean
  confidenceScore: number
  routingAdvice: RoutingAdvice | string
  options: TriageOption[]
  degraded: boolean
  model: string
  latencyMs: number
}

export interface CandidateSuggestion {
  userId: string
  displayName: string
  probability: number
  reason: string | null
}

export interface AssigneeSuggestionResponse {
  recommendedUserId: string | null
  confidenceScore: number
  routingAdvice: RoutingAdvice | string
  candidates: CandidateSuggestion[]
  degraded: boolean
  model: string
  latencyMs: number
}

export type TaxonomyKind = 'domain' | 'work_type'

export interface TaxonomyEntryResponse {
  id: string
  kind: string
  key: string
  label: string
  colorHex: string | null
  position: number
}

export interface WorkspaceTaxonomyResponse {
  workspaceId: string
  domains: TaxonomyEntryResponse[]
  workTypes: TaxonomyEntryResponse[]
  seeded: boolean
}

export interface TaxonomyEntryRequest {
  key: string
  label: string
  colorHex?: string | null
}

export interface UpdateTaxonomyRequest {
  domains?: TaxonomyEntryRequest[]
  workTypes?: TaxonomyEntryRequest[]
}

export interface UpdateMemberProfileRequest {
  skills: string[]
  primaryRole: string
  capacityWeight: number
  maxWipLimit: number
}

export interface RecoveryPlanSnapshot {
  kind: 'ReassignTask' | 'ExtendDeadline' | 'SplitSubtasks' | 'EscalateManager'
  targetTaskId?: string | null
  targetUserId?: string | null
  newDueDate?: string | null
  subtasks?: string[]
  warnings?: string[]
  [key: string]: unknown
}
