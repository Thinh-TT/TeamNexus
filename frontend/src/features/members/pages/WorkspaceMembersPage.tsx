import React, { useCallback, useEffect, useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Flex,
  Layout,
  Result,
  Space,
  Tabs,
  Typography,
  message,
} from 'antd'
import {
  ArrowLeftOutlined,
  MailOutlined,
  ReloadOutlined,
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
          <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
            <TeamOutlined style={{ marginRight: 8, color: '#6366f1' }} />
            Quản lý thành viên
          </Typography.Title>
        </Flex>

        <Space>
          <Button icon={<ReloadOutlined />} onClick={fetchData} loading={loading}>
            Làm mới
          </Button>

          {isManagerOrAdmin && (
            <>
              <Button
                icon={<MailOutlined />}
                onClick={() => setQuickEmailModalOpen(true)}
                data-testid="quick-email-btn"
              >
                Gửi email nhanh
              </Button>
              <Button
                type="primary"
                icon={<UserAddOutlined />}
                onClick={() => setInviteModalOpen(true)}
                data-testid="invite-member-btn"
                style={{ backgroundColor: '#6366f1' }}
              >
                Mời thành viên
              </Button>
            </>
          )}
        </Space>
      </div>

      <Content style={{ padding: '24px 32px', maxWidth: 1100, margin: '0 auto', width: '100%' }}>
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
