import React from 'react'
import { Avatar, Button, Dropdown, Flex, Layout, Space, Typography } from 'antd'
import type { MenuProps } from 'antd'
import {
  AppstoreOutlined,
  BarChartOutlined,
  BookOutlined,
  DashboardOutlined,
  DownOutlined,
  HistoryOutlined,
  LogoutOutlined,
  ProjectOutlined,
  SearchOutlined,
  SettingOutlined,
  TeamOutlined,
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

  const userMenuItems: MenuProps['items'] = [
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

  const workspaceNavItems: MenuProps['items'] = workspaceId
    ? [
        {
          key: 'nav-dashboard',
          icon: <DashboardOutlined style={{ color: '#4f46e5' }} />,
          label: 'Tổng quan Workspace',
          onClick: () => navigate(`/workspaces/${workspaceId}/dashboard`),
        },
        {
          key: 'nav-boards',
          icon: <ProjectOutlined style={{ color: '#4f46e5' }} />,
          label: 'Bảng Kanban',
          onClick: () => navigate(`/workspaces/${workspaceId}/boards`),
        },
        {
          key: 'nav-search',
          icon: <SearchOutlined style={{ color: '#06b6d4' }} />,
          label: 'Tìm kiếm đa tiêu chí',
          onClick: () => navigate(`/workspaces/${workspaceId}/search`),
        },
        {
          key: 'nav-knowledge',
          icon: <BookOutlined style={{ color: '#8b5cf6' }} />,
          label: 'Cơ sở tri thức (RAG)',
          onClick: () => navigate(`/workspaces/${workspaceId}/knowledge`),
        },
        {
          key: 'nav-reports',
          icon: <BarChartOutlined style={{ color: '#10b981' }} />,
          label: 'Báo cáo & Phân tích',
          onClick: () => navigate(`/workspaces/${workspaceId}/reports`),
        },
        {
          key: 'nav-members',
          icon: <TeamOutlined style={{ color: '#f59e0b' }} />,
          label: 'Thành viên & Quyền',
          onClick: () => navigate(`/workspaces/${workspaceId}/members`),
        },
        {
          key: 'nav-activity',
          icon: <HistoryOutlined style={{ color: '#64748b' }} />,
          label: 'Lịch sử hoạt động',
          onClick: () => navigate(`/workspaces/${workspaceId}/activity`),
        },
        {
          key: 'nav-settings',
          icon: <SettingOutlined style={{ color: '#64748b' }} />,
          label: 'Cài đặt Workspace',
          onClick: () => navigate(`/workspaces/${workspaceId}/settings`),
        },
      ]
    : []

  return (
    <Header
      style={{
        background: '#ffffff',
        borderBottom: '1px solid #e2e8f0',
        padding: '0 24px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        boxShadow: '0 1px 3px 0 rgba(0, 0, 0, 0.03)',
        position: 'sticky',
        top: 0,
        zIndex: 90,
      }}
      data-testid="app-header"
    >
      <Flex align="center" gap={12}>
        <NexusLogo size={32} style={{ cursor: 'pointer' }} onClick={() => navigate('/')} />
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

        {workspaceId && (
          <Dropdown menu={{ items: workspaceNavItems }} trigger={['click']}>
            <Button
              size="small"
              icon={<AppstoreOutlined />}
              style={{
                borderRadius: 8,
                borderColor: '#e2e8f0',
                color: '#4f46e5',
                background: '#eef2ff',
                fontWeight: 500,
                fontSize: 12,
                marginLeft: 4,
              }}
            >
              Phân hệ <DownOutlined style={{ fontSize: 9 }} />
            </Button>
          </Dropdown>
        )}
      </Flex>

      <Flex align="center" gap={14}>
        {children}

        {showNotifications && (
          <NotificationBell workspaceId={workspaceId} />
        )}

        <Dropdown menu={{ items: userMenuItems }} placement="bottomRight" arrow trigger={['click']}>
          <Space size="small" style={{ cursor: 'pointer', color: '#0f172a' }} data-testid="user-dropdown-trigger">
            <Avatar src={user?.avatarUrl} style={{ backgroundColor: '#4f46e5', fontWeight: 600 }}>
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
