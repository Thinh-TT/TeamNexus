import React from 'react'
import {
  CheckCircleOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  InfoCircleOutlined,
  ProfileOutlined,
  RadarChartOutlined,
  ThunderboltOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Card,
  Col,
  Empty,
  Flex,
  Progress,
  Row,
  Statistic,
  Tabs,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import type {
  ReportAssigneeRowResponse,
  ReportBoardRowResponse,
  ReportSummaryResponse,
} from '../types/reporting.types'
import { formatCount, formatDurationHours, formatPercent } from '../utils/reportFormat'
import { ReportTable } from './ReportTable'

export interface ReportSummaryPanelProps {
  report: ReportSummaryResponse
}

export const ReportSummaryPanel: React.FC<ReportSummaryPanelProps> = ({ report }) => {
  const {
    progress,
    performance,
    byBoard,
    byAssignee,
    activity,
    health,
    truncated,
    metricDefinitions,
  } = report

  const boardColumns: ColumnsType<ReportBoardRowResponse> = [
    {
      title: 'Tên Bảng',
      dataIndex: 'boardName',
      key: 'boardName',
      render: (name: string) => <span style={{ fontWeight: 600 }}>{name}</span>,
    },
    {
      title: 'Tổng Task',
      dataIndex: 'total',
      key: 'total',
      align: 'right',
      sorter: (a, b) => a.total - b.total,
      render: (v: number) => formatCount(v),
    },
    {
      title: 'Hoàn thành',
      dataIndex: 'done',
      key: 'done',
      align: 'right',
      sorter: (a, b) => a.done - b.done,
      render: (v: number) => <Tag color="success">{formatCount(v)}</Tag>,
    },
    {
      title: 'Đang mở',
      dataIndex: 'open',
      key: 'open',
      align: 'right',
      sorter: (a, b) => a.open - b.open,
      render: (v: number) => <Tag color="processing">{formatCount(v)}</Tag>,
    },
    {
      title: 'Quá hạn',
      dataIndex: 'overdue',
      key: 'overdue',
      align: 'right',
      sorter: (a, b) => a.overdue - b.overdue,
      render: (v: number) =>
        v > 0 ? <Tag color="error">{formatCount(v)}</Tag> : <Tag color="default">0</Tag>,
    },
    {
      title: 'Xong trong kỳ',
      dataIndex: 'completedInRange',
      key: 'completedInRange',
      align: 'right',
      sorter: (a, b) => a.completedInRange - b.completedInRange,
      render: (v: number) => formatCount(v),
    },
    {
      title: 'Thời gian hoàn thành TB',
      dataIndex: 'avgCompletionHours',
      key: 'avgCompletionHours',
      align: 'right',
      render: (v: number | null) => formatDurationHours(v),
    },
    {
      title: 'Tỉ lệ đúng hạn',
      dataIndex: 'onTimeRate',
      key: 'onTimeRate',
      align: 'right',
      render: (v: number | null) => formatPercent(v),
    },
  ]

  const assigneeColumns: ColumnsType<ReportAssigneeRowResponse> = [
    {
      title: 'Người phụ trách',
      dataIndex: 'assigneeName',
      key: 'assigneeName',
      render: (name: string, record) => (
        <span style={{ fontWeight: record.assigneeId ? 500 : 400, color: record.assigneeId ? '#1e293b' : '#64748b' }}>
          {name}
        </span>
      ),
    },
    {
      title: 'Tổng Task',
      dataIndex: 'total',
      key: 'total',
      align: 'right',
      sorter: (a, b) => a.total - b.total,
      render: (v: number) => formatCount(v),
    },
    {
      title: 'Hoàn thành',
      dataIndex: 'done',
      key: 'done',
      align: 'right',
      sorter: (a, b) => a.done - b.done,
      render: (v: number) => <Tag color="success">{formatCount(v)}</Tag>,
    },
    {
      title: 'Đang mở',
      dataIndex: 'open',
      key: 'open',
      align: 'right',
      sorter: (a, b) => a.open - b.open,
      render: (v: number) => <Tag color="processing">{formatCount(v)}</Tag>,
    },
    {
      title: 'Quá hạn',
      dataIndex: 'overdue',
      key: 'overdue',
      align: 'right',
      sorter: (a, b) => a.overdue - b.overdue,
      render: (v: number) =>
        v > 0 ? <Tag color="error">{formatCount(v)}</Tag> : <Tag color="default">0</Tag>,
    },
    {
      title: 'Xong trong kỳ',
      dataIndex: 'completedInRange',
      key: 'completedInRange',
      align: 'right',
      sorter: (a, b) => a.completedInRange - b.completedInRange,
      render: (v: number) => formatCount(v),
    },
    {
      title: 'Thời gian hoàn thành TB',
      dataIndex: 'avgCompletionHours',
      key: 'avgCompletionHours',
      align: 'right',
      render: (v: number | null) => formatDurationHours(v),
    },
    {
      title: 'Tỉ lệ đúng hạn',
      dataIndex: 'onTimeRate',
      key: 'onTimeRate',
      align: 'right',
      render: (v: number | null) => formatPercent(v),
    },
  ]

  const getSeverityTagColor = (sev: string) => {
    switch (sev.toLowerCase()) {
      case 'critical':
      case 'high':
        return 'error'
      case 'medium':
        return 'warning'
      case 'low':
        return 'processing'
      default:
        return 'default'
    }
  }

  return (
    <Flex vertical gap="middle">
      {/* Row Cap Reached Warning Alert */}
      {truncated?.rowCapReached && (
        <Alert
          type="warning"
          showIcon
          message="Giới hạn hiển thị dòng dữ liệu"
          description={`Dữ liệu chi tiết đã đạt giới hạn tối đa (${truncated.maxRows} dòng) và được cắt bớt để bảo toàn hiệu năng hệ thống. Các chỉ số thống kê tổng hợp bên dưới vẫn phản ánh đầy đủ toàn bộ workspace.`}
        />
      )}

      {/* Progress & KPI Metrics */}
      <Row gutter={[16, 16]}>
        <Col xs={24} sm={12} md={6}>
          <Card
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
          >
            <Statistic
              title={
                <Tooltip title={metricDefinitions?.total || 'Tổng số task hiện có trong phạm vi'}>
                  <span>
                    Tổng số Task <InfoCircleOutlined style={{ fontSize: 12, color: '#94a3b8' }} />
                  </span>
                </Tooltip>
              }
              value={progress.total}
              prefix={<ProfileOutlined style={{ color: '#6366f1' }} />}
            />
          </Card>
        </Col>

        <Col xs={24} sm={12} md={6}>
          <Card
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
          >
            <Statistic
              title={
                <Tooltip title={metricDefinitions?.done || 'Số task đã hoàn thành'}>
                  <span>
                    Đã Hoàn Thành <InfoCircleOutlined style={{ fontSize: 12, color: '#94a3b8' }} />
                  </span>
                </Tooltip>
              }
              value={progress.done}
              valueStyle={{ color: '#16a34a' }}
              prefix={<CheckCircleOutlined style={{ color: '#16a34a' }} />}
            />
          </Card>
        </Col>

        <Col xs={24} sm={12} md={6}>
          <Card
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
          >
            <Statistic
              title={
                <Tooltip title={metricDefinitions?.open || 'Số task chưa hoàn thành'}>
                  <span>
                    Đang Mở <InfoCircleOutlined style={{ fontSize: 12, color: '#94a3b8' }} />
                  </span>
                </Tooltip>
              }
              value={progress.open}
              valueStyle={{ color: '#0284c7' }}
              prefix={<ClockCircleOutlined style={{ color: '#0284c7' }} />}
            />
          </Card>
        </Col>

        <Col xs={24} sm={12} md={6}>
          <Card
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
          >
            <Statistic
              title={
                <Tooltip title={metricDefinitions?.overdue || 'Số task quá hạn'}>
                  <span>
                    Quá Hạn <InfoCircleOutlined style={{ fontSize: 12, color: '#94a3b8' }} />
                  </span>
                </Tooltip>
              }
              value={progress.overdue}
              valueStyle={{ color: progress.overdue > 0 ? '#dc2626' : '#64748b' }}
              prefix={
                <ExclamationCircleOutlined
                  style={{ color: progress.overdue > 0 ? '#dc2626' : '#64748b' }}
                />
              }
            />
          </Card>
        </Col>
      </Row>

      {/* Completion Rate & Performance Row */}
      <Card
        styles={{ body: { padding: 20 } }}
        style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      >
        <Flex vertical gap="middle">
          <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
            <Typography.Text strong style={{ fontSize: 15 }}>
              Tiến Độ Hoàn Thành Toàn Diện
            </Typography.Text>
            <Typography.Text type="secondary" style={{ fontSize: 13 }}>
              {progress.done}/{progress.total} task ({formatPercent(progress.donePercent)})
            </Typography.Text>
          </Flex>

          <Progress
            percent={progress.donePercent}
            status={progress.donePercent === 100 ? 'success' : 'active'}
            strokeColor={{
              '0%': '#6366f1',
              '100%': '#22c55e',
            }}
          />

          <Row gutter={[16, 16]} style={{ marginTop: 8 }}>
            <Col xs={12} sm={8} md={4}>
              <Statistic
                title="Tỉ lệ đúng hạn"
                value={formatPercent(performance.onTimeRate)}
                valueStyle={{ fontSize: 18 }}
              />
            </Col>
            <Col xs={12} sm={8} md={5}>
              <Statistic
                title="Thời gian xong TB"
                value={formatDurationHours(performance.avgCompletionHours)}
                valueStyle={{ fontSize: 18 }}
              />
            </Col>
            <Col xs={12} sm={8} md={5}>
              <Statistic
                title="Lead Time TB"
                value={formatDurationHours(performance.avgLeadTimeHours)}
                valueStyle={{ fontSize: 18 }}
              />
            </Col>
            <Col xs={12} sm={8} md={5}>
              <Statistic
                title="Throughput / tuần"
                value={`${performance.throughputPerWeek.toFixed(1)} task`}
                valueStyle={{ fontSize: 18 }}
              />
            </Col>
            <Col xs={12} sm={8} md={5}>
              <Statistic
                title="Xong trong kỳ"
                value={`${performance.completedInRange} task`}
                valueStyle={{ fontSize: 18, color: '#16a34a' }}
              />
            </Col>
          </Row>
        </Flex>
      </Card>

      {/* Main Details: Empty check or Breakdown Tables */}
      {progress.total === 0 ? (
        <Card
          styles={{ body: { padding: 40, textAlign: 'center' } }}
          style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
        >
          <Empty
            image={Empty.PRESENTED_IMAGE_SIMPLE}
            description="Không có task nào trong phạm vi báo cáo đã chọn"
          />
        </Card>
      ) : (
        <Card
          styles={{ body: { padding: 16 } }}
          style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
        >
          <Tabs
            defaultActiveKey="byBoard"
            items={[
              {
                key: 'byBoard',
                label: `Theo Bảng (${byBoard.length})`,
                children: (
                  <ReportTable<ReportBoardRowResponse>
                    rowKey="boardId"
                    columns={boardColumns}
                    dataSource={byBoard}
                    pagination={{ pageSize: 5 }}
                  />
                ),
              },
              {
                key: 'byAssignee',
                label: `Theo Người Phụ Trách (${byAssignee.length})`,
                children: (
                  <ReportTable<ReportAssigneeRowResponse>
                    rowKey={(record) => record.assigneeId || 'unassigned'}
                    columns={assigneeColumns}
                    dataSource={byAssignee}
                    pagination={{ pageSize: 5 }}
                  />
                ),
              },
            ]}
          />
        </Card>
      )}

      {/* Activity & AI Health Cards */}
      <Row gutter={[16, 16]}>
        {/* Activity Breakdown */}
        <Col xs={24} lg={health?.runsScanned > 0 ? 12 : 24}>
          <Card
            title={
              <Flex align="center" gap={8}>
                <ThunderboltOutlined style={{ color: '#eab308' }} />
                <span>Hoạt Động Trong Kỳ</span>
              </Flex>
            }
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0', height: '100%' }}
          >
            <Flex vertical gap="middle">
              <Row gutter={[16, 16]}>
                <Col span={8}>
                  <Statistic
                    title="Tổng thao tác"
                    value={activity?.totalActions || 0}
                    valueStyle={{ fontSize: 20 }}
                  />
                </Col>
                <Col span={8}>
                  <Statistic
                    title="Người hoạt động"
                    value={activity?.activeUsers || 0}
                    valueStyle={{ fontSize: 20 }}
                  />
                </Col>
                <Col span={8}>
                  <Statistic
                    title="Thao tác / ngày"
                    value={activity?.actionsPerDay?.toFixed(1) || 0}
                    valueStyle={{ fontSize: 20 }}
                  />
                </Col>
              </Row>

              <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                Phân bổ hành động:
              </Typography.Text>
              <Flex wrap="wrap" gap={8}>
                {activity?.byAction && activity.byAction.length > 0 ? (
                  activity.byAction.map((act) => (
                    <Tag key={act.action} color="blue" style={{ borderRadius: 6, padding: '4px 8px' }}>
                      {act.action}: <strong>{act.count}</strong>
                    </Tag>
                  ))
                ) : (
                  <Typography.Text type="secondary">Chưa có hoạt động nào trong khoảng thời gian này</Typography.Text>
                )}
              </Flex>
            </Flex>
          </Card>
        </Col>

        {/* AI Health - Only rendered when health runsScanned > 0 */}
        {health?.runsScanned > 0 && (
          <Col xs={24} lg={12}>
            <Card
              title={
                <Flex align="center" gap={8}>
                  <RadarChartOutlined style={{ color: '#4338ca' }} />
                  <span>Sức Khoẻ Dự Án (AI Observer)</span>
                </Flex>
              }
              styles={{ body: { padding: '16px 20px' } }}
              style={{ borderRadius: 12, border: '1px solid #e2e8f0', height: '100%' }}
            >
              <Flex vertical gap="middle">
                <Flex justify="space-between" align="center">
                  <Typography.Text>Số lượt quét đã phân tích:</Typography.Text>
                  <Tag color="purple" style={{ fontSize: 14, padding: '2px 10px', borderRadius: 12 }}>
                    {health.runsScanned} lượt quét
                  </Tag>
                </Flex>

                <div>
                  <Typography.Text type="secondary" style={{ fontSize: 13, display: 'block', marginBottom: 6 }}>
                    Vấn đề phát hiện theo mức độ:
                  </Typography.Text>
                  <Flex wrap="wrap" gap={8}>
                    {health.findingsBySeverity?.map((sev) => (
                      <Tag
                        key={sev.severity}
                        color={getSeverityTagColor(sev.severity)}
                        style={{ borderRadius: 6, padding: '4px 8px' }}
                      >
                        {sev.severity}: <strong>{sev.count}</strong>
                      </Tag>
                    ))}
                  </Flex>
                </div>

                <div>
                  <Typography.Text type="secondary" style={{ fontSize: 13, display: 'block', marginBottom: 6 }}>
                    Tín hiệu bất thường theo loại:
                  </Typography.Text>
                  <Flex wrap="wrap" gap={8}>
                    {health.signalsByType?.map((sig) => (
                      <Tag key={sig.type} color="volcano" style={{ borderRadius: 6, padding: '4px 8px' }}>
                        {sig.type}: <strong>{sig.count}</strong>
                      </Tag>
                    ))}
                  </Flex>
                </div>
              </Flex>
            </Card>
          </Col>
        )}
      </Row>
    </Flex>
  )
}
