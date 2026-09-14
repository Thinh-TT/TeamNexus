import React, { useState } from 'react'
import {
  Alert,
  Button,
  Form,
  Input,
  Modal,
  Select,
  Typography,
} from 'antd'
import { MailOutlined, ReloadOutlined } from '@ant-design/icons'
import type { InvitationResponse, WorkspaceRole } from '../types/member.types'

interface InviteMemberModalProps {
  open: boolean
  onClose: () => void
  onInvite: (data: {
    email: string
    role: WorkspaceRole
  }) => Promise<InvitationResponse | undefined>
  onResend?: (
    oldInvitationId: string,
    email: string,
    role: WorkspaceRole
  ) => Promise<void>
}

// RFC-lite pattern: 1 @, domain has dot, no spaces
export const EMAIL_RFC_LITE_REGEX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

export const InviteMemberModal: React.FC<InviteMemberModalProps> = ({
  open,
  onClose,
  onInvite,
  onResend,
}) => {
  const [form] = Form.useForm<{ email: string; role: WorkspaceRole }>()
  const [submitting, setSubmitting] = useState(false)
  const [resending, setResending] = useState(false)
  const [failedInvitation, setFailedInvitation] = useState<InvitationResponse | null>(null)

  const handleFinish = async (values: { email: string; role: WorkspaceRole }) => {
    try {
      setSubmitting(true)
      const res = await onInvite({
        email: values.email.trim(),
        role: values.role,
      })

      if (res) {
        if (!res.emailSent) {
          // Lời mời được tạo nhưng gửi email thất bại
          setFailedInvitation(res)
        } else {
          setFailedInvitation(null)
          form.resetFields()
          onClose()
        }
      }
    } finally {
      setSubmitting(false)
    }
  }

  const handleResend = async () => {
    if (!failedInvitation || !onResend) return
    try {
      setResending(true)
      await onResend(
        failedInvitation.id,
        failedInvitation.invitedEmail,
        failedInvitation.invitedRole
      )
      setFailedInvitation(null)
      form.resetFields()
      onClose()
    } finally {
      setResending(false)
    }
  }

  const handleCancel = () => {
    setFailedInvitation(null)
    form.resetFields()
    onClose()
  }

  return (
    <Modal
      title="Mời thành viên vào không gian làm việc"
      open={open}
      onCancel={handleCancel}
      footer={null}
      destroyOnClose
    >
      <div style={{ marginTop: 16 }}>
        {failedInvitation && (
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            message="Đã tạo lời mời nhưng gửi email thất bại"
            description={
              <div>
                <Typography.Paragraph style={{ fontSize: 13, marginBottom: 8 }}>
                  Hệ thống đã ghi nhận lời mời tới <strong>{failedInvitation.invitedEmail}</strong> nhưng
                  không thể gửi thư. Bạn có thể bấm gửi lại để thử gửi lại qua dịch vụ email.
                </Typography.Paragraph>
                {onResend && (
                  <Button
                    size="small"
                    type="primary"
                    icon={<ReloadOutlined />}
                    loading={resending}
                    onClick={handleResend}
                  >
                    Gửi lại
                  </Button>
                )}
              </div>
            }
          />
        )}

        <Form
          form={form}
          layout="vertical"
          initialValues={{ role: 'Member' }}
          onFinish={handleFinish}
        >
          <Form.Item
            label="Địa chỉ Email"
            name="email"
            rules={[
              { required: true, message: 'Vui lòng nhập địa chỉ email' },
              {
                validator: (_, value) => {
                  if (!value) return Promise.resolve()
                  const trimmed = value.trim()
                  if (!EMAIL_RFC_LITE_REGEX.test(trimmed)) {
                    return Promise.reject(
                      new Error('Định dạng email không hợp lệ')
                    )
                  }
                  return Promise.resolve()
                },
              },
            ]}
          >
            <Input
              prefix={<MailOutlined style={{ color: '#94a3b8' }} />}
              placeholder="nhanvien@example.com"
            />
          </Form.Item>

          <Form.Item
            label="Vai trò được chỉ định"
            name="role"
            rules={[{ required: true, message: 'Vui lòng chọn vai trò' }]}
          >
            <Select
              options={[
                { value: 'Member', label: 'Thành viên (Member)' },
                { value: 'Manager', label: 'Quản lý (Manager)' },
                { value: 'Admin', label: 'Quản trị viên (Admin)' },
              ]}
            />
          </Form.Item>

          <div style={{ display: 'flex', justifyContent: 'flex-end', gap: 8, marginTop: 24 }}>
            <Button onClick={handleCancel}>Đóng</Button>
            <Button type="primary" htmlType="submit" loading={submitting}>
              Gửi lời mời
            </Button>
          </div>
        </Form>
      </div>
    </Modal>
  )
}
