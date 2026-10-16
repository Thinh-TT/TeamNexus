import React from 'react'
import { Avatar, Button, Dropdown, Flex, Layout, Space, Typography } from 'antd'
import type { MenuProps } from 'antd'
import { DownOutlined, LogoutOutlined, UserOutlined } from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../features/auth/store/useAuthStore'
import { NotificationBell } from './NotificationBell'

import { NexusLogo } from './NexusLogo'

const { Header } = Layout

export interface AppHeaderProps {
  title?: React.ReactNode
  showNotifications?: boolean
  children?: React.ReactNode
  workspaceId?: string
}

export const AppHeader: React.FC<AppHeaderProps> = ({
  title,
  showNotifications = true,
  children,
  workspaceId,
}) => {
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()

  const menuItems: MenuProps['items'] = [
    {
      key: 'profile',
      icon: <UserOutlined />,
      label: 'Hồ sơ cá nhân',
      onClick: () => navigate('/profile'),
    },
    {
      type: 'divider',
    },
    {
      key: 'logout',
      icon: <LogoutOutlined />,
      danger: true,
      label: 'Đăng xuất',
      onClick: () => logout(),
    },
  ]

  return (
    <Header
      style={{
        background: '#ffffff',
        borderBottom: '1px solid #e2e8f0',
        padding: '0 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.04)',
      }}
      data-testid="app-header"
    >
      <Flex align="center" gap={12}>
        <NexusLogo size={32} style={{ cursor: 'pointer' }} />
        {title ? (
          typeof title === 'string' ? (
            <Typography.Title
              level={3}
              style={{ color: '#0f172a', margin: 0, cursor: 'pointer', fontWeight: 700, letterSpacing: '-0.5px' }}
              onClick={() => navigate('/')}
            >
              {title}
            </Typography.Title>
          ) : (
            title
          )
        ) : (
          <Typography.Title
            level={3}
            style={{ color: '#0f172a', margin: 0, cursor: 'pointer', fontWeight: 700, letterSpacing: '-0.5px' }}
            onClick={() => navigate('/')}
          >
            TeamNexus
          </Typography.Title>
        )}
      </Flex>

      <Flex align="center" gap={16}>
        {children}

        {showNotifications && (
          <NotificationBell workspaceId={workspaceId} />
        )}

        <Dropdown menu={{ items: menuItems }} placement="bottomRight" arrow trigger={['click']}>
          <Space size="small" style={{ cursor: 'pointer', color: '#0f172a' }} data-testid="user-dropdown-trigger">
            <Avatar src={user?.avatarUrl} style={{ backgroundColor: '#6366f1' }}>
              {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
            </Avatar>
            <Typography.Text style={{ color: '#0f172a', fontWeight: 500 }}>
              {user?.displayName ?? user?.email}
            </Typography.Text>
            <DownOutlined style={{ fontSize: 10, color: '#64748b' }} />
          </Space>
        </Dropdown>

        <Button danger onClick={() => logout()}>
          Đăng xuất
        </Button>
      </Flex>
    </Header>
  )
}
