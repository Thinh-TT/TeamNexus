import React, { useCallback, useEffect, useRef, useState } from 'react'
import { useDroppable } from '@dnd-kit/core'
import {
  Alert,
  Avatar,
  Badge,
  Button,
  Card,
  Empty,
  Flex,
  Input,
  Popover,
  Select,
  Space,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import {
  CloseOutlined,
  CommentOutlined,
  DownOutlined,
  LoadingOutlined,
  PlusOutlined,
  RobotOutlined,
  SendOutlined,
  StopOutlined,
  ThunderboltOutlined,
  UserOutlined,
} from '@ant-design/icons'
import type { ColumnResponse, TaskResponse } from '../../board/types/board.types'
import { AGENT_CHAT_PROPOSAL_LABELS } from '../types/agentChat.types'
import type { AgentChatAttachment } from '../types/agentChat.types'
import { MAX_ATTACHED_TASKS, attachRefusalReason } from '../utils/agentChatAttachments'
import { useAgentChat } from '../hooks/useAgentChat'
import { AgentChatTaskPicker } from './AgentChatTaskPicker'

const { Text } = Typography

/** The droppable id `BoardView` looks for in `handleDragEnd`. Exported so the two cannot disagree. */
export const AGENT_CHAT_DROPZONE_ID = 'ai-agent-chat-dropzone'

interface AgentChatPanelProps {
  boardId: string
  columns: ColumnResponse[]
  onClose: () => void
  /** Width in pixels, owned by the parent so the Kanban canvas can flex around the panel. */
  width: number
  /**
   * A card the user just dragged into the panel. The `token` changes on every drop, so dropping the SAME
   * card twice still fires the effect (and the hook's dedupe then ignores it) — which is what keeps the
   * "already attached" feedback honest instead of silently doing nothing.
   */
  attachRequest?: { token: number; task: AgentChatAttachment } | null
  /** Lets the parent show the attachment count without owning the list. */
  onAttachmentsChange?: (count: number) => void
  /**
   * Every card of the board, for the Phase 14.6 §A1 picker. Optional so the panel stays renderable (and
   * every existing test valid) without it: the drag-and-drop path needs no task list at all.
   */
  tasks?: TaskResponse[]
}

/**
 * The board's AI Agent Chat panel (Phase 14.5 §B).
 *
 * <p><b>Why a docked panel and not a Drawer.</b> The feature's defining interaction is dragging a card
 * from a Kanban column into the chat to attach it. A panel that is part of the layout keeps the board
 * visible and draggable and gives dnd-kit a measurable drop target; an overlay would sit on top of the
 * cards the user is trying to drag.</p>
 *
 * <p><b>Two write paths, both human-initiated:</b> "Gửi đề xuất" turns the model's proposal into a
 * <c>Pending</c> action a Manager approves, and "Lưu thành bình luận" records the answer as a Pending
 * comment. Nothing the model says writes business data on its own.</p>
 */
export const AgentChatPanel: React.FC<AgentChatPanelProps> = ({
  boardId,
  columns,
  onClose,
  width,
  attachRequest,
  onAttachmentsChange,
  tasks = [],
}) => {
  const [input, setInput] = useState('')
  const [pickerOpen, setPickerOpen] = useState(false)
  const scrollRef = useRef<HTMLDivElement | null>(null)

  const {
    messages,
    streamingAnswer,
    attachments,
    proposal,
    model,
    personas,
    persona,
    setPersona,
    status,
    error,
    httpStatus,
    ask,
    cancel,
    attach,
    detach,
    saveAnswer,
    submitProposal,
    clearError,
  } = useAgentChat(boardId)

  const { setNodeRef, isOver } = useDroppable({ id: AGENT_CHAT_DROPZONE_ID })

  const isStreaming = status === 'streaming'
  const isBusy = isStreaming || status === 'saving' || status === 'confirming'

  // Phase 14.7 §B: a picker with one choice is a control, not a choice — with no alternative to offer it is
  // hidden rather than shown disabled.
  const showPersonaPicker = personas.length >= 2

  const [showScrollBottom, setShowScrollBottom] = useState(false)
  const isUserScrollingRef = useRef(false)

  /** The role a turn was answered as, in the server's own words. Falls back to the raw key if the catalogue
   * no longer contains it (a persona removed between the answer and this render). */
  const personaLabel = (key: string): string =>
    personas.find((p) => p.key === key)?.displayName ?? key

  const handleScroll = useCallback(() => {
    const node = scrollRef.current
    if (!node) return
    const distanceFromBottom = node.scrollHeight - node.scrollTop - node.clientHeight
    const isFar = distanceFromBottom > 90
    setShowScrollBottom(isFar)
    isUserScrollingRef.current = isFar
  }, [])

  const scrollToBottom = useCallback((smooth = true) => {
    const node = scrollRef.current
    if (node && typeof node.scrollTo === 'function') {
      isUserScrollingRef.current = false
      setShowScrollBottom(false)
      node.scrollTo({ top: node.scrollHeight, behavior: smooth ? 'smooth' : 'auto' })
    }
  }, [])

  // Keep the newest turn in view while text arrives, unless the user scrolled up to read earlier text.
  // Guarded because `Element.scrollTo` does not exist in jsdom.
  useEffect(() => {
    const node = scrollRef.current

    if (node && typeof node.scrollTo === 'function') {
      if (!isUserScrollingRef.current) {
        node.scrollTo({ top: node.scrollHeight })
      }
    }
  }, [messages, streamingAnswer])

  // A drop from the Kanban board. The refusal is surfaced rather than swallowed: a card that disappears
  // into nothing reads as a bug, and the cap is the one thing the user can actually fix.
  useEffect(() => {
    if (!attachRequest) {
      return
    }

    const reason = attachRefusalReason(attachments, attachRequest.task.taskId)

    if (reason) {
      message.warning(reason)
      return
    }

    attach(attachRequest.task)
    // `attachments` is intentionally NOT a dependency: this effect answers a DROP event, and re-running it
    // on every attachment change would re-fire `message.warning` for a card already known to be attached.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attachRequest?.token])

  useEffect(() => {
    onAttachmentsChange?.(attachments.length)
  }, [attachments.length, onAttachmentsChange])

  const handleSend = useCallback(async () => {
    if (!input.trim() || isStreaming) {
      return
    }

    const text = input
    setInput('')
    isUserScrollingRef.current = false
    setShowScrollBottom(false)
    await ask(text)
  }, [ask, input, isStreaming])

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void handleSend()
    }
  }

  const handleSave = async () => {
    const log = await saveAnswer()
    if (log) {
      message.success('Đã tạo đề xuất bình luận — chờ Manager duyệt trong Lịch sử AI')
    }
  }

  const handleSubmitProposal = async () => {
    const log = await submitProposal()
    if (log) {
      message.success('Đã tạo đề xuất — chờ Manager duyệt trong Lịch sử AI')
    }
  }

  // The picker and the drop zone share ONE refusal rule, so "why can't I attach this?" is answered the same
  // way whichever route the user took.
  const handlePickTask = (task: TaskResponse) => {
    const reason = attachRefusalReason(attachments, task.id)

    if (reason) {
      message.warning(reason)
      return
    }

    attach({ taskId: task.id, title: task.title })
    setPickerOpen(false)
  }

  const lastAssistant = [...messages].reverse().find((m) => m.role === 'assistant')
  const savable = streamingAnswer || lastAssistant?.content || ''
  const canSave = attachments.length > 0 && savable.length > 0 && savable.length <= 2000

  const attachmentRefusal =
    attachments.length >= MAX_ATTACHED_TASKS
      ? `Đã gắn tối đa ${MAX_ATTACHED_TASKS} thẻ`
      : 'Kéo thẻ từ bảng vào đây, hoặc bấm "Đính kèm thẻ" để tìm nhanh'

  return (
    <aside
      data-testid="agent-chat-panel"
      data-board-id={boardId}
      aria-label="AI Agent Chat của bảng"
      style={{
        width,
        minWidth: width,
        maxWidth: width,
        height: '100%',
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: '#ffffff',
        borderLeft: '1px solid #e2e8f0',
        boxShadow: '-2px 0 6px rgba(15, 23, 42, 0.03)',
      }}
    >
      {/* Header */}
      <Flex
        justify="space-between"
        align="center"
        style={{ padding: '12px 16px', borderBottom: '1px solid #f1f5f9', background: '#fafafa' }}
      >
        <Space size={10}>
          <div
            style={{
              width: 32,
              height: 32,
              borderRadius: 8,
              backgroundColor: '#f5f3ff',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <RobotOutlined style={{ color: '#7c3aed', fontSize: 18 }} />
          </div>
          <div>
            <Flex align="center" gap={6}>
              <Text strong style={{ fontSize: 13, color: '#0f172a' }}>AI Agent Chat</Text>
              <Tag color="purple" style={{ margin: 0, borderRadius: 10, fontSize: 10, lineHeight: '16px' }}>
                ⚡ Fast / 🧠 Deep
              </Tag>
            </Flex>
            <div>
              <Text type="secondary" style={{ fontSize: 11 }}>
                {model ? `Toàn bảng · ${model}` : 'Toàn bảng'}
              </Text>
            </div>
          </div>
        </Space>

        <Button
          type="text"
          size="small"
          icon={<CloseOutlined />}
          onClick={onClose}
          aria-label="Đóng AI Agent Chat"
          data-testid="agent-chat-close"
        />
      </Flex>

      {/* Persona picker (Phase 14.7 §B). Its own row rather than inside the header line: the label must stay
          readable next to the model name on a panel this narrow. */}
      {showPersonaPicker && (
        <Flex
          align="center"
          gap={8}
          data-testid="agent-chat-persona-row"
          style={{ padding: '8px 14px', borderBottom: '1px solid #f1f5f9' }}
        >
          <Text type="secondary" style={{ fontSize: 11, whiteSpace: 'nowrap' }}>
            Vai trò
          </Text>
          <Select
            size="small"
            allowClear
            value={persona ?? undefined}
            onChange={(value) => setPersona(value ?? null)}
            onClear={() => setPersona(null)}
            // Clearing is a real choice here: it hands the turn back to the server's own default, and the
            // request then carries no `persona` parameter at all.
            placeholder="Mặc định của hệ thống"
            options={personas.map((p) => ({ value: p.key, label: p.displayName }))}
            disabled={isBusy}
            style={{ minWidth: 180, flex: 1 }}
            data-testid="agent-chat-persona-select"
            aria-label="Vai trò trả lời"
          />
        </Flex>
      )}

      {/* Attached tasks: the drop target */}
      <div
        ref={setNodeRef}
        data-testid="agent-chat-attach-list"
        style={{
          padding: '10px 14px',
          borderBottom: '1px dashed #c7d2fe',
          backgroundColor: isOver ? '#eef2ff' : '#f8fafc',
          transition: 'background-color 0.15s ease-in-out',
        }}
      >
        <Flex justify="space-between" align="center" style={{ marginBottom: 6 }}>
          <Text type="secondary" style={{ fontSize: 11 }}>
            {attachmentRefusal}
          </Text>
          <Space size={6}>
            {attachments.length > 0 && (
              <Badge count={attachments.length} style={{ backgroundColor: '#6366f1' }} />
            )}
            {/* Phase 14.6 §A1: the same attachment, without the gesture. Drag & drop stays (it is faster
                for a card already on screen), but it is no longer the only way in. */}
            <Popover
              trigger="click"
              open={pickerOpen}
              onOpenChange={setPickerOpen}
              placement="bottomRight"
              title={<Text style={{ fontSize: 12 }}>Đính kèm thẻ vào AI Chat</Text>}
              content={
                <AgentChatTaskPicker
                  tasks={tasks}
                  attachments={attachments}
                  onPick={handlePickTask}
                />
              }
            >
              <Button
                size="small"
                icon={<PlusOutlined />}
                data-testid="agent-chat-attach-picker-button"
                style={{ fontSize: 11 }}
              >
                Đính kèm thẻ
              </Button>
            </Popover>
          </Space>
        </Flex>

        {attachments.length === 0 ? (
          <Text type="secondary" style={{ fontSize: 11, fontStyle: 'italic' }}>
            Chưa có thẻ nào được gắn — AI sẽ trả lời ở phạm vi bảng.
          </Text>
        ) : (
          <Flex wrap gap={6}>
            {attachments.map((task) => (
              <Tag
                key={task.taskId}
                color="purple"
                closable
                data-testid={`agent-chat-attachment-${task.taskId}`}
                onClose={(e) => {
                  e.preventDefault()
                  detach(task.taskId)
                }}
                style={{ margin: 0, fontSize: 11 }}
              >
                {task.title}
              </Tag>
            ))}
          </Flex>
        )}
      </div>

      {/* Conversation */}
      <div style={{ flex: 1, position: 'relative', minHeight: 0, display: 'flex', flexDirection: 'column' }}>
        <div
          ref={scrollRef}
          onScroll={handleScroll}
          data-testid="agent-chat-messages"
          style={{ flex: 1, overflowY: 'auto', padding: '12px 14px', display: 'flex', flexDirection: 'column', gap: 12 }}
        >
          {messages.length === 0 && !streamingAnswer && (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={
                <div style={{ textAlign: 'left' }}>
                  <div style={{ fontWeight: 600, color: '#334155', fontSize: 12 }}>
                    Hỏi AI về cả bảng này
                  </div>
                  <Text type="secondary" style={{ fontSize: 11 }}>
                    Kéo thẻ vào khung phía trên để AI tập trung vào đúng việc bạn đang hỏi.
                  </Text>
                </div>
              }
              style={{ marginTop: 24 }}
            />
          )}

          {messages.map((msg) => {
            const isUser = msg.role === 'user'
            return (
              <Flex
                key={msg.id}
                gap={8}
                align="flex-start"
                style={{ flexDirection: isUser ? 'row-reverse' : 'row' }}
              >
                <Avatar
                  size={26}
                  icon={isUser ? <UserOutlined /> : <RobotOutlined />}
                  style={{ backgroundColor: isUser ? '#3b82f6' : '#7c3aed', flexShrink: 0 }}
                />
                <div
                  style={{
                    maxWidth: '85%',
                    backgroundColor: isUser ? '#eff6ff' : '#f8fafc',
                    border: `1px solid ${isUser ? '#bfdbfe' : '#e2e8f0'}`,
                    borderRadius: 8,
                    padding: '8px 10px',
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                    fontSize: 12.5,
                    color: '#1e293b',
                    lineHeight: 1.5,
                  }}
                >
                  {/* Phase 14.7 §B: the role this answer was produced as. Only a turn produced by this
                      session has one — history loaded from the server shows no tag, because the server does
                      not record which persona answered it. */}
                  {!isUser && msg.personaKey && (
                    <div style={{ marginBottom: 4 }}>
                      <Tag
                        color="purple"
                        data-testid="agent-chat-persona-tag"
                        style={{ margin: 0, fontSize: 10.5, lineHeight: '18px', borderRadius: 4 }}
                      >
                        <RobotOutlined style={{ marginRight: 4 }} />
                        {personaLabel(msg.personaKey)}
                      </Tag>
                    </div>
                  )}
                  {msg.content}
                </div>
              </Flex>
            )
          })}

          {streamingAnswer && (
            <Flex gap={8} align="flex-start">
              <Avatar size={26} icon={<RobotOutlined />} style={{ backgroundColor: '#7c3aed', flexShrink: 0 }} />
              <div
                aria-live="polite"
                style={{
                  maxWidth: '85%',
                  backgroundColor: '#f8fafc',
                  border: '1px solid #c7d2fe',
                  borderRadius: 8,
                  padding: '8px 10px',
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  fontSize: 12.5,
                  color: '#1e293b',
                  lineHeight: 1.5,
                }}
              >
                {persona && (
                  <div style={{ marginBottom: 4 }}>
                    <Tag
                      color="purple"
                      data-testid="agent-chat-persona-tag"
                      style={{ margin: 0, fontSize: 10.5, lineHeight: '18px', borderRadius: 4 }}
                    >
                      <RobotOutlined style={{ marginRight: 4 }} />
                      {personaLabel(persona)}
                    </Tag>
                  </div>
                )}
                {streamingAnswer}
                {isStreaming && <LoadingOutlined style={{ marginLeft: 6, color: '#6366f1' }} spin />}
              </div>
            </Flex>
          )}
        </div>

        {showScrollBottom && (
          <Tooltip title="Cuộn xuống mới nhất">
            <Button
              shape="circle"
              size="small"
              icon={<DownOutlined />}
              onClick={() => scrollToBottom(true)}
              aria-label="Cuộn xuống mới nhất"
              data-testid="agent-chat-scroll-bottom"
              style={{
                position: 'absolute',
                bottom: 12,
                right: 14,
                boxShadow: '0 2px 8px rgba(0,0,0,0.15)',
                zIndex: 10,
                backgroundColor: '#ffffff',
                borderColor: '#cbd5e1',
                color: '#475569',
              }}
            />
          </Tooltip>
        )}
      </div>

      {/* Action card: the model's proposal, and the button that turns it into a Pending action */}
      {proposal && (
        <Card
          size="small"
          data-testid="agent-chat-proposal-card"
          style={{
            margin: '0 14px 8px',
            borderRadius: 10,
            borderColor: '#a7f3d0',
            backgroundColor: '#f0fdf4',
            boxShadow: '0 1px 3px rgba(16, 185, 129, 0.08)',
          }}
          title={
            <Space size={6}>
              <ThunderboltOutlined style={{ color: '#10b981' }} />
              <Text style={{ fontSize: 12, fontWeight: 600, color: '#065f46' }}>
                Đề xuất: {AGENT_CHAT_PROPOSAL_LABELS[proposal.kind]}
              </Text>
            </Space>
          }
        >
          <Text style={{ fontSize: 12, color: '#1e293b' }}>{proposal.summary}</Text>

          {proposal.kind === 'MoveTaskToColumn' && proposal.payload.columnId && (
            <div style={{ marginTop: 6 }}>
              <Text type="secondary" style={{ fontSize: 11 }}>
                Cột đích: {columns.find((c) => c.id === proposal.payload.columnId)?.name ?? 'không xác định'}
              </Text>
            </div>
          )}

          <Flex justify="flex-end" gap={8} style={{ marginTop: 10 }}>
            <Button
              type="primary"
              size="small"
              icon={<ThunderboltOutlined />}
              loading={status === 'confirming'}
              disabled={isBusy && status !== 'confirming'}
              onClick={handleSubmitProposal}
              data-testid="agent-chat-submit-proposal"
              style={{ backgroundColor: '#10b981', borderColor: '#059669', borderRadius: 6, fontWeight: 500 }}
            >
              Gửi đề xuất
            </Button>
          </Flex>
        </Card>
      )}

      {/* Errors */}
      {error && (
        <Alert
          type="error"
          // `title`, not `message`: antd v6 deprecated `message` on Alert (the same switch the existing
          // AiChatPanel made), and a deprecation warning in the test output is noise that hides real ones.
          title={<span style={{ fontSize: 12 }}>{error}</span>}
          description={
            httpStatus ? (
              <Text type="secondary" style={{ fontSize: 11 }}>Mã lỗi: {httpStatus}</Text>
            ) : undefined
          }
          closable
          onClose={clearError}
          style={{ margin: '0 14px 8px', fontSize: 12 }}
        />
      )}

      {/* Save + input */}
      <div style={{ padding: '10px 14px', borderTop: '1px solid #f1f5f9' }}>
        <Flex justify="space-between" align="center" style={{ marginBottom: 6 }}>
          <Tooltip
            title={
              attachments.length === 0
                ? 'Gắn ít nhất một thẻ để biết bình luận sẽ được lưu vào đâu'
                : savable.length > 2000
                  ? 'Bình luận tối đa 2000 ký tự — câu trả lời này quá dài để lưu'
                  : 'Lưu câu trả lời thành bình luận (cần Manager duyệt)'
            }
          >
            <Button
              size="small"
              type="link"
              icon={<CommentOutlined />}
              disabled={!canSave || isBusy}
              onClick={handleSave}
              data-testid="agent-chat-save-answer"
              style={{ fontSize: 11, padding: 0 }}
            >
              Lưu thành bình luận
            </Button>
          </Tooltip>

          <Text type="secondary" style={{ fontSize: 10 }}>
            {attachments.length}/{MAX_ATTACHED_TASKS} thẻ
          </Text>
        </Flex>

        <Input.TextArea
          rows={2}
          value={input}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Hỏi AI về bảng hoặc các thẻ đã gắn (Enter để gửi, Shift+Enter xuống dòng)..."
          disabled={isBusy}
          style={{ resize: 'none', borderRadius: 8, fontSize: 12.5 }}
        />

        <Flex justify="space-between" align="center" style={{ marginTop: 6 }}>
          <Text type="secondary" style={{ fontSize: 10 }}>
            Enter để gửi • Shift+Enter xuống dòng
          </Text>
          <Space size={6}>
            {isStreaming && (
              <Button size="small" danger icon={<StopOutlined />} onClick={cancel} data-testid="agent-chat-stop">
                Dừng
              </Button>
            )}
            <Button
              type="primary"
              size="small"
              icon={<SendOutlined />}
              onClick={handleSend}
              loading={isStreaming}
              disabled={!input.trim() || isBusy}
              data-testid="agent-chat-send"
              style={{ backgroundColor: '#4f46e5', borderRadius: 6 }}
            >
              Gửi
            </Button>
          </Space>
        </Flex>
      </div>
    </aside>
  )
}
