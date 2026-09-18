import React, { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Col,
  Flex,
  Input,
  Modal,
  Radio,
  Row,
  Select,
  Space,
  Tag,
  Typography,
  message,
} from 'antd'
import {
  ArrowLeftOutlined,
  CheckCircleFilled,
  DeleteOutlined,
  RobotOutlined,
} from '@ant-design/icons'
import { useBoardTemplate } from '../hooks/useBoardTemplate'

const { Text, Paragraph } = Typography

interface BoardTemplateModalProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  onCreated?: () => void
}

const PRIORITY_COLORS: Record<string, string> = {
  Urgent: 'red',
  High: 'orange',
  Medium: 'blue',
  Low: 'default',
}

export const BoardTemplateModal: React.FC<BoardTemplateModalProps> = ({
  open,
  onClose,
  workspaceId,
  onCreated,
}) => {
  const [descriptionInput, setDescriptionInput] = useState('')
  const {
    status,
    proposal,
    error,
    generate,
    updateProposal,
    confirm,
    reset,
    clearError,
  } = useBoardTemplate(workspaceId)

  const isGenerating = status === 'generating'
  const isConfirming = status === 'confirming'
  const isPreview = status === 'preview' || status === 'error' || status === 'confirming'

  const handleCancel = () => {
    setDescriptionInput('')
    reset()
    onClose()
  }

  const handleGenerate = async () => {
    if (descriptionInput.trim().length < 20 || isGenerating) return
    await generate(descriptionInput)
  }

  const handleDoneColumnChange = (chosenColumnName: string) => {
    if (!proposal) return
    const updatedColumns = proposal.columns.map((c) => ({
      ...c,
      isDone: c.name === chosenColumnName,
    }))
    updateProposal({ columns: updatedColumns })
  }

  const handleTaskColumnChange = (taskIndex: number, newColumnName: string) => {
    if (!proposal) return
    const updatedTasks = [...proposal.tasks]
    updatedTasks[taskIndex] = {
      ...updatedTasks[taskIndex],
      columnName: newColumnName,
    }
    updateProposal({ tasks: updatedTasks })
  }

  const handleDeleteTask = (taskIndex: number) => {
    if (!proposal) return
    const updatedTasks = proposal.tasks.filter((_, idx) => idx !== taskIndex)
    updateProposal({ tasks: updatedTasks })
  }

  const handleConfirm = async () => {
    if (!proposal) return
    const result = await confirm()
    if (result) {
      message.success('Đã tạo đề xuất — chờ Manager duyệt trong Lịch sử AI')
      onCreated?.()
      handleCancel()
    }
  }

  const doneColumn = proposal?.columns.find((c) => c.isDone)?.name ?? ''

  return (
    <Modal
      open={open}
      onCancel={handleCancel}
      width={780}
      destroyOnHidden
      title={
        <Space size={8}>
          <RobotOutlined style={{ color: '#7c3aed', fontSize: 18 }} />
          <span>Tạo Bảng Công Việc Bằng AI (AI Board Template)</span>
        </Space>
      }
      footer={
        isPreview ? (
          <Flex justify="space-between" align="center">
            <Button
              icon={<ArrowLeftOutlined />}
              onClick={() => reset()}
              disabled={isConfirming}
            >
              Mô tả lại
            </Button>
            <Space>
              <Button onClick={handleCancel} disabled={isConfirming}>
                Hủy
              </Button>
              <Button
                type="primary"
                onClick={handleConfirm}
                loading={isConfirming}
                style={{ backgroundColor: '#6366f1' }}
              >
                Xác nhận tạo
              </Button>
            </Space>
          </Flex>
        ) : (
          <Flex justify="flex-end" gap={8}>
            <Button onClick={handleCancel} disabled={isGenerating}>
              Hủy
            </Button>
            <Button
              type="primary"
              onClick={handleGenerate}
              loading={isGenerating}
              disabled={descriptionInput.trim().length < 20}
              style={{ backgroundColor: '#6366f1' }}
            >
              Tạo mẫu đề xuất
            </Button>
          </Flex>
        )
      }
    >
      {error && (
        <Alert
          type="error"
          title={error}
          closable
          onClose={clearError}
          style={{ marginBottom: 16, borderRadius: 8 }}
        />
      )}

      {/* Step 1: Input Description */}
      {!isPreview && (
        <Flex vertical gap={12} style={{ padding: '8px 0' }}>
          <Paragraph type="secondary" style={{ fontSize: 13 }}>
            Nhập mô tả về mục tiêu dự án, quy trình làm việc hoặc sản phẩm cần phát triển (tối thiểu 20 ký tự). AI sẽ đề xuất tên bảng, các cột quy trình và 5–10 công việc khởi đầu phù hợp.
          </Paragraph>

          <div>
            <Text strong style={{ fontSize: 13, display: 'block', marginBottom: 6 }}>
              Mô tả dự án:
            </Text>
            <Input.TextArea
              rows={5}
              value={descriptionInput}
              onChange={(e) => setDescriptionInput(e.target.value)}
              placeholder="Ví dụ: Xây dựng ứng dụng web thương mại điện tử với tính năng đặt hàng, thanh toán qua cổng thanh toán điện tử và quản lý đơn hàng cho chủ cửa hàng..."
              maxLength={4000}
              showCount
              disabled={isGenerating}
              style={{ borderRadius: 8 }}
            />
          </div>

          <div style={{ backgroundColor: '#f8fafc', padding: 12, borderRadius: 8, border: '1px solid #e2e8f0' }}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              💡 Mẹo: Mô tả càng chi tiết về công nghệ và đối tượng người dùng, AI càng tạo ra các cột và nhiệm vụ sát với thực tế dự án hơn.
            </Text>
          </div>
        </Flex>
      )}

      {/* Step 2: Editable Preview */}
      {isPreview && proposal && (
        <Flex vertical gap={16} style={{ maxHeight: '65vh', overflowY: 'auto', paddingRight: 4 }}>
          {proposal.summary && (
            <Alert
              type="info"
              title="Phân tích AI:"
              description={proposal.summary}
              showIcon
              style={{ borderRadius: 8 }}
            />
          )}

          {/* Board metadata */}
          <Card size="small" style={{ borderRadius: 8, backgroundColor: '#f8fafc' }}>
            <Flex vertical gap={10}>
              <div>
                <Text strong style={{ fontSize: 12.5, display: 'block', marginBottom: 4 }}>
                  Tên bảng công việc:
                </Text>
                <Input
                  value={proposal.boardName}
                  onChange={(e) => updateProposal({ boardName: e.target.value })}
                  maxLength={100}
                  style={{ borderRadius: 6 }}
                />
              </div>

              <div>
                <Text strong style={{ fontSize: 12.5, display: 'block', marginBottom: 4 }}>
                  Mô tả bảng (tùy chọn):
                </Text>
                <Input.TextArea
                  rows={2}
                  value={proposal.boardDescription || ''}
                  onChange={(e) => updateProposal({ boardDescription: e.target.value })}
                  maxLength={500}
                  style={{ borderRadius: 6, resize: 'none' }}
                />
              </div>
            </Flex>
          </Card>

          {/* Columns Preview & Done column selection (D19: Radio.Group only!) */}
          <div>
            <Flex justify="space-between" align="center" style={{ marginBottom: 6 }}>
              <Text strong style={{ fontSize: 13 }}>
                Cột trạng thái ({proposal.columns.length} cột):
              </Text>
              <Text type="secondary" style={{ fontSize: 11.5 }}>
                Chọn 1 cột biểu thị trạng thái "Hoàn thành"
              </Text>
            </Flex>

            <Radio.Group
              value={doneColumn}
              onChange={(e) => handleDoneColumnChange(e.target.value)}
              style={{ width: '100%' }}
            >
              <Row gutter={[8, 8]}>
                {proposal.columns.map((col, idx) => (
                  <Col span={12} sm={8} key={idx}>
                    <Card
                      size="small"
                      style={{
                        borderRadius: 6,
                        borderColor: col.isDone ? '#10b981' : '#e2e8f0',
                        backgroundColor: col.isDone ? '#f0fdf4' : '#ffffff',
                      }}
                    >
                      <Radio value={col.name}>
                        <Space orientation="horizontal" size={4}>
                          <Text strong style={{ fontSize: 12 }}>
                            {col.name}
                          </Text>
                          {col.isDone && (
                            <CheckCircleFilled style={{ color: '#10b981', fontSize: 12 }} />
                          )}
                        </Space>
                      </Radio>
                    </Card>
                  </Col>
                ))}
              </Row>
            </Radio.Group>
          </div>

          {/* Tasks Preview with Column selector and Delete */}
          <div>
            <Flex justify="space-between" align="center" style={{ marginBottom: 6 }}>
              <Text strong style={{ fontSize: 13 }}>
                Danh sách thẻ công việc khởi đầu ({proposal.tasks.length} thẻ):
              </Text>
              <Text type="secondary" style={{ fontSize: 11.5 }}>
                Bạn có thể xóa bớt hoặc đổi cột trước khi tạo
              </Text>
            </Flex>

            <Flex vertical gap={8}>
              {proposal.tasks.map((task, idx) => (
                <Card
                  key={idx}
                  size="small"
                  style={{ borderRadius: 8, border: '1px solid #e2e8f0' }}
                >
                  <Flex justify="space-between" align="flex-start" gap={8}>
                    <div style={{ flex: 1 }}>
                      <Flex align="center" gap={6} wrap="wrap">
                        <Text strong style={{ fontSize: 13 }}>
                          {task.title}
                        </Text>
                        {task.priority && (
                          <Tag
                            color={PRIORITY_COLORS[task.priority] || 'default'}
                            style={{ margin: 0, fontSize: 11 }}
                          >
                            {task.priority}
                          </Tag>
                        )}
                      </Flex>

                      {task.description && (
                        <div style={{ fontSize: 12, color: '#64748b', marginTop: 4 }}>
                          {task.description}
                        </div>
                      )}

                      {task.labels && task.labels.length > 0 && (
                        <Space size={4} wrap style={{ marginTop: 4 }}>
                          {task.labels.map((lbl, lIdx) => (
                            <Tag key={lIdx} style={{ fontSize: 10.5, borderRadius: 6, margin: 0 }}>
                              {lbl.name}
                            </Tag>
                          ))}
                        </Space>
                      )}
                    </div>

                    <Space size={6} align="center">
                      <Select
                        size="small"
                        value={task.columnName}
                        onChange={(newCol) => handleTaskColumnChange(idx, newCol)}
                        style={{ minWidth: 120 }}
                        options={proposal.columns.map((c) => ({
                          value: c.name,
                          label: c.name,
                        }))}
                      />
                      <Button
                        size="small"
                        type="text"
                        danger
                        icon={<DeleteOutlined />}
                        onClick={() => handleDeleteTask(idx)}
                      />
                    </Space>
                  </Flex>
                </Card>
              ))}
            </Flex>
          </div>
        </Flex>
      )}
    </Modal>
  )
}
