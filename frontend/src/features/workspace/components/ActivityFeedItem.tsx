import React from 'react'
import {
  AppstoreOutlined,
  CheckSquareOutlined,
  InfoCircleOutlined,
  MessageOutlined,
} from '@ant-design/icons'
import { Card, Flex, Space, Tag, Typography } from 'antd'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import type { WorkspaceActivityItem } from '../types/workspace.types'
import {
  getActivityActionLabel,
  getActivityActorName,
  getActivityPayloadSummary,
} from '../utils/activityLabels'

dayjs.extend(relativeTime)

interface ActivityFeedItemProps {
  item: WorkspaceActivityItem
  boardName?: string
}

function getEntityIcon(entityType: string) {
  switch (entityType?.toLowerCase()) {
    case 'task':
      return <CheckSquareOutlined style={{ color: '#6366f1' }} />
    case 'comment':
      return <MessageOutlined style={{ color: '#0ea5e9' }} />
    case 'workspace':
      return <AppstoreOutlined style={{ color: '#8b5cf6' }} />
    default:
      return <InfoCircleOutlined style={{ color: '#94a3b8' }} />
  }
}

export const ActivityFeedItem: React.FC<ActivityFeedItemProps> = ({ item, boardName }) => {
  const actorName = getActivityActorName(item)
  const actionLabel = getActivityActionLabel(item.action)
  const summary = getActivityPayloadSummary(item)
  const formattedTime = dayjs(item.createdAt).format('DD/MM/YYYY HH:mm')
  const relative = dayjs(item.createdAt).fromNow()

  return (
    <Card
      size="small"
      style={{
        borderRadius: 8,
        border: '1px solid #e2e8f0',
        backgroundColor: '#ffffff',
        marginBottom: 8,
      }}
      bodyStyle={{ padding: '12px 16px' }}
    >
      <Flex justify="space-between" align="flex-start" wrap="wrap" gap={8}>
        <Space size={8} align="start">
          <div style={{ fontSize: 16, marginTop: 2 }}>{getEntityIcon(item.entityType)}</div>
          <div>
            <Typography.Text strong style={{ color: '#0f172a', marginRight: 4 }}>
              {actorName}
            </Typography.Text>
            <Typography.Text style={{ color: '#334155' }}>{actionLabel}</Typography.Text>
            {summary && (
              <Typography.Text type="secondary" style={{ marginLeft: 6, fontStyle: 'italic' }}>
                ({summary})
              </Typography.Text>
            )}
            {boardName && (
              <Tag style={{ marginLeft: 8, fontSize: 11 }} color="blue">
                {boardName}
              </Tag>
            )}
          </div>
        </Space>

        <Typography.Text
          type="secondary"
          style={{ fontSize: 12, whiteSpace: 'nowrap' }}
          title={formattedTime}
        >
          {relative}
        </Typography.Text>
      </Flex>
    </Card>
  )
}
