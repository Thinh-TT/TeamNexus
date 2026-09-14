import React, { useEffect, useState } from 'react'
import {
  Avatar,
  Button,
  Card,
  Descriptions,
  Flex,
  Input,
  Layout,
  Result,
  Space,
  Tag,
  Typography,
  message,
} from 'antd'
import { httpClient } from '../../../shared/api'
import { useAuth } from '../hooks/useAuth'
import { workspaceApi } from '../../workspace/services/workspaceApi'

const { Header, Content } = Layout

export const DashboardPage: React.FC = () => {
  const { user, logout } = useAuth()
  const [workspace, setWorkspace] = useState<{ id: string; name: string } | null>(null)
  const [rbacLoading, setRbacLoading] = useState<string | null>(null)
  const [rbacResult, setRbacResult] = useState<{ status: 'success' | 'error'; msg: string } | null>(
    null
  )

  useEffect(() => {
    workspaceApi
      .list()
      .then((list) => {
        if (list && list.length > 0) {
          setWorkspace(list[0])
        }
      })
      .catch(() => {})
  }, [])

  const handleTestEndpoint = async (endpoint: string, label: string) => {
    setRbacLoading(endpoint)
    setRbacResult(null)
    try {
      const res = await httpClient.get<{ message?: string; displayName?: string }>(endpoint)
      const msgText = res.data?.message ?? JSON.stringify(res.data)
      setRbacResult({ status: 'success', msg: `[${label}] Success: ${msgText}` })
      message.success(`Gọi endpoint ${label} thành công!`)
    } catch (err: unknown) {
      const errorResponse = (err as { response?: { status?: number; data?: { error?: string } } })
        ?.response
      const statusCode = errorResponse?.status ?? 500
      const errorMsg =
        statusCode === 403
          ? '403 Forbidden (Bạn không có quyền truy cập endpoint này)'
          : statusCode === 401
          ? '401 Unauthorized (Phiên làm việc hết hạn)'
          : `${statusCode} Error: ${errorResponse?.data?.error ?? 'Lỗi không xác định'}`
      setRbacResult({ status: 'error', msg: `[${label}] ${errorMsg}` })
      message.error(`Gọi endpoint ${label} thất bại: ${errorMsg}`)
    } finally {
      setRbacLoading(null)
    }
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <Header
        style={{
          background: '#0f172a',
          padding: '0 24px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Typography.Title level={3} style={{ color: '#fff', margin: 0 }}>
          TeamNexus
        </Typography.Title>
        <Space size="middle">
          <Avatar src={user?.avatarUrl} style={{ backgroundColor: '#6366f1' }}>
            {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
          </Avatar>
          <Typography.Text style={{ color: '#fff', fontWeight: 500 }}>
            {user?.displayName ?? user?.email}
          </Typography.Text>
          <Button type="primary" danger onClick={() => logout()}>
            Đăng xuất
          </Button>
        </Space>
      </Header>

      <Content style={{ padding: '32px 24px', maxWidth: 960, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          <Card title="Thông tin Tài khoản" style={{ borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.05)' }}>
            <Descriptions column={{ xs: 1, sm: 2 }}>
              <Descriptions.Item label="Tên hiển thị">{user?.displayName ?? 'Chưa cập nhật'}</Descriptions.Item>
              <Descriptions.Item label="Email">{user?.email}</Descriptions.Item>
              <Descriptions.Item label="ID">{user?.id}</Descriptions.Item>
              <Descriptions.Item label="Vai trò (Roles)">
                {user?.roles && user.roles.length > 0 ? (
                  user.roles.map((role) => (
                    <Tag color={role === 'Admin' ? 'red' : role === 'Manager' ? 'gold' : 'blue'} key={role}>
                      {role}
                    </Tag>
                  ))
                ) : (
                  <Tag>No role</Tag>
                )}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card
            title={
              <Flex align="center" gap={8}>
                <span style={{ fontSize: 16 }}>📊 Không Gian Làm Việc (Kanban Boards - Phase 2)</span>
              </Flex>
            }
            style={{ borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.05)' }}
          >
            <Typography.Paragraph type="secondary">
              Truy cập các bảng Kanban của Workspace để quản lý công việc và cộng tác thời gian thực:
            </Typography.Paragraph>

            <Flex gap={12} align="center" wrap="wrap">
              <Input
                placeholder="Nhập Workspace ID (Guid)..."
                id="dashboard-workspace-input"
                value={workspace?.id ?? ''}
                onChange={(e) =>
                  setWorkspace((prev) =>
                    prev ? { ...prev, id: e.target.value } : { id: e.target.value, name: 'Workspace' }
                  )
                }
                style={{ maxWidth: 380, borderRadius: 8 }}
              />
              <Button
                type="primary"
                style={{ backgroundColor: '#6366f1', borderRadius: 8 }}
                onClick={() => {
                  const input = document.getElementById('dashboard-workspace-input') as HTMLInputElement
                  const wsId = input?.value.trim() || workspace?.id || '00000000-0000-0000-0000-000000000001'
                  window.location.href = `/workspaces/${wsId}/boards`
                }}
              >
                Mở Danh Sách Bảng {workspace?.name ? `(${workspace.name})` : ''} →
              </Button>
            </Flex>
          </Card>

          <Card title="Kiểm tra Phân quyền RBAC (Phase 1 §3.3)" style={{ borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.05)' }}>
            <Typography.Paragraph type="secondary">
              Nhấn các nút bên dưới để thử nghiệm gửi request tới các endpoints backend được bảo vệ bởi Policy-based Authorization:
            </Typography.Paragraph>

            <Space wrap size="middle" style={{ marginBottom: 16 }}>
              <Button
                type="default"
                loading={rbacLoading === '/auth/me'}
                onClick={() => handleTestEndpoint('/auth/me', 'Member Policy (/api/auth/me)')}
              >
                Test Member Access
              </Button>
              <Button
                type="default"
                loading={rbacLoading === '/manager/ping'}
                onClick={() => handleTestEndpoint('/manager/ping', 'Manager Policy (/api/manager/ping)')}
              >
                Test Manager Access
              </Button>
              <Button
                type="default"
                loading={rbacLoading === '/admin/ping'}
                onClick={() => handleTestEndpoint('/admin/ping', 'Admin Policy (/api/admin/ping)')}
              >
                Test Admin Access
              </Button>
            </Space>

            {rbacResult && (
              <Result
                status={rbacResult.status}
                title={rbacResult.status === 'success' ? 'Truy cập Thành công' : 'Truy cập Bị từ chối / Lỗi'}
                subTitle={rbacResult.msg}
                style={{ padding: '16px 0 0 0' }}
              />
            )}
          </Card>
        </Flex>
      </Content>
    </Layout>
  )
}
