export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export interface SmartSetupRequest {
  description: string
}

export interface SmartSetupLabelSuggestion {
  labelId: string | null
  name: string
  exists: boolean
}

export interface SmartSetupAssigneeSuggestion {
  userId: string | null
  displayName: string | null
  matched: boolean
}

export interface SmartSetupTaskProposal {
  title: string
  description: string | null
  priority: TaskPriority | null
  labels: SmartSetupLabelSuggestion[]
  assignee: SmartSetupAssigneeSuggestion | null
}

export interface SmartSetupProposal {
  summary: string | null
  tasks: SmartSetupTaskProposal[]
}

export interface EditableTaskProposal extends SmartSetupTaskProposal {
  tempId: string
}

export interface WorkspaceMember {
  userId: string
  displayName: string
  role: string
  avatarUrl?: string | null
}
