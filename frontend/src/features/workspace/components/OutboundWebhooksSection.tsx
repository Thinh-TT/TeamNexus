import React, { useState } from 'react'
import {
  ApiOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  CloseCircleOutlined,
  DeleteOutlined,
  DiscordOutlined,
  EditOutlined,
  HistoryOutlined,
  PlusOutlined,
  SendOutlined,
  SlackOutlined,
  WarningOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Checkbox,
  Col,
  Drawer,
  Empty,
  Flex,
  Form,
  Input,
  Modal,
  Popconfirm,
  Radio,
  Row,
  Space,
  Statistic,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import {
  DEFAULT_WEBHOOK_EVENTS,
  WEBHOOK_EVENT_CATALOG,
  type CreateWorkspaceWebhookRequest,
  type UpdateWorkspaceWebhookRequest,
  type WebhookDeliveryItemResponse,
  type WebhookProvider,
  type WebhookTestResultResponse,
  type WorkspaceWebhookEndpointResponse,
  type WorkspaceWebhookStatsResponse,
} from '../types/integration.types'

interface OutboundWebhooksSectionProps {
  endpoints: WorkspaceWebhookEndpointResponse[]
  stats: WorkspaceWebhookStatsResponse
  loading: boolean
  onCreate: (req: CreateWorkspaceWebhookRequest) => Promise<boolean>
  onUpdate: (endpointId: string, req: UpdateWorkspaceWebhookRequest) => Promise<boolean>
  onDelete: (endpointId: string) => Promise<boolean>
  onTest: (endpointId: string) => Promise<WebhookTestResultResponse | null>
  onFetchDeliveries: (endpointId: string) => Promise<WebhookDeliveryItemResponse[]>
}

const EVENT_LABEL_MAP = new Map<string, string>(
  WEBHOOK_EVENT_CATALOG.map((item) => [item.key, item.label])
)

function getEventLabel(key: string): string {
  if (key === 'TestPing') return 'Kiểm tra kết nối (TestPing)'
  return EVENT_LABEL_MAP.get(key) ?? key
}

export const OutboundWebhooksSection: React.FC<OutboundWebhooksSectionProps> = ({
  endpoints,
  stats,
  loading,
  onCreate,
  onUpdate,
  onDelete,
  onTest,
  onFetchDeliveries,
}) => {
  const [form] = Form.useForm()
  const [modalOpen, setModalOpen] = useState(false)
  const [editingEndpoint, setEditingEndpoint] =
    useState<WorkspaceWebhookEndpointResponse | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [testingId, setTestingId] = useState<string | null>(null)
  const [togglingId, setTogglingId] = useState<string | null>(null)

  // Delivery history drawer state
  const [drawerOpen, setDrawerOpen] = useState(false)
  const [historyEndpoint, setHistoryEndpoint] =
    useState<WorkspaceWebhookEndpointResponse | null>(null)
  const [deliveries, setDeliveries] = useState<WebhookDeliveryItemResponse[]>([])
  const [deliveriesLoading, setDeliveriesLoading] = useState(false)

  const openCreateModal = () => {
    setEditingEndpoint(null)
    form.resetFields()
    form.setFieldsValue({
      provider: 'slack',
      name: '',
      targetUrl: '',
      subscribedEvents: DEFAULT_WEBHOOK_EVENTS,
    })
    setModalOpen(true)
  }

  const openEditModal = (endpoint: WorkspaceWebhookEndpointResponse) => {
    setEditingEndpoint(endpoint)
    form.resetFields()
    form.setFieldsValue({
      provider: endpoint.provider,
      name: endpoint.name,
      targetUrl: '',
      subscribedEvents:
        endpoint.subscribedEvents.length > 0
          ? endpoint.subscribedEvents
          : DEFAULT_WEBHOOK_EVENTS,
    })
    setModalOpen(true)
  }

  const handleSubmitModal = async () => {
    try {
      const values = await form.validateFields()
      setSubmitting(true)
      const events: string[] =
        Array.isArray(values.subscribedEvents) && values.subscribedEvents.length > 0
          ? values.subscribedEvents
          : DEFAULT_WEBHOOK_EVENTS

      if (editingEndpoint) {
        const updateReq: UpdateWorkspaceWebhookRequest = {
          name: values.name.trim(),
          subscribedEvents: events,
        }
        if (values.targetUrl && values.targetUrl.trim().length > 0) {
          updateReq.targetUrl = values.targetUrl.trim()
        }
        const ok = await onUpdate(editingEndpoint.id, updateReq)
        if (ok) {
          setModalOpen(false)
        }
      } else {
        const createReq: CreateWorkspaceWebhookRequest = {
          provider: values.provider as WebhookProvider,
          name: values.name.trim(),
          targetUrl: values.targetUrl.trim(),
          subscribedEvents: events,
        }
        const ok = await onCreate(createReq)
        if (ok) {
          setModalOpen(false)
        }
      }
    } catch {
      // Validation handled by Form
    } finally {
      setSubmitting(false)
    }
  }

  const handleOpenHistory = async (endpoint: WorkspaceWebhookEndpointResponse) => {
    setHistoryEndpoint(endpoint)
    setDrawerOpen(true)
    setDeliveriesLoading(true)
    try {
      const items = await onFetchDeliveries(endpoint.id)
      setDeliveries(items)
    } finally {
      setDeliveriesLoading(false)
    }
  }

  const handleTest = async (endpointId: string) => {
    setTestingId(endpointId)
    try {
      await onTest(endpointId)
    } finally {
      setTestingId(null)
    }
  }

  const handleToggleActive = async (
    endpoint: WorkspaceWebhookEndpointResponse,
    checked: boolean
  ) => {
    setTogglingId(endpoint.id)
    try {
      await onUpdate(endpoint.id, { isActive: checked })
    } finally {
      setTogglingId(null)
    }
  }

  const columns: ColumnsType<WorkspaceWebhookEndpointResponse> = [
    {
      title: 'Kênh Webhook',
      key: 'name',
      render: (_, record) => {
        const isDiscord = record.provider.toLowerCase() === 'discord'
        return (
          <Flex vertical gap={4}>
            <Flex align="center" gap={8} wrap="wrap">
              {isDiscord ? (
                <DiscordOutlined style={{ color: '#5865F2', fontSize: 16, flexShrink: 0 }} />
              ) : (
                <SlackOutlined style={{ color: '#E01E5A', fontSize: 16, flexShrink: 0 }} />
              )}
              <Typography.Text strong style={{ color: '#0f172a' }}>
                {record.name}
              </Typography.Text>
              <Tag
                color={isDiscord ? 'geekblue' : 'magenta'}
                style={{
                  margin: 0,
                  fontSize: 10.5,
                  fontWeight: 600,
                  whiteSpace: 'nowrap',
                  flexShrink: 0,
                }}
              >
                {isDiscord ? 'Discord' : 'Slack'}
              </Tag>
            </Flex>
            <Typography.Text
              type="secondary"
              style={{
                fontSize: 11.5,
                fontFamily: 'var(--tn-font-mono, monospace)',
              }}
            >
              {record.maskedTargetUrl}
            </Typography.Text>
          </Flex>
        )
      },
    },
    {
      title: 'Sự kiện đăng ký',
      key: 'events',
      render: (_, record) => (
        <Flex wrap="wrap" gap={4}>
          {record.subscribedEvents.map((ev) => (
            <Tag
              key={ev}
              style={{
                margin: 0,
                fontSize: 11,
                borderRadius: 5,
                backgroundColor: '#f1f5f9',
                borderColor: '#e2e8f0',
                color: '#334155',
                whiteSpace: 'nowrap',
                flexShrink: 0,
              }}
            >
              {getEventLabel(ev)}
            </Tag>
          ))}
        </Flex>
      ),
    },
    {
      title: 'Trạng thái',
      key: 'status',
      width: 170,
      render: (_, record) => (
        <Flex vertical gap={4}>
          <Flex align="center" gap={8}>
            <Switch
              size="small"
              checked={record.isActive}
              loading={togglingId === record.id}
              onChange={(checked) => void handleToggleActive(record, checked)}
              aria-label={`Bật hoặc tắt ${record.name}`}
            />
            <Tag
              color={record.isActive ? 'success' : 'error'}
              style={{ margin: 0, whiteSpace: 'nowrap', flexShrink: 0 }}
            >
              {record.isActive ? 'Hoạt động' : 'Đã tạm ngắt'}
            </Tag>
          </Flex>
          {record.failureCount > 0 && (
            <Tooltip title={record.lastError || 'Có lỗi khi gửi tới webhook'}>
              <Typography.Text type="danger" style={{ fontSize: 11 }}>
                <WarningOutlined style={{ marginRight: 4 }} />
                Lỗi liên tiếp: {record.failureCount}
              </Typography.Text>
            </Tooltip>
          )}
        </Flex>
      ),
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 240,
      render: (_, record) => (
        <Space size={4} wrap>
          <Button
            size="small"
            icon={<SendOutlined />}
            loading={testingId === record.id}
            onClick={() => void handleTest(record.id)}
            data-testid={`test-webhook-btn-${record.id}`}
            style={{ borderRadius: 6 }}
          >
            Gửi thử
          </Button>
          <Button
            size="small"
            icon={<HistoryOutlined />}
            onClick={() => void handleOpenHistory(record)}
            data-testid={`webhook-history-btn-${record.id}`}
            style={{ borderRadius: 6 }}
          >
            Lịch sử
          </Button>
          <Tooltip title="Chỉnh sửa">
            <Button
              size="small"
              icon={<EditOutlined />}
              onClick={() => openEditModal(record)}
              data-testid={`edit-webhook-btn-${record.id}`}
              style={{ borderRadius: 6 }}
            />
          </Tooltip>
          <Popconfirm
            title="Xoá Webhook này?"
            description="Lịch sử gửi Outbox của kênh này cũng sẽ bị xoá."
            okText="Xoá"
            cancelText="Huỷ"
            okButtonProps={{ danger: true }}
            onConfirm={() => void onDelete(record.id)}
          >
            <Tooltip title="Xoá">
              <Button
                size="small"
                danger
                icon={<DeleteOutlined />}
                data-testid={`delete-webhook-btn-${record.id}`}
                style={{ borderRadius: 6 }}
              />
            </Tooltip>
          </Popconfirm>
        </Space>
      ),
    },
  ]

  const deliveryColumns: ColumnsType<WebhookDeliveryItemResponse> = [
    {
      title: 'Sự kiện',
      dataIndex: 'eventType',
      key: 'eventType',
      render: (val: string) => (
        <Typography.Text strong style={{ fontSize: 12.5 }}>
          {getEventLabel(val)}
        </Typography.Text>
      ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 115,
      render: (status: string) => {
        const color = status === 'Sent' ? 'success' : status === 'Failed' ? 'error' : 'warning'
        return (
          <Tag color={color} style={{ margin: 0, whiteSpace: 'nowrap' }}>
            {status}
          </Tag>
        )
      },
    },
    {
      title: 'HTTP / Lần thử',
      key: 'attempts',
      width: 125,
      render: (_, item) => (
        <Typography.Text style={{ fontSize: 12, fontFamily: 'var(--tn-font-mono, monospace)' }}>
          {item.lastHttpStatus ?? '—'} · {item.attemptCount}/{item.maxAttempts}
        </Typography.Text>
      ),
    },
    {
      title: 'Thời gian',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 145,
      render: (val: string) => (
        <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
          {dayjs(val).format('DD/MM/YYYY HH:mm')}
        </Typography.Text>
      ),
    },
  ]

  return (
    <Card
      data-testid="outbound-webhooks-section"
      style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      title={
        <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
          <Flex align="center" gap={8}>
            <ApiOutlined style={{ color: '#4f46e5', fontSize: 18 }} />
            <div>
              <Typography.Text strong style={{ fontSize: 15, display: 'block' }}>
                Webhook thông báo ra ngoài (Slack / Discord)
              </Typography.Text>
              <Typography.Text type="secondary" style={{ fontSize: 12, fontWeight: 400 }}>
                Đẩy cảnh báo AI Observer, đề xuất Agent & sự kiện thẻ qua Transactional Outbox (chống SSRF HTTPS).
              </Typography.Text>
            </div>
          </Flex>
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={openCreateModal}
            data-testid="add-webhook-btn"
            style={{ backgroundColor: '#4f46e5', borderColor: '#4f46e5', borderRadius: 8 }}
          >
            Thêm Webhook
          </Button>
        </Flex>
      }
    >
      <Flex vertical gap={16}>
        {/* 24h Outbox Stats */}
        <Row gutter={[16, 16]}>
          <Col xs={24} sm={8}>
            <Card
              size="small"
              style={{
                borderRadius: 10,
                border: '1px solid #e2e8f0',
                borderTop: '3px solid #10b981',
              }}
            >
              <Statistic
                title="Đã gửi (24h)"
                value={stats.sentLast24h}
                prefix={<CheckCircleOutlined style={{ color: '#10b981' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={8}>
            <Card
              size="small"
              style={{
                borderRadius: 10,
                border: '1px solid #e2e8f0',
                borderTop: '3px solid #ef4444',
              }}
            >
              <Statistic
                title="Thất bại (24h)"
                value={stats.failedLast24h}
                prefix={<CloseCircleOutlined style={{ color: '#ef4444' }} />}
              />
            </Card>
          </Col>
          <Col xs={24} sm={8}>
            <Card
              size="small"
              style={{
                borderRadius: 10,
                border: '1px solid #e2e8f0',
                borderTop: '3px solid #f59e0b',
              }}
            >
              <Statistic
                title="Đang chờ Outbox"
                value={stats.pendingCount}
                prefix={<ClockCircleOutlined style={{ color: '#f59e0b' }} />}
              />
            </Card>
          </Col>
        </Row>

        <Table<WorkspaceWebhookEndpointResponse>
          rowKey="id"
          columns={columns}
          dataSource={endpoints}
          loading={loading}
          pagination={false}
          locale={{
            emptyText: (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Chưa có kênh Slack hoặc Discord nào được cấu hình."
              />
            ),
          }}
        />
      </Flex>

      {/* Create / Edit Modal */}
      <Modal
        open={modalOpen}
        title={editingEndpoint ? 'Chỉnh sửa Webhook' : 'Thêm kênh Webhook mới'}
        okText={editingEndpoint ? 'Lưu thay đổi' : 'Tạo Webhook'}
        cancelText="Huỷ"
        onCancel={() => setModalOpen(false)}
        onOk={() => void handleSubmitModal()}
        confirmLoading={submitting}
        width={600}
        destroyOnHidden
      >
        <Form form={form} layout="vertical" style={{ marginTop: 12 }}>
          <Form.Item name="provider" label="Nhà cung cấp" rules={[{ required: true }]}>
            <Radio.Group disabled={Boolean(editingEndpoint)}>
              <Radio.Button value="slack">
                <SlackOutlined style={{ color: '#E01E5A', marginRight: 6 }} />
                Slack
              </Radio.Button>
              <Radio.Button value="discord">
                <DiscordOutlined style={{ color: '#5865F2', marginRight: 6 }} />
                Discord
              </Radio.Button>
            </Radio.Group>
          </Form.Item>

          <Form.Item
            name="name"
            label="Tên kênh hiển thị"
            rules={[
              { required: true, message: 'Vui lòng nhập tên kênh Webhook' },
              { max: 120, message: 'Tên kênh tối đa 120 ký tự' },
            ]}
          >
            <Input placeholder="Ví dụ: #team-alerts hoặc Discord DevOps" maxLength={120} />
          </Form.Item>

          <Form.Item
            name="targetUrl"
            label="Incoming Webhook URL (HTTPS)"
            rules={
              editingEndpoint
                ? [
                    { max: 1024, message: 'URL tối đa 1024 ký tự' },
                    {
                      validator: (_, value: string | undefined) => {
                        if (!value || value.trim().length === 0) return Promise.resolve()
                        if (!value.trim().toLowerCase().startsWith('https://')) {
                          return Promise.reject(new Error('URL bắt buộc dùng giao thức https://'))
                        }
                        return Promise.resolve()
                      },
                    },
                  ]
                : [
                    { required: true, message: 'Vui lòng dán URL Webhook' },
                    { max: 1024, message: 'URL tối đa 1024 ký tự' },
                    {
                      validator: (_, value: string | undefined) => {
                        if (!value || value.trim().toLowerCase().startsWith('https://')) {
                          return Promise.resolve()
                        }
                        return Promise.reject(new Error('URL bắt buộc dùng giao thức https://'))
                      },
                    },
                  ]
            }
          >
            <Input
              placeholder={
                editingEndpoint
                  ? 'Để trống nếu giữ nguyên URL hiện tại'
                  : 'https://hooks.slack.com/services/... hoặc https://discord.com/api/webhooks/...'
              }
              style={{ fontFamily: 'var(--tn-font-mono, monospace)', fontSize: 12.5 }}
            />
          </Form.Item>

          <Form.Item
            name="subscribedEvents"
            label="Sự kiện muốn nhận thông báo"
            rules={[
              {
                validator: (_, val: string[] | undefined) =>
                  val && val.length > 0
                    ? Promise.resolve()
                    : Promise.reject(new Error('Chọn ít nhất 1 loại sự kiện')),
              },
            ]}
          >
            <Checkbox.Group style={{ width: '100%' }}>
              <Flex vertical gap={8} style={{ width: '100%' }}>
                {WEBHOOK_EVENT_CATALOG.map((ev) => (
                  <Checkbox key={ev.key} value={ev.key}>
                    <Flex vertical>
                      <Typography.Text strong style={{ fontSize: 13 }}>
                        {ev.label}
                      </Typography.Text>
                      <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
                        {ev.description}
                      </Typography.Text>
                    </Flex>
                  </Checkbox>
                ))}
              </Flex>
            </Checkbox.Group>
          </Form.Item>
        </Form>
      </Modal>

      {/* Outbox Delivery History Drawer */}
      <Drawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        title={`Lịch sử Outbox: ${historyEndpoint?.name ?? ''}`}
        size="large"
        destroyOnHidden
        data-testid="webhook-deliveries-drawer"
      >
        <Flex vertical gap={12}>
          <Alert
            type="info"
            showIcon
            title="Hiển thị tối đa 20 lần gửi gần nhất trong Outbox. Khi vượt 10 lỗi liên tiếp, kênh tự động tạm ngắt để bảo vệ hệ thống."
            style={{ borderRadius: 8 }}
          />
          <Table<WebhookDeliveryItemResponse>
            rowKey="id"
            size="small"
            columns={deliveryColumns}
            dataSource={deliveries}
            loading={deliveriesLoading}
            pagination={false}
            expandable={{
              rowExpandable: (record) => Boolean(record.lastError),
              expandedRowRender: (record) => (
                <Typography.Text type="danger" style={{ fontSize: 12 }}>
                  Lỗi: {record.lastError}
                </Typography.Text>
              ),
            }}
            locale={{
              emptyText: (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="Chưa có lượt gửi Outbox nào."
                />
              ),
            }}
          />
        </Flex>
      </Drawer>
    </Card>
  )
}
