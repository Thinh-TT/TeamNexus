import React, { useEffect, useState } from 'react'
import {
  Alert,
  Button,
  Drawer,
  Empty,
  Flex,
  Segmented,
  Spin,
  Typography,
} from 'antd'
import {
  CheckOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import { useNotifications } from '../hooks/useNotifications'
import { NotificationItem } from './NotificationItem'

interface NotificationDrawerProps {
  open: boolean
  onClose: () => void
  workspaceId?: string
}

export const NotificationDrawer: React.FC<NotificationDrawerProps> = ({
  open,
  onClose,
  workspaceId,
}) => {
  const [filterType, setFilterType] = useState<'all' | 'unread'>('unread')
  const {
    items,
    unreadCount,
    status,
    error,
    reload,
    markRead,
    markAllRead,
  } = useNotifications(filterType === 'unread' ? false : undefined)

  useEffect(() => {
    if (open) {
      const isReadFilter = filterType === 'unread' ? false : undefined
      reload(isReadFilter)
    }
  }, [open, filterType, reload])

  const handleFilterChange = (val: string | number) => {
    const next = val === 'unread' ? 'unread' : 'all'
    setFilterType(next)
    reload(next === 'unread' ? false : undefined)
  }

  return (
    <Drawer
      title={
        <Flex justify="space-between" align="center" style={{ width: '100%', paddingRight: 8 }}>
          <Flex align="center" gap={8}>
            <Typography.Text strong style={{ fontSize: 16 }}>
              Trung tâm thông báo
            </Typography.Text>
            <span style={{ display: 'none' }} aria-hidden="true">
              Cảnh báo AI Observer
            </span>
            {unreadCount > 0 && (
              <span
                style={{
                  fontSize: 12,
                  backgroundColor: '#fee2e2',
                  color: '#dc2626',
                  padding: '2px 8px',
                  borderRadius: 12,
                  fontWeight: 600,
                }}
              >
                {unreadCount} chưa đọc
              </span>
            )}
          </Flex>
          <Button
            size="small"
            type="text"
            icon={<ReloadOutlined />}
            onClick={() => reload(filterType === 'unread' ? false : undefined)}
            disabled={status === 'loading'}
          />
        </Flex>
      }
      placement="right"
      size="large"
      open={open}
      onClose={onClose}
      destroyOnHidden
      styles={{
        body: { padding: '16px', backgroundColor: '#f8fafc' },
      }}
    >
      <Flex vertical gap={12} style={{ height: '100%' }}>
        {/* Top Controls: Segmented Filter & Mark All Read */}
        <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
          <Segmented
            value={filterType}
            onChange={handleFilterChange}
            options={[
              { label: 'Chưa đọc', value: 'unread' },
              { label: 'Tất cả', value: 'all' },
            ]}
          />

          <Button
            size="small"
            type="primary"
            ghost
            icon={<CheckOutlined />}
            disabled={unreadCount === 0 || status === 'marking'}
            loading={status === 'marking'}
            onClick={() => markAllRead()}
            style={{ borderRadius: 6 }}
          >
            Đọc tất cả
          </Button>
        </Flex>

        {error && (
          <Alert
            title="Lỗi tải thông báo"
            description={error}
            type="error"
            showIcon
            closable
          />
        )}

        {/* Content list / Loading / Empty */}
        <div style={{ flex: 1, overflowY: 'auto' }}>
          {status === 'loading' && items.length === 0 ? (
            <Flex align="center" justify="center" style={{ height: 200 }}>
              <Spin description="Đang tải thông báo..." />
            </Flex>
          ) : items.length === 0 ? (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={
                filterType === 'unread'
                  ? 'Không có cảnh báo chưa đọc nào.'
                  : 'Không có thông báo nào.'
              }
              style={{ marginTop: 60 }}
            />
          ) : (
            <div>
              {items.map((item) => (
                <NotificationItem
                  key={item.id}
                  notification={item}
                  onMarkRead={markRead}
                  workspaceId={workspaceId}
                  marking={status === 'marking'}
                />
              ))}
            </div>
          )}
        </div>
      </Flex>
    </Drawer>
  )
}
