import React from 'react'
import { Button, Empty, Popconfirm, Table, Tag } from 'antd'
import type { ColumnsType } from 'antd/es/table'
import { CloseOutlined } from '@ant-design/icons'
import dayjs from 'dayjs'
import type { InvitationResponse, InvitationStatus } from '../types/member.types'
import { getRoleLabel, getRoleTagColor } from '../utils/memberRoleLabels'

interface PendingInvitationsTableProps {
  invitations: InvitationResponse[]
  loading?: boolean
  onCancelInvitation: (invitationId: string) => Promise<void> | void
}

const getStatusTag = (status: InvitationStatus) => {
  switch (status) {
    case 'Pending':
      return <Tag color="processing">Đang chờ</Tag>
    case 'Accepted':
      return <Tag color="success">Đã chấp nhận</Tag>
    case 'Cancelled':
      return <Tag color="default">Đã huỷ</Tag>
    case 'Expired':
      return <Tag color="error">Hết hạn</Tag>
    default:
      return <Tag>{status}</Tag>
  }
}

export const PendingInvitationsTable: React.FC<PendingInvitationsTableProps> = ({
  invitations,
  loading = false,
  onCancelInvitation,
}) => {
  const columns: ColumnsType<InvitationResponse> = [
    {
      title: 'Email nhận lời mời',
      dataIndex: 'invitedEmail',
      key: 'invitedEmail',
    },
    {
      title: 'Vai trò',
      dataIndex: 'invitedRole',
      key: 'invitedRole',
      width: 140,
      render: (role) => (
        <Tag color={getRoleTagColor(role)}>{getRoleLabel(role)}</Tag>
      ),
    },
    {
      title: 'Người mời',
      dataIndex: 'invitedByName',
      key: 'invitedByName',
      width: 160,
    },
    {
      title: 'Hết hạn vào',
      dataIndex: 'expiresAt',
      key: 'expiresAt',
      width: 160,
      render: (date: string) => (date ? dayjs(date).format('HH:mm DD/MM/YYYY') : '—'),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 140,
      render: (status: InvitationStatus) => getStatusTag(status),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 100,
      align: 'center',
      render: (_, record) => {
        if (record.status !== 'Pending') {
          return null
        }

        return (
          <Popconfirm
            title="Huỷ lời mời"
            description={`Bạn có chắc muốn huỷ lời mời gửi tới ${record.invitedEmail}?`}
            okText="Xác nhận"
            cancelText="Đóng"
            okButtonProps={{ danger: true }}
            onConfirm={() => onCancelInvitation(record.id)}
          >
            <Button
              danger
              type="text"
              size="small"
              icon={<CloseOutlined />}
              aria-label={`Huỷ lời mời ${record.invitedEmail}`}
            >
              Huỷ
            </Button>
          </Popconfirm>
        )
      },
    },
  ]

  if (invitations.length === 0 && !loading) {
    return (
      <Empty
        image={Empty.PRESENTED_IMAGE_SIMPLE}
        description="Chưa có lời mời nào đang chờ xử lý."
        style={{ padding: '24px 0' }}
      />
    )
  }

  return (
    <Table<InvitationResponse>
      dataSource={invitations}
      columns={columns}
      rowKey="id"
      loading={loading}
      pagination={false}
      style={{
        borderRadius: 8,
        overflow: 'hidden',
        border: '1px solid #e2e8f0',
      }}
    />
  )
}
