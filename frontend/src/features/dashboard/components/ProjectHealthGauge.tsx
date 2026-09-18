import React from 'react'
import { Card, Empty, Flex, Progress, Space, Statistic, Tooltip, Typography } from 'antd'
import { InfoCircleOutlined } from '@ant-design/icons'
import type { DashboardProjectHealth } from '../types/dashboard.types'

const { Text } = Typography

interface ProjectHealthGaugeProps {
  health?: DashboardProjectHealth | null
}

const BAND_COLORS: Record<string, string> = {
  'Tốt': '#10b981',
  'Cần chú ý': '#f59e0b',
  'Rủi ro': '#f97316',
  'Nghiêm trọng': '#ef4444',
}

const COMPONENT_LABELS: Record<string, string> = {
  overdue: 'Quá hạn',
  atRisk: 'Sắp hết hạn',
  stalled: 'Đứng yên',
  aging: 'Tồn lâu',
  load: 'Lệch tải',
}

export const ProjectHealthGauge: React.FC<ProjectHealthGaugeProps> = ({ health }) => {
  if (!health) {
    return (
      <Card
        data-testid="project-health-gauge"
        title="Sức khỏe dự án"
        style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}
        styles={{ body: { display: 'flex', alignItems: 'center', justifyContent: 'center', minHeight: 180 } }}
      >
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={
            <Flex vertical align="center" gap={2}>
              <Text type="secondary">Chưa đủ dữ liệu để tính sức khỏe dự án</Text>
              <Text type="secondary" style={{ fontSize: 11 }}>
                Hệ thống cần thêm dữ liệu công việc để đánh giá
              </Text>
            </Flex>
          }
        />
      </Card>
    )
  }

  const strokeColor = BAND_COLORS[health.band] || '#6366f1'
  const validScore = Number.isFinite(health.score) ? Math.max(0, Math.min(100, Math.round(health.score))) : 0

  const componentEntries = Object.entries(health.components || {})

  const tooltipContent = (
    <div style={{ maxWidth: 280, fontSize: 12 }}>
      <div style={{ fontWeight: 600, marginBottom: 4 }}>Chi tiết điểm trừ sức khỏe:</div>
      {componentEntries.length > 0 ? (
        <ul style={{ paddingLeft: 16, margin: '4px 0' }}>
          {componentEntries.map(([key, val]) => (
            <li key={key}>
              {COMPONENT_LABELS[key] || key}: <strong>-{val ?? 0} điểm</strong>
            </li>
          ))}
        </ul>
      ) : (
        <div style={{ fontStyle: 'italic', marginBottom: 4 }}>Không có khoản trừ nào</div>
      )}

      {health.reasons && health.reasons.length > 0 && (
        <>
          <div style={{ fontWeight: 600, marginTop: 6, marginBottom: 2 }}>Lý do chính:</div>
          <ul style={{ paddingLeft: 16, margin: '4px 0' }}>
            {health.reasons.map((r, idx) => (
              <li key={idx}>{r}</li>
            ))}
          </ul>
        </>
      )}
    </div>
  )

  return (
    <Card
      data-testid="project-health-gauge"
      title={
        <Space size={6}>
          <span>Sức khỏe dự án</span>
          <Tooltip title={tooltipContent}>
            <InfoCircleOutlined style={{ color: '#94a3b8', cursor: 'pointer', fontSize: 13 }} />
          </Tooltip>
        </Space>
      }
      style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}
    >
      <Flex vertical align="center" justify="center" gap={8} style={{ padding: '8px 0' }}>
        <Progress
          type="dashboard"
          percent={validScore}
          strokeColor={strokeColor}
          size={120}
          format={(percent) => `${percent}`}
        />
        <Statistic
          title="Tình trạng"
          value={health.band}
          styles={{ content: { color: strokeColor, fontWeight: 600, fontSize: 16 } }}
        />
      </Flex>
    </Card>
  )
}
