import React, { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Flex,
  List,
  Space,
  Tag,
  Typography,
} from 'antd'
import {
  ToolOutlined,
  WarningOutlined,
  UserOutlined,
  CalendarOutlined,
  BranchesOutlined,
  ExclamationCircleOutlined,
  CheckOutlined,
  CloseOutlined,
  UndoOutlined,
} from '@ant-design/icons'
import { aiActionApi } from '../services/aiActionApi'
import type { AiActionLog, AiActionLogDetail } from '../types/aiAction.types'
import type { RecoveryPlanSnapshot } from '../types/jev.types'

interface RecoveryPlanCardProps {
  actionLog: AiActionLog | AiActionLogDetail
  detail?: AiActionLogDetail | null
  onActionSuccess?: (updated: AiActionLogDetail) => void
}

/**
 * AI Observer Recovery Plan card component (Giai đoạn 14.7 §A5 Phần B).
 *
 * Pure Ant Design + Luminous Enterprise Workspace Accountability styling.
 */
export const RecoveryPlanCard: React.FC<RecoveryPlanCardProps> = ({
  actionLog,
  detail,
  onActionSuccess,
}) => {
  const [isActing, setIsActing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  let snapshot: RecoveryPlanSnapshot | null = null
  try {
    const raw =
      (detail as unknown as Record<string, unknown>)?.afterSnapshotJson ||
      (actionLog as unknown as Record<string, unknown>)?.basisJson ||
      detail?.afterSnapshot ||
      detail?.basis
    if (typeof raw === 'string') {
      snapshot = JSON.parse(raw) as RecoveryPlanSnapshot
    } else if (raw && typeof raw === 'object') {
      snapshot = raw as unknown as RecoveryPlanSnapshot
    }
  } catch {
    snapshot = null
  }

  const kind = snapshot?.kind || 'ReassignTask'
  const isEscalate = kind === 'EscalateManager'

  const handleApprove = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.approveAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể phê duyệt kế hoạch phục hồi.')
    } finally {
      setIsActing(false)
    }
  }

  const handleReject = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.rejectAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể từ chối kế hoạch phục hồi.')
    } finally {
      setIsActing(false)
    }
  }

  const handleUndo = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.undoAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể hoàn tác hành động.')
    } finally {
      setIsActing(false)
    }
  }

  const renderKindIcon = () => {
    switch (kind) {
      case 'ReassignTask':
        return <UserOutlined style={{ fontSize: 16, color: '#4f46e5' }} />
      case 'ExtendDeadline':
        return <CalendarOutlined style={{ fontSize: 16, color: '#10b981' }} />
      case 'SplitSubtasks':
        return <BranchesOutlined style={{ fontSize: 16, color: '#7c3aed' }} />
      case 'EscalateManager':
      default:
        return <ExclamationCircleOutlined style={{ fontSize: 16, color: '#f59e0b' }} />
    }
  }

  const getKindTitle = () => {
    switch (kind) {
      case 'ReassignTask':
        return 'Tái phân công người phụ trách thẻ'
      case 'ExtendDeadline':
        return 'Gia hạn thời hạn hoàn thành thẻ'
      case 'SplitSubtasks':
        return 'Tách nhỏ công việc thành các subtasks'
      case 'EscalateManager':
        return 'Báo cáo khẩn cấp cấp quản lý'
      default:
        return 'Kế hoạch phục hồi'
    }
  }

  return (
    <Card
      data-testid="recovery-plan-card"
      size="small"
      style={{
        borderRadius: 10,
        borderColor: '#ddd6fe',
        background: 'linear-gradient(180deg, #faf5ff 0%, #ffffff 100%)',
        boxShadow: '0 2px 8px rgba(124, 58, 237, 0.06)',
        marginBottom: 8,
      }}
      styles={{ body: { padding: 14 } }}
    >
      <Flex vertical gap={10}>
        {/* Header: Title, Kind Tag & Icon */}
        <Flex
          align="center"
          justify="space-between"
          gap={8}
          style={{ borderBottom: '1px solid #f1f5f9', paddingBottom: 8 }}
        >
          <Flex align="center" gap={8}>
            <div
              style={{
                padding: '5px 7px',
                borderRadius: 8,
                backgroundColor: '#f5f3ff',
                border: '1px solid #ddd6fe',
                color: '#7c3aed',
                display: 'flex',
                alignItems: 'center',
              }}
            >
              <ToolOutlined style={{ fontSize: 15 }} />
            </div>
            <Flex vertical gap={2}>
              <Typography.Text strong style={{ fontSize: 13, color: '#0f172a' }}>
                AI Observer: {getKindTitle()}
              </Typography.Text>
              <Space size={4}>
                <Tag
                  color="purple"
                  style={{
                    margin: 0,
                    fontSize: 10.5,
                    fontFamily: 'var(--tn-font-mono, monospace)',
                    borderRadius: 4,
                  }}
                >
                  {kind}
                </Tag>
              </Space>
            </Flex>
          </Flex>

          <div>{renderKindIcon()}</div>
        </Flex>

        {isEscalate && (
          <Alert
            data-testid="escalate-manager-warning"
            type="warning"
            showIcon
            icon={<WarningOutlined />}
            title="Cần họp khẩn cấp"
            description="Cần họp khẩn — không có thay đổi dữ liệu nào được áp dụng."
            style={{ borderRadius: 8 }}
          />
        )}

        {snapshot?.subtasks && snapshot.subtasks.length > 0 && (
          <Flex vertical gap={4}>
            <Typography.Text
              type="secondary"
              style={{ fontSize: 11, fontWeight: 600, textTransform: 'uppercase', letterSpacing: 0.5 }}
            >
              Các subtasks đề xuất:
            </Typography.Text>
            <List
              size="small"
              bordered
              dataSource={snapshot.subtasks}
              style={{ borderRadius: 8, backgroundColor: '#f8fafc', borderColor: '#e2e8f0' }}
              renderItem={(st) => (
                <List.Item style={{ padding: '6px 10px', fontSize: 12.5 }}>
                  <Typography.Text>• {st}</Typography.Text>
                </List.Item>
              )}
            />
          </Flex>
        )}

        {snapshot?.newDueDate && (
          <Flex align="center" gap={6}>
            <CalendarOutlined style={{ fontSize: 14, color: '#10b981' }} />
            <Typography.Text style={{ fontSize: 12.5 }}>
              Hạn mới đề xuất:{' '}
              <strong>{new Date(snapshot.newDueDate).toLocaleDateString()}</strong>
            </Typography.Text>
          </Flex>
        )}

        {error && (
          <Alert
            type="error"
            showIcon
            title={error}
            style={{ borderRadius: 8 }}
          />
        )}

        {/* Footer: Status and Action Buttons */}
        <Flex
          align="center"
          justify="space-between"
          gap={8}
          wrap="wrap"
          style={{ borderTop: '1px solid #f1f5f9', paddingTop: 8 }}
        >
          <Flex align="center" gap={6}>
            <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
              Trạng thái:
            </Typography.Text>
            <Tag
              color={
                actionLog.status === 'Approved'
                  ? 'success'
                  : actionLog.status === 'Pending'
                    ? 'processing'
                    : 'default'
              }
              style={{ margin: 0, fontSize: 11, fontWeight: 600, borderRadius: 9999 }}
            >
              {actionLog.status}
            </Tag>
          </Flex>

          <Space size={8}>
            {actionLog.status === 'Pending' && (
              <>
                <Button
                  size="small"
                  danger
                  onClick={handleReject}
                  loading={isActing}
                  icon={<CloseOutlined />}
                  style={{ borderRadius: 6 }}
                >
                  Từ chối
                </Button>

                <Button
                  size="small"
                  type="primary"
                  onClick={handleApprove}
                  loading={isActing}
                  icon={<CheckOutlined />}
                  style={{
                    borderRadius: 6,
                    backgroundColor: '#059669',
                    borderColor: '#059669',
                    fontWeight: 600,
                  }}
                >
                  Phê duyệt giải pháp
                </Button>
              </>
            )}

            {actionLog.status === 'Approved' && (
              <Button
                size="small"
                onClick={handleUndo}
                loading={isActing}
                icon={<UndoOutlined style={{ color: '#4f46e5' }} />}
                style={{ borderRadius: 6 }}
              >
                Hoàn tác (Undo)
              </Button>
            )}
          </Space>
        </Flex>
      </Flex>
    </Card>
  )
}
