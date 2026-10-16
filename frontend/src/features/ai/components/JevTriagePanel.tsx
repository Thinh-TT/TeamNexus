import React from 'react'
import { Alert, Button, Card, Descriptions, Flex, Space, Tag, Tooltip, Typography } from 'antd'
import {
  ThunderboltOutlined,
  CheckCircleOutlined,
  WarningOutlined,
  QuestionCircleOutlined,
  SafetyCertificateOutlined,
  LoadingOutlined,
  ClockCircleOutlined,
} from '@ant-design/icons'
import type { TaskTriageResponse, TriageOption } from '../types/jev.types'
import {
  formatConfidencePercent,
  getAdviceIconColor,
  getAdviceTagColor,
  isDirectAssign,
  isManualRequired,
  isOfferChoices,
} from '../utils/confidenceHelpers'

interface JevTriagePanelProps {
  triageData: TaskTriageResponse | null
  isLoading: boolean
  isDegraded: boolean
  onApplyDirect?: (suggestions: {
    domain?: string | null
    workType?: string | null
    priority?: string | null
  }) => void
  onSelectOption?: (option: TriageOption) => void
}

/** Human label for one suggestion field. */
const FIELD_LABELS: Record<string, string> = {
  domain: 'Miền kỹ thuật',
  workType: 'Loại hình',
  priority: 'Mức ưu tiên',
}

const fieldLabel = (field: string): string => FIELD_LABELS[field] ?? field

/**
 * Jev System 1 triage suggestions for a task (Giai đoạn 14.7 §A3).
 *
 * Styled with the Luminous Enterprise Workspace AI surface (Electric Violet `#7c3aed` / `#f5f3ff`)
 * using pure Ant Design components and explicit inline styles (no Tailwind utility classes).
 */
export const JevTriagePanel: React.FC<JevTriagePanelProps> = ({
  triageData,
  isLoading,
  isDegraded,
  onApplyDirect,
  onSelectOption,
}) => {
  if (isLoading) {
    return (
      <Card
        size="small"
        style={{
          borderRadius: 10,
          borderColor: '#ddd6fe',
          background: 'linear-gradient(135deg, #f5f3ff 0%, #eef2ff 100%)',
        }}
      >
        <Flex align="center" gap={8}>
          <LoadingOutlined style={{ fontSize: 14, color: '#7c3aed' }} />
          <Typography.Text style={{ fontSize: 12.5, color: '#5b21b6', fontWeight: 500 }}>
            Jev đang phân tích và triage thẻ...
          </Typography.Text>
        </Flex>
      </Card>
    )
  }

  if (isDegraded || triageData?.degraded) {
    return (
      <Alert
        type="warning"
        showIcon
        icon={<WarningOutlined />}
        title="Gợi ý AI tạm không khả dụng — bạn có thể tiếp tục điền thủ công."
        style={{ borderRadius: 10 }}
      />
    )
  }

  if (!triageData) {
    return null
  }

  const {
    suggestedDomain,
    suggestedWorkType,
    suggestedPriority,
    needsSecurityReview,
    confidenceScore,
    routingAdvice,
    options,
    latencyMs,
  } = triageData

  const confidencePct = formatConfidencePercent(confidenceScore)

  return (
    <Card
      data-testid="jev-triage-panel"
      size="small"
      style={{
        borderRadius: 10,
        borderColor: '#ddd6fe',
        background: 'linear-gradient(180deg, #faf5ff 0%, #ffffff 100%)',
        boxShadow: '0 0 0 1px rgba(124, 58, 237, 0.06), 0 4px 12px rgba(124, 58, 237, 0.05)',
      }}
      styles={{ body: { padding: 14 } }}
    >
      <Flex vertical gap={12}>
        {/* ---- header: what this is, how sure Jev is, how long it took ---- */}
        <Flex align="center" justify="space-between" gap={8} wrap="wrap">
          <Flex align="center" gap={8} wrap="wrap">
            <div
              style={{
                width: 24,
                height: 24,
                borderRadius: 6,
                backgroundColor: '#7c3aed',
                color: '#ffffff',
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <ThunderboltOutlined style={{ fontSize: 13 }} />
            </div>
            <Typography.Text strong style={{ fontSize: 13, color: '#0f172a' }}>
              Phân loại thông minh (Jev System 1)
            </Typography.Text>
            <Tag
              color={getAdviceTagColor(routingAdvice)}
              style={{ margin: 0, fontSize: 11, fontWeight: 600, borderRadius: 9999, padding: '0 8px' }}
            >
              {isDirectAssign(routingAdvice)
                ? `AI tự tin ${confidencePct}`
                : isOfferChoices(routingAdvice)
                  ? `AI gợi ý (${confidencePct})`
                  : 'Cần bạn tự chọn'}
            </Tag>
          </Flex>

          {latencyMs > 0 && (
            <Space
              size={4}
              style={{
                padding: '1px 7px',
                borderRadius: 6,
                backgroundColor: '#f8fafc',
                border: '1px solid #e2e8f0',
              }}
            >
              <ClockCircleOutlined style={{ fontSize: 11, color: '#64748b' }} />
              <Typography.Text
                type="secondary"
                style={{ fontSize: 11, fontFamily: 'var(--tn-font-mono, monospace)', fontWeight: 500 }}
              >
                {latencyMs}ms
              </Typography.Text>
            </Space>
          )}
        </Flex>

        {needsSecurityReview && (
          <Alert
            type="error"
            showIcon
            icon={<SafetyCertificateOutlined />}
            title="Thẻ này có yếu tố bảo mật/dữ liệu nhạy cảm cần được rà soát."
            style={{ borderRadius: 8 }}
          />
        )}

        {/* ---- branch 1: DirectAssign (confidence >= 0.85) ---- */}
        {isDirectAssign(routingAdvice) && (
          <Flex vertical gap={10}>
            <Descriptions
              size="small"
              bordered
              column={3}
              style={{ backgroundColor: '#ffffff', borderRadius: 8, overflow: 'hidden' }}
              items={[
                {
                  key: 'domain',
                  label: 'Miền kỹ thuật',
                  children: (
                    <Typography.Text strong style={{ color: '#4f46e5', fontSize: 12.5 }}>
                      {suggestedDomain || 'Chung'}
                    </Typography.Text>
                  ),
                },
                {
                  key: 'workType',
                  label: 'Loại hình',
                  children: (
                    <Typography.Text strong style={{ color: '#7c3aed', fontSize: 12.5 }}>
                      {suggestedWorkType || 'Feature'}
                    </Typography.Text>
                  ),
                },
                {
                  key: 'priority',
                  label: 'Mức ưu tiên',
                  children: (
                    <Typography.Text strong style={{ color: '#d97706', fontSize: 12.5 }}>
                      {suggestedPriority || 'Medium'}
                    </Typography.Text>
                  ),
                },
              ]}
            />

            {onApplyDirect && (
              <Button
                type="primary"
                block
                icon={<CheckCircleOutlined />}
                style={{
                  backgroundColor: '#7c3aed',
                  borderColor: '#7c3aed',
                  borderRadius: 8,
                  fontWeight: 600,
                  boxShadow: '0 2px 6px rgba(124, 58, 237, 0.25)',
                }}
                onClick={() =>
                  onApplyDirect({
                    domain: suggestedDomain,
                    workType: suggestedWorkType,
                    priority: suggestedPriority,
                  })
                }
              >
                Áp dụng toàn bộ gợi ý
              </Button>
            )}
          </Flex>
        )}

        {/* ---- branch 2: OfferChoices (0.50 <= confidence < 0.85) ---- */}
        {isOfferChoices(routingAdvice) && (
          <Flex vertical gap={8}>
            <Typography.Text style={{ fontSize: 12, color: '#475569' }}>
              Jev đề xuất các lựa chọn tiềm năng nhất. Nhấp để chọn:
            </Typography.Text>

            <Space size={[6, 6]} wrap>
              {options.map((opt, idx) => {
                const probPct = Math.round(opt.probability * 100)
                const label = `${fieldLabel(opt.field)}: ${opt.value}`

                return (
                  <Tooltip key={`${opt.field}-${opt.value}-${idx}`} title={label}>
                    <Button
                      size="small"
                      aria-label={label}
                      style={{
                        borderRadius: 6,
                        borderColor: '#ddd6fe',
                        backgroundColor: '#ffffff',
                      }}
                      onClick={() => onSelectOption?.(opt)}
                    >
                      <Space size={4}>
                        <Typography.Text type="secondary" style={{ fontSize: 10.5 }}>
                          {fieldLabel(opt.field)}
                        </Typography.Text>
                        <Typography.Text strong style={{ fontSize: 12, color: '#0f172a' }}>
                          {opt.value}
                        </Typography.Text>
                        {probPct > 0 && (
                          <Tag color="purple" style={{ margin: 0, fontSize: 10 }}>
                            {probPct}%
                          </Tag>
                        )}
                      </Space>
                    </Button>
                  </Tooltip>
                )
              })}
            </Space>
          </Flex>
        )}

        {/* ---- branch 3: ManualRequired (confidence < 0.50) ---- */}
        {isManualRequired(routingAdvice) && (
          <Flex align="flex-start" gap={8}>
            <QuestionCircleOutlined style={{ fontSize: 16, color: getAdviceIconColor(routingAdvice) }} />
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>
              Thông tin chưa đủ để Jev tự tin phân loại. Vui lòng tự chọn miền kỹ thuật và mức ưu tiên bên dưới.
            </Typography.Text>
          </Flex>
        )}
      </Flex>
    </Card>
  )
}
