import React, { useEffect } from 'react'
import { Form, Input, Modal } from 'antd'
import type { BoardResponse, CreateBoardRequest, UpdateBoardRequest } from '../types/board.types'

interface BoardModalProps {
  open: boolean
  board?: BoardResponse | null
  onClose: () => void
  onSubmit: (data: CreateBoardRequest | UpdateBoardRequest) => Promise<unknown>
}

export const BoardModal: React.FC<BoardModalProps> = ({
  open,
  board,
  onClose,
  onSubmit,
}) => {
  const [form] = Form.useForm()
  const isEditing = !!board

  useEffect(() => {
    if (open) {
      if (board) {
        form.setFieldsValue({
          name: board.name,
          description: board.description || '',
        })
      } else {
        form.resetFields()
        form.setFieldsValue({
          name: '',
          description: '',
        })
      }
    }
  }, [open, board, form])

  const handleFinish = async (values: { name: string; description?: string }) => {
    await onSubmit({
      name: values.name.trim(),
      description: values.description ? values.description.trim() : null,
    })
    onClose()
  }

  return (
    <Modal
      open={open}
      title={isEditing ? 'Chỉnh sửa Bảng' : 'Tạo Bảng Mới'}
      okText={isEditing ? 'Lưu thay đổi' : 'Tạo bảng'}
      cancelText="Huỷ"
      onCancel={onClose}
      onOk={() => form.submit()}
      destroyOnClose
    >
      <Form form={form} layout="vertical" onFinish={handleFinish} style={{ marginTop: 16 }}>
        <Form.Item
          name="name"
          label="Tên bảng"
          rules={[{ required: true, message: 'Vui lòng nhập tên bảng' }]}
        >
          <Input placeholder="Ví dụ: Sprint 1, Phát triển Tính năng AI..." autoFocus />
        </Form.Item>

        <Form.Item name="description" label="Mô tả bảng (tuỳ chọn)">
          <Input.TextArea rows={3} placeholder="Mô tả mục tiêu của bảng này..." />
        </Form.Item>
      </Form>
    </Modal>
  )
}
