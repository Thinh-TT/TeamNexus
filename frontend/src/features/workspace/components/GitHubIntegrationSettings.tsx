import React, { useEffect, useState } from 'react'
import {
  ApiOutlined,
  CheckCircleOutlined,
  CopyOutlined,
  DisconnectOutlined,
  GithubOutlined,
  KeyOutlined,
  LinkOutlined,
  QuestionCircleOutlined,
  SaveOutlined,
  SafetyCertificateOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Button,
  Card,
  Divider,
  Flex,
  Form,
  Input,
  Popconfirm,
  Space,
  Spin,
  Switch,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import {
  type GitHubIntegrationResponse,
  type TestGitHubConnectionResponse,
  type UpdateGitHubIntegrationRequest,
  workspaceIntegrationsApi,
} from '../services/workspaceIntegrationsApi'

export interface GitHubIntegrationSettingsProps {
  workspaceId: string
  isManagerOrAdmin?: boolean
}

export const GitHubIntegrationSettings: React.FC<GitHubIntegrationSettingsProps> = ({
  workspaceId,
  isManagerOrAdmin = true,
}) => {
  const [form] = Form.useForm()
  const [loading, setLoading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [testing, setTesting] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [integration, setIntegration] = useState<GitHubIntegrationResponse | null>(null)
  const [testResult, setTestResult] = useState<TestGitHubConnectionResponse | null>(null)

  const resolveWebhookUrl = (url?: string | null) => {
    const origin =
      typeof window !== 'undefined'
        ? window.location.origin
        : 'https://api.teamnexus.cloud'
    if (!url) return `${origin}/api/webhooks/github`
    if (url.startsWith('http://') || url.startsWith('https://')) return url
    return `${origin}${url.startsWith('/') ? '' : '/'}${url}`
  }

  const currentWebhookUrl = resolveWebhookUrl(integration?.webhookUrl)

  useEffect(() => {
    let ignore = false
    if (!workspaceId) return

    // eslint-disable-next-line @typescript-eslint/no-floating-promises
    void (async () => {
      setLoading(true)
      try {
        const data = await workspaceIntegrationsApi.getGitHubIntegration(workspaceId)
        if (ignore) return
        setIntegration(data)
        form.setFieldsValue({
          isEnabled: data.isEnabled ?? true,
          repoName: data.repoName ?? '',
          personalAccessToken: '',
          webhookSecret: '',
        })
      } catch {
        if (ignore) return
        setIntegration(null)
        form.setFieldsValue({
          isEnabled: false,
          repoName: '',
          personalAccessToken: '',
          webhookSecret: '',
        })
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }
    })()

    return () => {
      ignore = true
    }
  }, [workspaceId, form])

  const handleCopyWebhookUrl = async () => {
    try {
      if (typeof navigator !== 'undefined' && navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(currentWebhookUrl)
      } else if (typeof document !== 'undefined') {
        const textarea = document.createElement('textarea')
        textarea.value = currentWebhookUrl
        document.body.appendChild(textarea)
        textarea.select()
        document.execCommand('copy')
        document.body.removeChild(textarea)
      }
      message.success('Đã sao chép Webhook URL')
    } catch {
      message.error('Không thể sao chép liên kết vào clipboard')
    }
  }

  const handleFinish = async (values: {
    isEnabled: boolean
    repoName?: string
    personalAccessToken?: string
    webhookSecret?: string
  }) => {
    if (!isManagerOrAdmin) return
    setSaving(true)
    setTestResult(null)
    try {
      const payload: UpdateGitHubIntegrationRequest = {
        isEnabled: Boolean(values.isEnabled),
        repoName: values.repoName?.trim() || null,
        personalAccessToken: values.personalAccessToken?.trim() || null,
        webhookSecret: values.webhookSecret?.trim() || null,
      }
      const updated = await workspaceIntegrationsApi.updateGitHubIntegration(workspaceId, payload)
      setIntegration(updated)
      form.setFieldsValue({
        personalAccessToken: '',
        webhookSecret: '',
      })
      message.success('Đã lưu cấu hình tích hợp GitHub thành công')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { message?: string; error?: string } } })?.response?.data
          ?.message ||
        (err as { response?: { data?: { message?: string; error?: string } } })?.response?.data
          ?.error ||
        'Không thể lưu cấu hình tích hợp GitHub'
      message.error(errorMsg)
    } finally {
      setSaving(false)
    }
  }

  const handleTestConnection = async () => {
    setTesting(true)
    setTestResult(null)
    try {
      const res = await workspaceIntegrationsApi.testGitHubConnection(workspaceId)
      setTestResult(res)
      if (res.success) {
        message.success('Kiểm tra kết nối GitHub thành công')
      } else {
        message.error(res.errorMessage || 'Kết nối GitHub thất bại')
      }
    } catch (err: unknown) {
      const msg =
        (err as { response?: { data?: { message?: string; error?: string } } })?.response?.data
          ?.message ||
        (err as { response?: { data?: { message?: string; error?: string } } })?.response?.data
          ?.error ||
        'Không thể kiểm tra kết nối tới GitHub'
      setTestResult({
        success: false,
        errorMessage: msg,
      })
      message.error(msg)
    } finally {
      setTesting(false)
    }
  }

  const handleDelete = async () => {
    if (!isManagerOrAdmin) return
    setDeleting(true)
    setTestResult(null)
    try {
      await workspaceIntegrationsApi.deleteGitHubIntegration(workspaceId)
      setIntegration(null)
      form.resetFields()
      form.setFieldsValue({
        isEnabled: false,
        repoName: '',
        personalAccessToken: '',
        webhookSecret: '',
      })
      message.success('Đã ngắt kết nối GitHub thành công')
    } catch {
      message.error('Không thể ngắt kết nối GitHub')
    } finally {
      setDeleting(false)
    }
  }

  if (loading) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: 240 }}>
        <Spin size="large" />
      </Flex>
    )
  }

  const hasExistingIntegration = Boolean(
    integration && (integration.repoName || integration.hasPat || integration.hasWebhookSecret)
  )

  return (
    <Card
      style={{
        borderRadius: 12,
        border: '1px solid #e2e8f0',
        backgroundColor: '#ffffff',
      }}
      styles={{ body: { padding: 24 } }}
    >
      <Flex vertical gap={20}>
        {/* Header section */}
        <Flex justify="space-between" align="flex-start" wrap="wrap" gap={12}>
          <div>
            <Flex align="center" gap={10}>
              <GithubOutlined style={{ fontSize: 24, color: '#24292f' }} />
              <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
                Tích hợp GitHub Repository
              </Typography.Title>
            </Flex>
            <Typography.Paragraph
              type="secondary"
              style={{ margin: '6px 0 0 0', fontSize: 13, maxWidth: 650 }}
            >
              Liên kết kho lưu trữ GitHub để tự động chuyển thẻ Kanban sang trạng thái Hoàn thành khi
              Pull Request được Merged và theo dõi tiến độ mã nguồn trực tiếp trên thẻ công việc.
            </Typography.Paragraph>
          </div>

          {integration?.isEnabled ? (
            <Tag color="success" style={{ padding: '4px 10px', borderRadius: 999, fontWeight: 600 }}>
              ● Đang kích hoạt
            </Tag>
          ) : (
            <Tag color="default" style={{ padding: '4px 10px', borderRadius: 999, fontWeight: 500 }}>
              ○ Chưa kích hoạt
            </Tag>
          )}
        </Flex>

        <Divider style={{ margin: '4px 0' }} />

        {/* Test Result Alert */}
        {testResult && (
          <Alert
            data-testid="github-test-alert"
            type={testResult.success ? 'success' : 'error'}
            showIcon
            title={
              testResult.success ? 'Kết nối GitHub thành công' : 'Kết nối GitHub không thành công'
            }
            description={
              testResult.success
                ? `Đã xác thực repository "${testResult.repoFullName || integration?.repoName}". Nhánh mặc định: ${testResult.defaultBranch || 'main'}.`
                : testResult.errorMessage || 'Vui lòng kiểm tra lại Personal Access Token và quyền repo.'
            }
            closable
            onClose={() => setTestResult(null)}
          />
        )}

        <Form
          form={form}
          layout="vertical"
          onFinish={handleFinish}
          disabled={!isManagerOrAdmin}
          initialValues={{
            isEnabled: integration?.isEnabled ?? true,
            repoName: integration?.repoName ?? '',
          }}
        >
          {/* Switch enable */}
          <Flex align="center" gap={12} style={{ marginBottom: 16 }}>
            <Form.Item
              name="isEnabled"
              valuePropName="checked"
              noStyle
            >
              <Switch data-testid="github-enable-switch" />
            </Form.Item>
            <Typography.Text strong>Kích hoạt tích hợp GitHub cho không gian làm việc này</Typography.Text>
          </Flex>

          {/* Repo Name */}
          <Form.Item
            name="repoName"
            label={
              <Space>
                <span>Tên Repository</span>
                <Tooltip title="Tên kho chứa theo định dạng owner/repo (ví dụ: facebook/react hoặc Thinh-TT/TeamNexus)">
                  <QuestionCircleOutlined style={{ color: '#94a3b8' }} />
                </Tooltip>
              </Space>
            }
            rules={[
              {
                pattern: /^[^/\s]+\/[^/\s]+$/,
                message: 'Định dạng repository phải là owner/repo (vd: Thinh-TT/TeamNexus)',
              },
            ]}
          >
            <Input
              data-testid="github-repo-input"
              prefix={<GithubOutlined style={{ color: '#94a3b8' }} />}
              placeholder="owner/repo (vd: Thinh-TT/TeamNexus)"
              maxLength={150}
            />
          </Form.Item>

          {/* Personal Access Token (PAT) */}
          <Form.Item
            name="personalAccessToken"
            label={
              <Flex justify="space-between" align="center" style={{ width: '100%' }}>
                <Space>
                  <span>Personal Access Token (PAT)</span>
                  <Tooltip title="Token dùng để gọi GitHub API lấy thông tin Pull Request và kiểm tra trạng thái liên kết.">
                    <QuestionCircleOutlined style={{ color: '#94a3b8' }} />
                  </Tooltip>
                </Space>
                {integration?.hasPat && (
                  <Tag color="cyan" style={{ borderRadius: 6, fontWeight: 600 }}>
                    <SafetyCertificateOutlined style={{ marginRight: 4 }} />
                    Đã cấu hình ({integration.maskedPat || '••••••••'})
                  </Tag>
                )}
              </Flex>
            }
            extra={
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                Token cần tối thiểu quyền <code>repo:read</code> hoặc <code>public_repo</code>.{' '}
                <a
                  href="https://github.com/settings/tokens"
                  target="_blank"
                  rel="noopener noreferrer"
                  style={{ color: '#4f46e5' }}
                >
                  <LinkOutlined /> Mở trang tạo GitHub Token
                </a>
              </Typography.Text>
            }
          >
            <Input.Password
              data-testid="github-pat-input"
              prefix={<KeyOutlined style={{ color: '#94a3b8' }} />}
              placeholder={
                integration?.hasPat
                  ? 'Nhập PAT mới để cập nhật (quyền repo:read) - bỏ trống nếu giữ nguyên'
                  : 'ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx'
              }
              autoComplete="new-password"
            />
          </Form.Item>

          {/* Webhook Secret */}
          <Form.Item
            name="webhookSecret"
            label={
              <Flex justify="space-between" align="center" style={{ width: '100%' }}>
                <Space>
                  <span>Webhook Secret</span>
                  <Tooltip title="Chuỗi bí mật dùng để ký HMAC-SHA256 nhằm đảm bảo sự kiện gửi tới TeamNexus thật sự đến từ GitHub.">
                    <QuestionCircleOutlined style={{ color: '#94a3b8' }} />
                  </Tooltip>
                </Space>
                {integration?.hasWebhookSecret && (
                  <Tag color="green" style={{ borderRadius: 6, fontWeight: 600 }}>
                    <CheckCircleOutlined style={{ marginRight: 4 }} />
                    Đã cấu hình
                  </Tag>
                )}
              </Flex>
            }
            extra={
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                Khóa bí mật ký payload webhook HMAC-SHA256. Khớp với mục Secret trong Webhook Settings của GitHub.
              </Typography.Text>
            }
          >
            <Input.Password
              data-testid="github-secret-input"
              prefix={<KeyOutlined style={{ color: '#94a3b8' }} />}
              placeholder={
                integration?.hasWebhookSecret
                  ? 'Nhập secret mới để ký payload - bỏ trống nếu giữ nguyên'
                  : 'Nhập secret mới để ký payload HMAC-SHA256'
              }
              autoComplete="new-password"
            />
          </Form.Item>

          {/* Webhook Payload URL Box */}
          <div
            style={{
              padding: '14px 16px',
              backgroundColor: '#f8fafc',
              border: '1px solid #e2e8f0',
              borderRadius: 8,
              marginBottom: 24,
            }}
          >
            <Flex justify="space-between" align="center" style={{ marginBottom: 6 }}>
              <Typography.Text strong style={{ fontSize: 13, color: '#334155' }}>
                Webhook Payload URL
              </Typography.Text>
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>
                Content type: application/json | Event: Pull requests
              </Typography.Text>
            </Flex>

            <Flex gap={8} align="center">
              <Input
                data-testid="github-webhook-url"
                readOnly
                value={currentWebhookUrl}
                style={{ backgroundColor: '#ffffff', fontFamily: 'monospace', fontSize: 12 }}
              />
              <Button
                data-testid="github-copy-url-btn"
                icon={<CopyOutlined />}
                onClick={handleCopyWebhookUrl}
              >
                Sao chép liên kết
              </Button>
            </Flex>
            <Typography.Text type="secondary" style={{ fontSize: 12, marginTop: 6, display: 'block' }}>
              💡 Địa chỉ để GitHub gửi tín hiệu Webhook về TeamNexus khi PR được Merge. Khi triển khai lên server thật, dán URL trên vào mục <b>Settings → Webhooks</b> của GitHub Repository.
            </Typography.Text>
          </div>

          {/* Form Actions */}
          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            <div>
              {hasExistingIntegration && (
                <Popconfirm
                  title="Xác nhận ngắt kết nối GitHub?"
                  description="Toàn bộ cấu hình PAT, secret và liên kết repo cho workspace này sẽ bị xoá."
                  onConfirm={handleDelete}
                  okText="Ngắt kết nối"
                  cancelText="Huỷ"
                  okButtonProps={{ danger: true, 'data-testid': 'github-disconnect-confirm-btn' }}
                >
                  <Button
                    data-testid="github-disconnect-btn"
                    danger
                    icon={<DisconnectOutlined />}
                    loading={deleting}
                    disabled={!isManagerOrAdmin}
                  >
                    Ngắt kết nối
                  </Button>
                </Popconfirm>
              )}
            </div>

            <Flex gap={12}>
              <Button
                data-testid="github-test-btn"
                icon={<ApiOutlined />}
                onClick={handleTestConnection}
                loading={testing}
                disabled={!isManagerOrAdmin}
              >
                Kiểm tra kết nối
              </Button>

              <Button
                data-testid="github-save-btn"
                type="primary"
                htmlType="submit"
                icon={<SaveOutlined />}
                loading={saving}
                disabled={!isManagerOrAdmin}
                style={{ backgroundColor: '#4f46e5', borderColor: '#4f46e5', fontWeight: 600 }}
              >
                Lưu cấu hình
              </Button>
            </Flex>
          </Flex>
        </Form>
      </Flex>
    </Card>
  )
}

export default GitHubIntegrationSettings
