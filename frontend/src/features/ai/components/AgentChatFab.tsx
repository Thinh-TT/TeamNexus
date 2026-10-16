import React from 'react'
import { Badge, Button, Tooltip } from 'antd'
import { CloseOutlined, RobotOutlined } from '@ant-design/icons'

interface AgentChatFabProps {
  open: boolean
  /** Number of tasks currently attached; shown as a badge while the panel is closed. */
  attachedCount: number
  onToggle: () => void
}

/**
 * The floating button that opens the board's AI Agent Chat (Phase 14.5 §B).
 *
 * <p>Bottom-right, and it turns into a close button while the panel is open so the entry point and the
 * exit are the same affordance. It is hidden from the layout while the panel is docked (the panel has its
 * own close control) but stays mounted, so the badge state survives a toggle.</p>
 */
export const AgentChatFab: React.FC<AgentChatFabProps> = ({ open, attachedCount, onToggle }) => (
  <Tooltip title={open ? '' : 'Mở AI Agent Chat của bảng'} placement="left">
    <Badge
      count={open ? 0 : attachedCount}
      offset={[-2, 4]}
      data-testid="agent-chat-fab-badge"
      style={{ display: open ? 'none' : undefined }}
    >
      <Button
        type="primary"
        shape="circle"
        size="large"
        icon={open ? <CloseOutlined /> : <RobotOutlined />}
        onClick={onToggle}
        data-testid="agent-chat-fab"
        aria-label={open ? 'Đóng AI Agent Chat' : 'Mở AI Agent Chat của bảng'}
        style={{
          position: 'fixed',
          right: 24,
          bottom: 24,
          zIndex: 30,
          width: open ? 0 : 52,
          height: open ? 0 : 52,
          opacity: open ? 0 : 1,
          pointerEvents: open ? 'none' : 'auto',
          overflow: 'hidden',
          backgroundColor: '#6366f1',
          boxShadow: open ? 'none' : '0 6px 16px rgba(99, 102, 241, 0.35)',
          transition: 'opacity 0.18s ease, transform 0.18s ease',
        }}
      />
    </Badge>
  </Tooltip>
)
