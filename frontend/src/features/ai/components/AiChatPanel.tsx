import React, { useState } from 'react'
import {
  Alert,
  Avatar,
  Button,
  Flex,
  Input,
  Space,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import {
  CommentOutlined,
  LoadingOutlined,
  RobotOutlined,
  SendOutlined,
  StopOutlined,
  UserOutlined,
} from '@ant-design/icons'
import { useAiChat } from '../hooks/useAiChat'

const { Text } = Typography

interface AiChatPanelProps {
  taskId: string
}

export const AiChatPanel: React.FC<AiChatPanelProps> = ({ taskId }) => {
  const [inputContent, setInputContent] = useState('')
  const {
    messages,
    streamingAnswer,
    status,
    error,
    ask,
    cancel,
    saveAnswer,
    clearError,
  } = useAiChat(taskId)

  const isStreaming = status === 'streaming'
  const isSaving = status === 'saving'

  const handleSend = async () => {
    if (!inputContent.trim() || isStreaming) return
    const text = inputContent
    setInputContent('')
    await ask(text)
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      handleSend()
    }
  }

  const handleSave = async (contentToSave: string) => {
    try {
      await saveAnswer(contentToSave)
      message.success('Đã tạo đề xuất — chờ Manager duyệt trong Lịch sử AI')
    } catch {
      // Error handled in hook and error state
    }
  }

  const lastAssistant = [...messages].reverse().find((m) => m.role === 'assistant')
  const activeAnswerToSave = streamingAnswer || lastAssistant?.content || ''
  const isTooLongToSave = activeAnswerToSave.length > 2000

  return (
    <Flex
      vertical
      gap={12}
      data-testid="ai-chat-panel"
      data-task-id={taskId}
      style={{ height: '100%', minHeight: 340 }}
    >
      {/* Messages Scroll Area */}
      <div
        style={{
          maxHeight: 280,
          overflowY: 'auto',
          display: 'flex',
          flexDirection: 'column',
          gap: 12,
          paddingRight: 4,
        }}
      >
        {messages.length === 0 && !streamingAnswer && (
          <div
            style={{
              padding: '24px 16px',
              textAlign: 'center',
              backgroundColor: '#f8fafc',
              borderRadius: 8,
              border: '1px dashed #cbd5e1',
            }}
          >
            <RobotOutlined style={{ fontSize: 28, color: '#6366f1', marginBottom: 8 }} />
            <div style={{ fontWeight: 600, color: '#334155', marginBottom: 4 }}>
              Hỏi AI về nhiệm vụ này
            </div>
            <Text type="secondary" style={{ fontSize: 12, display: 'block', marginBottom: 12 }}>
              AI sẽ trả lời dựa trên tiêu đề, mô tả và lịch sử bình luận của nhiệm vụ.
            </Text>
            <Space wrap size={6}>
              <Tag
                color="blue"
                style={{ cursor: 'pointer' }}
                onClick={() => setInputContent('Tóm tắt tiến độ và các điểm cần lưu ý của nhiệm vụ này')}
              >
                💡 Tóm tắt tiến độ
              </Tag>
              <Tag
                color="cyan"
                style={{ cursor: 'pointer' }}
                onClick={() => setInputContent('Gợi ý các bước tiếp theo để hoàn thành nhiệm vụ nhanh hơn')}
              >
                🚀 Gợi ý các bước tiếp theo
              </Tag>
            </Space>
          </div>
        )}

        {messages.map((msg) => {
          const isUser = msg.role === 'user'
          return (
            <Flex
              key={msg.id}
              gap={10}
              align="flex-start"
              style={{
                flexDirection: isUser ? 'row-reverse' : 'row',
              }}
            >
              <Avatar
                size={28}
                icon={isUser ? <UserOutlined /> : <RobotOutlined />}
                style={{
                  backgroundColor: isUser ? '#3b82f6' : '#7c3aed',
                  flexShrink: 0,
                }}
              />
              <div
                style={{
                  maxWidth: '85%',
                  backgroundColor: isUser ? '#eff6ff' : '#f8fafc',
                  border: `1px solid ${isUser ? '#bfdbfe' : '#e2e8f0'}`,
                  borderRadius: 8,
                  padding: '8px 12px',
                }}
              >
                <div
                  style={{
                    whiteSpace: 'pre-wrap',
                    wordBreak: 'break-word',
                    fontSize: 13,
                    color: '#1e293b',
                    lineHeight: 1.5,
                  }}
                >
                  {msg.content}
                </div>

                {!isUser && (
                  <Flex justify="flex-end" style={{ marginTop: 6 }}>
                    <Tooltip
                      title={
                        msg.content.length > 2000
                          ? 'Bình luận tối đa 2000 ký tự — câu trả lời này quá dài để lưu'
                          : 'Lưu câu trả lời này thành bình luận (cần Manager duyệt)'
                      }
                    >
                      <Button
                        size="small"
                        type="link"
                        icon={<CommentOutlined />}
                        disabled={msg.content.length > 2000 || isSaving}
                        loading={isSaving}
                        data-testid="ai-chat-save-btn"
                        onClick={() => handleSave(msg.content)}
                        style={{ fontSize: 11, padding: 0 }}
                      >
                        Lưu thành bình luận
                      </Button>
                    </Tooltip>
                  </Flex>
                )}
              </div>
            </Flex>
          )
        })}

        {/* Live streaming bubble */}
        {streamingAnswer && (
          <Flex gap={10} align="flex-start">
            <Avatar
              size={28}
              icon={<RobotOutlined />}
              style={{ backgroundColor: '#7c3aed', flexShrink: 0 }}
            />
            <div
              aria-live="polite"
              style={{
                maxWidth: '85%',
                backgroundColor: '#f8fafc',
                border: '1px solid #c7d2fe',
                borderRadius: 8,
                padding: '8px 12px',
              }}
            >
              <div
                style={{
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-word',
                  fontSize: 13,
                  color: '#1e293b',
                  lineHeight: 1.5,
                }}
              >
                {streamingAnswer}
                {isStreaming && (
                  <LoadingOutlined style={{ marginLeft: 6, color: '#6366f1' }} spin />
                )}
              </div>

              {!isStreaming && (
                <Flex justify="flex-end" style={{ marginTop: 6 }}>
                  <Tooltip
                    title={
                      isTooLongToSave
                        ? 'Bình luận tối đa 2000 ký tự — câu trả lời này quá dài để lưu'
                        : 'Lưu câu trả lời này thành bình luận (cần Manager duyệt)'
                    }
                  >
                    <Button
                      size="small"
                      type="link"
                      icon={<CommentOutlined />}
                      disabled={isTooLongToSave || isSaving}
                      loading={isSaving}
                      data-testid="ai-chat-save-btn"
                      onClick={() => handleSave(streamingAnswer)}
                      style={{ fontSize: 11, padding: 0 }}
                    >
                      Lưu thành bình luận
                    </Button>
                  </Tooltip>
                </Flex>
              )}
            </div>
          </Flex>
        )}
      </div>

      {/* Error display */}
      {error && (
        <Alert
          type="error"
          title={error}
          closable
          onClose={clearError}
          style={{ fontSize: 12, padding: '4px 10px', borderRadius: 6 }}
        />
      )}

      {/* Input area */}
      <Flex vertical gap={6} style={{ marginTop: 'auto' }}>
        <Input.TextArea
          rows={2}
          value={inputContent}
          onChange={(e) => setInputContent(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Hỏi AI về nhiệm vụ này (Enter để gửi, Shift+Enter xuống dòng)..."
          disabled={isStreaming || isSaving}
          style={{ resize: 'none', borderRadius: 8 }}
        />
        <Flex justify="space-between" align="center">
          <Text type="secondary" style={{ fontSize: 11 }}>
            Enter để gửi • Shift+Enter để xuống dòng
          </Text>
          <Space>
            {isStreaming && (
              <Button
                size="small"
                danger
                icon={<StopOutlined />}
                onClick={cancel}
                style={{ borderRadius: 6 }}
              >
                Dừng
              </Button>
            )}
            <Button
              type="primary"
              size="small"
              icon={<SendOutlined />}
              onClick={handleSend}
              loading={isStreaming}
              disabled={!inputContent.trim()}
              style={{ backgroundColor: '#6366f1', borderRadius: 6 }}
            >
              Gửi
            </Button>
          </Space>
        </Flex>
      </Flex>
    </Flex>
  )
}
