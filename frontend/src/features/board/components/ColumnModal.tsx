import React, { useEffect } from 'react'
import { Checkbox, Form, Input, Modal } from 'antd'
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

  useEffect(() => {
    if (open) {
      if (column) {
        form.setFieldsValue({
          name: column.name,
          isDone: column.isDone,
        })
      } else {
        form.resetFields()
        form.setFieldsValue({
          name: '',
          isDone: false,
        })
      }
    }
  }, [open, column, form])

  const handleFinish = async (values: { name: string; isDone: boolean }) => {
    await onSubmit({
      name: values.name.trim(),
      isDone: values.isDone,
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
      destroyOnClose
    >
      <Form form={form} layout="vertical" onFinish={handleFinish} style={{ marginTop: 16 }}>
        <Form.Item
          name="name"
          label="Tên cột"
          rules={[{ required: true, message: 'Vui lòng nhập tên cột' }]}
        >
          <Input placeholder="Ví dụ: Cần làm, Đang làm, Đã xong..." autoFocus />
        </Form.Item>

        <Form.Item name="isDone" valuePropName="checked">
          <Checkbox>
            Đánh dấu là cột hoàn thành (Thẻ đưa vào cột này sẽ tự động ghi nhận hoàn thành)
          </Checkbox>
        </Form.Item>
      </Form>
    </Modal>
  )
}
