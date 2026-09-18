import React, { useEffect, useState } from 'react'
import {
  AppstoreOutlined,
  BarChartOutlined,
  CheckCircleOutlined,
  CopyOutlined,
  DashboardOutlined,
  FolderOpenOutlined,
  ProjectOutlined,
  RobotOutlined,
  SearchOutlined,
  SettingOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Button,
  Card,
  Col,
  Descriptions,
  Empty,
  Flex,
  Input,
  Layout,
  message,
  Row,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../hooks/useAuth'
import { workspaceApi } from '../../workspace/services/workspaceApi'
import type { WorkspaceSummary } from '../../workspace/types/workspace.types'
import { AppHeader } from '../../../shared/components/AppHeader'

const { Content } = Layout

export const DashboardPage: React.FC = () => {
  const { user } = useAuth()
  const navigate = useNavigate()
  const [workspaces, setWorkspaces] = useState<WorkspaceSummary[]>([])
  const [manualWsId, setManualWsId] = useState<string>('')

  useEffect(() => {
    workspaceApi
      .list()
      .then((list) => {
        if (list && list.length > 0) {
          setWorkspaces(list)
          setManualWsId(list[0].id)
        }
      })
      .catch(() => {})
  }, [])

  const getGreeting = () => {
    const hour = new Date().getHours()
    if (hour < 12) return 'Chào buổi sáng'
    if (hour < 18) return 'Chào buổi chiều'
    return 'Chào buổi tối'
  }

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard?.writeText(text)
    message.success(`Đã sao chép ${label}`)
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <AppHeader workspaceId={workspaces[0]?.id} />

      <Content style={{ padding: '32px 24px', maxWidth: 1120, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          {/* WELCOME HERO COMMAND BANNER (LIGHT MODE) */}
          <div
            style={{
              background: '#ffffff',
              borderRadius: 16,
              padding: '28px 32px',
              border: '1px solid #e2e8f0',
              boxShadow: '0 4px 20px rgba(0, 0, 0, 0.03)',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
            {/* Ambient Background Blur Accent */}
            <div
              style={{
                position: 'absolute',
                top: -50,
                right: -50,
                width: 250,
                height: 250,
                borderRadius: '50%',
                background: 'radial-gradient(circle, rgba(99, 102, 241, 0.1) 0%, transparent 70%)',
                pointerEvents: 'none',
              }}
            />

            <Row gutter={[24, 24]} align="middle" justify="space-between" style={{ position: 'relative', zIndex: 1 }}>
              <Col xs={24} md={16}>
                <Flex align="center" gap={16} wrap="wrap">
                  <Avatar
                    size={56}
                    src={user?.avatarUrl}
                    style={{
                      backgroundColor: '#6366f1',
                      border: '3px solid #e0e7ff',
                      fontSize: 22,
                      fontWeight: 600,
                    }}
                  >
                    {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
                  </Avatar>
                  <div>
                    <Typography.Title level={2} style={{ color: '#0f172a', margin: 0, fontSize: 24, fontWeight: 700 }}>
                      {getGreeting()}, {user?.displayName ?? 'bạn'}! 👋
                    </Typography.Title>
                    <Typography.Paragraph style={{ color: '#64748b', margin: '4px 0 0', fontSize: 14 }}>
                      Chào mừng bạn quay lại Trung tâm Điều phối Workspace TeamNexus.
                    </Typography.Paragraph>
                  </div>
                </Flex>

                <Space size={[8, 8]} wrap style={{ marginTop: 18 }}>
                  <Tag
                    color="geekblue"
                    style={{
                      borderRadius: 12,
                      padding: '2px 10px',
                    }}
                  >
                    <AppstoreOutlined /> {workspaces.length} Không gian làm việc
                  </Tag>
                  <Tag
                    color="purple"
                    style={{
                      borderRadius: 12,
                      padding: '2px 10px',
                    }}
                  >
                    <RobotOutlined /> DeepSeek AI Copilot Sẵn sàng
                  </Tag>
                  <Tag
                    color="success"
                    style={{
                      borderRadius: 12,
                      padding: '2px 10px',
                    }}
                  >
                    <CheckCircleOutlined /> Real-time SignalR Kết nối
                  </Tag>
                </Space>
              </Col>

              <Col xs={24} md={8} style={{ textAlign: 'right' }}>
                <Space wrap>
                  {workspaces.length > 0 && (
                    <Button
                      icon={<SearchOutlined />}
                      onClick={() => navigate(`/workspaces/${workspaces[0].id}/search`)}
                      style={{
                        borderRadius: 8,
                        background: '#f8fafc',
                        color: '#0f172a',
                        borderColor: '#cbd5e1',
                      }}
                    >
                      Tìm tác vụ
                    </Button>
                  )}
                  <Button
                    icon={<UserOutlined />}
                    onClick={() => navigate('/profile')}
                    style={{
                      borderRadius: 8,
                      background: '#f8fafc',
                      color: '#0f172a',
                      borderColor: '#cbd5e1',
                    }}
                  >
                    Hồ sơ
                  </Button>
                </Space>
              </Col>
            </Row>
          </div>

          {/* WORKSPACES GRID SECTION */}
          <Card
            title={
              <Flex align="center" justify="space-between" wrap="wrap" gap={8}>
                <Flex align="center" gap={8}>
                  <div
                    style={{
                      width: 28,
                      height: 28,
                      borderRadius: 6,
                      background: '#ede9fe',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                    }}
                  >
                    <AppstoreOutlined style={{ color: '#6366f1' }} />
                  </div>
                  <span style={{ fontSize: 16, fontWeight: 600, color: '#0f172a' }}>
                    Workspace của bạn
                  </span>
                </Flex>
                <Tag color="indigo" style={{ borderRadius: 10 }}>
                  {workspaces.length} Không gian
                </Tag>
              </Flex>
            }
            style={{ borderRadius: 14, boxShadow: '0 4px 16px rgba(0,0,0,0.04)', border: '1px solid #e2e8f0' }}
          >
            <Typography.Paragraph type="secondary" style={{ marginBottom: 20 }}>
              Danh sách các Không gian làm việc mà bạn đang tham gia:
            </Typography.Paragraph>

            {workspaces.length === 0 ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Bạn chưa tham gia Không gian làm việc nào"
              />
            ) : (
              <Row gutter={[20, 20]}>
                {workspaces.map((ws) => (
                  <Col xs={24} md={12} key={ws.id}>
                    <Card
                      hoverable
                      style={{
                        borderRadius: 12,
                        border: '1px solid #e2e8f0',
                        background: '#ffffff',
                        boxShadow: '0 2px 8px rgba(0,0,0,0.02)',
                        transition: 'all 0.25s ease',
                      }}
                      styles={{ body: { padding: 20 } }}
                    >
                      <Flex justify="space-between" align="flex-start" gap={8}>
                        <Flex align="center" gap={12}>
                          <div
                            style={{
                              width: 44,
                              height: 44,
                              borderRadius: 10,
                              background: 'linear-gradient(135deg, #e0e7ff 0%, #ede9fe 100%)',
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              color: '#4f46e5',
                              fontSize: 18,
                              fontWeight: 700,
                            }}
                          >
                            {ws.name?.[0]?.toUpperCase() ?? 'W'}
                          </div>
                          <div>
                            <Typography.Text strong style={{ fontSize: 16, color: '#0f172a', display: 'block' }}>
                              {ws.name}
                            </Typography.Text>
                            <Space size={4} style={{ marginTop: 2 }}>
                              <Tag color="purple" style={{ margin: 0, borderRadius: 6, fontSize: 11 }}>
                                {ws.role}
                              </Tag>
                              {ws.isOwner && (
                                <Tag color="gold" style={{ margin: 0, borderRadius: 6, fontSize: 11 }}>
                                  Owner
                                </Tag>
                              )}
                            </Space>
                          </div>
                        </Flex>

                        <Space size={4}>
                          <Tooltip title="Cài đặt Workspace">
                            <Button
                              type="text"
                              size="small"
                              icon={<SettingOutlined />}
                              onClick={() => navigate(`/workspaces/${ws.id}/settings`)}
                            />
                          </Tooltip>
                          <Tooltip title="Thành viên">
                            <Button
                              type="text"
                              size="small"
                              icon={<TeamOutlined />}
                              onClick={() => navigate(`/workspaces/${ws.id}/members`)}
                            />
                          </Tooltip>
                          <Tooltip title="Báo cáo tiến độ">
                            <Button
                              type="text"
                              size="small"
                              icon={<BarChartOutlined />}
                              onClick={() => navigate(`/workspaces/${ws.id}/reports`)}
                            />
                          </Tooltip>
                        </Space>
                      </Flex>

                      {ws.description && (
                        <Typography.Paragraph
                          type="secondary"
                          ellipsis={{ rows: 2 }}
                          style={{ margin: '14px 0 8px', fontSize: 13, minHeight: 38 }}
                        >
                          {ws.description}
                        </Typography.Paragraph>
                      )}

                      <Flex justify="space-between" align="center" style={{ marginTop: 12, paddingTop: 12, borderTop: '1px solid #f1f5f9' }}>
                        <Flex align="center" gap={4}>
                          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                            ID: {ws.id.slice(0, 8)}...
                          </Typography.Text>
                          <Tooltip title="Sao chép ID">
                            <Button
                              type="text"
                              size="small"
                              icon={<CopyOutlined style={{ fontSize: 12, color: '#94a3b8' }} />}
                              onClick={() => copyToClipboard(ws.id, 'Workspace ID')}
                            />
                          </Tooltip>
                        </Flex>

                        <Space>
                          <Button
                            type="primary"
                            icon={<DashboardOutlined />}
                            style={{
                              backgroundColor: '#6366f1',
                              borderRadius: 6,
                              fontWeight: 500,
                              boxShadow: '0 2px 6px rgba(99, 102, 241, 0.3)',
                            }}
                            onClick={() => navigate(`/workspaces/${ws.id}/dashboard`)}
                          >
                            Tổng quan
                          </Button>
                          <Button
                            icon={<ProjectOutlined />}
                            style={{ borderRadius: 6, fontWeight: 500 }}
                            onClick={() => navigate(`/workspaces/${ws.id}/boards`)}
                          >
                            Bảng Kanban
                          </Button>
                        </Space>
                      </Flex>
                    </Card>
                  </Col>
                ))}
              </Row>
            )}

            {/* MANUAL WORKSPACE ID ACCESS SECTION */}
            <div
              style={{
                marginTop: 28,
                padding: '16px 20px',
                background: '#f8fafc',
                borderRadius: 10,
                border: '1px solid #e2e8f0',
              }}
            >
              <Typography.Text type="secondary" style={{ display: 'block', marginBottom: 10, fontSize: 13, fontWeight: 500 }}>
                Hoặc nhập Workspace ID thủ công:
              </Typography.Text>
              <Flex gap={12} align="center" wrap="wrap">
                <Input
                  placeholder="Nhập Workspace ID (Guid)..."
                  id="dashboard-workspace-input"
                  value={manualWsId}
                  onChange={(e) => setManualWsId(e.target.value)}
                  style={{ maxWidth: 420, borderRadius: 8 }}
                />
                <Button
                  type="default"
                  icon={<FolderOpenOutlined />}
                  style={{ borderRadius: 8, fontWeight: 500 }}
                  onClick={() => {
                    const wsId = manualWsId.trim() || '00000000-0000-0000-0000-000000000001'
                    navigate(`/workspaces/${wsId}/dashboard`)
                  }}
                >
                  Vào Dashboard →
                </Button>
              </Flex>
            </div>
          </Card>

          {/* USER ACCOUNT INFORMATION CARD */}
          <Card
            title={
              <Flex align="center" gap={8}>
                <UserOutlined style={{ color: '#6366f1' }} />
                <span>Thông tin Tài khoản</span>
              </Flex>
            }
            style={{ borderRadius: 14, boxShadow: '0 4px 16px rgba(0,0,0,0.04)', border: '1px solid #e2e8f0' }}
          >
            <Descriptions
              column={{ xs: 1, sm: 2 }}
              bordered
              size="small"
              style={{ borderRadius: 8, overflow: 'hidden' }}
            >
              <Descriptions.Item label="Tên hiển thị">
                <Typography.Text strong>{user?.displayName ?? 'Chưa cập nhật'}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label="Email">
                <Typography.Text copyable>{user?.email}</Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label="ID Người dùng">
                <Typography.Text copyable style={{ fontSize: 12, fontFamily: 'monospace' }}>
                  {user?.id}
                </Typography.Text>
              </Descriptions.Item>
              <Descriptions.Item label="Vai trò hệ thống">
                {user?.roles && user.roles.length > 0 ? (
                  user.roles.map((role) => (
                    <Tag
                      color={role === 'Admin' ? 'red' : role === 'Manager' ? 'gold' : 'blue'}
                      key={role}
                      style={{ borderRadius: 6 }}
                    >
                      {role}
                    </Tag>
                  ))
                ) : (
                  <Tag style={{ borderRadius: 6 }}>User</Tag>
                )}
              </Descriptions.Item>
            </Descriptions>
          </Card>
        </Flex>
      </Content>
    </Layout>
  )
}
