import React from 'react'
import { Avatar, Dropdown, Flex, Layout, Space, Tag, Typography } from 'antd'
import type { MenuProps } from 'antd'
import {
  AppstoreOutlined,
  DownOutlined,
  LogoutOutlined,
  SearchOutlined,
  ThunderboltFilled,
  UserOutlined,
} from '@ant-design/icons'
import { useLocation, useNavigate } from 'react-router-dom'
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

const getWorkspaceSectionLabel = (pathname: string): string => {
  if (pathname.endsWith('/dashboard')) return 'Tổng quan'
  if (pathname.endsWith('/search')) return 'Tìm kiếm & Lọc thẻ'
  if (pathname.endsWith('/knowledge')) return 'Kho tri thức RAG'
  if (pathname.endsWith('/members')) return 'Thành viên & AI'
  if (pathname.endsWith('/reports')) return 'Báo cáo & Tiến độ'
  if (pathname.endsWith('/activity')) return 'Nhật ký hoạt động'
  if (pathname.endsWith('/settings')) return 'Cài đặt'
  if (pathname.includes('/boards/')) return 'Bảng công việc'
  if (pathname.endsWith('/boards')) return 'Danh sách bảng'
  return 'Không gian làm việc'
}

export const AppHeader: React.FC<AppHeaderProps> = ({
  title,
  showNotifications = true,
  children,
  workspaceId,
}) => {
  const navigate = useNavigate()
  const location = useLocation()
  const { user, logout } = useAuthStore()

  const isInsideWorkspaceRoute = Boolean(workspaceId) && location.pathname.startsWith('/workspaces/')
  const isSearchPage = location.pathname.endsWith('/search')

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
        gap: 16,
      }}
      data-testid="app-header"
    >
      <Flex align="center" gap={12} style={{ minWidth: 0, flexShrink: 0, whiteSpace: 'nowrap' }}>
        {isInsideWorkspaceRoute ? (
          <Flex align="center" gap={8} style={{ whiteSpace: 'nowrap', flexShrink: 0 }}>
            <span
              onClick={() => navigate(`/workspaces/${workspaceId}/dashboard`)}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 6,
                fontSize: 13,
                fontWeight: 600,
                color: '#64748b',
                cursor: 'pointer',
                whiteSpace: 'nowrap',
              }}
            >
              <AppstoreOutlined style={{ color: '#4f46e5' }} />
              Workspace
            </span>
            <span style={{ color: '#cbd5e1', fontSize: 13 }}>/</span>
            {title ? (
              typeof title === 'string' ? (
                <Typography.Text
                  strong
                  style={{ color: '#0f172a', fontSize: 13.5, whiteSpace: 'nowrap' }}
                >
                  {title}
                </Typography.Text>
              ) : (
                <div style={{ display: 'inline-flex', alignItems: 'center', whiteSpace: 'nowrap' }}>
                  {title}
                </div>
              )
            ) : (
              <Typography.Text
                strong
                style={{ color: '#0f172a', fontSize: 13.5, whiteSpace: 'nowrap' }}
              >
                {getWorkspaceSectionLabel(location.pathname)}
              </Typography.Text>
            )}
          </Flex>
        ) : (
          <Flex align="center" gap={10} style={{ whiteSpace: 'nowrap', flexShrink: 0 }}>
            <NexusLogo size={30} style={{ cursor: 'pointer', flexShrink: 0 }} />
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
                    whiteSpace: 'nowrap',
                    flexShrink: 0,
                  }}
                  onClick={() => navigate('/')}
                >
                  {title}
                </Typography.Title>
              ) : (
                title
              )
            ) : (
              <Flex align="center" gap={8} style={{ whiteSpace: 'nowrap', flexShrink: 0 }}>
                <Typography.Title
                  level={4}
                  style={{
                    color: '#0f172a',
                    margin: 0,
                    cursor: 'pointer',
                    fontWeight: 700,
                    letterSpacing: '-0.02em',
                    whiteSpace: 'nowrap',
                    flexShrink: 0,
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
                    whiteSpace: 'nowrap',
                    flexShrink: 0,
                  }}
                >
                  <ThunderboltFilled style={{ fontSize: 10, marginRight: 4 }} />
                  Hybrid AI v4.2
                </Tag>
              </Flex>
            )}
          </Flex>
        )}

        {/* Quick Command Search Bar when inside a Workspace (hidden on /search page per Issue #17) */}
        {workspaceId && !isSearchPage && (
          <div
            onClick={() => navigate(`/workspaces/${workspaceId}/search`)}
            role="searchbox"
            aria-label="Tìm kiếm nhanh trong workspace"
            style={{
              marginLeft: 8,
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
              whiteSpace: 'nowrap',
              flexShrink: 0,
              transition: 'border-color 0.15s ease',
            }}
          >
            <SearchOutlined style={{ fontSize: 13, color: '#94a3b8' }} />
            <span>Tìm thẻ, tài liệu RAG...</span>
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

      <Flex align="center" gap={12} style={{ flexShrink: 0, whiteSpace: 'nowrap' }}>
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
              whiteSpace: 'nowrap',
              flexShrink: 0,
            }}
            data-testid="user-dropdown-trigger"
          >
            <Avatar
              src={user?.avatarUrl}
              size={26}
              style={{ backgroundColor: '#4f46e5', fontWeight: 600, flexShrink: 0 }}
            >
              {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
            </Avatar>
            <Typography.Text
              style={{
                color: '#0f172a',
                fontWeight: 600,
                fontSize: 13,
                maxWidth: 160,
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                display: 'inline-block',
                verticalAlign: 'middle',
              }}
            >
              {user?.displayName ?? user?.email}
            </Typography.Text>
            <DownOutlined style={{ fontSize: 10, color: '#64748b' }} />
          </Space>
        </Dropdown>
      </Flex>
    </Header>
  )
}
