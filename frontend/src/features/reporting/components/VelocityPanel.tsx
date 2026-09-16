import React from 'react'
import { Card, Col, Flex, Row, Statistic, Tooltip } from 'antd'
import { CheckCircleOutlined, FieldTimeOutlined, UnorderedListOutlined } from '@ant-design/icons'
import type { ReportProgressSeriesResponse } from '../types/reporting.types'
import { velocityStats } from '../utils/burndown'

export interface VelocityPanelProps {
  series: ReportProgressSeriesResponse | null
}

/**
 * Ba chỉ số năng suất suy ra từ chuỗi thời gian (Giai đoạn 13 §4).
 *
 * <para>
 * Lấy **nguyên** `velocity` từ server chứ không tính lại từ mảng bucket đã hiển thị: biểu đồ có thể đã
 * cắt còn 31 cột, còn `avgCompletionsPerWeek` được tính trên **toàn bộ** cửa sổ. Tự tính ở client sẽ cho
 * một con số khác `/reports/summary` cho cùng workspace — đúng loại lỗi khiến người dùng mất tin vào
 * báo cáo.
 * </para>
 */
export const VelocityPanel: React.FC<VelocityPanelProps> = ({ series }) => {
  const stats = velocityStats(
    series ?? {
      velocity: { avgCompletionsPerWeek: 0, completedInRange: 0, openAtEnd: 0 },
    } as ReportProgressSeriesResponse
  )

  return (
    <Card
      title="Năng suất & Tốc độ"
      style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      data-testid="velocity-panel"
    >
      <Row gutter={[16, 16]}>
        <Col xs={24} sm={8}>
          <Tooltip title="Trung bình số thẻ hoàn thành mỗi tuần trong khoảng thời gian đang xem.">
            <Statistic
              title="Năng suất trung bình / tuần"
              value={stats.avgCompletionsPerWeek}
              precision={1}
              prefix={<FieldTimeOutlined style={{ color: '#6366f1' }} />}
              data-testid="velocity-avg"
            />
          </Tooltip>
        </Col>

        <Col xs={24} sm={8}>
          <Tooltip title="Tổng số thẻ có completed_at rơi vào khoảng thời gian đang xem.">
            <Statistic
              title="Hoàn thành trong kỳ"
              value={stats.completedInRange}
              prefix={<CheckCircleOutlined style={{ color: '#10b981' }} />}
              valueStyle={{ color: '#10b981' }}
              data-testid="velocity-completed"
            />
          </Tooltip>
        </Col>

        <Col xs={24} sm={8}>
          <Tooltip title="Số thẻ còn mở ở cuối khoảng thời gian đang xem.">
            <Statistic
              title="Còn mở ở cuối kỳ"
              value={stats.openAtEnd}
              prefix={<UnorderedListOutlined style={{ color: '#f59e0b' }} />}
              valueStyle={{ color: '#f59e0b' }}
              data-testid="velocity-open"
            />
          </Tooltip>
        </Col>
      </Row>

      {series?.truncated?.bucketCapReached && (
        <Flex style={{ marginTop: 12 }}>
          <span style={{ fontSize: 12, color: '#8c8c8c' }}>
            Chỉ số được tính trên toàn bộ khoảng thời gian, kể cả phần không hiển thị trên biểu đồ.
          </span>
        </Flex>
      )}
    </Card>
  )
}
