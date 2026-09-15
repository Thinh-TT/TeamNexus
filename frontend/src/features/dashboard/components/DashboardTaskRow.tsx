import React from 'react'
import { CalendarOutlined, CheckCircleOutlined, FolderOutlined } from '@ant-design/icons'
import { Flex, List, Space, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import { useNavigate } from 'react-router-dom'
import type { DashboardTaskItem } from '../types/dashboard.types'

interface DashboardTaskRowProps {
  workspaceId: string
  task: DashboardTaskItem
}

const PRIORITY_MAP: Record<string, { label: string; color: string }> = {
  Urgent: { label: 'Khẩn cấp', color: 'error' },
  High: { label: 'Cao', color: 'warning' },
  Medium: { label: 'Trung bình', color: 'processing' },
  Low: { label: 'Thấp', color: 'default' },
}

export const DashboardTaskRow: React.FC<DashboardTaskRowProps> = ({ workspaceId, task }) => {
  const navigate = useNavigate()

  const handleClick = () => {
    navigate(`/workspaces/${workspaceId}/boards/${task.boardId}`)
  }

  // Nhãn hạn chót
  const renderDueTag = () => {
    if (task.isDone) {
      return (
        <Tag color="success" icon={<CheckCircleOutlined />}>
          Đã xong
        </Tag>
      )
    }

    if (task.overdueByDays !== null && task.overdueByDays >= 1) {
      return (
        <Tag color="error" icon={<CalendarOutlined />}>
          Quá hạn {task.overdueByDays} ngày
        </Tag>
      )
    }

    if (task.dueDate) {
      const isToday = dayjs(task.dueDate).startOf('day').isSame(dayjs().startOf('day'))
      const label = isToday ? 'Hôm nay' : dayjs(task.dueDate).format('DD/MM')
      return (
        <Tag color={isToday ? 'warning' : 'default'} icon={<CalendarOutlined />}>
          {label}
        </Tag>
      )
    }

    return null
  }

  const priorityConfig = task.priority ? PRIORITY_MAP[task.priority] : null

  return (
    <List.Item
      onClick={handleClick}
      style={{
        padding: '12px 16px',
        cursor: 'pointer',
        borderRadius: 8,
        transition: 'background 0.2s',
      }}
      className="dashboard-task-row"
    >
      <Flex justify="space-between" align="center" style={{ width: '100%' }} wrap="wrap" gap={8}>
        <Flex vertical gap={4} style={{ flex: 1, minWidth: 200 }}>
          <Typography.Text strong style={{ fontSize: 14, color: '#0f172a' }}>
            {task.title}
          </Typography.Text>
          <Space orientation="horizontal" size={6} wrap>
            <Tag icon={<FolderOutlined />} style={{ borderRadius: 4, margin: 0 }}>
              {task.boardName}
            </Tag>
            <Tag style={{ borderRadius: 4, margin: 0, background: '#f1f5f9', color: '#475569' }}>
              {task.columnName}
            </Tag>
          </Space>
        </Flex>

        <Space size={6} wrap>
          {priorityConfig && (
            <Tag color={priorityConfig.color} style={{ borderRadius: 4, margin: 0 }}>
              {priorityConfig.label}
            </Tag>
          )}
          {renderDueTag()}
        </Space>
      </Flex>
    </List.Item>
  )
}
