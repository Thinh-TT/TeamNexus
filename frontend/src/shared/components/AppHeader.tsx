import React from 'react'
import { Avatar, Button, Dropdown, Flex, Layout, Space, Tag, Typography } from 'antd'
import type { MenuProps } from 'antd'
import {
  DownOutlined,
  LogoutOutlined,
  SearchOutlined,
  ThunderboltFilled,
  UserOutlined,
} from '@ant-design/icons'
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
        background: 'rgba(255, 255, 255, 0.92)',
        backdropFilter: 'blur(8px)',
        borderBottom: '1px solid #e2e8f0',
        padding: '0 24px',
        height: 58,
        lineHeight: '58px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        boxShadow: '0 1px 2px 0 rgba(15, 23, 42, 0.03)',
        position: 'sticky',
        top: 0,
        zIndex: 20,
      }}
      data-testid="app-header"
    >
      <Flex align="center" gap={12}>
        <NexusLogo size={30} style={{ cursor: 'pointer' }} />
        {title ? (
          typeof title === 'string' ? (
            <Typography.Title
              level={4}
              style={{
                color: '#0f172a',
                margin: 0,
                cursor: 'pointer',
                fontWeight: 700,
                letterSpacing: '-0.02em',
              }}
              onClick={() => navigate('/')}
            >
              {title}
            </Typography.Title>
          ) : (
            title
          )
        ) : (
          <Flex align="center" gap={8}>
            <Typography.Title
              level={4}
              style={{
                color: '#0f172a',
                margin: 0,
                cursor: 'pointer',
                fontWeight: 700,
                letterSpacing: '-0.02em',
              }}
              onClick={() => navigate('/')}
            >
              TeamNexus
            </Typography.Title>
            <Tag
              style={{
                margin: 0,
                borderRadius: 9999,
                fontSize: 11,
                fontWeight: 600,
                padding: '0 8px',
                lineHeight: '20px',
                background: '#f5f3ff',
                color: '#6d28d9',
                borderColor: '#ddd6fe',
              }}
            >
              <ThunderboltFilled style={{ fontSize: 10, marginRight: 4 }} />
              Hybrid AI v4.2
            </Tag>
          </Flex>
        )}

        {/* Quick Command Search Bar when inside a Workspace */}
        {workspaceId && (
          <div
            onClick={() => navigate(`/workspaces/${workspaceId}/search`)}
            role="searchbox"
            aria-label="Tìm kiếm nhanh trong workspace"
            style={{
              marginLeft: 12,
              display: 'inline-flex',
              alignItems: 'center',
              gap: 8,
              height: 32,
              padding: '0 12px',
              borderRadius: 8,
              backgroundColor: '#f8fafc',
              border: '1px solid #e2e8f0',
              color: '#64748b',
              fontSize: 12.5,
              cursor: 'pointer',
              lineHeight: '30px',
              transition: 'border-color 0.15s ease',
            }}
          >
            <SearchOutlined style={{ fontSize: 13, color: '#94a3b8' }} />
            <span>Tìm thẻ, tài liệu RAG, nhãn...</span>
            <kbd
              style={{
                fontFamily: 'var(--tn-font-mono)',
                fontSize: 10.5,
                padding: '1px 5px',
                borderRadius: 4,
                backgroundColor: '#ffffff',
                border: '1px solid #cbd5e1',
                color: '#475569',
                lineHeight: '14px',
              }}
            >
              Ctrl+K
            </kbd>
          </div>
        )}
      </Flex>

      <Flex align="center" gap={12}>
        {children}

        {showNotifications && <NotificationBell workspaceId={workspaceId} />}

        <Dropdown menu={{ items: menuItems }} placement="bottomRight" arrow trigger={['click']}>
          <Space
            size="small"
            style={{
              cursor: 'pointer',
              color: '#0f172a',
              padding: '4px 10px',
              borderRadius: 9999,
              border: '1px solid #e2e8f0',
              background: '#f8fafc',
              lineHeight: 1.2,
            }}
            data-testid="user-dropdown-trigger"
          >
            <Avatar src={user?.avatarUrl} size={26} style={{ backgroundColor: '#4f46e5', fontWeight: 600 }}>
              {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
            </Avatar>
            <Typography.Text style={{ color: '#0f172a', fontWeight: 600, fontSize: 13 }}>
              {user?.displayName ?? user?.email}
            </Typography.Text>
            <DownOutlined style={{ fontSize: 10, color: '#64748b' }} />
          </Space>
        </Dropdown>

        <Button
          danger
          size="small"
          onClick={() => logout()}
          style={{ borderRadius: 8, height: 32 }}
        >
          Đăng xuất
        </Button>
      </Flex>
    </Header>
  )
}
