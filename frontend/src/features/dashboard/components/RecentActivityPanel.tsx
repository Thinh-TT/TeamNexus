import React from 'react'
import { HistoryOutlined } from '@ant-design/icons'
import { Avatar, Button, Card, Empty, Flex, List, Typography } from 'antd'
import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import 'dayjs/locale/vi'
import { useNavigate } from 'react-router-dom'
import { getActivityActionLabel } from '../../workspace/utils/activityLabels'
import type { DashboardActivityItem } from '../types/dashboard.types'

dayjs.extend(relativeTime)
dayjs.locale('vi')

interface RecentActivityPanelProps {
  workspaceId: string
  activities: DashboardActivityItem[]
}

export const RecentActivityPanel: React.FC<RecentActivityPanelProps> = ({
  workspaceId,
  activities,
}) => {
  const navigate = useNavigate()

  return (
    <Card
      title={
        <Flex justify="space-between" align="center">
          <span>Hoạt động gần đây</span>
          <Button
            type="link"
            size="small"
            icon={<HistoryOutlined />}
            onClick={() => navigate(`/workspaces/${workspaceId}/activity`)}
            style={{ padding: 0 }}
          >
            Xem tất cả
          </Button>
        </Flex>
      }
      style={{ borderRadius: 12, border: '1px solid #e2e8f0', height: '100%' }}
      styles={{ body: { padding: '8px 16px' } }}
    >
      {activities.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="Chưa có hoạt động nào trong workspace"
          style={{ margin: '24px 0' }}
        />
      ) : (
        <List
          itemLayout="horizontal"
          dataSource={activities}
          renderItem={(item) => {
            const actor = item.authorName || 'Hệ thống'
            const actionLabel = getActivityActionLabel(item.action)

            return (
              <List.Item
                style={{
                  padding: '10px 0',
                  borderBottom: '1px solid #f8fafc',
                }}
              >
                <List.Item.Meta
                  avatar={
                    <Avatar
                      size={28}
                      style={{
                        backgroundColor: item.authorName ? '#6366f1' : '#94a3b8',
                        fontSize: 12,
                      }}
                    >
                      {actor[0]?.toUpperCase() ?? 'H'}
                    </Avatar>
                  }
                  title={
                    <Typography.Text style={{ fontSize: 13 }}>
                      <strong>{actor}</strong> {actionLabel}
                    </Typography.Text>
                  }
                  description={
                    <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                      {dayjs(item.createdAt).fromNow()}
                    </Typography.Text>
                  }
                />
              </List.Item>
            )
          }}
        />
      )}
    </Card>
  )
}
