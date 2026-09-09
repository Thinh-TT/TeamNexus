import React, { useMemo, useState } from 'react'
import {
  DndContext,
  type DragEndEvent,
  type DragOverEvent,
  DragOverlay,
  type DragStartEvent,
  KeyboardSensor,
  PointerSensor,
  closestCorners,
  useSensor,
  useSensors,
} from '@dnd-kit/core'
import { sortableKeyboardCoordinates } from '@dnd-kit/sortable'
import {
  ArrowLeftOutlined,
  LoadingOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import {
  Badge,
  Button,
  Flex,
  Input,
  Select,
  Spin,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import { useNavigate } from 'react-router-dom'
import { useBoard } from '../hooks/useBoard'
import type {
  ColumnResponse,
  HubConnectionStatus,
  TaskPriority,
  TaskResponse,
} from '../types/board.types'
import { BoardModal } from './BoardModal'
import { ColumnModal } from './ColumnModal'
import { KanbanColumn } from './KanbanColumn'
import { TaskCard } from './TaskCard'
import { TaskDetailModal } from './TaskDetailModal'

interface BoardViewProps {
  workspaceId: string
  boardId: string
}

const renderConnectionStatus = (status: HubConnectionStatus) => {
  switch (status) {
    case 'connected':
      return (
        <Tooltip title="Real-time: Kết nối máy chủ ổn định. Mọi thay đổi được đồng bộ tức thì.">
          <Tag color="success" style={{ display: 'flex', alignItems: 'center', gap: 4, margin: 0, borderRadius: 12 }}>
            <Badge status="success" />
            <span style={{ fontSize: 11.5 }}>Đã kết nối</span>
          </Tag>
        </Tooltip>
      )
    case 'reconnecting':
      return (
        <Tooltip title="Đang cố gắng kết nối lại máy chủ Real-time...">
          <Tag color="warning" style={{ display: 'flex', alignItems: 'center', gap: 4, margin: 0, borderRadius: 12 }}>
            <SyncOutlined spin />
            <span style={{ fontSize: 11.5 }}>Đang kết nối lại</span>
          </Tag>
        </Tooltip>
      )
    case 'connecting':
      return (
        <Tooltip title="Đang khởi tạo kết nối SignalR...">
          <Tag color="processing" style={{ display: 'flex', alignItems: 'center', gap: 4, margin: 0, borderRadius: 12 }}>
            <LoadingOutlined spin />
            <span style={{ fontSize: 11.5 }}>Đang kết nối</span>
          </Tag>
        </Tooltip>
      )
    case 'disconnected':
    default:
      return (
        <Tooltip title="Mất kết nối máy chủ Real-time. Dữ liệu sẽ cập nhật lại khi kết nối phục hồi.">
          <Tag color="default" style={{ display: 'flex', alignItems: 'center', gap: 4, margin: 0, borderRadius: 12 }}>
            <Badge status="default" />
            <span style={{ fontSize: 11.5 }}>Mất kết nối</span>
          </Tag>
        </Tooltip>
      )
  }
}

export const BoardView: React.FC<BoardViewProps> = ({ workspaceId, boardId }) => {
  const navigate = useNavigate()
  const {
    board,
    columns,
    tasksByColumn,
    workspaceLabels,
    activeTask,
    connectionStatus,
    isLoading,
    refetch,
    setActiveTask,
    createColumn,
    updateColumn,
    deleteColumn,
    createTask,
    updateTask,
    moveTask,
    deleteTask,
    createLabel,
    attachLabel,
    detachLabel,
  } = useBoard(workspaceId, boardId)

  // Drag & drop state
  const [activeDragTask, setActiveDragTask] = useState<TaskResponse | null>(null)

  // Search & Filter state
  const [searchQuery, setSearchQuery] = useState('')
  const [priorityFilter, setPriorityFilter] = useState<TaskPriority | 'ALL'>('ALL')

  // Modals state
  const [columnModalOpen, setColumnModalOpen] = useState(false)
  const [editingColumn, setEditingColumn] = useState<ColumnResponse | null>(null)
  const [boardModalOpen, setBoardModalOpen] = useState(false)

  // Configure Dnd sensors (Pointer with 5px threshold to allow card clicks)
  const sensors = useSensors(
    useSensor(PointerSensor, {
      activationConstraint: {
        distance: 5,
      },
    }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  )

  // Filter tasks per column based on search & priority
  const filteredTasksByColumn = useMemo(() => {
    const result: Record<string, TaskResponse[]> = {}
    const query = searchQuery.trim().toLowerCase()

    Object.entries(tasksByColumn).forEach(([colId, tasks]) => {
      result[colId] = tasks.filter((t) => {
        const matchesQuery =
          !query ||
          t.title.toLowerCase().includes(query) ||
          t.assigneeName?.toLowerCase().includes(query) ||
          t.labels?.some((l) => l.name.toLowerCase().includes(query))

        const matchesPriority =
          priorityFilter === 'ALL' || t.priority === priorityFilter

        return matchesQuery && matchesPriority
      })
    })

    return result
  }, [tasksByColumn, searchQuery, priorityFilter])

  // ---- Drag & Drop Event Handlers ----
  const handleDragStart = (event: DragStartEvent) => {
    const { active } = event
    const activeData = active.data.current
    if (activeData?.type === 'Task') {
      setActiveDragTask(activeData.task as TaskResponse)
    }
  }

  const handleDragOver = (_event: DragOverEvent) => {
    // Cross-column preview handled optimistically or on drop
  }

  const handleDragEnd = async (event: DragEndEvent) => {
    const { active, over } = event
    setActiveDragTask(null)

    if (!over) return

    const activeId = String(active.id)
    const overId = String(over.id)

    if (activeId === overId) return

    // Find source column and task
    let sourceColId: string | null = null
    let activeTaskObj: TaskResponse | null = null

    for (const [colId, tasks] of Object.entries(tasksByColumn)) {
      const found = tasks.find((t) => t.id === activeId)
      if (found) {
        sourceColId = colId
        activeTaskObj = found
        break
      }
    }

    if (!sourceColId || !activeTaskObj) return

    // Find target column and target position
    let targetColId: string | null = null
    let targetPosition = 0

    const isOverColumn = columns.some((c) => c.id === overId)
    if (isOverColumn) {
      targetColId = overId
      targetPosition = tasksByColumn[overId]?.length || 0
    } else {
      // Over a task in a column
      for (const [colId, tasks] of Object.entries(tasksByColumn)) {
        const targetIndex = tasks.findIndex((t) => t.id === overId)
        if (targetIndex !== -1) {
          targetColId = colId
          targetPosition = targetIndex
          break
        }
      }
    }

    if (!targetColId) return

    // Call move task with optimistic update
    await moveTask(activeId, sourceColId, targetColId, targetPosition)
  }

  if (isLoading && !board) {
    return (
      <Flex align="center" justify="center" style={{ height: '80vh' }}>
        <Spin size="large" tip="Đang tải bảng Kanban..." />
      </Flex>
    )
  }

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100vh',
        backgroundColor: '#f8fafc',
        overflow: 'hidden',
      }}
    >
      {/* Top Bar */}
      <div
        style={{
          background: '#ffffff',
          borderBottom: '1px solid #e2e8f0',
          padding: '12px 24px',
          boxShadow: '0 1px 2px rgba(0, 0, 0, 0.03)',
        }}
      >
        <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
          {/* Left: Navigation & Board Info */}
          <Flex align="center" gap={12}>
            <Button
              type="text"
              icon={<ArrowLeftOutlined />}
              onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
            />
            <div>
              <Flex align="center" gap={8}>
                <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
                  {board?.name ?? 'Kanban Board'}
                </Typography.Title>
                {renderConnectionStatus(connectionStatus)}
              </Flex>
              {board?.description && (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  {board.description}
                </Typography.Text>
              )}
            </div>
          </Flex>

          {/* Right: Search, Filter, Actions */}
          <Flex align="center" gap={10} wrap="wrap">
            <Input
              prefix={<SearchOutlined style={{ color: '#94a3b8' }} />}
              placeholder="Tìm kiếm thẻ, nhãn, người..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              allowClear
              style={{ width: 220, borderRadius: 8 }}
            />

            <Select
              value={priorityFilter}
              onChange={setPriorityFilter}
              style={{ width: 140 }}
              options={[
                { value: 'ALL', label: 'Tất cả mức độ' },
                { value: 'Urgent', label: '🔴 Khẩn cấp' },
                { value: 'High', label: '🟠 Cao' },
                { value: 'Medium', label: '🔵 Trung bình' },
                { value: 'Low', label: '⚪ Thấp' },
              ]}
            />

            <Tooltip title="Làm mới dữ liệu">
              <Button icon={<ReloadOutlined />} onClick={refetch} />
            </Tooltip>

            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => {
                setEditingColumn(null)
                setColumnModalOpen(true)
              }}
              style={{ backgroundColor: '#6366f1', borderRadius: 8 }}
            >
              Thêm cột
            </Button>
          </Flex>
        </Flex>
      </div>

      {/* Main Kanban Canvas with DndContext */}
      <div
        style={{
          flex: 1,
          padding: '20px 24px',
          overflowX: 'auto',
          overflowY: 'hidden',
          display: 'flex',
          gap: 16,
          alignItems: 'flex-start',
        }}
      >
        <DndContext
          sensors={sensors}
          collisionDetection={closestCorners}
          onDragStart={handleDragStart}
          onDragOver={handleDragOver}
          onDragEnd={handleDragEnd}
        >
          {columns.map((column) => (
            <KanbanColumn
              key={column.id}
              column={column}
              tasks={filteredTasksByColumn[column.id] || []}
              onTaskClick={(task) => setActiveTask(task)}
              onEditColumn={(col) => {
                setEditingColumn(col)
                setColumnModalOpen(true)
              }}
              onDeleteColumn={deleteColumn}
              onCreateTask={createTask}
            />
          ))}

          {/* "+ Thêm cột" Button Card at the end of columns */}
          <div
            style={{
              minWidth: 260,
              width: 260,
              borderRadius: 12,
              border: '1.5px dashed #cbd5e1',
              padding: '16px',
              textAlign: 'center',
              backgroundColor: '#ffffff60',
              cursor: 'pointer',
            }}
            onClick={() => {
              setEditingColumn(null)
              setColumnModalOpen(true)
            }}
          >
            <Button type="dashed" block icon={<PlusOutlined />}>
              Thêm cột mới
            </Button>
          </div>

          {/* Drag Overlay for smooth card pickup ghost preview */}
          <DragOverlay>
            {activeDragTask ? (
              <TaskCard
                task={activeDragTask}
                isDoneColumn={
                  columns.find((c) => c.id === activeDragTask.columnId)?.isDone
                }
                isDragOverlay
              />
            ) : null}
          </DragOverlay>
        </DndContext>
      </div>

      {/* Modals */}
      <TaskDetailModal
        open={!!activeTask}
        task={activeTask}
        columns={columns}
        workspaceLabels={workspaceLabels}
        onClose={() => setActiveTask(null)}
        onUpdateTask={updateTask}
        onMoveTask={moveTask}
        onDeleteTask={deleteTask}
        onCreateLabel={createLabel}
        onAttachLabel={attachLabel}
        onDetachLabel={detachLabel}
      />

      <ColumnModal
        open={columnModalOpen}
        column={editingColumn}
        onClose={() => {
          setColumnModalOpen(false)
          setEditingColumn(null)
        }}
        onSubmit={async (data) => {
          if (editingColumn) {
            await updateColumn(editingColumn.id, data)
          } else {
            await createColumn(data as { name: string; isDone?: boolean })
          }
        }}
      />

      <BoardModal
        open={boardModalOpen}
        board={board}
        onClose={() => setBoardModalOpen(false)}
        onSubmit={async () => {
          // Future board edit
        }}
      />
    </div>
  )
}
