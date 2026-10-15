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
  SolutionOutlined,
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
import { MemberSkillsForm } from './MemberSkillsForm'

interface MembersTableProps {
  members: WorkspaceMemberResponse[]
  loading?: boolean
  canManage: boolean
  currentUserId?: string
  workspaceId?: string
  onRefresh?: () => void
  onRoleChange: (userId: string, newRole: WorkspaceRole) => Promise<void> | void
  onRemoveMember: (userId: string) => Promise<void> | void
}

export const MembersTable: React.FC<MembersTableProps> = ({
  members,
  loading = false,
  canManage,
  currentUserId,
  workspaceId,
  onRefresh,
  onRoleChange,
  onRemoveMember,
}) => {
  const [selectedMemberForSkills, setSelectedMemberForSkills] =
    React.useState<WorkspaceMemberResponse | null>(null)
  const [skillsModalOpen, setSkillsModalOpen] = React.useState(false)
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
      title: 'Hồ sơ năng lực',
      key: 'skills',
      render: (_, record) => {
        if (record.memberType === 'ai_agent') {
          return <Tag color="purple">Hệ thống AI</Tag>
        }
        return (
          <Space direction="vertical" size={2}>
            {record.primaryRole && (
              <Typography.Text strong style={{ fontSize: 12 }}>
                {record.primaryRole}
              </Typography.Text>
            )}
            <Space size={2} wrap>
              {record.skills && record.skills.length > 0 ? (
                record.skills.slice(0, 3).map((s) => (
                  <Tag key={s} style={{ margin: 0, fontSize: 11 }}>
                    {s}
                  </Tag>
                ))
              ) : (
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                  Chưa thiết lập
                </Typography.Text>
              )}
              {record.skills && record.skills.length > 3 && (
                <Tag style={{ margin: 0, fontSize: 11 }}>
                  +{record.skills.length - 3}
                </Tag>
              )}
            </Space>
          </Space>
        )
      },
    },
    {
      title: 'Ngày tham gia',
      dataIndex: 'joinedAt',
      key: 'joinedAt',
      width: 130,
      render: (date: string) => (date ? dayjs(date).format('DD/MM/YYYY') : '—'),
    },
  ]

  if (canManage) {
    columns.push({
      title: 'Hành động',
      key: 'actions',
      width: 130,
      align: 'center',
      render: (_, record) => {
        const perm = isMemberActionDisabled(record, currentUserId)

        return (
          <Space size={4}>
            {canManage && record.memberType !== 'ai_agent' && (
              <Tooltip title="Chỉnh sửa hồ sơ năng lực & kỹ năng">
                <Button
                  type="text"
                  size="small"
                  icon={<SolutionOutlined style={{ color: '#6366f1' }} />}
                  onClick={() => {
                    setSelectedMemberForSkills(record)
                    setSkillsModalOpen(true)
                  }}
                  data-testid={`edit-skills-btn-${record.userId}`}
                />
              </Tooltip>
            )}

            {perm.disabled ? (
              <Tooltip title={perm.reason}>
                <Button
                  danger
                  type="text"
                  size="small"
                  icon={<DeleteOutlined />}
                  disabled
                />
              </Tooltip>
            ) : (
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
            )}
          </Space>
        )
      },
    })
  }

  return (
    <>
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

      <MemberSkillsForm
        open={skillsModalOpen}
        onClose={() => setSkillsModalOpen(false)}
        workspaceId={workspaceId || ''}
        member={selectedMemberForSkills}
        onSuccess={onRefresh}
      />
    </>
  )
}
