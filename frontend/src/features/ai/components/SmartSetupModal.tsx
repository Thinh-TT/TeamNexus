import React, { useEffect } from 'react'
import {
  BulbOutlined,
  CheckCircleOutlined,
  PlusOutlined,
  RedoOutlined,
  RobotOutlined,
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
  Result,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd'
import type { LabelResponse } from '../../board/types/board.types'
import { useSmartSetup } from '../hooks/useSmartSetup'
import { ProposedTaskItem } from './ProposedTaskItem'

const { Text, Paragraph } = Typography

interface SmartSetupModalProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  boardId: string
  workspaceLabels?: LabelResponse[]
}

export const SmartSetupModal: React.FC<SmartSetupModalProps> = ({
  open,
  onClose,
  workspaceId,
  boardId,
  workspaceLabels = [],
}) => {
  const {
    status,
    description,
    summary,
    tasks,
    members,
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
    confirm,
    reset,
    backToPrompt,
  } = useSmartSetup()

  useEffect(() => {
    if (open && workspaceId) {
      fetchMembers(workspaceId)
    }
  }, [open, workspaceId, fetchMembers])

  const handleClose = () => {
    reset()
    onClose()
  }

  const handleGenerate = async () => {
    await generate(boardId, description)
  }

  const renderStepContent = () => {
    // Step 3: Confirmed result
    if (status === 'confirmed') {
      return (
        <div style={{ padding: '24px 0' }}>
          <Result
            status="success"
            icon={<CheckCircleOutlined style={{ color: '#10b981' }} />}
            title="Đề xuất phân rã công việc đã được xác nhận!"
            subTitle={
              <div style={{ maxWidth: 540, margin: '0 auto', textAlign: 'center' }}>
                <Paragraph type="secondary">
                  Tổng cộng <strong>{tasks.length} sub-tasks</strong> đã được ghi nhận.
                  Các công việc sẽ được áp dụng trực tiếp vào bảng Kanban thông qua{' '}
                  <Tag color="purple" style={{ borderRadius: 6 }}>
                    Accountability Layer
                  </Tag>{' '}
                  ở Giai đoạn 4.
                </Paragraph>
              </div>
            }
            extra={[
              <Button
                type="primary"
                key="close"
                size="large"
                onClick={handleClose}
                style={{ borderRadius: 8, minWidth: 120, backgroundColor: '#6366f1' }}
              >
                Đóng
              </Button>,
            ]}
          />
        </div>
      )
    }

    // Step 2: Review and Edit Proposals
    if (status === 'ready') {
      return (
        <Flex vertical gap={16}>
          {/* AI Summary Banner */}
          {summary && (
            <Alert
              message={
                <Space align="start">
                  <RobotOutlined style={{ color: '#6366f1', fontSize: 16, marginTop: 3 }} />
                  <div>
                    <Text strong style={{ color: '#4338ca' }}>
                      Tóm tắt đề xuất từ AI:
                    </Text>
                    <div style={{ fontSize: 13, color: '#374151', marginTop: 2 }}>
                      {summary}
                    </div>
                  </div>
                </Space>
              }
              type="info"
              style={{
                borderRadius: 8,
                backgroundColor: '#eff6ff',
                borderColor: '#bfdbfe',
              }}
            />
          )}

          {/* Sub-tasks header bar */}
          <Flex align="center" justify="space-between">
            <Space size={8}>
              <Text strong style={{ fontSize: 15 }}>
                Danh sách sub-tasks đề xuất
              </Text>
              <Tag color="blue" style={{ borderRadius: 12, fontWeight: 600 }}>
                {tasks.length} công việc
              </Tag>
            </Space>

            <Button
              type="dashed"
              size="small"
              icon={<PlusOutlined />}
              onClick={addTask}
              style={{ borderRadius: 6 }}
            >
              Thêm sub-task
            </Button>
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
                maxHeight: '460px',
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
              style={{ borderRadius: 8 }}
            >
              Sửa lại mô tả
            </Button>

            <Space size={10}>
              <Button onClick={handleClose} style={{ borderRadius: 8 }}>
                Hủy
              </Button>
              <Button
                type="primary"
                icon={<CheckCircleOutlined />}
                onClick={confirm}
                disabled={tasks.length === 0}
                style={{
                  backgroundColor: '#10b981',
                  borderColor: '#10b981',
                  borderRadius: 8,
                  fontWeight: 600,
                }}
              >
                Xác nhận đề xuất ({tasks.length})
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
