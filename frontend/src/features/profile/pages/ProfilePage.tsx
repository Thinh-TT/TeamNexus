import React, { useCallback, useEffect, useState } from 'react'
import {
  Alert,
  Avatar,
  Button,
  Card,
  Empty,
  Flex,
  Form,
  Input,
  Layout,
  List,
  Popconfirm,
  Space,
  Spin,
  Switch,
  Tabs,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import {
  CrownOutlined,
  GlobalOutlined,
  LogoutOutlined,
  ProjectOutlined,
  BellOutlined,
  SaveOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { useNavigate } from 'react-router-dom'
import { useAuthStore } from '../../auth/store/useAuthStore'
import { AppHeader } from '../../../shared/components/AppHeader'
import { isSafeHref } from '../../board/utils/markdown'
import { profileApi } from '../services/profileApi'
import type { MyWorkspaceResponse, UpdateProfileRequest } from '../types/profile.types'
import { getRoleLabel, getRoleTagColor } from '../../members/utils/memberRoleLabels'

const { Content } = Layout

export const ProfilePage: React.FC = () => {
  const navigate = useNavigate()
  const { user, checkAuth } = useAuthStore()
  const [form] = Form.useForm<UpdateProfileRequest>()

  const [saving, setSaving] = useState(false)
  const [avatarPreview, setAvatarPreview] = useState<string | null>(user?.avatarUrl ?? null)

  const [workspaces, setWorkspaces] = useState<MyWorkspaceResponse[]>([])
  const [loadingWorkspaces, setLoadingWorkspaces] = useState(false)
  const [workspaceError, setWorkspaceError] = useState<string | null>(null)

  /**
   * Email tóm tắt hằng ngày (Giai đoạn 13 §3.4).
   *
   * <p>
   * Trạng thái lấy từ `GET /api/users/me` chứ **không** từ `useAuthStore`: `/api/auth/me` — endpoint
   * bootstrap của store — **không** trả `digestEnabled`, nên nếu đọc từ store thì công tắc sẽ luôn hiện
   * `false` và người dùng sẽ tưởng digest của họ đang tắt.
   * </p>
   */
  const [digestEnabled, setDigestEnabled] = useState<boolean>(true)
  const [savingDigest, setSavingDigest] = useState(false)
  const [loadingDigest, setLoadingDigest] = useState(true)

  /** Tab đang mở; mặc định mở tab "Thông báo" khi URL có `#notifications` (link "Tắt nhận" trong email). */
  const [activeTab, setActiveTab] = useState<string>(() =>
    typeof window !== 'undefined' && window.location.hash === '#notifications'
      ? 'notifications'
      : 'profile'
  )

  const fetchProfile = useCallback(async () => {
    try {
      setLoadingDigest(true)
      const profile = await profileApi.getProfile()
      setDigestEnabled(profile.digestEnabled)
    } catch {
      // Fail-soft: không tải được hồ sơ thì công tắc ở trạng thái mặc định và người dùng vẫn dùng được
      // phần còn lại của trang. Không hiện `message.error` ở đây vì lỗi này không do người dùng gây ra
      // và thao tác bật/tắt vẫn sẽ báo lỗi nếu thật sự hỏng.
    } finally {
      setLoadingDigest(false)
    }
  }, [])

  useEffect(() => {
    Promise.resolve().then(() => {
      void fetchProfile()
    })
  }, [fetchProfile])

  // Prefill form từ auth store hoặc api
  useEffect(() => {
    if (user) {
      form.setFieldsValue({
        displayName: user.displayName,
        avatarUrl: user.avatarUrl ?? '',
      })
      Promise.resolve().then(() => {
        setAvatarPreview(user.avatarUrl ?? null)
      })
    }
  }, [user, form])

  const fetchWorkspaces = useCallback(async () => {
    try {
      setLoadingWorkspaces(true)
      setWorkspaceError(null)
      const data = await profileApi.listMyWorkspaces()
      setWorkspaces(data)
    } catch (err: any) {
      const msg = err?.response?.data?.error || 'Không thể tải danh sách workspace'
      setWorkspaceError(msg)
      message.error(msg)
    } finally {
      setLoadingWorkspaces(false)
    }
  }, [])

  useEffect(() => {
    Promise.resolve().then(() => {
      fetchWorkspaces()
    })
  }, [fetchWorkspaces])

  const handleUpdateProfile = async (values: UpdateProfileRequest) => {
    try {
      setSaving(true)
      const avatarClean = values.avatarUrl?.trim() || null
      await profileApi.updateProfile({
        displayName: values.displayName.trim(),
        avatarUrl: avatarClean,
      })
      await checkAuth()
      message.success('Cập nhật hồ sơ thành công')
    } catch (err: any) {
      const msg = err?.response?.data?.error || 'Không thể cập nhật hồ sơ'
      message.error(msg)
    } finally {
      setSaving(false)
    }
  }

  const handleLeaveWorkspace = async (workspaceId: string) => {
    try {
      await profileApi.leaveWorkspace(workspaceId)
      message.success('Đã rời khỏi workspace thành công')
      fetchWorkspaces()
    } catch (err: any) {
      const msg = err?.response?.data?.error || 'Không thể rời khỏi workspace'
      message.error(msg)
    }
  }

  /**
   * Bật/tắt email tóm tắt hằng ngày (Giai đoạn 13 §3.4).
   *
   * <p>
   * Gửi **cả** `displayName`/`avatarUrl` vì `PUT /api/users/me` yêu cầu `displayName` và ghi đè hai
   * field đó — nếu chỉ gửi `digestEnabled`, tên hiển thị sẽ bị ghi rỗng.
   * </p>
   *
   * <p>
   * <b>Rollback khi lỗi:</b> công tắc đổi trước (optimistic) để bấm có cảm giác tức thì, nhưng nếu API
   * hỏng thì trả về trạng thái cũ. Giữ nguyên trạng thái mới sau khi lỗi sẽ khiến người dùng tin rằng
   * digest đã tắt trong khi server vẫn đang gửi.
   * </p>
   */
  const handleToggleDigest = async (next: boolean) => {
    const previous = digestEnabled
    setDigestEnabled(next)
    setSavingDigest(true)

    try {
      const updated = await profileApi.updateProfile({
        displayName: form.getFieldValue('displayName') || user?.displayName || '',
        avatarUrl: form.getFieldValue('avatarUrl')?.trim() || null,
        digestEnabled: next,
      })

      setDigestEnabled(updated.digestEnabled)
      message.success(next ? 'Đã bật email tóm tắt hằng ngày' : 'Đã tắt email tóm tắt hằng ngày')
    } catch (err: any) {
      setDigestEnabled(previous)
      const msg = err?.response?.data?.error || 'Không thể cập nhật tuỳ chọn thông báo'
      message.error(msg)
    } finally {
      setSavingDigest(false)
    }
  }

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <AppHeader />

      <Content style={{ padding: '32px 24px', maxWidth: 900, margin: '0 auto', width: '100%' }}>
        <Typography.Title level={3} style={{ marginBottom: 24, color: '#0f172a' }}>
          Hồ sơ người dùng
        </Typography.Title>

        <Card style={{ borderRadius: 12, border: '1px solid #e2e8f0', boxShadow: '0 1px 3px rgba(0,0,0,0.05)' }}>
          <Tabs
            activeKey={activeTab}
            onChange={setActiveTab}
            items={[
              {
                key: 'profile',
                label: (
                  <span>
                    <UserOutlined style={{ marginRight: 6 }} />
                    Thông tin cá nhân
                  </span>
                ),
                children: (
                  <div style={{ maxWidth: 600, padding: '12px 0' }}>
                    <Flex align="center" gap={20} style={{ marginBottom: 28 }}>
                      <Avatar
                        size={72}
                        src={avatarPreview}
                        icon={<UserOutlined />}
                        style={{ backgroundColor: '#6366f1', fontSize: 32 }}
                      >
                        {!avatarPreview && user?.displayName?.[0]?.toUpperCase()}
                      </Avatar>
                      <div>
                        <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
                          {user?.displayName ?? 'Người dùng'}
                        </Typography.Title>
                        <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                          {user?.email}
                        </Typography.Text>
                      </div>
                    </Flex>

                    <Form
                      form={form}
                      layout="vertical"
                      initialValues={{
                        displayName: user?.displayName,
                        avatarUrl: user?.avatarUrl ?? '',
                      }}
                      onFinish={handleUpdateProfile}
                    >
                      <Form.Item
                        label="Tên hiển thị"
                        name="displayName"
                        rules={[
                          { required: true, message: 'Vui lòng nhập tên hiển thị' },
                          { min: 1, max: 120, message: 'Tên hiển thị từ 1 đến 120 ký tự' },
                        ]}
                      >
                        <Input placeholder="Ví dụ: Nguyễn Văn A" maxLength={120} showCount />
                      </Form.Item>

                      <Form.Item
                        label="Đường dẫn Avatar (URL)"
                        name="avatarUrl"
                        rules={[
                          {
                            validator: (_, value) => {
                              if (!value || value.trim() === '') {
                                return Promise.resolve()
                              }
                              if (!isSafeHref(value)) {
                                return Promise.reject(
                                  new Error('URL avatar không an toàn. Chỉ chấp nhận liên kết http:// hoặc https://')
                                )
                              }
                              return Promise.resolve()
                            },
                          },
                        ]}
                      >
                        <Input
                          placeholder="https://example.com/avatar.png"
                          prefix={<GlobalOutlined style={{ color: '#94a3b8' }} />}
                          onChange={(e) => {
                            const val = e.target.value.trim()
                            setAvatarPreview(isSafeHref(val) ? val : null)
                          }}
                        />
                      </Form.Item>

                      <Button
                        type="primary"
                        htmlType="submit"
                        icon={<SaveOutlined />}
                        loading={saving}
                        style={{ marginTop: 8 }}
                      >
                        Lưu thay đổi
                      </Button>
                    </Form>
                  </div>
                ),
              },
              {
                key: 'workspaces',
                label: (
                  <span>
                    <ProjectOutlined style={{ marginRight: 6 }} />
                    Không gian làm việc của tôi ({workspaces.length})
                  </span>
                ),
                children: (
                  <div style={{ padding: '12px 0' }}>
                    {workspaceError && (
                      <Alert
                        type="error"
                        message="Lỗi tải danh sách không gian làm việc"
                        description={workspaceError}
                        showIcon
                        style={{ marginBottom: 16 }}
                      />
                    )}

                    {loadingWorkspaces ? (
                      <Flex align="center" justify="center" style={{ padding: 40 }}>
                        <Spin description="Đang tải danh sách..." />
                      </Flex>
                    ) : workspaces.length === 0 ? (
                      <Empty
                        image={Empty.PRESENTED_IMAGE_SIMPLE}
                        description="Bạn chưa tham gia không gian làm việc nào."
                      />
                    ) : (
                      <List
                        itemLayout="horizontal"
                        dataSource={workspaces}
                        renderItem={(ws) => (
                          <List.Item
                            key={ws.id}
                            style={{
                              padding: '16px 20px',
                              borderRadius: 8,
                              marginBottom: 12,
                              border: '1px solid #e2e8f0',
                              backgroundColor: '#ffffff',
                            }}
                            actions={[
                              <Button
                                key="open"
                                type="link"
                                onClick={() => navigate(`/workspaces/${ws.id}/boards`)}
                              >
                                Vào bảng việc
                              </Button>,
                              ws.isOwner ? (
                                <Tooltip
                                  key="leave"
                                  title="Chủ sở hữu không thể rời workspace. Bạn cần chuyển quyền sở hữu tại Cài đặt trước."
                                >
                                  <span>
                                    <Button danger type="text" disabled icon={<LogoutOutlined />}>
                                      Rời
                                    </Button>
                                  </span>
                                </Tooltip>
                              ) : (
                                <Popconfirm
                                  key="leave"
                                  title="Rời không gian làm việc"
                                  description={`Bạn có chắc chắn muốn rời khỏi ${ws.name}?`}
                                  okText="Xác nhận"
                                  cancelText="Huỷ"
                                  okButtonProps={{ danger: true }}
                                  onConfirm={() => handleLeaveWorkspace(ws.id)}
                                >
                                  <Button danger type="text" icon={<LogoutOutlined />}>
                                    Rời
                                  </Button>
                                </Popconfirm>
                              ),
                            ]}
                          >
                            <List.Item.Meta
                              avatar={
                                <Avatar
                                  shape="square"
                                  size={44}
                                  style={{ backgroundColor: '#4f46e5', fontWeight: 600, fontSize: 18 }}
                                >
                                  {ws.name?.[0]?.toUpperCase()}
                                </Avatar>
                              }
                              title={
                                <Space size={8} align="center">
                                  <Typography.Text strong style={{ fontSize: 15, color: '#0f172a' }}>
                                    {ws.name}
                                  </Typography.Text>
                                  <Tag color={getRoleTagColor(ws.role)} style={{ margin: 0 }}>
                                    {getRoleLabel(ws.role)}
                                  </Tag>
                                  {ws.isOwner && (
                                    <Tag
                                      color="gold"
                                      icon={<CrownOutlined />}
                                      style={{ margin: 0, fontWeight: 600 }}
                                    >
                                      Chủ sở hữu
                                    </Tag>
                                  )}
                                </Space>
                              }
                              description={
                                ws.description ? (
                                  <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                                    {ws.description}
                                  </Typography.Text>
                                ) : (
                                  <Typography.Text type="secondary" style={{ fontSize: 13, fontStyle: 'italic' }}>
                                    Chưa có mô tả
                                  </Typography.Text>
                                )
                              }
                            />
                          </List.Item>
                        )}
                      />
                    )}
                  </div>
                ),
              },
              {
                key: 'notifications',
                label: (
                  <span>
                    <BellOutlined style={{ marginRight: 6 }} />
                    Thông báo
                  </span>
                ),
                children: (
                  <div style={{ maxWidth: 620, padding: '12px 0' }} data-testid="notification-settings">
                    <Flex vertical gap={12}>
                      <div>
                        <Typography.Title level={5} style={{ margin: 0, color: '#0f172a' }}>
                          Email tóm tắt công việc hằng ngày
                        </Typography.Title>
                        <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                          Gửi mỗi sáng: thẻ quá hạn, sắp đến hạn và vừa được giao cho bạn. Bạn có thể tắt
                          bất cứ lúc nào.
                        </Typography.Text>
                      </div>

                      <Flex align="center" gap={12}>
                        <Switch
                          checked={digestEnabled}
                          loading={savingDigest || loadingDigest}
                          onChange={(checked) => void handleToggleDigest(checked)}
                          data-testid="digest-toggle"
                        />
                        <Typography.Text>{digestEnabled ? 'Đang bật' : 'Đang tắt'}</Typography.Text>
                      </Flex>

                      <Alert
                        type="info"
                        showIcon
                        message="Digest chỉ gửi khi bạn có việc cần làm"
                        description="Nếu không có thẻ nào đang mở, hệ thống sẽ không gửi email — bạn sẽ không nhận thư rỗng."
                      />
                    </Flex>
                  </div>
                ),
              },
            ]}
          />
        </Card>
      </Content>
    </Layout>
  )
}
