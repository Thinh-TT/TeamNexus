import React, { useEffect, useState } from 'react'
import { AlertOutlined, RobotOutlined } from '@ant-design/icons'
import { Alert, Button, Card, Empty, Flex, Spin, Typography } from 'antd'
import { notificationApi } from '../../ai/services/notificationApi'
import { NotificationDrawer } from '../../ai/components/NotificationDrawer'
import type { NotificationResponse } from '../../ai/types/notification.types'

interface ObserverAlertsPanelProps {
  workspaceId: string
}

export const ObserverAlertsPanel: React.FC<ObserverAlertsPanelProps> = ({ workspaceId }) => {
  const [alerts, setAlerts] = useState<NotificationResponse[]>([])
  const [unreadCount, setUnreadCount] = useState<number>(0)
  const [loading, setLoading] = useState(true)
  const [drawerOpen, setDrawerOpen] = useState(false)

  const fetchAlerts = async () => {
    setLoading(true)
    try {
      const res = await notificationApi.listNotifications({
        isRead: false,
        kind: 'observer',
        take: 10,
      })
      setAlerts(res.items)
      setUnreadCount(res.unreadCount)
    } catch {
      // Fail-soft: không ném, để trống
      setAlerts([])
      setUnreadCount(0)
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    Promise.resolve().then(() => {
      fetchAlerts()
    })
  }, [workspaceId])

  return (
    <>
      <Card
        title={
          <Flex align="center" gap={8}>
            <RobotOutlined style={{ color: '#6366f1' }} />
            <span>Cảnh báo AI Observer</span>
          </Flex>
        }
        style={{ borderRadius: 12, border: '1px solid #e2e8f0', height: '100%' }}
        styles={{ body: { padding: '16px 20px' } }}
      >
        {loading ? (
          <Flex justify="center" align="center" style={{ padding: '24px 0' }}>
            <Spin size="small" />
          </Flex>
        ) : unreadCount > 0 ? (
          <Alert
            message={
              <Typography.Text strong>
                Có {unreadCount} cảnh báo AI Observer chưa đọc
              </Typography.Text>
            }
            description={
              <Flex vertical gap={8} style={{ marginTop: 6 }}>
                <div>
                  {alerts.slice(0, 3).map((item) => (
                    <div key={item.id} style={{ fontSize: 13, marginBottom: 4 }}>
                      • <strong>{item.title}</strong>: {item.message}
                    </div>
                  ))}
                  {unreadCount > 3 && (
                    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                      và {unreadCount - 3} cảnh báo khác...
                    </Typography.Text>
                  )}
                </div>
                <div>
                  <Button
                    type="primary"
                    size="small"
                    icon={<AlertOutlined />}
                    onClick={() => setDrawerOpen(true)}
                    style={{ backgroundColor: '#6366f1', marginTop: 4 }}
                  >
                    Xem chi tiết
                  </Button>
                </div>
              </Flex>
            }
            type="warning"
            showIcon
            style={{ borderRadius: 8 }}
          />
        ) : (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Không có cảnh báo AI Observer chưa đọc"
            style={{ margin: '16px 0' }}
          />
        )}
      </Card>

      <NotificationDrawer
        open={drawerOpen}
        onClose={() => {
          setDrawerOpen(false)
          fetchAlerts()
        }}
        workspaceId={workspaceId}
      />
    </>
  )
}
