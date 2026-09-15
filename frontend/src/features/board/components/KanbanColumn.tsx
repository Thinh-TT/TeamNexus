import React, { useState } from 'react'
import { useDroppable } from '@dnd-kit/core'
import { SortableContext, verticalListSortingStrategy } from '@dnd-kit/sortable'
import {
  CheckCircleOutlined,
  CheckOutlined,
  CloseOutlined,
  DeleteOutlined,
  EditOutlined,
  MoreOutlined,
  PlusOutlined,
  QuestionCircleOutlined,
  RobotOutlined,
} from '@ant-design/icons'
import {
  Avatar,
  Badge,
  Button,
  Card,
  Dropdown,
  Flex,
  Input,
  type MenuProps,
  Popconfirm,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type {
  ColumnResponse,
  CreateTaskRequest,
  TaskResponse,
  WorkspaceMemberResponse,
} from '../types/board.types'
import { TaskCard } from './TaskCard'

interface KanbanColumnProps {
  column: ColumnResponse
  tasks: TaskResponse[]
  workspaceMembers?: WorkspaceMemberResponse[]
  onTaskClick: (task: TaskResponse) => void
  onEditColumn: (column: ColumnResponse) => void
  onDeleteColumn: (columnId: string) => void
  onCreateTask: (data: CreateTaskRequest) => Promise<unknown>
}

export const KanbanColumn: React.FC<KanbanColumnProps> = ({
  column,
  tasks,
  workspaceMembers,
  onTaskClick,
  onEditColumn,
  onDeleteColumn,
  onCreateTask,
}) => {
  const [isAddingTask, setIsAddingTask] = useState(false)
  const [newTaskTitle, setNewTaskTitle] = useState('')
  const [newTaskAssigneeId, setNewTaskAssigneeId] = useState<string | null>(null)
  const [isSubmittingTask, setIsSubmittingTask] = useState(false)

  const { setNodeRef, isOver } = useDroppable({
    id: column.id,
    data: {
      type: 'Column',
      column,
    },
  })

  const handleQuickAddTask = async () => {
    const trimmed = newTaskTitle.trim()
    if (!trimmed) {
      setIsAddingTask(false)
      return
    }

    setIsSubmittingTask(true)
    try {
      await onCreateTask({
        columnId: column.id,
        title: trimmed,
        assigneeId: newTaskAssigneeId || undefined,
      })
      setNewTaskTitle('')
      setNewTaskAssigneeId(null)
      setIsAddingTask(false)
    } finally {
      setIsSubmittingTask(false)
    }
  }

  const menuItems: MenuProps['items'] = [
    {
      key: 'edit',
      icon: <EditOutlined />,
      label: 'Chỉnh sửa cột',
      onClick: () => onEditColumn(column),
    },
    {
      type: 'divider',
    },
    {
      key: 'delete',
      icon: <DeleteOutlined />,
      danger: true,
      label: (
        <Popconfirm
          title="Xoá cột này?"
          description={
            tasks.length > 0
              ? `Cột đang có ${tasks.length} thẻ. Bạn phải chuyển hoặc xoá hết thẻ trước khi xoá cột (theo quy định hệ thống).`
              : 'Hành động này không thể hoàn tác.'
          }
          okText="Xoá"
          cancelText="Huỷ"
          okButtonProps={{ danger: true }}
          onConfirm={() => onDeleteColumn(column.id)}
        >
          <span>Xoá cột</span>
        </Popconfirm>
      ),
    },
  ]

  const taskIds = tasks.map((t) => t.id)

  return (
    <div
      ref={setNodeRef}
      style={{
        width: 300,
        minWidth: 300,
        maxWidth: 300,
        display: 'flex',
        flexDirection: 'column',
        borderRadius: 14,
        backgroundColor: isOver ? '#eef2ff' : '#f8fafc',
        border: isOver ? '1.5px dashed #6366f1' : '1px solid #e2e8f0',
        transition: 'all 0.18s ease-in-out',
        maxHeight: 'calc(100vh - 175px)',
        boxShadow: '0 1px 3px rgba(0, 0, 0, 0.02)',
      }}
    >
      {/* Column Header */}
      <Flex
        justify="space-between"
        align="center"
        style={{
          padding: '12px 14px 10px 14px',
          borderBottom: '1px solid #f1f5f9',
        }}
      >
        <Flex align="center" gap={8} style={{ overflow: 'hidden' }}>
          {column.isDone && (
            <Tooltip title="Cột hoàn thành (Done)">
              <CheckCircleOutlined style={{ color: '#10b981', fontSize: 14 }} />
            </Tooltip>
          )}
          {column.isClarification && (
            <Tooltip title="Cột chờ làm rõ (Clarification)">
              <QuestionCircleOutlined
                style={{ color: '#f59e0b', fontSize: 14 }}
                data-testid="clarification-col-icon"
              />
            </Tooltip>
          )}
          <Typography.Text
            strong
            style={{
              fontSize: 14,
              color: '#0f172a',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {column.name}
          </Typography.Text>
          <Badge
            count={tasks.length}
            showZero
            style={{
              backgroundColor: '#e2e8f0',
              color: '#475569',
              boxShadow: 'none',
              fontWeight: 600,
              fontSize: 11,
              borderRadius: 10,
              padding: '0 4px',
            }}
          />
        </Flex>

        <Space size={2}>
          <Tooltip title="Thêm thẻ nhanh">
            <Button
              type="text"
              size="small"
              icon={<PlusOutlined style={{ fontSize: 13, color: '#64748b' }} />}
              onClick={() => setIsAddingTask(true)}
            />
          </Tooltip>
          <Dropdown menu={{ items: menuItems }} trigger={['click']} placement="bottomRight">
            <Button
              type="text"
              size="small"
              icon={<MoreOutlined style={{ fontSize: 14, color: '#64748b' }} />}
            />
          </Dropdown>
        </Space>
      </Flex>

      {/* Task List (Scrollable) */}
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          overflowX: 'hidden',
          padding: '8px 10px',
          display: 'flex',
          flexDirection: 'column',
          gap: 8,
          minHeight: 80,
        }}
      >
        <SortableContext items={taskIds} strategy={verticalListSortingStrategy}>
          {tasks.map((task) => (
            <TaskCard
              key={task.id}
              task={task}
              isDoneColumn={column.isDone}
              onClick={() => onTaskClick(task)}
            />
          ))}
        </SortableContext>

        {tasks.length === 0 && !isAddingTask && (
          <Flex
            align="center"
            justify="center"
            style={{
              height: 70,
              border: '1px dashed #cbd5e1',
              borderRadius: 8,
              backgroundColor: '#ffffff50',
            }}
          >
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Kéo thẻ vào đây hoặc tạo mới
            </Typography.Text>
          </Flex>
        )}

        {/* Inline Quick Add Task Box */}
        {isAddingTask && (
          <Card
            size="small"
            style={{
              borderRadius: 8,
              boxShadow: '0 2px 8px rgba(0,0,0,0.06)',
              border: '1px solid #cbd5e1',
            }}
            bodyStyle={{ padding: '8px' }}
          >
            <Input.TextArea
              autoFocus
              placeholder="Nhập tiêu đề cho thẻ..."
              value={newTaskTitle}
              onChange={(e) => setNewTaskTitle(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  handleQuickAddTask()
                } else if (e.key === 'Escape') {
                  setIsAddingTask(false)
                }
              }}
              rows={2}
              style={{ resize: 'none', marginBottom: 8, fontSize: 13 }}
            />
            {workspaceMembers && workspaceMembers.length > 0 && (
              <Select
                allowClear
                placeholder="Chọn người thực hiện (tuỳ chọn)"
                value={newTaskAssigneeId}
                onChange={(val) => setNewTaskAssigneeId(val ?? null)}
                style={{ width: '100%', marginBottom: 8 }}
                size="small"
                data-testid="quick-add-assignee-select"
                options={workspaceMembers.map((m) => ({
                  value: m.userId,
                  label: (
                    <Flex align="center" gap={6}>
                      <Avatar
                        size={18}
                        icon={m.memberType === 'ai_agent' ? <RobotOutlined /> : undefined}
                        style={{
                          backgroundColor: m.memberType === 'ai_agent' ? '#7c3aed' : '#6366f1',
                          fontSize: 10,
                        }}
                      >
                        {m.memberType !== 'ai_agent' && (m.displayName?.[0]?.toUpperCase() || 'U')}
                      </Avatar>
                      <span>{m.displayName}</span>
                      {m.memberType === 'ai_agent' && (
                        <Tag color="purple" style={{ margin: 0, fontSize: 10, lineHeight: '16px' }}>
                          AI Agent
                        </Tag>
                      )}
                    </Flex>
                  ),
                }))}
              />
            )}
            <Flex justify="flex-end" gap={6}>
              <Button
                size="small"
                icon={<CloseOutlined />}
                onClick={() => {
                  setNewTaskTitle('')
                  setNewTaskAssigneeId(null)
                  setIsAddingTask(false)
                }}
              >
                Huỷ
              </Button>
              <Button
                type="primary"
                size="small"
                icon={<CheckOutlined />}
                loading={isSubmittingTask}
                onClick={handleQuickAddTask}
                style={{ backgroundColor: '#6366f1' }}
              >
                Thêm thẻ
              </Button>
            </Flex>
          </Card>
        )}
      </div>

      {/* Column Footer */}
      {!isAddingTask && (
        <div style={{ padding: '4px 10px 10px 10px' }}>
          <Button
            type="text"
            block
            icon={<PlusOutlined />}
            onClick={() => setIsAddingTask(true)}
            style={{
              color: '#64748b',
              textAlign: 'left',
              fontSize: 13,
              borderRadius: 6,
              height: 32,
            }}
          >
            Thêm thẻ mới
          </Button>
        </div>
      )}
    </div>
  )
}
