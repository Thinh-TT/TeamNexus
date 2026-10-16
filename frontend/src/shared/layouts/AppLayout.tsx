import React, { useState } from 'react'
import {
  Avatar,
  Badge,
  Breadcrumb,
  Button,
  Dropdown,
  Flex,
  Layout,
  Menu,
  type MenuProps,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import {
  BarChartOutlined,
  BookOutlined,
  DashboardOutlined,
  DownOutlined,
  HistoryOutlined,
  LoadingOutlined,
  LogoutOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  ProjectOutlined,
  SearchOutlined,
  SettingOutlined,
  SyncOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Link, useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../features/auth/store/useAuthStore'
import { useWorkspaceRole } from '../hooks/useWorkspaceRole'
import { NexusLogo } from '../components/NexusLogo'
import { NotificationBell } from '../components/NotificationBell'
import type { HubConnectionStatus } from '../../features/board/types/board.types'
import { COLD_START_MESSAGE } from '../../features/board/utils/reconnectPolicy'

const { Header, Sider, Content } = Layout

export interface AppLayoutProps {
  workspaceId?: string
  currentMenuKey?:
    | 'dashboard'
    | 'boards'
    | 'search'
    | 'knowledge'
    | 'reports'
    | 'members'
    | 'activity'
    | 'settings'
  breadcrumbs?: Array<{ label: React.ReactNode; path?: string }>
  headerExtra?: React.ReactNode
  showNotifications?: boolean
  connectionStatus?: HubConnectionStatus
  onReconnect?: () => void
  isColdStarting?: boolean
  children: React.ReactNode
}

export const AppLayout: React.FC<AppLayoutProps> = ({
  workspaceId,
  currentMenuKey,
  breadcrumbs,
  headerExtra,
  showNotifications = true,
  connectionStatus,
  onReconnect,
  isColdStarting = false,
  children,
}) => {
  const [collapsed, setCollapsed] = useState(false)
  const navigate = useNavigate()
  const { user, logout } = useAuthStore()
  const { role } = useWorkspaceRole(workspaceId)

  const menuItems: MenuProps['items'] = workspaceId
    ? [
        {
          key: 'dashboard',
          icon: <DashboardOutlined style={{ fontSize: 16 }} />,
          label: 'Tổng quan',
          onClick: () => navigate(`/workspaces/${workspaceId}/dashboard`),
        },
        {
          key: 'boards',
          icon: <ProjectOutlined style={{ fontSize: 16 }} />,
          label: 'Bảng Kanban',
          onClick: () => navigate(`/workspaces/${workspaceId}/boards`),
        },
        {
          key: 'search',
          icon: <SearchOutlined style={{ fontSize: 16 }} />,
          label: 'Tìm kiếm',
          onClick: () => navigate(`/workspaces/${workspaceId}/search`),
        },
        {
          key: 'knowledge',
          icon: <BookOutlined style={{ fontSize: 16 }} />,
          label: 'Cơ sở tri thức',
          onClick: () => navigate(`/workspaces/${workspaceId}/knowledge`),
        },
        {
          key: 'reports',
          icon: <BarChartOutlined style={{ fontSize: 16 }} />,
          label: 'Báo cáo & Phân tích',
          onClick: () => navigate(`/workspaces/${workspaceId}/reports`),
        },
        {
          key: 'members',
          icon: <TeamOutlined style={{ fontSize: 16 }} />,
          label: 'Thành viên',
          onClick: () => navigate(`/workspaces/${workspaceId}/members`),
        },
        {
          key: 'activity',
          icon: <HistoryOutlined style={{ fontSize: 16 }} />,
          label: 'Lịch sử hoạt động',
          onClick: () => navigate(`/workspaces/${workspaceId}/activity`),
        },
        {
          key: 'settings',
          icon: <SettingOutlined style={{ fontSize: 16 }} />,
          label: 'Cài đặt Workspace',
          onClick: () => navigate(`/workspaces/${workspaceId}/settings`),
        },
      ]
    : []

  const userDropdownItems: MenuProps['items'] = [
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

  const renderConnectionPill = () => {
    if (!connectionStatus) return null

    switch (connectionStatus) {
      case 'connected':
        return (
          <Tooltip title="Real-time: Kết nối SignalR ổn định. Mọi thay đổi đồng bộ tức thời.">
            <Tag
              color="success"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 5,
                borderRadius: 12,
                padding: '2px 8px',
                margin: 0,
                border: '1px solid rgba(16, 185, 129, 0.3)',
                background: 'rgba(16, 185, 129, 0.08)',
                color: '#047857',
                fontWeight: 500,
              }}
            >
              <Badge status="success" />
              <span style={{ fontSize: 11.5 }}>SignalR: Đã kết nối</span>
            </Tag>
          </Tooltip>
        )
      case 'reconnecting':
        return (
          <Tooltip title={isColdStarting ? COLD_START_MESSAGE : 'Đang cố gắng kết nối lại...'}>
            <Tag
              color="warning"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 5,
                borderRadius: 12,
                padding: '2px 8px',
                margin: 0,
              }}
            >
              <SyncOutlined spin />
              <span style={{ fontSize: 11.5 }}>Đang kết nối lại</span>
            </Tag>
          </Tooltip>
        )
      case 'connecting':
        return (
          <Tooltip title="Đang khởi tạo kết nối SignalR...">
            <Tag
              color="processing"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 5,
                borderRadius: 12,
                padding: '2px 8px',
                margin: 0,
              }}
            >
              <LoadingOutlined spin />
              <span style={{ fontSize: 11.5 }}>Đang kết nối</span>
            </Tag>
          </Tooltip>
        )
      case 'disconnected':
      default:
        return (
          <Flex align="center" gap={6}>
            <Tooltip title="Mất kết nối máy chủ Real-time.">
              <Tag color="default" style={{ borderRadius: 12, margin: 0 }}>
                <Badge status="default" />
                <span style={{ fontSize: 11.5 }}>Mất kết nối</span>
              </Tag>
            </Tooltip>
            {onReconnect && (
              <Button
                size="small"
                onClick={onReconnect}
                style={{ fontSize: 11.5, height: 22, padding: '0 8px', borderRadius: 10 }}
              >
                Kết nối lại
              </Button>
            )}
          </Flex>
        )
    }
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      {/* Collapsible Left Sidebar */}
      {workspaceId && (
        <Sider
          collapsible
          collapsed={collapsed}
          onCollapse={(value) => setCollapsed(value)}
          trigger={null}
          width={240}
          collapsedWidth={72}
          theme="light"
          style={{
            overflow: 'auto',
            height: '100vh',
            position: 'sticky',
            top: 0,
            left: 0,
            borderRight: '1px solid #e2e8f0',
            background: '#ffffff',
            boxShadow: '1px 0 3px rgba(0, 0, 0, 0.02)',
            zIndex: 100,
          }}
          data-testid="app-sider"
        >
          {/* Logo & Workspace Brand Header */}
          <div
            style={{
              height: 64,
              display: 'flex',
              alignItems: 'center',
              justifyContent: collapsed ? 'center' : 'space-between',
              padding: collapsed ? '0 8px' : '0 16px',
              borderBottom: '1px solid #f1f5f9',
            }}
          >
            <Flex align="center" gap={10} style={{ overflow: 'hidden' }}>
              <NexusLogo size={collapsed ? 30 : 32} style={{ cursor: 'pointer', flexShrink: 0 }} />
              {!collapsed && (
                <div style={{ overflow: 'hidden' }}>
                  <Typography.Text
                    strong
                    style={{
                      fontSize: 16,
                      color: '#0f172a',
                      letterSpacing: '-0.3px',
                      display: 'block',
                      lineHeight: '18px',
                    }}
                  >
                    TeamNexus
                  </Typography.Text>
                  <Tag
                    color="indigo"
                    style={{
                      fontSize: 10,
                      lineHeight: '14px',
                      padding: '0 4px',
                      borderRadius: 4,
                      margin: '2px 0 0 0',
                    }}
                  >
                    Workspace Core
                  </Tag>
                </div>
              )}
            </Flex>
          </div>

          {/* Navigation Menu */}
          <div style={{ padding: '12px 8px' }}>
            <Menu
              mode="inline"
              selectedKeys={currentMenuKey ? [currentMenuKey] : []}
              items={menuItems}
              style={{
                borderRight: 'none',
                background: 'transparent',
                fontWeight: 500,
              }}
            />
          </div>

          {/* Sider Bottom Footer */}
          <div
            style={{
              position: 'absolute',
              bottom: 0,
              left: 0,
              right: 0,
              padding: '12px',
              borderTop: '1px solid #f1f5f9',
              background: '#ffffff',
            }}
          >
            <Flex align="center" justify={collapsed ? 'center' : 'space-between'} gap={8}>
              <Button
                type="text"
                icon={collapsed ? <MenuUnfoldOutlined /> : <MenuFoldOutlined />}
                onClick={() => setCollapsed(!collapsed)}
                style={{
                  fontSize: 14,
                  width: collapsed ? 38 : 34,
                  height: 34,
                  borderRadius: 8,
                  color: '#64748b',
                }}
                data-testid="sidebar-collapse-btn"
              />
              {!collapsed && role && (
                <Tag
                  color={role === 'Admin' ? 'red' : role === 'Manager' ? 'blue' : 'default'}
                  style={{ borderRadius: 6, fontSize: 11, margin: 0 }}
                >
                  {role}
                </Tag>
              )}
            </Flex>
          </div>
        </Sider>
      )}

      {/* Main Layout Area */}
      <Layout style={{ background: '#f8fafc' }}>
        {/* Top Header Bar */}
        <Header
          style={{
            height: 64,
            background: '#ffffff',
            borderBottom: '1px solid #e2e8f0',
            padding: '0 24px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            position: 'sticky',
            top: 0,
            zIndex: 90,
            boxShadow: '0 1px 2px rgba(0, 0, 0, 0.02)',
          }}
          data-testid="app-layout-header"
        >
          {/* Left: Breadcrumbs & Real-time Indicator */}
          <Flex align="center" gap={16}>
            {!workspaceId && (
              <Flex align="center" gap={10} style={{ cursor: 'pointer' }} onClick={() => navigate('/')}>
                <NexusLogo size={28} />
                <Typography.Text strong style={{ fontSize: 16, color: '#0f172a' }}>
                  TeamNexus
                </Typography.Text>
              </Flex>
            )}

            {breadcrumbs && breadcrumbs.length > 0 && (
              <Breadcrumb
                items={breadcrumbs.map((b) => ({
                  title: b.path ? <Link to={b.path}>{b.label}</Link> : b.label,
                }))}
                style={{ fontSize: 13 }}
              />
            )}

            {renderConnectionPill()}
          </Flex>

          {/* Right: Actions, Notifications, User */}
          <Flex align="center" gap={12}>
            {headerExtra}

            {showNotifications && (
              <NotificationBell workspaceId={workspaceId} />
            )}

            <Dropdown menu={{ items: userDropdownItems }} placement="bottomRight" arrow trigger={['click']}>
              <Space
                size="small"
                style={{
                  cursor: 'pointer',
                  padding: '4px 8px',
                  borderRadius: 8,
                  transition: 'background 0.2s',
                }}
                data-testid="user-dropdown-trigger"
              >
                <Avatar
                  src={user?.avatarUrl}
                  style={{ backgroundColor: '#4f46e5', fontWeight: 600 }}
                  size={32}
                >
                  {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
                </Avatar>
                <div style={{ lineHeight: 1.2, textAlign: 'left', display: 'none' }} className="user-name-desktop">
                  <Typography.Text strong style={{ fontSize: 13, color: '#0f172a', display: 'block' }}>
                    {user?.displayName ?? user?.email}
                  </Typography.Text>
                </div>
                <DownOutlined style={{ fontSize: 10, color: '#64748b' }} />
              </Space>
            </Dropdown>
          </Flex>
        </Header>

        {/* Content Area */}
        <Content style={{ padding: '0', minHeight: 'calc(100vh - 64px)' }}>
          {children}
        </Content>
      </Layout>
    </Layout>
  )
}
