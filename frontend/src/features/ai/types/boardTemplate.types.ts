export interface BoardTemplateColumnProposal {
  name: string
  isDone: boolean
}

export interface BoardTemplateTaskLabel {
  labelId: string | null
  name: string
  exists: boolean
}

export interface BoardTemplateTaskAssignee {
  userId: string | null
  displayName: string | null
  matched: boolean
}

export interface BoardTemplateTaskProposal {
  title: string
  description?: string | null
  priority?: 'Low' | 'Medium' | 'High' | 'Urgent' | null
  columnName: string
  labels?: BoardTemplateTaskLabel[]
  assignee?: BoardTemplateTaskAssignee | null
}

export interface BoardTemplateProposal {
  summary?: string | null
  boardName: string
  boardDescription?: string | null
  columns: BoardTemplateColumnProposal[]
  tasks: BoardTemplateTaskProposal[]
}

export interface BoardTemplateRequest {
  description: string
}

export interface ConfirmBoardTemplateRequest {
  proposal: BoardTemplateProposal
}
