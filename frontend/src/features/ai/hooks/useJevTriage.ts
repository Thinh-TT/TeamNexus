import { useCallback, useEffect, useRef, useState } from 'react'
import { jevApi } from '../services/jevApi'
import type { TaskTriageResponse } from '../types/jev.types'

interface UseJevTriageProps {
  boardId: string
  title: string
  description?: string | null
  enabled?: boolean
}

export function useJevTriage({
  boardId,
  title,
  description,
  enabled = true,
}: UseJevTriageProps) {
  const [triageData, setTriageData] = useState<TaskTriageResponse | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [isDegraded, setIsDegraded] = useState(false)
  const [isJevDisabled, setIsJevDisabled] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const abortControllerRef = useRef<AbortController | null>(null)
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const hasTriageRef = useRef(false)

  const reset = useCallback(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort()
      abortControllerRef.current = null
    }
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current)
      debounceTimerRef.current = null
    }
    hasTriageRef.current = false
    setTriageData(null)
    setIsLoading(false)
    setIsDegraded(false)
    setError(null)
  }, [])

  const executeTriage = useCallback(
    async (taskTitle: string, taskDesc?: string | null) => {
      const trimmedTitle = taskTitle.trim()
      if (trimmedTitle.length < 3 || !enabled || isJevDisabled) {
        setTriageData(null)
        setIsLoading(false)
        return
      }

      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
      }
      const controller = new AbortController()
      abortControllerRef.current = controller

      setIsLoading(true)
      setError(null)

      try {
        const res = await jevApi.triageTask(
          boardId,
          { title: trimmedTitle, description: taskDesc?.trim() || null },
          controller.signal
        )

        hasTriageRef.current = true
        setTriageData(res)
        setIsDegraded(res.degraded)
      } catch (err: unknown) {
        if (err instanceof Error && err.name === 'CanceledError') {
          return
        }

        const axiosErr = err as { response?: { status?: number; data?: { message?: string } } }
        if (axiosErr.response?.status === 503) {
          setIsJevDisabled(true)
          setTriageData(null)
        } else {
          setIsDegraded(true)
          setError(axiosErr.response?.data?.message || 'Không thể lấy gợi ý AI.')
        }
      } finally {
        setIsLoading(false)
      }
    },
    [boardId, enabled, isJevDisabled]
  )

  useEffect(() => {
    if (!enabled || isJevDisabled) return

    const trimmed = title.trim()
    if (trimmed.length < 3) {
      if (hasTriageRef.current) {
        reset()
      }
      return
    }

    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current)
    }

    debounceTimerRef.current = setTimeout(() => {
      void executeTriage(title, description)
    }, 400)

    return () => {
      if (debounceTimerRef.current) {
        clearTimeout(debounceTimerRef.current)
      }
    }
  }, [title, description, enabled, isJevDisabled, executeTriage, reset])

  return {
    triageData,
    isLoading,
    isDegraded,
    isJevDisabled,
    error,
    reset,
    triggerTriage: executeTriage,
  }
}
