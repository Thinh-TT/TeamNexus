import React, { useEffect, useState } from 'react'
import {
  ExclamationCircleOutlined,
  InfoCircleOutlined,
  UserSwitchOutlined,
  WarningOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Divider,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Select,
  Space,
  Tabs,
  Typography,
} from 'antd'
import type { WorkspaceMemberResponse } from '../../board/types/board.types'
import type {
  UpdateWorkspaceRequest,
  WorkspaceDetail,
} from '../types/workspace.types'

interface WorkspaceSettingsModalProps {
  open: boolean
  onClose: () => void
  detail: WorkspaceDetail | null
  members: WorkspaceMemberResponse[]
  isManagerOrAdmin: boolean
  isAdminOrOwner: boolean
  onSave: (data: UpdateWorkspaceRequest) => Promise<boolean>
  onTransfer: (newOwnerId: string) => Promise<boolean>
  onDelete: () => Promise<boolean>
}

export const WorkspaceSettingsModal: React.FC<WorkspaceSettingsModalProps> = ({
  open,
  onClose,
  detail,
  members,
  isManagerOrAdmin,
  isAdminOrOwner,
  onSave,
  onTransfer,
  onDelete,
}) => {
  const [form] = Form.useForm()
  const [activeTab, setActiveTab] = useState('info')
  const [saving, setSaving] = useState(false)

  // Transfer ownership state
  const [selectedNewOwnerId, setSelectedNewOwnerId] = useState<string | null>(null)
  const [transferring, setTransferring] = useState(false)

  // Delete workspace confirmation state
  const [confirmNameInput, setConfirmNameInput] = useState('')
  const [deleting, setDeleting] = useState(false)

  useEffect(() => {
    if (detail && open) {
      form.setFieldsValue({
        name: detail.name,
        description: detail.description || '',
      })
      Promise.resolve().then(() => {
        setSelectedNewOwnerId(null)
        setConfirmNameInput('')
        setActiveTab('info')
      })
    }
  }, [detail, open, form])

  if (!detail) return null

  // Human members only (AI Agents excluded!)
  const eligibleMembers = members.filter(
    (m) => m.memberType === 'human'
  )

  const handleFinishSave = async (values: { name: string; description?: string }) => {
    if (!isManagerOrAdmin) return
    setSaving(true)
    try {
      const success = await onSave({
        name: values.name.trim(),
        description: values.description ? values.description.trim() : null,
      })
      if (success) {
        onClose()
      }
    } finally {
      setSaving(false)
    }
  }

  const handleConfirmTransfer = async () => {
    if (!selectedNewOwnerId) return
    setTransferring(true)
    try {
      const success = await onTransfer(selectedNewOwnerId)
      if (success) {
        onClose()
      }
    } finally {
      setTransferring(false)
    }
  }

  const handleConfirmDelete = async () => {
    if (confirmNameInput !== detail.name) return
    setDeleting(true)
    try {
      const success = await onDelete()
      if (success) {
        onClose()
      }
    } finally {
      setDeleting(false)
    }
  }

  const items = [
    {
      key: 'info',
      label: (
        <Space>
          <InfoCircleOutlined />
          <span>Thông tin</span>
        </Space>
      ),
      children: (
        <Form
          form={form}
          layout="vertical"
          onFinish={handleFinishSave}
          disabled={!isManagerOrAdmin}
        >
          <Form.Item
            name="name"
            label="Tên không gian làm việc"
            rules={[
              { required: true, message: 'Vui lòng nhập tên workspace' },
              { max: 120, message: 'Tên không được vượt quá 120 ký tự' },
            ]}
          >
            <Input placeholder="Nhập tên workspace..." maxLength={120} />
          </Form.Item>

          <Form.Item
            name="description"
            label="Mô tả không gian làm việc"
            rules={[{ max: 2000, message: 'Mô tả không được vượt quá 2000 ký tự' }]}
          >
            <Input.TextArea
              rows={4}
              placeholder="Nhập mô tả cho workspace..."
              maxLength={2000}
            />
          </Form.Item>

          {isManagerOrAdmin && (
            <Flex justify="flex-end" style={{ marginTop: 16 }}>
              <Button type="primary" htmlType="submit" loading={saving}>
                Lưu Thay Đổi
              </Button>
            </Flex>
          )}
        </Form>
      ),
    },
    ...(isAdminOrOwner
      ? [
          {
            key: 'danger',
            label: (
              <Space style={{ color: '#ef4444' }}>
                <WarningOutlined />
                <span>Vùng nguy hiểm</span>
              </Space>
            ),
            children: (
              <Flex vertical gap={24}>
                {/* Transfer Ownership */}
                <Card
                  size="small"
                  style={{
                    borderColor: '#fde68a',
                    backgroundColor: '#fffbeb',
                    borderRadius: 8,
                  }}
                >
                  <Typography.Title level={5} style={{ marginTop: 0, color: '#b45309' }}>
                    <UserSwitchOutlined style={{ marginRight: 6 }} />
                    Chuyển quyền sở hữu không gian làm việc
                  </Typography.Title>
                  <Typography.Paragraph type="secondary" style={{ fontSize: 13 }}>
                    Chỉ có thể chuyển cho thành viên là người thật (loại trừ AI Agent). Người nhận sẽ được nâng quyền Quản trị viên (Admin).
                  </Typography.Paragraph>

                  <Flex gap={12} align="center" wrap="wrap">
                    <Select
                      style={{ width: 280 }}
                      placeholder="Chọn thành viên nhận quyền sở hữu..."
                      value={selectedNewOwnerId}
                      onChange={setSelectedNewOwnerId}
                      data-testid="transfer-owner-select"
                      options={eligibleMembers.map((m) => ({
                        value: m.userId,
                        label: `${m.displayName}${(m as { email?: string }).email ? ` (${(m as { email?: string }).email})` : ` (${m.role})`}`,
                        disabled: m.userId === detail.ownerId,
                      }))}
                    />

                    <Popconfirm
                      title="Xác nhận chuyển quyền sở hữu?"
                      description="Hành động này sẽ biến thành viên đã chọn thành Chủ sở hữu mới của workspace."
                      onConfirm={handleConfirmTransfer}
                      okText="Xác nhận chuyển"
                      cancelText="Huỷ"
                      disabled={!selectedNewOwnerId}
                    >
                      <Button
                        type="primary"
                        danger
                        disabled={!selectedNewOwnerId}
                        loading={transferring}
                        data-testid="transfer-owner-confirm-btn"
                      >
                        Chuyển Quyền Sở Hữu
                      </Button>
                    </Popconfirm>
                  </Flex>
                </Card>

                <Divider style={{ margin: '8px 0' }} />

                {/* Delete Workspace */}
                <Card
                  size="small"
                  style={{
                    borderColor: '#fca5a5',
                    backgroundColor: '#fef2f2',
                    borderRadius: 8,
                  }}
                >
                  <Typography.Title level={5} style={{ marginTop: 0, color: '#dc2626' }}>
                    <ExclamationCircleOutlined style={{ marginRight: 6 }} />
                    Xoá không gian làm việc này
                  </Typography.Title>
                  <Typography.Paragraph type="secondary" style={{ fontSize: 13 }}>
                    Hành động này sẽ ẩn vĩnh viễn không gian làm việc và toàn bộ bảng Kanban bên trong. Vui lòng gõ chính xác <strong>{detail.name}</strong> để xác nhận xoá.
                  </Typography.Paragraph>

                  <Flex vertical gap={12}>
                    <Input
                      placeholder={`Gõ "${detail.name}" để xác nhận...`}
                      value={confirmNameInput}
                      onChange={(e) => setConfirmNameInput(e.target.value)}
                      style={{ maxWidth: 360 }}
                      data-testid="delete-workspace-input"
                    />

                    <div>
                      <Popconfirm
                        title="Bạn có chắc chắn muốn xoá workspace này?"
                        description="Mọi bảng Kanban và hoạt động liên quan sẽ không còn khả dụng."
                        onConfirm={handleConfirmDelete}
                        okText="Xác nhận xoá vĩnh viễn"
                        cancelText="Huỷ"
                        disabled={confirmNameInput !== detail.name}
                      >
                        <Button
                          type="primary"
                          danger
                          disabled={confirmNameInput !== detail.name}
                          loading={deleting}
                          data-testid="delete-workspace-btn"
                        >
                          Xoá Không Gian Làm Việc
                        </Button>
                      </Popconfirm>
                    </div>
                  </Flex>
                </Card>
              </Flex>
            ),
          },
        ]
      : []),
  ]

  return (
    <Modal
      open={open}
      onCancel={onClose}
      title="Cài đặt không gian làm việc"
      footer={null}
      width={680}
      destroyOnClose
    >
      <Tabs activeKey={activeTab} onChange={setActiveTab} items={items} />
    </Modal>
  )
}
