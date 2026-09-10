import React, { useState } from 'react'
import {
  CheckOutlined,
  CloseOutlined,
  DownOutlined,
  InfoCircleOutlined,
  RollbackOutlined,
  UpOutlined,
  UserOutlined,
  WarningOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Flex,
  Input,
  Modal,
  Popconfirm,
  Space,
  Tag,
  Typography,
} from 'antd'
import type {
  AiActionLog,
  AiActionLogDetail,
  AiActionStatus,
} from '../types/aiAction.types'
import { formatDateTime } from '../utils/aiActionHelpers'

const { Text, Paragraph } = Typography

interface AiActionLogItemProps {
  log: AiActionLog
  onApprove: (logId: string) => Promise<unknown>
  onReject: (logId: string, note?: string | null) => Promise<unknown>
  onUndo: (logId: string) => Promise<unknown>
  onFetchDetail?: (logId: string) => Promise<AiActionLogDetail | null>
  isProcessing?: boolean
}

const renderStatusTag = (status: AiActionStatus) => {
  switch (status) {
    case 'Pending':
      return (
        <Tag color="gold" style={{ fontWeight: 600, borderRadius: 12 }}>
          Chờ duyệt
        </Tag>
      )
    case 'Approved':
      return (
        <Tag color="green" style={{ fontWeight: 600, borderRadius: 12 }}>
          Đã duyệt
        </Tag>
      )
    case 'Rejected':
      return (
        <Tag color="red" style={{ fontWeight: 600, borderRadius: 12 }}>
          Đã từ chối
        </Tag>
      )
    case 'Undone':
      return (
        <Tag color="default" style={{ fontWeight: 600, borderRadius: 12 }}>
          Đã hoàn tác
        </Tag>
      )
    default:
      return <Tag>{status}</Tag>
  }
}

export const AiActionLogItem: React.FC<AiActionLogItemProps> = ({
  log,
  onApprove,
  onReject,
  onUndo,
  onFetchDetail,
  isProcessing = false,
}) => {
  const [expanded, setExpanded] = useState(false)
  const [detail, setDetail] = useState<AiActionLogDetail | null>(null)
  const [isLoadingDetail, setIsLoadingDetail] = useState(false)
  const [rejectModalOpen, setRejectModalOpen] = useState(false)
  const [rejectNote, setRejectNote] = useState('')

  const handleToggleExpand = async () => {
    const nextState = !expanded
    setExpanded(nextState)
    if (nextState && !detail && onFetchDetail) {
      setIsLoadingDetail(true)
      const data = await onFetchDetail(log.id)
      setDetail(data)
      setIsLoadingDetail(false)
    }
  }

  const handleApprove = async () => {
    await onApprove(log.id)
    if (expanded && onFetchDetail) {
      const data = await onFetchDetail(log.id)
      setDetail(data)
    }
  }

  const handleConfirmReject = async () => {
    await onReject(log.id, rejectNote.trim() || null)
    setRejectModalOpen(false)
    setRejectNote('')
    if (expanded && onFetchDetail) {
      const data = await onFetchDetail(log.id)
      setDetail(data)
    }
  }

  const handleUndo = async () => {
    await onUndo(log.id)
    if (expanded && onFetchDetail) {
      const data = await onFetchDetail(log.id)
      setDetail(data)
    }
  }

  return (
    <Card
      size="small"
      style={{
        borderRadius: 10,
        marginBottom: 12,
        borderColor: log.status === 'Pending' ? '#fcd34d' : '#e2e8f0',
        backgroundColor: log.status === 'Pending' ? '#fffbeb' : '#ffffff',
      }}
      styles={{
        body: { padding: '12px 16px' },
      }}
    >
      <Flex vertical gap={10}>
        {/* Header row: Status tag, Action type, Task count, Expand icon */}
        <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
          <Space size={8} align="center">
            {renderStatusTag(log.status)}
            <Text strong style={{ fontSize: 13.5 }}>
              {log.action === 'CreateSubtasks'
                ? 'Tạo sub-tasks đề xuất'
                : log.action}
            </Text>
            <Tag color="blue" style={{ borderRadius: 10 }}>
              {log.taskCount} tasks
            </Tag>
          </Space>

          <Button
            type="text"
            size="small"
            icon={expanded ? <UpOutlined /> : <DownOutlined />}
            onClick={handleToggleExpand}
            style={{ fontSize: 12, color: '#64748b' }}
          >
            {expanded ? 'Thu gọn' : 'Chi tiết'}
          </Button>
        </Flex>

        {/* Metadata row: Requestor, Decider, timestamps */}
        <Flex justify="space-between" align="center" wrap="wrap" gap={8} style={{ fontSize: 12, color: '#64748b' }}>
          <Space size={4}>
            <UserOutlined style={{ fontSize: 11 }} />
            <span>Yêu cầu bởi:</span>
            <Text strong style={{ fontSize: 12, color: '#334155' }}>
              {log.requestedByName || 'Thành viên'}
            </Text>
            <span>• {formatDateTime(log.createdAt)}</span>
          </Space>

          {log.decidedAt && (
            <Space size={4}>
              <span>
                {log.status === 'Approved'
                  ? 'Duyệt bởi:'
                  : log.status === 'Rejected'
                  ? 'Từ chối bởi:'
                  : 'Xử lý bởi:'}
              </span>
              <Text strong style={{ fontSize: 12, color: '#334155' }}>
                {log.decidedByName || 'Manager'}
              </Text>
              <span>• {formatDateTime(log.decidedAt)}</span>
            </Space>
          )}
        </Flex>

        {/* Decision note if any */}
        {log.decisionNote && (
          <Alert
            title="Ghi chú quyết định:"
            description={log.decisionNote}
            type="warning"
            showIcon
            style={{ borderRadius: 6, padding: '6px 12px', fontSize: 12 }}
          />
        )}

        {/* Expanded detail section */}
        {expanded && (
          <div
            style={{
              marginTop: 4,
              padding: 12,
              backgroundColor: '#f8fafc',
              borderRadius: 8,
              border: '1px solid #e2e8f0',
            }}
          >
            {isLoadingDetail ? (
              <Text type="secondary" style={{ fontSize: 12 }}>
                Đang tải chi tiết snapshot...
              </Text>
            ) : detail ? (
              <Flex vertical gap={10}>
                {/* Basis summary */}
                {detail.basis && (
                  <div>
                    <Text strong style={{ fontSize: 12.5, color: '#475569' }}>
                      Cơ sở phân tích:
                    </Text>
                    <Paragraph
                      type="secondary"
                      style={{
                        fontSize: 12,
                        margin: '2px 0 0',
                        fontStyle: 'italic',
                      }}
                    >
                      {detail.basis.descriptionExcerpt ||
                        `Mô tả ${detail.basis.descriptionLength || 0} ký tự`}
                    </Paragraph>
                  </div>
                )}

                {/* Proposed tasks list in after_snapshot */}
                {detail.afterSnapshot?.tasks && detail.afterSnapshot.tasks.length > 0 && (
                  <div>
                    <Text strong style={{ fontSize: 12.5, color: '#475569' }}>
                      Danh sách sub-tasks ({detail.afterSnapshot.tasks.length}):
                    </Text>
                    <div style={{ marginTop: 6, display: 'flex', flexDirection: 'column', gap: 6 }}>
                      {detail.afterSnapshot.tasks.map((task, idx) => (
                        <div
                          key={idx}
                          style={{
                            padding: '6px 10px',
                            backgroundColor: '#ffffff',
                            borderRadius: 6,
                            border: '1px solid #e2e8f0',
                            fontSize: 12.5,
                          }}
                        >
                          <Flex justify="space-between" align="center">
                            <Text strong>{task.title}</Text>
                            {task.priority && (
                              <Tag
                                color={
                                  task.priority === 'Urgent'
                                    ? 'red'
                                    : task.priority === 'High'
                                    ? 'orange'
                                    : task.priority === 'Medium'
                                    ? 'blue'
                                    : 'default'
                                }
                                style={{ margin: 0, fontSize: 11 }}
                              >
                                {task.priority}
                              </Tag>
                            )}
                          </Flex>
                          {task.description && (
                            <div style={{ fontSize: 11.5, color: '#64748b', marginTop: 2 }}>
                              {task.description}
                            </div>
                          )}
                          {task.labels && task.labels.length > 0 && (
                            <Space size={4} wrap style={{ marginTop: 4 }}>
                              {task.labels.map((lbl, lIdx) => (
                                <Tag key={lIdx} style={{ fontSize: 10.5, borderRadius: 8, margin: 0 }}>
                                  {lbl.name}
                                </Tag>
                              ))}
                            </Space>
                          )}
                        </div>
                      ))}
                    </div>
                  </div>
                )}

                {/* Warnings / Undo Warnings */}
                {detail.appliedSnapshot?.warnings &&
                  detail.appliedSnapshot.warnings.length > 0 && (
                    <Alert
                      type="warning"
                      showIcon
                      icon={<WarningOutlined />}
                      title="Cảnh báo khi áp dụng:"
                      description={
                        <ul style={{ margin: 0, paddingLeft: 18, fontSize: 11.5 }}>
                          {detail.appliedSnapshot.warnings.map((w, idx) => (
                            <li key={idx}>{w}</li>
                          ))}
                        </ul>
                      }
                      style={{ borderRadius: 6, padding: '6px 12px' }}
                    />
                  )}

                {detail.appliedSnapshot?.undoWarnings &&
                  detail.appliedSnapshot.undoWarnings.length > 0 && (
                    <Alert
                      type="info"
                      showIcon
                      icon={<InfoCircleOutlined />}
                      title="Ghi chú khi hoàn tác:"
                      description={
                        <ul style={{ margin: 0, paddingLeft: 18, fontSize: 11.5 }}>
                          {detail.appliedSnapshot.undoWarnings.map((w, idx) => (
                            <li key={idx}>{w}</li>
                          ))}
                        </ul>
                      }
                      style={{ borderRadius: 6, padding: '6px 12px' }}
                    />
                  )}
              </Flex>
            ) : (
              <Text type="secondary" style={{ fontSize: 12 }}>
                Không có dữ liệu chi tiết snapshot.
              </Text>
            )}
          </div>
        )}

        {/* Action buttons footer */}
        {log.status === 'Pending' && (
          <Flex justify="flex-end" gap={8} style={{ marginTop: 4 }}>
            <Button
              size="small"
              danger
              icon={<CloseOutlined />}
              onClick={() => setRejectModalOpen(true)}
              disabled={isProcessing}
              style={{ borderRadius: 6 }}
            >
              Từ chối
            </Button>
            <Button
              type="primary"
              size="small"
              icon={<CheckOutlined />}
              onClick={handleApprove}
              loading={isProcessing}
              style={{
                backgroundColor: '#10b981',
                borderColor: '#10b981',
                borderRadius: 6,
                fontWeight: 500,
              }}
            >
              Duyệt & áp dụng
            </Button>
          </Flex>
        )}

        {log.status === 'Approved' && (
          <Flex justify="flex-end" style={{ marginTop: 4 }}>
            <Popconfirm
              title="Xác nhận hoàn tác"
              description="Các thẻ công việc do hành động này tạo sẽ bị xóa mềm khỏi bảng. Bạn có chắc chắn muốn hoàn tác?"
              onConfirm={handleUndo}
              okText="Hoàn tác"
              cancelText="Hủy"
              okButtonProps={{ danger: true }}
            >
              <Button
                size="small"
                icon={<RollbackOutlined />}
                loading={isProcessing}
                style={{ borderRadius: 6, color: '#d97706', borderColor: '#fcd34d' }}
              >
                Hoàn tác hành động
              </Button>
            </Popconfirm>
          </Flex>
        )}
      </Flex>

      {/* Reject reason modal */}
      <Modal
        title="Từ chối hành động AI"
        open={rejectModalOpen}
        onCancel={() => {
          setRejectModalOpen(false)
          setRejectNote('')
        }}
        onOk={handleConfirmReject}
        okText="Xác nhận từ chối"
        okButtonProps={{ danger: true, loading: isProcessing }}
        cancelText="Hủy"
        destroyOnHidden
      >
        <Flex vertical gap={10} style={{ marginTop: 12 }}>
          <Text style={{ fontSize: 13 }}>
            Nhập lý do từ chối đề xuất (tối đa 500 ký tự, tuỳ chọn):
          </Text>
          <Input.TextArea
            rows={3}
            value={rejectNote}
            onChange={(e) => setRejectNote(e.target.value)}
            maxLength={500}
            showCount
            placeholder="Ví dụ: Đề xuất chưa phù hợp với phạm vi sprint này..."
          />
        </Flex>
      </Modal>
    </Card>
  )
}
