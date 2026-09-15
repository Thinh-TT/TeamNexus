import React from 'react'
import {
  CalendarOutlined,
  CheckCircleOutlined,
  CommentOutlined,
  FolderOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Button,
  Card,
  Empty,
  Flex,
  List,
  Space,
  Spin,
  Tag,
  Typography,
} from 'antd'
import { useNavigate } from 'react-router-dom'
import { dueDateLabel, isOverdue } from '../../board/utils/taskDueDate'
import type { TaskSearchItem } from '../types/search.types'

interface TaskSearchResultListProps {
  workspaceId: string
  items: TaskSearchItem[]
  loading: boolean
  loadingMore: boolean
  hasMore: boolean
  hasQuery: boolean
  onLoadMore: () => void
}

const PRIORITY_MAP: Record<string, { label: string; color: string }> = {
  Urgent: { label: 'Khẩn cấp', color: 'error' },
  High: { label: 'Cao', color: 'warning' },
  Medium: { label: 'Trung bình', color: 'processing' },
  Low: { label: 'Thấp', color: 'default' },
}

export const TaskSearchResultList: React.FC<TaskSearchResultListProps> = ({
  workspaceId,
  items,
  loading,
  loadingMore,
  hasMore,
  hasQuery,
  onLoadMore,
}) => {
  const navigate = useNavigate()

  if (loading && items.length === 0) {
    return (
      <Flex align="center" justify="center" style={{ height: 300 }}>
        <Spin size="large" tip="Đang tìm kiếm thẻ..." />
      </Flex>
    )
  }

  if (items.length === 0) {
    return (
      <Card style={{ borderRadius: 10, padding: '32px 0', textAlign: 'center' }}>
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            hasQuery
              ? 'Không tìm thấy thẻ nào phù hợp với điều kiện tìm kiếm'
              : 'Nhập từ khoá hoặc điều chỉnh bộ lọc để bắt đầu tìm kiếm thẻ'
          }
        />
      </Card>
    )
  }

  return (
    <Flex vertical gap={16}>
      <Flex justify="space-between" align="center">
        <Typography.Text type="secondary" style={{ fontSize: 13 }}>
          Hiển thị <strong>{items.length}</strong> kết quả
        </Typography.Text>
      </Flex>

      <List
        dataSource={items}
        renderItem={(item) => {
          const { task, boardName, columnName, isDoneColumn } = item
          const priorityConfig = task.priority ? PRIORITY_MAP[task.priority] : null
          const isDone = isDoneColumn || Boolean(task.completedAt)
          const dueLabel = dueDateLabel(task, new Date(), isDone)
          const overdue = isOverdue(task, new Date(), isDone)

          return (
            <List.Item
              key={task.id}
              onClick={() => navigate(`/workspaces/${workspaceId}/boards/${task.boardId}`)}
              style={{
                background: '#ffffff',
                padding: '16px 20px',
                borderRadius: 10,
                border: '1px solid #e2e8f0',
                marginBottom: 10,
                cursor: 'pointer',
                transition: 'all 0.2s',
              }}
              className="task-search-item"
            >
              <Flex vertical gap={8} style={{ width: '100%' }}>
                {/* Header: Title + Priority + Done tag */}
                <Flex justify="space-between" align="flex-start" wrap="wrap" gap={8}>
                  <Typography.Text strong style={{ fontSize: 15, color: '#0f172a', flex: 1 }}>
                    {task.title}
                  </Typography.Text>

                  <Space size={6} wrap>
                    {isDone && (
                      <Tag color="success" icon={<CheckCircleOutlined />}>
                        Đã xong
                      </Tag>
                    )}
                    {priorityConfig && (
                      <Tag color={priorityConfig.color}>{priorityConfig.label}</Tag>
                    )}
                  </Space>
                </Flex>

                {/* Description snippet if any */}
                {task.description && (
                  <Typography.Paragraph
                    type="secondary"
                    ellipsis={{ rows: 2 }}
                    style={{ margin: 0, fontSize: 13 }}
                  >
                    {task.description}
                  </Typography.Paragraph>
                )}

                {/* Meta details: Board, Column, Assignee, Due date, Labels */}
                <Flex justify="space-between" align="center" wrap="wrap" gap={8} style={{ marginTop: 4 }}>
                  <Space size={8} wrap>
                    <Tag icon={<FolderOutlined />} style={{ borderRadius: 4, margin: 0 }}>
                      {boardName}
                    </Tag>
                    <Tag style={{ borderRadius: 4, margin: 0, background: '#f1f5f9', color: '#475569' }}>
                      {columnName}
                    </Tag>

                    {task.assigneeName ? (
                      <Space size={4}>
                        <Avatar size={20} style={{ backgroundColor: '#6366f1', fontSize: 10 }}>
                          {task.assigneeName[0]?.toUpperCase()}
                        </Avatar>
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {task.assigneeName}
                        </Typography.Text>
                      </Space>
                    ) : (
                      <Space size={4}>
                        <Avatar size={20} icon={<UserOutlined />} style={{ backgroundColor: '#cbd5e1' }} />
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          Chưa gán
                        </Typography.Text>
                      </Space>
                    )}

                    {dueLabel && (
                      <Tag
                        color={isDone ? 'default' : overdue ? 'error' : 'default'}
                        icon={<CalendarOutlined />}
                        style={{ margin: 0 }}
                      >
                        {dueLabel}
                      </Tag>
                    )}

                    {task.commentCount > 0 && (
                      <Space size={3}>
                        <CommentOutlined style={{ color: '#94a3b8', fontSize: 13 }} />
                        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                          {task.commentCount}
                        </Typography.Text>
                      </Space>
                    )}
                  </Space>

                  {task.labels && task.labels.length > 0 && (
                    <Flex wrap="wrap" gap={4}>
                      {task.labels.map((l) => (
                        <Tag
                          key={l.id}
                          style={{
                            margin: 0,
                            padding: '1px 6px',
                            fontSize: 11,
                            backgroundColor: `${l.color}15`,
                            borderColor: `${l.color}40`,
                            color: l.color,
                          }}
                        >
                          {l.name}
                        </Tag>
                      ))}
                    </Flex>
                  )}
                </Flex>
              </Flex>
            </List.Item>
          )
        }}
      />

      {hasMore && (
        <Flex justify="center" style={{ margin: '16px 0' }}>
          <Button
            type="default"
            size="large"
            loading={loadingMore}
            onClick={onLoadMore}
            data-testid="search-load-more-btn"
            style={{ minWidth: 180, borderRadius: 8 }}
          >
            {loadingMore ? 'Đang tải thêm...' : 'Tải thêm kết quả'}
          </Button>
        </Flex>
      )}
    </Flex>
  )
}
