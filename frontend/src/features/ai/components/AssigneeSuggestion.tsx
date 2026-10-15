import React, { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Flex,
  Space,
  Tag,
  Typography,
} from 'antd'
import {
  ThunderboltOutlined,
  UserOutlined,
  WarningOutlined,
  LoadingOutlined,
  DownOutlined,
  UpOutlined,
  ClockCircleOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons'
import { jevApi } from '../services/jevApi'
import type { AssigneeSuggestionResponse } from '../types/jev.types'
import {
  formatConfidencePercent,
  getAdviceTagColor,
  isDirectAssign,
  isManualRequired,
  isOfferChoices,
} from '../utils/confidenceHelpers'

interface AssigneeSuggestionProps {
  boardId: string
  taskId: string
  currentAssigneeId?: string | null
  onSelectAssignee: (userId: string) => void
}

/**
 * Smart Assignee Dispatching panel powered by Jev System 1 (Giai đoạn 14.7 §A4).
 *
 * ⚠️ **ISSUE-03 — written in PURE Ant Design on purpose.**
 *
 * Replaced the previous Tailwind utility classes (`space-y-2`, `inline-flex`, `bg-indigo-500/10`...)
 * which caused text run-together defects on production (`Đánh giá phân côngCần phân công thủ công`, etc.)
 * because TeamNexus does not have Tailwind CSS compiled or configured.
 */
export const AssigneeSuggestion: React.FC<AssigneeSuggestionProps> = ({
  boardId,
  taskId,
  currentAssigneeId,
  onSelectAssignee,
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [data, setData] = useState<AssigneeSuggestionResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleFetchSuggestions = async () => {
    if (isOpen && data) {
      setIsOpen(false)
      return
    }

    setIsOpen(true)
    setIsLoading(true)
    setError(null)

    try {
      const res = await jevApi.suggestAssignees(boardId, taskId)
      setData(res)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể lấy gợi ý người làm.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div data-testid="assignee-suggestion">
      <Button
        size="small"
        icon={<ThunderboltOutlined style={{ color: '#4f46e5' }} />}
        onClick={handleFetchSuggestions}
        style={{
          borderRadius: 6,
          borderColor: '#c7d2fe',
          backgroundColor: '#eef2ff',
          color: '#4338ca',
          fontWeight: 500,
          fontSize: 12,
        }}
      >
        <span>Gợi ý người làm (Jev)</span>
        {isOpen ? (
          <UpOutlined style={{ fontSize: 10, marginLeft: 4 }} />
        ) : (
          <DownOutlined style={{ fontSize: 10, marginLeft: 4 }} />
        )}
      </Button>

      {isOpen && (
        <Card
          size="small"
          style={{
            marginTop: 8,
            borderRadius: 8,
            borderColor: '#e2e8f0',
            backgroundColor: '#ffffff',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.05)',
          }}
          styles={{ body: { padding: 12 } }}
        >
          {isLoading && (
            <Flex align="center" justify="center" gap={8} style={{ padding: '12px 0' }}>
              <LoadingOutlined style={{ fontSize: 16, color: '#1677ff' }} />
              <Typography.Text style={{ fontSize: 12, color: '#1d39c4' }}>
                Jev đang đối soát hồ sơ kỹ năng và tải việc...
              </Typography.Text>
            </Flex>
          )}

          {error && !isLoading && (
            <Alert
              type="error"
              showIcon
              icon={<WarningOutlined />}
              title={error}
              style={{ borderRadius: 6 }}
            />
          )}

          {data && !isLoading && (
            <Flex vertical gap={10}>
              {/* Header: Status, Advice Tag & Latency */}
              <Flex
                align="center"
                justify="space-between"
                gap={8}
                style={{ borderBottom: '1px solid #f1f5f9', paddingBottom: 8 }}
              >
                <Flex align="center" gap={8}>
                  <Typography.Text strong style={{ fontSize: 12.5 }}>
                    Đánh giá phân công
                  </Typography.Text>
                  <Tag
                    color={getAdviceTagColor(data.routingAdvice)}
                    style={{ margin: 0, fontSize: 11 }}
                  >
                    {isDirectAssign(data.routingAdvice)
                      ? `Tự tin ${formatConfidencePercent(data.confidenceScore)}`
                      : isOfferChoices(data.routingAdvice)
                      ? `Lựa chọn (${formatConfidencePercent(data.confidenceScore)})`
                      : 'Cần phân công thủ công'}
                  </Tag>
                </Flex>

                {data.latencyMs > 0 && (
                  <Space size={4}>
                    <ClockCircleOutlined style={{ fontSize: 11, color: '#8c8c8c' }} />
                    <Typography.Text
                      type="secondary"
                      style={{ fontSize: 11, fontFamily: 'monospace' }}
                    >
                      {data.latencyMs}ms
                    </Typography.Text>
                  </Space>
                )}
              </Flex>

              {data.degraded && (
                <Alert
                  type="warning"
                  showIcon
                  icon={<WarningOutlined />}
                  title="Dịch vụ AI tạm không khả dụng. Bạn hãy phân công thủ công."
                  style={{ borderRadius: 6 }}
                />
              )}

              {/* Nhánh 1: DirectAssign (Tự tin cao >= 85%) */}
              {isDirectAssign(data.routingAdvice) && data.recommendedUserId && (
                <Flex vertical gap={8}>
                  {data.candidates
                    .filter((c) => c.userId === data.recommendedUserId)
                    .map((candidate) => (
                      <Card
                        key={candidate.userId}
                        size="small"
                        style={{
                          borderRadius: 6,
                          borderColor: '#b7eb8f',
                          backgroundColor: '#f6ffed',
                        }}
                        styles={{ body: { padding: '10px 12px' } }}
                      >
                        <Flex align="center" justify="space-between" gap={8}>
                          <Flex vertical gap={2} style={{ minWidth: 0, flex: 1 }}>
                            <Flex align="center" gap={6}>
                              <Typography.Text
                                strong
                                style={{ fontSize: 12.5, color: '#237804' }}
                              >
                                {candidate.displayName}
                              </Typography.Text>
                              <Tag color="success" style={{ margin: 0, fontSize: 10.5 }}>
                                {Math.round(candidate.probability * 100)}% phù hợp
                              </Tag>
                            </Flex>
                            {candidate.reason && (
                              <Typography.Text
                                type="secondary"
                                style={{ fontSize: 11 }}
                              >
                                {candidate.reason}
                              </Typography.Text>
                            )}
                          </Flex>

                          <Button
                            type="primary"
                            size="small"
                            icon={<UserOutlined />}
                            onClick={() => {
                              onSelectAssignee(candidate.userId)
                              setIsOpen(false)
                            }}
                            disabled={candidate.userId === currentAssigneeId}
                            style={{ borderRadius: 6 }}
                          >
                            {candidate.userId === currentAssigneeId ? 'Đang gán' : 'Gán ngay'}
                          </Button>
                        </Flex>
                      </Card>
                    ))}
                </Flex>
              )}

              {/* Nhánh 2: OfferChoices (50% <= Tự tin < 85%) */}
              {isOfferChoices(data.routingAdvice) && data.candidates.length > 0 && (
                <Flex vertical gap={6}>
                  <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                    Jev đề xuất danh sách ứng viên phù hợp kỹ năng và khối lượng việc:
                  </Typography.Text>
                  <Flex vertical gap={6}>
                    {data.candidates.map((cand) => (
                      <Card
                        key={cand.userId}
                        size="small"
                        style={{
                          borderRadius: 6,
                          borderColor: '#e2e8f0',
                          backgroundColor: '#fafafa',
                        }}
                        styles={{ body: { padding: '8px 10px' } }}
                      >
                        <Flex align="center" justify="space-between" gap={8}>
                          <Flex vertical gap={2} style={{ minWidth: 0, flex: 1 }}>
                            <Flex align="center" gap={6}>
                              <Typography.Text strong style={{ fontSize: 12 }}>
                                {cand.displayName}
                              </Typography.Text>
                              <Tag color="processing" style={{ margin: 0, fontSize: 10.5 }}>
                                {Math.round(cand.probability * 100)}%
                              </Tag>
                            </Flex>
                            {cand.reason && (
                              <Space size={4}>
                                <InfoCircleOutlined
                                  style={{ fontSize: 11, color: '#8c8c8c' }}
                                />
                                <Typography.Text
                                  type="secondary"
                                  style={{ fontSize: 11 }}
                                >
                                  {cand.reason}
                                </Typography.Text>
                              </Space>
                            )}
                          </Flex>

                          <Button
                            size="small"
                            onClick={() => {
                              onSelectAssignee(cand.userId)
                              setIsOpen(false)
                            }}
                            disabled={cand.userId === currentAssigneeId}
                            style={{ borderRadius: 6 }}
                          >
                            {cand.userId === currentAssigneeId ? 'Đang gán' : 'Chọn'}
                          </Button>
                        </Flex>
                      </Card>
                    ))}
                  </Flex>
                </Flex>
              )}

              {/* Nhánh 3: ManualRequired (Tự tin < 50%) */}
              {isManualRequired(data.routingAdvice) && (
                <Alert
                  type="info"
                  showIcon
                  title="Chưa có ứng viên đạt độ tương thích cao. Vui lòng tự chọn thành viên từ danh sách."
                  style={{ borderRadius: 6 }}
                />
              )}
            </Flex>
          )}
        </Card>
      )}
    </div>
  )
}
