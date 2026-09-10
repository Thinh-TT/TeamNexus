import React from 'react'
import { Card, Flex, Space, Tag, Tooltip, Typography } from 'antd'
import type { ObserverRunFinding } from '../types/notification.types'

interface ObserverFindingCardProps {
  finding: ObserverRunFinding
}

const getSeverityTagColor = (severity?: string): string => {
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

const formatShortId = (id: string): string => {
  if (!id) return ''
  return id.length > 8 ? `...${id.slice(-6)}` : id
}

export const ObserverFindingCard: React.FC<ObserverFindingCardProps> = ({
  finding,
}) => {
  return (
    <Card
      size="small"
      style={{
        marginBottom: 10,
        borderRadius: 8,
        borderColor: '#e2e8f0',
        backgroundColor: '#ffffff',
      }}
      styles={{
        body: { padding: '12px' },
      }}
    >
      <Flex vertical gap={6}>
        <Flex align="center" gap={6} wrap="wrap">
          <Tag
            color={getSeverityTagColor(finding.severity)}
            style={{ fontWeight: 600, margin: 0 }}
          >
            {finding.severity.toUpperCase()}
          </Tag>
          <Tag color="blue" style={{ margin: 0 }}>
            {finding.type}
          </Tag>
        </Flex>

        <Typography.Text strong style={{ fontSize: 13, color: '#0f172a' }}>
          {finding.title}
        </Typography.Text>

        <Typography.Paragraph
          style={{
            fontSize: 12,
            color: '#475569',
            marginBottom: 6,
            whiteSpace: 'pre-wrap',
            lineHeight: 1.4,
          }}
        >
          {finding.message}
        </Typography.Paragraph>

        {/* Evidence IDs */}
        {(finding.taskIds?.length > 0 || finding.userIds?.length > 0) && (
          <Flex vertical gap={4} style={{ marginTop: 2 }}>
            {finding.taskIds?.length > 0 && (
              <Flex align="center" gap={6} wrap="wrap">
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  Tasks:
                </Typography.Text>
                <Space orientation="horizontal" size={[0, 4]} wrap>
                  {finding.taskIds.map((tid) => (
                    <Tooltip key={tid} title={`Task ID: ${tid}`}>
                      <Tag style={{ fontSize: 10.5, margin: 0, padding: '0 4px' }}>
                        {formatShortId(tid)}
                      </Tag>
                    </Tooltip>
                  ))}
                </Space>
              </Flex>
            )}

            {finding.userIds?.length > 0 && (
              <Flex align="center" gap={6} wrap="wrap">
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  Users:
                </Typography.Text>
                <Space orientation="horizontal" size={[0, 4]} wrap>
                  {finding.userIds.map((uid) => (
                    <Tooltip key={uid} title={`User ID: ${uid}`}>
                      <Tag color="cyan" style={{ fontSize: 10.5, margin: 0, padding: '0 4px' }}>
                        {formatShortId(uid)}
                      </Tag>
                    </Tooltip>
                  ))}
                </Space>
              </Flex>
            )}
          </Flex>
        )}
      </Flex>
    </Card>
  )
}
