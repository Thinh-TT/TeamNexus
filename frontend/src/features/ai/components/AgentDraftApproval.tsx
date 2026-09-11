import React, { useCallback, useEffect, useState } from 'react'
import { Alert, Card, Flex, Spin, message } from 'antd'
import { aiActionApi } from '../services/aiActionApi'
import type { AiActionLogDetail } from '../types/aiAction.types'
import { AiActionLogItem } from './AiActionLogItem'

interface AgentDraftApprovalProps {
  aiActionLogId: string
  onActionComplete?: () => void
}

export const AgentDraftApproval: React.FC<AgentDraftApprovalProps> = ({
  aiActionLogId,
  onActionComplete,
}) => {
  const [logDetail, setLogDetail] = useState<AiActionLogDetail | null>(null)
  const [loading, setLoading] = useState<boolean>(false)
  const [isProcessing, setIsProcessing] = useState<boolean>(false)
  const [error, setError] = useState<string | null>(null)

  const fetchLog = useCallback(async () => {
    if (!aiActionLogId) return
    setLoading(true)
    setError(null)
    try {
      const data = await aiActionApi.getAiAction(aiActionLogId)
      setLogDetail(data)
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        (err instanceof Error ? err.message : 'Không thể tải thông tin phê duyệt AI')
      setError(errorMsg)
    } finally {
      setLoading(false)
    }
  }, [aiActionLogId])

  useEffect(() => {
    Promise.resolve().then(() => {
      fetchLog()
    })
  }, [fetchLog])

  const handleApprove = async (logId: string) => {
    setIsProcessing(true)
    try {
      const updated = await aiActionApi.approveAiAction(logId)
      message.success('Đã phê duyệt kết quả của AI Agent')
      setLogDetail(updated)
      onActionComplete?.()
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        (err instanceof Error ? err.message : 'Phê duyệt thất bại')
      message.error(errorMsg)
    } finally {
      setIsProcessing(false)
    }
  }

  const handleReject = async (logId: string, note?: string | null) => {
    setIsProcessing(true)
    try {
      const updated = await aiActionApi.rejectAiAction(logId, note)
      message.info('Đã từ chối kết quả của AI Agent')
      setLogDetail(updated)
      onActionComplete?.()
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        (err instanceof Error ? err.message : 'Từ chối thất bại')
      message.error(errorMsg)
    } finally {
      setIsProcessing(false)
    }
  }

  const handleUndo = async (logId: string) => {
    setIsProcessing(true)
    try {
      const updated = await aiActionApi.undoAiAction(logId)
      message.info('Đã hoàn tác kết quả đã duyệt')
      setLogDetail(updated)
      onActionComplete?.()
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ||
        (err instanceof Error ? err.message : 'Hoàn tác thất bại')
      message.error(errorMsg)
    } finally {
      setIsProcessing(false)
    }
  }

  const handleFetchDetail = async (logId: string) => {
    try {
      const data = await aiActionApi.getAiAction(logId)
      setLogDetail(data)
      return data
    } catch {
      return null
    }
  }

  if (loading && !logDetail) {
    return (
      <Flex justify="center" align="center" style={{ padding: '24px 0' }}>
        <Spin tip="Đang tải bản thảo kết quả..." />
      </Flex>
    )
  }

  if (error) {
    return (
      <Alert
        type="warning"
        showIcon
        message="Không thể hiển thị bản thảo phê duyệt"
        description={error}
        style={{ borderRadius: 8 }}
      />
    )
  }

  if (!logDetail) {
    return null
  }

  return (
    <div data-testid="agent-draft-approval">
      <Card
        size="small"
        bordered={false}
        style={{ background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 8 }}
        styles={{ body: { padding: 12 } }}
      >
        <AiActionLogItem
          log={logDetail}
          onApprove={handleApprove}
          onReject={handleReject}
          onUndo={handleUndo}
          onFetchDetail={handleFetchDetail}
          isProcessing={isProcessing}
        />
      </Card>
    </div>
  )
}
