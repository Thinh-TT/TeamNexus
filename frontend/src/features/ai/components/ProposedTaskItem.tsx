import React, { useState } from 'react'
import {
  DeleteOutlined,
  ExclamationCircleOutlined,
  PlusOutlined,
  TagOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Button,
  Card,
  Flex,
  Input,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { LabelResponse } from '../../board/types/board.types'
import type {
  EditableTaskProposal,
  SmartSetupLabelSuggestion,
  TaskPriority,
  WorkspaceMember,
} from '../types/smartSetup.types'

const { Text } = Typography

interface ProposedTaskItemProps {
  task: EditableTaskProposal
  index: number
  members: WorkspaceMember[]
  workspaceLabels: LabelResponse[]
  onUpdate: (tempId: string, patch: Partial<EditableTaskProposal>) => void
  onDelete: (tempId: string) => void
  onAddLabel: (tempId: string, label: SmartSetupLabelSuggestion) => void
  onRemoveLabel: (tempId: string, labelIndex: number) => void
}

export const ProposedTaskItem: React.FC<ProposedTaskItemProps> = ({
  task,
  index,
  members,
  workspaceLabels,
  onUpdate,
  onDelete,
  onAddLabel,
  onRemoveLabel,
}) => {
  const [isAddingCustomLabel, setIsAddingCustomLabel] = useState(false)
  const [customLabelInput, setCustomLabelInput] = useState('')

  const handleTitleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    onUpdate(task.tempId, { title: e.target.value })
  }

  const handleDescriptionChange = (
    e: React.ChangeEvent<HTMLTextAreaElement>
  ) => {
    onUpdate(task.tempId, { description: e.target.value || null })
  }

  const handlePriorityChange = (val: TaskPriority | 'NONE') => {
    onUpdate(task.tempId, { priority: val === 'NONE' ? null : val })
  }

  const handleAssigneeChange = (val: string | 'UNASSIGNED') => {
    if (val === 'UNASSIGNED') {
      onUpdate(task.tempId, { assignee: null })
      return
    }

    const selectedMember = members.find((m) => m.userId === val)
    if (selectedMember) {
      onUpdate(task.tempId, {
        assignee: {
          userId: selectedMember.userId,
          displayName: selectedMember.displayName,
          matched: true,
        },
      })
    }
  }

  const handleAddCustomLabel = () => {
    const trimmed = customLabelInput.trim()
    if (!trimmed) {
      setIsAddingCustomLabel(false)
      return
    }

    const matchedWorkspaceLabel = workspaceLabels.find(
      (l) => l.name.toLowerCase() === trimmed.toLowerCase()
    )

    onAddLabel(task.tempId, {
      labelId: matchedWorkspaceLabel ? matchedWorkspaceLabel.id : null,
      name: trimmed,
      exists: !!matchedWorkspaceLabel,
    })

    setCustomLabelInput('')
    setIsAddingCustomLabel(false)
  }

  const handleSelectWorkspaceLabel = (labelName: string) => {
    const matched = workspaceLabels.find(
      (l) => l.name.toLowerCase() === labelName.toLowerCase()
    )
    if (matched) {
      onAddLabel(task.tempId, {
        labelId: matched.id,
        name: matched.name,
        exists: true,
      })
    }
  }

  const isAssigneeUnmatched =
    task.assignee && !task.assignee.matched && task.assignee.displayName

  const availableWorkspaceLabels = workspaceLabels.filter(
    (wl) =>
      !task.labels.some(
        (tl) => tl.name.toLowerCase() === wl.name.toLowerCase()
      )
  )

  return (
    <Card
      size="small"
      styles={{
        body: { padding: '14px 16px' },
      }}
      style={{
        marginBottom: 12,
        borderRadius: 10,
        border: '1px solid #e2e8f0',
        backgroundColor: '#ffffff',
        boxShadow: '0 1px 3px rgba(0, 0, 0, 0.04)',
      }}
    >
      <Flex vertical gap={10}>
        {/* Header: Task Index, Title & Delete Button */}
        <Flex align="center" justify="space-between" gap={8}>
          <Flex align="center" gap={8} style={{ flex: 1 }}>
            <Tag
              color="blue"
              style={{
                borderRadius: 6,
                fontWeight: 600,
                marginRight: 0,
                padding: '2px 8px',
              }}
            >
              #{index + 1}
            </Tag>
            <Input
              value={task.title}
              onChange={handleTitleChange}
              placeholder="Tiêu đề sub-task..."
              maxLength={200}
              style={{
                fontWeight: 600,
                fontSize: 14,
                borderRadius: 6,
              }}
            />
          </Flex>

          <Tooltip title="Xóa sub-task này">
            <Button
              type="text"
              danger
              icon={<DeleteOutlined />}
              onClick={() => onDelete(task.tempId)}
              style={{ borderRadius: 6 }}
            />
          </Tooltip>
        </Flex>

        {/* Priority & Assignee controls */}
        <Flex align="center" gap={12} wrap="wrap">
          {/* Priority Select */}
          <Flex align="center" gap={6}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Độ ưu tiên:
            </Text>
            <Select
              size="small"
              value={task.priority ?? 'NONE'}
              onChange={handlePriorityChange}
              style={{ width: 130 }}
              options={[
                { value: 'NONE', label: '⚪ Không ưu tiên' },
                { value: 'Urgent', label: '🔴 Khẩn cấp' },
                { value: 'High', label: '🟠 Cao' },
                { value: 'Medium', label: '🔵 Trung bình' },
                { value: 'Low', label: '⚪ Thấp' },
              ]}
            />
          </Flex>

          {/* Assignee Select */}
          <Flex align="center" gap={6}>
            <Text type="secondary" style={{ fontSize: 12 }}>
              Người phụ trách:
            </Text>
            <Select
              size="small"
              value={
                task.assignee?.matched && task.assignee?.userId
                  ? task.assignee.userId
                  : 'UNASSIGNED'
              }
              onChange={handleAssigneeChange}
              style={{ width: 170 }}
              popupMatchSelectWidth={false}
              options={[
                {
                  value: 'UNASSIGNED',
                  label: (
                    <Space size={6}>
                      <Avatar size={18} icon={<UserOutlined />} />
                      <span>Chưa phân công</span>
                    </Space>
                  ),
                },
                ...members.map((m) => ({
                  value: m.userId,
                  label: (
                    <Space size={6}>
                      <Avatar
                        size={18}
                        src={m.avatarUrl}
                        icon={!m.avatarUrl && <UserOutlined />}
                      />
                      <span>{m.displayName}</span>
                      <Text type="secondary" style={{ fontSize: 11 }}>
                        ({m.role})
                      </Text>
                    </Space>
                  ),
                })),
              ]}
            />
          </Flex>

          {/* Unmatched Assignee Warning Alert / Badge */}
          {isAssigneeUnmatched && (
            <Tag
              color="warning"
              icon={<ExclamationCircleOutlined />}
              style={{ borderRadius: 6, margin: 0 }}
            >
              AI gợi ý: <strong>{task.assignee?.displayName}</strong> (chưa khớp thành viên)
            </Tag>
          )}
        </Flex>

        {/* Labels Section */}
        <Flex align="center" gap={6} wrap="wrap">
          <Text type="secondary" style={{ fontSize: 12, display: 'flex', alignItems: 'center', gap: 4 }}>
            <TagOutlined /> Nhãn:
          </Text>

          {task.labels.map((lbl, lblIdx) => (
            <Tag
              key={`${lbl.name}-${lblIdx}`}
              closable
              onClose={() => onRemoveLabel(task.tempId, lblIdx)}
              color={lbl.exists ? 'blue' : 'orange'}
              style={{
                borderRadius: 12,
                fontSize: 12,
                display: 'inline-flex',
                alignItems: 'center',
                margin: 0,
              }}
            >
              {lbl.name}
              {!lbl.exists && (
                <span style={{ fontSize: 10, opacity: 0.85, marginLeft: 4 }}>
                  (mới)
                </span>
              )}
            </Tag>
          ))}

          {/* Add Label triggers (custom or pick existing) */}
          {task.labels.length < 5 && (
            <>
              {isAddingCustomLabel ? (
                <Input
                  size="small"
                  value={customLabelInput}
                  onChange={(e) => setCustomLabelInput(e.target.value)}
                  onPressEnter={handleAddCustomLabel}
                  onBlur={handleAddCustomLabel}
                  autoFocus
                  placeholder="Nhập tên nhãn..."
                  maxLength={80}
                  style={{ width: 120, borderRadius: 12, fontSize: 11.5 }}
                />
              ) : (
                <Space size={4}>
                  <Button
                    size="small"
                    type="dashed"
                    icon={<PlusOutlined />}
                    onClick={() => setIsAddingCustomLabel(true)}
                    style={{ borderRadius: 12, fontSize: 11.5 }}
                  >
                    Nhãn
                  </Button>

                  {availableWorkspaceLabels.length > 0 && (
                    <Select
                      size="small"
                      placeholder="Chọn nhãn sẵn có"
                      value={null}
                      onChange={handleSelectWorkspaceLabel}
                      style={{ width: 140 }}
                      options={availableWorkspaceLabels.map((l) => ({
                        value: l.name,
                        label: (
                          <Tag color="cyan" style={{ borderRadius: 8, margin: 0 }}>
                            {l.name}
                          </Tag>
                        ),
                      }))}
                    />
                  )}
                </Space>
              )}
            </>
          )}
        </Flex>

        {/* Task Description */}
        <div>
          <Input.TextArea
            value={task.description ?? ''}
            onChange={handleDescriptionChange}
            placeholder="Mô tả chi tiết sub-task (tùy chọn)..."
            maxLength={2000}
            rows={2}
            style={{
              fontSize: 12.5,
              borderRadius: 6,
              backgroundColor: '#f8fafc',
            }}
          />
        </div>
      </Flex>
    </Card>
  )
}
