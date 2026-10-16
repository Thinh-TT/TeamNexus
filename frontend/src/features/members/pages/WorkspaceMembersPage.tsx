import React, { useCallback, useEffect, useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Col,
  Flex,
  Layout,
  Result,
  Row,
  Space,
  Tabs,
  Tag,
  Typography,
  message,
} from 'antd'
import {
  ArrowLeftOutlined,
  MailOutlined,
  ReloadOutlined,
  RobotOutlined,
  TeamOutlined,
  UserAddOutlined,
} from '@ant-design/icons'
import { useNavigate, useParams } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/useAuth'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { AppHeader } from '../../../shared/components/AppHeader'
import { memberApi } from '../services/memberApi'
import type {
  CreateInvitationRequest,
  InvitationResponse,
  QuickEmailRequest,
  QuickEmailResult,
  WorkspaceMemberResponse,
  WorkspaceRole,
} from '../types/member.types'
import { MembersTable } from '../components/MembersTable'
import { PendingInvitationsTable } from '../components/PendingInvitationsTable'
import { InviteMemberModal } from '../components/InviteMemberModal'
import { QuickEmailModal } from '../components/QuickEmailModal'

const { Content } = Layout

export const WorkspaceMembersPage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()
  const { user } = useAuth()
  const { isManagerOrAdmin, isAdmin, loading: roleLoading } = useWorkspaceRole(workspaceId)

  const [members, setMembers] = useState<WorkspaceMemberResponse[]>([])
  const [invitations, setInvitations] = useState<InvitationResponse[]>([])
  const [loading, setLoading] = useState<boolean>(true)
  const [error, setError] = useState<string | null>(null)
  const [forbidden, setForbidden] = useState<boolean>(false)

  const [inviteModalOpen, setInviteModalOpen] = useState(false)
  const [quickEmailModalOpen, setQuickEmailModalOpen] = useState(false)

  const fetchData = useCallback(async () => {
    if (!workspaceId) return
    try {
      setLoading(true)
      setError(null)
      setForbidden(false)

      const membersData = await memberApi.listMembers(workspaceId)
      setMembers(membersData)

      if (isManagerOrAdmin) {
        try {
          const invData = await memberApi.listInvitations(workspaceId)
          setInvitations(invData)
        } catch {
          // Non-blocking if invitations fail
        }
      }
    } catch (err: any) {
      if (err?.response?.status === 403 || err?.response?.status === 404) {
        setForbidden(true)
      } else {
        setError(err?.response?.data?.error || err?.message || 'Không thể tải danh sách thành viên')
      }
    } finally {
      setLoading(false)
    }
  }, [workspaceId, isManagerOrAdmin])

  useEffect(() => {
    if (!roleLoading) {
      Promise.resolve().then(() => {
        fetchData()
      })
    }
  }, [fetchData, roleLoading])

  const handleRoleChange = async (targetUserId: string, newRole: WorkspaceRole) => {
    try {
      await memberApi.updateMemberRole(workspaceId, targetUserId, newRole)
      message.success('Cập nhật vai trò thành công')
      fetchData()
    } catch (err: any) {
      message.error(err?.response?.data?.error || 'Không thể cập nhật vai trò')
    }
  }

  const handleRemoveMember = async (targetUserId: string) => {
    try {
      await memberApi.removeMember(workspaceId, targetUserId)
      message.success('Đã xoá thành viên khỏi workspace')
      fetchData()
    } catch (err: any) {
      message.error(err?.response?.data?.error || 'Không thể xoá thành viên')
    }
  }

  const handleInvite = async (
    data: CreateInvitationRequest
  ): Promise<InvitationResponse | undefined> => {
    try {
      const created = await memberApi.createInvitation(workspaceId, data)
      if (created.emailSent) {
        message.success(`Đã gửi lời mời tới ${data.email}`)
      } else {
        message.warning('Đã tạo lời mời nhưng gửi email thất bại')
      }
      fetchData()
      return created
    } catch (err: any) {
      message.error(err?.response?.data?.error || 'Không thể tạo lời mời')
      return undefined
    }
  }

  const handleResend = async (
    oldInvitationId: string,
    email: string,
    roleToInvite: WorkspaceRole
  ) => {
    try {
      // Huỷ lời mời cũ trước rồi tạo lại lời mời mới
      await memberApi.cancelInvitation(workspaceId, oldInvitationId)
      const created = await memberApi.createInvitation(workspaceId, {
        email,
        role: roleToInvite,
      })
      if (created.emailSent) {
        message.success(`Đã gửi lại lời mời tới ${email}`)
      } else {
        message.warning('Gửi lại lời mời vẫn chưa thể gửi email thành công')
      }
      fetchData()
    } catch (err: any) {
      message.error(err?.response?.data?.error || 'Không thể gửi lại lời mời')
    }
  }

  const handleCancelInvitation = async (invitationId: string) => {
    try {
      await memberApi.cancelInvitation(workspaceId, invitationId)
      message.success('Đã huỷ lời mời')
      fetchData()
    } catch (err: any) {
      message.error(err?.response?.data?.error || 'Không thể huỷ lời mời')
    }
  }

  const handleSendQuickEmail = async (
    data: QuickEmailRequest
  ): Promise<QuickEmailResult | undefined> => {
    try {
      const res = await memberApi.sendQuickEmail(workspaceId, data)
      if (res.sent > 0) {
        message.success(`Đã gửi thành công ${res.sent}/${res.requested} email`)
      }
      return res
    } catch (err: any) {
      if (err?.response?.status === 429) {
        message.error(
          err?.response?.data?.error ||
            'Bạn đã vượt quá giới hạn gửi email trong 1 giờ. Vui lòng thử lại sau.'
        )
      } else {
        message.error(err?.response?.data?.error || 'Không thể gửi email nhanh')
      }
      return undefined
    }
  }

  if (forbidden) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <AppHeader />
        <Result
          status="403"
          title="403"
          subTitle="Bạn không có quyền truy cập vào danh sách thành viên của workspace này."
          extra={
            <Button type="primary" onClick={() => navigate('/')}>
              Về trang chủ
            </Button>
          }
        />
      </Layout>
    )
  }

  const pendingCount = invitations.filter((i: InvitationResponse) => i.status === 'Pending').length

  const tabItems = [
    {
      key: 'members',
      label: (
        <span>
          <TeamOutlined style={{ marginRight: 6 }} />
          Thành viên ({members.length})
        </span>
      ),
      children: (
        <MembersTable
          members={members}
          loading={loading}
          canManage={isAdmin}
          currentUserId={user?.id}
          onRoleChange={handleRoleChange}
          onRemoveMember={handleRemoveMember}
        />
      ),
    },
  ]

  if (isManagerOrAdmin) {
    tabItems.push({
      key: 'invitations',
      label: (
        <span>
          <MailOutlined style={{ marginRight: 6 }} />
          Lời mời đang chờ ({pendingCount})
        </span>
      ),
      children: (
        <PendingInvitationsTable
          invitations={invitations}
          loading={loading}
          onCancelInvitation={handleCancelInvitation}
        />
      ),
    })
  }

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId} />

      {/* Subheader Toolbar */}
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
          <Typography.Title level={4} style={{ margin: 0, color: '#0f172a', fontWeight: 700 }}>
            <TeamOutlined style={{ marginRight: 8, color: '#4f46e5' }} />
            Quản lý thành viên
          </Typography.Title>
          <Tag
            style={{
              margin: 0,
              borderRadius: 999,
              fontSize: 11,
              fontWeight: 600,
              color: '#7c3aed',
              backgroundColor: '#f5f3ff',
              borderColor: '#ddd6fe',
            }}
          >
            Human + AI Workforce
          </Tag>
        </Flex>

        <Space>
          <Button icon={<ReloadOutlined />} onClick={fetchData} loading={loading} style={{ borderRadius: 8 }}>
            Làm mới
          </Button>

          {isManagerOrAdmin && (
            <>
              <Button
                icon={<MailOutlined />}
                onClick={() => setQuickEmailModalOpen(true)}
                data-testid="quick-email-btn"
                style={{ borderRadius: 8 }}
              >
                Gửi email nhanh
              </Button>
              <Button
                type="primary"
                icon={<UserAddOutlined />}
                onClick={() => setInviteModalOpen(true)}
                data-testid="invite-member-btn"
                style={{ backgroundColor: '#4f46e5', borderColor: '#4f46e5', borderRadius: 8, fontWeight: 600 }}
              >
                Mời thành viên
              </Button>
            </>
          )}
        </Space>
      </div>

      <Content style={{ padding: '24px 32px', maxWidth: 1180, margin: '0 auto', width: '100%' }}>
        {error && (
          <Alert
            type="error"
            message="Lỗi tải dữ liệu"
            description={error}
            showIcon
            closable
            style={{ marginBottom: 16 }}
          />
        )}

        {/* Autonomous AI Agent Personas Showcase */}
        <Card
          size="small"
          style={{
            marginBottom: 16,
            borderRadius: 12,
            border: '1px solid #ddd6fe',
            background: 'linear-gradient(135deg, #faf5ff 0%, #f5f3ff 55%, #ffffff 100%)',
          }}
          styles={{ body: { padding: '14px 18px' } }}
        >
          <Flex justify="space-between" align="center" wrap="wrap" gap={12} style={{ marginBottom: 12 }}>
            <Flex align="center" gap={8}>
              <div
                style={{
                  width: 26,
                  height: 26,
                  borderRadius: 7,
                  background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: '#ffffff',
                }}
              >
                <RobotOutlined style={{ fontSize: 13 }} />
              </div>
              <Typography.Text strong style={{ fontSize: 13.5, color: '#0f172a' }}>
                Autonomous AI Agent Personas
              </Typography.Text>
              <Tag
                style={{
                  margin: 0,
                  borderRadius: 999,
                  fontSize: 10.5,
                  fontWeight: 600,
                  color: '#6d28d9',
                  backgroundColor: '#ede9fe',
                  borderColor: '#ddd6fe',
                }}
              >
                5 Chuyên gia AI sẵn sàng nhận việc
              </Tag>
            </Flex>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Nhấp vào thẻ AI Persona để lọc nhanh các công việc đang phụ trách
            </Typography.Text>
          </Flex>

          <Row gutter={[10, 10]}>
            {[
              { code: '@Researcher', role: 'Phân tích & RAG Context', color: '#4f46e5', bg: '#eef2ff' },
              { code: '@Developer', role: 'Thiết kế kỹ thuật & Subtasks', color: '#7c3aed', bg: '#f5f3ff' },
              { code: '@Tester', role: 'Test Cases & Edge Verification', color: '#0284c7', bg: '#f0f9ff' },
              { code: '@Reviewer', role: 'Kiểm định chất lượng & DoD', color: '#059669', bg: '#ecfdf5' },
              { code: '@Writer', role: 'Tài liệu kỹ thuật & Release Notes', color: '#d97706', bg: '#fffbeb' },
            ].map((persona) => (
              <Col xs={24} sm={12} md={8} lg={4} key={persona.code} style={{ flex: '1 1 180px' }}>
                <div
                  role="button"
                  tabIndex={0}
                  onClick={() =>
                    navigate(
                      `/workspaces/${workspaceId}/search?q=${encodeURIComponent(
                        persona.code.replace('@', '')
                      )}`
                    )
                  }
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' || e.key === ' ') {
                      e.preventDefault()
                      navigate(
                        `/workspaces/${workspaceId}/search?q=${encodeURIComponent(
                          persona.code.replace('@', '')
                        )}`
                      )
                    }
                  }}
                  style={{
                    padding: '10px 12px',
                    borderRadius: 8,
                    backgroundColor: '#ffffff',
                    border: '1px solid #e2e8f0',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 4,
                    cursor: 'pointer',
                    transition: 'all 0.16s ease',
                    boxShadow: '0 1px 2px rgba(15, 23, 42, 0.03)',
                  }}
                  onMouseEnter={(e) => {
                    e.currentTarget.style.borderColor = persona.color
                    e.currentTarget.style.transform = 'translateY(-1px)'
                    e.currentTarget.style.boxShadow = '0 4px 10px rgba(79, 70, 229, 0.1)'
                  }}
                  onMouseLeave={(e) => {
                    e.currentTarget.style.borderColor = '#e2e8f0'
                    e.currentTarget.style.transform = 'translateY(0)'
                    e.currentTarget.style.boxShadow = '0 1px 2px rgba(15, 23, 42, 0.03)'
                  }}
                >
                  <Flex align="center" justify="space-between">
                    <span
                      style={{
                        fontSize: 12,
                        fontWeight: 700,
                        color: persona.color,
                        fontFamily: "'JetBrains Mono', monospace",
                      }}
                    >
                      {persona.code}
                    </span>
                    <span
                      style={{
                        fontSize: 10.5,
                        fontWeight: 600,
                        color: '#059669',
                        backgroundColor: '#ecfdf5',
                        padding: '0 6px',
                        borderRadius: 999,
                      }}
                    >
                      Sẵn sàng →
                    </span>
                  </Flex>
                  <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                    {persona.role}
                  </Typography.Text>
                </div>
              </Col>
            ))}
          </Row>
        </Card>

        <Card style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}>
          <Tabs defaultActiveKey="members" items={tabItems} />
        </Card>
      </Content>

      <InviteMemberModal
        open={inviteModalOpen}
        onClose={() => setInviteModalOpen(false)}
        onInvite={handleInvite}
        onResend={handleResend}
      />

      <QuickEmailModal
        open={quickEmailModalOpen}
        onClose={() => setQuickEmailModalOpen(false)}
        members={members}
        onSend={handleSendQuickEmail}
      />
    </Layout>
  )
}
