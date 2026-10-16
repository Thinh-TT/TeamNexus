import React, { useEffect, useState } from 'react'
import {
  AppstoreOutlined,
  BarChartOutlined,
  CustomerServiceOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  HistoryOutlined,
  MenuFoldOutlined,
  MenuUnfoldOutlined,
  ProjectOutlined,
  SearchOutlined,
  SettingOutlined,
  SwapOutlined,
  TeamOutlined,
  ThunderboltFilled,
} from '@ant-design/icons'
import { Avatar, Button, Flex, Tag, Tooltip, Typography } from 'antd'
import { Outlet, useLocation, useNavigate, useParams } from 'react-router-dom'
import { useAuthStore } from '../../features/auth/store/useAuthStore'
import { NexusLogo } from './NexusLogo'

interface NavItem {
  key: string
  label: string
  pathSuffix: string
  icon: React.ReactNode
  badge?: string
  badgeColor?: string
  shortcut?: string
}

const NAV_ITEMS: NavItem[] = [
  {
    key: 'dashboard',
    label: 'Tổng quan (Dashboard)',
    pathSuffix: '/dashboard',
    icon: <DashboardOutlined />,
  },
  {
    key: 'boards',
    label: 'Bảng Kanban (Boards)',
    pathSuffix: '/boards',
    icon: <ProjectOutlined />,
  },
  {
    key: 'search',
    label: 'Tìm kiếm xuyên bảng',
    pathSuffix: '/search',
    icon: <SearchOutlined />,
    shortcut: 'Ctrl+K',
  },
  {
    key: 'knowledge',
    label: 'Kho tri thức RAG',
    pathSuffix: '/knowledge',
    icon: <DatabaseOutlined />,
    badge: 'RAG',
    badgeColor: '#7c3aed',
  },
  {
    key: 'members',
    label: 'Thành viên & Kỹ năng',
    pathSuffix: '/members',
    icon: <TeamOutlined />,
    badge: 'AI',
    badgeColor: '#4f46e5',
  },
  {
    key: 'reports',
    label: 'Báo cáo & Tiến độ',
    pathSuffix: '/reports',
    icon: <BarChartOutlined />,
  },
  {
    key: 'activity',
    label: 'Nhật ký hoạt động',
    pathSuffix: '/activity',
    icon: <HistoryOutlined />,
  },
  {
    key: 'settings',
    label: 'Cài đặt (Settings)',
    pathSuffix: '/settings',
    icon: <SettingOutlined />,
  },
]

export const WorkspaceShell: React.FC = () => {
  const { workspaceId } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()
  const location = useLocation()
  const { user } = useAuthStore()
  const [collapsed, setCollapsed] = useState(false)

  // Global Ctrl+K / Cmd+K shortcut to jump to Cross-Board Search
  useEffect(() => {
    if (!workspaceId) return
    const handleKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        navigate(`/workspaces/${workspaceId}/search`)
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [workspaceId, navigate])

  const isActiveRoute = (pathSuffix: string) => {
    if (!workspaceId) return false
    const targetPrefix = `/workspaces/${workspaceId}${pathSuffix}`
    return (
      location.pathname === targetPrefix ||
      location.pathname.startsWith(`${targetPrefix}/`)
    )
  }

  const shortWorkspaceId = workspaceId ? workspaceId.slice(0, 8).toUpperCase() : 'WORKSPACE'

  return (
    <div
      style={{
        display: 'flex',
        minHeight: '100vh',
        width: '100%',
        backgroundColor: 'var(--tn-bg-canvas, #f8fafc)',
      }}
      data-testid="workspace-shell"
    >
      {/* Left Navigation Sidebar — Luminous Enterprise Workspace */}
      <aside
        aria-label="Điều hướng Không gian làm việc"
        data-testid="workspace-sidebar"
        style={{
          width: collapsed ? 68 : 244,
          minWidth: collapsed ? 68 : 244,
          height: '100vh',
          position: 'sticky',
          top: 0,
          backgroundColor: '#ffffff',
          borderRight: '1px solid #e2e8f0',
          display: 'flex',
          flexDirection: 'column',
          justifyContent: 'space-between',
          transition: 'width 0.18s cubic-bezier(0.4, 0, 0.2, 1), min-width 0.18s cubic-bezier(0.4, 0, 0.2, 1)',
          zIndex: 30,
          userSelect: 'none',
        }}
      >
        {/* Top Section: Brand + Active Workspace Pill + Navigation Menu */}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12, padding: collapsed ? '14px 10px' : '14px 14px' }}>
          {/* Brand Row */}
          <Flex align="center" justify={collapsed ? 'center' : 'space-between'} gap={8}>
            <Flex
              align="center"
              gap={10}
              style={{ cursor: 'pointer', overflow: 'hidden' }}
              onClick={() => navigate('/')}
            >
              <NexusLogo size={30} />
              {!collapsed && (
                <div style={{ lineHeight: 1.15 }}>
                  <Typography.Text
                    strong
                    style={{
                      fontSize: 15.5,
                      color: '#0f172a',
                      letterSpacing: '-0.02em',
                      display: 'block',
                    }}
                  >
                    TeamNexus
                  </Typography.Text>
                  <Typography.Text
                    style={{
                      fontSize: 10.5,
                      color: '#64748b',
                      fontFamily: 'var(--tn-font-mono)',
                    }}
                  >
                    Workspace OS v4.2
                  </Typography.Text>
                </div>
              )}
            </Flex>

            {!collapsed && (
              <Tooltip title="Thu gọn thanh bên">
                <Button
                  type="text"
                  size="small"
                  icon={<MenuFoldOutlined style={{ color: '#64748b' }} />}
                  onClick={() => setCollapsed(true)}
                  aria-label="Thu gọn thanh bên"
                />
              </Tooltip>
            )}
          </Flex>

          {collapsed && (
            <Flex justify="center">
              <Tooltip title="Mở rộng thanh bên" placement="right">
                <Button
                  type="text"
                  size="small"
                  icon={<MenuUnfoldOutlined style={{ color: '#64748b' }} />}
                  onClick={() => setCollapsed(false)}
                  aria-label="Mở rộng thanh bên"
                />
              </Tooltip>
            </Flex>
          )}

          {/* Workspace Context Switcher Card */}
          {!collapsed ? (
            <div
              onClick={() => navigate('/')}
              title="Nhấn để chuyển đổi Không gian làm việc"
              style={{
                padding: '10px 12px',
                borderRadius: 10,
                background: 'linear-gradient(180deg, #f8fafc 0%, #f1f5f9 100%)',
                border: '1px solid #e2e8f0',
                cursor: 'pointer',
                transition: 'border-color 0.15s ease',
              }}
            >
              <Flex align="center" justify="space-between" style={{ marginBottom: 4 }}>
                <Typography.Text
                  strong
                  ellipsis
                  style={{ fontSize: 12.5, color: '#0f172a', maxWidth: 145 }}
                >
                  WS #{shortWorkspaceId}
                </Typography.Text>
                <Flex align="center" gap={4}>
                  <span className="tn-live-dot" />
                  <span
                    style={{
                      fontSize: 10.5,
                      fontWeight: 600,
                      color: '#059669',
                      fontFamily: 'var(--tn-font-mono)',
                    }}
                  >
                    LIVE
                  </span>
                </Flex>
              </Flex>
              <Flex align="center" justify="space-between">
                <Tag
                  style={{
                    margin: 0,
                    fontSize: 10.5,
                    lineHeight: '16px',
                    padding: '0 6px',
                    borderRadius: 4,
                    background: '#eef2ff',
                    color: '#4f46e5',
                    borderColor: '#c7d2fe',
                    fontWeight: 600,
                  }}
                >
                  Hybrid AI Active
                </Tag>
                <span style={{ fontSize: 11, color: '#64748b' }}>
                  <SwapOutlined /> Đổi WS
                </span>
              </Flex>
            </div>
          ) : (
            <Tooltip title={`WS #${shortWorkspaceId} — Đổi Workspace`} placement="right">
              <div
                onClick={() => navigate('/')}
                style={{
                  width: 40,
                  height: 36,
                  margin: '0 auto',
                  borderRadius: 8,
                  background: '#eef2ff',
                  border: '1px solid #c7d2fe',
                  color: '#4f46e5',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: 'pointer',
                  fontWeight: 700,
                  fontSize: 12,
                }}
              >
                <AppstoreOutlined />
              </div>
            </Tooltip>
          )}

          {/* Navigation Menu Items */}
          <nav
            role="menu"
            aria-label="Danh mục chức năng Workspace"
            style={{ display: 'flex', flexDirection: 'column', gap: 3, marginTop: 2 }}
          >
            {NAV_ITEMS.map((item) => {
              const active = isActiveRoute(item.pathSuffix)
              const menuNode = (
                <div
                  key={item.key}
                  role="menuitem"
                  tabIndex={0}
                  data-testid={`sidebar-nav-${item.key}`}
                  onClick={() => {
                    if (workspaceId) {
                      navigate(`/workspaces/${workspaceId}${item.pathSuffix}`)
                    }
                  }}
                  onKeyDown={(e) => {
                    if ((e.key === 'Enter' || e.key === ' ') && workspaceId) {
                      e.preventDefault()
                      navigate(`/workspaces/${workspaceId}${item.pathSuffix}`)
                    }
                  }}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: collapsed ? 'center' : 'space-between',
                    padding: collapsed ? '9px 0' : '8px 11px',
                    borderRadius: 8,
                    cursor: 'pointer',
                    position: 'relative',
                    backgroundColor: active ? '#eef2ff' : 'transparent',
                    color: active ? '#4f46e5' : '#334155',
                    fontWeight: active ? 600 : 500,
                    fontSize: 13,
                    transition: 'all 0.14s ease',
                    borderLeft: active && !collapsed ? '3px solid #4f46e5' : '3px solid transparent',
                  }}
                >
                  <Flex align="center" gap={10} style={{ minWidth: 0 }}>
                    <span
                      style={{
                        fontSize: 15,
                        color: active ? '#4f46e5' : '#64748b',
                        display: 'inline-flex',
                        alignItems: 'center',
                      }}
                    >
                      {item.icon}
                    </span>
                    {!collapsed && (
                      <span
                        style={{
                          whiteSpace: 'nowrap',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                        }}
                      >
                        {item.label}
                      </span>
                    )}
                  </Flex>

                  {!collapsed && item.shortcut && (
                    <span
                      style={{
                        fontSize: 10.5,
                        fontFamily: 'var(--tn-font-mono)',
                        padding: '1px 5px',
                        borderRadius: 4,
                        backgroundColor: '#f1f5f9',
                        border: '1px solid #e2e8f0',
                        color: '#64748b',
                      }}
                    >
                      {item.shortcut}
                    </span>
                  )}

                  {!collapsed && item.badge && (
                    <span
                      style={{
                        fontSize: 10,
                        fontWeight: 700,
                        padding: '1px 6px',
                        borderRadius: 9999,
                        backgroundColor: item.badgeColor === '#7c3aed' ? '#f5f3ff' : '#eef2ff',
                        color: item.badgeColor ?? '#4f46e5',
                        border: `1px solid ${item.badgeColor === '#7c3aed' ? '#ddd6fe' : '#c7d2fe'}`,
                      }}
                    >
                      {item.badge}
                    </span>
                  )}
                </div>
              )

              return collapsed ? (
                <Tooltip key={item.key} title={item.label} placement="right">
                  {menuNode}
                </Tooltip>
              ) : (
                menuNode
              )
            })}
          </nav>
        </div>

        {/* Bottom Section: Dual-Engine AI Telemetry Card & User Quick Profile */}
        <div
          style={{
            padding: collapsed ? '12px 8px' : '12px 14px',
            borderTop: '1px solid #f1f5f9',
            display: 'flex',
            flexDirection: 'column',
            gap: 10,
          }}
        >
          {!collapsed ? (
            <div
              style={{
                padding: '9px 11px',
                borderRadius: 9,
                background: 'linear-gradient(135deg, #f5f3ff 0%, #eef2ff 100%)',
                border: '1px solid #ddd6fe',
              }}
            >
              <Flex align="center" justify="space-between" style={{ marginBottom: 4 }}>
                <Flex align="center" gap={5}>
                  <ThunderboltFilled style={{ color: '#7c3aed', fontSize: 12 }} />
                  <Typography.Text
                    strong
                    style={{ fontSize: 11.5, color: '#5b21b6' }}
                  >
                    Dual-Engine AI
                  </Typography.Text>
                </Flex>
                <span
                  style={{
                    fontSize: 10,
                    fontFamily: 'var(--tn-font-mono)',
                    color: '#059669',
                    fontWeight: 600,
                  }}
                >
                  &lt;300ms
                </span>
              </Flex>
              <Typography.Text
                style={{ fontSize: 11, color: '#475569', display: 'block', lineHeight: 1.35 }}
              >
                Jev 1.13 Sys-1 + DeepSeek Sys-2
              </Typography.Text>
            </div>
          ) : (
            <Tooltip title="Dual-Engine AI: Jev 1.13 System 1 (<300ms) + DeepSeek System 2" placement="right">
              <div
                style={{
                  width: 36,
                  height: 36,
                  margin: '0 auto',
                  borderRadius: 8,
                  background: '#f5f3ff',
                  border: '1px solid #ddd6fe',
                  color: '#7c3aed',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                }}
              >
                <CustomerServiceOutlined />
              </div>
            </Tooltip>
          )}

          {/* User Profile Footer */}
          {user && (
            <Flex
              align="center"
              justify={collapsed ? 'center' : 'space-between'}
              gap={8}
              onClick={() => navigate('/profile')}
              style={{
                padding: collapsed ? '4px 0' : '6px 8px',
                borderRadius: 8,
                cursor: 'pointer',
                backgroundColor: '#f8fafc',
                border: '1px solid #f1f5f9',
              }}
              title="Hồ sơ cá nhân"
            >
              <Flex align="center" gap={8} style={{ minWidth: 0 }}>
                <Avatar
                  size={28}
                  src={user.avatarUrl}
                  style={{ backgroundColor: '#4f46e5', fontSize: 12, fontWeight: 600, flexShrink: 0 }}
                >
                  {user.displayName?.[0]?.toUpperCase() ?? 'U'}
                </Avatar>
                {!collapsed && (
                  <div style={{ minWidth: 0, lineHeight: 1.2 }}>
                    <Typography.Text
                      strong
                      ellipsis
                      style={{ fontSize: 12, color: '#0f172a', display: 'block' }}
                    >
                      {user.displayName ?? user.email}
                    </Typography.Text>
                    <Typography.Text
                      ellipsis
                      style={{ fontSize: 10.5, color: '#64748b', display: 'block' }}
                    >
                      {user.email}
                    </Typography.Text>
                  </div>
                )}
              </Flex>
            </Flex>
          )}
        </div>
      </aside>

      {/* Main Workspace Viewport */}
      <div
        style={{
          flex: 1,
          minWidth: 0,
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <Outlet />
      </div>
    </div>
  )
}
