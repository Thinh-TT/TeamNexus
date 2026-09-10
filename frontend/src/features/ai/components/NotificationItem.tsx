import React, { useState } from 'react'
import {
  Badge,
  Button,
  Card,
  Flex,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import {
  CheckOutlined,
  DownOutlined,
  ExportOutlined,
  UpOutlined,
} from '@ant-design/icons'
import dayjs from 'dayjs'
import { useNavigate } from 'react-router-dom'
import type {
  NotificationResponse,
  NotificationSeverity,
  NotificationType,
} from '../types/notification.types'

interface NotificationItemProps {
  notification: NotificationResponse
  onMarkRead: (id: string) => Promise<void>
  workspaceId: string
  marking?: boolean
}

const getSeverityTagColor = (severity?: NotificationSeverity | string): string => {
  switch (severity?.toLowerCase()) {
    case 'critical':
      return 'error'
    case 'high':
      return 'warning'
    case 'medium':
      return 'processing'
    case 'low':
    default:
      return 'default'
  }
}

const getTypeText = (type: NotificationType | string): string => {
  switch (type) {
    case 'OverdueTask':
      return 'Quá hạn'
    case 'StalledTask':
      return 'Đình trệ'
    case 'Overload':
      return 'Quá tải'
    case 'Bottleneck':
      return 'Nghẽn việc'
    default:
      return type
  }
}

export const NotificationItem: React.FC<NotificationItemProps> = ({
  notification,
  onMarkRead,
  workspaceId,
  marking,
}) => {
  const navigate = useNavigate()
  const [expanded, setExpanded] = useState(false)

  const payload = notification.payload
  const severity = payload?.severity ?? 'Medium'
  const boardId = payload?.boardId

  const handleOpenBoard = () => {
    if (boardId) {
      navigate(`/workspaces/${workspaceId}/boards/${boardId}`)
    }
  }

  return (
    <Card
      size="small"
      style={{
        marginBottom: 10,
        borderRadius: 8,
        borderColor: notification.isRead ? '#e2e8f0' : '#93c5fd',
        backgroundColor: notification.isRead ? '#ffffff' : '#f0f9ff',
        boxShadow: notification.isRead
          ? 'none'
          : '0 1px 3px rgba(59, 130, 246, 0.08)',
        transition: 'all 0.2s ease',
      }}
      styles={{
        body: { padding: '12px 14px' },
      }}
    >
      <Flex vertical gap={6}>
        {/* Header: Status, Type Tag, Severity Tag, and Action */}
        <Flex justify="space-between" align="center" wrap="wrap" gap={6}>
          <Flex align="center" gap={6} wrap="wrap">
            {!notification.isRead && (
              <Badge status="processing" color="#3b82f6" text="" />
            )}
            <Tag
              color={getSeverityTagColor(severity)}
              style={{ fontWeight: 600, margin: 0 }}
            >
              {severity.toUpperCase()}
            </Tag>
            <Tag color="geekblue" style={{ margin: 0 }}>
              {getTypeText(notification.type)}
            </Tag>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>
              {dayjs(notification.createdAt).format('HH:mm DD/MM/YYYY')}
            </Typography.Text>
          </Flex>

          <Flex align="center" gap={4}>
            {!notification.isRead && (
              <Tooltip title="Đánh dấu đã đọc">
                <Button
                  size="small"
                  type="text"
                  icon={<CheckOutlined style={{ color: '#16a34a' }} />}
                  loading={marking}
                  onClick={() => onMarkRead(notification.id)}
                  style={{ borderRadius: 6 }}
                >
                  Đã đọc
                </Button>
              </Tooltip>
            )}
            <Button
              size="small"
              type="text"
              icon={expanded ? <UpOutlined /> : <DownOutlined />}
              onClick={() => setExpanded(!expanded)}
              style={{ fontSize: 11, color: '#64748b' }}
            />
          </Flex>
        </Flex>

        {/* Title */}
        <Typography.Text
          strong
          style={{
            fontSize: 13.5,
            color: '#0f172a',
            lineHeight: 1.4,
          }}
        >
          {notification.title}
        </Typography.Text>

        {/* Details & Actions */}
        {expanded && (
          <div
            style={{
              marginTop: 4,
              paddingTop: 8,
              borderTop: '1px dashed #cbd5e1',
            }}
          >
            <Typography.Paragraph
              style={{
                fontSize: 12.5,
                color: '#334155',
                whiteSpace: 'pre-wrap',
                marginBottom: 8,
                lineHeight: 1.5,
              }}
            >
              {notification.message}
            </Typography.Paragraph>

            <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
              {boardId ? (
                <Button
                  size="small"
                  type="link"
                  icon={<ExportOutlined />}
                  onClick={handleOpenBoard}
                  style={{ padding: 0, fontSize: 12 }}
                >
                  Mở bảng Kanban liên quan
                </Button>
              ) : (
                <div />
              )}

              {notification.readAt && (
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  Đã đọc: {dayjs(notification.readAt).format('HH:mm DD/MM/YYYY')}
                </Typography.Text>
              )}
            </Flex>
          </div>
        )}
      </Flex>
    </Card>
  )
}
