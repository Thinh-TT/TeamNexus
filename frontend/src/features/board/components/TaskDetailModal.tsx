import React, { useEffect, useState } from 'react'
import {
  CalendarOutlined,
  CheckCircleFilled,
  DeleteOutlined,
  EditOutlined,
  FlagOutlined,
  PlusOutlined,
  SendOutlined,
  TagOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Button,
  Card,
  Col,
  ColorPicker,
  DatePicker,
  Divider,
  Dropdown,
  Flex,
  Form,
  Input,
  type MenuProps,
  Modal,
  Popconfirm,
  Row,
  Select,
  Space,
  Tabs,
  Tag,
  Typography,
  message,
} from 'antd'
import dayjs from 'dayjs'
import { useAuth } from '../../auth/hooks/useAuth'
import { boardApi } from '../services/boardApi'
import type {
  ColumnResponse,
  CommentResponse,
  CreateCommentRequest,
  CreateLabelRequest,
  LabelResponse,
  TaskResponse,
  UpdateTaskRequest,
} from '../types/board.types'

interface TaskDetailModalProps {
  task: TaskResponse | null
  columns: ColumnResponse[]
  workspaceLabels: LabelResponse[]
  open: boolean
  onClose: () => void
  onUpdateTask: (taskId: string, data: UpdateTaskRequest) => Promise<unknown>
  onMoveTask: (
    taskId: string,
    sourceColId: string,
    destColId: string,
    position: number
  ) => Promise<unknown>
  onDeleteTask: (taskId: string) => Promise<unknown>
  onCreateLabel: (data: CreateLabelRequest) => Promise<LabelResponse | undefined>
  onAttachLabel: (taskId: string, labelId: string) => Promise<unknown>
  onDetachLabel: (taskId: string, labelId: string) => Promise<unknown>
}

const PRESET_LABEL_COLORS = [
  '#ef4444',
  '#f97316',
  '#f59e0b',
  '#10b981',
  '#06b6d4',
  '#3b82f6',
  '#6366f1',
  '#8b5cf6',
  '#ec4899',
  '#64748b',
]

export const TaskDetailModal: React.FC<TaskDetailModalProps> = ({
  task,
  columns,
  workspaceLabels,
  open,
  onClose,
  onUpdateTask,
  onMoveTask,
  onDeleteTask,
  onCreateLabel,
  onAttachLabel,
  onDetachLabel,
}) => {
  const { user } = useAuth()
  const [form] = Form.useForm()

  // Comments state
  const [comments, setComments] = useState<CommentResponse[]>([])
  const [loadingComments, setLoadingComments] = useState(false)
  const [newCommentContent, setNewCommentContent] = useState('')
  const [isSubmittingComment, setIsSubmittingComment] = useState(false)
  const [editingCommentId, setEditingCommentId] = useState<string | null>(null)
  const [editingCommentContent, setEditingCommentContent] = useState('')

  // New label inline form state
  const [isCreatingLabel, setIsCreatingLabel] = useState(false)
  const [newLabelName, setNewLabelName] = useState('')
  const [newLabelColor, setNewLabelColor] = useState('#6366f1')

  // Sync task data into form and fetch comments
  useEffect(() => {
    if (!task || !open) return

    form.setFieldsValue({
      title: task.title,
      description: task.description || '',
      columnId: task.columnId,
      priority: task.priority || null,
      dueDate: task.dueDate ? dayjs(task.dueDate) : null,
    })

    let ignore = false
    const loadComments = async () => {
      setLoadingComments(true)
      try {
        const data = await boardApi.getComments(task.id)
        if (!ignore) setComments(data)
      } catch {
        if (!ignore) message.error('Không thể tải bình luận')
      } finally {
        if (!ignore) setLoadingComments(false)
      }
    }

    loadComments()

    return () => {
      ignore = true
    }
  }, [task, open, form])

  if (!task) return null

  const handleSaveMetadata = async () => {
    try {
      const values = await form.validateFields()

      const updateData: UpdateTaskRequest = {
        title: values.title.trim(),
        description: values.description ? values.description.trim() : null,
        priority: values.priority || null,
        dueDate: values.dueDate ? values.dueDate.toISOString() : null,
        assigneeId: task.assigneeId,
      }

      await onUpdateTask(task.id, updateData)

      // Check if column was changed
      if (values.columnId && values.columnId !== task.columnId) {
        await onMoveTask(task.id, task.columnId, values.columnId, 0)
      }
    } catch {
      // Form validation error
    }
  }

  // ---- Comment Handlers ----
  const handleAddComment = async () => {
    const trimmed = newCommentContent.trim()
    if (!trimmed) return

    setIsSubmittingComment(true)
    try {
      const req: CreateCommentRequest = { content: trimmed }
      const newComment = await boardApi.createComment(task.id, req)
      setComments((prev) => [...prev, newComment])
      setNewCommentContent('')
      message.success('Đã gửi bình luận')
    } catch {
      message.error('Gửi bình luận thất bại')
    } finally {
      setIsSubmittingComment(false)
    }
  }

  const handleUpdateComment = async (commentId: string) => {
    const trimmed = editingCommentContent.trim()
    if (!trimmed) return

    try {
      const updated = await boardApi.updateComment(task.id, commentId, { content: trimmed })
      setComments((prev) => prev.map((c) => (c.id === commentId ? updated : c)))
      setEditingCommentId(null)
      message.success('Đã sửa bình luận')
    } catch {
      message.error('Sửa bình luận thất bại')
    }
  }

  const handleDeleteComment = async (commentId: string) => {
    try {
      await boardApi.deleteComment(task.id, commentId)
      setComments((prev) => prev.filter((c) => c.id !== commentId))
      message.success('Đã xoá bình luận')
    } catch {
      message.error('Xoá bình luận thất bại')
    }
  }

  // ---- Label Handlers ----
  const handleQuickCreateLabel = async () => {
    if (!newLabelName.trim()) return
    try {
      const created = await onCreateLabel({
        name: newLabelName.trim(),
        color: newLabelColor,
      })
      if (created) {
        await onAttachLabel(task.id, created.id)
      }
      setNewLabelName('')
      setIsCreatingLabel(false)
    } catch {
      // Handled in hook
    }
  }

  const unattachedLabels = workspaceLabels.filter(
    (wl) => !task.labels.some((tl) => tl.id === wl.id)
  )

  const labelMenuItems: MenuProps['items'] = [
    ...unattachedLabels.map((lbl) => ({
      key: lbl.id,
      label: (
        <Flex align="center" gap={8}>
          <div
            style={{
              width: 12,
              height: 12,
              borderRadius: 3,
              backgroundColor: lbl.color,
            }}
          />
          <span>{lbl.name}</span>
        </Flex>
      ),
      onClick: () => onAttachLabel(task.id, lbl.id),
    })),
    ...(unattachedLabels.length > 0 ? [{ type: 'divider' as const }] : []),
    {
      key: 'create_new_label',
      icon: <PlusOutlined />,
      label: 'Tạo nhãn mới',
      onClick: () => setIsCreatingLabel(true),
    },
  ]

  const isDoneColumn = columns.find((c) => c.id === task.columnId)?.isDone

  return (
    <Modal
      open={open}
      onCancel={onClose}
      width={780}
      style={{ top: 32 }}
      footer={null}
      destroyOnClose
    >
      <Form form={form} layout="vertical" onFinish={handleSaveMetadata}>
        <Row gutter={24}>
          {/* Main Content Area (Left) */}
          <Col xs={24} md={16}>
            {/* Title */}
            <Form.Item
              name="title"
              rules={[{ required: true, message: 'Tiêu đề không được để trống' }]}
              style={{ marginBottom: 12 }}
            >
              <Input
                size="large"
                placeholder="Tiêu đề thẻ..."
                style={{
                  fontSize: 18,
                  fontWeight: 600,
                  borderRadius: 8,
                  border: '1px solid transparent',
                  paddingLeft: 8,
                }}
                onPressEnter={handleSaveMetadata}
                onBlur={handleSaveMetadata}
              />
            </Form.Item>

            {/* Labels Section */}
            <div style={{ marginBottom: 16 }}>
              <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 6 }}>
                <TagOutlined style={{ marginRight: 4 }} /> Nhãn:
              </Typography.Text>
              <Flex wrap="wrap" gap={6} align="center">
                {task.labels?.map((label) => (
                  <Tag
                    key={label.id}
                    closable
                    onClose={(e) => {
                      e.preventDefault()
                      onDetachLabel(task.id, label.id)
                    }}
                    style={{
                      margin: 0,
                      padding: '2px 8px',
                      fontSize: 12,
                      borderRadius: 4,
                      backgroundColor: `${label.color}18`,
                      borderColor: `${label.color}50`,
                      color: label.color,
                      fontWeight: 500,
                    }}
                  >
                    {label.name}
                  </Tag>
                ))}

                <Dropdown menu={{ items: labelMenuItems }} trigger={['click']}>
                  <Button
                    size="small"
                    icon={<PlusOutlined />}
                    style={{ borderRadius: 4, fontSize: 12, color: '#64748b' }}
                  >
                    Gán nhãn
                  </Button>
                </Dropdown>
              </Flex>

              {/* Inline Create Label Form */}
              {isCreatingLabel && (
                <Card
                  size="small"
                  style={{
                    marginTop: 10,
                    borderRadius: 8,
                    border: '1px solid #cbd5e1',
                    background: '#f8fafc',
                  }}
                  bodyStyle={{ padding: 10 }}
                >
                  <Typography.Text strong style={{ fontSize: 12, display: 'block', marginBottom: 6 }}>
                    Tạo nhãn mới cho Workspace:
                  </Typography.Text>
                  <Flex gap={8} align="center">
                    <ColorPicker
                      value={newLabelColor}
                      presets={[{ label: 'Gợi ý màu', colors: PRESET_LABEL_COLORS }]}
                      onChange={(c) => setNewLabelColor(c.toHexString())}
                    />
                    <Input
                      placeholder="Tên nhãn..."
                      value={newLabelName}
                      onChange={(e) => setNewLabelName(e.target.value)}
                      size="small"
                      style={{ flex: 1 }}
                      onPressEnter={handleQuickCreateLabel}
                    />
                    <Button size="small" type="primary" onClick={handleQuickCreateLabel}>
                      Lưu
                    </Button>
                    <Button size="small" onClick={() => setIsCreatingLabel(false)}>
                      Huỷ
                    </Button>
                  </Flex>
                </Card>
              )}
            </div>

            {/* Description */}
            <Form.Item name="description" label="Mô tả chi tiết" style={{ marginBottom: 16 }}>
              <Input.TextArea
                rows={4}
                placeholder="Thêm mô tả chi tiết cho thẻ này..."
                style={{ borderRadius: 8 }}
                onBlur={handleSaveMetadata}
              />
            </Form.Item>

            <Divider style={{ margin: '16px 0' }} />

            {/* Tabs (Comments & Activity) */}
            <Tabs
              defaultActiveKey="comments"
              items={[
                {
                  key: 'comments',
                  label: `Bình luận (${comments.length})`,
                  children: (
                    <Flex vertical gap={12}>
                      {/* Comments List */}
                      <div
                        style={{
                          maxHeight: 240,
                          overflowY: 'auto',
                          display: 'flex',
                          flexDirection: 'column',
                          gap: 12,
                          paddingRight: 4,
                        }}
                      >
                        {loadingComments && (
                          <Typography.Text type="secondary">Đang tải bình luận...</Typography.Text>
                        )}
                        {!loadingComments && comments.length === 0 && (
                          <Typography.Text type="secondary" style={{ fontStyle: 'italic', fontSize: 13 }}>
                            Chưa có bình luận nào. Hãy bắt đầu cuộc thảo luận!
                          </Typography.Text>
                        )}
                        {comments.map((comment) => {
                          const isAuthor = user?.id === comment.authorId
                          const isEditing = editingCommentId === comment.id

                          return (
                            <Flex key={comment.id} gap={10} align="flex-start">
                              <Avatar size={30} style={{ backgroundColor: '#6366f1' }}>
                                {comment.authorName?.[0]?.toUpperCase() ?? 'U'}
                              </Avatar>
                              <div
                                style={{
                                  flex: 1,
                                  background: '#f8fafc',
                                  padding: '8px 12px',
                                  borderRadius: 8,
                                  border: '1px solid #e2e8f0',
                                }}
                              >
                                <Flex justify="space-between" align="center">
                                  <Space size={6}>
                                    <Typography.Text strong style={{ fontSize: 13 }}>
                                      {comment.authorName}
                                    </Typography.Text>
                                    <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                                      {dayjs(comment.createdAt).format('HH:mm DD/MM/YYYY')}
                                    </Typography.Text>
                                  </Space>

                                  {isAuthor && !isEditing && (
                                    <Space size={4}>
                                      <Button
                                        type="text"
                                        size="small"
                                        icon={<EditOutlined style={{ fontSize: 12 }} />}
                                        onClick={() => {
                                          setEditingCommentId(comment.id)
                                          setEditingCommentContent(comment.content)
                                        }}
                                      />
                                      <Popconfirm
                                        title="Xoá bình luận?"
                                        okText="Xoá"
                                        cancelText="Huỷ"
                                        okButtonProps={{ danger: true }}
                                        onConfirm={() => handleDeleteComment(comment.id)}
                                      >
                                        <Button
                                          type="text"
                                          size="small"
                                          danger
                                          icon={<DeleteOutlined style={{ fontSize: 12 }} />}
                                        />
                                      </Popconfirm>
                                    </Space>
                                  )}
                                </Flex>

                                {isEditing ? (
                                  <div style={{ marginTop: 8 }}>
                                    <Input.TextArea
                                      value={editingCommentContent}
                                      onChange={(e) => setEditingCommentContent(e.target.value)}
                                      rows={2}
                                      style={{ marginBottom: 6 }}
                                    />
                                    <Space size={6}>
                                      <Button
                                        size="small"
                                        type="primary"
                                        onClick={() => handleUpdateComment(comment.id)}
                                      >
                                        Lưu
                                      </Button>
                                      <Button
                                        size="small"
                                        onClick={() => setEditingCommentId(null)}
                                      >
                                        Huỷ
                                      </Button>
                                    </Space>
                                  </div>
                                ) : (
                                  <Typography.Paragraph
                                    style={{ margin: '4px 0 0 0', fontSize: 13, whiteSpace: 'pre-wrap' }}
                                  >
                                    {comment.content}
                                  </Typography.Paragraph>
                                )}
                              </div>
                            </Flex>
                          )
                        })}
                      </div>

                      {/* Add comment input */}
                      <Flex gap={8} align="flex-start" style={{ marginTop: 8 }}>
                        <Avatar size={30} src={user?.avatarUrl} style={{ backgroundColor: '#6366f1' }}>
                          {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
                        </Avatar>
                        <div style={{ flex: 1 }}>
                          <Input.TextArea
                            placeholder="Viết bình luận..."
                            value={newCommentContent}
                            onChange={(e) => setNewCommentContent(e.target.value)}
                            rows={2}
                            style={{ resize: 'none', borderRadius: 8 }}
                            onKeyDown={(e) => {
                              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                                handleAddComment()
                              }
                            }}
                          />
                          <Flex justify="flex-end" style={{ marginTop: 6 }}>
                            <Button
                              type="primary"
                              size="small"
                              icon={<SendOutlined />}
                              loading={isSubmittingComment}
                              onClick={handleAddComment}
                              style={{ backgroundColor: '#6366f1' }}
                            >
                              Gửi bình luận
                            </Button>
                          </Flex>
                        </div>
                      </Flex>
                    </Flex>
                  ),
                },
              ]}
            />
          </Col>

          {/* Sidebar Area (Right) */}
          <Col xs={24} md={8} style={{ borderLeft: '1px solid #f1f5f9' }}>
            <Flex vertical gap={14}>
              {/* Column Status */}
              <div>
                <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  Cột trạng thái:
                </Typography.Text>
                <Form.Item name="columnId" noStyle>
                  <Select
                    style={{ width: '100%' }}
                    onChange={handleSaveMetadata}
                    options={columns.map((c) => ({
                      value: c.id,
                      label: (
                        <Space>
                          {c.isDone && <CheckCircleFilled style={{ color: '#10b981' }} />}
                          <span>{c.name}</span>
                        </Space>
                      ),
                    }))}
                  />
                </Form.Item>
              </div>

              {/* Priority */}
              <div>
                <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  <FlagOutlined style={{ marginRight: 4 }} /> Độ ưu tiên:
                </Typography.Text>
                <Form.Item name="priority" noStyle>
                  <Select
                    style={{ width: '100%' }}
                    allowClear
                    placeholder="Chọn mức độ ưu tiên"
                    onChange={handleSaveMetadata}
                    options={[
                      { value: 'Urgent', label: <Tag color="error">Khẩn cấp</Tag> },
                      { value: 'High', label: <Tag color="warning">Cao</Tag> },
                      { value: 'Medium', label: <Tag color="processing">Trung bình</Tag> },
                      { value: 'Low', label: <Tag color="default">Thấp</Tag> },
                    ]}
                  />
                </Form.Item>
              </div>

              {/* Due Date */}
              <div>
                <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  <CalendarOutlined style={{ marginRight: 4 }} /> Hạn chót:
                </Typography.Text>
                <Form.Item name="dueDate" noStyle>
                  <DatePicker
                    style={{ width: '100%' }}
                    format="DD/MM/YYYY"
                    onChange={handleSaveMetadata}
                    placeholder="Chọn ngày hạn chót"
                  />
                </Form.Item>
              </div>

              {/* Assignee */}
              <div>
                <Typography.Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  <UserOutlined style={{ marginRight: 4 }} /> Người thực hiện:
                </Typography.Text>
                <Flex align="center" gap={8} style={{ padding: '6px 10px', background: '#f8fafc', borderRadius: 6, border: '1px solid #e2e8f0' }}>
                  {task.assigneeName ? (
                    <>
                      <Avatar size={24} style={{ backgroundColor: '#6366f1', fontSize: 11 }}>
                        {task.assigneeName[0].toUpperCase()}
                      </Avatar>
                      <Typography.Text strong style={{ fontSize: 13 }}>
                        {task.assigneeName}
                      </Typography.Text>
                    </>
                  ) : (
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      Chưa phân công
                    </Typography.Text>
                  )}
                </Flex>
              </div>

              {/* Status info */}
              {(isDoneColumn || task.completedAt) && (
                <div style={{ padding: '8px 10px', background: '#ecfdf5', borderRadius: 6, border: '1px solid #a7f3d0' }}>
                  <Flex align="center" gap={6}>
                    <CheckCircleFilled style={{ color: '#10b981' }} />
                    <Typography.Text strong style={{ color: '#065f46', fontSize: 12 }}>
                      Đã hoàn thành
                    </Typography.Text>
                  </Flex>
                  {task.completedAt && (
                    <Typography.Text type="secondary" style={{ fontSize: 11, display: 'block', marginTop: 2 }}>
                      Lúc: {dayjs(task.completedAt).format('HH:mm DD/MM/YYYY')}
                    </Typography.Text>
                  )}
                </div>
              )}

              <Divider style={{ margin: '8px 0' }} />

              {/* Meta Timestamps */}
              <div style={{ fontSize: 11, color: '#94a3b8' }}>
                <div>Tạo lúc: {dayjs(task.createdAt).format('HH:mm DD/MM/YYYY')}</div>
                <div>Cập nhật: {dayjs(task.updatedAt).format('HH:mm DD/MM/YYYY')}</div>
              </div>

              {/* Delete Button */}
              <Popconfirm
                title="Xoá thẻ này?"
                description="Hành động này sẽ chuyển thẻ vào thùng rác."
                okText="Xoá thẻ"
                cancelText="Huỷ"
                okButtonProps={{ danger: true }}
                onConfirm={async () => {
                  await onDeleteTask(task.id)
                  onClose()
                }}
              >
                <Button danger block icon={<DeleteOutlined />} style={{ marginTop: 8 }}>
                  Xoá thẻ
                </Button>
              </Popconfirm>
            </Flex>
          </Col>
        </Row>
      </Form>
    </Modal>
  )
}
