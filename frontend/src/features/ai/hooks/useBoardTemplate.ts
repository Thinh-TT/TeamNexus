import { useCallback, useState } from 'react'
import type { AiActionLog } from '../types/aiAction.types'
import type { BoardTemplateProposal } from '../types/boardTemplate.types'
import { boardTemplateApi } from '../services/boardTemplateApi'

export type BoardTemplateStatus =
  | 'idle'
  | 'generating'
  | 'preview'
  | 'confirming'
  | 'created'
  | 'error'

export interface UseBoardTemplateReturn {
  status: BoardTemplateStatus
  proposal: BoardTemplateProposal | null
  error: string | null
  httpStatus: number | null
  generate: (description: string) => Promise<void>
  updateProposal: (patch: Partial<BoardTemplateProposal>) => void
  confirm: () => Promise<AiActionLog | null>
  reset: () => void
  clearError: () => void
}

export function useBoardTemplate(workspaceId: string): UseBoardTemplateReturn {
  const [status, setStatus] = useState<BoardTemplateStatus>('idle')
  const [proposal, setProposal] = useState<BoardTemplateProposal | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)

  const clearError = useCallback(() => {
    setError(null)
    setHttpStatus(null)
    if (status === 'error') {
      setStatus(proposal ? 'preview' : 'idle')
    }
  }, [proposal, status])

  const reset = useCallback(() => {
    setStatus('idle')
    setProposal(null)
    setError(null)
    setHttpStatus(null)
  }, [])

  const generate = useCallback(
    async (description: string) => {
      if (!description || !description.trim()) return

      setStatus('generating')
      setError(null)
      setHttpStatus(null)

      try {
        const data = await boardTemplateApi.generateTemplate(workspaceId, description.trim())
        setProposal(data)
        setStatus('preview')
      } catch (err: any) {
        const msg =
          err?.response?.data?.error ||
          err?.message ||
          'Không thể khởi tạo mẫu bảng từ AI'
        setError(msg)
        setHttpStatus(err?.response?.status ?? null)
        setStatus('error')
      }
    },
    [workspaceId]
  )

  const updateProposal = useCallback((patch: Partial<BoardTemplateProposal>) => {
    setProposal((prev) => (prev ? { ...prev, ...patch } : null))
  }, [])

  const confirm = useCallback(async (): Promise<AiActionLog | null> => {
    if (!proposal) return null

    setStatus('confirming')
    setError(null)
    setHttpStatus(null)

    try {
      const result = await boardTemplateApi.confirmTemplate(workspaceId, proposal)
      setStatus('created')
      return result
    } catch (err: any) {
      const msg =
        err?.response?.data?.error ||
        err?.message ||
        'Không thể xác nhận tạo bảng từ mẫu AI'
      setError(msg)
      setHttpStatus(err?.response?.status ?? null)
      setStatus('error')
      return null
    }
  }, [proposal, workspaceId])

  return {
    status,
    proposal,
    error,
    httpStatus,
    generate,
    updateProposal,
    confirm,
    reset,
    clearError,
  }
}
