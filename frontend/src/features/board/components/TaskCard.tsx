import React from 'react'
import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import {
  CalendarOutlined,
  CheckCircleFilled,
  MessageOutlined,
  MoreOutlined,
  QuestionCircleOutlined,
  RobotOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { Avatar, Button, Card, Dropdown, Flex, type MenuProps, Space, Tag, Tooltip, Typography } from 'antd'
import dayjs from 'dayjs'
import type { TaskResponse } from '../types/board.types'
import { agentApi } from '../../ai/services/agentApi'
import { AGENT_FALLBACK_DISPLAY_NAME } from '../../ai/utils/agentRunHelpers'
import { isOverdue, isTaskCompleted, overdueDays } from '../utils/taskDueDate'
import { getPriorityConfig } from '../utils/taskPriority'

interface TaskCardProps {
  task: TaskResponse
  isDoneColumn?: boolean
  onClick?: () => void
  isDragOverlay?: boolean
  /**
   * Phase 14.6 §A1: adds a "…" menu whose "Gửi vào AI Chat" action opens the board's AI Agent Chat panel
   * with this card already attached. Optional on purpose — the drag overlay renders the same card and must
   * not grow an interactive menu, and every existing caller keeps working unchanged.
   */
  onSendToAiChat?: (task: TaskResponse) => void
}

export const TaskCard: React.FC<TaskCardProps> = ({
  task,
  isDoneColumn = false,
  onClick,
  isDragOverlay = false,
  onSendToAiChat,
}) => {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: task.id,
    data: {
      type: 'Task',
      task,
    },
    disabled: isDragOverlay,
  })

  const priorityConfig = getPriorityConfig(task.priority)
  const isCompleted = isTaskCompleted(task, isDoneColumn)
  const taskIsOverdue = isOverdue(task, new Date(), isDoneColumn)
  const daysOverdue = overdueDays(task, new Date(), isDoneColumn)
  const hasActiveAgent = Boolean(task.activeAgentRunId)

  const [isHovered, setIsHovered] = React.useState(false)

  const style: React.CSSProperties = {
    transform: CSS.Translate.toString(transform),
    transition: transition
      ? `${transition}, box-shadow 0.18s ease, border-color 0.18s ease`
      : 'box-shadow 0.18s ease, border-color 0.18s ease, transform 0.18s ease',
    opacity: isDragging ? 0.35 : 1,
    cursor: 'grab',
    userSelect: 'none',
    boxShadow: isDragOverlay
      ? '0 16px 32px -4px rgba(15, 23, 42, 0.18), 0 8px 16px -4px rgba(15, 23, 42, 0.1)'
      : hasActiveAgent
        ? '0 0 0 1px rgba(124, 58, 237, 0.12), 0 6px 14px -2px rgba(124, 58, 237, 0.08)'
        : isHovered && !isDragging
          ? '0 6px 14px -2px rgba(15, 23, 42, 0.07), 0 2px 4px -1px rgba(15, 23, 42, 0.04)'
          : '0 1px 2px 0 rgba(15, 23, 42, 0.04)',
    border: isDragOverlay
      ? '1.5px solid #4f46e5'
      : hasActiveAgent
        ? '1px solid #c4b5fd'
        : isHovered && !isDragging
          ? '1px solid #cbd5e1'
          : '1px solid #e2e8f0',
    borderLeft: taskIsOverdue
      ? '3px solid #ef4444'
      : hasActiveAgent
        ? '3px solid #7c3aed'
        : isDragOverlay
          ? '1.5px solid #4f46e5'
          : isHovered && !isDragging
            ? '1px solid #cbd5e1'
            : '1px solid #e2e8f0',
    borderRadius: 10,
    background: hasActiveAgent
      ? 'linear-gradient(180deg, #faf5ff 0%, #ffffff 42%)'
      : '#ffffff',
    transformOrigin: '50% 50%',
    rotate: isDragOverlay ? '2deg' : '0deg',
  }

  const [clarificationQuestion, setClarificationQuestion] = React.useState<string | null>(null)
  const [agentRunDisplayName, setAgentRunDisplayName] = React.useState<string | null>(null)

  React.useEffect(() => {
    let ignore = false
    if (task.activeAgentRunId) {
      agentApi
        .listRuns(task.id, 1)
        .then((runs) => {
          if (!ignore) {
            const latest = runs.length > 0 ? runs[0] : null
            setClarificationQuestion(latest?.clarificationQuestion || null)
            setAgentRunDisplayName(latest?.agentDisplayName || null)
          }
        })
        .catch(() => {})
    } else {
      Promise.resolve().then(() => {
        if (!ignore) {
          setClarificationQuestion(null)
          setAgentRunDisplayName(null)
        }
      })
    }
    return () => {
      ignore = true
    }
  }, [task.id, task.activeAgentRunId])

  const aiAgentLabel = task.assigneeName || agentRunDisplayName || AGENT_FALLBACK_DISPLAY_NAME

  const visibleLabels = task.labels?.slice(0, 2) || []
  const extraLabels = task.labels && task.labels.length > 2 ? task.labels.slice(2) : []

  const showActionsMenu = Boolean(onSendToAiChat) && !isDragOverlay

  const cardMenuItems: MenuProps['items'] = [
    {
      key: 'send-to-ai-chat',
      icon: <RobotOutlined style={{ color: '#7c3aed' }} />,
      label: 'Gửi vào AI Chat',
      onClick: (info) => {
        info.domEvent.stopPropagation()
        onSendToAiChat?.(task)
      },
    },
  ]

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      onClick={onClick}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <Card
        size="small"
        variant="borderless"
        style={{ background: 'transparent', boxShadow: 'none' }}
        styles={{ body: { padding: '12px 13px' } }}
      >
        <Flex vertical gap={8}>
          {/* Top meta: Priority + Labels + Overdue badge + Active Agent badge */}
          {(priorityConfig || (task.labels && task.labels.length > 0) || taskIsOverdue || task.activeAgentRunId) && (
            <Flex wrap="wrap" gap={4} align="center">
              {taskIsOverdue && (
                <Tag
                  color="error"
                  style={{
                    margin: 0,
                    fontSize: 10.5,
                    lineHeight: '18px',
                    borderRadius: 5,
                    fontWeight: 600,
                  }}
                  data-testid="task-card-overdue-badge"
                >
                  {`Quá hạn ${daysOverdue} ngày`}
                </Tag>
              )}
              {priorityConfig && (
                <span
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: 4,
                    fontSize: 11,
                    fontWeight: 600,
                    color: priorityConfig.color,
                    backgroundColor: priorityConfig.bg,
                    border: `1px solid ${priorityConfig.border}`,
                    borderRadius: 5,
                    padding: '0 6px',
                    lineHeight: '18px',
                  }}
                >
                  <span
                    style={{
                      width: 5,
                      height: 5,
                      borderRadius: '50%',
                      backgroundColor: priorityConfig.color,
                      display: 'inline-block',
                    }}
                  />
                  {priorityConfig.label}
                </span>
              )}
              {visibleLabels.map((label) => (
                <Tag
                  key={label.id}
                  style={{
                    margin: 0,
                    fontSize: 10.5,
                    lineHeight: '18px',
                    borderRadius: 5,
                    backgroundColor: `${label.color}14`,
                    borderColor: `${label.color}38`,
                    color: label.color,
                    fontWeight: 600,
                  }}
                >
                  {label.name}
                </Tag>
              ))}
              {extraLabels.length > 0 && (
                <Tooltip
                  title={
                    <Flex vertical gap={2} style={{ padding: '2px 0' }}>
                      {extraLabels.map((l) => (
                        <span key={l.id} style={{ fontSize: 11.5 }}>
                          {l.name}
                        </span>
                      ))}
                    </Flex>
                  }
                >
                  <Tag
                    style={{
                      margin: 0,
                      fontSize: 10,
                      lineHeight: '18px',
                      borderRadius: 5,
                      backgroundColor: '#f1f5f9',
                      borderColor: '#cbd5e1',
                      color: '#475569',
                      fontWeight: 600,
                      cursor: 'pointer',
                    }}
                  >
                    +{extraLabels.length}
                  </Tag>
                </Tooltip>
              )}
              {task.activeAgentRunId && (
                <Tag
                  color="purple"
                  icon={<RobotOutlined />}
                  style={{
                    margin: 0,
                    fontSize: 10.5,
                    lineHeight: '18px',
                    borderRadius: 5,
                    fontWeight: 600,
                  }}
                  data-testid="agent-active-badge"
                >
                  AI Agent
                </Tag>
              )}
            </Flex>
          )}

          {/* Title */}
          <Flex align="flex-start" gap={6}>
            {isCompleted && (
              <CheckCircleFilled style={{ color: '#10b981', fontSize: 14, marginTop: 3 }} />
            )}
            <Typography.Text
              strong
              delete={isCompleted}
              style={{
                fontSize: 13.5,
                lineHeight: 1.45,
                color: isCompleted ? '#64748b' : '#0f172a',
                flex: 1,
                fontWeight: 600,
              }}
            >
              {task.title}
            </Typography.Text>

            {showActionsMenu && (
              <Dropdown menu={{ items: cardMenuItems }} trigger={['click']} placement="bottomRight">
                <Button
                  type="text"
                  size="small"
                  icon={<MoreOutlined style={{ fontSize: 14 }} />}
                  aria-label="Hành động khác cho thẻ"
                  data-testid={`task-card-menu-${task.id}`}
                  onClick={(e) => e.stopPropagation()}
                  onPointerDown={(e) => e.stopPropagation()}
                  style={{
                    flexShrink: 0,
                    marginTop: -4,
                    color: '#64748b',
                    opacity: isHovered ? 1 : 0.55,
                  }}
                />
              </Dropdown>
            )}
          </Flex>

          {/* Clarification question prompt if agent is awaiting clarification */}
          {clarificationQuestion && (
            <Flex
              align="flex-start"
              gap={6}
              data-testid="task-card-clarification"
              style={{
                backgroundColor: '#fffbeb',
                border: '1px solid #fde68a',
                borderRadius: 6,
                padding: '5px 8px',
                marginTop: 2,
              }}
            >
              <QuestionCircleOutlined style={{ color: '#d97706', fontSize: 12, marginTop: 2 }} />
              <Typography.Paragraph
                style={{
                  margin: 0,
                  fontSize: 11.5,
                  color: '#92400e',
                  lineHeight: 1.35,
                  display: '-webkit-box',
                  WebkitLineClamp: 2,
                  WebkitBoxOrient: 'vertical',
                  overflow: 'hidden',
                }}
              >
                {clarificationQuestion}
              </Typography.Paragraph>
            </Flex>
          )}

          {/* Bottom info: Due date, comments, assignee */}
          <Flex
            justify="space-between"
            align="center"
            style={{
              marginTop: 2,
              paddingTop: 6,
              borderTop: '1px solid #f8fafc',
            }}
          >
            <Space size={10}>
              {task.dueDate && (
                <Tooltip
                  title={`Hạn chót: ${dayjs(task.dueDate).format('DD/MM/YYYY')}${
                    taskIsOverdue ? ' (Quá hạn)' : ''
                  }`}
                >
                  <Space
                    size={4}
                    style={{
                      fontSize: 11.5,
                      color: taskIsOverdue ? '#ef4444' : isCompleted ? '#94a3b8' : '#64748b',
                      fontWeight: taskIsOverdue ? 600 : 500,
                      fontFamily: 'var(--tn-font-mono, monospace)',
                    }}
                  >
                    <CalendarOutlined />
                    <span>{dayjs(task.dueDate).format('DD/MM')}</span>
                  </Space>
                </Tooltip>
              )}

              {task.commentCount > 0 && (
                <Tooltip title={`${task.commentCount} bình luận`}>
                  <Space size={3} style={{ fontSize: 11.5, color: '#64748b', fontWeight: 500 }}>
                    <MessageOutlined />
                    <span>{task.commentCount}</span>
                  </Space>
                </Tooltip>
              )}
            </Space>

            {task.assigneeIsAiAgent ? (
              <Tooltip title={`Người phụ trách: ${aiAgentLabel}`}>
                <Avatar
                  size={22}
                  icon={<RobotOutlined />}
                  style={{
                    backgroundColor: '#7c3aed',
                    color: '#ffffff',
                    fontSize: 11,
                    boxShadow: '0 0 0 2px #ede9fe',
                  }}
                  aria-label={`Người phụ trách: ${aiAgentLabel}`}
                  data-testid="agent-assignee-avatar"
                />
              </Tooltip>
            ) : task.assigneeName ? (
              <Tooltip title={`Người phụ trách: ${task.assigneeName}`}>
                <Avatar
                  size={22}
                  style={{
                    backgroundColor: '#4f46e5',
                    fontSize: 10.5,
                    fontWeight: 600,
                  }}
                >
                  {task.assigneeName[0].toUpperCase()}
                </Avatar>
              </Tooltip>
            ) : (
              <Tooltip title="Chưa phân công">
                <Avatar
                  size={22}
                  icon={<UserOutlined />}
                  style={{ backgroundColor: '#f1f5f9', color: '#94a3b8', fontSize: 10 }}
                />
              </Tooltip>
            )}
          </Flex>
        </Flex>
      </Card>
    </div>
  )
}
