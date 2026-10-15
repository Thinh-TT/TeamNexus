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
 * ⚠️ **ISSUE-02 — this component is written in PURE Ant Design on purpose.**
 *
 * The first version used Tailwind utility classes (`grid grid-cols-3 gap-2`, `space-y-3`, `bg-slate-900/60`,
 * `bg-emerald-600`, …). TeamNexus has **no Tailwind**: no `tailwind.config.*`, no PostCSS pipeline, and no
 * Tailwind dependency in `package.json`. Every one of those classes resolved to nothing, so the browser fell
 * back to its defaults and the panel rendered as run-together text with an unstyled HTML button —
 * `Phân loại thông minh (Jev System 1)AI tự tin 96%`, `Miền kỹ thuậtDevOps`, and so on.
 *
 * The rule that follows is worth stating explicitly for the next component: **layout comes from `Flex`/`Space`
 * with a `gap`, colour comes from `Tag`/`Alert`/`Button` props or an explicit `style`, and typography comes
 * from `Typography`.** Never a utility class name, because nothing is generating CSS for it.
 *
 * The three branches mirror the confidence floors enforced server-side (`ConfidenceRouting`):
 * `DirectAssign` ⇒ one suggestion with a 1-click apply; `OfferChoices` ⇒ ranked options to pick from;
 * `ManualRequired` ⇒ nothing offered, deliberately.
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
      <Card size="small" style={{ borderRadius: 8, borderColor: '#d6e4ff', backgroundColor: '#f0f5ff' }}>
        <Flex align="center" gap={8}>
          <LoadingOutlined style={{ fontSize: 14, color: '#1677ff' }} />
          <Typography.Text style={{ fontSize: 12, color: '#1d39c4' }}>
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
        // `title`, not `message`: antd v6 deprecated `message` on Alert (the switch AgentChatPanel and
        // MemberSkillsForm already made), and a deprecation warning in the test output is noise that hides
        // real ones. The whole sentence stays in ONE prop — splitting it across `title` + `description` is
        // what made the original text read as run-together fragments.
        title="Gợi ý AI tạm không khả dụng — bạn có thể tiếp tục điền thủ công."
        style={{ borderRadius: 8 }}
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
      style={{ borderRadius: 8, borderColor: '#e2e8f0', backgroundColor: '#ffffff' }}
      styles={{ body: { padding: 12 } }}
    >
      <Flex vertical gap={10}>
        {/* ---- header: what this is, how sure Jev is, how long it took ---- */}
        <Flex align="center" justify="space-between" gap={8} wrap="wrap">
          <Flex align="center" gap={8}>
            <ThunderboltOutlined style={{ fontSize: 16, color: '#1677ff' }} />
            <Typography.Text strong style={{ fontSize: 13 }}>
              Phân loại thông minh (Jev System 1)
            </Typography.Text>
            <Tag color={getAdviceTagColor(routingAdvice)} style={{ margin: 0, fontSize: 11 }}>
              {isDirectAssign(routingAdvice)
                ? `AI tự tin ${confidencePct}`
                : isOfferChoices(routingAdvice)
                  ? `AI gợi ý (${confidencePct})`
                  : 'Cần bạn tự chọn'}
            </Tag>
          </Flex>

          {latencyMs > 0 && (
            <Space size={4}>
              <ClockCircleOutlined style={{ fontSize: 12, color: '#8c8c8c' }} />
              <Typography.Text type="secondary" style={{ fontSize: 11, fontFamily: 'monospace' }}>
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
            style={{ borderRadius: 6 }}
          />
        )}

        {/* ---- branch 1: DirectAssign (confidence >= 0.85) ---- */}
        {isDirectAssign(routingAdvice) && (
          <Flex vertical gap={10}>
            <Descriptions
              size="small"
              bordered
              column={3}
              items={[
                {
                  key: 'domain',
                  label: 'Miền kỹ thuật',
                  children: suggestedDomain || 'Chung',
                },
                {
                  key: 'workType',
                  label: 'Loại hình',
                  children: suggestedWorkType || 'Feature',
                },
                {
                  key: 'priority',
                  label: 'Mức ưu tiên',
                  children: suggestedPriority || 'Medium',
                },
              ]}
            />

            {onApplyDirect && (
              <Button
                type="primary"
                block
                icon={<CheckCircleOutlined />}
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
            <Typography.Text style={{ fontSize: 12 }}>
              Jev đề xuất các lựa chọn tiềm năng nhất. Nhấp để chọn:
            </Typography.Text>

            {/* `Space wrap` rather than a flex-wrap utility class: it produces the same wrapping with no CSS. */}
            <Space size={[6, 6]} wrap>
              {options.map((opt, idx) => {
                const probPct = Math.round(opt.probability * 100)
                const label = `${fieldLabel(opt.field)}: ${opt.value}`

                return (
                  <Tooltip key={`${opt.field}-${opt.value}-${idx}`} title={label}>
                    <Button
                      size="small"
                      // The accessible name has to stay a single string — the ISSUE-02 symptom was exactly
                      // this label being glued to its value by missing layout CSS.
                      aria-label={label}
                      onClick={() => onSelectOption?.(opt)}
                    >
                      <Space size={4}>
                        <Typography.Text type="secondary" style={{ fontSize: 10 }}>
                          {fieldLabel(opt.field)}
                        </Typography.Text>
                        <Typography.Text strong style={{ fontSize: 12 }}>
                          {opt.value}
                        </Typography.Text>
                        {probPct > 0 && (
                          <Tag color="blue" style={{ margin: 0, fontSize: 10 }}>
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
