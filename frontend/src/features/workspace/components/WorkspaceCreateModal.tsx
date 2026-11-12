import React, { useEffect, useState } from 'react'
import { Form, Input, Modal } from 'antd'
import type { CreateWorkspaceRequest } from '../types/workspace.types'

interface WorkspaceCreateModalProps {
  open: boolean
  onClose: () => void
  onSubmit: (data: CreateWorkspaceRequest) => Promise<unknown>
}

/**
 * Form tạo không gian làm việc mới (POST /api/workspaces).
 *
 * Modal này chỉ thu thập dữ liệu: nơi gọi (`DashboardPage`) sở hữu lời gọi API và hiển thị lỗi bằng
 * `message.error`. Vì AntD Form không await `onFinish`, lỗi từ `onSubmit` được nuốt tại đây để không
 * tạo unhandled rejection và modal vẫn mở cho người dùng sửa (cùng pattern `ColumnModal`).
 */
export const WorkspaceCreateModal: React.FC<WorkspaceCreateModalProps> = ({
  open,
  onClose,
  onSubmit,
}) => {
  const [form] = Form.useForm()
  const [submitting, setSubmitting] = useState(false)

  useEffect(() => {
    if (open) {
      form.resetFields()
      form.setFieldsValue({ name: '', description: '' })
    }
  }, [open, form])

  const handleFinish = async (values: { name: string; description?: string }) => {
    setSubmitting(true)
    try {
      await onSubmit({
        name: values.name.trim(),
        description: values.description && values.description.trim() ? values.description.trim() : null,
      })
      onClose()
    } catch {
      // Nơi gọi đã hiện thông báo lỗi; modal ở lại để người dùng chỉnh sửa.
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <Modal
      open={open}
      title="Tạo không gian làm việc mới"
      okText="Tạo không gian"
      cancelText="Huỷ"
      confirmLoading={submitting}
      onCancel={onClose}
      onOk={() => form.submit()}
      destroyOnHidden
    >
      <Form
        form={form}
        layout="vertical"
        onFinish={handleFinish}
        style={{ marginTop: 16 }}
        data-testid="workspace-create-modal"
      >
        <Form.Item
          name="name"
          label="Tên không gian làm việc"
          rules={[
            {
              required: true,
              whitespace: true,
              message: 'Vui lòng nhập tên không gian làm việc',
            },
            { max: 120, message: 'Tên không được vượt quá 120 ký tự' },
          ]}
        >
          <Input
            placeholder="Ví dụ: Dự án Beta, Nhóm Kinh doanh..."
            maxLength={120}
            autoFocus
            data-testid="workspace-name-input"
          />
        </Form.Item>

        <Form.Item
          name="description"
          label="Mô tả (tuỳ chọn)"
          rules={[{ max: 2000, message: 'Mô tả không được vượt quá 2000 ký tự' }]}
        >
          <Input.TextArea
            rows={3}
            placeholder="Mô tả mục tiêu của không gian làm việc này..."
            maxLength={2000}
            data-testid="workspace-description-input"
          />
        </Form.Item>
      </Form>
    </Modal>
  )
}
