import React, { useState } from 'react'
import { Badge, Button } from 'antd'
import { BellOutlined } from '@ant-design/icons'
import { useUnreadCount } from '../hooks/useUnreadCount'
import { NotificationDrawer } from '../../features/ai/components/NotificationDrawer'

interface NotificationBellProps {
  workspaceId?: string
}

export const NotificationBell: React.FC<NotificationBellProps> = ({ workspaceId }) => {
  const [drawerOpen, setDrawerOpen] = useState(false)
  const { unreadCount, refresh } = useUnreadCount()

  const handleClose = () => {
    setDrawerOpen(false)
    refresh()
  }

  return (
    <>
      <Badge count={unreadCount} size="small" offset={[-2, 4]} overflowCount={99}>
        <Button
          type="text"
          shape="circle"
          icon={<BellOutlined style={{ color: '#475569', fontSize: 18 }} />}
          onClick={() => setDrawerOpen(true)}
          data-testid="notification-bell"
          aria-label="Thông báo"
          style={{ display: 'flex', alignItems: 'center', justifyContent: 'center' }}
        />
      </Badge>

      <NotificationDrawer
        open={drawerOpen}
        onClose={handleClose}
        workspaceId={workspaceId}
      />
    </>
  )
}
