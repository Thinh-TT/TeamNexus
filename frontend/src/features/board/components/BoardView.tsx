import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react'
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
  Segmented,
  Select,
  Space,
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
import { AgentChatFab } from '../../ai/components/AgentChatFab'
import { AgentChatPanel, AGENT_CHAT_DROPZONE_ID } from '../../ai/components/AgentChatPanel'
import { AiActionHistoryDrawer } from '../../ai/components/AiActionHistoryDrawer'
import { NotificationDrawer } from '../../ai/components/NotificationDrawer'
import { ObserverRunsDrawer } from '../../ai/components/ObserverRunsDrawer'
import { aiActionApi } from '../../ai/services/aiActionApi'
import { notificationApi } from '../../ai/services/notificationApi'
import { BoardModal } from './BoardModal'
import { ColumnModal } from './ColumnModal'
import { KanbanColumn } from './KanbanColumn'
import { TaskCalendar } from './TaskCalendar'
import { TaskCard } from './TaskCard'
import { TaskDetailModal } from './TaskDetailModal'
import { useWorkspaceMembers } from '../hooks/useWorkspaceMembers'
import { COLD_START_MESSAGE, COLD_START_THRESHOLD_MS } from '../utils/reconnectPolicy'

interface BoardViewProps {
  workspaceId: string
  boardId: string
}

/**
 * Hai chế độ xem của một board (Giai đoạn 13 §1).
 *
 * `kanban` là **mặc định** và phải giữ nguyên như vậy: `BoardView.test.tsx` có 11 test khẳng định nội
 * dung Kanban ngay khi mount, nên đổi mặc định (hoặc dùng `Tabs` — vốn không render children của tab
 * không hoạt động) sẽ làm đỏ cả loạt test đang xanh mà không đem lại lợi ích gì.
 */
type BoardViewMode = 'kanban' | 'calendar'


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

  /**
   * Chế độ xem (Giai đoạn 13 §1). Mặc định `kanban` ⇒ hành vi và test hiện tại không đổi.
   * Cố ý dùng `useState` + render có điều kiện thay vì antd `Tabs`: đổi view **không** được unmount
   * `useBoard`, nếu không kết nối SignalR realtime sẽ bị ngắt mỗi lần người dùng xem lịch.
   */
  const [viewMode, setViewMode] = useState<BoardViewMode>('kanban')

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

  /**
   * AI Agent Chat (Phase 14.5 §B): a docked panel, not a Drawer.
   *
   * The feature's defining interaction is dragging a card out of a column and into the chat, so the panel
   * has to be part of the layout — an overlay would sit on top of the very cards the user is trying to
   * drag, and dnd-kit would have no stable measurable target underneath it.
   */
  const [agentChatOpen, setAgentChatOpen] = useState(false)
  /**
   * Panel width. The spec asks for "about a quarter of the board", which is only meaningful for the board
   * the user is actually looking at — so the default is a fraction of the measured canvas and the user can
   * drag the divider. Clamped so the panel can never swallow the board or collapse to nothing.
   */
  const [agentChatWidth, setAgentChatWidth] = useState(360)
  const [agentChatAttachRequest, setAgentChatAttachRequest] = useState<{
    token: number
    task: { taskId: string; title: string }
  } | null>(null)
  const [agentChatAttachedCount, setAgentChatAttachedCount] = useState(0)

  const canvasRef = useRef<HTMLDivElement | null>(null)

  // Kept in a ref so `openAgentChat` can tell "open it" from "it is already open" without re-creating the
  // callback (and therefore without re-rendering the Kanban canvas) on every open/close.
  const agentChatOpenRef = useRef(false)

  const AGENT_CHAT_MIN_WIDTH = 320
  const AGENT_CHAT_MAX_WIDTH = 560

  useEffect(() => {
    agentChatOpenRef.current = agentChatOpen
  }, [agentChatOpen])

  // "One quarter of the board, within reason": measured on first open rather than guessed from the window,
  // because the board canvas is not the whole window (the header and the app shell take their share).
  const openAgentChat = useCallback(() => {
    // Already open ⇒ keep the width the user dragged to. Recomputing it here would silently undo their
    // resize every time a card is attached, which is exactly the kind of friction Phase 14.6 §A1 removes.
    if (agentChatOpenRef.current) {
      return
    }

    const canvasWidth = canvasRef.current?.clientWidth ?? window.innerWidth

    setAgentChatWidth(
      Math.round(
        Math.max(AGENT_CHAT_MIN_WIDTH, Math.min(canvasWidth / 4, AGENT_CHAT_MAX_WIDTH))
      )
    )
    setAgentChatOpen(true)
  }, [])

  /**
   * Phase 14.6 §A1: a card's "…" menu can send it straight into the chat. Shares the drop path's mechanism
   * (`attachRequest` + a token that changes every time) so the panel cannot tell the two routes apart — and
   * a second request for a card already attached still produces the "already attached" feedback.
   */
  const sendTaskToAgentChat = useCallback(
    (task: TaskResponse) => {
      openAgentChat()
      setAgentChatAttachRequest((prev) => ({
        token: (prev?.token ?? 0) + 1,
        task: { taskId: task.id, title: task.title },
      }))
    },
    [openAgentChat]
  )

  /** Resizes the panel from the divider the user drags. Never below the minimum, never past the maximum. */
  const handleAgentChatResizeStart = useCallback(
    (event: React.PointerEvent<HTMLDivElement>) => {
      event.preventDefault()

      const startX = event.clientX
      const startWidth = agentChatWidth

      const onMove = (moveEvent: PointerEvent) => {
        const next = startWidth - (moveEvent.clientX - startX)
        setAgentChatWidth(
          Math.round(Math.max(AGENT_CHAT_MIN_WIDTH, Math.min(next, AGENT_CHAT_MAX_WIDTH)))
        )
      }

      const onUp = () => {
        window.removeEventListener('pointermove', onMove)
        window.removeEventListener('pointerup', onUp)
      }

      window.addEventListener('pointermove', onMove)
      window.addEventListener('pointerup', onUp)
    },
    [agentChatWidth]
  )

  const [pendingAiActionCount, setPendingAiActionCount] = useState<number>(0)
  const [unreadNotificationCount, setUnreadNotificationCount] = useState<number>(0)
  const { isManagerOrAdmin } = useWorkspaceRole(workspaceId)

  // Fetch pending AI actions count. Phase 14.5 §Track A (A2.3) removed the duplicate fetch that used to sit
  // in the mount effect below: two calls to the same endpoint on every board open meant two round trips for
  // one number, and the `refreshPendingCount` helper already existed for the post-approval refresh — so the
  // mount path now just calls it.
  const refreshPendingCount = useCallback(() => {
    if (!boardId) return
    aiActionApi
      .listAiActions(boardId, { status: 'Pending', take: 100 })
      .then((logs) => setPendingAiActionCount(logs.length))
      .catch((err: unknown) => {
        // Phase 14.5 (A2.3): this used to swallow silently. A wrong badge with no trace is worse than a
        // noisy log — the count is advisory, so the request must not fail, but the reason must be findable.
        console.warn('[BoardView] Không tải được số hành động AI đang chờ duyệt', err)
      })
  }, [boardId])

  // Fetch unread notification count
  const refreshNotificationCount = useCallback(() => {
    notificationApi
      .listNotifications({ take: 1 })
      .then((res) => setUnreadNotificationCount(res.unreadCount))
      .catch((err: unknown) => {
        // Phase 14.5 (A2.3): same reasoning as the pending count — advisory data must not break the board,
        // but a silently wrong badge should leave a trace.
        console.warn('[BoardView] Không tải được số thông báo chưa đọc', err)
      })
  }, [])

  useEffect(() => {
    // Phase 14.5 (A2.3): ONE fetch for the pending count, through the same helper the modals call after a
    // decision. The duplicate inline call that used to live here is gone, and the helper logs its failures.
    refreshPendingCount()
    refreshNotificationCount()
  }, [refreshPendingCount, refreshNotificationCount])

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

  const totalTaskCount = useMemo(() => {
    return Object.values(tasksByColumn).reduce((acc, tasks) => acc + tasks.length, 0)
  }, [tasksByColumn])

  const filteredTaskCount = useMemo(() => {
    return Object.values(filteredTasksByColumn).reduce((acc, tasks) => acc + tasks.length, 0)
  }, [filteredTasksByColumn])

  /**
   * Cùng tập thẻ mà Kanban đang hiển thị, nhưng ở dạng mảng phẳng — đầu vào của lịch (Giai đoạn 13 §1).
   *
   * Cố ý lấy từ `filteredTasksByColumn` (chứ không phải `tasksByColumn`) để lịch **tôn trọng** ô tìm
   * kiếm và bộ lọc ưu tiên đang bật. Hai màn hình cùng một board phải nói cùng một chuyện.
   */
  const allFilteredTasks = useMemo(
    () => Object.values(filteredTasksByColumn).flat(),
    [filteredTasksByColumn]
  )

  /**
   * Every card of the board, unfiltered — the input to the AI chat's card picker (Phase 14.6 §A1).
   *
   * Deliberately NOT `allFilteredTasks`: the board's search box answers "what do I want to look at right
   * now?", while the picker answers "which card am I talking about?". Sharing the filter would make a card
   * unfindable precisely when someone was searching for it.
   */
  const allBoardTasks = useMemo(() => Object.values(tasksByColumn).flat(), [tasksByColumn])

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

    // AI Agent Chat (Phase 14.5 §B): a card dragged onto the chat panel is ATTACHED, not moved.
    //
    // Checked FIRST, before any column resolution: without this early return the drop would fall through
    // to the "over a column/task" branches, fail to find a target column, and silently do nothing — the
    // card would appear to vanish. The task's own column is never touched, so a drag that ends here is a
    // pure no-op on the board.
    if (overId === AGENT_CHAT_DROPZONE_ID) {
      const dragged = Object.values(tasksByColumn)
        .flat()
        .find((t) => t.id === activeId)

      if (dragged) {
        openAgentChat()
        setAgentChatAttachRequest((prev) => ({
          token: (prev?.token ?? 0) + 1,
          // Only the id and the title are needed; the server reads the live task, so the copy here is for
          // the pill only.
          task: { taskId: dragged.id, title: dragged.title },
        }))
      }

      return
    }

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
      {/* Top Bar - Tier 1: Board Info & Main Action Buttons */}
      <div
        style={{
          background: '#ffffff',
          borderBottom: '1px solid #f1f5f9',
          padding: '10px 24px',
          boxShadow: '0 1px 2px rgba(0, 0, 0, 0.02)',
        }}
      >
        <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
          {/* Left: Navigation & Board Info */}
          <Flex align="center" gap={12}>
            <Button
              type="text"
              icon={<ArrowLeftOutlined />}
              onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
              style={{ borderRadius: 8 }}
            />
            <div>
              <Flex align="center" gap={8}>
                <Typography.Title level={4} style={{ margin: 0, color: '#0f172a', fontWeight: 600 }}>
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

          {/* Right: Primary Actions & Tools */}
          <Flex align="center" gap={8} wrap="wrap">
            {/* Workspace Navigation (Manager/Admin only) */}
            {isManagerOrAdmin && (
              <Space size={6}>
                <Button
                  icon={<BarChartOutlined />}
                  onClick={() =>
                    navigate(`/workspaces/${workspaceId}/reports?boardId=${boardId}`)
                  }
                  style={{
                    borderRadius: 8,
                    borderColor: '#e2e8f0',
                    color: '#475569',
                    fontSize: 12.5,
                  }}
                >
                  Báo cáo
                </Button>

                <Button
                  icon={<HistoryOutlined />}
                  onClick={() =>
                    navigate(`/workspaces/${workspaceId}/activity`)
                  }
                  style={{
                    borderRadius: 8,
                    borderColor: '#e2e8f0',
                    color: '#475569',
                    fontSize: 12.5,
                  }}
                >
                  Hoạt động
                </Button>

                <Button
                  icon={<SettingOutlined />}
                  onClick={() =>
                    navigate(`/workspaces/${workspaceId}/settings`)
                  }
                  style={{
                    borderRadius: 8,
                    borderColor: '#e2e8f0',
                    color: '#475569',
                    fontSize: 12.5,
                  }}
                >
                  Cài đặt
                </Button>
              </Space>
            )}

            {/* AI Action Buttons */}
            <Badge count={unreadNotificationCount} offset={[-4, 4]}>
              <Button
                icon={<BellOutlined />}
                onClick={() => setNotificationOpen(true)}
                style={{
                  borderRadius: 8,
                  borderColor: '#cbd5e1',
                  color: '#475569',
                  fontSize: 12.5,
                }}
              >
                Cảnh báo AI
              </Button>
            </Badge>

            <Badge count={pendingAiActionCount} offset={[-4, 4]}>
              <Button
                icon={<HistoryOutlined />}
                onClick={() => setAiHistoryOpen(true)}
                style={{
                  borderRadius: 8,
                  borderColor: '#cbd5e1',
                  color: '#475569',
                  fontSize: 12.5,
                }}
              >
                Lịch sử AI
              </Button>
            </Badge>

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
                  fontSize: 12.5,
                }}
              >
                AI Observer
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
                fontSize: 12.5,
              }}
            >
              AI Smart Setup
            </Button>

            {/* View switch (Giai đoạn 13 §1): "Bảng Kanban" (mặc định) ⇄ "Lịch theo hạn chót". */}
            <Segmented
              value={viewMode}
              onChange={(value) => setViewMode(value as BoardViewMode)}
              data-testid="board-view-switch"
              options={[
                { label: 'Bảng Kanban', value: 'kanban' },
                { label: 'Lịch', value: 'calendar' },
              ]}
              style={{ borderRadius: 8 }}
            />

            {/* Primary Action Button */}
            <Button
              type="primary"
              icon={<PlusOutlined />}
              onClick={() => {
                setEditingColumn(null)
                setColumnModalOpen(true)
              }}
              style={{
                backgroundColor: '#6366f1',
                borderRadius: 8,
                fontWeight: 500,
                boxShadow: '0 2px 4px rgba(99, 102, 241, 0.25)',
              }}
            >
              Thêm cột
            </Button>
          </Flex>
        </Flex>
      </div>

      {/* Top Bar - Tier 2: Search, Filters & Summary */}
      <div
        style={{
          background: '#ffffff',
          borderBottom: '1px solid #e2e8f0',
          padding: '8px 24px',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
          gap: 12,
          flexWrap: 'wrap',
        }}
      >
        {/* Left: Search & Workspace Search Link */}
        <Flex align="center" gap={10} style={{ flex: '1 1 320px', maxWidth: 520 }}>
          <Input
            prefix={<SearchOutlined style={{ color: '#94a3b8' }} />}
            placeholder="Tìm kiếm thẻ, nhãn, người..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            allowClear
            style={{ width: 260, borderRadius: 8 }}
          />

          <Button
            type="link"
            size="small"
            onClick={() => {
              const qParam = searchQuery.trim() ? `&q=${encodeURIComponent(searchQuery.trim())}` : ''
              navigate(`/workspaces/${workspaceId}/search?boardId=${boardId}${qParam}`)
            }}
            data-testid="board-search-workspace-btn"
            style={{ padding: '0 4px', fontSize: 13, color: '#6366f1' }}
          >
            Tìm trong workspace →
          </Button>
        </Flex>

        {/* Right: Filter & Quick Summary */}
        <Flex align="center" gap={10}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            {searchQuery || priorityFilter !== 'ALL'
              ? `Hiển thị ${filteredTaskCount}/${totalTaskCount} thẻ (${columns.length} cột)`
              : `${columns.length} cột · ${totalTaskCount} thẻ`}
          </Typography.Text>

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
            <Button
              icon={<ReloadOutlined />}
              onClick={refetch}
              style={{ borderRadius: 8 }}
            />
          </Tooltip>
        </Flex>
      </div>

      {viewMode === 'calendar' ? (
        <TaskCalendar
          workspaceId={workspaceId}
          boardId={boardId}
          tasks={allFilteredTasks}
          columns={columns}
          onTaskClick={(task) => setActiveTask(task)}
        />
      ) : (
      /* Main Kanban Canvas with DndContext, plus the docked AI Agent Chat (Phase 14.5 §B).
         One flex row: the canvas flexes, the panel keeps its width. This is what makes "drag a card into
         the chat" possible at all — the cards stay visible and draggable while the drop target is docked. */
      <div style={{ flex: 1, display: 'flex', minHeight: 0, overflow: 'hidden' }}>
      <div
        ref={canvasRef}
        style={{
          flex: 1,
          minWidth: 0,
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
              onSendToAiChat={sendTaskToAgentChat}
            />
          ))}

          {/* "+ Thêm cột" Button Card at the end of columns */}
          <div
            style={{
              minWidth: 260,
              width: 260,
              borderRadius: 14,
              border: '1.5px dashed #cbd5e1',
              padding: '16px',
              textAlign: 'center',
              backgroundColor: '#ffffff80',
              cursor: 'pointer',
              transition: 'all 0.18s ease-in-out',
            }}
            onClick={() => {
              setEditingColumn(null)
              setColumnModalOpen(true)
            }}
          >
            <Button
              type="dashed"
              block
              icon={<PlusOutlined />}
              style={{
                borderRadius: 8,
                height: 38,
                borderColor: '#cbd5e1',
                color: '#64748b',
                fontWeight: 500,
              }}
            >
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

      {/* Docked AI Agent Chat (Phase 14.5 §B). Mounted only while open: that is what makes the panel load
          the stored transcript on open (and re-load when the board changes) instead of on every board view. */}
      {agentChatOpen && (
        <>
          {/* Resize handle: a 6px column the user drags to widen or narrow the panel. */}
          <div
            role="separator"
            aria-orientation="vertical"
            aria-label="Kéo để đổi kích thước AI Agent Chat"
            data-testid="agent-chat-resizer"
            onPointerDown={handleAgentChatResizeStart}
            style={{ width: 6, cursor: 'col-resize', backgroundColor: 'transparent' }}
          />
          <AgentChatPanel
            boardId={boardId}
            columns={columns}
            width={agentChatWidth}
            onClose={() => setAgentChatOpen(false)}
            attachRequest={agentChatAttachRequest}
            onAttachmentsChange={setAgentChatAttachedCount}
            // Phase 14.6 §A1: the picker searches EVERY card of the board, not just the ones the board's own
            // search box currently shows — "which card do I mean?" is a different question, and inheriting
            // that filter would hide cards exactly when the user goes looking for them.
            tasks={allBoardTasks}
          />
        </>
      )}
      </div>
      )}

      <AgentChatFab
        open={agentChatOpen}
        attachedCount={agentChatAttachedCount}
        onToggle={() => (agentChatOpen ? setAgentChatOpen(false) : openAgentChat())}
      />

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
