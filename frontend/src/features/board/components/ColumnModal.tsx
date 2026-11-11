import React, { useEffect } from 'react'
import { Alert, Checkbox, Form, Input, Modal } from 'antd'
import type { ColumnResponse, CreateColumnRequest, UpdateColumnRequest } from '../types/board.types'

interface ColumnModalProps {
  open: boolean
  column?: ColumnResponse | null
  onClose: () => void
  onSubmit: (data: CreateColumnRequest | UpdateColumnRequest) => Promise<unknown>
}

export const ColumnModal: React.FC<ColumnModalProps> = ({
  open,
  column,
  onClose,
  onSubmit,
}) => {
  const [form] = Form.useForm()
  const isEditing = !!column
  const isClarificationColumn = Boolean(column?.isClarification)

  useEffect(() => {
    if (open) {
      if (column) {
        form.setFieldsValue({
          name: column.name,
          isDone: column.isDone,
          isInReview: Boolean(column.isInReview),
        })
      } else {
        form.resetFields()
        form.setFieldsValue({
          name: '',
          isDone: false,
          isInReview: false,
        })
      }
    }
  }, [open, column, form])

  const handleFinish = async (values: {
    name: string
    isDone: boolean
    isInReview?: boolean
  }) => {
    await onSubmit({
      name: values.name.trim(),
      isDone: isClarificationColumn ? false : Boolean(values.isDone),
      isInReview: isClarificationColumn ? false : Boolean(values.isInReview),
    })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEditing ? 'Chỉnh sửa Cột' : 'Tạo Cột Mới'}
      okText={isEditing ? 'Lưu thay đổi' : 'Tạo cột'}
      cancelText="Huỷ"
      onCancel={onClose}
      onOk={() => form.submit()}
      destroyOnHidden
    >
      <Form form={form} layout="vertical" onFinish={handleFinish} style={{ marginTop: 16 }}>
        <Form.Item
          name="name"
          label="Tên cột"
          rules={[{ required: true, message: 'Vui lòng nhập tên cột' }]}
        >
          <Input placeholder="Ví dụ: Cần làm, Đang làm, Đang duyệt PR, Đã xong..." autoFocus />
        </Form.Item>

        {isClarificationColumn && (
          <Alert
            type="info"
            showIcon
            title="Cột này đang giữ vai trò 'Chờ làm rõ' do AI Agent quản lý nên không thể gán thêm vai trò Hoàn thành hoặc Đang duyệt PR."
            style={{ marginBottom: 12, borderRadius: 8 }}
          />
        )}

        <Form.Item name="isDone" valuePropName="checked" style={{ marginBottom: 10 }}>
          <Checkbox
            disabled={isClarificationColumn}
            data-testid="column-is-done-checkbox"
            onChange={(e) => {
              if (e.target.checked) {
                form.setFieldValue('isInReview', false)
              }
            }}
          >
            Đánh dấu là cột hoàn thành (Thẻ đưa vào cột này sẽ tự động ghi nhận hoàn thành)
          </Checkbox>
        </Form.Item>

        <Form.Item name="isInReview" valuePropName="checked" style={{ marginBottom: 0 }}>
          <Checkbox
            disabled={isClarificationColumn}
            data-testid="column-is-in-review-checkbox"
            onChange={(e) => {
              if (e.target.checked) {
                form.setFieldValue('isDone', false)
              }
            }}
          >
            Đánh dấu là cột Đang duyệt PR (In Review — tự động chuyển thẻ khi mở GitHub PR)
          </Checkbox>
        </Form.Item>
      </Form>
    </Modal>
  )
}
