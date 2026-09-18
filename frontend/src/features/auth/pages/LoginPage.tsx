import React, { useEffect } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Alert, Button, Card, Flex, Typography } from 'antd'
import { useAuth } from '../hooks/useAuth'

const GoogleIcon: React.FC = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" style={{ marginRight: 8 }}>
    <path
      fill="#4285F4"
      d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17z"
    />
    <path
      fill="#34A853"
      d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.1-6.72-4.93H1.29v3.15C3.26 21.3 7.35 24 12 24z"
    />
    <path
      fill="#FBBC05"
      d="M5.28 14.27c-.25-.72-.38-1.49-.38-2.27s.13-1.55.38-2.27V6.58H1.29C.47 8.21 0 10.05 0 12s.47 3.79 1.29 5.42l3.99-3.15z"
    />
    <path
      fill="#EA4335"
      d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.35 0 3.26 2.7 1.29 6.58l3.99 3.15c.95-2.83 3.6-4.98 6.72-4.98z"
    />
  </svg>
)

const GitHubIcon: React.FC = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style={{ marginRight: 8 }}>
    <path
      fillRule="evenodd"
      clipRule="evenodd"
      d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.53 1.032 1.53 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z"
    />
  </svg>
)

export const LoginPage: React.FC = () => {
  const { isAuthenticated, loginWithProvider } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()

  useEffect(() => {
    if (isAuthenticated) {
      navigate('/', { replace: true })
    }
  }, [isAuthenticated, navigate])

  const isAuthError = searchParams.get('auth') === 'error'

  return (
    <Flex
      align="center"
      justify="center"
      style={{
        minHeight: '100vh',
        background: 'linear-gradient(135deg, #f8fafc 0%, #e0e7ff 50%, #fdf4ff 100%)',
        padding: 24,
      }}
    >
      <Card
        style={{
          width: '100%',
          maxWidth: 420,
          borderRadius: 16,
          boxShadow: '0 20px 40px rgba(15, 23, 42, 0.08)',
          background: '#ffffff',
          border: '1px solid #e2e8f0',
        }}
      >
        <div style={{ textAlign: 'left', marginBottom: 12 }}>
          <Button
            type="link"
            size="small"
            onClick={() => navigate('/')}
            style={{ padding: 0, color: '#6366f1', fontSize: 13 }}
          >
            ← Quay lại trang chủ
          </Button>
        </div>

        <Flex vertical align="center" gap="small" style={{ marginBottom: 24 }}>
          <div
            style={{
              width: 44,
              height: 44,
              borderRadius: 12,
              background: 'linear-gradient(135deg, #6366f1 0%, #a855f7 100%)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              boxShadow: '0 4px 14px rgba(99, 102, 241, 0.4)',
              marginBottom: 4,
            }}
          >
            <span style={{ fontSize: 22, color: '#fff', fontWeight: 'bold' }}>✦</span>
          </div>
          <Typography.Title level={2} style={{ margin: 0, color: '#0f172a' }}>
            TeamNexus
          </Typography.Title>
          <Typography.Text type="secondary" style={{ textAlign: 'center' }}>
            Trợ lý điều phối không gian làm việc thông minh
          </Typography.Text>
        </Flex>

        {isAuthError && (
          <Alert
            message="Lỗi đăng nhập"
            description="Đăng nhập không thành công hoặc bị hủy. Vui lòng thử lại."
            type="error"
            showIcon
            style={{ marginBottom: 20 }}
          />
        )}

        <Flex vertical gap="middle">
          <Button
            size="large"
            block
            icon={<GitHubIcon />}
            onClick={() => loginWithProvider('github')}
            style={{
              height: 48,
              borderRadius: 8,
              fontWeight: 500,
              backgroundColor: '#24292e',
              color: '#fff',
              border: 'none',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            Đăng nhập với GitHub
          </Button>

          <Button
            size="large"
            block
            icon={<GoogleIcon />}
            onClick={() => loginWithProvider('google')}
            style={{
              height: 48,
              borderRadius: 8,
              fontWeight: 500,
              backgroundColor: '#fff',
              color: '#3c4043',
              borderColor: '#dadce0',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            Đăng nhập với Google
          </Button>
        </Flex>

        <Typography.Paragraph
          type="secondary"
          style={{ fontSize: 12, textAlign: 'center', marginTop: 24, marginBottom: 0 }}
        >
          Bằng cách đăng nhập, bạn đồng ý với Điều khoản dịch vụ và Chính sách bảo mật của TeamNexus.
        </Typography.Paragraph>
      </Card>
    </Flex>
  )
}
