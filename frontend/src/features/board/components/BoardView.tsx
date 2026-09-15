import React, { useCallback, useEffect, useMemo, useState } from 'react'
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
  BarChartOutlined,
  BellOutlined,
  HistoryOutlined,
  LoadingOutlined,
  PlusOutlined,
  RadarChartOutlined,
  ReloadOutlined,
  RobotOutlined,
  SearchOutlined,
  SettingOutlined,
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
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { useBoard } from '../hooks/useBoard'
import type {
  ColumnResponse,
  HubConnectionStatus,
  TaskPriority,
  TaskResponse,
} from '../types/board.types'
import { SmartSetupModal } from '../../ai/components/SmartSetupModal'
import { AiActionHistoryDrawer } from '../../ai/components/AiActionHistoryDrawer'
import { NotificationDrawer } from '../../ai/components/NotificationDrawer'
import { ObserverRunsDrawer } from '../../ai/components/ObserverRunsDrawer'
import { aiActionApi } from '../../ai/services/aiActionApi'
import { notificationApi } from '../../ai/services/notificationApi'
import { BoardModal } from './BoardModal'
import { ColumnModal } from './ColumnModal'
import { KanbanColumn } from './KanbanColumn'
import { TaskCard } from './TaskCard'
import { TaskDetailModal } from './TaskDetailModal'
import { useWorkspaceMembers } from '../hooks/useWorkspaceMembers'
import { COLD_START_MESSAGE, COLD_START_THRESHOLD_MS } from '../utils/reconnectPolicy'

interface BoardViewProps {
  workspaceId: string
  boardId: string
}

const renderConnectionStatus = (
  status: HubConnectionStatus,
  reconnect?: () => void,
  isColdStarting = false
) => {
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
        <Tooltip
          title={
            isColdStarting
              ? COLD_START_MESSAGE
              : 'Đang cố gắng kết nối lại máy chủ Real-time...'
          }
        >
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
        <Flex align="center" gap={6}>
          <Tooltip title="Mất kết nối máy chủ Real-time. Dữ liệu sẽ cập nhật lại khi kết nối phục hồi.">
            <Tag color="default" style={{ display: 'flex', alignItems: 'center', gap: 4, margin: 0, borderRadius: 12 }}>
              <Badge status="default" />
              <span style={{ fontSize: 11.5 }}>Mất kết nối</span>
            </Tag>
          </Tooltip>
          {reconnect && (
            <Button
              size="small"
              onClick={reconnect}
              style={{ fontSize: 11.5, height: 22, padding: '0 8px', borderRadius: 10 }}
            >
              Kết nối lại
            </Button>
          )}
        </Flex>
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
    reconnect,
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

  const [isColdStarting, setIsColdStarting] = useState(false)

  useEffect(() => {
    if (connectionStatus !== 'reconnecting') {
      return
    }
    const timer = setTimeout(() => {
      setIsColdStarting(true)
    }, COLD_START_THRESHOLD_MS)
    return () => {
      clearTimeout(timer)
      setIsColdStarting(false)
    }
  }, [connectionStatus])

  const { members: workspaceMembers } = useWorkspaceMembers(workspaceId)

  // Drag & drop state
  const [activeDragTask, setActiveDragTask] = useState<TaskResponse | null>(null)

  // Search & Filter state
  const [searchQuery, setSearchQuery] = useState('')
  const [priorityFilter, setPriorityFilter] = useState<TaskPriority | 'ALL'>('ALL')

  // Modals and Drawer state
  const [columnModalOpen, setColumnModalOpen] = useState(false)
  const [editingColumn, setEditingColumn] = useState<ColumnResponse | null>(null)
  const [boardModalOpen, setBoardModalOpen] = useState(false)
  const [smartSetupModalOpen, setSmartSetupModalOpen] = useState(false)
  const [aiHistoryOpen, setAiHistoryOpen] = useState(false)
  const [notificationOpen, setNotificationOpen] = useState(false)
  const [observerRunsOpen, setObserverRunsOpen] = useState(false)
  const [pendingAiActionCount, setPendingAiActionCount] = useState<number>(0)
  const [unreadNotificationCount, setUnreadNotificationCount] = useState<number>(0)
  const { isManagerOrAdmin } = useWorkspaceRole(workspaceId)

  // Fetch pending AI actions count on mount / board change
  const refreshPendingCount = useCallback(() => {
    if (!boardId) return
    aiActionApi
      .listAiActions(boardId, { status: 'Pending', take: 100 })
      .then((logs) => setPendingAiActionCount(logs.length))
      .catch(() => {})
  }, [boardId])

  // Fetch unread notification count
  const refreshNotificationCount = useCallback(() => {
    notificationApi
      .listNotifications({ take: 1 })
      .then((res) => setUnreadNotificationCount(res.unreadCount))
      .catch(() => {})
  }, [])

  useEffect(() => {
    let ignore = false
    if (boardId) {
      aiActionApi
        .listAiActions(boardId, { status: 'Pending', take: 100 })
        .then((logs) => {
          if (!ignore) {
            setPendingAiActionCount(logs.length)
          }
        })
        .catch(() => {})
    }
    refreshNotificationCount()
    return () => {
      ignore = true
    }
  }, [boardId, refreshNotificationCount])

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

    for (const [colId, tasks] of Object.entries(tasksByColumn)) {
      result[colId] = tasks.filter((t) => {
        // Priority filter
        if (priorityFilter !== 'ALL' && t.priority !== priorityFilter) {
          return false
        }
        // Search query filter (matches title, description, assignee name, or label text)
        if (!query) return true
        const titleMatch = t.title.toLowerCase().includes(query)
        const descMatch = t.description?.toLowerCase().includes(query) ?? false
        const assigneeMatch =
          t.assigneeName?.toLowerCase().includes(query) ?? false
        const labelMatch = t.labels?.some((l) =>
          l.name.toLowerCase().includes(query)
        ) ?? false

        return titleMatch || descMatch || assigneeMatch || labelMatch
      })
    }
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
        <Spin size="large" description="Đang tải bảng Kanban..." />
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
                {renderConnectionStatus(connectionStatus, reconnect, isColdStarting)}
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

            <Button
              type="link"
              size="small"
              onClick={() => {
                const qParam = searchQuery.trim() ? `&q=${encodeURIComponent(searchQuery.trim())}` : ''
                navigate(`/workspaces/${workspaceId}/search?boardId=${boardId}${qParam}`)
              }}
              data-testid="board-search-workspace-btn"
              style={{ padding: '0 4px', fontSize: 13 }}
            >
              Tìm trong workspace →
            </Button>

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

            {/* AI Notifications Button with Unread Badge */}
            <Badge count={unreadNotificationCount} offset={[-4, 4]}>
              <Button
                icon={<BellOutlined />}
                onClick={() => setNotificationOpen(true)}
                style={{
                  borderRadius: 8,
                  borderColor: '#cbd5e1',
                  color: '#475569',
                }}
              >
                Cảnh báo AI
              </Button>
            </Badge>

            {/* AI History Button with Pending Badge */}
            <Badge count={pendingAiActionCount} offset={[-4, 4]}>
              <Button
                icon={<HistoryOutlined />}
                onClick={() => setAiHistoryOpen(true)}
                style={{
                  borderRadius: 8,
                  borderColor: '#cbd5e1',
                  color: '#475569',
                }}
              >
                Lịch sử AI
              </Button>
            </Badge>

            {/* AI Observer Runs (Manager/Admin only) */}
            {isManagerOrAdmin && (
              <Button
                icon={<RadarChartOutlined />}
                onClick={() => setObserverRunsOpen(true)}
                style={{
                  borderRadius: 8,
                  borderColor: '#818cf8',
                  color: '#4338ca',
                  fontWeight: 500,
                  backgroundColor: '#eef2ff',
                }}
              >
                AI Observer
              </Button>
            )}

            {/* Reports (Manager/Admin only) */}
            {isManagerOrAdmin && (
              <Button
                icon={<BarChartOutlined />}
                onClick={() =>
                  navigate(`/workspaces/${workspaceId}/reports?boardId=${boardId}`)
                }
                style={{
                  borderRadius: 8,
                  borderColor: '#818cf8',
                  color: '#4338ca',
                  fontWeight: 500,
                  backgroundColor: '#eef2ff',
                }}
              >
                Báo cáo
              </Button>
            )}

            {/* Workspace Activity (Manager/Admin only) */}
            {isManagerOrAdmin && (
              <Button
                icon={<HistoryOutlined />}
                onClick={() =>
                  navigate(`/workspaces/${workspaceId}/activity`)
                }
                style={{
                  borderRadius: 8,
                  borderColor: '#818cf8',
                  color: '#4338ca',
                  fontWeight: 500,
                  backgroundColor: '#eef2ff',
                }}
              >
                Hoạt động
              </Button>
            )}

            {/* Workspace Settings (Manager/Admin only) */}
            {isManagerOrAdmin && (
              <Button
                icon={<SettingOutlined />}
                onClick={() =>
                  navigate(`/workspaces/${workspaceId}/settings`)
                }
                style={{
                  borderRadius: 8,
                  borderColor: '#818cf8',
                  color: '#4338ca',
                  fontWeight: 500,
                  backgroundColor: '#eef2ff',
                }}
              >
                Cài đặt
              </Button>
            )}

            <Button
              icon={<RobotOutlined />}
              onClick={() => setSmartSetupModalOpen(true)}
              style={{
                borderRadius: 8,
                borderColor: '#818cf8',
                color: '#4f46e5',
                fontWeight: 500,
                backgroundColor: '#f5f3ff',
              }}
            >
              AI Smart Setup
            </Button>

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
              workspaceMembers={workspaceMembers}
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

      {/* Modals & Drawers */}
      <TaskDetailModal
        open={!!activeTask}
        task={activeTask}
        columns={columns}
        workspaceLabels={workspaceLabels}
        workspaceId={workspaceId}
        workspaceMembers={workspaceMembers}
        isManagerOrAdmin={isManagerOrAdmin}
        onClose={() => setActiveTask(null)}
        onUpdateTask={updateTask}
        onMoveTask={moveTask}
        onDeleteTask={deleteTask}
        onCreateLabel={createLabel}
        onAttachLabel={attachLabel}
        onDetachLabel={detachLabel}
        onRefreshBoard={refetch}
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

      <SmartSetupModal
        open={smartSetupModalOpen}
        onClose={() => {
          setSmartSetupModalOpen(false)
          refreshPendingCount()
        }}
        workspaceId={workspaceId}
        boardId={boardId}
        workspaceLabels={workspaceLabels}
        columns={columns}
        onApplied={() => {
          refetch()
          refreshPendingCount()
        }}
      />

      <AiActionHistoryDrawer
        open={aiHistoryOpen}
        onClose={() => {
          setAiHistoryOpen(false)
          refreshPendingCount()
        }}
        boardId={boardId}
        onBoardChanged={() => {
          refetch()
          refreshPendingCount()
        }}
      />

      <NotificationDrawer
        open={notificationOpen}
        onClose={() => {
          setNotificationOpen(false)
          refreshNotificationCount()
        }}
        workspaceId={workspaceId}
      />

      {isManagerOrAdmin && (
        <ObserverRunsDrawer
          open={observerRunsOpen}
          onClose={() => setObserverRunsOpen(false)}
          workspaceId={workspaceId}
          onScanFinished={() => {
            refreshNotificationCount()
            refetch()
          }}
        />
      )}
    </div>
  )
}
