import React, { useEffect, useState } from 'react'
import {
  CopyOutlined,
  DeleteOutlined,
  GithubOutlined,
  KeyOutlined,
  ReloadOutlined,
  SaveOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Col,
  Divider,
  Empty,
  Flex,
  Form,
  Input,
  Popconfirm,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import dayjs from 'dayjs'
import type { BoardResponse } from '../../board/types/board.types'
import type {
  GitHubWebhookDeliveryResponse,
  UpsertWorkspaceGitHubIntegrationRequest,
  WorkspaceGitHubIntegrationResponse,
} from '../types/integration.types'

const EMPTY_GUID = '00000000-0000-0000-0000-000000000000'

interface GitHubIntegrationSectionProps {
  config: WorkspaceGitHubIntegrationResponse | null
  deliveries: GitHubWebhookDeliveryResponse[]
  boards: BoardResponse[]
  oneTimeSecret: string | null
  loading: boolean
  onSave: (
    req: UpsertWorkspaceGitHubIntegrationRequest
  ) => Promise<WorkspaceGitHubIntegrationResponse | null>
  onRotateSecret: () => Promise<string | null>
  onDelete: () => Promise<boolean>
  onRefreshDeliveries: () => Promise<void>
  onDismissSecret: () => void
}

export const GitHubIntegrationSection: React.FC<GitHubIntegrationSectionProps> = ({
  config,
  deliveries,
  boards,
  oneTimeSecret,
  loading,
  onSave,
  onRotateSecret,
  onDelete,
  onRefreshDeliveries,
  onDismissSecret,
}) => {
  const [form] = Form.useForm()
  const [saving, setSaving] = useState(false)
  const [rotating, setRotating] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [refreshingDeliveries, setRefreshingDeliveries] = useState(false)

  useEffect(() => {
    if (config) {
      form.setFieldsValue({
        isEnabled: config.isEnabled,
        defaultBoardId: config.defaultBoardId ?? undefined,
        issueLabelFilter: config.issueLabelFilter ?? '',
        autoMoveOnPr: config.autoMoveOnPr,
        autoCreateTasksFromIssues: config.autoCreateTasksFromIssues,
        syncIssueComments: config.syncIssueComments,
      })
    } else {
      form.setFieldsValue({
        isEnabled: true,
        defaultBoardId: boards[0]?.id ?? undefined,
        issueLabelFilter: '',
        autoMoveOnPr: true,
        autoCreateTasksFromIssues: true,
        syncIssueComments: true,
      })
    }
  }, [config, boards, form])

  const handleCopyText = async (value: string, successText: string) => {
    try {
      await navigator.clipboard?.writeText(value)
      message.success(successText)
    } catch {
      message.error('Không thể sao chép vào khay nhớ tạm')
    }
  }

  const handleSave = async () => {
    try {
      const values = await form.validateFields()
      setSaving(true)

      const filterText = typeof values.issueLabelFilter === 'string'
        ? values.issueLabelFilter.trim()
        : ''

      const req: UpsertWorkspaceGitHubIntegrationRequest = {
        isEnabled: Boolean(values.isEnabled),
        // Backend ignores null on update; Guid.Empty explicitly clears DefaultBoardId.
        defaultBoardId: values.defaultBoardId
          ? values.defaultBoardId
          : config
            ? EMPTY_GUID
            : undefined,
        // Empty string explicitly clears IssueLabelFilter on update.
        issueLabelFilter: filterText,
        autoMoveOnPr: Boolean(values.autoMoveOnPr),
        autoCreateTasksFromIssues: Boolean(values.autoCreateTasksFromIssues),
        syncIssueComments: Boolean(values.syncIssueComments),
      }

      await onSave(req)
    } catch {
      // Validation handled by Form
    } finally {
      setSaving(false)
    }
  }

  const handleRotate = async () => {
    setRotating(true)
    try {
      await onRotateSecret()
    } finally {
      setRotating(false)
    }
  }

  const handleDelete = async () => {
    setDeleting(true)
    try {
      await onDelete()
    } finally {
      setDeleting(false)
    }
  }

  const handleRefreshDeliveries = async () => {
    setRefreshingDeliveries(true)
    try {
      await onRefreshDeliveries()
    } finally {
      setRefreshingDeliveries(false)
    }
  }

  const deliveryColumns: ColumnsType<GitHubWebhookDeliveryResponse> = [
    {
      title: 'Delivery ID',
      dataIndex: 'deliveryId',
      key: 'deliveryId',
      width: 130,
      render: (id: string) => (
        <Tooltip title={id}>
          <Typography.Text
            style={{
              fontFamily: 'var(--tn-font-mono, monospace)',
              fontSize: 11.5,
              color: '#475569',
            }}
          >
            {id.length > 10 ? `${id.slice(0, 8)}…` : id}
          </Typography.Text>
        </Tooltip>
      ),
    },
    {
      title: 'Sự kiện',
      key: 'eventName',
      width: 180,
      render: (_, record) => (
        <Space size={4} wrap>
          <Tag
            style={{
              margin: 0,
              fontFamily: 'var(--tn-font-mono, monospace)',
              fontSize: 11,
              whiteSpace: 'nowrap',
            }}
          >
            {record.eventName}
          </Tag>
          {record.action && (
            <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
              .{record.action}
            </Typography.Text>
          )}
        </Space>
      ),
    },
    {
      title: 'Repository',
      dataIndex: 'repositoryFullName',
      key: 'repositoryFullName',
      width: 180,
      render: (repo: string | null) =>
        repo ? (
          <Typography.Text style={{ fontSize: 12, fontFamily: 'var(--tn-font-mono, monospace)' }}>
            {repo}
          </Typography.Text>
        ) : (
          <Typography.Text type="secondary">—</Typography.Text>
        ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 110,
      render: (status: string) => {
        const color =
          status === 'Processed' ? 'success' : status === 'Failed' ? 'error' : 'default'
        return (
          <Tag color={color} style={{ margin: 0, whiteSpace: 'nowrap' }}>
            {status}
          </Tag>
        )
      },
    },
    {
      title: 'Tóm tắt kết quả',
      key: 'summary',
      render: (_, record) => (
        <Flex vertical gap={2}>
          {record.summary && (
            <Typography.Text style={{ fontSize: 12.5 }}>{record.summary}</Typography.Text>
          )}
          {record.errorMessage && (
            <Typography.Text type="danger" style={{ fontSize: 12 }}>
              {record.errorMessage}
            </Typography.Text>
          )}
        </Flex>
      ),
    },
    {
      title: 'Thời gian nhận',
      dataIndex: 'receivedAt',
      key: 'receivedAt',
      width: 150,
      render: (val: string) => (
        <Typography.Text type="secondary" style={{ fontSize: 11.5 }}>
          {dayjs(val).format('DD/MM/YYYY HH:mm')}
        </Typography.Text>
      ),
    },
  ]

  return (
    <Card
      data-testid="github-integration-section"
      loading={loading}
      style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      title={
        <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
          <Flex align="center" gap={10}>
            <GithubOutlined style={{ fontSize: 20, color: '#0f172a' }} />
            <div>
              <Flex align="center" gap={8}>
                <Typography.Text strong style={{ fontSize: 15 }}>
                  Tích hợp GitHub Webhook (Inbound)
                </Typography.Text>
                {config && (
                  <Tag
                    color={config.isEnabled ? 'success' : 'default'}
                    style={{ margin: 0, whiteSpace: 'nowrap' }}
                  >
                    {config.isEnabled ? 'Đang bật' : 'Đang tắt'}
                  </Tag>
                )}
              </Flex>
              <Typography.Text type="secondary" style={{ fontSize: 12, fontWeight: 400 }}>
                Nhận sự kiện Pull Request (mã TN-123), GitHub Issue (+ Jev Triage) và đồng bộ bình luận một chiều.
              </Typography.Text>
            </div>
          </Flex>
        </Flex>
      }
    >
      <Flex vertical gap={20}>
        {/* One-time Secret Reveal Alert */}
        {oneTimeSecret && (
          <Alert
            type="warning"
            showIcon
            icon={<KeyOutlined />}
            data-testid="github-oneshot-secret-alert"
            closable
            onClose={onDismissSecret}
            title="Lưu Webhook Secret này ngay (chỉ hiển thị duy nhất 1 lần)"
            description={
              <Flex vertical gap={8} style={{ marginTop: 6 }}>
                <Typography.Text style={{ fontSize: 12.5 }}>
                  Dán chuỗi bí mật dưới đây vào mục <strong>Secret</strong> khi cấu hình Webhook trên GitHub Repository. Hệ thống chỉ lưu bản mã hoá và không thể xem lại chuỗi gốc sau khi rời trang.
                </Typography.Text>
                <Flex align="center" gap={8} wrap="wrap">
                  <Input
                    readOnly
                    value={oneTimeSecret}
                    data-testid="github-oneshot-secret-value"
                    style={{
                      flex: 1,
                      minWidth: 260,
                      fontFamily: 'var(--tn-font-mono, monospace)',
                      fontSize: 12.5,
                      backgroundColor: '#ffffff',
                    }}
                  />
                  <Button
                    type="primary"
                    icon={<CopyOutlined />}
                    data-testid="copy-github-secret-btn"
                    onClick={() =>
                      void handleCopyText(oneTimeSecret, 'Đã sao chép GitHub Webhook Secret')
                    }
                  >
                    Sao chép Secret
                  </Button>
                </Flex>
              </Flex>
            }
            style={{ borderRadius: 10 }}
          />
        )}

        {/* Payload URL & Secret Hint (when configured) */}
        {config ? (
          <Card
            size="small"
            style={{ borderRadius: 10, backgroundColor: '#f8fafc', border: '1px solid #e2e8f0' }}
          >
            <Row gutter={[16, 12]} align="middle">
              <Col xs={24} md={15}>
                <Typography.Text strong style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  Webhook Payload URL (dán vào GitHub → Settings → Webhooks, chọn application/json):
                </Typography.Text>
                <Flex gap={8}>
                  <Input
                    readOnly
                    value={config.webhookPayloadUrl}
                    data-testid="github-payload-url-input"
                    style={{
                      fontFamily: 'var(--tn-font-mono, monospace)',
                      fontSize: 12,
                      backgroundColor: '#ffffff',
                    }}
                  />
                  <Button
                    icon={<CopyOutlined />}
                    data-testid="copy-github-payload-url-btn"
                    onClick={() =>
                      void handleCopyText(config.webhookPayloadUrl, 'Đã sao chép Payload URL')
                    }
                  >
                    Sao chép
                  </Button>
                </Flex>
              </Col>
              <Col xs={24} md={9}>
                <Typography.Text strong style={{ fontSize: 12, display: 'block', marginBottom: 4 }}>
                  Webhook Secret hiện tại (đã mã hoá):
                </Typography.Text>
                <Flex align="center" justify="space-between" gap={8}>
                  <Tag
                    style={{
                      margin: 0,
                      fontFamily: 'var(--tn-font-mono, monospace)',
                      fontSize: 12,
                      padding: '4px 10px',
                      whiteSpace: 'nowrap',
                    }}
                  >
                    ****{config.secretHint}
                  </Tag>
                  <Popconfirm
                    title="Xoay vòng Webhook Secret?"
                    description="Secret cũ sẽ mất hiệu lực ngay lập tức và bạn cần cập nhật secret mới lên GitHub."
                    okText="Xoay vòng ngay"
                    cancelText="Huỷ"
                    onConfirm={() => void handleRotate()}
                  >
                    <Button
                      icon={<SyncOutlined />}
                      loading={rotating}
                      data-testid="rotate-github-secret-btn"
                    >
                      Xoay vòng Secret
                    </Button>
                  </Popconfirm>
                </Flex>
              </Col>
            </Row>
          </Card>
        ) : (
          <Alert
            type="info"
            showIcon
            title="Chưa kích hoạt tích hợp GitHub cho không gian làm việc này"
            description="Sau khi nhấn 'Kích hoạt tích hợp GitHub', hệ thống sẽ sinh Payload URL và Webhook Secret (hiển thị 1 lần) để bạn dán vào GitHub Repository."
            style={{ borderRadius: 10 }}
          />
        )}

        {/* Configuration Form */}
        <Form form={form} layout="vertical">
          <Row gutter={[20, 12]}>
            <Col xs={24} md={12}>
              <Form.Item
                name="defaultBoardId"
                label="Bảng mặc định nhận Issue mới (Default Board)"
                tooltip="Khi GitHub gửi sự kiện issues.opened/labeled, thẻ mới sẽ được tạo ở cột đầu tiên của bảng này."
              >
                <Select
                  allowClear
                  placeholder="Chọn bảng mặc định..."
                  data-testid="github-default-board-select"
                  options={boards.map((b) => ({
                    value: b.id,
                    label: b.name,
                  }))}
                />
              </Form.Item>
            </Col>

            <Col xs={24} md={12}>
              <Form.Item
                name="issueLabelFilter"
                label="Bộ lọc nhãn GitHub Issue (tuỳ chọn)"
                tooltip="Chỉ tạo thẻ cho các Issue có ít nhất 1 nhãn khớp danh sách này (không phân biệt hoa thường). Bỏ trống để nhận mọi Issue."
                rules={[
                  { max: 500, message: 'Bộ lọc nhãn tối đa 500 ký tự' },
                  {
                    validator: (_, value: string | undefined) => {
                      if (!value || !value.trim()) return Promise.resolve()
                      const labels = value
                        .split(/[,;]/)
                        .map((s) => s.trim())
                        .filter(Boolean)
                      if (labels.length > 20) {
                        return Promise.reject(new Error('Bộ lọc nhãn tối đa 20 nhãn'))
                      }
                      return Promise.resolve()
                    },
                  },
                ]}
              >
                <Input
                  placeholder="Ví dụ: teamnexus, bug, urgent (để trống = nhận tất cả)"
                  maxLength={500}
                  data-testid="github-label-filter-input"
                />
              </Form.Item>
            </Col>
          </Row>

          <Card
            size="small"
            style={{ borderRadius: 10, border: '1px solid #e2e8f0', marginBottom: 16 }}
          >
            <Flex vertical gap={12}>
              <Flex justify="space-between" align="center" gap={12}>
                <div>
                  <Typography.Text strong style={{ display: 'block', fontSize: 13 }}>
                    Bật tích hợp GitHub Webhook
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    Cho phép endpoint nhận và xác thực chữ ký HMAC-SHA256 từ GitHub.
                  </Typography.Text>
                </div>
                <Form.Item name="isEnabled" valuePropName="checked" noStyle>
                  <Switch data-testid="github-enabled-switch" />
                </Form.Item>
              </Flex>

              <Divider style={{ margin: '2px 0' }} />

              <Flex justify="space-between" align="center" gap={12}>
                <div>
                  <Typography.Text strong style={{ display: 'block', fontSize: 13 }}>
                    Tự động chuyển cột khi mở / merge Pull Request
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    Khi tiêu đề, nhánh hoặc mô tả PR chứa mã TN-123: chuyển sang Đang duyệt PR khi mở và Hoàn thành khi merge (hoàn tác được trong Lịch sử AI).
                  </Typography.Text>
                </div>
                <Form.Item name="autoMoveOnPr" valuePropName="checked" noStyle>
                  <Switch data-testid="github-auto-move-pr-switch" />
                </Form.Item>
              </Flex>

              <Divider style={{ margin: '2px 0' }} />

              <Flex justify="space-between" align="center" gap={12}>
                <div>
                  <Typography.Text strong style={{ display: 'block', fontSize: 13 }}>
                    Tự động tạo thẻ & Jev Triage từ GitHub Issue
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    Tạo thẻ mới trên Default Board khi có Issue mở/gắn nhãn khớp bộ lọc và phân loại tự động bằng Jev 1.13.
                  </Typography.Text>
                </div>
                <Form.Item name="autoCreateTasksFromIssues" valuePropName="checked" noStyle>
                  <Switch data-testid="github-auto-create-issue-switch" />
                </Form.Item>
              </Flex>

              <Divider style={{ margin: '2px 0' }} />

              <Flex justify="space-between" align="center" gap={12}>
                <div>
                  <Typography.Text strong style={{ display: 'block', fontSize: 13 }}>
                    Đồng bộ bình luận một chiều từ GitHub
                  </Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                    Chuyển bình luận trên Issue/PR liên kết (hoặc nhắc mã TN-123) về thẻ dưới tài khoản GitHub Bot.
                  </Typography.Text>
                </div>
                <Form.Item name="syncIssueComments" valuePropName="checked" noStyle>
                  <Switch data-testid="github-sync-comments-switch" />
                </Form.Item>
              </Flex>
            </Flex>
          </Card>

          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            {config ? (
              <Popconfirm
                title="Xoá cấu hình tích hợp GitHub?"
                description="Webhook URL hiện tại sẽ ngừng hoạt động (trả 404) và nhật ký delivery của cấu hình này sẽ bị xoá."
                okText="Xoá cấu hình"
                cancelText="Huỷ"
                okButtonProps={{ danger: true }}
                onConfirm={() => void handleDelete()}
              >
                <Button
                  danger
                  icon={<DeleteOutlined />}
                  loading={deleting}
                  data-testid="delete-github-config-btn"
                >
                  Xoá cấu hình GitHub
                </Button>
              </Popconfirm>
            ) : (
              <span />
            )}

            <Button
              type="primary"
              icon={<SaveOutlined />}
              loading={saving}
              onClick={() => void handleSave()}
              data-testid={config ? 'save-github-config-btn' : 'enable-github-btn'}
              style={{ backgroundColor: '#4f46e5', borderColor: '#4f46e5', borderRadius: 8 }}
            >
              {config ? 'Lưu cấu hình GitHub' : 'Kích hoạt tích hợp GitHub'}
            </Button>
          </Flex>
        </Form>

        {/* Delivery Ledger Table */}
        {config && (
          <>
            <Divider style={{ margin: '4px 0' }} />
            <Flex vertical gap={10}>
              <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
                <Typography.Text strong style={{ fontSize: 14 }}>
                  Nhật ký sự kiện GitHub Webhook (30 gần nhất)
                </Typography.Text>
                <Button
                  size="small"
                  icon={<ReloadOutlined />}
                  loading={refreshingDeliveries}
                  onClick={() => void handleRefreshDeliveries()}
                  data-testid="refresh-github-deliveries-btn"
                >
                  Làm mới nhật ký
                </Button>
              </Flex>

              <Table<GitHubWebhookDeliveryResponse>
                rowKey="id"
                size="small"
                columns={deliveryColumns}
                dataSource={deliveries}
                pagination={false}
                locale={{
                  emptyText: (
                    <Empty
                      image={Empty.PRESENTED_IMAGE_SIMPLE}
                      description="Chưa ghi nhận sự kiện GitHub Webhook nào. Hãy gửi Ping từ trang cấu hình GitHub."
                    />
                  ),
                }}
              />
            </Flex>
          </>
        )}
      </Flex>
    </Card>
  )
}
