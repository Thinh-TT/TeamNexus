import React from 'react'
import {
  AppstoreOutlined,
  CheckSquareOutlined,
  InfoCircleOutlined,
  MessageOutlined,
} from '@ant-design/icons'
import { Avatar, Flex, Space, Tag, Typography } from 'antd'
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
  isLast?: boolean
}

function getEntityIcon(entityType: string) {
  switch (entityType?.toLowerCase()) {
    case 'task':
      return <CheckSquareOutlined style={{ color: '#4f46e5', fontSize: 12 }} />
    case 'comment':
      return <MessageOutlined style={{ color: '#0284c7', fontSize: 12 }} />
    case 'workspace':
      return <AppstoreOutlined style={{ color: '#7c3aed', fontSize: 12 }} />
    default:
      return <InfoCircleOutlined style={{ color: '#64748b', fontSize: 12 }} />
  }
}

export const ActivityFeedItem: React.FC<ActivityFeedItemProps> = ({
  item,
  boardName,
  isLast = false,
}) => {
  const actorName = getActivityActorName(item)
  const actionLabel = getActivityActionLabel(item.action)
  const summary = getActivityPayloadSummary(item)
  const formattedTime = dayjs(item.createdAt).format('DD/MM/YYYY HH:mm')
  const relative = dayjs(item.createdAt).fromNow()

  return (
    <div
      style={{
        position: 'relative',
        display: 'flex',
        gap: 14,
        paddingBottom: isLast ? 4 : 18,
      }}
    >
      {/* Vertical timeline connector */}
      {!isLast && (
        <div
          style={{
            position: 'absolute',
            left: 15,
            top: 32,
            bottom: 0,
            width: 2,
            backgroundColor: '#e2e8f0',
          }}
        />
      )}

      {/* Timeline node: Actor Avatar + Semantic Entity Icon badge */}
      <div style={{ position: 'relative', flexShrink: 0 }}>
        <Avatar
          size={32}
          style={{
            backgroundColor: item.userId ? '#eef2ff' : '#f1f5f9',
            color: item.userId ? '#4f46e5' : '#64748b',
            fontWeight: 700,
            fontSize: 12.5,
            border: '1px solid #c7d2fe',
          }}
        >
          {actorName[0]?.toUpperCase() || 'H'}
        </Avatar>
        <span
          style={{
            position: 'absolute',
            right: -3,
            bottom: -2,
            width: 18,
            height: 18,
            borderRadius: '50%',
            backgroundColor: '#ffffff',
            border: '1px solid #e2e8f0',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            boxShadow: '0 1px 2px rgba(15, 23, 42, 0.06)',
          }}
        >
          {getEntityIcon(item.entityType)}
        </span>
      </div>

      {/* Timeline content */}
      <div
        style={{
          flex: 1,
          minWidth: 0,
          padding: '4px 0 6px 0',
          borderBottom: isLast ? 'none' : '1px dashed #f1f5f9',
        }}
      >
        <Flex align="center" justify="space-between" wrap="wrap" gap={8}>
          <Space size={6} wrap>
            <Typography.Text strong style={{ color: '#0f172a', fontSize: 13.5 }}>
              {actorName}
            </Typography.Text>
            <Typography.Text style={{ color: '#334155', fontSize: 13.5 }}>
              {actionLabel}
            </Typography.Text>
            {summary && (
              <Typography.Text
                type="secondary"
                style={{ fontStyle: 'italic', fontSize: 13 }}
              >
                ({summary})
              </Typography.Text>
            )}
            {boardName && (
              <Tag
                style={{
                  margin: 0,
                  fontSize: 11,
                  borderRadius: 6,
                  fontWeight: 500,
                }}
                color="blue"
              >
                {boardName}
              </Tag>
            )}
          </Space>

          <Typography.Text
            type="secondary"
            style={{
              fontSize: 11.5,
              whiteSpace: 'nowrap',
              fontFamily: 'var(--tn-font-mono, monospace)',
            }}
            title={formattedTime}
          >
            {relative} · {formattedTime}
          </Typography.Text>
        </Flex>
      </div>
    </div>
  )
}
