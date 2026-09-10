import React, { useEffect, useMemo, useState } from 'react'
import {
  BulbOutlined,
  CheckCircleOutlined,
  CloseOutlined,
  HourglassOutlined,
  PlusOutlined,
  RedoOutlined,
  RobotOutlined,
  RollbackOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Divider,
  Empty,
  Flex,
  Input,
  Modal,
  Popconfirm,
  Result,
  Select,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd'
import type { ColumnResponse, LabelResponse } from '../../board/types/board.types'
import { useSmartSetup } from '../hooks/useSmartSetup'
import { ProposedTaskItem } from './ProposedTaskItem'
import { formatDateTime } from '../utils/aiActionHelpers'

const { Text, Paragraph } = Typography

interface SmartSetupModalProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  boardId: string
  workspaceLabels?: LabelResponse[]
  columns?: ColumnResponse[]
  onApplied?: () => void
}

export const SmartSetupModal: React.FC<SmartSetupModalProps> = ({
  open,
  onClose,
  workspaceId,
  boardId,
  workspaceLabels = [],
  columns = [],
  onApplied,
}) => {
  const {
    status,
    description,
    summary,
    tasks,
    members,
    actionLog,
    isActionLoading,
    error,
    httpStatus,
    setDescription,
    fetchMembers,
    generate,
    updateTask,
    deleteTask,
    addTask,
    addLabel,
    removeLabel,
    submitConfirm,
    approveAction,
    rejectAction,
    undoAction,
    reset,
    backToPrompt,
  } = useSmartSetup()

  // Selected target column in Step 2 (user override or default to first non-done)
  const [userSelectedColumnId, setUserSelectedColumnId] = useState<string | null>(null)
  const [rejectModalOpen, setRejectModalOpen] = useState(false)
  const [rejectNote, setRejectNote] = useState('')

  const activeColumnId = useMemo(() => {
    if (userSelectedColumnId) return userSelectedColumnId
    if (columns.length > 0) {
      const firstNonDone = columns.find((c) => !c.isDone)
      return firstNonDone ? firstNonDone.id : columns[0].id
    }
    return null
  }, [columns, userSelectedColumnId])

  useEffect(() => {
    if (open && workspaceId) {
      fetchMembers(workspaceId)
    }
  }, [open, workspaceId, fetchMembers])

  const handleClose = () => {
    reset()
    setUserSelectedColumnId(null)
    onClose()
  }

  const handleGenerate = async () => {
    await generate(boardId, description)
  }

  const handleConfirmProposal = async () => {
    await submitConfirm(boardId, activeColumnId)
  }

  const handleApprove = async () => {
    const updated = await approveAction()
    if (updated) {
      onApplied?.()
    }
  }

  const handleConfirmReject = async () => {
    await rejectAction(rejectNote.trim() || null)
    setRejectModalOpen(false)
    setRejectNote('')
  }

  const handleUndo = async () => {
    const updated = await undoAction()
    if (updated) {
      onApplied?.()
    }
  }

  const renderStepContent = () => {
    // Step 3: Pending or decided state in Accountability Layer
    if (status === 'pending' || status === 'confirmed' || actionLog !== null) {
      const currentStatus = actionLog?.status || 'Pending'

      return (
        <div style={{ padding: '16px 0' }}>
          {error && (
            <Alert
              type="error"
              message={error}
              showIcon
              closable
              style={{ marginBottom: 16, borderRadius: 8 }}
            />
          )}

          {currentStatus === 'Pending' && (
            <Result
              status="info"
              icon={<HourglassOutlined style={{ color: '#f59e0b' }} />}
              title="Đã tạo yêu cầu chờ phê duyệt!"
              subTitle={
                <div style={{ maxWidth: 540, margin: '0 auto', textAlign: 'center' }}>
                  <Paragraph type="secondary" style={{ marginBottom: 8 }}>
                    Đề xuất gồm <strong>{actionLog?.taskCount || tasks.length} sub-tasks</strong> đã được ghi nhận vào hàng đợi Accountability Layer với trạng thái{' '}
                    <Tag color="gold" style={{ borderRadius: 6, fontWeight: 600 }}>
                      Pending (Chờ duyệt)
                    </Tag>
                  </Paragraph>
                  <div style={{ fontSize: 12.5, color: '#64748b' }}>
                    Mã hành động: <code>{actionLog?.id}</code>
                    {actionLog?.createdAt && (
                      <span> • Thời điểm tạo: {formatDateTime(actionLog.createdAt)}</span>
                    )}
                  </div>
                </div>
              }
              extra={[
                <Button
                  key="reject"
                  danger
                  icon={<CloseOutlined />}
                  onClick={() => setRejectModalOpen(true)}
                  disabled={isActionLoading}
                  style={{ borderRadius: 8, minWidth: 110 }}
                >
                  Từ chối
                </Button>,
                <Button
                  type="primary"
                  key="approve"
                  icon={<CheckCircleOutlined />}
                  loading={isActionLoading}
                  onClick={handleApprove}
                  style={{
                    borderRadius: 8,
                    minWidth: 160,
                    backgroundColor: '#10b981',
                    borderColor: '#10b981',
                    fontWeight: 600,
                  }}
                >
                  Duyệt & áp dụng ngay
                </Button>,
                <Button key="close" onClick={handleClose} style={{ borderRadius: 8 }}>
                  Đóng
                </Button>,
              ]}
            />
          )}

          {currentStatus === 'Approved' && (
            <Result
              status="success"
              icon={<CheckCircleOutlined style={{ color: '#10b981' }} />}
              title="Đã duyệt & áp dụng thành công!"
              subTitle={
                <div style={{ maxWidth: 540, margin: '0 auto', textAlign: 'center' }}>
                  <Paragraph type="secondary">
                    Các sub-tasks đã được tạo và hiển thị trực tiếp trên bảng Kanban.
                  </Paragraph>
                  <div style={{ fontSize: 12.5, color: '#64748b' }}>
                    Mã hành động: <code>{actionLog?.id}</code>
                  </div>
                </div>
              }
              extra={[
                <Popconfirm
                  key="undo"
                  title="Xác nhận hoàn tác"
                  description="Các thẻ công việc do hành động này tạo sẽ bị xóa mềm khỏi bảng. Bạn có chắc chắn muốn hoàn tác?"
                  onConfirm={handleUndo}
                  okText="Hoàn tác"
                  cancelText="Hủy"
                  okButtonProps={{ danger: true }}
                >
                  <Button
                    icon={<RollbackOutlined />}
                    loading={isActionLoading}
                    style={{ borderRadius: 8, color: '#d97706', borderColor: '#fcd34d' }}
                  >
                    Hoàn tác hành động
                  </Button>
                </Popconfirm>,
                <Button
                  type="primary"
                  key="close"
                  onClick={handleClose}
                  style={{ borderRadius: 8, minWidth: 100, backgroundColor: '#6366f1' }}
                >
                  Đóng
                </Button>,
              ]}
            />
          )}

          {currentStatus === 'Rejected' && (
            <Result
              status="warning"
              title="Đề xuất đã bị từ chối"
              subTitle={
                <div style={{ maxWidth: 540, margin: '0 auto', textAlign: 'center' }}>
                  <Paragraph type="secondary">
                    Hành động này đã được đánh dấu từ chối và không ghi dữ liệu vào bảng Kanban.
                    {actionLog?.decisionNote && (
                      <div style={{ marginTop: 8 }}>
                        <strong>Lý do:</strong> {actionLog.decisionNote}
                      </div>
                    )}
                  </Paragraph>
                </div>
              }
              extra={[
                <Button
                  type="primary"
                  key="close"
                  onClick={handleClose}
                  style={{ borderRadius: 8, minWidth: 100 }}
                >
                  Đóng
                </Button>,
              ]}
            />
          )}

          {currentStatus === 'Undone' && (
            <Result
              status="info"
              icon={<RollbackOutlined style={{ color: '#6366f1' }} />}
              title="Đã hoàn tác hành động thành công"
              subTitle={
                <div style={{ maxWidth: 540, margin: '0 auto', textAlign: 'center' }}>
                  <Paragraph type="secondary">
                    Các sub-tasks được tạo bởi hành động này đã được thu hồi và gỡ bỏ khỏi bảng.
                  </Paragraph>
                </div>
              }
              extra={[
                <Button
                  type="primary"
                  key="close"
                  onClick={handleClose}
                  style={{ borderRadius: 8, minWidth: 100, backgroundColor: '#6366f1' }}
                >
                  Đóng
                </Button>,
              ]}
            />
          )}

          {/* Reject reason modal */}
          <Modal
            title="Từ chối đề xuất AI"
            open={rejectModalOpen}
            onCancel={() => {
              setRejectModalOpen(false)
              setRejectNote('')
            }}
            onOk={handleConfirmReject}
            okText="Xác nhận từ chối"
            okButtonProps={{ danger: true, loading: isActionLoading }}
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
        </div>
      )
    }

    // Step 2: Review and Edit Proposals
    if (status === 'ready' || status === 'submitting') {
      return (
        <Flex vertical gap={16}>
          {/* AI Summary Banner */}
          {summary && (
            <Alert
              title="Tóm tắt đề xuất từ AI:"
              description={summary}
              type="info"
              icon={<RobotOutlined style={{ color: '#6366f1', fontSize: 16 }} />}
              showIcon
              style={{
                borderRadius: 8,
                backgroundColor: '#eff6ff',
                borderColor: '#bfdbfe',
              }}
            />
          )}

          {error && (
            <Alert
              type="error"
              message={
                httpStatus === 403
                  ? 'Không đủ quyền thực hiện'
                  : 'Không thể xác nhận đề xuất'
              }
              description={error}
              showIcon
              style={{ borderRadius: 8 }}
            />
          )}

          {/* Sub-tasks header bar */}
          <Flex align="center" justify="space-between" wrap="wrap" gap={8}>
            <Space size={8}>
              <Text strong style={{ fontSize: 15 }}>
                Danh sách sub-tasks đề xuất
              </Text>
              <Tag color="blue" style={{ borderRadius: 12, fontWeight: 600 }}>
                {tasks.length} công việc
              </Tag>
            </Space>

            <Flex align="center" gap={8}>
              {columns.length > 0 && (
                <Space size={6}>
                  <Text style={{ fontSize: 12.5, color: '#475569' }}>Cột đích:</Text>
                  <Select
                    size="small"
                    value={activeColumnId}
                    onChange={setUserSelectedColumnId}
                    style={{ width: 140 }}
                    options={columns.map((c) => ({
                      value: c.id,
                      label: c.isDone ? `${c.name} (Xong)` : c.name,
                    }))}
                  />
                </Space>
              )}

              <Button
                type="dashed"
                size="small"
                icon={<PlusOutlined />}
                onClick={addTask}
                disabled={status === 'submitting'}
                style={{ borderRadius: 6 }}
              >
                Thêm sub-task
              </Button>
            </Flex>
          </Flex>

          {/* Tasks List */}
          {tasks.length === 0 ? (
            <Empty
              description="Chưa có sub-task nào. Bạn có thể bấm '+ Thêm sub-task' hoặc quay lại nhập lại mô tả."
              style={{ padding: '30px 0' }}
            />
          ) : (
            <div
              style={{
                maxHeight: '440px',
                overflowY: 'auto',
                paddingRight: 6,
              }}
            >
              {tasks.map((task, idx) => (
                <ProposedTaskItem
                  key={task.tempId}
                  task={task}
                  index={idx}
                  members={members}
                  workspaceLabels={workspaceLabels}
                  onUpdate={updateTask}
                  onDelete={deleteTask}
                  onAddLabel={addLabel}
                  onRemoveLabel={removeLabel}
                />
              ))}
            </div>
          )}

          <Divider style={{ margin: '8px 0' }} />

          {/* Actions Footer for Step 2 */}
          <Flex align="center" justify="space-between">
            <Button
              icon={<RedoOutlined />}
              onClick={backToPrompt}
              disabled={status === 'submitting'}
              style={{ borderRadius: 8 }}
            >
              Sửa lại mô tả
            </Button>

            <Space size={10}>
              <Button
                onClick={handleClose}
                disabled={status === 'submitting'}
                style={{ borderRadius: 8 }}
              >
                Hủy
              </Button>
              <Button
                type="primary"
                icon={status === 'submitting' ? <Spin size="small" /> : <CheckCircleOutlined />}
                loading={status === 'submitting'}
                onClick={handleConfirmProposal}
                disabled={tasks.length === 0 || status === 'submitting'}
                style={{
                  backgroundColor: '#10b981',
                  borderColor: '#10b981',
                  borderRadius: 8,
                  fontWeight: 600,
                }}
              >
                {status === 'submitting'
                  ? 'Đang gửi đề xuất...'
                  : `Xác nhận đề xuất (${tasks.length})`}
              </Button>
            </Space>
          </Flex>
        </Flex>
      )
    }

    // Step 1: Prompt Input View (idle, generating, or error)
    return (
      <Flex vertical gap={16}>
        {/* Intro banner */}
        <div
          style={{
            padding: '12px 16px',
            background: 'linear-gradient(135deg, #eef2ff 0%, #f5f3ff 100%)',
            borderRadius: 8,
            border: '1px solid #e0e7ff',
          }}
        >
          <Flex align="center" gap={10}>
            <div
              style={{
                width: 36,
                height: 36,
                borderRadius: '50%',
                backgroundColor: '#6366f1',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: '#ffffff',
                fontSize: 18,
              }}
            >
              <ThunderboltOutlined />
            </div>
            <div>
              <Text strong style={{ fontSize: 14, color: '#312e81' }}>
                AI Smart Setup — Tự động phân rã công việc
              </Text>
              <div style={{ fontSize: 12.5, color: '#4b5563' }}>
                Nhập ý tưởng, tính năng hoặc yêu cầu dự án. AI sẽ đề xuất danh sách sub-tasks,
                mức độ ưu tiên, nhãn và người phụ trách phù hợp.
              </div>
            </div>
          </Flex>
        </div>

        {/* Error Alert if any */}
        {error && (
          <Alert
            message={
              httpStatus === 403
                ? 'Không đủ quyền thực hiện'
                : httpStatus === 502
                ? 'Lỗi phản hồi AI'
                : 'Không thể tạo đề xuất'
            }
            description={error}
            type={httpStatus === 403 ? 'warning' : 'error'}
            showIcon
            style={{ borderRadius: 8 }}
          />
        )}

        {/* Text Input Area */}
        <div>
          <Flex align="center" justify="space-between" style={{ marginBottom: 6 }}>
            <Text strong style={{ fontSize: 13 }}>
              Mô tả tính năng / công việc cần phân rã:
            </Text>
            <Text type="secondary" style={{ fontSize: 11.5 }}>
              Tối đa 4000 ký tự
            </Text>
          </Flex>

          <Input.TextArea
            rows={6}
            placeholder="Ví dụ: Xây dựng tính năng thông báo Real-time cho bảng Kanban khi có người gán task mới hoặc comment, bao gồm thiết kế API, SignalR Hub, giao diện popup chuông thông báo và đánh dấu đã đọc..."
            value={description}
            onChange={(e) => setDescription(e.target.value)}
            maxLength={4000}
            showCount
            disabled={status === 'generating'}
            style={{
              borderRadius: 8,
              fontSize: 13.5,
              lineHeight: 1.6,
            }}
          />
        </div>

        {/* Quick prompt suggestions */}
        <Flex align="center" gap={6} wrap="wrap">
          <Text type="secondary" style={{ fontSize: 12 }}>
            <BulbOutlined /> Gợi ý:
          </Text>
          <Tag
            style={{ cursor: 'pointer', borderRadius: 12 }}
            onClick={() =>
              setDescription(
                'Tích hợp xác thực hai lớp (2FA) bằng ứng dụng Authenticator (TOTP), bao gồm tạo mã QR, xác minh mã OTP và backup recovery codes.'
              )
            }
          >
            Bảo mật 2FA TOTP
          </Tag>
          <Tag
            style={{ cursor: 'pointer', borderRadius: 12 }}
            onClick={() =>
              setDescription(
                'Phát triển tính năng xuất báo cáo tiến độ dự án theo tuần ra file Excel và PDF, có biểu đồ hoàn thành công việc.'
              )
            }
          >
            Xuất báo cáo PDF/Excel
          </Tag>
        </Flex>

        <Divider style={{ margin: '8px 0' }} />

        {/* Footer Actions */}
        <Flex align="center" justify="flex-end" gap={10}>
          <Button
            onClick={handleClose}
            disabled={status === 'generating'}
            style={{ borderRadius: 8 }}
          >
            Hủy
          </Button>

          <Button
            type="primary"
            icon={status === 'generating' ? <Spin size="small" /> : <ThunderboltOutlined />}
            loading={status === 'generating'}
            onClick={handleGenerate}
            disabled={!description.trim() || status === 'generating'}
            style={{
              backgroundColor: '#6366f1',
              borderRadius: 8,
              fontWeight: 600,
              minWidth: 160,
            }}
          >
            {status === 'generating' ? 'AI đang phân tích...' : 'Tạo đề xuất với AI'}
          </Button>
        </Flex>
      </Flex>
    )
  }

  return (
    <Modal
      title={
        <Space size={8}>
          <RobotOutlined style={{ color: '#6366f1', fontSize: 18 }} />
          <span>AI Smart Setup</span>
        </Space>
      }
      open={open}
      onCancel={handleClose}
      footer={null}
      width={720}
      destroyOnHidden
      getContainer={false}
      styles={{
        body: { paddingTop: 12 },
      }}
      style={{ top: 40 }}
    >
      {renderStepContent()}
    </Modal>
  )
}
