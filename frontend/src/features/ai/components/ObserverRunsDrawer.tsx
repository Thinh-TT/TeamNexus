import React, { useEffect } from 'react'
import {
  Alert,
  Button,
  Card,
  Descriptions,
  Divider,
  Drawer,
  Empty,
  Flex,
  List,
  Popconfirm,
  Spin,
  Tag,
  Typography,
} from 'antd'
import {
  ArrowLeftOutlined,
  PlayCircleOutlined,
  RadarChartOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import dayjs from 'dayjs'
import { useObserverRuns } from '../hooks/useObserverRuns'
import type { ObserverRunStatus } from '../types/notification.types'
import { ObserverFindingCard } from './ObserverFindingCard'

interface ObserverRunsDrawerProps {
  open: boolean
  onClose: () => void
  workspaceId: string
  onScanFinished?: () => void
}

const getStatusTag = (status: ObserverRunStatus) => {
  switch (status) {
    case 'Completed':
      return <Tag color="success">HOÀN TẤT</Tag>
    case 'Running':
      return <Tag color="processing">ĐANG CHẠY</Tag>
    case 'Skipped':
      return <Tag color="warning">BỎ QUA</Tag>
    case 'Failed':
      return <Tag color="error">THẤT BẠI</Tag>
    default:
      return <Tag>{status}</Tag>
  }
}

export const ObserverRunsDrawer: React.FC<ObserverRunsDrawerProps> = ({
  open,
  onClose,
  workspaceId,
  onScanFinished,
}) => {
  const {
    runs,
    selectedRun,
    status,
    error,
    reload,
    openRun,
    closeRun,
    triggerScan,
  } = useObserverRuns(workspaceId)

  useEffect(() => {
    if (open) {
      reload()
    } else {
      closeRun()
    }
  }, [open, reload, closeRun])

  const handleTriggerScan = async () => {
    await triggerScan(onScanFinished)
  }

  return (
    <Drawer
      title={
        <Flex justify="space-between" align="center" style={{ width: '100%', paddingRight: 8 }}>
          <Flex align="center" gap={8}>
            {selectedRun ? (
              <Button
                size="small"
                type="text"
                icon={<ArrowLeftOutlined />}
                onClick={closeRun}
              />
            ) : (
              <RadarChartOutlined style={{ color: '#4f46e5', fontSize: 18 }} />
            )}
            <Typography.Text strong style={{ fontSize: 16 }}>
              {selectedRun ? 'Chi tiết đợt quét' : 'AI Observer — Lịch sử quét'}
            </Typography.Text>
          </Flex>

          <Flex align="center" gap={6}>
            {!selectedRun && (
              <>
                <Button
                  size="small"
                  type="text"
                  icon={<ReloadOutlined />}
                  onClick={() => reload()}
                  disabled={status === 'loading' || status === 'scanning'}
                />
                <Popconfirm
                  title="Kích hoạt quét AI Observer"
                  description="Bạn có muốn phân tích toàn bộ tín hiệu rủi ro của dự án ngay bây giờ?"
                  okText="Quét ngay"
                  cancelText="Huỷ"
                  onConfirm={handleTriggerScan}
                  disabled={status === 'scanning'}
                >
                  <Button
                    size="small"
                    type="primary"
                    icon={<PlayCircleOutlined />}
                    loading={status === 'scanning'}
                    style={{ backgroundColor: '#4f46e5', borderRadius: 6 }}
                  >
                    Quét ngay
                  </Button>
                </Popconfirm>
              </>
            )}
          </Flex>
        </Flex>
      }
      placement="right"
      size="large"
      open={open}
      onClose={onClose}
      destroyOnHidden
      styles={{
        body: { padding: '16px', backgroundColor: '#f8fafc' },
      }}
    >
      <Flex vertical gap={12} style={{ height: '100%' }}>
        {error && (
          <Alert
            title="Lỗi thao tác AI Observer"
            description={error}
            type="error"
            showIcon
            closable
          />
        )}

        {/* View 1: Selected Run Detail */}
        {selectedRun ? (
          <div style={{ flex: 1, overflowY: 'auto' }}>
            <Card
              size="small"
              style={{
                marginBottom: 16,
                borderRadius: 8,
                backgroundColor: '#ffffff',
                borderColor: '#e2e8f0',
              }}
              styles={{ body: { padding: '14px' } }}
            >
              <Descriptions
                title={<span style={{ fontSize: 14 }}>Thông tin đợt quét</span>}
                size="small"
                column={2}
              >
                <Descriptions.Item label="Trạng thái">
                  {getStatusTag(selectedRun.status)}
                </Descriptions.Item>
                <Descriptions.Item label="Thời gian bắt đầu">
                  {dayjs(selectedRun.startedAt).format('HH:mm:ss DD/MM/YYYY')}
                </Descriptions.Item>
                <Descriptions.Item label="Tín hiệu phát hiện">
                  <Tag color="blue">{selectedRun.signalsDetected}</Tag>
                </Descriptions.Item>
                <Descriptions.Item label="Cảnh báo tạo ra">
                  <Tag color={selectedRun.notificationsCreated > 0 ? 'orange' : 'default'}>
                    {selectedRun.notificationsCreated}
                  </Tag>
                </Descriptions.Item>
                <Descriptions.Item label="Gọi DeepSeek AI">
                  {selectedRun.aiCalled ? <Tag color="purple">Có</Tag> : <Tag>Không</Tag>}
                </Descriptions.Item>
                {selectedRun.summary && (
                  <>
                    {(selectedRun.summary as { durationMs?: number }).durationMs !== undefined && (
                      <Descriptions.Item label="Thời lượng">
                        {((selectedRun.summary as { durationMs?: number }).durationMs ?? 0)} ms
                      </Descriptions.Item>
                    )}
                    {(selectedRun.summary as { promptTokens?: number }).promptTokens !== undefined &&
                      (selectedRun.summary as { promptTokens?: number }).promptTokens !== null && (
                        <Descriptions.Item label="Token sử dụng">
                          {((selectedRun.summary as { promptTokens?: number }).promptTokens ?? 0) +
                            ((selectedRun.summary as { completionTokens?: number }).completionTokens ?? 0)}
                        </Descriptions.Item>
                      )}
                  </>
                )}
              </Descriptions>
            </Card>

            <Typography.Title level={5} style={{ margin: '8px 0 12px', fontSize: 14 }}>
              Danh sách phát hiện ({selectedRun.findings?.length || 0})
            </Typography.Title>

            {selectedRun.findings && selectedRun.findings.length > 0 ? (
              <div>
                {selectedRun.findings.map((f, idx) => (
                  <ObserverFindingCard key={`${f.type}-${idx}`} finding={f} />
                ))}
              </div>
            ) : (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Không có cảnh báo nào được ghi nhận trong đợt quét này."
                style={{ marginTop: 40 }}
              />
            )}
          </div>
        ) : (
          /* View 2: List of Observer Runs */
          <div style={{ flex: 1, overflowY: 'auto' }}>
            {status === 'loading' && (runs?.length ?? 0) === 0 ? (
              <Flex align="center" justify="center" style={{ height: 200 }}>
                <Spin description="Đang tải lịch sử quét..." />
              </Flex>
            ) : (runs?.length ?? 0) === 0 ? (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Chưa có đợt quét AI Observer nào. Nhấn 'Quét ngay' để bắt đầu."
                style={{ marginTop: 60 }}
              />
            ) : (
              <List
                dataSource={runs}
                renderItem={(run) => (
                  <Card
                    key={run.id}
                    size="small"
                    hoverable
                    onClick={() => openRun(run.id)}
                    style={{
                      marginBottom: 10,
                      borderRadius: 8,
                      borderColor: '#e2e8f0',
                      backgroundColor: '#ffffff',
                      cursor: 'pointer',
                    }}
                    styles={{ body: { padding: '12px 14px' } }}
                  >
                    <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
                      <Flex vertical gap={4}>
                        <Flex align="center" gap={6}>
                          {getStatusTag(run.status)}
                          <Typography.Text strong style={{ fontSize: 13 }}>
                            {dayjs(run.startedAt).format('HH:mm:ss DD/MM/YYYY')}
                          </Typography.Text>
                        </Flex>
                        <Flex align="center" gap={12} wrap="wrap" style={{ marginTop: 2 }}>
                          <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                            Tín hiệu: <strong>{run.signalsDetected}</strong>
                          </Typography.Text>
                          <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                            Cảnh báo: <strong>{run.notificationsCreated}</strong>
                          </Typography.Text>
                          <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                            AI: {run.aiCalled ? 'Đã gọi' : 'Không'}
                          </Typography.Text>
                        </Flex>
                      </Flex>

                      <Button size="small" type="link">
                        Xem chi tiết →
                      </Button>
                    </Flex>
                  </Card>
                )}
              />
            )}
          </div>
        )}

        <Divider style={{ margin: '8px 0' }} />
        <Typography.Text type="secondary" style={{ fontSize: 11, textAlign: 'center' }}>
          * Chỉ thành viên có vai trò Manager hoặc Admin mới có quyền thực hiện quét và xem dữ liệu này.
        </Typography.Text>
      </Flex>
    </Drawer>
  )
}
