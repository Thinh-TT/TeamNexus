import React from 'react'
import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  CalendarOutlined,
  CheckCircleFilled,
  MessageOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Avatar, Card, Flex, Space, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import type { TaskPriority, TaskResponse } from '../types/board.types'

interface TaskCardProps {
  task: TaskResponse
  isDoneColumn?: boolean
  onClick?: () => void
  isDragOverlay?: boolean
}

const getPriorityConfig = (priority: TaskPriority | null) => {
  switch (priority) {
    case 'Urgent':
      return { color: 'error', label: 'Khẩn cấp', bg: '#fef2f2', border: '#fecaca' }
    case 'High':
      return { color: 'warning', label: 'Cao', bg: '#fffbeb', border: '#fde68a' }
    case 'Medium':
      return { color: 'processing', label: 'Trung bình', bg: '#eff6ff', border: '#bfdbfe' }
    case 'Low':
      return { color: 'default', label: 'Thấp', bg: '#f8fafc', border: '#e2e8f0' }
    default:
      return null
  }
}

export const TaskCard: React.FC<TaskCardProps> = ({
  task,
  isDoneColumn = false,
  onClick,
  isDragOverlay = false,
}) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: task.id,
    data: {
      type: 'Task',
      task,
    },
    disabled: isDragOverlay,
  })

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition,
    opacity: isDragging ? 0.35 : 1,
    cursor: 'grab',
    userSelect: 'none',
    boxShadow: isDragOverlay
      ? '0 12px 24px -4px rgba(0, 0, 0, 0.15), 0 8px 16px -4px rgba(0, 0, 0, 0.1)'
      : '0 1px 3px rgba(0, 0, 0, 0.06), 0 1px 2px rgba(0, 0, 0, 0.04)',
    border: isDragOverlay ? '1.5px solid #6366f1' : '1px solid #e2e8f0',
    borderRadius: 8,
    background: '#ffffff',
    transformOrigin: '50% 50%',
    rotate: isDragOverlay ? '2deg' : '0deg',
  }

  const priorityConfig = getPriorityConfig(task.priority)
  const isCompleted = isDoneColumn || !!task.completedAt
  const isOverdue =
    task.dueDate && !isCompleted && dayjs(task.dueDate).isBefore(dayjs(), 'day')

  return (
    <div ref={setNodeRef} style={style} {...attributes} {...listeners} onClick={onClick}>
      <Card
        size="small"
        bordered={false}
        style={{ background: 'transparent' }}
        bodyStyle={{ padding: '12px' }}
      >
        <Flex vertical gap={8}>
          {/* Top meta: Priority + Labels */}
          {(priorityConfig || (task.labels && task.labels.length > 0)) && (
            <Flex wrap="wrap" gap={4} align="center">
              {priorityConfig && (
                <Tag
                  color={priorityConfig.color}
                  style={{ margin: 0, fontSize: 11, lineHeight: '18px', borderRadius: 4 }}
                >
                  {priorityConfig.label}
                </Tag>
              )}
              {task.labels?.map((label) => (
                <Tag
                  key={label.id}
                  style={{
                    margin: 0,
                    fontSize: 11,
                    lineHeight: '18px',
                    borderRadius: 4,
                    backgroundColor: `${label.color}15`,
                    borderColor: `${label.color}40`,
                    color: label.color,
                    fontWeight: 500,
                  }}
                >
                  {label.name}
                </Tag>
              ))}
            </Flex>
          )}

          {/* Title */}
          <Flex align="flex-start" gap={6}>
            {isCompleted && (
              <CheckCircleFilled style={{ color: '#10b981', fontSize: 14, marginTop: 3 }} />
            )}
            <Typography.Text
              strong
              delete={isCompleted}
              style={{
                fontSize: 13.5,
                lineHeight: 1.4,
                color: isCompleted ? '#64748b' : '#1e293b',
                flex: 1,
              }}
            >
              {task.title}
            </Typography.Text>
          </Flex>

          {/* Bottom info: Due date, comments, assignee */}
          <Flex justify="space-between" align="center" style={{ marginTop: 2 }}>
            <Space size={10}>
              {task.dueDate && (
                <Tooltip
                  title={`Hạn chót: ${dayjs(task.dueDate).format('DD/MM/YYYY')}${
                    isOverdue ? ' (Quá hạn)' : ''
                  }`}
                >
                  <Space
                    size={3}
                    style={{
                      fontSize: 11.5,
                      color: isOverdue ? '#ef4444' : isCompleted ? '#94a3b8' : '#64748b',
                      fontWeight: isOverdue ? 600 : 400,
                    }}
                  >
                    <CalendarOutlined />
                    <span>{dayjs(task.dueDate).format('DD/MM')}</span>
                  </Space>
                </Tooltip>
              )}

              {task.commentCount > 0 && (
                <Tooltip title={`${task.commentCount} bình luận`}>
                  <Space size={3} style={{ fontSize: 11.5, color: '#64748b' }}>
                    <MessageOutlined />
                    <span>{task.commentCount}</span>
                  </Space>
                </Tooltip>
              )}
            </Space>

            {task.assigneeName ? (
              <Tooltip title={`Người phụ trách: ${task.assigneeName}`}>
                <Avatar
                  size={22}
                  style={{
                    backgroundColor: '#6366f1',
                    fontSize: 10,
                    fontWeight: 600,
                  }}
                >
                  {task.assigneeName[0].toUpperCase()}
                </Avatar>
              </Tooltip>
            ) : (
              <Tooltip title="Chưa phân công">
                <Avatar
                  size={22}
                  icon={<UserOutlined />}
                  style={{ backgroundColor: '#e2e8f0', color: '#94a3b8', fontSize: 10 }}
                />
              </Tooltip>
            )}
          </Flex>
        </Flex>
      </Card>
    </div>
  )
}
