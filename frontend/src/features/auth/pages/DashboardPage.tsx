import React, { useEffect, useState } from 'react'
import {
  AppstoreOutlined,
  DashboardOutlined,
  FolderOpenOutlined,
  ProjectOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Descriptions,
  Empty,
  Flex,
  Input,
  Layout,
  List,
  Space,
  Tag,
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

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <AppHeader workspaceId={workspaces[0]?.id} />

      <Content style={{ padding: '32px 24px', maxWidth: 960, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          <Card
            title="Thông tin Tài khoản"
            style={{ borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.05)' }}
          >
            <Descriptions column={{ xs: 1, sm: 2 }}>
              <Descriptions.Item label="Tên hiển thị">
                {user?.displayName ?? 'Chưa cập nhật'}
              </Descriptions.Item>
              <Descriptions.Item label="Email">{user?.email}</Descriptions.Item>
              <Descriptions.Item label="ID">{user?.id}</Descriptions.Item>
              <Descriptions.Item label="Vai trò hệ thống">
                {user?.roles && user.roles.length > 0 ? (
                  user.roles.map((role) => (
                    <Tag
                      color={role === 'Admin' ? 'red' : role === 'Manager' ? 'gold' : 'blue'}
                      key={role}
                    >
                      {role}
                    </Tag>
                  ))
                ) : (
                  <Tag>User</Tag>
                )}
              </Descriptions.Item>
            </Descriptions>
          </Card>

          <Card
            title={
              <Flex align="center" gap={8}>
                <AppstoreOutlined style={{ color: '#6366f1' }} />
                <span>Workspace của bạn</span>
              </Flex>
            }
            style={{ borderRadius: 12, boxShadow: '0 4px 12px rgba(0,0,0,0.05)' }}
          >
            <Typography.Paragraph type="secondary">
              Danh sách các Không gian làm việc mà bạn đang tham gia:
            </Typography.Paragraph>

            {workspaces.length === 0 ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Bạn chưa tham gia Không gian làm việc nào"
              />
            ) : (
              <List
                dataSource={workspaces}
                renderItem={(ws) => (
                  <List.Item
                    key={ws.id}
                    style={{
                      padding: '16px',
                      border: '1px solid #e2e8f0',
                      borderRadius: 8,
                      marginBottom: 12,
                      background: '#fff',
                    }}
                  >
                    <Flex justify="space-between" align="center" style={{ width: '100%' }} wrap="wrap" gap={12}>
                      <div>
                        <Typography.Text strong style={{ fontSize: 16, color: '#0f172a' }}>
                          {ws.name}
                        </Typography.Text>
                        <Tag color="purple" style={{ marginLeft: 8 }}>
                          {ws.role}
                        </Tag>
                        {ws.description && (
                          <Typography.Paragraph type="secondary" style={{ margin: '4px 0 0 0', fontSize: 13 }}>
                            {ws.description}
                          </Typography.Paragraph>
                        )}
                        <Typography.Text type="secondary" style={{ display: 'block', fontSize: 12, marginTop: 4 }}>
                          ID: {ws.id}
                        </Typography.Text>
                      </div>

                      <Space>
                        <Button
                          type="primary"
                          icon={<DashboardOutlined />}
                          style={{ backgroundColor: '#6366f1', borderRadius: 6 }}
                          onClick={() => navigate(`/workspaces/${ws.id}/dashboard`)}
                        >
                          Tổng quan
                        </Button>
                        <Button
                          icon={<ProjectOutlined />}
                          style={{ borderRadius: 6 }}
                          onClick={() => navigate(`/workspaces/${ws.id}/boards`)}
                        >
                          Bảng Kanban
                        </Button>
                      </Space>
                    </Flex>
                  </List.Item>
                )}
              />
            )}

            <div style={{ marginTop: 24, paddingTop: 16, borderTop: '1px solid #f1f5f9' }}>
              <Typography.Text type="secondary" style={{ display: 'block', marginBottom: 8, fontSize: 13 }}>
                Hoặc nhập Workspace ID thủ công:
              </Typography.Text>
              <Flex gap={12} align="center" wrap="wrap">
                <Input
                  placeholder="Nhập Workspace ID (Guid)..."
                  id="dashboard-workspace-input"
                  value={manualWsId}
                  onChange={(e) => setManualWsId(e.target.value)}
                  style={{ maxWidth: 380, borderRadius: 8 }}
                />
                <Button
                  icon={<FolderOpenOutlined />}
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
        </Flex>
      </Content>
    </Layout>
  )
}
