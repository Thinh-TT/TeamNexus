import React, { useMemo } from 'react'
import { Alert, Card, Empty, Flex, Tooltip, Typography } from 'antd'
import type { ReportProgressSeriesResponse } from '../types/reporting.types'
import {
  MAX_CHART_BUCKETS,
  barHeightPercent,
  idealOpenSeries,
  seriesBuckets,
  seriesMax,
  visibleBuckets,
} from '../utils/burndown'

export interface BurndownChartProps {
  series: ReportProgressSeriesResponse | null
  loading?: boolean
}

/** Chiều cao vùng vẽ (px) — cột được tính theo % của vùng này. */
const PLOT_HEIGHT = 180

/**
 * Biểu đồ Burndown: số thẻ **hoàn thành** so với số thẻ **còn mở** theo từng mốc thời gian.
 *
 * <para>
 * <b>Không dùng thư viện biểu đồ.</b> Hai chuỗi số nguyên trên một trục thời gian là thứ `div` + CSS
 * làm được, và ràng buộc của giai đoạn là không thêm package npm. Đổi lại phải tự lo ba thứ mà thư viện
 * thường lo: chia 0 khi board rỗng (`barHeightPercent` trả 0), cắt bớt cột khi cửa sổ quá dài, và nói rõ
 * khi đã cắt.
 * </para>
 *
 * <para>
 * <b>Đường "lý tưởng"</b> = `openTasks − completions` tích luỹ, kẹp tại 0. Nó là hình minh hoạ, không
 * phải số liệu — nên nó mờ và không có tooltip.
 * </para>
 */
export const BurndownChart: React.FC<BurndownChartProps> = ({ series, loading = false }) => {
  const buckets = useMemo(() => (series ? seriesBuckets(series) : []), [series])
  const { buckets: visible, hiddenCount } = useMemo(
    () => visibleBuckets(buckets, MAX_CHART_BUCKETS),
    [buckets]
  )

  const max = useMemo(
    () => seriesMax(visible.flatMap((bucket) => [bucket.openTasks, bucket.completions])),
    [visible]
  )

  const ideal = useMemo(() => {
    if (visible.length === 0) return []
    // Điểm xuất phát là số thẻ mở ở bucket đầu tiên cộng lại phần đã hoàn thành trong chính bucket đó —
    // nếu không, đường lý tưởng sẽ luôn thấp hơn thực tế đúng bằng số thẻ đóng ở cột đầu.
    const first = visible[0]
    return idealOpenSeries(first.openTasks + first.completions, visible)
  }, [visible])

  if (!series || buckets.length === 0) {
    return (
      <Card
        title="Biểu đồ tiến độ (Burndown)"
        style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
        data-testid="burndown-chart"
      >
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={loading ? 'Đang tải biểu đồ...' : 'Chưa có dữ liệu để vẽ biểu đồ tiến độ.'}
        />
      </Card>
    )
  }

  return (
    <Card
      title="Biểu đồ tiến độ (Burndown)"
      style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      data-testid="burndown-chart"
      extra={
        <Flex gap={12} align="center">
          <LegendDot color="#6366f1" label="Hoàn thành" />
          <LegendDot color="#cbd5e1" label="Còn mở" />
          <LegendDot color="#a5b4fc" label="Lý tưởng" dashed />
        </Flex>
      }
    >
      {series.truncated?.bucketCapReached && (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 12 }}
          message={`Khoảng thời gian quá dài nên chỉ hiển thị ${series.truncated.maxBuckets} mốc gần nhất.`}
        />
      )}

      {hiddenCount > 0 && (
        <Alert
          type="info"
          showIcon
          style={{ marginBottom: 12 }}
          message={`Biểu đồ chỉ vẽ ${MAX_CHART_BUCKETS} mốc gần nhất; ${hiddenCount} mốc trước đó không hiển thị.`}
        />
      )}

      <Flex align="flex-end" gap={4} style={{ height: PLOT_HEIGHT, paddingTop: 8 }}>
        {visible.map((bucket, index) => {
          const completionHeight = barHeightPercent(bucket.completions, max)
          const openHeight = barHeightPercent(bucket.openTasks, max)
          const idealHeight = barHeightPercent(ideal[index] ?? 0, max)

          return (
            <Tooltip
              key={bucket.key}
              title={
                <div style={{ fontSize: 12 }}>
                  <div>
                    <strong>{bucket.label}</strong>
                  </div>
                  <div>Hoàn thành: {bucket.completions}</div>
                  <div>Còn mở: {bucket.openTasks}</div>
                  <div>Tạo mới: {bucket.creations}</div>
                  <div>Lý tưởng: {ideal[index] ?? 0}</div>
                </div>
              }
            >
              <Flex
                vertical
                justify="flex-end"
                align="center"
                style={{ flex: '1 1 0', height: '100%', position: 'relative' }}
                data-testid={`burndown-bucket-${bucket.key}`}
              >
                {/* Đường lý tưởng: cột mảnh, mờ, không tương tác. */}
                <div
                  style={{
                    position: 'absolute',
                    bottom: 0,
                    width: 3,
                    height: `${idealHeight}%`,
                    backgroundColor: '#a5b4fc',
                    borderRadius: 2,
                  }}
                />
                <Flex
                  align="flex-end"
                  gap={1}
                  style={{ height: '100%', width: '100%', justifyContent: 'center' }}
                >
                  <div
                    style={{
                      width: '38%',
                      height: `${completionHeight}%`,
                      backgroundColor: '#6366f1',
                      borderRadius: '3px 3px 0 0',
                    }}
                    data-testid={`burndown-completions-${bucket.key}`}
                  />
                  <div
                    style={{
                      width: '38%',
                      height: `${openHeight}%`,
                      backgroundColor: '#cbd5e1',
                      borderRadius: '3px 3px 0 0',
                    }}
                    data-testid={`burndown-open-${bucket.key}`}
                  />
                </Flex>
              </Flex>
            </Tooltip>
          )
        })}
      </Flex>

      {/* Nhãn trục hoành: chỉ hiện thưa để không chồng chữ khi có 31 cột. */}
      <Flex gap={4} style={{ marginTop: 6 }}>
        {visible.map((bucket, index) => {
          const step = Math.ceil(visible.length / 10)
          const show = index % step === 0

          return (
            <Typography.Text
              key={`label-${bucket.key}`}
              type="secondary"
              style={{
                flex: '1 1 0',
                fontSize: 10,
                textAlign: 'center',
                whiteSpace: 'nowrap',
                overflow: 'hidden',
                visibility: show ? 'visible' : 'hidden',
              }}
            >
              {bucket.label}
            </Typography.Text>
          )
        })}
      </Flex>

      <Typography.Text type="secondary" style={{ fontSize: 12, marginTop: 8 }}>
        Phạm vi: {series.scope.type === 'board' ? series.scope.boardName ?? 'một bảng' : 'toàn workspace'} ·
        múi giờ UTC{series.tzOffsetMinutes >= 0 ? '+' : ''}
        {series.tzOffsetMinutes / 60} · mốc theo {series.bucketDays === 7 ? 'tuần' : 'ngày'}
      </Typography.Text>
    </Card>
  )
}

const LegendDot: React.FC<{ color: string; label: string; dashed?: boolean }> = ({
  color,
  label,
  dashed = false,
}) => (
  <Flex align="center" gap={4}>
    <span
      style={{
        display: 'inline-block',
        width: dashed ? 3 : 10,
        height: 10,
        borderRadius: dashed ? 2 : '50%',
        backgroundColor: color,
      }}
    />
    <Typography.Text type="secondary" style={{ fontSize: 12 }}>
      {label}
    </Typography.Text>
  </Flex>
)
