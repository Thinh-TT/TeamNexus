import React, { useMemo, useState } from 'react'
import { Avatar, Button, Empty, Flex, Input, Tag, Tooltip, Typography } from 'antd'
import { CheckOutlined, PlusOutlined, SearchOutlined, UserOutlined } from '@ant-design/icons'
import type { TaskResponse } from '../../board/types/board.types'
import { getPriorityConfig } from '../../board/utils/taskPriority'
import type { AgentChatAttachment } from '../types/agentChat.types'
import { MAX_ATTACHED_TASKS } from '../utils/agentChatAttachments'

const { Text } = Typography

/** How many matches the picker renders at once. A Kanban board can hold hundreds of cards. */
const MAX_VISIBLE_RESULTS = 30

interface AgentChatTaskPickerProps {
  /** Every task of the board, in board order. The picker searches this list, not what is on screen. */
  tasks: TaskResponse[]
  /** Already attached: shown as taken so a user cannot pick the same card twice. */
  attachments: AgentChatAttachment[]
  /** Called with the chosen card. Deciding whether it MAY be attached stays with the panel. */
  onPick: (task: TaskResponse) => void
}

/**
 * Quick card search for the AI Agent Chat (Phase 14.6 §A1).
 *
 * <p><b>Why this exists next to drag &amp; drop.</b> Dragging a card into the panel is a precise gesture: on a
 * trackpad, a touch screen, or a board that scrolls horizontally, the card has to travel a long way into a
 * narrow drop zone and a slip silently drops it into another column instead. The picker is the same feature
 * without the gesture — and the drop zone stays, because for a card already on screen the drag is faster.</p>
 *
 * <p>The list is deliberately <b>not</b> limited by the board's own search box: this is a different question
 * ("which card do I mean?"), and inheriting the board filter would make cards unfindable exactly when the
 * user is looking for them.</p>
 */
export const AgentChatTaskPicker: React.FC<AgentChatTaskPickerProps> = ({
  tasks,
  attachments,
  onPick,
}) => {
  const [query, setQuery] = useState('')

  const attachedIds = useMemo(
    () => new Set(attachments.map((item) => item.taskId)),
    [attachments]
  )

  const matches = useMemo(() => {
    const needle = query.trim().toLowerCase()

    const filtered = needle
      ? tasks.filter(
          (task) =>
            task.title.toLowerCase().includes(needle) ||
            (task.assigneeName?.toLowerCase().includes(needle) ?? false)
        )
      : tasks

    return filtered.slice(0, MAX_VISIBLE_RESULTS)
  }, [tasks, query])

  const atCap = attachments.length >= MAX_ATTACHED_TASKS

  return (
    <div
      data-testid="agent-chat-task-picker"
      style={{ width: 300, display: 'flex', flexDirection: 'column', gap: 8 }}
    >
      <Input
        size="small"
        allowClear
        autoFocus
        prefix={<SearchOutlined style={{ color: '#94a3b8' }} />}
        placeholder="Tìm thẻ theo tiêu đề hoặc người phụ trách..."
        value={query}
        onChange={(e) => setQuery(e.target.value)}
        data-testid="agent-chat-task-picker-search"
      />

      <Text type="secondary" style={{ fontSize: 11 }}>
        {atCap
          ? `Đã gắn tối đa ${MAX_ATTACHED_TASKS} thẻ — bỏ bớt một thẻ để gắn thêm.`
          : `Đã gắn ${attachments.length}/${MAX_ATTACHED_TASKS} thẻ`}
      </Text>

      <div style={{ maxHeight: 260, overflowY: 'auto', display: 'flex', flexDirection: 'column', gap: 4 }}>
        {matches.length === 0 ? (
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description={<Text type="secondary" style={{ fontSize: 11 }}>Không tìm thấy thẻ nào</Text>}
            style={{ margin: '8px 0' }}
          />
        ) : (
          matches.map((task) => {
            const already = attachedIds.has(task.id)
            const disabled = already || atCap
            const priority = getPriorityConfig(task.priority)

            return (
              <Button
                key={task.id}
                type="text"
                block
                disabled={disabled}
                onClick={() => onPick(task)}
                data-testid={`agent-chat-picker-option-${task.id}`}
                style={{
                  height: 'auto',
                  padding: '6px 8px',
                  textAlign: 'left',
                  display: 'block',
                  whiteSpace: 'normal',
                }}
              >
                <Flex vertical gap={2} style={{ width: '100%' }}>
                  <Flex align="center" gap={6} justify="space-between">
                    <Text style={{ fontSize: 12.5, fontWeight: 500 }} ellipsis>
                      {task.title}
                    </Text>
                    {already ? (
                      <CheckOutlined style={{ color: '#10b981', fontSize: 11 }} />
                    ) : (
                      <PlusOutlined style={{ color: '#6366f1', fontSize: 11 }} />
                    )}
                  </Flex>

                  <Flex align="center" gap={6} wrap="wrap">
                    {priority && (
                      <Tag
                        style={{
                          margin: 0,
                          fontSize: 10,
                          lineHeight: '16px',
                          borderRadius: 4,
                          color: priority.color === 'default' ? '#475569' : undefined,
                          backgroundColor: priority.bg,
                          borderColor: priority.border,
                        }}
                      >
                        {priority.label}
                      </Tag>
                    )}

                    <Tooltip title={task.assigneeName ?? 'Chưa phân công'}>
                      <Flex align="center" gap={4}>
                        <Avatar
                          size={16}
                          icon={<UserOutlined />}
                          style={{
                            backgroundColor: task.assigneeIsAiAgent ? '#7c3aed' : '#6366f1',
                            fontSize: 9,
                          }}
                        >
                          {!task.assigneeIsAiAgent && task.assigneeName
                            ? task.assigneeName[0].toUpperCase()
                            : undefined}
                        </Avatar>
                        <Text type="secondary" style={{ fontSize: 11 }}>
                          {task.assigneeName ?? 'Chưa phân công'}
                        </Text>
                      </Flex>
                    </Tooltip>
                  </Flex>
                </Flex>
              </Button>
            )
          })
        )}
      </div>
    </div>
  )
}
