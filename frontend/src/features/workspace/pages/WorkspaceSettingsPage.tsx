import React, { useState } from 'react'
import {
  ArrowLeftOutlined,
  CalendarOutlined,
  EditOutlined,
  ProjectOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Col,
  Descriptions,
  Flex,
  Layout,
  Result,
  Row,
  Spin,
  Statistic,
  Tag,
  Typography,
} from 'antd'
import dayjs from 'dayjs'
import { useNavigate, useParams } from 'react-router-dom'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { useWorkspaceMembers } from '../../board/hooks/useWorkspaceMembers'
import { WorkspaceSettingsModal } from '../components/WorkspaceSettingsModal'
import { useWorkspaceDetail } from '../hooks/useWorkspaceDetail'
import { AppHeader } from '../../../shared/components/AppHeader'

const { Content } = Layout

export const WorkspaceSettingsPage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()

  const { isManagerOrAdmin, isAdmin, isOwner, loading: roleLoading } = useWorkspaceRole(workspaceId)
  const { detail, loading: detailLoading, save, transfer, remove } = useWorkspaceDetail(workspaceId)
  const { members } = useWorkspaceMembers(workspaceId)

  const [modalOpen, setModalOpen] = useState(false)

  if (roleLoading || detailLoading) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '60vh' }}>
        <Spin size="large" />
      </Flex>
    )
  }

  if (!isManagerOrAdmin) {
    return (
      <Result
        status="403"
        title="403"
        subTitle="Bạn không có quyền quản lý không gian làm việc này. Chỉ Manager hoặc Admin mới có quyền truy cập."
        extra={
          <Button type="primary" onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}>
            Quay lại bảng làm việc
          </Button>
        }
      />
    )
  }

  if (!detail) {
    return (
      <Result
        status="404"
        title="404"
        subTitle="Không tìm thấy thông tin không gian làm việc."
        extra={
          <Button type="primary" onClick={() => navigate('/')}>
            Về trang chủ
          </Button>
        }
      />
    )
  }

  const handleDelete = async () => {
    const success = await remove()
    if (success) {
      navigate('/')
      return true
    }
    return false
  }

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId} />

      <div
        style={{
          backgroundColor: '#ffffff',
          borderBottom: '1px solid #e2e8f0',
          padding: '0 24px',
          height: 64,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Flex align="center" gap={12}>
          <Button
            type="text"
            icon={<ArrowLeftOutlined />}
            onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
          />
          <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
            Cài đặt không gian làm việc
          </Typography.Title>
        </Flex>

        <Button
          type="primary"
          icon={<EditOutlined />}
          style={{ backgroundColor: '#6366f1' }}
          onClick={() => setModalOpen(true)}
          data-testid="open-settings-modal-btn"
        >
          Chỉnh Sửa Cài Đặt
        </Button>
      </div>

      <Content style={{ padding: '24px 32px', maxWidth: 1000, margin: '0 auto', width: '100%' }}>
        <Row gutter={[24, 24]}>
          {/* Quick stats */}
          <Col xs={24} sm={12}>
            <Card style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}>
              <Statistic
                title="Số thành viên"
                value={detail.memberCount}
                prefix={<TeamOutlined style={{ color: '#6366f1' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={12}>
            <Card style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}>
              <Statistic
                title="Số bảng Kanban"
                value={detail.boardCount}
                prefix={<ProjectOutlined style={{ color: '#0ea5e9' }} />}
              />
            </Card>
          </Col>

          {/* Details Card */}
          <Col xs={24}>
            <Card
              title="Thông tin chung"
              style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
            >
              <Descriptions bordered column={{ xs: 1, sm: 2 }}>
                <Descriptions.Item label="Tên không gian làm việc" span={2}>
                  <Typography.Text strong style={{ fontSize: 16 }}>
                    {detail.name}
                  </Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="Mô tả" span={2}>
                  {detail.description || (
                    <Typography.Text type="secondary">Chưa có mô tả</Typography.Text>
                  )}
                </Descriptions.Item>
                <Descriptions.Item label="Chủ sở hữu">
                  <Flex align="center" gap={6}>
                    <UserOutlined style={{ color: '#6366f1' }} />
                    <span>{detail.ownerDisplayName}</span>
                  </Flex>
                </Descriptions.Item>
                <Descriptions.Item label="Vai trò của bạn">
                  <Tag color="blue">{detail.currentUserRole}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label="Ngày tạo">
                  <Flex align="center" gap={6}>
                    <CalendarOutlined />
                    <span>{dayjs(detail.createdAt).format('DD/MM/YYYY HH:mm')}</span>
                  </Flex>
                </Descriptions.Item>
                <Descriptions.Item label="Cập nhật lần cuối">
                  <Flex align="center" gap={6}>
                    <CalendarOutlined />
                    <span>{dayjs(detail.updatedAt).format('DD/MM/YYYY HH:mm')}</span>
                  </Flex>
                </Descriptions.Item>
              </Descriptions>
            </Card>
          </Col>
        </Row>
      </Content>

      <WorkspaceSettingsModal
        open={modalOpen}
        onClose={() => setModalOpen(false)}
        detail={detail}
        members={members}
        isManagerOrAdmin={isManagerOrAdmin}
        isAdminOrOwner={isAdmin || isOwner}
        onSave={save}
        onTransfer={(newOwnerId) => transfer({ newOwnerId })}
        onDelete={handleDelete}
      />
    </Layout>
  )
}
