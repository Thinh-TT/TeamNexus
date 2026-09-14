import React from 'react'
import {
  Avatar,
  Button,
  Flex,
  Popconfirm,
  Select,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  CrownOutlined,
  DeleteOutlined,
  RobotOutlined,
  UserOutlined,
} from '@ant-design/icons'
import dayjs from 'dayjs'
import type { WorkspaceMemberResponse, WorkspaceRole } from '../types/member.types'
import {
  getMemberTypeLabel,
  getRoleLabel,
  getRoleTagColor,
  isMemberActionDisabled,
} from '../utils/memberRoleLabels'

interface MembersTableProps {
  members: WorkspaceMemberResponse[]
  loading?: boolean
  canManage: boolean
  currentUserId?: string
  onRoleChange: (userId: string, newRole: WorkspaceRole) => Promise<void> | void
  onRemoveMember: (userId: string) => Promise<void> | void
}

export const MembersTable: React.FC<MembersTableProps> = ({
  members,
  loading = false,
  canManage,
  currentUserId,
  onRoleChange,
  onRemoveMember,
}) => {
  const columns: ColumnsType<WorkspaceMemberResponse> = [
    {
      title: 'Thành viên',
      key: 'member',
      render: (_, record) => {
        const isAgent = record.memberType === 'ai_agent'
        return (
          <Flex align="center" gap={12}>
            <Avatar
              src={record.avatarUrl}
              icon={isAgent ? <RobotOutlined /> : <UserOutlined />}
              style={{ backgroundColor: isAgent ? '#0ea5e9' : '#6366f1' }}
            >
              {!record.avatarUrl && !isAgent && record.displayName?.[0]?.toUpperCase()}
            </Avatar>
            <Flex vertical>
              <Space size={6} align="center">
                <Typography.Text strong style={{ color: '#0f172a' }}>
                  {record.displayName}
                </Typography.Text>
                {record.isOwner && (
                  <Tooltip title="Chủ sở hữu workspace">
                    <Tag
                      color="gold"
                      icon={<CrownOutlined />}
                      style={{ margin: 0, fontWeight: 600 }}
                      data-testid="owner-badge"
                    >
                      Chủ sở hữu
                    </Tag>
                  </Tooltip>
                )}
                {isAgent && (
                  <Tag color="cyan" style={{ margin: 0 }}>
                    AI Agent
                  </Tag>
                )}
              </Space>
              {record.email && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {record.email}
                </Typography.Text>
              )}
            </Flex>
          </Flex>
        )
      },
    },
    {
      title: 'Vai trò',
      dataIndex: 'role',
      key: 'role',
      width: 180,
      render: (role: WorkspaceRole, record) => {
        const perm = isMemberActionDisabled(record, currentUserId)

        if (canManage && !perm.disabled) {
          return (
            <Select<WorkspaceRole>
              value={role}
              style={{ width: 140 }}
              onChange={(newRole) => onRoleChange(record.userId, newRole)}
              aria-label={`Đổi vai trò của ${record.displayName}`}
              options={[
                { value: 'Member', label: 'Thành viên' },
                { value: 'Manager', label: 'Quản lý' },
                { value: 'Admin', label: 'Quản trị viên' },
              ]}
            />
          )
        }

        return (
          <Tag color={getRoleTagColor(role)} style={{ fontWeight: 500 }}>
            {getRoleLabel(role)}
          </Tag>
        )
      },
    },
    {
      title: 'Loại',
      dataIndex: 'memberType',
      key: 'memberType',
      width: 140,
      render: (type: string) => {
        return (
          <Tag color={type === 'ai_agent' ? 'purple' : 'blue'}>
            {getMemberTypeLabel(type)}
          </Tag>
        )
      },
    },
    {
      title: 'Ngày tham gia',
      dataIndex: 'joinedAt',
      key: 'joinedAt',
      width: 150,
      render: (date: string) => (date ? dayjs(date).format('DD/MM/YYYY') : '—'),
    },
  ]

  if (canManage) {
    columns.push({
      title: 'Hành động',
      key: 'actions',
      width: 110,
      align: 'center',
      render: (_, record) => {
        const perm = isMemberActionDisabled(record, currentUserId)

        if (perm.disabled) {
          return (
            <Tooltip title={perm.reason}>
              <Button
                danger
                type="text"
                size="small"
                icon={<DeleteOutlined />}
                disabled
              />
            </Tooltip>
          )
        }

        return (
          <Popconfirm
            title="Xoá thành viên"
            description={`Bạn có chắc muốn xoá ${record.displayName} khỏi workspace này?`}
            okText="Xác nhận"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => onRemoveMember(record.userId)}
          >
            <Button
              danger
              type="text"
              size="small"
              icon={<DeleteOutlined />}
              aria-label={`Xoá ${record.displayName}`}
            >
              Xoá
            </Button>
          </Popconfirm>
        )
      },
    })
  }

  return (
    <Table<WorkspaceMemberResponse>
      dataSource={members}
      columns={columns}
      rowKey="userId"
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
