import React, { useState } from 'react'
import {
  Alert,
  Avatar,
  Button,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Typography,
} from 'antd'
import { SendOutlined, UserOutlined } from '@ant-design/icons'
import type {
  QuickEmailRequest,
  QuickEmailResult,
  WorkspaceMemberResponse,
} from '../types/member.types'

interface QuickEmailModalProps {
  open: boolean
  onClose: () => void
  members: WorkspaceMemberResponse[]
  onSend: (data: QuickEmailRequest) => Promise<QuickEmailResult | undefined>
}

export const QuickEmailModal: React.FC<QuickEmailModalProps> = ({
  open,
  onClose,
  members,
  onSend,
}) => {
  const [form] = Form.useForm<QuickEmailRequest>()
  const [submitting, setSubmitting] = useState(false)
  const [result, setResult] = useState<QuickEmailResult | null>(null)

  // Chỉ lấy thành viên là con người (human), loại trừ hoàn toàn AI Agent
  const humanMembers = members.filter((m) => m.memberType !== 'ai_agent')

  const handleFinish = async (values: QuickEmailRequest) => {
    try {
      setSubmitting(true)
      const res = await onSend({
        subject: values.subject.trim(),
        body: values.body.trim(),
        recipientUserIds: values.recipientUserIds,
      })

      if (res) {
        setResult(res)
        if (res.sent > 0 && res.failed === 0) {
          form.resetFields()
        }
      }
    } finally {
      setSubmitting(false)
    }
  }

  const handleClose = () => {
    setResult(null)
    form.resetFields()
    onClose()
  }

  return (
    <Modal
      title="Gửi email nhanh cho thành viên"
      open={open}
      onCancel={handleClose}
      footer={null}
      destroyOnClose
      width={600}
    >
      <div style={{ marginTop: 16 }}>
        {result && (
          <Alert
            type={result.failed > 0 ? 'warning' : 'success'}
            showIcon
            style={{ marginBottom: 16 }}
            message={`Đã gửi thành công ${result.sent}/${result.requested} email`}
            description={
              result.errors.length > 0 ? (
                <div>
                  <Typography.Text type="secondary">
                    Lỗi phát sinh:
                  </Typography.Text>
                  <ul style={{ paddingLeft: 20, margin: '4px 0 0 0' }}>
                    {result.errors.map((err, i) => (
                      <li key={i} style={{ fontSize: 12 }}>
                        {err}
                      </li>
                    ))}
                  </ul>
                </div>
              ) : undefined
            }
          />
        )}

        <Form form={form} layout="vertical" onFinish={handleFinish}>
          <Form.Item
            label="Người nhận (chỉ thành viên là người dùng)"
            name="recipientUserIds"
            rules={[
              {
                required: true,
                message: 'Vui lòng chọn ít nhất một người nhận',
              },
            ]}
          >
            <Select
              mode="multiple"
              placeholder="Chọn thành viên nhận email..."
              optionFilterProp="label"
              style={{ width: '100%' }}
              options={humanMembers.map((m) => ({
                value: m.userId,
                label: `${m.displayName} (${m.email || 'không có email'})`,
                item: m,
              }))}
              optionRender={(opt) => {
                const member = (opt.data as { item: WorkspaceMemberResponse }).item
                return (
                  <Space size={8}>
                    <Avatar
                      size="small"
                      src={member.avatarUrl}
                      icon={<UserOutlined />}
                      style={{ backgroundColor: '#6366f1' }}
                    />
                    <span>{member.displayName}</span>
                    {member.email && (
                      <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                        ({member.email})
                      </Typography.Text>
                    )}
                  </Space>
                )
              }}
            />
          </Form.Item>

          <Form.Item
            label="Tiêu đề email"
            name="subject"
            rules={[
              { required: true, message: 'Vui lòng nhập tiêu đề email' },
              {
                max: 200,
                message: 'Tiêu đề không được vượt quá 200 ký tự',
              },
            ]}
          >
            <Input
              placeholder="Nhập tiêu đề thư..."
              maxLength={200}
              showCount
            />
          </Form.Item>

          <Form.Item
            label="Nội dung email"
            name="body"
            rules={[
              { required: true, message: 'Vui lòng nhập nội dung email' },
              {
                max: 8000,
                message: 'Nội dung không được vượt quá 8000 ký tự',
              },
            ]}
          >
            <Input.TextArea
              rows={6}
              placeholder="Nhập nội dung chi tiết cần thông báo tới các thành viên..."
              maxLength={8000}
              showCount
            />
          </Form.Item>

          <div
            style={{
              display: 'flex',
              justifyContent: 'flex-end',
              gap: 8,
              marginTop: 24,
            }}
          >
            <Button onClick={handleClose}>Đóng</Button>
            <Button
              type="primary"
              htmlType="submit"
              icon={<SendOutlined />}
              loading={submitting}
            >
              Gửi email
            </Button>
          </div>
        </Form>
      </div>
    </Modal>
  )
}
