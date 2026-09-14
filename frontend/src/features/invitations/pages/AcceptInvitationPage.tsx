import React, { useCallback, useEffect, useState } from 'react'
import {
  Alert,
  Avatar,
  Button,
  Card,
  Descriptions,
  Divider,
  Flex,
  Layout,
  Result,
  Spin,
  Tag,
  Typography,
  message,
} from 'antd'
import {
  CheckCircleOutlined,
  GithubOutlined,
  GoogleOutlined,
  MailOutlined,
  ProjectOutlined,
} from '@ant-design/icons'
import dayjs from 'dayjs'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useAuthStore } from '../../auth/store/useAuthStore'
import { getRoleLabel, getRoleTagColor } from '../../members/utils/memberRoleLabels'
import type { InvitationPreviewResponse } from '../../members/types/member.types'
import { invitationApi } from '../services/invitationApi'
import {
  clearPendingInviteToken,
  getPendingInviteToken,
  savePendingInviteToken,
} from '../utils/pendingInvite'

const { Header, Content } = Layout

export const AcceptInvitationPage: React.FC = () => {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const { isAuthenticated, loginWithProvider, user } = useAuthStore()

  // Ưu tiên đọc từ URL, nếu không có thì đọc từ sessionStorage
  const tokenFromUrl = searchParams.get('token')
  const [token] = useState<string | null>(() => {
    if (tokenFromUrl && tokenFromUrl.trim() !== '') {
      savePendingInviteToken(tokenFromUrl)
      return tokenFromUrl.trim()
    }
    return getPendingInviteToken()
  })

  const [loadingPreview, setLoadingPreview] = useState(false)
  const [preview, setPreview] = useState<InvitationPreviewResponse | null>(null)
  const [accepting, setAccepting] = useState(false)
  const [errorCode, setErrorCode] = useState<number | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const fetchPreview = useCallback(async (activeToken: string) => {
    try {
      setLoadingPreview(true)
      setErrorCode(null)
      setErrorMessage(null)
      const data = await invitationApi.previewInvitation(activeToken)
      setPreview(data)
    } catch (err: any) {
      const status = err?.response?.status
      if (status === 410 || status === 404 || status === 403 || status === 400) {
        setErrorCode(status)
        setErrorMessage(err?.response?.data?.error || null)
        clearPendingInviteToken()
      } else {
        const msg = err?.response?.data?.error || 'Không thể tải thông tin lời mời'
        message.error(msg)
        setErrorMessage(msg)
      }
    } finally {
      setLoadingPreview(false)
    }
  }, [])

  useEffect(() => {
    if (token && isAuthenticated) {
      Promise.resolve().then(() => {
        fetchPreview(token)
      })
    }
  }, [token, isAuthenticated, fetchPreview])

  const handleAccept = async () => {
    if (!token) return
    try {
      setAccepting(true)
      const res = await invitationApi.acceptInvitation(token)
      clearPendingInviteToken()

      if (res.alreadyMember) {
        message.info('Bạn đã là thành viên của không gian làm việc này.')
      } else {
        message.success('Tham gia không gian làm việc thành công!')
      }

      navigate(`/workspaces/${res.workspaceId}/boards`)
    } catch (err: any) {
      const status = err?.response?.status
      if (status === 403 || status === 410 || status === 404) {
        setErrorCode(status)
        setErrorMessage(err?.response?.data?.error || null)
        clearPendingInviteToken()
      } else {
        const msg = err?.response?.data?.error || 'Không thể chấp nhận lời mời'
        message.error(msg)
      }
    } finally {
      setAccepting(false)
    }
  }

  const handleDecline = () => {
    clearPendingInviteToken()
    message.info('Đã từ chối lời mời')
    navigate('/')
  }

  // 1. Trường hợp không có token
  if (!token) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Result
          status="error"
          title="400"
          subTitle="Mã lời mời không hợp lệ hoặc bị thiếu."
          extra={
            <Button type="primary" onClick={() => navigate('/')}>
              Về trang chủ
            </Button>
          }
        />
      </Layout>
    )
  }

  // 2. Trường hợp gặp các mã lỗi API (403, 404, 410)
  if (errorCode === 403) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Result
          status="403"
          title="403 - Không có quyền truy cập"
          subTitle={
            errorMessage ||
            'Lời mời này được gửi tới một địa chỉ email khác. Vui lòng đăng nhập đúng tài khoản email được mời.'
          }
          extra={[
            <Button key="home" onClick={() => navigate('/')}>
              Về trang chủ
            </Button>,
          ]}
        />
      </Layout>
    )
  }

  if (errorCode === 410) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Result
          status="warning"
          title="410 - Lời mời không còn hiệu lực"
          subTitle={
            errorMessage ||
            'Lời mời này đã hết hạn hoặc đã bị người quản trị huỷ.'
          }
          extra={
            <Button type="primary" onClick={() => navigate('/')}>
              Về trang chủ
            </Button>
          }
        />
      </Layout>
    )
  }

  if (errorCode === 404) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Result
          status="404"
          title="404 - Không tìm thấy"
          subTitle="Mã lời mời không tồn tại trên hệ thống."
          extra={
            <Button type="primary" onClick={() => navigate('/')}>
              Về trang chủ
            </Button>
          }
        />
      </Layout>
    )
  }

  // 3. Trường hợp chưa đăng nhập
  if (!isAuthenticated) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Header style={{ background: '#0f172a', padding: '0 24px', display: 'flex', alignItems: 'center' }}>
          <Typography.Title level={3} style={{ color: '#fff', margin: 0 }}>
            TeamNexus
          </Typography.Title>
        </Header>

        <Content style={{ padding: '60px 24px', display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
          <Card
            style={{
              maxWidth: 480,
              width: '100%',
              borderRadius: 12,
              border: '1px solid #e2e8f0',
              boxShadow: '0 4px 12px rgba(0,0,0,0.05)',
              textAlign: 'center',
              padding: '12px 16px',
            }}
          >
            <Avatar
              size={56}
              icon={<MailOutlined />}
              style={{ backgroundColor: '#6366f1', marginBottom: 16 }}
            />
            <Typography.Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
              Bạn nhận được lời mời tham gia workspace
            </Typography.Title>
            <Typography.Paragraph type="secondary" style={{ fontSize: 14, marginBottom: 24 }}>
              Để tiếp tục và chấp nhận lời mời này, vui lòng đăng nhập bằng tài khoản liên kết với địa chỉ email được mời.
            </Typography.Paragraph>

            <Flex vertical gap={12}>
              <Button
                type="primary"
                size="large"
                icon={<GithubOutlined />}
                style={{ backgroundColor: '#24292e', borderColor: '#24292e' }}
                onClick={() => loginWithProvider('github')}
              >
                Đăng nhập bằng GitHub
              </Button>
              <Button
                size="large"
                icon={<GoogleOutlined />}
                onClick={() => loginWithProvider('google')}
              >
                Đăng nhập bằng Google
              </Button>
            </Flex>
          </Card>
        </Content>
      </Layout>
    )
  }

  // 4. Đã đăng nhập và đang tải thông tin preview
  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <Header style={{ background: '#0f172a', padding: '0 24px', display: 'flex', alignItems: 'center' }}>
        <Typography.Title level={3} style={{ color: '#fff', margin: 0, cursor: 'pointer' }} onClick={() => navigate('/')}>
          TeamNexus
        </Typography.Title>
      </Header>

      <Content style={{ padding: '60px 24px', display: 'flex', justifyContent: 'center', alignItems: 'center' }}>
        {loadingPreview ? (
          <Card style={{ padding: 40, borderRadius: 12, textAlign: 'center', minWidth: 360 }}>
            <Spin size="large" description="Đang kiểm tra lời mời..." />
          </Card>
        ) : preview ? (
          <Card
            style={{
              maxWidth: 540,
              width: '100%',
              borderRadius: 12,
              border: '1px solid #e2e8f0',
              boxShadow: '0 4px 12px rgba(0,0,0,0.05)',
              padding: '12px 16px',
            }}
          >
            <Flex align="center" gap={16} style={{ marginBottom: 20 }}>
              <Avatar
                size={54}
                shape="square"
                icon={<ProjectOutlined />}
                style={{ backgroundColor: '#4f46e5', fontSize: 24 }}
              >
                {preview.workspaceName?.[0]?.toUpperCase()}
              </Avatar>
              <div>
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  Lời mời tham gia
                </Typography.Text>
                <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
                  {preview.workspaceName}
                </Typography.Title>
              </div>
            </Flex>

            <Divider style={{ margin: '12px 0 16px 0' }} />

            <Descriptions column={1} size="small" bordered style={{ marginBottom: 24, borderRadius: 8 }}>
              <Descriptions.Item label="Người mời">
                <strong>{preview.invitedByName}</strong>
              </Descriptions.Item>
              <Descriptions.Item label="Email được mời">
                <span>{preview.invitedEmail}</span>
              </Descriptions.Item>
              <Descriptions.Item label="Vai trò sẽ nhận">
                <Tag color={getRoleTagColor(preview.invitedRole)}>
                  {getRoleLabel(preview.invitedRole)}
                </Tag>
              </Descriptions.Item>
              <Descriptions.Item label="Hạn hiệu lực">
                {preview.expiresAt ? dayjs(preview.expiresAt).format('HH:mm DD/MM/YYYY') : '—'}
              </Descriptions.Item>
            </Descriptions>

            {user?.email && user.email.toLowerCase() !== preview.invitedEmail.toLowerCase() && (
              <Alert
                type="warning"
                showIcon
                style={{ marginBottom: 20 }}
                message="Email tài khoản không khớp"
                description={`Bạn đang đăng nhập bằng ${user.email}, nhưng lời mời được gửi tới ${preview.invitedEmail}. Hệ thống sẽ từ chối nếu không trùng khớp email.`}
              />
            )}

            <Flex justify="flex-end" gap={12}>
              <Button onClick={handleDecline}>
                Từ chối
              </Button>
              <Button
                type="primary"
                icon={<CheckCircleOutlined />}
                loading={accepting}
                onClick={handleAccept}
                style={{ backgroundColor: '#16a34a', borderColor: '#16a34a' }}
              >
                Tham gia ngay
              </Button>
            </Flex>
          </Card>
        ) : (
          <Card style={{ padding: 40, borderRadius: 12, textAlign: 'center', minWidth: 360 }}>
            <Typography.Text type="secondary">
              Không thể hiển thị thông tin lời mời.
            </Typography.Text>
          </Card>
        )}
      </Content>
    </Layout>
  )
}
