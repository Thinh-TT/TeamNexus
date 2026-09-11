import React from 'react'
import {
  AlertOutlined,
  ClockCircleOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
  RedoOutlined,
  StopOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Collapse,
  Divider,
  Flex,
  Popconfirm,
  Space,
  Spin,
  Statistic,
  Tag,
  Timeline,
  Typography,
} from 'antd'
import dayjs from 'dayjs'
import type {
  AgentRunDetailResponse,
  AgentRunResponse,
} from '../types/agentRun.types'
import { getStatusTag, getStopReasonMessage } from '../utils/agentRunHelpers'
import { AgentDraftApproval } from './AgentDraftApproval'

interface AgentRunPanelProps {
  taskId: string
  currentRun: AgentRunResponse | null
  runDetail: AgentRunDetailResponse | null
  loading?: boolean
  actionLoading?: boolean
  isManagerOrAdmin?: boolean
  onStartRun: () => Promise<unknown>
  onRerun: (runId: string) => Promise<unknown>
  onCancelRun: (runId: string) => Promise<unknown>
  onRefresh?: () => void
}

export const AgentRunPanel: React.FC<AgentRunPanelProps> = ({
  taskId: _taskId,
  currentRun,
  runDetail,
  loading = false,
  actionLoading = false,
  isManagerOrAdmin = false,
  onStartRun,
  onRerun,
  onCancelRun,
  onRefresh,
}) => {
  if (loading && !currentRun) {
    return (
      <Flex justify="center" align="center" style={{ padding: '32px 0' }}>
        <Spin tip="Đang tải dữ liệu Agent..." />
      </Flex>
    )
  }

  const isRunning = currentRun?.status === 'Running'
  const isAwaitingClarification = currentRun?.status === 'AwaitingClarification'
  const trace = runDetail?.toolCallTrace || []

  return (
    <div data-testid="agent-run-panel" style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
      {/* Header card with status and control buttons */}
      <Card
        size="small"
        style={{
          borderRadius: 8,
          borderColor: isRunning ? '#93c5fd' : '#e2e8f0',
          background: isRunning ? '#f0f9ff' : '#ffffff',
        }}
        styles={{ body: { padding: 14 } }}
      >
        <Flex justify="space-between" align="center" wrap="wrap" gap={10}>
          <Flex align="center" gap={8} wrap="wrap">
            <Typography.Text strong style={{ fontSize: 14 }}>
              AI Agent Executor:
            </Typography.Text>
            {currentRun ? (
              getStatusTag(currentRun.status)
            ) : (
              <Tag color="default">Chưa chạy</Tag>
            )}
            {currentRun?.startedAt && (
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                <ClockCircleOutlined style={{ marginRight: 4 }} />
                {dayjs(currentRun.startedAt).format('HH:mm:ss DD/MM')}
              </Typography.Text>
            )}
          </Flex>

          {/* Action buttons (Only for Manager/Admin) */}
          {isManagerOrAdmin && (
            <Space size={8} wrap>
              {isAwaitingClarification && currentRun && (
                <Button
                  type="primary"
                  size="small"
                  icon={<RedoOutlined />}
                  loading={actionLoading}
                  onClick={() => onRerun(currentRun.id)}
                  style={{ backgroundColor: '#f59e0b', borderColor: '#f59e0b' }}
                  data-testid="rerun-btn"
                >
                  Chạy lại
                </Button>
              )}

              {isRunning && currentRun && (
                <Popconfirm
                  title="Huỷ tác vụ Agent?"
                  description="Agent sẽ dừng ngay ở vòng lặp tiếp theo."
                  onConfirm={() => onCancelRun(currentRun.id)}
                  okText="Huỷ run"
                  cancelText="Không"
                  okButtonProps={{ danger: true }}
                >
                  <Button
                    danger
                    size="small"
                    icon={<StopOutlined />}
                    loading={actionLoading}
                    data-testid="cancel-btn"
                  >
                    Huỷ
                  </Button>
                </Popconfirm>
              )}

              <Button
                type="primary"
                size="small"
                icon={<PlayCircleOutlined />}
                disabled={isRunning}
                loading={actionLoading}
                onClick={onStartRun}
                style={{ backgroundColor: isRunning ? undefined : '#6366f1' }}
                data-testid="start-agent-btn"
              >
                Chạy Agent
              </Button>
            </Space>
          )}
        </Flex>

        {/* Metric statistics */}
        {currentRun && (
          <Flex gap={24} wrap="wrap" style={{ marginTop: 14, paddingTop: 12, borderTop: '1px solid #f1f5f9' }}>
            <Statistic
              title={<span style={{ fontSize: 11.5, color: '#64748b' }}>Lượt gọi Tool</span>}
              value={currentRun.toolCallCount}
              valueStyle={{ fontSize: 16, fontWeight: 600, color: '#334155' }}
            />
            <Statistic
              title={<span style={{ fontSize: 11.5, color: '#64748b' }}>Tổng Token</span>}
              value={currentRun.totalTokens}
              valueStyle={{ fontSize: 16, fontWeight: 600, color: '#334155' }}
            />
            <Statistic
              title={<span style={{ fontSize: 11.5, color: '#64748b' }}>Lượt gọi LLM</span>}
              value={currentRun.llmCallCount}
              valueStyle={{ fontSize: 16, fontWeight: 600, color: '#334155' }}
            />
          </Flex>
        )}
      </Card>

      {/* Clarification prompt banner */}
      {isAwaitingClarification && currentRun?.clarificationQuestion && (
        <Alert
          type="warning"
          showIcon
          icon={<QuestionCircleOutlined />}
          message={
            <Typography.Text strong style={{ color: '#b45309', fontSize: 13 }}>
              Agent đang cần làm rõ thông tin:
            </Typography.Text>
          }
          description={
            <div style={{ marginTop: 4 }}>
              <Typography.Paragraph
                style={{
                  margin: '4px 0 8px',
                  fontSize: 13,
                  color: '#92400e',
                  fontWeight: 500,
                  whiteSpace: 'pre-wrap',
                }}
              >
                {currentRun.clarificationQuestion}
              </Typography.Paragraph>
              <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                💡 <strong>Hướng dẫn:</strong> Trưởng nhóm hoặc thành viên phụ trách hãy để lại bình luận trả lời trên thẻ công việc này, sau đó bấm <strong>"Chạy lại"</strong> để Agent tiếp tục.
              </Typography.Text>
            </div>
          }
          style={{ borderRadius: 8, borderColor: '#fde68a', backgroundColor: '#fffbeb' }}
          data-testid="clarification-alert"
        />
      )}

      {/* Previous resolution context (if rerun) */}
      {runDetail?.previousQuestion && (
        <Card
          size="small"
          style={{ background: '#f8fafc', borderRadius: 8, borderColor: '#e2e8f0' }}
          styles={{ body: { padding: '10px 14px' } }}
        >
          <Typography.Text strong style={{ fontSize: 12, color: '#475569', display: 'block' }}>
            Câu hỏi trước đó:
          </Typography.Text>
          <Typography.Paragraph
            type="secondary"
            style={{ fontSize: 12, margin: '2px 0 6px', fontStyle: 'italic' }}
          >
            "{runDetail.previousQuestion}"
          </Typography.Paragraph>

          {runDetail.resolutionCommentContent && (
            <>
              <Typography.Text strong style={{ fontSize: 12, color: '#475569', display: 'block' }}>
                Câu trả lời tiếp nhận:
              </Typography.Text>
              <Typography.Paragraph
                style={{ fontSize: 12, margin: '2px 0 0', color: '#10b981', fontWeight: 500 }}
              >
                "{runDetail.resolutionCommentContent}"
              </Typography.Paragraph>
            </>
          )}
        </Card>
      )}

      {/* Error / Stop reason alert */}
      {currentRun?.status === 'Failed' && (
        <Alert
          type="error"
          showIcon
          icon={<AlertOutlined />}
          message="Tác vụ AI Agent gặp sự cố"
          description={getStopReasonMessage(currentRun.stopReason, currentRun.error)}
          style={{ borderRadius: 8 }}
          data-testid="agent-error-alert"
        />
      )}

      {/* Accountability approval section */}
      {currentRun?.aiActionLogId && (
        <div>
          <Divider style={{ margin: '8px 0 14px' }}>
            <Typography.Text strong style={{ fontSize: 12.5, color: '#6366f1' }}>
              Bản thảo kết quả cần phê duyệt
            </Typography.Text>
          </Divider>
          <AgentDraftApproval
            aiActionLogId={currentRun.aiActionLogId}
            onActionComplete={onRefresh}
          />
        </div>
      )}

      {/* Tool Call Trace Timeline */}
      {trace.length > 0 && (
        <Collapse
          defaultActiveKey={isRunning ? ['trace'] : []}
          size="small"
          style={{ borderRadius: 8, background: '#ffffff', border: '1px solid #e2e8f0' }}
          items={[
            {
              key: 'trace',
              label: (
                <Flex justify="space-between" align="center" style={{ width: '100%', paddingRight: 8 }}>
                  <Typography.Text strong style={{ fontSize: 12.5 }}>
                    Nhật ký công cụ đã gọi ({trace.length})
                  </Typography.Text>
                  {currentRun?.traceTruncated && (
                    <Tag color="warning" style={{ fontSize: 10 }}>
                      Đã cắt bớt
                    </Tag>
                  )}
                </Flex>
              ),
              children: (
                <div style={{ maxHeight: 300, overflowY: 'auto', padding: '8px 4px' }}>
                  {currentRun?.traceTruncated && (
                    <Alert
                      type="warning"
                      message="Nhật ký tool call đã bị cắt ngắn do vượt quá giới hạn lưu trữ."
                      style={{ marginBottom: 12, borderRadius: 6, fontSize: 11.5 }}
                    />
                  )}
                  <Timeline
                    items={trace.map((entry, idx) => ({
                      key: idx,
                      color: entry.isError ? 'red' : '#6366f1',
                      children: (
                        <div style={{ fontSize: 12 }}>
                          <Flex justify="space-between" align="center">
                            <Typography.Text strong style={{ color: entry.isError ? '#dc2626' : '#1e293b' }}>
                              {entry.name}
                            </Typography.Text>
                            <Space size={6}>
                              <Tag style={{ margin: 0, fontSize: 10 }}>
                                {entry.durationMs}ms
                              </Tag>
                              <Typography.Text type="secondary" style={{ fontSize: 10 }}>
                                {dayjs(entry.at).format('HH:mm:ss')}
                              </Typography.Text>
                            </Space>
                          </Flex>
                          {entry.arguments && (
                            <Typography.Paragraph
                              ellipsis={{ rows: 2, expandable: true, symbol: 'xem thêm' }}
                              style={{
                                margin: '2px 0',
                                fontSize: 11,
                                color: '#64748b',
                                fontFamily: 'monospace',
                                background: '#f1f5f9',
                                padding: '2px 6px',
                                borderRadius: 4,
                              }}
                            >
                              {entry.arguments}
                            </Typography.Paragraph>
                          )}
                          {entry.resultSummary && (
                            <Typography.Paragraph
                              ellipsis={{ rows: 2, expandable: true, symbol: 'xem thêm' }}
                              style={{
                                margin: '2px 0 0',
                                fontSize: 11,
                                color: entry.isError ? '#b91c1c' : '#475569',
                              }}
                            >
                              {entry.resultSummary}
                            </Typography.Paragraph>
                          )}
                        </div>
                      ),
                    }))}
                  />
                </div>
              ),
            },
          ]}
        />
      )}
    </div>
  )
}
