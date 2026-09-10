import { useCallback, useState } from 'react'
import { smartSetupApi } from '../services/smartSetupApi'
import type {
  EditableTaskProposal,
  SmartSetupLabelSuggestion,
  SmartSetupProposal,
  SmartSetupTaskProposal,
  WorkspaceMember,
} from '../types/smartSetup.types'

export type SmartSetupStatus = 'idle' | 'generating' | 'ready' | 'error' | 'confirmed'

export interface UseSmartSetupResult {
  status: SmartSetupStatus
  description: string
  summary: string | null
  tasks: EditableTaskProposal[]
  members: WorkspaceMember[]
  error: string | null
  httpStatus: number | null
  isLoadingMembers: boolean
  setDescription: (desc: string) => void
  fetchMembers: (workspaceId: string) => Promise<void>
  generate: (boardId: string, desc?: string) => Promise<SmartSetupProposal | null>
  updateTask: (tempId: string, patch: Partial<SmartSetupTaskProposal>) => void
  deleteTask: (tempId: string) => void
  addTask: () => void
  addLabel: (tempId: string, label: SmartSetupLabelSuggestion) => void
  removeLabel: (tempId: string, labelIndex: number) => void
  confirm: () => void
  reset: () => void
  backToPrompt: () => void
}

const createTempId = () => {
  if (typeof crypto !== 'undefined' && crypto.randomUUID) {
    return crypto.randomUUID()
  }
  return `task-${Date.now()}-${Math.random().toString(36).substring(2, 9)}`
}

export const useSmartSetup = (): UseSmartSetupResult => {
  const [status, setStatus] = useState<SmartSetupStatus>('idle')
  const [description, setDescription] = useState('')
  const [summary, setSummary] = useState<string | null>(null)
  const [tasks, setTasks] = useState<EditableTaskProposal[]>([])
  const [members, setMembers] = useState<WorkspaceMember[]>([])
  const [error, setError] = useState<string | null>(null)
  const [httpStatus, setHttpStatus] = useState<number | null>(null)
  const [isLoadingMembers, setIsLoadingMembers] = useState(false)

  const fetchMembers = useCallback(async (workspaceId: string) => {
    if (!workspaceId) return
    setIsLoadingMembers(true)
    try {
      const data = await smartSetupApi.getWorkspaceMembers(workspaceId)
      setMembers(data)
    } catch {
      // Non-blocking: fallback to empty members list
      setMembers([])
    } finally {
      setIsLoadingMembers(false)
    }
  }, [])

  const generate = useCallback(
    async (boardId: string, customDesc?: string): Promise<SmartSetupProposal | null> => {
      const textToUse = customDesc !== undefined ? customDesc : description
      const trimmed = textToUse.trim()

      if (!trimmed) {
        setStatus('error')
        setError('Vui lòng nhập mô tả công việc hoặc dự án (1–4000 ký tự).')
        setHttpStatus(400)
        return null
      }

      setStatus('generating')
      setError(null)
      setHttpStatus(null)

      try {
        const result = await smartSetupApi.generateSmartSetup(boardId, {
          description: trimmed,
        })

        setSummary(result.summary)
        setTasks(
          (result.tasks || []).map((t) => ({
            ...t,
            tempId: createTempId(),
          }))
        )
        setStatus('ready')
        return result
      } catch (err: unknown) {
        setStatus('error')
        const errObj = err as {
          response?: { status?: number; data?: { error?: string } }
          message?: string
        }
        const statusCode = errObj?.response?.status ?? 500
        const serverError = errObj?.response?.data?.error
        setHttpStatus(statusCode)

        if (statusCode === 400) {
          setError(
            serverError ||
              'Mô tả không hợp lệ hoặc AI không trả về sub-task nào. Vui lòng nhập mô tả chi tiết hơn.'
          )
        } else if (statusCode === 403) {
          setError(
            serverError ||
              'Bạn cần quyền Manager hoặc Admin trong workspace này để sử dụng tính năng AI Smart Setup.'
          )
        } else if (statusCode === 404) {
          setError('Bảng làm việc không tồn tại hoặc đã bị xóa.')
        } else if (statusCode === 502) {
          setError(
            serverError ||
              'AI tạm thời không phản hồi hoặc phản hồi không đúng định dạng. Vui lòng thử lại.'
          )
        } else {
          setError(
            serverError ||
              (err instanceof Error ? err.message : 'Đã xảy ra lỗi trong quá trình phân tích AI. Vui lòng thử lại.')
          )
        }
        return null
      }
    },
    [description]
  )

  const updateTask = useCallback(
    (tempId: string, patch: Partial<SmartSetupTaskProposal>) => {
      setTasks((prev) =>
        prev.map((t) => (t.tempId === tempId ? { ...t, ...patch } : t))
      )
    },
    []
  )

  const deleteTask = useCallback((tempId: string) => {
    setTasks((prev) => prev.filter((t) => t.tempId !== tempId))
  }, [])

  const addTask = useCallback(() => {
    const newTask: EditableTaskProposal = {
      tempId: createTempId(),
      title: 'Công việc mới',
      description: null,
      priority: 'Medium',
      labels: [],
      assignee: null,
    }
    setTasks((prev) => [...prev, newTask])
  }, [])

  const addLabel = useCallback(
    (tempId: string, label: SmartSetupLabelSuggestion) => {
      setTasks((prev) =>
        prev.map((t) => {
          if (t.tempId !== tempId) return t
          const exists = t.labels.some(
            (l) => l.name.toLowerCase() === label.name.toLowerCase()
          )
          if (exists) return t
          return {
            ...t,
            labels: [...t.labels, label],
          }
        })
      )
    },
    []
  )

  const removeLabel = useCallback((tempId: string, labelIndex: number) => {
    setTasks((prev) =>
      prev.map((t) => {
        if (t.tempId !== tempId) return t
        return {
          ...t,
          labels: t.labels.filter((_, idx) => idx !== labelIndex),
        }
      })
    )
  }, [])

  const confirm = useCallback(() => {
    setStatus('confirmed')
  }, [])

  const reset = useCallback(() => {
    setStatus('idle')
    setDescription('')
    setSummary(null)
    setTasks([])
    setError(null)
    setHttpStatus(null)
  }, [])

  const backToPrompt = useCallback(() => {
    setStatus('idle')
    setError(null)
    setHttpStatus(null)
  }, [])

  return {
    status,
    description,
    summary,
    tasks,
    members,
    error,
    httpStatus,
    isLoadingMembers,
    setDescription,
    fetchMembers,
    generate,
    updateTask,
    deleteTask,
    addTask,
    addLabel,
    removeLabel,
    confirm,
    reset,
    backToPrompt,
  }
}
